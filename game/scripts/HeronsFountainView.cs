using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Draws Heron's fountain as three glass vessels whose water levels come
/// from the fluid network, plus a jet whose height is the nozzle's head.
/// </summary>
public partial class HeronsFountainView : Node3D
{
    public HeronsFountain Model { get; } = new();

    private readonly List<(Tank tank, MeshInstance3D water)> _vessels = [];
    private MeshInstance3D _jet = null!;

    public override void _Ready()
    {
        var glass = Shapes.Mat(new Color(0.85f, 0.92f, 0.95f), roughness: 0.1f, alpha: 0.18f);
        var water = Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.8f);

        foreach (var tank in new[] { Model.Basin, Model.Supply, Model.Receiver })
        {
            float side = Mathf.Sqrt((float)tank.Area);
            var shell = Shapes.Box(new Vector3(side, (float)tank.Height, side), glass);
            shell.Position = new Vector3(0, (float)(tank.BaseElevation + tank.Height / 2), 0);
            AddChild(shell);

            var fill = Shapes.Box(new Vector3(side * 0.96f, 1, side * 0.96f), water);
            AddChild(fill);
            _vessels.Add((tank, fill));
        }

        _jet = Shapes.Cylinder(0.006f, 1, water);
        AddChild(_jet);
        UpdateVisuals();
    }

    public void Simulate(double dt)
    {
        Model.Step(dt);
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        foreach (var (tank, fill) in _vessels)
        {
            float level = Mathf.Max((float)tank.Level, 0.001f);
            fill.Scale = new Vector3(1, level, 1);
            fill.Position = new Vector3(0, (float)tank.BaseElevation + level / 2, 0);
        }

        float jet = Model.Nozzle.Flow > 0 ? (float)Model.Nozzle.JetHeight : 0;
        _jet.Visible = jet > 0.002f;
        _jet.Scale = new Vector3(1, Mathf.Max(jet, 0.001f), 1);
        _jet.Position = new Vector3(0, (float)Model.Nozzle.ToPortElevation + jet / 2, 0);
    }

    public string Status =>
        $"Fountain    air {Model.Air.GaugePressure / 1000,5:F2} kPa   " +
        $"jet {Model.Nozzle.JetHeight * 100,5:F1} cm   " +
        $"basin {Model.Basin.WaterVolume * 1000,4:F1} L   " +
        $"supply {Model.Supply.WaterVolume * 1000,4:F1} L   " +
        $"receiver {Model.Receiver.WaterVolume * 1000,4:F1} L";
}
