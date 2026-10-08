using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Doors and air pumps (issue #41), seen. A door is a leaf as big as its
/// opening (a square of √area) swinging on a vertical hinge as far as it is
/// open; a small one reads as a valve. Gas rushing through shows as a plume
/// on the low-pressure side, as strong as the flow. An air pump is a squat
/// bronze drum whose flywheel turns while it runs.
/// </summary>
public partial class MachineView
{
    private readonly List<(Door Door, Node3D Hinge, GpuParticles3D Rush, double Full)> _doorViews = [];
    private readonly List<(GasPump Pump, Node3D Wheel, Label3D Label)> _gasPumpViews = [];

    private void BuildDoors()
    {
        foreach (var (id, door) in Runtime.Doors)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            float side = Mathf.Sqrt((float)door.Area);
            float w = Mathf.Min(side, 1.2f), h = (float)door.Area / w;      // a door is taller than wide
            var hinge = new Node3D { Position = V(part.At) + new Vector3(0, 0, -w / 2) };
            var leaf = Shapes.Box(new Vector3(0.05f, h, w), Surface(part.Material));
            leaf.Position = new Vector3(0, h / 2, w / 2);
            hinge.AddChild(leaf);
            AddChild(hinge);
            AddLabel(id, V(part.At) + new Vector3(0, h + 0.1f, 0));
            var rush = SteamCloud(V(part.At) + new Vector3(0, h / 2, 0), amount: 60, radius: Mathf.Min(0.3f, w / 3), lifetime: 0.8f);
            double full = Enclosure.OrificeFlow(door.Cd, door.Area, Math.Max(door.A.Pressure, door.B.Pressure), 293, Math.Min(door.A.Pressure, door.B.Pressure), 1.4, 287);
            _doorViews.Add((door, hinge, rush, Math.Max(1e-6, full)));
        }
        foreach (var (id, pump) in Runtime.GasPumps)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var drum = Shapes.Cylinder(0.18f, 0.3f, Surface(part.Material));
            drum.Position = V(part.At) + new Vector3(0, 0.15f, 0);
            AddChild(drum);
            var wheel = new Node3D { Position = V(part.At) + new Vector3(0, 0.15f, 0.2f) };
            var rim = Shapes.Box(new Vector3(0.3f, 0.04f, 0.02f), Surface("iron"));
            wheel.AddChild(rim);
            AddChild(wheel);
            var label = new Label3D
            {
                Position = V(part.At) + new Vector3(0, 0.55f, 0), FontSize = 24, OutlineSize = 6, PixelSize = 0.004f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            };
            AddChild(label);
            _gasPumpViews.Add((pump, wheel, label));
        }
    }

    private void DrawDoors()
    {
        foreach (var (door, hinge, rush, full) in _doorViews)
        {
            hinge.RotationDegrees = new Vector3(0, 90 * (float)door.Open, 0);
            double flow = Math.Abs(door.Flow);
            rush.Emitting = flow > 1e-6;
            if (rush.Emitting)
            {
                rush.AmountRatio = (float)Math.Clamp(flow / full, 0.1, 1);
                if (rush.ProcessMaterial is ParticleProcessMaterial pm)   // the one jet rule (Skins.JetSpeed)
                {
                    float speed = Skins.JetSpeed(door.A.Pressure - door.B.Pressure);
                    pm.InitialVelocityMin = speed * 0.8f; pm.InitialVelocityMax = speed * 1.2f;
                }
            }
        }
        foreach (var (pump, wheel, label) in _gasPumpViews)
        {
            if (pump.Running) wheel.RotateZ((float)(pump.Speed * 40 * GetPhysicsProcessDeltaTime()));
            label.Text = $"{pump.Name}: {(pump.Running ? $"{pump.Flow * 1000:0.##} g/s, {pump.Power:0} W" : "stopped")}, {pump.Work / 1000:0.#} kJ";
        }
    }
}
