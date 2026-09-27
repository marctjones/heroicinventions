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
    };
    private static readonly (double Scale, string Label)[] Speeds =
        [(0.1, "0.1×"), (0.25, "0.25×"), (1, "1×"), (5, "5×"), (20, "20×")];

    private static readonly Dictionary<string, string> DisplayNames = new()
    {
        ["aeolipile"] = "Aeolipile",
        ["herons-fountain"] = "Heron's Fountain",
        ["material-samples"] = "Material Samples",
    };

    private MaterialLibrary _materials = null!;
    private readonly SortedDictionary<string, string> _machineFiles = []; // name → res:// path
    private readonly Dictionary<string, MachineView> _byName = [];        // just the current one, for the live link
    private MachineView? _current;
    private string? _currentName;
    private bool _running;
    private double _timeScale = 1;
    private double? _quitAfterSimSeconds; // for scripted recording: exact, unlike --quit-after under load

    private Camera3D _camera = null!;
    private Label _hud = null!;
    private Button _restartButton = null!;
    private Button _runButton = null!;
    private Button _menuButton = null!;
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
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
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
            var b = new Button { Text = label, ToggleMode = true, ButtonPressed = scale == 5 };
            b.Pressed += () => SetSpeed(scale);
            speedRow.AddChild(b);
            _speedButtons.Add(b);
        }

        // Fixed to the bottom-left corner of the 1600×1000 viewport set in
        // project.godot (canvas_items stretch keeps this position correct
        // across window sizes since Godot scales the whole canvas).
        _hud = new Label { Position = new Vector2(20, 850), Text = "Choose a machine to run it." };
        _hud.AddThemeFontSizeOverride("font_size", 20);
        layer.AddChild(_hud);
    }

    private static Button BigButton(string text)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40) };
        b.AddThemeFontSizeOverride("font_size", 18);
        return b;
    }

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
        // impression on a freshly clicked button. Default to 5× so
        // something visible happens within a handful of real seconds;
        // the speed row lets you drop back to 1× or slower any time.
        SetSpeed(5);

        _restartButton.Disabled = false;
        _menuButton.Disabled = false;
        _runButton.Disabled = false;
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

        _hud.Text = _current is null
            ? "Choose a machine to run it."
            : $"{(_running ? "RUNNING" : "PAUSED")}   time ×{_timeScale:0.##}\n{_current.Status}\n{BoilingHint()}" +
              "Space pause/run · F fire · R restart · Esc menu · 1-9 pick a machine · speed buttons above";
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
