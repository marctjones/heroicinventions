using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// A pendulum hung on a bearing, swung by the sim (<see cref="Pendulum"/>)
/// rather than by Jolt, so what is drawn is what the tests check. The same
/// rod and ball as a Jolt pendulum; its pin is drawn through the frame's
/// axle. A pale ball marks where it was let go from, a dark one the last
/// point it turned at, so the run-down shows swing by swing.
/// </summary>
public partial class MachineView
{
    private readonly List<(Pendulum p, Node3D node, MeshInstance3D turned, Label3D label, Vector3 pivot, Basis yaw)> _bearingPendulums = [];

    private void BuildBearingPendulum(PartSpec part, Pendulum p)
    {
        float length = (float)p.Length;
        float bobRadius = (float)Pendulum.BobRadiusFor(p.Length);
        var surface = PartSurface(part, bobRadius * 2);
        var pivot = V(part.At);
        var yaw = YawOf(part);

        var node = new Node3D { Name = part.Id, Transform = new Transform3D(yaw * new Basis(new Vector3(0, 0, 1), (float)p.Angle), pivot) };
        AddChild(node);
        var rod = Shapes.Cylinder((float)Pendulum.RodRadius, length, surface);
        rod.Position = new Vector3(0, -length / 2, 0);
        node.AddChild(rod);
        var bob = Shapes.Sphere(bobRadius, surface);
        bob.Position = new Vector3(0, -length, 0);
        node.AddChild(bob);
        // the pin, thick enough to see, running through the frame's axle
        node.AddChild(Shapes.Rod(new Vector3(0, 0, -0.03f), new Vector3(0, 0, 0.03f), (float)p.Bearing.JournalRadius, Surface("iron")));

        Vector3 BobAt(double angle) => pivot + yaw * (new Vector3(Mathf.Sin((float)angle), -Mathf.Cos((float)angle), 0) * length + new Vector3(0, 0, bobRadius + 0.02f)); // in front of the ball, so it shows as the ball swings past
        var released = Shapes.Sphere(0.02f, Shapes.Mat(new Color(0.9f, 0.9f, 0.85f), alpha: 0.6f));
        released.Position = BobAt(p.Angle);
        AddChild(released);
        var turned = Shapes.Sphere(0.025f, Shapes.Mat(new Color(0.7f, 0.15f, 0.1f)));
        turned.Position = BobAt(p.Angle);
        AddChild(turned);

        var label = new Label3D
        {
            Position = pivot + new Vector3(0, 0.22f, 0),
            FontSize = 24,
            OutlineSize = 6,
            PixelSize = 0.0025f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
        };
        AddChild(label);
        _bearingPendulums.Add((p, node, turned, label, pivot, yaw));
        // posts clear of the whole swing, not just the ball at rest: a lone pendulum's frame isn't a cradle's
        _pendulumMounts.Add((pivot, bobRadius, length * Mathf.Sin(Mathf.Abs((float)p.Angle)) + bobRadius + 0.08f, yaw, _building ?? ""));
    }

    private void DrawBearingPendulums()
    {
        foreach (var (p, node, turned, label, pivot, yaw) in _bearingPendulums)
        {
            node.Basis = yaw * new Basis(new Vector3(0, 0, 1), (float)p.Angle);
            float at = (float)p.TurnedAt, length = (float)p.Length;
            float inFront = (yaw.Inverse() * (turned.Position - pivot)).Z;   // the marker's offset in front of the ball, in the swing's own frame
            turned.Position = pivot + yaw * (new Vector3(Mathf.Sin(at), -Mathf.Cos(at), 0) * length + new Vector3(0, 0, inFront));
            string state = p.Stopped ? "stopped" : $"swings to {p.Amplitude * 180 / Math.PI:F1}°";
            string wear = p.Bearing.Wear > 0 ? $"\npin worn {p.Bearing.Wear * 1000:F2}×10⁻³ mm³" : "";
            label.Text = $"{p.Name}\n{state}\nheat {p.Bearing.Heat:F2} J{wear}";
        }
    }

    private double BearingPendulumEnergy(out double potential)
    {
        potential = _bearingPendulums.Sum(b => b.p.Weight * (b.pivot.Y - b.p.CentreOfMass * Math.Cos(b.p.Angle)));
        return _bearingPendulums.Sum(b => b.p.KineticEnergy);
    }
}
