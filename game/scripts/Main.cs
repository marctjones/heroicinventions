using Godot;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

public enum Mode { Build, Run }

/// <summary>
/// Demo workshop: an aeolipile, Heron's fountain, and material blocks.
/// Build mode freezes everything (the part editor will live here);
/// Run mode steps the sim core and lets Jolt move the rigid bodies.
///
/// Keys: Space = Build/Run, F = fire on/off, T = time 1×/10×, R = reset.
/// </summary>
public partial class Main : Node3D
{
    private Mode _mode = Mode.Build;
    private double _timeScale = 1;

    private AeolipileView _aeolipile = null!;
    private HeronsFountainView _fountain = null!;
    private readonly List<MaterialBlock> _blocks = [];
    private Label _hud = null!;

    public override void _Ready()
    {
        BuildEnvironment();

        _aeolipile = new AeolipileView { Position = new Vector3(-0.6f, 0, 0) };
        AddChild(_aeolipile);

        _fountain = new HeronsFountainView { Position = new Vector3(0.6f, 0, 0) };
        AddChild(_fountain);

        var materials = MaterialLibrary.LoadDefault();
        var samples = new (string id, Color color)[]
        {
            ("cedar", new Color(0.76f, 0.52f, 0.36f)),
            ("oak", new Color(0.55f, 0.38f, 0.22f)),
            ("granite", Shapes.Stone),
            ("bronze", Shapes.Bronze),
        };
        for (int i = 0; i < samples.Length; i++)
        {
            var block = new MaterialBlock(materials[samples[i].id], 0.15f, samples[i].color)
            {
                Position = new Vector3(-1.6f + i * 0.25f, 1.2f + i * 0.2f, 0.6f),
                Freeze = true,
            };
            AddChild(block);
            _blocks.Add(block);
        }

        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Label { Position = new Vector2(16, 16) };
        _hud.AddThemeFontSizeOverride("font_size", 15);
        layer.AddChild(_hud);
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

        var floor = new StaticBody3D();
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8, 0.1f, 8) } });
        var slab = Shapes.Box(new Vector3(8, 0.1f, 8), Shapes.Mat(Shapes.Stone));
        floor.AddChild(slab);
        floor.Position = new Vector3(0, -0.05f, 0);
        AddChild(floor);

        var camera = new Camera3D { Position = new Vector3(0, 1.3f, 3.0f) };
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
                foreach (var b in _blocks) b.Freeze = _mode == Mode.Build;
                break;
            case Key.F:
                _aeolipile.ToggleFire();
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
            // The sim core runs faster than real time when asked; Jolt stays at 1×.
            double dt = delta * _timeScale;
            _aeolipile.Simulate(dt);
            _fountain.Simulate(dt);
        }

        _hud.Text =
            $"{(_mode == Mode.Build ? "BUILD (paused)" : "RUN")}   time ×{_timeScale}\n" +
            $"{_aeolipile.Status}\n{_fountain.Status}\n" +
            string.Join("   ", _blocks.Select(b => $"{b.Material.Name} {b.Mass:F1} kg")) + "\n" +
            "Space build/run · F fire · T time ×10 · R reset";
    }
}
