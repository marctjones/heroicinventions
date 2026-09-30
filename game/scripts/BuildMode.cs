using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// The in-game build-mode editor: point, click and drag to lay out a
/// machine in 3D. Everything that decides what a design means (which parts
/// exist, whether two ports can join, what the saved file looks like,
/// undo/redo) lives in HeroicInventions.Sim.Editor.BuildSession, tested
/// headlessly (tests/HeroicInventions.Sim.Tests/BuildSessionTests.cs).
///
/// This class is a skin over that session with *no separate editing path*:
/// every mouse action builds the same command string the console accepts
/// and runs it through <see cref="BuildSession.Execute"/>, so a build
/// session is also a readable, replayable log of the design.
///
/// The machine is drawn by the game's own <see cref="MachineView"/>, frozen
/// and out of physics, so what you build is exactly what will run: a tank
/// stands on its base, a ramp tilts, a gear is the real gear. Its
/// <see cref="MachineView.PartNodes"/> give each part's nodes for picking,
/// highlighting and dragging.
///
/// Controls follow common 3D editors:
///   camera   right-drag orbit · middle-drag or Shift+right-drag pan ·
///            scroll zoom · F frame the selection (or everything)
///   select   click a part (hover shows what a click would pick) ·
///            click empty ground or Esc to deselect
///   place    pick a palette entry; a translucent ghost follows the mouse
///            and lands on whatever surface is under it (the ground or
///            another part's top); click to place; Shift keeps placing;
///            Esc or right-click cancels
///   move     drag a part: it slides over surfaces, stacking on what it
///            crosses; hold Ctrl to raise or lower it instead; positions
///            snap to a 5 cm grid (G toggles); ports snap on release
///   join     the Join-parts buttons: a rope, a gear mesh, an axle (arbor),
///            shared air, or a piston's cylinder. Click the parts in turn (Enter
///            ends an axle or shared air); each is one command, like every edit
///   edit     the inspector shows the selected part's numbers and material;
///            Delete removes; Ctrl/Cmd+D duplicates; Ctrl/Cmd+Z undoes;
///            Ctrl/Cmd+Shift+Z redoes
/// </summary>
public partial class BuildMode : Node3D
{
    public const float SnapRadius = 0.2f; // m, for ports
    private const float GridStep = 0.05f; // m
    private const float DragThreshold = 4f; // px before a press on a part becomes a drag

    private readonly MaterialLibrary _materials;
    private readonly List<(string Id, string Label, string? PrimitiveKind, CatalogueEntry? Catalogue)> _palette = [];
    private BuildSession _session = null!;

    // the machine as it will run, frozen
    private MachineView? _preview;
    private readonly Dictionary<string, List<Node3D>> _fallbackVisuals = [];   // parts that can't be built yet
    private readonly Dictionary<string, string> _unfinished = [];              // those parts, and why

    /// <summary>An engine error made readable: "mirror mirror_3 heats ?, which is neither..." reads as what the part still needs.</summary>
    private static string Plain(string message) =>
        message.Replace(" ?,", " nothing yet,").Replace(" ? ", " nothing yet ").Replace("(?)", "(nothing yet)");
    private readonly List<MeshInstance3D> _pipeVisuals = [];
    private readonly List<MeshInstance3D> _portMarkers = [];

    // every part's connection points, drawn and clickable: click one, then another, to join them
    private readonly List<(string Part, string Port, string Kind, Vector3 At, MeshInstance3D Node)> _ports = [];
    private (string Part, string Port, Vector3 At)? _connectFrom;
    private MeshInstance3D? _connectLine;

    // camera: spherical coordinates around a pivot
    private Camera3D _camera = null!;
    private Vector3 _pivot = new(0, 0.4f, 0);
    private float _distance = 2.5f, _yaw = 0.5f, _pitch = 0.55f;
    private bool _orbiting, _panning;

    // selection and tools
    private string? _selectedId, _hoverId;
    private string? _placingPaletteId;
    private Node3D? _ghost;
    private float _ghostBottom;                  // the ghost part's lowest point, relative to its #:at
    private LinkGestures.Kind? _link;            // a Join-parts tool is active
    private readonly List<string> _linkPicks = [];
    private static readonly Color PickColor = new(0.3f, 0.8f, 1f, 0.4f);
    private string? _pressedId;                  // left button down on this part
    private Vector2 _pressPos;
    private bool _dragging;
    private Vector3 _dragStartAt, _dragGrab;     // part's #:at when the drag began; where on the part it was grabbed
    private float _dragBottom;
    private bool _gridSnap = true;
    private string _material = "bronze";
    private int _serial = 1;

    // UI
    private RichTextLabel _console = null!;
    private LineEdit _consoleInput = null!;
    private ItemList _paletteList = null!;
    private OptionButton _materialBox = null!;
    private List<string> _materialIds = [];
    private VBoxContainer _inspector = null!;
    private Label _status = null!;
    private Label _partHelp = null!;
    private FileDialog _saveDialog = null!, _loadDialog = null!;

    private static readonly StandardMaterial3D SelectedOverlay = Overlay(new Color(1f, 0.62f, 0.1f, 0.35f));
    private static readonly StandardMaterial3D PickedOverlay = Overlay(PickColor);
    private static readonly StandardMaterial3D HoverOverlay = Overlay(new Color(1f, 1f, 1f, 0.18f));

    /// <summary>
    /// Each part in plain words: what a person would call it, which group it
    /// sits in, and one line on what it does and what it needs. The engine's
    /// own names (boiler, hearth, jetwheel) follow in brackets.
    /// </summary>
    private static readonly Dictionary<string, (string Label, string Group, string Description)> PartInfo = new()
    {
        ["hearth"] = ("Fire (hearth)", "Fire, water and steam", "A wood fire. Pick the pot it heats in the inspector."),
        ["boiler"] = ("Pot with a lid (boiler)", "Fire, water and steam", "A litre of water with a steam spout on its lid. Heat it with a fire or mirrors."),
        ["jetwheel"] = ("Paddle wheel for a steam jet", "Fire, water and steam", "Branca's wheel: click the pot's steam point, then the wheel's, to aim the spout at its paddles."),
        ["smokejack"] = ("Hot-air wheel (smoke jack)", "Fire, water and steam", "Vanes in a chimney turned by a fire's rising hot air. Pick the fire it sits over in the inspector."),
        ["rotor"] = ("Steam ball (aeolipile)", "Fire, water and steam", "Heron's ball, spun by its own steam jets. Connect a pot's steam to it."),
        ["mirror"] = ("Mirror", "Fire, water and steam", "Throws sunlight onto a pot. Pick the pot in the inspector."),
        ["door"] = ("Door or valve", "Structure", "An opening between two enclosures (or one and outside). Gas rushes through when it is open."),
        ["air-pump"] = ("Air pump", "Structure", "Pumps gas out of one zone into another, as an airlock's chamber is pumped down."),
        ["stirling"] = ("Hot-air engine (Stirling)", "Wheels and power", "Driven by heat, not the air: aim mirrors or a fire at its receiver. Works on Mars."),
        ["crucible"] = ("Crucible of sand", "Fire, water and steam", "Sand at a focal spot. Mirrors melt it to glass, if they concentrate enough light: flat ones can't."),
        ["burning-mirror"] = ("Burning mirror", "Fire, water and steam", "A curved mirror (or lens) gathering the sun into a small spot. Pick its target in the inspector."),
        ["pane"] = ("Glass panes", "Structure", "Glass in an enclosure's wall or roof: the only way light gets in. Too thin for the pressure, it cracks."),
        ["pond"] = ("Warm pond", "Water", "Gives a tank's water a temperature: warmed, it evaporates into the air over it."),
        ["roof"] = ("Cold roof", "Structure", "An enclosure's roof chilled by the outside: its air's vapour condenses on it and rains into a gutter."),
        ["enclosure"] = ("Enclosure", "Structure", "A room with its own air: set its pressure, gases (o2, n2 …) and walls. Parts inside read its air instead of the planet's."),
        ["hopper"] = ("Sand hopper", "Water", "Grain draining through a hole at a steady rate, however deep: a timer that works where water would freeze."),
        ["ratchet"] = ("Ratchet and pawl", "Wheels and power", "Lets a wheel turn one way only, a tooth at a time, and holds what tries to turn it back. Pick the wheel in the inspector."),
        ["cam"] = ("Peg wheel (cam)", "Wheels and power", "Pegs on a wheel lift a hammer as it turns, then let it fall. Pick the wheel in the inspector."),
        ["grip"] = ("Tongs or hook (grip)", "Weights and levers", "Takes hold of a loose block within reach while closed, and lets go when opened or overloaded. Hang it on a lever or the world."),
        ["bellows"] = ("Bellows", "Fire, water and steam", "Blows air into a fire so it burns hotter."),
        ["safety-valve"] = ("Safety valve", "Fire, water and steam", "Lets steam out of a pot before it bursts."),
        ["tank"] = ("Tank of water", "Water", "Holds water. Join tanks with pipes at their water points."),
        ["pump"] = ("Pump", "Water", "Lifts water from one tank to another."),
        ["sluice"] = ("Sluice gate", "Water", "A gate that lets water out of a pool into a channel."),
        ["float-valve"] = ("Float valve", "Water", "Shuts off the water when a tank is full."),
        ["leak"] = ("Hole (leak)", "Water", "A hole in a tank's side that water runs out of."),
        ["waterwheel"] = ("Water wheel", "Wheels and power", "Turned by water falling on it or flowing under it."),
        ["windmill"] = ("Windmill", "Wheels and power", "Turned by the wind."),
        ["capstan"] = ("Capstan", "Wheels and power", "A post that a rope is wound round to hold a load."),
        ["piston"] = ("Piston", "Wheels and power", "Slides up and down in a cylinder."),
        ["lever"] = ("Lever or see-saw", "Weights and levers", "A beam on a pivot. Put weights on its ends."),
        ["block"] = ("Block", "Weights and levers", "A cube of material: a weight, a load, something to slide or drop."),
        ["ramp"] = ("Ramp", "Weights and levers", "A slope for blocks to slide or rest on."),
        ["pendulum"] = ("Pendulum", "Weights and levers", "A weight on a rod that swings."),
        ["counterpoise"] = ("Door with a counterweight", "Weights and levers", "A door that opens when a bucket outweighs its counterweight."),
        ["post"] = ("Stone post or plinth", "Structure", "Something solid to stand things on."),
        ["sluice-box"] = ("Sluice box", "Water", "A riffled box in a channel: fed crushed ore, it keeps the grains too dense for the flow to lift and washes the rest on."),
        ["float"] = ("Float", "Water", "A flat float riding a tank's water, as deep as its weight needs (Archimedes); it grounds when the water is shallower."),
        ["digger"] = ("Digging gang", "Structure", "Labourers cutting a trench into the ground a machine stands on; a wall too tall for its soil falls in."),
    };
    private static readonly string[] GroupOrder = ["Fire, water and steam", "Water", "Wheels and power", "Weights and levers", "Structure"];

    public event Action? RunRequested;
    public event Action? ExitRequested;

    public BuildMode(MaterialLibrary materials) => _materials = materials;

    // Live editing (issue #75): editing a machine that is running in a world.
    // Each change rebuilds the real machine in place, state carried over,
    // instead of a frozen preview; the world keeps running meanwhile.
    private readonly MachineDef? _liveStart;
    private readonly Func<MachineDef, MachineView>? _applyLive;
    private readonly Func<MachineView>? _liveView;
    private string? _lastApplied;
    private bool Live => _applyLive is not null;

    public BuildMode(MaterialLibrary materials, MachineDef running, Func<MachineDef, MachineView> applyLive, Func<MachineView> liveView)
        : this(materials)
    {
        _liveStart = running;
        _applyLive = applyLive;
        _liveView = liveView;
    }
    public BuildMode() : this(MaterialLibrary.LoadDefault()) { }

    public override void _Ready()
    {
        string catalogueRes = "res://meshes/catalogue/catalogue.rktd";
        var catalogue = Godot.FileAccess.FileExists(catalogueRes)
            ? CatalogueReader.Parse(Godot.FileAccess.GetFileAsString(catalogueRes))
            : [];
        _session = new BuildSession(_materials, catalogue, ProjectSettings.GlobalizePath("user://machines"));
        if (_liveStart is not null)
        {
            _session.Open(_liveStart);
            // written the way Redraw writes it, so merely opening doesn't count as a change
            _lastApplied = MachineWriter.Write(EditorDocument.Load(_session.Document.ToMachineDef()).ToMachineDef());
        }

        foreach (string kind in PartTemplates.PrimitiveKinds)
            _palette.Add((kind, PartInfo.GetValueOrDefault(kind).Label ?? kind, kind, null));
        foreach (var e in catalogue) _palette.Add((e.Id, e.Description, null, e));

        BuildUi();
        BuildGrid();
        _camera = new Camera3D { Fov = 50 };
        AddChild(_camera);
        _camera.MakeCurrent();
        UpdateCamera();

        string autoLoad = OS.GetEnvironment("HEROIC_EDITOR_LOAD");
        if (!string.IsNullOrEmpty(autoLoad))
        {
            string absolute = autoLoad.StartsWith("res://") || autoLoad.StartsWith("user://")
                ? ProjectSettings.GlobalizePath(autoLoad) : autoLoad;
            RunCommand($"(load {System.IO.Path.GetFileNameWithoutExtension(autoLoad)})", absolute);
            FrameAll();
        }
        if (OS.GetEnvironment("HEROIC_EDITOR_AUTOBUILD") == "1") AutoBuildDemo();

        string autoSave = OS.GetEnvironment("HEROIC_EDITOR_SAVE");
        if (!string.IsNullOrEmpty(autoSave))
        {
            _session.SaveFile(autoSave);
            Log($"saved {autoSave}");
            GD.Print($"[BuildMode] saved {autoSave}");
        }
        Redraw();

        if (Live) FrameAll();

        string script = OS.GetEnvironment("HEROIC_EDITOR_INPUT");
        if (!string.IsNullOrEmpty(script)) _inputScript = new Queue<string>(script.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    // ------------------------------------------------------- scripted input

    /// <summary>
    /// HEROIC_EDITOR_INPUT="palette tank; move 700 450; down 700 450; up 700 450; key delete; wait 5; ..."
    /// feeds mouse and key events through Godot's own input pipeline
    /// (Input.ParseInputEvent), the same path real clicks take, one step
    /// every few frames, so a headless or recorded run exercises the editor's
    /// actual handling. Positions are viewport pixels. "palette NAME" picks
    /// a palette entry as a click in the list would; "drag X1 Y1 X2 Y2"
    /// presses, moves in steps and releases; "rdrag" and "mdrag" orbit and
    /// pan; "key NAME" accepts delete, escape, f, g, ctrl+z, ctrl+d.
    /// </summary>
    private Queue<string>? _inputScript;
    private int _inputWait;

    public override void _Process(double delta)
    {
        if (_inputScript is null) return;
        if (_inputWait-- > 0) return;
        _inputWait = 3;
        while (_inputScript.TryDequeue(out var step))
        {
            GD.Print($"[BuildMode] input: {step}");
            var w = step.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float N(int i) => float.Parse(w[i], System.Globalization.CultureInfo.InvariantCulture);
            switch (w[0])
            {
                case "palette":
                    for (int i = 0; i < _paletteList.ItemCount; i++)
                        if (_paletteList.GetItemMetadata(i).AsString() == w[1]) _paletteList.Select(i);
                    StartPlacing(w[1]);
                    return;
                case "move": Mouse(new Vector2(N(1), N(2))); return;
                case "down": Mouse(new Vector2(N(1), N(2)), MouseButton.Left, true); return;
                case "up": Mouse(new Vector2(N(1), N(2)), MouseButton.Left, false); return;
                case "shiftdown": Mouse(new Vector2(N(1), N(2)), MouseButton.Left, true, shift: true); return;
                case "drag" or "rdrag" or "mdrag":
                {
                    var button = w[0] == "drag" ? MouseButton.Left : w[0] == "rdrag" ? MouseButton.Right : MouseButton.Middle;
                    var a = new Vector2(N(1), N(2)); var b = new Vector2(N(3), N(4));
                    Mouse(a, button, true);
                    for (int k = 1; k <= 10; k++) Mouse(a.Lerp(b, k / 10f));
                    Mouse(b, button, false);
                    return;
                }
                case "key":
                {
                    var ev = new InputEventKey { Pressed = true };
                    foreach (var part in w[1].Split('+'))
                        switch (part)
                        {
                            case "ctrl": ev.CtrlPressed = true; break;
                            case "shift": ev.ShiftPressed = true; break;
                            default: ev.Keycode = OS.FindKeycodeFromString(part); break;
                        }
                    Input.ParseInputEvent(ev);
                    return;
                }
                case "wait": _inputWait = (int)N(1); return;
                case "cmd": RunCommand(string.Join(' ', w.Skip(1))); return;
                case "save": _session.SaveFile(w[1]); GD.Print($"[BuildMode] saved {w[1]}"); return;
                case "click-port":
                {
                    // click a named connection point wherever it is on screen: "click-port pot_2.steam"
                    var parts = w[1].Split('.');
                    var port = _ports.FirstOrDefault(p => p.Part == parts[0] && p.Port == parts[1]);
                    if (port.Node is null) { GD.Print($"[BuildMode] no port {w[1]}"); return; }
                    // injected events are in window pixels; the camera projects to the
                    // 3D viewport's, which the window's content scale stretches
                    var at = GetViewport().GetScreenTransform() * _camera.UnprojectPosition(port.At);
                    Mouse(at);
                    Mouse(at, MouseButton.Left, true);
                    Mouse(at, MouseButton.Left, false);
                    return;
                }
                case "link":
                    StartLink(Enum.Parse<LinkGestures.Kind>(w[1], ignoreCase: true));
                    return;
                case "click-part":
                {
                    // click a part wherever it is on screen: "click-part gear-1"
                    if (BoundsOf(NodesOf(w[1])) is not { } box) { GD.Print($"[BuildMode] no part {w[1]}"); return; }
                    var at = GetViewport().GetScreenTransform() * _camera.UnprojectPosition(box.GetCenter());
                    Mouse(at);
                    Mouse(at, MouseButton.Left, true);
                    Mouse(at, MouseButton.Left, false);
                    return;
                }
                case "log":
                    GD.Print($"[BuildMode] state: selected={_selectedId ?? "none"} parts={string.Join(",", _session.Document.Parts.Values.Select(p => $"{p.Id}@({F(p.At.X)} {F(p.At.Y)} {F(p.At.Z)})"))}");
                    continue;
            }
        }
        _inputScript = null;
    }

    private Vector2 _lastMouse;

    private void Mouse(Vector2 at, MouseButton button = MouseButton.None, bool pressed = false, bool shift = false)
    {
        if (at != _lastMouse)
        {
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = at - _lastMouse, ShiftPressed = shift });
            _lastMouse = at;
        }
        if (button != MouseButton.None)
            Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = pressed, ShiftPressed = shift });
    }

    // ------------------------------------------------------------------ UI

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        // left: palette, material, file and run
        var left = new PanelContainer { CustomMinimumSize = new Vector2(250, 0) };
        left.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        layer.AddChild(left);
        var leftCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        left.AddChild(leftCol);

        leftCol.AddChild(new Label { Text = "Build Mode" });

        // Start from something that works, and change it.
        var examples = new OptionButton { TooltipText = "Load a working machine to change" };
        examples.AddItem("Start from an example…");
        var exampleFiles = ExampleMachines();
        foreach (var (name, _) in exampleFiles) examples.AddItem(name);
        examples.ItemSelected += index =>
        {
            if (index <= 0) return;
            var (_, path) = exampleFiles[(int)index - 1];
            RunCommand($"(load {System.IO.Path.GetFileNameWithoutExtension(path)})", ProjectSettings.GlobalizePath(path));
            Select(null);
            FrameAll();
            examples.Select(0);
        };
        leftCol.AddChild(examples);

        leftCol.AddChild(new Label { Text = "Parts: pick one, then click in the scene", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _paletteList = new ItemList { CustomMinimumSize = new Vector2(230, 260), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var groups = _palette.GroupBy(p => p.PrimitiveKind is { } k ? PartInfo.GetValueOrDefault(k).Group ?? "Other" : "Gears, pulleys and drums")
            .OrderBy(g => Array.IndexOf(GroupOrder, g.Key) is var i and >= 0 ? i : 99);
        foreach (var group in groups)
        {
            int header = _paletteList.AddItem(group.Key);
            _paletteList.SetItemSelectable(header, false);
            _paletteList.SetItemCustomFgColor(header, new Color(1f, 0.8f, 0.45f));
            foreach (var item in group)
            {
                int i = _paletteList.AddItem("   " + item.Label);
                _paletteList.SetItemMetadata(i, item.Id);
                _paletteList.SetItemTooltip(i, item.PrimitiveKind is { } k && PartInfo.TryGetValue(k, out var info) ? info.Description : item.Label);
            }
        }
        _paletteList.ItemSelected += index => StartPlacing(_paletteList.GetItemMetadata((int)index).AsString());
        leftCol.AddChild(_paletteList);
        _partHelp = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(230, 0), Modulate = new Color(1, 1, 1, 0.75f) };
        leftCol.AddChild(_partHelp);

        leftCol.AddChild(new Label { Text = "Join parts: pick a tool, then click the parts" });
        var joinGrid = new GridContainer { Columns = 2 };
        foreach (var (kind, label, tip) in new[]
        {
            (LinkGestures.Kind.Rope, "Rope", "A rope between two parts (click one, then the other); trim its length in the inspector"),
            (LinkGestures.Kind.Mesh, "Gear mesh", "Two gears in mesh (click both)"),
            (LinkGestures.Kind.Belt, "Belt", "An open belt from a driving drum to a driven one (click both); set its tension in the console with (belt …)"),
            (LinkGestures.Kind.Arbor, "Axle", "Wheels fixed on one axle: click them, then Enter. The first carries the bearing"),
            (LinkGestures.Kind.SealedAir, "Shared air", "Tanks sharing one sealed air space: click them, then Enter"),
            (LinkGestures.Kind.Cylinder, "Cylinder", "A piston joined to the boiler that feeds it (click both)"),
            (LinkGestures.Kind.Joint, "Ball joint", "Two moving parts joined at a point halfway between them, turning every way about it (click both)"),
        })
        {
            var b = new Button { Text = label, TooltipText = tip, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.Pressed += () => StartLink(kind);
            joinGrid.AddChild(b);
        }
        leftCol.AddChild(joinGrid);

        leftCol.AddChild(new Label { Text = "New parts are made of" });
        _materialBox = new OptionButton();
        _materialIds = _materials.All.OrderBy(m => m.Id).Select(m => m.Id).ToList();
        foreach (var mat in _materials.All.OrderBy(m => m.Id)) _materialBox.AddItem(mat.Name);
        _materialBox.Select(Math.Max(0, _materialIds.IndexOf(_material)));
        _materialBox.ItemSelected += index => _material = _materialIds[(int)index];
        leftCol.AddChild(_materialBox);

        // The planet the scene stands on (issue #38): its gravity, air and sunlight.
        leftCol.AddChild(new Label { Text = "On the planet" });
        var planetBox = new OptionButton { TooltipText = "Gravity, air pressure and mix, sunlight: (planet mars) in the console, with #:gravity etc. to change a number" };
        var planetIds = HeroicInventions.Sim.Planet.Presets.Keys.ToList();
        foreach (var id in planetIds) planetBox.AddItem(HeroicInventions.Sim.Planet.Presets[id].Name);
        planetBox.Select(Math.Max(0, planetIds.IndexOf(_session.Document.Planet.Id)));
        planetBox.ItemSelected += index => RunCommand($"(planet {planetIds[(int)index]})");
        leftCol.AddChild(planetBox);

        var fileRow = new HBoxContainer();
        var saveButton = new Button { Text = "Save…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        saveButton.Pressed += () => _saveDialog.PopupCentered(new Vector2I(700, 500));
        fileRow.AddChild(saveButton);
        var loadButton = new Button { Text = "Load…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        loadButton.Pressed += () => _loadDialog.PopupCentered(new Vector2I(700, 500));
        fileRow.AddChild(loadButton);
        leftCol.AddChild(fileRow);

        var runButton = new Button { Text = "Run this machine" };
        runButton.Pressed += () => RunRequested?.Invoke();
        leftCol.AddChild(runButton);
        var exitButton = new Button { Text = "Leave build mode" };
        exitButton.Pressed += () => ExitRequested?.Invoke();
        leftCol.AddChild(exitButton);

        // right: inspector and console
        var right = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        right.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        right.OffsetLeft = -300;
        layer.AddChild(right);
        var rightCol = new VBoxContainer();
        right.AddChild(rightCol);

        rightCol.AddChild(new Label { Text = "Selected part" });
        var inspectorScroll = new ScrollContainer { CustomMinimumSize = new Vector2(280, 260), SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _inspector = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        inspectorScroll.AddChild(_inspector);
        rightCol.AddChild(inspectorScroll);

        rightCol.AddChild(new HSeparator());
        rightCol.AddChild(new Label { Text = "Console (every action is a command)" });
        _console = new RichTextLabel { CustomMinimumSize = new Vector2(280, 150), ScrollFollowing = true, BbcodeEnabled = false, SelectionEnabled = true };
        rightCol.AddChild(_console);
        _consoleInput = new LineEdit { PlaceholderText = "(tank t1 #:at (0 0 0) #:area 1 #:height 1)" };
        _consoleInput.TextSubmitted += text => { RunCommand(text); _consoleInput.Text = ""; };
        rightCol.AddChild(_consoleInput);

        // bottom: help and status
        var help = new Label
        {
            Text = "Right-drag orbit · middle-drag or Shift+right-drag pan · scroll zoom · F frame · "
                 + "click select · drag move (Ctrl: up/down) · Del delete · Ctrl+D duplicate · Ctrl+Z undo · G grid · Esc cancel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        help.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        help.OffsetLeft = 260; help.OffsetRight = -310; help.OffsetTop = -44; help.OffsetBottom = -6;
        layer.AddChild(help);
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _status.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _status.OffsetLeft = 260; _status.OffsetRight = -310; _status.OffsetTop = 10; _status.OffsetBottom = 34;
        layer.AddChild(_status);

        _saveDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.SaveFile, Access = FileDialog.AccessEnum.Filesystem,
            CurrentDir = ProjectSettings.GlobalizePath("user://machines"), Filters = ["*.machine"],
        };
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://machines"));
        _saveDialog.FileSelected += path => { _session.SaveFile(path); Log($"saved {path}"); };
        layer.AddChild(_saveDialog);

        _loadDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem,
            CurrentDir = ProjectSettings.GlobalizePath("res://machines"), Filters = ["*.machine"],
        };
        _loadDialog.FileSelected += path =>
        {
            RunCommand($"(load {System.IO.Path.GetFileNameWithoutExtension(path)})", path);
            Select(null);
            FrameAll();
        };
        layer.AddChild(_loadDialog);
    }

    /// <summary>A faint 20 m grid of 1 m lines on the ground, so placement has a sense of scale.</summary>
    private void BuildGrid()
    {
        var mat = Shapes.Mat(new Color(0.2f, 0.25f, 0.2f), alpha: 0.35f);
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        for (int i = -10; i <= 10; i++)
        {
            AddChild(Shapes.Rod(new Vector3(i, 0.002f, -10), new Vector3(i, 0.002f, 10), 0.004f, mat));
            AddChild(Shapes.Rod(new Vector3(-10, 0.002f, i), new Vector3(10, 0.002f, i), 0.004f, mat));
        }
    }

    // ------------------------------------------------------------ commands

    /// <summary>
    /// Runs one command through the session (the single path every mouse
    /// action and the console both use), echoes it and its result, and
    /// redraws from the session's document.
    /// </summary>
    private bool RunCommand(string command, string? loadFrom = null)
    {
        _console.AddText($"> {command}\n");
        GD.Print($"[BuildMode] > {command}");
        bool ok = true;
        try
        {
            string result = loadFrom is null ? _session.Execute(command) : _session.LoadFile(loadFrom);
            _console.AddText($"{result}\n");
            GD.Print($"[BuildMode] {result}");
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or MachineFormatException or KeyNotFoundException)
        {
            _console.AddText($"error: {e.Message}\n");
            GD.Print($"[BuildMode] error: {e.Message}");
            ok = false;
        }
        if (_selectedId is { } id && !_session.Document.Parts.ContainsKey(id)) _selectedId = null;
        Redraw();
        return ok;
    }

    private void Log(string text) => _console.AddText(text + "\n");

    private static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static string Xyz(Vector3 v) => $"({F(v.X)} {F(v.Y)} {F(v.Z)})";

    /// <summary>The shipped machines, steam wheels first, with readable names.</summary>
    private static List<(string Name, string Path)> ExampleMachines()
    {
        var files = new List<string>();
        using var dir = DirAccess.Open("res://machines");
        if (dir is not null)
            foreach (var f in dir.GetFiles()) if (f.EndsWith(".machine")) files.Add($"res://machines/{f}");
        string Pretty(string path)
        {
            string n = System.IO.Path.GetFileNameWithoutExtension(path).Replace('-', ' ');
            return char.ToUpperInvariant(n[0]) + n[1..];
        }
        int Rank(string path) => path.Contains("branca") ? 0 : path.Contains("solar-steam") ? 1 : 2;
        return files.OrderBy(Rank).ThenBy(f => f).Select(f => (Pretty(f), f)).ToList();
    }

    // -------------------------------------------------------------- drawing

    /// <summary>
    /// Rebuilds the preview from the session's document: the real
    /// MachineView, taken out of physics so nothing falls or turns. A design
    /// that can't be built yet (a sluice with no channel named) falls back
    /// to simple placeholder shapes, with the reason shown.
    /// </summary>
    private void Redraw()
    {
        if (!Live) _preview?.QueueFree();   // a live machine belongs to the world, not the editor
        _preview = null;
        foreach (var nodes in _fallbackVisuals.Values) foreach (var n in nodes) n.QueueFree();
        _fallbackVisuals.Clear();
        foreach (var v in _pipeVisuals) v.QueueFree();
        _pipeVisuals.Clear();

        var doc = _session.Document;
        _unfinished.Clear();
        if (doc.Parts.Count > 0)
        {
            // One unfinished part (a mirror not yet aimed at anything) used to
            // stop the whole machine being drawn. Set such parts aside one at a
            // time, naming what each still needs, and draw the rest for real.
            var working = EditorDocument.Load(doc.ToMachineDef());
            for (int attempt = 0; attempt <= doc.Parts.Count && working.Parts.Count > 0; attempt++)
            {
                try
                {
                    if (Live)
                    {
                        // rebuild the running machine only if the design really changed
                        var def = working.ToMachineDef();
                        string text = MachineWriter.Write(def);
                        _preview = text == _lastApplied ? _liveView!() : _applyLive!(def);
                        _lastApplied = text;
                        break;
                    }
                    var view = new MachineView(new MachineRuntime(working.ToMachineDef(), _materials), _materials)
                    {
                        ProcessMode = ProcessModeEnum.Disabled, // out of physics: a frozen snapshot of what will run
                    };
                    AddChild(view);
                    view.SetFrozen(true);
                    _preview = view;
                    break;
                }
                catch (Exception e)
                {
                    if (!Live) _preview?.QueueFree();
                    _preview = null;
                    string? culprit = working.Parts.Keys
                        .Where(id => System.Text.RegularExpressions.Regex.IsMatch(e.Message, $@"(^|[\s(]){System.Text.RegularExpressions.Regex.Escape(id)}($|[\s:.,)])"))
                        .OrderByDescending(id => id.Length).FirstOrDefault();
                    if (culprit is null)
                    {
                        foreach (var id in working.Parts.Keys) _unfinished[id] = e.Message;
                        break;
                    }
                    _unfinished[culprit] = e.Message;
                    working.RemovePart(culprit);
                }
            }
            foreach (var id in _unfinished.Keys)
                if (doc.Parts.TryGetValue(id, out var part))
                {
                    var box = FallbackVisual(part);
                    box.MaterialOverride = Shapes.Mat(new Color(0.9f, 0.3f, 0.2f), alpha: 0.6f);
                    ((StandardMaterial3D)box.MaterialOverride).Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                    AddChild(box);
                    _fallbackVisuals[id] = [box];
                }
            if (_unfinished.Count > 0) DrawPipes();
        }
        string? problem = _unfinished.Count == 0 ? null
            : $"{_unfinished.Count} part{(_unfinished.Count == 1 ? "" : "s")} not finished (red): {string.Join("; ", _unfinished.Select(kv => $"{kv.Key}: {Plain(kv.Value)}"))}";
        _status.Text = problem is null
            ? (_placingPaletteId is { } p ? $"Placing {p}: click to place, Shift keeps placing, Esc cancels" : _selectedId is { } s ? $"Selected {s}" : "")
            : problem;
        DrawLinks();
        DrawPorts();
        RefreshHighlights();
        RebuildInspector();
    }

    /// <summary>
    /// A dot on every connection point: blue where water goes in or out,
    /// white where steam does. Click one, then another, to join them.
    /// </summary>
    private void DrawPorts()
    {
        foreach (var p in _ports) p.Node.QueueFree();
        _ports.Clear();
        foreach (var part in _session.Document.Parts.Values)
            foreach (var port in part.Ports)
            {
                var w = EditorDocument.PortWorldPosition(part, port);
                var at = new Vector3((float)w.X, (float)w.Y, (float)w.Z);
                var colour = port.Kind switch { "water" => new Color(0.3f, 0.6f, 1f), "steam" => new Color(1f, 1f, 1f), _ => new Color(0.95f, 0.8f, 0.3f) };
                var mat = Shapes.Mat(colour);
                mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                mat.NoDepthTest = true;   // visible through the part it belongs to
                var dot = Shapes.Sphere(0.022f, mat);
                dot.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                dot.Position = at;
                AddChild(dot);
                _ports.Add((part.Id, port.Name, port.Kind, at, dot));
            }
    }

    /// <summary>The connection point nearest the mouse ray, within a few pixels' reach.</summary>
    private (string Part, string Port, string Kind, Vector3 At)? PickPort(Vector2 screen)
    {
        var (from, dir) = Ray(screen);
        (string, string, string, Vector3)? best = null;
        float bestT = float.MaxValue;
        foreach (var p in _ports)
        {
            float t = (p.At - from).Dot(dir);
            if (t <= 0) continue;
            float miss = (from + dir * t).DistanceTo(p.At);
            if (miss < Mathf.Max(0.03f, t * 0.015f) && t < bestT) { bestT = t; best = (p.Part, p.Port, p.Kind, p.At); }
        }
        return best;
    }

    private void CancelConnect()
    {
        _connectFrom = null;
        _connectLine?.QueueFree();
        _connectLine = null;
    }

    private void DrawConnectLine(Vector3 to)
    {
        _connectLine?.QueueFree();
        if (_connectFrom is not { } from) return;
        var mat = Shapes.Mat(new Color(1f, 0.85f, 0.3f));
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.NoDepthTest = true;
        _connectLine = Shapes.Rod(from.At, to, 0.006f, mat);
        AddChild(_connectLine);
    }

    private IEnumerable<Node3D> NodesOf(string id) =>
        _preview?.PartNodes.GetValueOrDefault(id) is { } nodes ? nodes
        : _fallbackVisuals.GetValueOrDefault(id) ?? (IEnumerable<Node3D>)[];

    /// <summary>A part's world bounding box: every mesh it drew, merged.</summary>
    private Aabb? BoundsOf(IEnumerable<Node3D> nodes)
    {
        Aabb? box = null;
        foreach (var root in nodes)
            foreach (var vi in Descendants(root).OfType<VisualInstance3D>())
            {
                if (vi is GpuParticles3D or Label3D) continue;
                var local = vi.GetAabb();
                if (local.Size == Vector3.Zero) continue;
                var world = vi.GlobalTransform * local;
                box = box is { } b ? b.Merge(world) : world;
            }
        return box;
    }

    private static IEnumerable<Node> Descendants(Node n)
    {
        yield return n;
        foreach (var c in n.GetChildren()) foreach (var d in Descendants(c)) yield return d;
    }

    private void RefreshHighlights()
    {
        foreach (var id in _session.Document.Parts.Keys)
        {
            var overlay = id == _selectedId ? SelectedOverlay : _linkPicks.Contains(id) ? PickedOverlay : id == _hoverId ? HoverOverlay : null;
            foreach (var root in NodesOf(id))
                foreach (var g in Descendants(root).OfType<GeometryInstance3D>())
                    if (g is not Label3D) g.MaterialOverlay = overlay;
        }
    }

    private static StandardMaterial3D Overlay(Color c) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = new Color(c.R, c.G, c.B),
        EmissionEnergyMultiplier = 0.6f,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    // ---------------------------------------------------------- inspector

    private void RebuildInspector()
    {
        foreach (var c in _inspector.GetChildren()) c.QueueFree();
        if (_selectedId is not { } id || !_session.Document.Parts.TryGetValue(id, out var part))
        {
            _inspector.AddChild(new Label { Text = "Nothing selected. Click a part.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            return;
        }
        _inspector.AddChild(new Label { Text = $"{part.Id} ({part.Kind})" });
        if (_unfinished.TryGetValue(id, out var why))
            _inspector.AddChild(new Label { Text = $"Not finished: {Plain(why)}", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1f, 0.55f, 0.45f) });

        _inspector.AddChild(new Label { Text = "Position (m)" });
        var posRow = new HBoxContainer();
        foreach (var (axis, value) in new[] { ("x", part.At.X), ("y", part.At.Y), ("z", part.At.Z) })
        {
            var field = NumberField(value, text =>
            {
                if (!TryNumber(text, out double v)) return;
                var at = part.At;
                var to = axis switch { "x" => at with { X = v }, "y" => at with { Y = v }, _ => at with { Z = v } };
                RunCommand($"(move {id} ({F(to.X)} {F(to.Y)} {F(to.Z)}))");
            });
            field.TooltipText = axis;
            posRow.AddChild(field);
        }
        _inspector.AddChild(posRow);

        _inspector.AddChild(new Label { Text = "Material" });
        var matBox = new OptionButton();
        foreach (var mid in _materialIds) matBox.AddItem(_materials[mid].Name);
        matBox.Select(Math.Max(0, _materialIds.IndexOf(part.Material)));
        matBox.ItemSelected += index => RunCommand($"(set {id} #:material {_materialIds[(int)index]})");
        _inspector.AddChild(matBox);

        foreach (var (key, value) in part.Props.OrderBy(p => p.Key))
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = key, CustomMinimumSize = new Vector2(120, 0), ClipText = true, TooltipText = key });
            if (value is SNumber n)
            {
                row.AddChild(NumberField(n.Value, text => { if (TryNumber(text, out double v)) RunCommand($"(set {id} #:{key} {F(v)})"); }));
                if (IsOptionalNumber(part.Kind, key))
                {
                    var clear = new Button { Text = "×", TooltipText = "clear (none)" };
                    clear.Pressed += () => RunCommand($"(set {id} #:{key} #f)");
                    row.AddChild(clear);
                }
            }
            else if (value is SSymbol sym && SymbolChoices(key) is { } choices)
            {
                // A prop with a fixed set of answers: a dropdown of them.
                var pick = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                var options = choices.Contains(sym.Name) ? choices : [sym.Name, .. choices];
                foreach (var o in options) pick.AddItem(o);
                pick.Select(options.IndexOf(sym.Name));
                pick.ItemSelected += index => RunCommand($"(set {id} #:{key} {options[(int)index]})");
                row.AddChild(pick);
            }
            else if (value is SSymbol && key is not "shape")
            {
                // A prop naming another part (or a channel or inflow): pick it from what is on the bench.
                var pick = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                var doc = _session.Document;
                var targets = doc.Parts.Keys.Concat(doc.Channels.Select(c => c.Id)).Concat(doc.Sources.Select(x => x.Id)).Concat(doc.Pipes.Keys)
                    .Where(k => k != id).Distinct().OrderBy(k => k).ToList();
                var sym2 = ((SSymbol)value).Name;
                pick.AddItem(sym2 == "?" ? "(choose one)" : sym2);
                foreach (var t in targets.Where(t => t != sym2)) pick.AddItem(t);
                pick.ItemSelected += index => { if (index > 0) RunCommand($"(set {id} #:{key} {pick.GetItemText((int)index)})"); };
                row.AddChild(pick);
            }
            else if (value is SBool flag && IsFlag(key))
            {
                var box = new CheckBox { Text = flag.Value ? "yes" : "no", ButtonPressed = flag.Value };
                box.Toggled += on => RunCommand($"(set {id} #:{key} {(on ? "#t" : "#f")})");
                row.AddChild(box);
            }
            else if (value is SBool)
            {
                // An optional number that is not set (#f): type one to set it.
                var field = new LineEdit { PlaceholderText = "not set: type a number", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                field.TextSubmitted += text => { if (TryNumber(text, out double v)) RunCommand($"(set {id} #:{key} {F(v)})"); };
                row.AddChild(field);
            }
            else
                row.AddChild(new Label { Text = SExprText(value), TooltipText = "set in the console", Modulate = new Color(1, 1, 1, 0.6f) });
            _inspector.AddChild(row);
        }

        var links = LinkGestures.LinksOn(_session.Document, id);
        if (links.Count > 0)
        {
            _inspector.AddChild(new HSeparator());
            _inspector.AddChild(new Label { Text = "Joined to" });
            foreach (var (label, remove, ropeId) in links)
            {
                var row = new HBoxContainer();
                row.AddChild(new Label { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart });
                var take = new Button { Text = "Remove" };
                take.Pressed += () => RunCommand(remove);
                row.AddChild(take);
                _inspector.AddChild(row);
                if (_session.Document.Belts.FirstOrDefault(b => remove == $"(remove {b.Id})") is { } belt)
                {
                    var tighten = new HBoxContainer();
                    tighten.AddChild(new Label { Text = "  tension (N)" });
                    tighten.AddChild(NumberField(belt.Tension, text => { if (TryNumber(text, out double v)) RunCommand($"(set-belt {belt.Id} #:tension {F(v)})"); }));
                    _inspector.AddChild(tighten);
                }
                if (ropeId is not null && _session.Document.Ropes.FirstOrDefault(r => r.Id == ropeId) is { } rope)
                {
                    var trim = new HBoxContainer();
                    trim.AddChild(new Label { Text = "  length (m)" });
                    trim.AddChild(NumberField(rope.Length, text => { if (TryNumber(text, out double v)) RunCommand($"(set-rope {ropeId} #:length {F(v)})"); }));
                    trim.AddChild(new Label { Text = "diameter" });
                    trim.AddChild(NumberField(rope.Diameter, text => { if (TryNumber(text, out double v)) RunCommand($"(set-rope {ropeId} #:diameter {F(v)})"); }));
                    _inspector.AddChild(trim);
                    if (rope.Over.Count > 0 && rope.Turns is null)
                    {
                        // what its #:over points are: pulleys that turn, or fixed bars it drags over (capstan friction)
                        var over = new HBoxContainer();
                        over.AddChild(new Label { Text = "  runs over" });
                        var barBox = new OptionButton { TooltipText = "A fixed bar holds e^(μθ) times the slack side's pull: 4.4× over half a turn of oak" };
                        barBox.AddItem("pulleys (no friction)");
                        foreach (var mid in _materialIds) barBox.AddItem($"a fixed {_materials[mid].Name.ToLowerInvariant()} bar");
                        barBox.Select(rope.Bar is { } bar ? _materialIds.IndexOf(bar) + 1 : 0);
                        barBox.ItemSelected += index => RunCommand(index == 0
                            ? $"(set-rope {ropeId} #:bar #f #:mu #f)"
                            : $"(set-rope {ropeId} #:bar {_materialIds[(int)index - 1]})");
                        over.AddChild(barBox);
                        _inspector.AddChild(over);
                    }
                }
            }
        }

        var buttons = new HBoxContainer();
        var dup = new Button { Text = "Duplicate" };
        dup.Pressed += DuplicateSelected;
        buttons.AddChild(dup);
        var del = new Button { Text = "Delete" };
        del.Pressed += DeleteSelected;
        buttons.AddChild(del);
        _inspector.AddChild(buttons);
    }

    /// <summary>The fixed answers of a symbol prop, or null when it names a part or something free-form.</summary>
    private List<string>? SymbolChoices(string key) => key switch
    {
        "axis" => ["x", "y", "z"],
        "fuel-kind" => ["wood", "charcoal", "coal"],
        "kind" => ["tongs", "hook"],
        "rope" => _materialIds,
        _ => null,
    };

    /// <summary>A true/false prop, as opposed to an optional number that is #f until set.</summary>
    private static bool IsFlag(string key) => key is "round" or "fast" or "reverse" or "breakable";

    /// <summary>A number the part's template starts as #f (a sluice's width, a pendulum's bearing): it can be cleared back to none.</summary>
    private static bool IsOptionalNumber(string kind, string key) =>
        PartTemplates.PrimitiveKinds.Contains(kind) && PartTemplates.Create(kind, "x", new Vec3(0, 0, 0), "bronze").Props.GetValueOrDefault(key) is SBool;

    private static string SExprText(SExpr e) => e switch
    {
        SNumber n => F(n.Value),
        SSymbol s => s.Name,
        SBool b => b.Value ? "yes" : "none",
        SString s => s.Value,
        _ => e.ToString() ?? "",
    };

    private static LineEdit NumberField(double value, Action<string> commit)
    {
        var field = new LineEdit { Text = F(value), CustomMinimumSize = new Vector2(70, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        field.TextSubmitted += text => commit(text);
        field.FocusExited += () => { if (field.Text != F(value)) commit(field.Text); };
        return field;
    }

    private static bool TryNumber(string text, out double v) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

    // -------------------------------------------------------------- camera

    private void UpdateCamera()
    {
        var offset = new Vector3(
            _distance * Mathf.Cos(_pitch) * Mathf.Sin(_yaw),
            _distance * Mathf.Sin(_pitch),
            _distance * Mathf.Cos(_pitch) * Mathf.Cos(_yaw));
        _camera.Position = _pivot + offset;
        _camera.LookAt(_pivot, Vector3.Up);
    }

    /// <summary>F: point the camera at the selection, or at the whole machine.</summary>
    private void Frame(IEnumerable<string> ids)
    {
        Aabb? box = null;
        foreach (var id in ids)
            if (BoundsOf(NodesOf(id)) is { } b) box = box is { } a ? a.Merge(b) : b;
        if (box is not { } bb) return;
        _pivot = bb.GetCenter();
        _distance = Mathf.Clamp(bb.Size.Length() * 1.4f + 0.5f, 1f, 60f);
        UpdateCamera();
    }

    private void FrameAll() => Frame(_session.Document.Parts.Keys);

    // --------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _distance = Mathf.Max(0.3f, _distance * 0.9f); UpdateCamera(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _distance = Mathf.Min(80f, _distance / 0.9f); UpdateCamera(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } rb:
                if (rb.Pressed && _placingPaletteId is not null) { CancelPlacing(); break; }
                _orbiting = rb.Pressed && !rb.ShiftPressed;
                _panning = rb.Pressed && rb.ShiftPressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _panning = mb.Pressed; break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } lb:
                if (lb.Pressed) OnLeftDown(lb.Position, lb.ShiftPressed);
                else OnLeftUp();
                break;
            case InputEventMouseMotion mm:
                OnMotion(mm);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                OnKey(key);
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void OnMotion(InputEventMouseMotion mm)
    {
        if (_orbiting)
        {
            _yaw -= mm.Relative.X * 0.008f;
            _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.008f, -0.1f, 1.5f);
            UpdateCamera();
            return;
        }
        if (_panning)
        {
            float scale = _distance * 0.0015f;
            var right = _camera.GlobalBasis.X;
            var forward = new Vector3(_camera.GlobalBasis.Z.X, 0, _camera.GlobalBasis.Z.Z).Normalized();
            _pivot += (-right * mm.Relative.X + forward * -mm.Relative.Y) * scale;
            UpdateCamera();
            return;
        }
        if (_placingPaletteId is not null) { MoveGhost(mm.Position); return; }
        var hoverPort = PickPort(mm.Position);
        foreach (var p in _ports) p.Node.Scale = Vector3.One * (hoverPort is { } h && h.Part == p.Part && h.Port == p.Port ? 1.8f : 1f);
        if (_connectFrom is not null)
        {
            if (hoverPort is { } hp) DrawConnectLine(hp.At);
            else if (SurfacePoint(mm.Position, null, out var ground)) DrawConnectLine(ground);
            return;
        }
        if (hoverPort is { } hovered && _pressedId is null)
            _status.Text = $"{hovered.Part}: {hovered.Kind} point \"{hovered.Port}\". Click it, then another, to connect them";
        if (_pressedId is { } id)
        {
            if (!_dragging && mm.Position.DistanceTo(_pressPos) > DragThreshold) BeginDrag(id, _pressPos);
            if (_dragging) Drag(id, mm.Position, Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta), mm.Relative);
            return;
        }
        string? hover = PickPart(mm.Position, out _);
        if (hover != _hoverId) { _hoverId = hover; RefreshHighlights(); }
    }

    private void OnKey(InputEventKey key)
    {
        bool cmd = key.CtrlPressed || key.MetaPressed;
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                if (_link is { } l) FinishLink(l);
                break;
            case Key.Escape:
                if (_link is not null) { CancelLink(); _status.Text = ""; }
                else if (_connectFrom is not null) { CancelConnect(); _status.Text = ""; }
                else if (_placingPaletteId is not null) CancelPlacing();
                else if (_selectedId is not null) Select(null);
                else ExitRequested?.Invoke();
                break;
            case Key.Delete or Key.Backspace:
                DeleteSelected(); break;
            case Key.D when cmd:
                DuplicateSelected(); break;
            case Key.Z when cmd && key.ShiftPressed:
                RunCommand("(redo)"); break;
            case Key.Z when cmd:
                RunCommand("(undo)"); break;
            case Key.Y when cmd:
                RunCommand("(redo)"); break;
            case Key.F:
                if (_selectedId is { } s) Frame([s]); else FrameAll();
                break;
            case Key.G:
                _gridSnap = !_gridSnap;
                _status.Text = _gridSnap ? "Grid snap on (5 cm)" : "Grid snap off";
                break;
        }
    }

    // ----------------------------------------------------------- picking

    private (Vector3 From, Vector3 Dir) Ray(Vector2 screen) => (_camera.ProjectRayOrigin(screen), _camera.ProjectRayNormal(screen));

    /// <summary>The nearest part whose drawn shape the mouse ray passes through.</summary>
    private string? PickPart(Vector2 screen, out float distance, string? exclude = null)
    {
        var (from, dir) = Ray(screen);
        string? best = null;
        distance = float.MaxValue;
        foreach (var id in _session.Document.Parts.Keys)
        {
            if (id == exclude) continue;
            if (BoundsOf(NodesOf(id)) is not { } box) continue;
            if (RayHitsBox(from, dir, box, out float t) && t < distance) { distance = t; best = id; }
        }
        return best;
    }

    private static bool RayHitsBox(Vector3 from, Vector3 dir, Aabb box, out float t)
    {
        float tMin = 0, tMax = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = from[axis], d = dir[axis], lo = box.Position[axis], hi = box.End[axis];
            if (Mathf.Abs(d) < 1e-9f) { if (o < lo || o > hi) { t = 0; return false; } continue; }
            float t1 = (lo - o) / d, t2 = (hi - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax) { t = 0; return false; }
        }
        t = tMin;
        return true;
    }

    /// <summary>
    /// Where something under the mouse would rest: on top of the part the
    /// ray hits first (its bounding box's top), or on the ground.
    /// </summary>
    private bool SurfacePoint(Vector2 screen, string? exclude, out Vector3 point)
    {
        var (from, dir) = Ray(screen);
        string? hit = PickPart(screen, out float t, exclude);
        if (hit is not null && BoundsOf(NodesOf(hit)) is { } box)
        {
            var p = from + dir * t;
            point = new Vector3(p.X, box.End.Y, p.Z);
            return true;
        }
        point = default;
        if (GroundHeight is { } ground) return OnGround(from, dir, ground, out point);
        if (Mathf.Abs(dir.Y) < 1e-6f) return false;
        float tg = -from.Y / dir.Y;
        if (tg < 0) return false;
        point = from + dir * tg;
        return true;
    }

    /// <summary>
    /// The ground's height at a world point, when the machine stands on a
    /// world's map (issue #37): a part placed on open ground then lands on the
    /// terrain there, not on a flat floor at 0.
    /// </summary>
    public Func<double, double, double>? GroundHeight { get; set; }

    /// <summary>Where the ray first meets the ground: stepped out half a metre at a time, then narrowed by halving.</summary>
    private static bool OnGround(Vector3 from, Vector3 dir, Func<double, double, double> ground, out Vector3 point)
    {
        point = default;
        float Above(float t) { var p = from + dir * t; return p.Y - (float)ground(p.X, p.Z); }
        if (Above(0) <= 0) return false;
        float lo = 0, hi = 0.5f;
        while (Above(hi) > 0) { lo = hi; hi += 0.5f; if (hi > 2000) return false; }
        for (int i = 0; i < 30; i++) { float mid = (lo + hi) / 2; if (Above(mid) > 0) lo = mid; else hi = mid; }
        point = from + dir * hi;
        return true;
    }

    private Vector3 Snap(Vector3 p) => _gridSnap
        ? new Vector3(Mathf.Snapped(p.X, GridStep), Mathf.Snapped(p.Y, GridStep * 0.2f), Mathf.Snapped(p.Z, GridStep))
        : p;

    // ------------------------------------------------------------- select

    private void Select(string? id)
    {
        _selectedId = id;
        RefreshHighlights();
        RebuildInspector();
        _status.Text = id is null ? "" : $"Selected {id}";
    }

    private void OnLeftDown(Vector2 screen, bool shift)
    {
        if (_placingPaletteId is { } paletteId)
        {
            PlaceGhost(paletteId, shift);
            return;
        }
        if (_link is { } link)
        {
            if (PickPart(screen, out _) is { } hit) PickForLink(link, hit);
            return;
        }
        if (PickPort(screen) is { } port)
        {
            if (_connectFrom is { } from && (from.Part != port.Part || from.Port != port.Port))
            {
                RunCommand($"(snap {from.Part}.{from.Port} {port.Part}.{port.Port})");
                CancelConnect();
            }
            else if (_connectFrom is not null) CancelConnect();
            else
            {
                _connectFrom = (port.Part, port.Port, port.At);
                _status.Text = $"Connecting {port.Part}'s {port.Port}: click the point to join it to, Esc to cancel";
            }
            return;
        }
        if (_connectFrom is not null) { CancelConnect(); _status.Text = ""; }
        string? id = PickPart(screen, out _);
        Select(id);
        _pressedId = id;
        _pressPos = screen;
        _dragging = false;
    }

    private void OnLeftUp()
    {
        if (_dragging && _pressedId is { } id)
        {
            var at = _session.Document.Parts[id].At;
            RunCommand($"(move {id} ({F(at.X)} {F(at.Y)} {F(at.Z)}))");
            TrySnapAllPorts(id);
            ClearSnapTargets();
        }
        _pressedId = null;
        _dragging = false;
    }

    // --------------------------------------------------------------- move

    private void BeginDrag(string id, Vector2 screen)
    {
        _dragging = true;
        var part = _session.Document.Parts[id];
        _dragStartAt = new Vector3((float)part.At.X, (float)part.At.Y, (float)part.At.Z);
        var box = BoundsOf(NodesOf(id));
        _dragBottom = box is { } b ? b.Position.Y - _dragStartAt.Y : 0;
        // keep the point grabbed under the mouse, horizontally
        _dragGrab = SurfacePoint(screen, id, out var p) ? new Vector3(p.X - _dragStartAt.X, 0, p.Z - _dragStartAt.Z) : Vector3.Zero;
    }

    /// <summary>
    /// Slides the part over whatever is under the mouse (so it stacks on
    /// what it crosses), or with Ctrl held raises and lowers it. The part's
    /// own nodes are moved directly for a smooth drag; the document gets the
    /// final position as one (move) command on release.
    /// </summary>
    private void Drag(string id, Vector2 screen, bool vertical, Vector2 relative)
    {
        var part = _session.Document.Parts[id];
        var current = new Vector3((float)part.At.X, (float)part.At.Y, (float)part.At.Z);
        Vector3 target;
        if (vertical)
        {
            float dy = -relative.Y * _distance * 0.0015f;
            target = current with { Y = Mathf.Max(0, current.Y + dy) };
        }
        else
        {
            if (!SurfacePoint(screen, id, out var p)) return;
            target = new Vector3(p.X - _dragGrab.X, p.Y - _dragBottom, p.Z - _dragGrab.Z);
        }
        target = Snap(target);
        var delta = target - current;
        if (delta == Vector3.Zero) return;
        _session.Document.Move(id, new Vec3(target.X, target.Y, target.Z));
        foreach (var n in NodesOf(id)) n.GlobalPosition += delta;
        ShowSnapTargets(id);
    }

    // -------------------------------------------------------------- place

    private void StartPlacing(string paletteId)
    {
        CancelPlacing();
        _placingPaletteId = paletteId;
        _ghost = BuildGhost(paletteId, out _ghostBottom);
        AddChild(_ghost);
        _ghost.Visible = false;
        var item = _palette.First(p => p.Id == paletteId);
        string label = item.PrimitiveKind is { } k && PartInfo.TryGetValue(k, out var info) ? info.Label : item.Label;
        _partHelp.Text = item.PrimitiveKind is { } k2 && PartInfo.TryGetValue(k2, out var info2) ? info2.Description : "";
        _status.Text = $"Placing {label}: click to place, Shift keeps placing, Esc cancels";
    }

    private void CancelPlacing()
    {
        _ghost?.QueueFree();
        _ghost = null;
        _placingPaletteId = null;
        _paletteList.DeselectAll();
    }

    /// <summary>
    /// A translucent copy of the part as it will look, drawn by a one-part
    /// MachineView where the part can stand alone, else a placeholder box.
    /// Also returns its lowest point relative to its #:at, so it can be set
    /// down on a surface whatever the part's own origin is (a tank's base, a
    /// pendulum's pivot, a block's centre).
    /// </summary>
    private Node3D BuildGhost(string paletteId, out float bottom)
    {
        var item = _palette.First(p => p.Id == paletteId);
        var spec = item.Catalogue is { } entry
            ? PartTemplates.Create(entry, "ghost", new Vec3(0, 0, 0), _material)
            : PartTemplates.Create(item.PrimitiveKind!, "ghost", new Vec3(0, 0, 0), _material);
        Node3D ghost;
        try
        {
            var def = new MachineDef { Name = "ghost", Parts = [spec], Pipes = [], Connects = [], SealedAir = [] };
            var view = new MachineView(new MachineRuntime(def, _materials), _materials) { ProcessMode = ProcessModeEnum.Disabled };
            ghost = view;
        }
        catch (Exception)
        {
            ghost = new Node3D();
            ghost.AddChild(FallbackVisual(spec));   // (FallbackVisual no longer parents its box itself)
        }
        AddChild(ghost);   // briefly, to measure it
        if (ghost is MachineView mv) mv.SetFrozen(true);
        foreach (var g in Descendants(ghost).OfType<GeometryInstance3D>())
        {
            g.Transparency = 0.55f;
            g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        bottom = BoundsOf([ghost]) is { } box ? box.Position.Y : 0;
        RemoveChild(ghost);
        return ghost;
    }

    private void MoveGhost(Vector2 screen)
    {
        if (_ghost is null) return;
        if (!SurfacePoint(screen, null, out var p)) { _ghost.Visible = false; return; }
        _ghost.Visible = true;
        _ghost.Position = Snap(new Vector3(p.X, p.Y - _ghostBottom, p.Z));
    }

    private void PlaceGhost(string paletteId, bool keepPlacing)
    {
        if (_ghost is not { Visible: true }) return;
        var item = _palette.First(p => p.Id == paletteId);
        string id = $"{paletteId.Replace('-', '_')}_{_serial++}";
        while (_session.Document.Parts.ContainsKey(id)) id = $"{paletteId.Replace('-', '_')}_{_serial++}";
        string head = item.Catalogue is { } entry ? entry.PartKind : item.PrimitiveKind!;
        string catalogueArg = item.Catalogue is not null ? $" #:catalogue {paletteId}" : "";
        var at = _ghost.Position;
        if (RunCommand($"({head} {id} #:at {Xyz(at)}{catalogueArg} #:material {_material})"))
        {
            TrySnapAllPorts(id);
            if (!keepPlacing) { CancelPlacing(); Select(id); }
        }
    }

    // -------------------------------------------------------- edit actions

    private void DeleteSelected()
    {
        if (_selectedId is { } id) RunCommand($"(remove {id})");
    }

    /// <summary>A copy of the selected part, its numbers and names carried over, set 0.5 m to the side.</summary>
    private void DuplicateSelected()
    {
        if (_selectedId is not { } id || !_session.Document.Parts.TryGetValue(id, out var part)) return;
        string baseName = part.Id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').TrimEnd('_');
        string copy = $"{baseName}_{_serial++}";
        while (_session.Document.Parts.ContainsKey(copy)) copy = $"{baseName}_{_serial++}";
        var props = string.Concat(part.Props
            .Where(p => p.Key is not ("catalogue" or "shape" or "mesh" or "volume" or "inertia-x" or "inertia-y" or "inertia-z"))
            .Where(p => p.Value is SNumber || (p.Value is SSymbol s && s.Name != "?"))
            .Select(p => $" #:{p.Key} {SExprText(p.Value)}"));
        string catalogueArg = part.Props.GetValueOrDefault("catalogue") is SSymbol c ? $" #:catalogue {c.Name}" : "";
        var at = new Vector3((float)part.At.X + 0.5f, (float)part.At.Y, (float)part.At.Z);
        if (RunCommand($"({part.Kind} {copy} #:at {Xyz(at)}{catalogueArg} #:material {part.Material}{props})")) Select(copy);
    }

    // ---------------------------------------------------------- join parts

    private void StartLink(LinkGestures.Kind kind)
    {
        CancelPlacing();
        CancelConnect();
        _linkPicks.Clear();
        _link = kind;
        _status.Text = LinkGestures.Prompt(kind, 0);
        RefreshHighlights();
    }

    private void CancelLink()
    {
        _link = null;
        _linkPicks.Clear();
        RefreshHighlights();
    }

    /// <summary>A click on a part while a Join tool is active: two-part tools finish on the second pick, axles and shared air wait for Enter.</summary>
    private void PickForLink(LinkGestures.Kind kind, string id)
    {
        if (_linkPicks.Contains(id)) { _linkPicks.Remove(id); }
        else _linkPicks.Add(id);
        RefreshHighlights();
        var (min, max) = LinkGestures.Picks(kind);
        if (max is { } m && _linkPicks.Count == m) FinishLink(kind);
        else _status.Text = LinkGestures.Prompt(kind, _linkPicks.Count);
    }

    private void FinishLink(LinkGestures.Kind kind)
    {
        try
        {
            string command = LinkGestures.Command(_session.Document, kind, _linkPicks);
            var last = _linkPicks[^1];
            _linkPicks.Clear();
            _link = null;
            RunCommand(command);
            Select(last);
        }
        catch (InvalidOperationException e)
        {
            // keep the tool active: the person can fix the picks (click a picked part again to drop it)
            _console.AddText($"error: {e.Message}\n");
            _status.Text = e.Message;
            RefreshHighlights();
        }
    }

    /// <summary>
    /// Thin lines for the links that aren't drawn as parts: a gear mesh
    /// (amber), an axle (grey), shared air (pale blue), a cylinder (orange).
    /// Ropes are drawn by the machine view itself.
    /// </summary>
    private void DrawLinks()
    {
        var doc = _session.Document;
        Vector3? Centre(string id) => BoundsOf(NodesOf(id)) is { } b ? b.GetCenter()
            : doc.Parts.TryGetValue(id, out var p) ? new Vector3((float)p.At.X, (float)p.At.Y, (float)p.At.Z) : null;
        void Line(string a, string b, Color c)
        {
            if (Centre(a) is not { } pa || Centre(b) is not { } pb || pa.DistanceTo(pb) < 0.01f) return;
            var rod = Shapes.Rod(pa, pb, 0.008f, Shapes.Mat(c));
            AddChild(rod);
            _pipeVisuals.Add(rod);
        }
        foreach (var g in doc.Meshes) Line(g.A, g.B, new Color(1f, 0.75f, 0.2f));
        foreach (var a in doc.Arbors) for (int i = 1; i < a.Parts.Count; i++) Line(a.Parts[0], a.Parts[i], new Color(0.7f, 0.7f, 0.75f));
        foreach (var a in doc.SealedAir) for (int i = 1; i < a.Tanks.Count; i++) Line(a.Tanks[0], a.Tanks[i], new Color(0.6f, 0.85f, 1f));
        foreach (var c in doc.Cylinders) Line(c.Piston, c.Boiler, new Color(1f, 0.5f, 0.2f));
    }

    // -------------------------------------------------------------- ports

    private void TrySnapAllPorts(string id)
    {
        if (!_session.Document.Parts.TryGetValue(id, out var part)) return;
        foreach (var port in part.Ports)
        {
            var hit = _session.Document.NearestCompatiblePort(id, port.Name, SnapRadius);
            if (hit is null) continue;
            RunCommand($"(snap {id}.{port.Name} {hit.Value.Part.Id}.{hit.Value.Port.Name})");
        }
    }

    private void ShowSnapTargets(string id)
    {
        ClearSnapTargets();
        if (!_session.Document.Parts.TryGetValue(id, out var part)) return;
        foreach (var port in part.Ports)
        {
            var hit = _session.Document.NearestCompatiblePort(id, port.Name, SnapRadius);
            if (hit is null) continue;
            var world = EditorDocument.PortWorldPosition(hit.Value.Part, hit.Value.Port);
            var marker = Shapes.Sphere(0.06f, Shapes.Mat(new Color(0.2f, 1f, 0.3f)));
            marker.Position = new Vector3((float)world.X, (float)world.Y, (float)world.Z);
            AddChild(marker);
            _portMarkers.Add(marker);
        }
    }

    private void ClearSnapTargets()
    {
        foreach (var m in _portMarkers) m.QueueFree();
        _portMarkers.Clear();
    }

    // ----------------------------------------------------------- fallback

    private void DrawPipes()
    {
        var doc = _session.Document;
        foreach (var (_, pipe) in doc.Pipes)
        {
            if (!doc.Parts.TryGetValue(pipe.From.Part, out var fromPart) || !doc.Parts.TryGetValue(pipe.To.Part, out var toPart)) continue;
            var fromPort = fromPart.Ports.FirstOrDefault(p => p.Name == pipe.From.Port);
            var toPort = toPart.Ports.FirstOrDefault(p => p.Name == pipe.To.Port);
            if (fromPort is null || toPort is null) continue;
            var a = EditorDocument.PortWorldPosition(fromPart, fromPort);
            var b = EditorDocument.PortWorldPosition(toPart, toPort);
            var rod = Shapes.Rod(new Vector3((float)a.X, (float)a.Y, (float)a.Z), new Vector3((float)b.X, (float)b.Y, (float)b.Z), 0.02f, Shapes.Mat(Shapes.Water));
            AddChild(rod);
            _pipeVisuals.Add(rod);
        }
    }

    /// <summary>
    /// A plain shape for a part the real view can't draw yet (a fire not yet
    /// given a pot, a wheel with no steam), sized and placed as the real part
    /// will be: most parts stand on their #:at, but a wheel or steam ball is
    /// centred on its axle there. Ghosts and stacking use this footprint, so
    /// a pot set on an unfinished fire lands where it will really sit.
    /// </summary>
    private MeshInstance3D FallbackVisual(PartSpec part)
    {
        var mat = Shapes.Mat(Shapes.ColorFor(part.Material));
        float r = (float)part.Number("radius", 0.15);
        (Vector3 size, bool centred) = part.Kind switch
        {
            "tank" => (new Vector3((float)Math.Sqrt(part.Number("area", 0.05)), (float)part.Number("height", 0.3), (float)Math.Sqrt(part.Number("area", 0.05))), false),
            "boiler" => (new Vector3(r * 2, (float)part.Number("height", 0.3), r * 2), false),
            "post" => (new Vector3((float)part.Number("size-x", 0.2), (float)part.Number("size-y", 1), (float)part.Number("size-z", 0.2)), false),
            "block" => (Vector3.One * (float)part.Number("size", 0.1), true),
            "hearth" => (new Vector3(0.3f, 0.08f, 0.3f), false),
            "jetwheel" => (new Vector3(r * 2, r * 2, (float)part.Number("width", 0.03) + 0.06f), true),
            "rotor" => (Vector3.One * (float)(part.Number("radius", 0.06) + part.Number("arm", 0.08)) * 2, true),
            _ => (new Vector3(0.3f, 0.3f, 0.3f), false),
        };
        var box = Shapes.Box(size, mat);
        box.Position = new Vector3((float)part.At.X, (float)part.At.Y + (centred ? 0 : size.Y / 2), (float)part.At.Z);
        return box;
    }

    // ------------------------------------------------------------- hooks

    /// <summary>The machine currently on the bench, for "Run this machine".</summary>
    public MachineDef CurrentMachineDef() => _session.Document.ToMachineDef();

    /// <summary>
    /// Drives the editor through the console command path instead of the
    /// mouse: places two tanks and snaps a pipe between them. Used by
    /// HEROIC_EDITOR_AUTOBUILD=1 so a headless run can screenshot and save
    /// a real editor session.
    /// </summary>
    public void AutoBuildDemo()
    {
        RunCommand("(tank high #:at (0 1.0 0) #:area 1.0 #:height 1.0 #:water 0.3)");
        RunCommand("(tank low #:at (1.5 0.0 0) #:area 1.0 #:height 1.0)");
        RunCommand("(move low (0.05 0.02 0))"); // under "high", close enough for the outlet/inlet ports to snap
        RunCommand("(snap high.outlet low.inlet)");
        RunCommand("(check)");
        FrameAll();
    }
}
