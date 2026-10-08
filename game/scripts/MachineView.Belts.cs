using Godot;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// Belts (issue #51): an endless open belt between two drums. Each physics tick
/// the drums' rim speeds are brought level by an angular impulse on each, as much
/// as it takes up to what the belt can carry (Belt.MaxForce); past that the belt
/// slips and the rest of the speed difference stays. The belt is drawn as the loop
/// it is — the two tangent runs and the arcs round the drums — with marks that
/// travel round it at the driven drum's rim speed, and it goes red while it slips.
/// </summary>
public partial class MachineView
{
    private const int BeltArcSteps = 12;
    private static readonly Color BeltHolding = new(0.55f, 0.38f, 0.2f), BeltSlipping = new(0.9f, 0.15f, 0.1f);

    private sealed class BeltView
    {
        public required Belt Belt;
        public required RigidBody3D A, B;
        public required Vector3 Axis;
        public required List<Vector3> Path;          // the closed loop, world coordinates
        public required List<float> Along;           // cumulative length at each Path point
        public required StandardMaterial3D Material;
        public required MeshInstance3D[] Marks;
        public float Phase;                          // how far the marks have travelled, m
    }

    private readonly List<BeltView> _beltViews = [];

    private void BuildBelts()
    {
        foreach (var (id, belt) in Runtime.Belts)
        {
            _building = id;
            var spec = Runtime.Def.Belts.First(b => b.Id == id);
            var partA = Runtime.Def.Part(spec.A)!;
            var partB = Runtime.Def.Part(spec.B)!;
            var bodyA = _bodiesById[spec.A];
            var bodyB = _bodiesById[spec.B];
            var axis = _hinges[bodyA].Axis;
            Vector3 cA = V(partA.At), cB = V(partB.At);
            var d = cB - cA;
            var u = (d - axis * axis.Dot(d)).Normalized();
            var w = axis.Cross(u);
            float rA = (float)belt.RadiusA, rB = (float)belt.RadiusB;
            float phi = Mathf.Asin((rB - rA) / (float)belt.Centres);
            Vector3 N(float alpha) => u * Mathf.Cos(alpha) + w * Mathf.Sin(alpha);
            float top = Mathf.Pi / 2 + phi, bottom = -top;

            var path = new List<Vector3>();
            path.Add(cA + rA * N(top));
            path.Add(cB + rB * N(top));
            for (int k = 1; k <= BeltArcSteps; k++) path.Add(cB + rB * N(top - k / (float)BeltArcSteps * (Mathf.Pi + 2 * phi)));
            path.Add(cA + rA * N(bottom));
            for (int k = 1; k < BeltArcSteps; k++) path.Add(cA + rA * N(bottom - k / (float)BeltArcSteps * (Mathf.Pi - 2 * phi)));
            var along = new List<float> { 0 };
            for (int i = 1; i <= path.Count; i++) along.Add(along[^1] + path[i - 1].DistanceTo(path[i % path.Count]));

            var mat = Shapes.Mat(BeltHolding, roughness: 0.9f);
            for (int i = 0; i < path.Count; i++)
                AddChild(Shapes.Rod(path[i], path[(i + 1) % path.Count], 0.006f, mat));
            var markMat = Shapes.Mat(new Color(0.95f, 0.9f, 0.7f));
            var marks = new MeshInstance3D[12];
            for (int i = 0; i < marks.Length; i++) { marks[i] = Shapes.Sphere(0.011f, markMat); AddChild(marks[i]); }
            var view = new BeltView { Belt = belt, A = bodyA, B = bodyB, Axis = axis, Path = path, Along = along, Material = mat, Marks = marks };
            _beltViews.Add(view);
            AddLabel($"{id}", (path[0] + path[1]) / 2 + Vector3.Up * 0.05f);
        }
    }

    /// <summary>Grips each belt's drums for one physics tick.</summary>
    private void DriveBelts(double dt)
    {
        foreach (var v in _beltViews)
        {
            var belt = v.Belt;
            double wa = v.A.AngularVelocity.Dot(v.Axis), wb = v.B.AngularVelocity.Dot(v.Axis);
            double slip = wa * belt.RadiusA - wb * belt.RadiusB;
            double ia = InertiaOnAxle(v.A), ib = InertiaOnAxle(v.B);
            var (jA, jB, force, left) = belt.Grip(slip, ia, ib, dt);
            v.A.ApplyTorqueImpulse(v.Axis * (float)jA);
            v.B.ApplyTorqueImpulse(v.Axis * (float)jB);
            belt.Force = force;
            belt.Slip = left;
            v.Phase += (float)(wb * belt.RadiusB * dt);
        }
    }

    /// <summary>What turns with a drum: its own inertia about its axle, and its arbor-mates'.</summary>
    private double InertiaOnAxle(RigidBody3D body) =>
        InertiaAbout(body, _hinges[body]) + _arborMates.GetValueOrDefault(body, []).Sum(m => InertiaAbout(m, _hinges[m]));

    private void DrawBelts()
    {
        foreach (var v in _beltViews)
        {
            v.Material.AlbedoColor = Math.Abs(v.Belt.Slip) > 0.01 ? BeltSlipping : BeltHolding;
            float length = v.Along[^1];
            for (int i = 0; i < v.Marks.Length; i++)
            {
                float s = ((v.Phase + i * length / v.Marks.Length) % length + length) % length;
                int seg = Math.Max(0, v.Along.FindLastIndex(a => a <= s));
                seg = Math.Min(seg, v.Path.Count - 1);
                float segLen = v.Along[seg + 1] - v.Along[seg];
                float t = segLen > 1e-6f ? (s - v.Along[seg]) / segLen : 0;
                v.Marks[i].Position = v.Path[seg].Lerp(v.Path[(seg + 1) % v.Path.Count], t);
            }
        }
    }
}
