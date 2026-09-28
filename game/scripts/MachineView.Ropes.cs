using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Ropes and chains. Jolt has no rope joint, so each rope is solved here
/// once per physics tick, before the step, as a one-sided constraint on
/// its length: while taut, it applies the impulse that stops its two ends
/// separating along it (plus a gentle pull back for any stretch already
/// there). The impulse lands on each end at the attachment point, so a
/// load hanging from a drum turns the drum, and the drum turning lifts the
/// load — the rope carries force both ways, as a real one does.
///
/// How hard a given impulse moves an end depends on what it's attached
/// to: for a part on a hinge (an arm, a drum, a pendulum), its moment of
/// inertia about the hinge and the lever arm of the pull; for a free body,
/// its mass and inertia about its centre of mass; a fixed point doesn't
/// move at all. Those "effective masses" set the impulse size exactly.
/// </summary>
public partial class MachineView
{
    // Parts on a hinge, with the hinge's pivot and axis in world space.
    private readonly Dictionary<RigidBody3D, (Vector3 Pivot, Vector3 Axis)> _hinges = [];
    private readonly List<Rope> _ropes = [];
    // Wheels keyed to one arbor turn together: each one's partners.
    private readonly Dictionary<RigidBody3D, List<RigidBody3D>> _arborMates = [];

    private sealed class Rope
    {
        public required RopeSpec Spec;
        public RigidBody3D? A, B;       // null: a fixed point
        public Vector3 ALocal, BLocal;  // in the body's frame, or world if fixed
        public required List<Vector3> Over;
        public RigidBody3D? Drum;
        public float DrumRadius;
        public Vector3 DrumAxis;
        public float Wound;             // rope wound onto the drum so far, m
        public float Strength;          // breaking load, N
        public float Tension;           // last tick, N
        public float ArmTurned;         // how far the From part has turned, rad (for release)
        public float MostLag;           // most the load has lagged the arm, degrees (negative)
        public bool Released, Broken;
        public readonly List<MeshInstance3D> Segments = [];
        public bool Active => !Released && !Broken;

        public string Describe() =>
            $"{Spec.Id} T={Tension:F0}N{(Released ? " released" : "")}{(Broken ? " BROKEN" : "")}";
    }

    private void BuildRope(RopeSpec spec)
    {
        var mat = _materials[spec.Material];
        var rope = new Rope
        {
            Spec = spec,
            Over = spec.Over.Select(V).ToList(),
            // tensile strength (MPa) × cross-section
            Strength = (float)(mat.TensileStrength * 1e6 * Math.PI * spec.Diameter * spec.Diameter / 4),
        };
        (rope.A, rope.ALocal) = ResolveEnd(spec.From);
        (rope.B, rope.BLocal) = ResolveEnd(spec.To);
        if (spec.WindOn is { } drumId)
        {
            var drumPart = Runtime.Def.Part(drumId)!;
            rope.Drum = _bodiesById[drumId];
            rope.DrumRadius = (float)drumPart.Number("radius");
            rope.DrumAxis = _hinges[rope.Drum].Axis;
            rope.A = rope.Drum;
        }
        var look = Surface(spec.Material);
        foreach (var _ in Enumerable.Range(0, rope.Over.Count + 1))
        {
            var seg = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = (float)spec.Diameter / 2, BottomRadius = (float)spec.Diameter / 2, Height = 1, RadialSegments = 8 },
                MaterialOverride = look,
            };
            AddChild(seg);
            rope.Segments.Add(seg);
        }
        _ropes.Add(rope);
        DrawRope(rope);
    }

    /// <summary>A rope end's body (null if fixed) and its point, local to that body or in world space.</summary>
    private (RigidBody3D?, Vector3) ResolveEnd(RopeEnd end)
    {
        var local = V(end.Local);
        if (end.Part == "world") return (null, local);
        if (_bodiesById.TryGetValue(end.Part, out var body)) return (body, local);
        // a part that doesn't move (a fixture, a tank): a fixed point
        return (null, V(Runtime.Def.Part(end.Part)!.At) + local);
    }

    /// <summary>
    /// The rope's path in world space: its From point, the pulley points, its
    /// To point. On a drum, the From point is where the rope leaves the
    /// drum: the tangent point, on the side where turning the drum forward
    /// (positive about its axle) winds the rope on.
    /// </summary>
    private static List<Vector3> RopePath(Rope r)
    {
        var b = r.B is null ? r.BLocal : r.B.GlobalTransform * r.BLocal;
        var next = r.Over.Count > 0 ? r.Over[0] : b;
        Vector3 a;
        if (r.Drum is { } drum)
        {
            var c = drum.GlobalPosition;
            var ax = r.DrumAxis;
            var d = next - c;
            var inPlane = d - ax * ax.Dot(d);
            float dist = inPlane.Length();
            var toward = inPlane / dist;
            var side = ax.Cross(toward);
            float cos = Mathf.Min(1, r.DrumRadius / dist), sin = Mathf.Sqrt(1 - cos * cos);
            var t1 = c + r.DrumRadius * (cos * toward + sin * side);
            var t2 = c + r.DrumRadius * (cos * toward - sin * side);
            // forward rotation must carry the rim away from `next`
            a = ax.Cross(t1 - c).Dot(next - t1) < 0 ? t1 : t2;
        }
        else a = r.A is null ? r.ALocal : r.A.GlobalTransform * r.ALocal;
        var path = new List<Vector3> { a };
        path.AddRange(r.Over);
        path.Add(b);
        return path;
    }

    private static Vector3 CentreOfMass(RigidBody3D body) =>
        body.GlobalPosition + PhysicsServer3D.BodyGetDirectState(body.GetRid()).CenterOfMass;

    private static Vector3 PointVelocity(RigidBody3D? body, Vector3 point) =>
        body is null || body.Freeze ? Vector3.Zero : body.LinearVelocity + body.AngularVelocity.Cross(point - CentreOfMass(body));

    /// <summary>1 / effective mass of <paramref name="body"/> for an impulse along <paramref name="dir"/> at <paramref name="point"/>.</summary>
    private float InverseMassAlong(RigidBody3D? body, Vector3 point, Vector3 dir)
    {
        if (body is null || body.Freeze) return 0;
        var state = PhysicsServer3D.BodyGetDirectState(body.GetRid());
        var invI = state.InverseInertiaTensor;
        var com = body.GlobalPosition + state.CenterOfMass;
        if (_hinges.TryGetValue(body, out var hinge))
        {
            // turning about the hinge: lever arm k over the inertia about the
            // hinge of everything that turns with it (the whole arbor)
            float k = (point - hinge.Pivot).Cross(dir).Dot(hinge.Axis);
            float inertia = InertiaAbout(body, hinge)
                            + _arborMates.GetValueOrDefault(body, []).Sum(m => InertiaAbout(m, hinge));
            return k * k / inertia;
        }
        var arm = (point - com).Cross(dir);
        return 1 / body.Mass + arm.Dot(invI * arm);
    }

    /// <summary>Moment of inertia about a hinge line: about the centre of mass, plus m·d² for the offset.</summary>
    private static float InertiaAbout(RigidBody3D body, (Vector3 Pivot, Vector3 Axis) hinge)
    {
        var state = PhysicsServer3D.BodyGetDirectState(body.GetRid());
        var com = body.GlobalPosition + state.CenterOfMass;
        float iCom = hinge.Axis.Dot(state.InverseInertiaTensor.Inverse() * hinge.Axis);
        var off = com - hinge.Pivot;
        var perp = off - hinge.Axis * hinge.Axis.Dot(off);
        return iCom + body.Mass * perp.LengthSquared();
    }

    /// <summary>
    /// All ropes at once, a few passes per tick (sequential impulses): ropes
    /// that pull on the same part — a beam with a chain at each end — see
    /// each other's pull within the tick. Solved one at a time and once,
    /// each over-corrected for the other and the chains chattered between
    /// taut and slack. Impulses only reach the bodies at the next physics
    /// step, so each pass works on a running tally of how every impulse so
    /// far has changed the bodies' speeds; each rope's total impulse is kept
    /// at or above zero, since a rope can't push.
    /// </summary>
    private void ResolveRopes()
    {
        float dt = (float)GetPhysicsProcessDeltaTime();
        var linear = new Dictionary<RigidBody3D, Vector3>();   // velocity changes so far this tick
        var angular = new Dictionary<RigidBody3D, Vector3>();
        Vector3 Velocity(RigidBody3D? body, Vector3 point) =>
            body is null || body.Freeze ? Vector3.Zero
            : PointVelocity(body, point) + linear.GetValueOrDefault(body)
              + angular.GetValueOrDefault(body).Cross(point - CentreOfMass(body));
        void Push(RigidBody3D? body, Vector3 point, Vector3 impulse)
        {
            if (body is null || body.Freeze) return;
            if (_hinges.TryGetValue(body, out var hinge))
            {
                // turns about its hinge only: Δω = (r × J)·axis / I, along the axis
                float inertia = InertiaAbout(body, hinge) + _arborMates.GetValueOrDefault(body, []).Sum(m => InertiaAbout(m, hinge));
                angular[body] = angular.GetValueOrDefault(body) + hinge.Axis * ((point - hinge.Pivot).Cross(impulse).Dot(hinge.Axis) / inertia);
                return;
            }
            var state = PhysicsServer3D.BodyGetDirectState(body.GetRid());
            linear[body] = linear.GetValueOrDefault(body) + impulse / body.Mass;
            angular[body] = angular.GetValueOrDefault(body) + state.InverseInertiaTensor * (point - CentreOfMass(body)).Cross(impulse);
        }

        var active = new List<(Rope Rope, List<Vector3> Path, Vector3 A, Vector3 B, Vector3 Ua, Vector3 Ub, float Stretch, float W)>();
        foreach (var r in _ropes)
        {
            if (!r.Active) continue;
            if (r.Drum is { } drum)
                r.Wound += r.DrumRadius * drum.AngularVelocity.Dot(r.DrumAxis) * dt;
            if (r.A is { } armBody && _hinges.TryGetValue(armBody, out var armHinge))
                r.ArmTurned += Mathf.Abs(armBody.AngularVelocity.Dot(armHinge.Axis)) * dt;
            var path = RopePath(r);
            float length = 0;
            for (int i = 1; i < path.Count; i++) length += path[i].DistanceTo(path[i - 1]);
            var a = path[0];
            var b = path[^1];
            var ua = (path[1] - a).Normalized();   // the rope pulls A this way
            var ub = (path[^2] - b).Normalized();  // and B this way
            float w = InverseMassAlong(r.A, a, ua) + InverseMassAlong(r.B, b, ub);
            active.Add((r, path, a, b, ua, ub, length - ((float)r.Spec.Length - r.Wound), w));
        }

        var total = new float[active.Count];
        for (int pass = 0; pass < 8; pass++)
            for (int i = 0; i < active.Count; i++)
            {
                var (r, _, a, b, ua, ub, stretch, w) = active[i];
                if (w <= 0) continue;
                // how fast the ends are pulling apart along the rope, with every impulse so far
                float separating = -ua.Dot(Velocity(r.A, a)) - ub.Dot(Velocity(r.B, b));
                // slack: let the gap close within this step, no further; taut or
                // stretched: no separation, and take back a third of the stretch
                float allowed = stretch < 0 ? -stretch / dt : -0.3f * stretch / dt;
                float delta = (separating - allowed) / w;
                float before = total[i];
                total[i] = Mathf.Max(0, before + delta);
                float applied = total[i] - before;
                Push(r.A, a, ua * applied);
                Push(r.B, b, ub * applied);
            }

        for (int i = 0; i < active.Count; i++)
        {
            var (r, _, a, b, ua, ub, _, _) = active[i];
            r.Tension = total[i] / dt;
            if (r.Tension > r.Strength)
            {
                r.Broken = true;
                GD.Print($"rope {r.Spec.Id} broke: {r.Tension:F0} N exceeds its {r.Strength:F0} N breaking load");
                continue;
            }
            // A nocked end (a bolt on a bowstring) is driven, never held
            // back: once the string would pull it against its own motion,
            // the bolt has outrun the string, and they part.
            if (r.Spec.Nocked && total[i] > 0 && r.B is { } nockedBody
                && PointVelocity(nockedBody, b) is var vb && vb.Length() > 0.5f   // really moving, not settling
                && ub.Dot(vb.Normalized()) < -0.2f)
            {
                r.Released = true;
                foreach (var seg in r.Segments) seg.Visible = false;
                GD.Print($"rope {r.Spec.Id} let go of {nockedBody.Name} at {Runtime.Time:F3}s, moving {nockedBody.LinearVelocity.Length():F1} m/s");
                continue;
            }
            if (total[i] > 0)
            {
                r.A?.ApplyImpulse(ua * total[i], a - r.A.GlobalPosition);
                r.B?.ApplyImpulse(ub * total[i], b - r.B.GlobalPosition);
            }
            CheckRelease(r, a, b);
        }
    }

    /// <summary>
    /// A sling lets go when the rope has swung round to within ReleaseDeg of
    /// pointing straight out along the arm (pivot through the rope's end) —
    /// how the loop slips off a trebuchet's release pin. The load has to
    /// have lagged behind the arm first and then whipped round: at the
    /// start the sling lies flat along the ground, which can already look
    /// "nearly in line" with an arm pointing down, and must not count.
    /// </summary>
    private void CheckRelease(Rope r, Vector3 a, Vector3 b)
    {
        if (r.Spec.ReleaseDeg is not { } releaseDeg || r.A is null || !_hinges.TryGetValue(r.A, out var hinge)) return;
        if (r.ArmTurned < Mathf.DegToRad(20)) return;
        var arm = (a - hinge.Pivot).Normalized();
        var rope = (b - a).Normalized();
        float turning = Mathf.Sign(r.A.AngularVelocity.Dot(hinge.Axis));
        // negative while the load lags behind the arm, 0 when in line with it
        float angle = Mathf.RadToDeg(Mathf.Atan2(hinge.Axis.Dot(arm.Cross(rope)), arm.Dot(rope))) * turning;
        r.MostLag = Mathf.Min(r.MostLag, angle);
        if (r.MostLag > -(float)releaseDeg - 20 || angle < -(float)releaseDeg) return;
        r.Released = true;
        foreach (var seg in r.Segments) seg.Visible = false;
        GD.Print($"rope {r.Spec.Id} released at {Runtime.Time:F2}s");
    }

    private void DrawRope(Rope r)
    {
        if (!r.Active) return;
        var path = RopePath(r);
        for (int i = 0; i < r.Segments.Count; i++)
        {
            var (from, to) = (path[i], path[i + 1]);
            var dir = to - from;
            float len = Mathf.Max(dir.Length(), 0.0001f);
            var up = dir / len;
            var axis = Vector3.Up.Cross(up);
            var basis = axis.LengthSquared() > 1e-8f ? new Basis(axis.Normalized(), Vector3.Up.AngleTo(up))
                      : up.Y < 0 ? new Basis(Vector3.Right, Mathf.Pi) : Basis.Identity;
            r.Segments[i].Transform = new Transform3D(basis * Basis.FromScale(new Vector3(1, len, 1)), (from + to) / 2); // stretched along its own length
        }
    }
}
