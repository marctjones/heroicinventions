using Godot;
using HeroicInventions.Sim.Mechanics;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Draws Hero's aeolipile and drives it from the sim core. The rotor's
/// spin is not a Jolt body: the sim computes it, and this node only shows it.
/// </summary>
public partial class AeolipileView : Node3D
{
    public Aeolipile Model { get; } = new(new Boiler(waterMassKg: 0.3, heatInputW: 3000));

    private Node3D _rotor = null!;
    private MeshInstance3D _fire = null!;
    private const float AxleHeight = 0.45f;

    public override void _Ready()
    {
        var bronze = Shapes.Mat(Shapes.Bronze, metallic: 0.8f, roughness: 0.35f);
        var copper = Shapes.Mat(Shapes.Copper, metallic: 0.7f, roughness: 0.4f);

        var boiler = Shapes.Cylinder(0.12f, 0.16f, copper);
        boiler.Position = new Vector3(0, 0.16f, 0);
        AddChild(boiler);

        _fire = Shapes.Box(new Vector3(0.22f, 0.06f, 0.22f), Shapes.Mat(new Color(1f, 0.45f, 0.1f)));
        _fire.Position = new Vector3(0, 0.03f, 0);
        AddChild(_fire);

        // Two hollow posts carry steam up to the bearings on either side of the sphere.
        foreach (float x in new[] { -0.09f, 0.09f })
        {
            var post = Shapes.Cylinder(0.008f, AxleHeight - 0.24f, bronze);
            post.Position = new Vector3(x, 0.24f + (AxleHeight - 0.24f) / 2, 0);
            AddChild(post);
        }

        _rotor = new Node3D { Position = new Vector3(0, AxleHeight, 0) };
        AddChild(_rotor);
        _rotor.AddChild(Shapes.Sphere(0.06f, bronze));

        // Nozzle arms: radial stubs along ±Y ending in tangential tips along ±Z,
        // so both jets push the sphere the same way around the X axle.
        foreach (int s in new[] { 1, -1 })
        {
            var arm = Shapes.Cylinder(0.006f, 0.03f, bronze);
            arm.Position = new Vector3(0, s * 0.075f, 0);
            _rotor.AddChild(arm);

            var tip = Shapes.Cylinder(0.006f, 0.03f, bronze);
            tip.Position = new Vector3(0, s * 0.09f, s * 0.012f);
            tip.RotationDegrees = new Vector3(90, 0, 0);
            _rotor.AddChild(tip);
        }
    }

    public void Simulate(double dt)
    {
        Model.Step(dt);
        _rotor.Rotation = new Vector3((float)Model.Angle, 0, 0);
        _fire.Visible = Model.Boiler.HeatInput > 0 && !Model.Boiler.IsDry;
    }

    public void ToggleFire() => Model.Boiler.HeatInput = Model.Boiler.HeatInput > 0 ? 0 : 3000;

    public string Status =>
        $"Aeolipile   water {Model.Boiler.Temperature,5:F1} °C   " +
        $"steam {Model.Boiler.GaugePressure / 1000,5:F1} kPa   " +
        $"{Model.Rpm,6:F0} rpm   water left {Model.Boiler.WaterMass * 1000,4:F0} g   " +
        $"fire {(Model.Boiler.HeatInput > 0 ? "on" : "off")}";
}
