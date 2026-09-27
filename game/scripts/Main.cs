using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>How the camera frames one machine so it fills the screen.</summary>
public readonly record struct CameraProfile(Vector3 Eye, Vector3 LookAt, float FovDegrees);

/// <summary>
/// A one-machine-at-a-time viewer. Pick a machine from the menu; it loads
/// alone, framed close by its own camera profile, and starts running
/// immediately. Restart reloads it fresh from its .machine file — a true
/// reset, not a rewind. Speed buttons control simulated time directly,
/// which matters more than screen size for something like the aeolipile's
/// rotor: it settles near 4,000 rpm, faster than any camera can resolve
/// at 1×, so slow motion is what actually makes it visible spinning up.
///
/// The headline HUD is deliberately small — total energy and its
/// kinetic/potential mix, a single speed number, and whichever of
/// efficiency or "energy retained" applies (see MachineView.EnergyHud) —
/// with the full per-part numbers available behind a Details toggle
/// rather than always on screen.
///
/// Two environment variables help headless recording and remote control:
///   HEROIC_AUTORUN=1              start running immediately (no click)
///   HEROIC_AUTOSELECT=&lt;name&gt;       select a machine at startup
///   HEROIC_LIVE_LINK=1            open the Racket live-link TCP server
/// </summary>
public partial class Main : Node3D
{
    private const string MachinesDir = "res://machines";

    private static readonly CameraProfile MenuCamera = new(new Vector3(0, 1.4f, 3.0f), new Vector3(0, 0.6f, 0), 60);
    private static readonly Dictionary<string, CameraProfile> Profiles = new()
    {
        ["aeolipile"] = new(new Vector3(0, 0.55f, 0.85f), new Vector3(0, 0.32f, 0), 42),
        ["herons-fountain"] = new(new Vector3(0, 0.95f, 1.35f), new Vector3(0, 0.55f, 0), 42),
        ["material-samples"] = new(new Vector3(0, 1.15f, 2.0f), new Vector3(0, 0.7f, 0), 45),
        ["pendulum-demo"] = new(new Vector3(0, 0.7f, 1.3f), new Vector3(0, 0.5f, 0), 42),
        ["lever-demo"] = new(new Vector3(0, 0.75f, 1.5f), new Vector3(0, 0.55f, 0), 42),
        ["inclined-plane-demo"] = new(new Vector3(0, 0.9f, 1.9f), new Vector3(0, 0.4f, -0.4f), 45),
        ["newtons-cradle"] = new(new Vector3(0, 0.75f, 1.0f), new Vector3(0, 0.6f, 0), 38),
        ["trebuchet"] = new(new Vector3(0.2f, 1.3f, 3.2f), new Vector3(0.3f, 0.9f, 0), 55),
    };
    private static readonly (double Scale, string Label)[] Speeds =
        [(0.1, "0.1×"), (0.25, "0.25×"), (1, "1×"), (5, "5×"), (20, "20×")];
    private static readonly Dictionary<string, double> DefaultSpeeds = new() { ["aeolipile"] = 5 };
    private static readonly (int Width, int Height, string Label)[] WindowSizes =
        [(1152, 720, "Small"), (1600, 1000, "Medium"), (1920, 1200, "Large")];

    private static readonly Dictionary<string, string> DisplayNames = new()
    {
        ["aeolipile"] = "Aeolipile",
        ["herons-fountain"] = "Heron's Fountain",
        ["material-samples"] = "Material Samples",
        ["pendulum-demo"] = "Pendulum",
        ["lever-demo"] = "Lever / See-Saw",
        ["inclined-plane-demo"] = "Inclined Plane",
        ["newtons-cradle"] = "Newton's Cradle",
        ["trebuchet"] = "Trebuchet",
    };

    private MaterialLibrary _materials = null!;
    private readonly SortedDictionary<string, string> _machineFiles = []; // name → res:// path
    private readonly Dictionary<string, MachineView> _byName = [];        // just the current one, for the live link
    private MachineView? _current;
    private string? _currentName;
    private bool _running;
    private double _timeScale = 1;
    private bool _showDetails;
    private double? _quitAfterSimSeconds; // for scripted recording: exact, unlike --quit-after under load
    private readonly bool _debugPhysics = OS.GetEnvironment("HEROIC_DEBUG_PHYSICS") == "1";
    private double _debugTimer;

    private Camera3D _camera = null!;
    private Label _hud = null!;
    private Button _restartButton = null!;
    private Button _runButton = null!;
    private Button _menuButton = null!;
    private Button _detailsButton = null!;
    private readonly List<Button> _speedButtons = [];

    public override void _Ready()
    {
        _materials = MaterialLibrary.LoadDefault();
        BuildEnvironment();
        ScanMachineFiles();
        BuildUI();
        ApplyCamera(MenuCamera);

        if (OS.GetEnvironment(LiveLinkServer.EnableEnvVar) == "1")
            AddChild(new LiveLinkServer(_byName, running => SetRunning(running)));

        string autoSelect = OS.GetEnvironment("HEROIC_AUTOSELECT");
        if (!string.IsNullOrEmpty(autoSelect) && _machineFiles.ContainsKey(autoSelect))
            SelectMachine(autoSelect);
        else if (OS.GetEnvironment("HEROIC_AUTORUN") == "1" && _machineFiles.Count > 0)
            SelectMachine(_machineFiles.Keys.First());

        if (double.TryParse(OS.GetEnvironment("HEROIC_SPEED"), System.Globalization.CultureInfo.InvariantCulture, out double speed))
            SetSpeed(speed);

        if (double.TryParse(OS.GetEnvironment("HEROIC_QUIT_AFTER_SIM_SECONDS"), System.Globalization.CultureInfo.InvariantCulture, out double quitAfter))
            _quitAfterSimSeconds = quitAfter;
    }

    private void ScanMachineFiles()
    {
        foreach (string file in DirAccess.GetFilesAt(MachinesDir).Where(f => f.EndsWith(".machine")).Order())
        {
            string path = $"{MachinesDir}/{file}";
            try
            {
                var def = MachineDef.Parse(Godot.FileAccess.GetFileAsString(path));
                _machineFiles[def.Name] = path;
            }
            catch (Exception e) when (e is MachineFormatException or FormatException)
            {
                GD.PushError($"{path}: {e.Message}");
            }
        }
    }

    // ------------------------------------------------------------------ UI

    private void BuildUI()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        var panel = new PanelContainer { Position = new Vector2(20, 20) };
        layer.AddChild(panel);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) };
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);

        var title = new Label { Text = "Heroic Inventions" };
        title.AddThemeFontSizeOverride("font_size", 22);
        col.AddChild(title);

        col.AddChild(new HSeparator());

        foreach (var (name, _) in _machineFiles)
        {
            var button = BigButton(DisplayNames.GetValueOrDefault(name, name));
            button.Pressed += () => SelectMachine(name);
            col.AddChild(button);
        }

        col.AddChild(new HSeparator());

        _runButton = BigButton("Pause");
        _runButton.Disabled = true;
        _runButton.Pressed += () => SetRunning(!_running);
        col.AddChild(_runButton);

        _restartButton = BigButton("Restart");
        _restartButton.Disabled = true;
        _restartButton.Pressed += RestartCurrent;
        col.AddChild(_restartButton);

        _detailsButton = BigButton("Show details");
        _detailsButton.Disabled = true;
        _detailsButton.Pressed += () =>
        {
            _showDetails = !_showDetails;
            _detailsButton.Text = _showDetails ? "Hide details" : "Show details";
        };
        col.AddChild(_detailsButton);

        _menuButton = BigButton("Back to menu");
        _menuButton.Disabled = true;
        _menuButton.Pressed += DeselectMachine;
        col.AddChild(_menuButton);

        col.AddChild(new HSeparator());
        col.AddChild(new Label { Text = "Speed" });
        var speedRow = new HBoxContainer();
        col.AddChild(speedRow);
        foreach (var (scale, label) in Speeds)
        {
            var b = new Button { Text = label, ToggleMode = true, ButtonPressed = scale == 1 };
            b.Pressed += () => SetSpeed(scale);
            speedRow.AddChild(b);
            _speedButtons.Add(b);
        }

        col.AddChild(new HSeparator());
        col.AddChild(new Label { Text = "Window size" });
        var sizeRow = new HBoxContainer();
        col.AddChild(sizeRow);
        foreach (var (w, h, label) in WindowSizes)
        {
            var b = new Button { Text = label };
            b.Pressed += () => SetWindowSize(w, h);
            sizeRow.AddChild(b);
        }
        var fullscreenButton = new Button { Text = "Fullscreen", ToggleMode = true };
        fullscreenButton.Pressed += () => ToggleFullscreen(fullscreenButton.ButtonPressed);
        col.AddChild(fullscreenButton);

        // Fixed to the bottom-left corner. The stretch mode set in
        // project.godot (canvas_items) scales this whole canvas together
        // with the 3D view, so a fixed position here stays in the right
        // place relative to the scene at every window size.
        _hud = new Label { Position = new Vector2(20, 780), Text = "Choose a machine to run it." };
        _hud.AddThemeFontSizeOverride("font_size", 20);
        layer.AddChild(_hud);
    }

    private static Button BigButton(string text)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40) };
        b.AddThemeFontSizeOverride("font_size", 18);
        return b;
    }

    private void SetWindowSize(int width, int height)
    {
        var window = GetWindow();
        window.Mode = Window.ModeEnum.Windowed;
        window.Size = new Vector2I(width, height);
    }

    private void ToggleFullscreen(bool on) =>
        GetWindow().Mode = on ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;

    // --------------------------------------------------------------- state

    private void SelectMachine(string name)
    {
        _current?.QueueFree();
        _byName.Clear();

        var def = MachineDef.Parse(Godot.FileAccess.GetFileAsString(_machineFiles[name]));
        var view = new MachineView(new MachineRuntime(def, _materials), _materials) { Position = Vector3.Zero };
        AddChild(view);
        _current = view;
        _currentName = name;
        _byName[name] = view;

        ApplyCamera(Profiles.GetValueOrDefault(name, MenuCamera with { Eye = new Vector3(0, 1, 2) }));
        SetRunning(true);
        // A real aeolipile doesn't spin until its water boils (~30s of
        // simulated time for 0.3kg at 3kW) — accurate, but a bad first
        // impression on a freshly clicked button, so it defaults faster.
        // Gravity-driven demos (pendulum, lever, ramp) act immediately and
        // are easiest to watch at 1×; the speed row overrides either way.
        SetSpeed(DefaultSpeeds.GetValueOrDefault(name, 1));

        _restartButton.Disabled = false;
        _menuButton.Disabled = false;
        _runButton.Disabled = false;
        _detailsButton.Disabled = false;
    }

    private void RestartCurrent()
    {
        if (_currentName is { } name) SelectMachine(name);
    }

    private void DeselectMachine()
    {
        _current?.QueueFree();
        _current = null;
        _currentName = null;
        _byName.Clear();
        SetRunning(false);
        ApplyCamera(MenuCamera);
        _hud.Text = "Choose a machine to run it.";

        _restartButton.Disabled = true;
        _menuButton.Disabled = true;
        _runButton.Disabled = true;
        _detailsButton.Disabled = true;
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _current?.SetFrozen(!running);
        _runButton.Text = running ? "Pause" : "Run";
    }

    private void SetSpeed(double scale)
    {
        _timeScale = scale;
        foreach (var b in _speedButtons) b.SetPressedNoSignal(Mathf.IsEqualApprox((float)scale, ParseSpeed(b.Text)));
    }

    private static float ParseSpeed(string label) => float.Parse(label.TrimEnd('×'), System.Globalization.CultureInfo.InvariantCulture);

    private void ApplyCamera(CameraProfile profile)
    {
        _camera.Position = profile.Eye;
        _camera.LookAt(profile.LookAt, Vector3.Up);
        _camera.Fov = profile.FovDegrees;
    }

    // --------------------------------------------------------------- scene

    private void BuildEnvironment()
    {
        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Sky, Sky = sky },
        });

        var sun = new DirectionalLight3D { ShadowEnabled = true };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-50, 30, 0);

        var floor = new StaticBody3D { Position = new Vector3(0, -0.05f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8, 0.1f, 8) } });
        floor.AddChild(Shapes.Box(new Vector3(8, 0.1f, 8), Shapes.Mat(Shapes.Stone)));
        AddChild(floor);

        _camera = new Camera3D();
        AddChild(_camera);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.Space when _current is not null:
                SetRunning(!_running);
                break;
            case Key.F:
                _current?.ToggleFire();
                break;
            case Key.R when _current is not null:
                RestartCurrent();
                break;
            case Key.Escape when _current is not null:
                DeselectMachine();
                break;
            case Key.D when _current is not null:
                _detailsButton.EmitSignal(BaseButton.SignalName.Pressed);
                break;
            case >= Key.Key1 and <= Key.Key9:
            {
                int index = (int)(key.Keycode - Key.Key1);
                var names = _machineFiles.Keys.ToList();
                if (index < names.Count) SelectMachine(names[index]);
                break;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_running && _current is not null)
            _current.Simulate(delta * _timeScale);

        if (_quitAfterSimSeconds is { } limit && _current is not null && _current.Runtime.Time >= limit)
            GetTree().Quit();

        if (_debugPhysics && _current is not null && (_debugTimer += delta) >= 0.5)
        {
            _debugTimer = 0;
            GD.Print($"[{_current.Runtime.Time:F2}s] {_current.DebugState()}\n{_current.EnergyHud()}");
        }

        _hud.Text = _current is null
            ? "Choose a machine to run it."
            : $"{(_running ? "RUNNING" : "PAUSED")}   time ×{_timeScale:0.##}\n" +
              $"{_current.EnergyHud()}\n{BoilingHint()}" +
              (_showDetails ? $"{_current.Details}\n" : "") +
              "Space pause/run · F fire · R restart · D details · Esc menu · 1-9 pick a machine";
    }

    /// <summary>
    /// Nothing spins until a boiler's water reaches 100°C — real physics,
    /// but easy to mistake for "it's broken" while it's still heating.
    /// </summary>
    private string BoilingHint()
    {
        if (_current is null) return "";
        var coldBoilers = _current.Runtime.Boilers.Values.Where(b => b.Temperature < 99).ToList();
        if (coldBoilers.Count == 0 || _current.Runtime.Rotors.Count == 0) return "";
        double hottest = coldBoilers.Max(b => b.Temperature);
        return $"heating — {hottest:F0}°C of 100°C, then it starts spinning (try a higher speed above)\n";
    }
}
