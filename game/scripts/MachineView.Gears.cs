using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Gear trains. Each wheel turned by a crank (#:drive-rpm) drives every
/// gear reachable from it through (mesh a b) links and arbors: across a
/// mesh the speed scales by −(teeth driving / teeth driven), reversing
/// direction; along an arbor it's shared. Each driven gear's hinge motor
/// is set every tick to that speed, plus a correction that pulls its angle
/// back to exactly where the ratio says it should be — so after any
/// number of turns every tooth still sits in its partner's gap.
///
/// The driven gears don't load the crank: right for a hand-turned train of
/// light bronze gears like the Antikythera mechanism's, not for a gear
/// train carrying real torque.
/// </summary>
public partial class MachineView
{
    private readonly Dictionary<string, HingeJoint3D> _axleJoints = [];
    private readonly List<GearFollower> _gearFollowers = [];

    private sealed class GearFollower
    {
        public required RigidBody3D Body, Root;
        public required HingeJoint3D Joint;
        public required Vector3 Axis;
        public required double Factor;       // ω = Factor · ω_root
        public double Angle, RootAngle;      // unwrapped, since the start
        public double LastRaw, LastRootRaw;
    }

    /// <summary>Rotation about an axis, from the body's own X direction, in (-π, π].</summary>
    private static double RawAngle(RigidBody3D body, Vector3 axis)
    {
        var reference = Mathf.Abs(axis.X) < 0.9f ? Vector3.Right : Vector3.Up;
        var u = (reference - axis * axis.Dot(reference)).Normalized();
        var v = axis.Cross(u);
        var x = body.GlobalTransform.Basis * reference;
        return Math.Atan2(x.Dot(v), x.Dot(u));
    }

    private static double Unwrap(double delta) => delta - Math.Tau * Math.Round(delta / Math.Tau);

    private void BuildGearTrains()
    {
        if (Runtime.Def.Meshes.Count == 0) return;
        // Wheels on one arbor turn together: work in terms of each arbor's first wheel.
        var leader = Runtime.Def.Parts.Where(p => p.Kind == "wheel").ToDictionary(p => p.Id, p => p.Id);
        foreach (var arbor in Runtime.Def.Arbors)
            foreach (var p in arbor.Parts) leader[p] = arbor.Parts[0];

        var edges = new Dictionary<string, List<(string To, double Factor)>>();
        foreach (var m in Runtime.Def.Meshes)
        {
            var a = Runtime.Def.Part(m.A)!;
            var b = Runtime.Def.Part(m.B)!;
            CheckMesh(a, b, m.Location);
            double ab = -a.Number("teeth") / b.Number("teeth");
            edges.TryAdd(leader[m.A], []);
            edges.TryAdd(leader[m.B], []);
            edges[leader[m.A]].Add((leader[m.B], ab));
            edges[leader[m.B]].Add((leader[m.A], 1 / ab));
        }

        var factor = new Dictionary<string, (string Root, double Factor)>();
        foreach (var root in edges.Keys.Where(id => Runtime.Def.Part(id)!.Number("drive-rpm", 0) != 0))
        {
            factor[root] = (root, 1);
            var queue = new Queue<string>([root]);
            while (queue.Count > 0)
            {
                var here = queue.Dequeue();
                foreach (var (to, f) in edges[here])
                {
                    double want = factor[here].Factor * f;
                    if (factor.TryGetValue(to, out var had))
                    {
                        if (had.Root != root || Math.Abs(had.Factor - want) > 1e-9 * Math.Abs(want))
                            throw new MachineFormatException($"gear {to} is driven two ways at once: the train locks up", Runtime.Def.Part(to)!.Location);
                        continue;
                    }
                    factor[to] = (root, want);
                    queue.Enqueue(to);
                }
            }
        }

        foreach (var (id, (root, f)) in factor)
        {
            if (id == root) continue;
            var joint = _axleJoints[id];
            joint.SetFlag(HingeJoint3D.Flag.EnableMotor, true);
            joint.SetParam(HingeJoint3D.Param.MotorMaxImpulse, 1e6f);
            var body = _bodiesById[id];
            var rootBody = _bodiesById[root];
            var axis = _hinges[body].Axis;
            _gearFollowers.Add(new GearFollower
            {
                Body = body, Root = rootBody, Joint = joint, Axis = axis, Factor = f,
                LastRaw = RawAngle(body, axis), LastRootRaw = RawAngle(rootBody, _hinges[rootBody].Axis),
            });
        }
    }

    /// <summary>Gears only mesh if cut to one module and set their pitch circles' radii apart.</summary>
    private static void CheckMesh(PartSpec a, PartSpec b, SourceLocation? at)
    {
        foreach (var g in new[] { a, b })
            if (g.Symbol("shape", "") != "gear")
                throw new MachineFormatException($"{g.Id} is not a gear, so it can't mesh", at);
        if (Math.Abs(a.Number("module") - b.Number("module")) > 1e-9)
            throw new MachineFormatException($"{a.Id} and {b.Id} are cut to different modules ({a.Number("module") * 1000} and {b.Number("module") * 1000} mm); their teeth can't engage", at);
        double want = a.Number("pitch-radius") + b.Number("pitch-radius");
        double apart = Math.Sqrt(Math.Pow(a.At.X - b.At.X, 2) + Math.Pow(a.At.Y - b.At.Y, 2));
        if (Math.Abs(apart - want) > 0.02 * want)
            throw new MachineFormatException($"{a.Id} and {b.Id} are {apart * 1000:F1} mm apart; to mesh they must be {want * 1000:F1} mm (the sum of their pitch radii)", at);
    }

    private void DriveGearTrains()
    {
        foreach (var g in _gearFollowers)
        {
            var rootAxis = _hinges[g.Root].Axis;
            double raw = RawAngle(g.Body, g.Axis), rootRaw = RawAngle(g.Root, rootAxis);
            g.Angle += Unwrap(raw - g.LastRaw);
            g.RootAngle += Unwrap(rootRaw - g.LastRootRaw);
            (g.LastRaw, g.LastRootRaw) = (raw, rootRaw);
            // the ratio's speed, plus a pull back onto the ratio's angle
            double speed = g.Factor * g.Root.AngularVelocity.Dot(rootAxis)
                           + 20 * (g.Factor * g.RootAngle - g.Angle);
            g.Joint.SetParam(HingeJoint3D.Param.MotorTargetVelocity, -(float)speed); // negated, as for every hinge motor here
        }
    }

    /// <summary>For telemetry: each driven gear's turns against what its ratio says.</summary>
    private IEnumerable<string> GearReport() =>
        _gearFollowers.Select(g => $"{g.Body.Name} turned {g.Angle / Math.Tau:F3} (ratio says {g.Factor * g.RootAngle / Math.Tau:F3})");
}
