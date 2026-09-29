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
    private readonly Dictionary<string, List<Node3D>> _fallbackVisuals = [];   // when the machine can't be built yet
    private readonly List<MeshInstance3D> _pipeVisuals = [];
    private readonly List<MeshInstance3D> _portMarkers = [];

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
    private FileDialog _saveDialog = null!, _loadDialog = null!;

    private static readonly StandardMaterial3D SelectedOverlay = Overlay(new Color(1f, 0.62f, 0.1f, 0.35f));
    private static readonly StandardMaterial3D HoverOverlay = Overlay(new Color(1f, 1f, 1f, 0.18f));

    public event Action? RunRequested;
    public event Action? ExitRequested;

    public BuildMode(MaterialLibrary materials) => _materials = materials;
    public BuildMode() : this(MaterialLibrary.LoadDefault()) { }

    public override void _Ready()
    {
        string catalogueRes = "res://meshes/catalogue/catalogue.rktd";
        var catalogue = Godot.FileAccess.FileExists(catalogueRes)
            ? CatalogueReader.Parse(Godot.FileAccess.GetFileAsString(catalogueRes))
            : [];
        _session = new BuildSession(_materials, catalogue, ProjectSettings.GlobalizePath("user://machines"));

        foreach (string kind in PartTemplates.PrimitiveKinds)
            _palette.Add((kind, char.ToUpperInvariant(kind[0]) + kind[1..], kind, null));
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
                    int index = _palette.FindIndex(p => p.Id == w[1]);
                    _paletteList.Select(index);
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
        leftCol.AddChild(new Label { Text = "Parts: pick one, then click in the scene", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _paletteList = new ItemList { CustomMinimumSize = new Vector2(230, 260), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        foreach (var item in _palette) _paletteList.AddItem(item.Label);
        _paletteList.ItemSelected += index => StartPlacing(_palette[(int)index].Id);
        leftCol.AddChild(_paletteList);

        leftCol.AddChild(new Label { Text = "New parts are made of" });
        _materialBox = new OptionButton();
        _materialIds = _materials.All.OrderBy(m => m.Id).Select(m => m.Id).ToList();
        foreach (var mat in _materials.All.OrderBy(m => m.Id)) _materialBox.AddItem(mat.Name);
        _materialBox.Select(Math.Max(0, _materialIds.IndexOf(_material)));
        _materialBox.ItemSelected += index => _material = _materialIds[(int)index];
        leftCol.AddChild(_materialBox);

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

    // -------------------------------------------------------------- drawing

    /// <summary>
    /// Rebuilds the preview from the session's document: the real
    /// MachineView, taken out of physics so nothing falls or turns. A design
    /// that can't be built yet (a sluice with no channel named) falls back
    /// to simple placeholder shapes, with the reason shown.
    /// </summary>
    private void Redraw()
    {
        _preview?.QueueFree();
        _preview = null;
        foreach (var nodes in _fallbackVisuals.Values) foreach (var n in nodes) n.QueueFree();
        _fallbackVisuals.Clear();
        foreach (var v in _pipeVisuals) v.QueueFree();
        _pipeVisuals.Clear();

        var doc = _session.Document;
        string? problem = null;
        if (doc.Parts.Count > 0)
        {
            try
            {
                var view = new MachineView(new MachineRuntime(doc.ToMachineDef(), _materials), _materials)
                {
                    ProcessMode = ProcessModeEnum.Disabled, // out of physics: a frozen snapshot of what will run
                };
                AddChild(view);
                view.SetFrozen(true);
                _preview = view;
            }
            catch (Exception e)
            {
                problem = e.Message;
                _preview?.QueueFree();
                _preview = null;
                foreach (var part in doc.Parts.Values) _fallbackVisuals[part.Id] = [FallbackVisual(part)];
                DrawPipes();
            }
        }
        _status.Text = problem is null
            ? (_placingPaletteId is { } p ? $"Placing {p}: click to place, Shift keeps placing, Esc cancels" : _selectedId is { } s ? $"Selected {s}" : "")
            : $"Not runnable yet ({problem}); shown as placeholders";
        RefreshHighlights();
        RebuildInspector();
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
            var overlay = id == _selectedId ? SelectedOverlay : id == _hoverId ? HoverOverlay : null;
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
                row.AddChild(NumberField(n.Value, text => { if (TryNumber(text, out double v)) RunCommand($"(set {id} #:{key} {F(v)})"); }));
            else
                row.AddChild(new Label { Text = SExprText(value), TooltipText = "set in the console", Modulate = new Color(1, 1, 1, 0.6f) });
            _inspector.AddChild(row);
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
            case Key.Escape:
                if (_placingPaletteId is not null) CancelPlacing();
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
        if (Mathf.Abs(dir.Y) < 1e-6f) return false;
        float tg = -from.Y / dir.Y;
        if (tg < 0) return false;
        point = from + dir * tg;
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
        _status.Text = $"Placing {paletteId}: click to place, Shift keeps placing, Esc cancels";
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
            ghost.AddChild(FallbackVisual(spec));
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

    /// <summary>A plain shape standing on its #:at, for a part the real view can't draw yet.</summary>
    private MeshInstance3D FallbackVisual(PartSpec part)
    {
        var mat = Shapes.Mat(Shapes.ColorFor(part.Material));
        var size = part.Kind switch
        {
            "tank" => new Vector3((float)Math.Sqrt(part.Number("area", 0.05)), (float)part.Number("height", 0.3), (float)Math.Sqrt(part.Number("area", 0.05))),
            "post" => new Vector3((float)part.Number("size-x", 0.2), (float)part.Number("size-y", 1), (float)part.Number("size-z", 0.2)),
            "block" => Vector3.One * (float)part.Number("size", 0.1),
            _ => new Vector3(0.3f, 0.3f, 0.3f),
        };
        var box = Shapes.Box(size, mat);
        box.Position = new Vector3((float)part.At.X, (float)part.At.Y + size.Y / 2, (float)part.At.Z);
        AddChild(box);
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
