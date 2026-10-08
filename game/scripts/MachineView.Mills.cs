using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Millstones (issue #25): a wheel with #:grind-torque is a runner stone
/// grinding grain on its bed stone. The miller's setting of the stones is a
/// torque they resist the turn with; whatever turns it pays that, and every
/// joule so spent grinds #:yield kg/kWh of flour (default 54: a pair of
/// 48-inch French burr stones grinds 400 lb an hour on 4.5 hp, freshly
/// dressed). The flour gathers in a heap beside the stones.
/// </summary>
public partial class MachineView
{
    private sealed class Millstone
    {
        public required RigidBody3D Stone;
        public required Vector3 Axis;
        public required HeroicInventions.Sim.Mechanics.HingeDrive Drive;   // its grind-torque, N·m, which a person can reset (issue #154)
        public required float Yield;           // kg per J
        public double Flour, Power;            // kg so far; W now
        public required MeshInstance3D Heap;
    }

    private readonly List<Millstone> _millstones = [];

    private void BuildMillstones()
    {
        foreach (var part in Runtime.Def.Parts.Where(p => p.Kind == "wheel" && p.Props.GetValueOrDefault("grind-torque") is SNumber))
        {
            _building = part.Id;
            if (!_bodiesById.TryGetValue(part.Id, out var stone) || !_hinges.TryGetValue(stone, out var hinge)) continue;
            // the grinding is its drag: without this, the axle's 0.2/s bearing
            // damping (plus Godot's 0.1) on a 950 kg stone was a 134 N·m brake
            Undamped(stone);
            float radius = (float)part.Number("radius", 0.5);
            var heap = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 1, Height = 1, RadialSegments = 24 },
                MaterialOverride = Shapes.Mat(new Color(0.96f, 0.94f, 0.88f), roughness: 0.95f),
                Position = V(part.At) + new Vector3(radius * 1.6f, 0, 0) with { Y = 0 },
                Scale = Vector3.One * 0.001f,
            };
            AddChild(heap);
            _millstones.Add(new Millstone
            {
                Stone = stone, Axis = hinge.Axis.Normalized(),
                Drive = Runtime.Drives[part.Id],
                Yield = (float)(part.Number("yield", 54) / 3.6e6),
                Heap = heap,
            });
        }
    }

    /// <summary>Each runner resists its turn with the miller's torque, and grinds with the work that takes.</summary>
    private void GrindMillstones(double dt)
    {
        foreach (var m in _millstones)
        {
            float spin = m.Stone.AngularVelocity.Dot(m.Axis);
            var state = PhysicsServer3D.BodyGetDirectState(m.Stone.GetRid());
            float inertia = m.Axis.Dot(state.InverseInertiaTensor.Inverse() * m.Axis);
            // never more than stops it this tick: a stone at rest grinds nothing
            float torque = Mathf.Min((float)m.Drive.Grind, inertia * Mathf.Abs(spin) / (float)dt);
            m.Stone.ApplyTorque(-Mathf.Sign(spin) * torque * m.Axis);
            m.Power = torque * Mathf.Abs(spin);
            m.Flour += m.Yield * m.Power * dt;
        }
    }

    private void DrawMillstones()
    {
        foreach (var m in _millstones)
        {
            // a cone of flour, loose-poured (about 600 kg/m3), a third as high as it is wide
            float volume = (float)(m.Flour / 600);
            float r = Mathf.Max(0.001f, Mathf.Pow(volume * 9 / Mathf.Pi, 1f / 3));   // V = pi r^2 h / 3, h = r / 3
            m.Heap.Scale = new Vector3(r, r / 3, r);
            m.Heap.Position = m.Heap.Position with { Y = r / 6 };
        }
    }
}
