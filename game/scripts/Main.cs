using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

public enum Mode { Build, Run }

/// <summary>
/// The workshop. Loads every machine in res://machines (written in
/// #lang heroic and compiled by racket/build.rkt) and lays them out in a row.
/// Build mode freezes everything; Run mode steps each machine's solvers
/// and lets Jolt move the rigid bodies.
///
/// Keys: Space = Build/Run, F = fires on/off, T = time 1×/10×,
/// R = reload the machine files from disk.
/// </summary>
public partial class Main : Node3D
{
    private const string MachinesDir = "res://machines";
    private const float Spacing = 1.2f;

    private Mode _mode = Mode.Build;
    private double _timeScale = 1;

    private readonly List<MachineView> _machines = [];
    private readonly List<string> _errors = [];
    private Label _hud = null!;

    public override void _Ready()
    {
        BuildEnvironment();
        LoadMachines();

        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Label { Position = new Vector2(16, 16) };
        _hud.AddThemeFontSizeOverride("font_size", 15);
        layer.AddChild(_hud);
    }

    private void LoadMachines()
    {
        var materials = MaterialLibrary.LoadDefault();
        var files = DirAccess.GetFilesAt(MachinesDir).Where(f => f.EndsWith(".machine")).Order().ToList();

        for (int i = 0; i < files.Count; i++)
        {
            string path = $"{MachinesDir}/{files[i]}";
            try
            {
                var def = MachineDef.Parse(Godot.FileAccess.GetFileAsString(path));
                var view = new MachineView(new MachineRuntime(def, materials), materials)
                {
                    Position = new Vector3((i - (files.Count - 1) / 2f) * Spacing, 0, 0),
                };
                AddChild(view);
                _machines.Add(view);
            }
            catch (Exception e) when (e is MachineFormatException or FormatException)
            {
                // A broken machine file shouldn't stop the others from loading.
                _errors.Add($"{files[i]}: {e.Message}");
                GD.PushError($"{path}: {e.Message}");
            }
        }
    }

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

        var camera = new Camera3D { Position = new Vector3(0, 1.3f, 3.2f) };
        AddChild(camera);
        camera.LookAt(new Vector3(0, 0.6f, 0));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.Space:
                _mode = _mode == Mode.Build ? Mode.Run : Mode.Build;
                foreach (var m in _machines) m.SetFrozen(_mode == Mode.Build);
                break;
            case Key.F:
                foreach (var m in _machines) m.ToggleFire();
                break;
            case Key.T:
                _timeScale = _timeScale == 1 ? 10 : 1;
                break;
            case Key.R:
                GetTree().ReloadCurrentScene();
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_mode == Mode.Run)
        {
            // The machine solvers can run faster than real time; Jolt stays at 1×.
            double dt = delta * _timeScale;
            foreach (var m in _machines) m.Simulate(dt);
        }

        _hud.Text =
            $"{(_mode == Mode.Build ? "BUILD (paused)" : "RUN")}   time ×{_timeScale}\n" +
            string.Join("\n", _machines.Select(m => m.Status)) + "\n" +
            string.Join("", _errors.Select(e => $"⚠ {e}\n")) +
            "Space build/run · F fire · T time ×10 · R reload machines";
    }
}
