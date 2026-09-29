using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// The in-game build-mode editor (GitHub issue #4, milestone M5): a part
/// palette fed from the M4 catalogue, port snapping, a material picker, and
/// save/load of .machine files. Everything that decides what a design
/// means — which parts exist, whether two ports can join, what the saved
/// file looks like, undo/redo — lives in
/// HeroicInventions.Sim.Editor.BuildSession, tested headlessly with no
/// Godot involved (see tests/HeroicInventions.Sim.Tests/BuildSessionTests.cs).
///
/// This class is a thin skin over that session, in the literal sense that
/// it has *no separate editing path*: a mouse drag that places a part or
/// snaps two ports together builds the exact same command string the text
/// console at the bottom of the palette accepts, and calls
/// <see cref="BuildSession.Execute"/> with it. Anything the mouse can do,
/// typing the printed command into the console reproduces exactly, and
/// every command is echoed there so a build session doubles as a readable
/// log of the design.
///
/// Placement and dragging work on the ground plane (world Y = 0): the
/// mouse ray is intersected with that plane, so a part's position is set by
/// (x, z) with Y taken from wherever it already sits (a tank is not floated
/// by dragging it sideways). A dragged part's ports are shown as green
/// spheres over every compatible port within <see cref="SnapRadius"/>;
/// releasing the mouse issues a (snap a.port b.port) for each one found.
/// </summary>
public partial class BuildMode : Node3D
{
    public const float SnapRadius = 0.2f; // m

    private readonly MaterialLibrary _materials;
    private readonly List<(string Id, string Label, string? PrimitiveKind, CatalogueEntry? Catalogue)> _palette = [];
    private BuildSession _session = null!;
    private readonly Dictionary<string, Node3D> _partVisuals = [];
    private readonly Dictionary<string, MeshInstance3D> _pipeVisuals = [];
    private readonly List<MeshInstance3D> _portMarkers = [];

    private string? _pendingPaletteId;
    private string? _draggingPartId;
    private string _material = "bronze";
    private Camera3D _camera = null!;
    private RichTextLabel _console = null!;
    private LineEdit _consoleInput = null!;
    private ItemList _paletteList = null!;
    private FileDialog _saveDialog = null!, _loadDialog = null!;
    private int _serial = 1;

    public event Action? RunRequested;

    public BuildMode(MaterialLibrary materials) => _materials = materials;

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
        BuildGround();
        _camera = new Camera3D { Position = new Vector3(0, 4.0f, 5.0f) };
        AddChild(_camera);
        _camera.LookAt(new Vector3(0, 0.3f, 0), Vector3.Up);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -30, 0), LightEnergy = 1.1f });

        string autoLoad = OS.GetEnvironment("HEROIC_EDITOR_LOAD");
        if (!string.IsNullOrEmpty(autoLoad))
        {
            string absolute = autoLoad.StartsWith("res://") || autoLoad.StartsWith("user://")
                ? ProjectSettings.GlobalizePath(autoLoad) : autoLoad;
            RunConsoleCommand($"(load {System.IO.Path.GetFileNameWithoutExtension(autoLoad)})", absolute);
        }
        if (OS.GetEnvironment("HEROIC_EDITOR_AUTOBUILD") == "1") AutoBuildDemo();

        string autoSave = OS.GetEnvironment("HEROIC_EDITOR_SAVE");
        if (!string.IsNullOrEmpty(autoSave))
        {
            _session.SaveFile(autoSave);
            Log($"saved {autoSave}");
            GD.Print($"[BuildMode] saved {autoSave}");
        }
    }

    // ------------------------------------------------------------------ UI

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(280, 0) };
        panel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        layer.AddChild(panel);
        var col = new VBoxContainer();
        panel.AddChild(col);

        col.AddChild(new Label { Text = "Build Mode" });
        col.AddChild(new Label { Text = "Pick a part, click the ground to place it." });

        _paletteList = new ItemList { CustomMinimumSize = new Vector2(260, 220) };
        foreach (var item in _palette) _paletteList.AddItem(item.Label);
        _paletteList.ItemSelected += index => _pendingPaletteId = _palette[(int)index].Id;
        col.AddChild(_paletteList);

        col.AddChild(new Label { Text = "Material" });
        var materialBox = new OptionButton();
        var materialIds = _materials.All.OrderBy(m => m.Id).Select(m => m.Id).ToList();
        foreach (var mat in _materials.All.OrderBy(m => m.Id)) materialBox.AddItem(mat.Name);
        materialBox.ItemSelected += index =>
        {
            _material = materialIds[(int)index];
            if (_draggingPartId is { } id) RunConsoleCommand($"(set {id} #:material {_material})");
        };
        col.AddChild(materialBox);

        var deleteButton = new Button { Text = "Delete selected" };
        deleteButton.Pressed += () => { if (_draggingPartId is { } id) RunConsoleCommand($"(remove {id})"); };
        col.AddChild(deleteButton);

        var row = new HBoxContainer();
        var saveButton = new Button { Text = "Save…" };
        saveButton.Pressed += () => _saveDialog.PopupCentered();
        row.AddChild(saveButton);
        var loadButton = new Button { Text = "Load…" };
        loadButton.Pressed += () => _loadDialog.PopupCentered();
        row.AddChild(loadButton);
        col.AddChild(row);

        var runButton = new Button { Text = "Run this machine" };
        runButton.Pressed += () => RunRequested?.Invoke();
        col.AddChild(runButton);

        col.AddChild(new HSeparator());
        col.AddChild(new Label { Text = "Console" });
        _console = new RichTextLabel { CustomMinimumSize = new Vector2(260, 140), ScrollFollowing = true, BbcodeEnabled = false };
        col.AddChild(_console);
        _consoleInput = new LineEdit { PlaceholderText = "(tank id #:at (0 0 0) #:area 1 #:height 1)" };
        _consoleInput.TextSubmitted += text => { RunConsoleCommand(text); _consoleInput.Text = ""; };
        col.AddChild(_consoleInput);

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
        _loadDialog.FileSelected += path => RunConsoleCommand($"(load {System.IO.Path.GetFileNameWithoutExtension(path)})", path);
        layer.AddChild(_loadDialog);
    }

    private void BuildGround()
    {
        var ground = Shapes.Box(new Vector3(20, 0.02f, 20), Shapes.Mat(new Color(0.5f, 0.55f, 0.45f)));
        ground.Position = new Vector3(0, -0.01f, 0);
        AddChild(ground);
    }

    /// <summary>
    /// Runs one command through the session — the single path every
    /// mouse-driven action and the console input box both use — echoes it
    /// and its result, and re-syncs the 3D visuals from the session's
    /// document afterwards (simplest correct approach: the document is the
    /// truth, so redraw from it rather than patch each visual by hand).
    /// <paramref name="loadFrom"/> is used for a (load NAME) issued from a
    /// file dialog outside the session's own machines directory: it loads
    /// that exact file, but the console still shows the short command.
    /// </summary>
    private void RunConsoleCommand(string command, string? loadFrom = null)
    {
        _console.AddText($"> {command}\n");
        GD.Print($"[BuildMode] > {command}");
        try
        {
            string result = loadFrom is null ? _session.Execute(command) : _session.LoadFile(loadFrom);
            _console.AddText($"{result}\n");
            GD.Print($"[BuildMode] {result}");
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or MachineFormatException)
        {
            _console.AddText($"error: {e.Message}\n");
            GD.Print($"[BuildMode] error: {e.Message}");
        }
        RedrawFromSession();
    }

    private void Log(string text) => _console.AddText(text + "\n");

    // -------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed) OnMouseDown(mb.Position);
            else OnMouseUp();
        }
        else if (@event is InputEventMouseMotion mm && _draggingPartId is not null)
        {
            OnDrag(mm.Position);
        }
    }

    private bool GroundPoint(Vector2 screenPos, out Vector3 point)
    {
        var from = _camera.ProjectRayOrigin(screenPos);
        var dir = _camera.ProjectRayNormal(screenPos);
        point = default;
        if (Mathf.Abs(dir.Y) < 1e-6f) return false;
        float t = -from.Y / dir.Y;
        if (t < 0) return false;
        point = from + dir * t;
        return true;
    }

    private void OnMouseDown(Vector2 screenPos)
    {
        if (_pendingPaletteId is { } paletteId)
        {
            if (GroundPoint(screenPos, out var ground)) PlaceFromPalette(paletteId, ground);
            _pendingPaletteId = null;
            _paletteList.DeselectAll();
            return;
        }
        _draggingPartId = PickPart(screenPos);
    }

    private void OnDrag(Vector2 screenPos)
    {
        if (_draggingPartId is not { } id || !GroundPoint(screenPos, out var ground)) return;
        if (!_session.Document.Parts.TryGetValue(id, out var part)) return;
        // Moved every frame of the drag without going through the console
        // log (that would flood it); the final position is still reached
        // by ordinary (move) commands, just not logged on every pixel.
        var moved = _session.Document.Move(id, new HeroicInventions.Sim.Machines.Vec3(ground.X, part.At.Y, ground.Z));
        if (_partVisuals.TryGetValue(id, out var visual))
            visual.Position = new Vector3((float)moved.At.X, (float)moved.At.Y, (float)moved.At.Z);
        ShowSnapTargets(id);
    }

    private void OnMouseUp()
    {
        if (_draggingPartId is not { } id) { ClearSnapTargets(); return; }
        var at = _session.Document.Parts[id].At;
        RunConsoleCommand($"(move {id} ({at.X} {at.Y} {at.Z}))");
        TrySnapAllPorts(id);
        ClearSnapTargets();
        _draggingPartId = null;
    }

    private string? PickPart(Vector2 screenPos)
    {
        if (!GroundPoint(screenPos, out var ground)) return null;
        return _session.Document.Parts.Values
            .Select(p => (p.Id, Dist: new Vector2((float)(p.At.X - ground.X), (float)(p.At.Z - ground.Z)).Length()))
            .Where(p => p.Dist < 0.4f)
            .OrderBy(p => p.Dist)
            .Select(p => (string?)p.Id)
            .FirstOrDefault();
    }

    // --------------------------------------------------------- placement

    private void PlaceFromPalette(string paletteId, Vector3 at)
    {
        var item = _palette.First(p => p.Id == paletteId);
        string id = $"{paletteId.Replace('-', '_')}_{_serial++}";
        string head = item.Catalogue is { } entry ? entry.PartKind : item.PrimitiveKind!;
        string catalogueArg = item.Catalogue is not null ? $" #:catalogue {paletteId}" : "";
        RunConsoleCommand($"({head} {id} #:at ({at.X} {at.Y} {at.Z}){catalogueArg} #:material {_material})");
    }

    /// <summary>Tries to snap every port of the just-dropped part to whatever's nearby and compatible, issuing one (snap) command per hit.</summary>
    private void TrySnapAllPorts(string id)
    {
        if (!_session.Document.Parts.TryGetValue(id, out var part)) return;
        foreach (var port in part.Ports)
        {
            var hit = _session.Document.NearestCompatiblePort(id, port.Name, SnapRadius);
            if (hit is null) continue;
            RunConsoleCommand($"(snap {id}.{port.Name} {hit.Value.Part.Id}.{hit.Value.Port.Name})");
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

    // ----------------------------------------------------- redraw from doc

    /// <summary>Rebuilds every visual from the session's document — simple and always correct, since the document (not the scene) is the truth.</summary>
    private void RedrawFromSession()
    {
        var doc = _session.Document;
        foreach (string goneId in _partVisuals.Keys.Except(doc.Parts.Keys).ToList())
        {
            _partVisuals[goneId].QueueFree();
            _partVisuals.Remove(goneId);
        }
        foreach (var part in doc.Parts.Values)
        {
            if (_partVisuals.TryGetValue(part.Id, out var visual))
            {
                visual.Position = new Vector3((float)part.At.X, (float)part.At.Y, (float)part.At.Z);
                if (visual is MeshInstance3D mi) mi.MaterialOverride = Shapes.Mat(Shapes.ColorFor(part.Material));
            }
            else
            {
                AddVisual(part);
            }
        }

        foreach (var v in _pipeVisuals.Values) v.QueueFree();
        _pipeVisuals.Clear();
        foreach (var (id, pipe) in doc.Pipes)
        {
            if (!doc.Parts.TryGetValue(pipe.From.Part, out var fromPart) || !doc.Parts.TryGetValue(pipe.To.Part, out var toPart)) continue;
            var fromPort = fromPart.Ports.FirstOrDefault(p => p.Name == pipe.From.Port);
            var toPort = toPart.Ports.FirstOrDefault(p => p.Name == pipe.To.Port);
            if (fromPort is null || toPort is null) continue;
            var a = EditorDocument.PortWorldPosition(fromPart, fromPort);
            var b = EditorDocument.PortWorldPosition(toPart, toPort);
            var rod = Shapes.Rod(new Vector3((float)a.X, (float)a.Y, (float)a.Z), new Vector3((float)b.X, (float)b.Y, (float)b.Z), 0.02f, Shapes.Mat(Shapes.Water));
            AddChild(rod);
            _pipeVisuals[id] = rod;
        }
    }

    private static readonly Dictionary<string, ArrayMesh> CachedMeshes = [];

    private void AddVisual(PartSpec part)
    {
        var mat = Shapes.Mat(Shapes.ColorFor(part.Material));
        MeshInstance3D mesh = part.Kind switch
        {
            "tank" => Shapes.Box(new Vector3((float)Math.Sqrt(part.Number("area")), (float)part.Number("height"), (float)Math.Sqrt(part.Number("area"))), mat),
            "boiler" => Shapes.Cylinder((float)part.Number("radius"), (float)part.Number("height"), mat),
            "block" => Shapes.Box(Vector3.One * (float)part.Number("size"), mat),
            "pendulum" => Shapes.Sphere(0.08f, mat),
            "lever" => Shapes.Box(new Vector3((float)part.Number("length"), 0.05f, 0.2f), mat),
            "ramp" => Shapes.Box(new Vector3((float)part.Number("length"), 0.05f, (float)part.Number("width")), mat),
            "post" => Shapes.Box(new Vector3((float)part.Number("size-x"), (float)part.Number("size-y"), (float)part.Number("size-z")), mat),
            "piston" => Shapes.Cylinder((float)part.Number("bore"), (float)part.Number("stroke"), mat),
            _ => CatalogueVisual(part, mat),
        };
        mesh.Position = new Vector3((float)part.At.X, (float)part.At.Y, (float)part.At.Z);
        AddChild(mesh);
        _partVisuals[part.Id] = mesh;
    }

    /// <summary>
    /// A catalogue part's real mesh (game/meshes/catalogue/&lt;stem&gt;.glb),
    /// loaded the same way MachineView loads a machine's own generated
    /// parts (see MachineView.GeneratedMesh) — so a placed gear looks like
    /// the gear that will actually run, not a placeholder.
    /// </summary>
    private static MeshInstance3D CatalogueVisual(PartSpec part, StandardMaterial3D mat)
    {
        string path = $"res://meshes/catalogue/{part.Text("mesh")}.glb";
        if (!CachedMeshes.TryGetValue(path, out var arrayMesh))
        {
            var doc = new GltfDocument();
            var state = new GltfState();
            var err = doc.AppendFromFile(ProjectSettings.GlobalizePath(path), state);
            arrayMesh = err == Error.Ok && state.GetMeshes().Count > 0 ? state.GetMeshes()[0].Mesh.GetMesh() : null;
            if (arrayMesh is not null) CachedMeshes[path] = arrayMesh;
        }
        return arrayMesh is not null
            ? new MeshInstance3D { Mesh = arrayMesh, MaterialOverride = mat }
            : Shapes.Sphere(0.1f, mat);
    }

    /// <summary>The machine currently on the bench, for "Run this machine".</summary>
    public MachineDef CurrentMachineDef() => _session.Document.ToMachineDef();

    /// <summary>
    /// Drives the whole editor through the console command path instead of
    /// the mouse: places two tanks and snaps a pipe between them. Used by
    /// HEROIC_EDITOR_AUTOBUILD=1 so a headless run can screenshot and save
    /// a real editor session (and, chained with HEROIC_EDITOR_SAVE, prove
    /// the saved file simulates) without simulated mouse input.
    /// </summary>
    public void AutoBuildDemo()
    {
        RunConsoleCommand("(tank high #:at (0 1.0 0) #:area 1.0 #:height 1.0 #:water 0.3)");
        RunConsoleCommand("(tank low #:at (1.5 0.0 0) #:area 1.0 #:height 1.0)");
        RunConsoleCommand("(move low (0.05 0.02 0))"); // drag it under "high" close enough for the outlet/inlet ports to snap
        RunConsoleCommand("(snap high.outlet low.inlet)");
        RunConsoleCommand("(check)");
    }
}
