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

    /// <summary>
    /// Last tick's stretch-correction impulses, taken back at the start of
    /// the next (a split impulse): the correction closes the stretch over
    /// one step without leaving that speed in the bodies. As a lasting
    /// velocity kick it put energy in every time the trebuchet's chain was
    /// yanked taut, and the machine climbed to 155% of its starting energy
    /// after its throw (issue #45).
    /// </summary>
    private readonly List<(RigidBody3D Body, Vector3 Impulse, Vector3 Offset)> _ropeBias = [];
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
        public float Tension;           // last tick, N (the tight side's, over bars)
        public float Stretch;           // last tick, m its path was longer than its length (negative: slack)
        // Over fixed bars (#:bar): the capstan friction coefficient (0 for
        // turning pulleys), the angle the rope turns through over the bars
        // (rad), each side's tension, and how fast the rope slides over
        // them toward its From end (m/s, negative toward its To end).
        public float Mu, Wrap, TensionFrom, TensionTo, Slip;
        public MeshInstance3D? BarMesh;
        public StandardMaterial3D? BarLook;
        public Label3D? BarLabel;
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
        if (spec.Links is not null) { BuildChain(spec); return; }   // a chain of pinned links (#31)
        var mat = _materials[spec.Material];
        var rope = new Rope
        {
            Spec = spec,
            Over = spec.Over.Select(V).ToList(),
            // tensile strength (MPa) × cross-section
            Strength = (float)(mat.TensileStrength * 1e6 * Math.PI * spec.Diameter * spec.Diameter / 4),
        };
        if (spec.Bar is { } bar)
            // rope on bar as Jolt combines two surfaces' friction, √(μ₁μ₂), as the capstan part does
            rope.Mu = (float)(spec.Mu ?? Math.Sqrt(mat.Friction * _materials[bar].Friction));
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
        if (spec.Bar is { } barMat && rope.Over.Count > 0) BuildBar(rope, barMat);
        _ropes.Add(rope);
        DrawRope(rope);
    }

    /// <summary>
    /// The fixed bar a #:bar rope drags over: a round timber through the
    /// middle of its #:over points, across the plane the rope turns in. It
    /// glows with the heat friction makes there (tension difference × slip
    /// speed), so a rope sliding over it shows, and one held by it doesn't.
    /// </summary>
    private void BuildBar(Rope rope, string material)
    {
        // the circle through its first three points, or their middle
        var centre = rope.Over.Aggregate(Vector3.Zero, (acc, p) => acc + p) / rope.Over.Count;
        if (rope.Over.Count >= 3)
        {
            var (p0, p1, p2) = (rope.Over[0], rope.Over[1], rope.Over[2]);
            var (u, v) = (p1 - p0, p2 - p0);
            var n = u.Cross(v);
            if (n.LengthSquared() > 1e-10f)
                centre = p0 + (v.LengthSquared() * n.Cross(u) + u.LengthSquared() * v.Cross(n)) / (2 * n.LengthSquared());
        }
        float radius = rope.Over.Count > 1 ? rope.Over.Average(p => p.DistanceTo(centre)) : (float)rope.Spec.Diameter;
        var path = RopePath(rope);
        rope.Wrap = WrapAngle(path);
        var across = (path[1] - path[0]).Cross(path[2] - path[1]);
        var axis = across.LengthSquared() > 1e-8f ? across.Normalized() : Vector3.Back;
        float length = Mathf.Max(0.4f, Mathf.Max(3 * radius, 8 * (float)rope.Spec.Diameter));
        var baseLook = Surface(material);
        rope.BarLook = new StandardMaterial3D
        {
            AlbedoColor = baseLook.AlbedoColor,
            Roughness = baseLook.Roughness,
            EmissionEnabled = true,
            Emission = new Color(1f, 0.35f, 0.05f),
            EmissionEnergyMultiplier = 0,
        };
        rope.BarMesh = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = length, RadialSegments = 16 },
            MaterialOverride = rope.BarLook,
        };
        AddChild(rope.BarMesh);
        var tilt = Vector3.Up.Cross(axis);
        rope.BarMesh.Transform = new Transform3D(
            tilt.LengthSquared() > 1e-8f ? new Basis(tilt.Normalized(), Vector3.Up.AngleTo(axis)) : Basis.Identity, centre);
        rope.BarLabel = new Label3D
        {
            Position = centre + new Vector3(0, radius + 0.25f, 0),
            FontSize = 24, OutlineSize = 6, PixelSize = 0.006f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
        };
        AddChild(rope.BarLabel);
    }

    /// <summary>The angle a rope turns through over its #:over points (rad): the sum of its bends there.</summary>
    private static float WrapAngle(List<Vector3> path)
    {
        float wrap = 0;
        for (int i = 1; i < path.Count - 1; i++)
        {
            var din = path[i] - path[i - 1];
            var dout = path[i + 1] - path[i];
            if (din.LengthSquared() < 1e-12f || dout.LengthSquared() < 1e-12f) continue;
            wrap += din.AngleTo(dout);
        }
        return wrap;
    }

    /// <summary>A rope end's body (null if fixed) and its point, local to that body or in world space.</summary>
    private (RigidBody3D?, Vector3) ResolveEnd(RopeEnd end)
    {
        var local = V(end.Local);
        if (end.Part == "world") return (null, local);
        if (_bodiesById.TryGetValue(end.Part, out var body)) return (body, local);
        // a part that doesn't move (a fixture, a tank): a fixed point
        var fixedPart = Runtime.Def.Part(end.Part)!;
        return (null, V(fixedPart.At) + YawOf(fixedPart) * local);
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
            // (the running tally of this tick's velocity changes; see below)
            if (body is null || body.Freeze) return;
            if (_hinges.TryGetValue(body, out var hinge))
            {
                // turns about its hinge only: Δω = (r × J)·axis / I, along the axis
                float inertia = InertiaAbout(body, hinge) + _arborMates.GetValueOrDefault(body, []).Sum(m => InertiaAbout(m, hinge));
                var spin = hinge.Axis * ((point - hinge.Pivot).Cross(impulse).Dot(hinge.Axis) / inertia);
                angular[body] = angular.GetValueOrDefault(body) + spin;
                // turning about the pivot carries the centre of mass round with it: Velocity() reads a point's change as
                // linear + angular × (point − centre of mass), which is then angular × (point − pivot), as the hinge has it.
                // Without this the tally misread how far an impulse moves a hinged end, by (point − com) over (point −
                // pivot): on the trebuchet's short arm, 3.3 times, and each pass of the solve then corrected the wrong amount (#80).
                linear[body] = linear.GetValueOrDefault(body) + spin.Cross(CentreOfMass(body) - hinge.Pivot);
                return;
            }
            var state = PhysicsServer3D.BodyGetDirectState(body.GetRid());
            linear[body] = linear.GetValueOrDefault(body) + impulse / body.Mass;
            angular[body] = angular.GetValueOrDefault(body) + state.InverseInertiaTensor * (point - CentreOfMass(body)).Cross(impulse);
        }

        // Take back last tick's stretch correction, and count that in the
        // tally too: the bodies' velocities still include it, so without it
        // the solve would read the correction as the rope's ends closing,
        // decide the rope was going slack, and stop holding its load.
        foreach (var (body, impulse, offset) in _ropeBias)
        {
            if (!IsInstanceValid(body) || body.Freeze) continue;
            body.ApplyImpulse(-impulse, offset);
            Push(body, body.GlobalPosition + offset, -impulse);
        }
        _ropeBias.Clear();

        var active = new List<(Rope Rope, List<Vector3> Path, Vector3 A, Vector3 B, Vector3 Ua, Vector3 Ub, float Stretch, float W, float WA, float WB)>();
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
            float wa = InverseMassAlong(r.A, a, ua), wb = InverseMassAlong(r.B, b, ub);
            if (r.Mu > 0) r.Wrap = WrapAngle(path);
            r.Stretch = length - ((float)r.Spec.Length - r.Wound);
            active.Add((r, path, a, b, ua, ub, r.Stretch, wa + wb, wa, wb));
        }

        var total = new float[active.Count];
        // Over fixed bars the two sides can pull differently: From gets
        // total + friction/2, To total - friction/2 (see OverBars).
        var friction = new float[active.Count];
        for (int pass = 0; pass < 8; pass++)
            for (int i = 0; i < active.Count; i++)
            {
                var (r, _, a, b, ua, ub, stretch, w, wa, wb) = active[i];
                if (w <= 0) continue;
                // slack: let the gap close within this step, no further; taut or
                // stretched: no separation. The stretch itself is taken back
                // separately below, so this impulse is never more than
                // inelastic and can't put energy in.
                float allowed = stretch < 0 ? -stretch / dt : 0;
                if (r.Mu > 0 && wa > 0 && wb > 0)
                {
                    OverBars(i, r, a, b, ua, ub, wa, wb, allowed, total, friction);
                    continue;
                }
                // how fast the ends are pulling apart along the rope, with every impulse so far
                float separating = -ua.Dot(Velocity(r.A, a)) - ub.Dot(Velocity(r.B, b));
                float delta = (separating - allowed) / w;
                float before = total[i];
                total[i] = Mathf.Max(0, before + delta);
                float applied = total[i] - before;
                Push(r.A, a, ua * applied);
                Push(r.B, b, ub * applied);
            }

        // A rope over fixed bars: its two sides' impulses, pA and pB, solved
        // together and exactly (the one-row-at-a-time way converges far too
        // slowly when one end is much lighter than the other: a held load
        // crept down at a steady 3 cm/s). Each side's length grows at
        // rA = rA0 - wA pA (rA0: with this rope's own pull taken out), and so
        // for B. The rope can't lengthen past what slack allows. Friction
        // lets the sides differ up to the capstan equation, pA <= E pB and
        // pB <= E pA with E = e^(mu theta); inside that the rope doesn't
        // slide over the bars (rA = rB); at the limit it slides toward the
        // tight side. One end fixed (w = 0): nothing can slide, and the rope
        // is solved as a plain one above. The stretch correction below goes
        // through here too: shared out by equal impulses, it closed a held
        // rope's stretch mostly from its light end, and the rope crept over
        // the bar toward the heavy one at 3 cm/s.
        void OverBars(int i, Rope r, Vector3 a, Vector3 b, Vector3 ua, Vector3 ub, float wa, float wb, float allowed, float[] total, float[] friction)
        {
            float pa = total[i] + friction[i] / 2, pb = total[i] - friction[i] / 2;
            float ra0 = -ua.Dot(Velocity(r.A, a)) + wa * pa;
            float rb0 = -ub.Dot(Velocity(r.B, b)) + wb * pb;
            float e = Mathf.Exp(r.Mu * r.Wrap);
            float na, nb;
            if (ra0 + rb0 <= allowed) na = nb = 0;                  // closing at least as fast as the slack allows: slack
            else
            {
                na = (ra0 - allowed / 2) / wa;                        // held: neither side's length changes
                nb = (rb0 - allowed / 2) / wb;
                if (na > e * nb)                                      // From would need more than friction gives: slides toward From
                {
                    nb = (ra0 + rb0 - allowed) / (wa * e + wb);
                    na = e * nb;
                }
                else if (nb > e * na)                                 // slides toward To
                {
                    na = (ra0 + rb0 - allowed) / (wa + wb * e);
                    nb = e * na;
                }
            }
            Push(r.A, a, ua * (na - pa));
            Push(r.B, b, ub * (nb - pb));
            total[i] = (na + nb) / 2;
            friction[i] = na - nb;
        }

        // Stretch correction: close a third of any stretch this step, solved
        // on top of the real impulses, applied now and taken back next tick.
        var bias = new float[active.Count];
        var biasFriction = new float[active.Count];
        for (int pass = 0; pass < 4; pass++)
            for (int i = 0; i < active.Count; i++)
            {
                var (r, _, a, b, ua, ub, stretch, w, wa, wb) = active[i];
                if (w <= 0 || stretch <= 0) continue;
                if (r.Mu > 0 && wa > 0 && wb > 0)
                {
                    OverBars(i, r, a, b, ua, ub, wa, wb, -0.3f * stretch / dt, bias, biasFriction);
                    continue;
                }
                float separating = -ua.Dot(Velocity(r.A, a)) - ub.Dot(Velocity(r.B, b));
                float delta = (separating + 0.3f * stretch / dt) / w;
                float before = bias[i];
                bias[i] = Mathf.Max(0, before + delta);
                float applied = bias[i] - before;
                Push(r.A, a, ua * applied);
                Push(r.B, b, ub * applied);
            }

        for (int i = 0; i < active.Count; i++)
        {
            var (r, _, a, b, ua, ub, _, _, _, _) = active[i];
            float fromImpulse = total[i] + friction[i] / 2, toImpulse = total[i] - friction[i] / 2;
            r.TensionFrom = fromImpulse / dt;
            r.TensionTo = toImpulse / dt;
            r.Tension = Mathf.Max(r.TensionFrom, r.TensionTo);
            if (r.Mu > 0) r.Slip = 0.5f * (-ua.Dot(PointVelocity(r.A, a)) + ub.Dot(PointVelocity(r.B, b)));
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
                r.A?.ApplyImpulse(ua * fromImpulse, a - r.A.GlobalPosition);
                r.B?.ApplyImpulse(ub * toImpulse, b - r.B.GlobalPosition);
            }
            if (bias[i] > 0)
            {
                float biasA = bias[i] + biasFriction[i] / 2, biasB = bias[i] - biasFriction[i] / 2;
                if (r.A is { Freeze: false } bodyA)
                {
                    bodyA.ApplyImpulse(ua * biasA, a - bodyA.GlobalPosition);
                    _ropeBias.Add((bodyA, ua * biasA, a - bodyA.GlobalPosition));
                }
                if (r.B is { Freeze: false } bodyB)
                {
                    bodyB.ApplyImpulse(ub * biasB, b - bodyB.GlobalPosition);
                    _ropeBias.Add((bodyB, ub * biasB, b - bodyB.GlobalPosition));
                }
            }
            CheckRelease(r, a, b);
            TurnPulley(r, active[i].Path);
        }
    }

    /// <summary>
    /// A pulley the rope runs over turns with it: its rim, where the rope
    /// touches it (the first #:over point), moves at the rope's speed —
    /// how fast the load end is being hauled in along the rope — heading
    /// back along the rope toward its From end.
    /// </summary>
    private void TurnPulley(Rope r, List<Vector3> path)
    {
        if (r.Spec.Turns is not { } sheaveId || path.Count < 3 || r.B is null) return;
        var sheave = _bodiesById[sheaveId];
        var (centre, axis) = _hinges[sheave];
        var contact = path[1];
        var along = (path[0] - contact).Normalized();                // rope heading back toward its From end
        var ub = (path[^2] - path[^1]).Normalized();
        float haul = ub.Dot(PointVelocity(r.B, path[^1]));           // load end moving along the rope
        var arm = contact - centre;
        // a rope that doesn't slip moves the rim at the full rope speed;
        // the geometry only says which way round that is
        float omega = Mathf.Sign(arm.Cross(along).Dot(axis)) * haul / arm.Length();
        var joint = _axleJoints[sheaveId];
        joint.SetFlag(HingeJoint3D.Flag.EnableMotor, true);
        joint.SetParam(HingeJoint3D.Param.MotorMaxImpulse, 1e4f);
        joint.SetParam(HingeJoint3D.Param.MotorTargetVelocity, -omega); // negated, as for every hinge motor here
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
        if (r.BarLook is { } look)
        {
            // friction's heat at the bar, W: what the tight side loses to the slack one, times the sliding speed
            float heat = r.Active ? Mathf.Abs((r.TensionFrom - r.TensionTo) * r.Slip) : 0;
            look.EmissionEnergyMultiplier = Mathf.Clamp(heat / 500f, 0, 3);
            r.BarLabel!.Text = !r.Active ? "" :
                $"{Mathf.Max(r.TensionFrom, r.TensionTo):F0} N : {Mathf.Min(r.TensionFrom, r.TensionTo):F0} N" +
                (Mathf.Abs(r.Slip) > 0.01f ? $"  sliding {Mathf.Abs(r.Slip):F2} m/s" : "  holding") +
                $"\nmost e^(μθ) = {Mathf.Exp(r.Mu * r.Wrap):F1}";
        }
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
