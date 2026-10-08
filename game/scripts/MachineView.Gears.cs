using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// Gear trains. Two kinds, by what turns them.
///
/// Cranked: a wheel turned by a crank (#:drive-rpm) drives every gear
/// reachable from it through (mesh a b) links and arbors: across a mesh the
/// speed scales by −(teeth driving / teeth driven), reversing direction;
/// along an arbor it's shared. Each driven gear's hinge motor is set every
/// tick to that speed, plus a correction that pulls its angle back to
/// exactly where the ratio says it should be — so after any number of turns
/// every tooth still sits in its partner's gap. The driven gears don't load
/// the crank: right for a hand-turned train of light bronze gears like the
/// Antikythera mechanism's, not for a gear train carrying real torque.
///
/// Driven and loaded (issue #113): a train with no crank in it is turned by
/// whatever turns its gears — a flywheel let go spinning, or a water wheel,
/// windmill or jet wheel keyed on an arbor with one of its wheels — and its
/// load slows that driver. Every mesh is a <see cref="ShaftLink"/> between
/// the two arbors' Jolt bodies (<see cref="JoltShaft"/>), of ratio
/// −(teeth driving / teeth driven) and the mesh's #:efficiency; a part the
/// sim turns, on an arbor, is a ShaftLink of ratio ±1 (its own sense of
/// turning about the axle) to the arbor's first Jolt wheel. That exchange is
/// the one coupling point between the sim's turning parts and Jolt's bodies.
/// Each tick, after the sim has stepped its parts under their own drive and
/// load (water in the buckets, wind on the sails) and before Jolt steps its
/// bodies under theirs (a brake, a rod pushing a saw), the links trade the
/// least angular momentum that puts every pair back on its ratio. A load at
/// the output slows its gear during Jolt's step; the next tick's exchange
/// takes that much back from the driver, ratio × torque / η. Each link also
/// keeps the cranked train's angle pull, as a bias on the exchange (at most
/// a tenth of the speed, so a train friction has stopped stays stopped):
/// meshed teeth stay in each other's gaps, and a crank keeps its water
/// wheel's angle though its load slows it within every step.
/// The bodies of a driven train lose the engine's 0.2/s axle damping, so
/// what slows them is what the machine gives them; a bearing on one of them
/// (#:bearing-mu) stops the whole train, its friction clamped against the
/// inertia of everything geared to it, not just its own wheel's.
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

    /// <summary>A link of a driven train: a mesh between two arbors, or a sim-turned part keyed on an arbor.</summary>
    private sealed class DrivenLink
    {
        public required ShaftLink Link;
        public required string FromId, ToId;            // the parts named in the machine: the mesh's gears, or the sim part and the wheel
        public RigidBody3D? FromBody, ToBody;           // the arbors' first Jolt wheels; null for a sim part
        public double FromAngle, ToAngle, LastFrom, LastTo;
        public required Label3D Label;
    }

    private readonly List<DrivenLink> _drivenLinks = [];
    private readonly List<ShaftLink> _drivenShaftLinks = [];
    /// <summary>For each Jolt body in a driven train: every member of its train, with its speed as a multiple of a common reference.</summary>
    private readonly Dictionary<RigidBody3D, (List<(IShaft Shaft, double Factor)> Train, double Factor)> _trainOf = [];

    /// <summary>A part the sim turns, which can sit on an arbor and drive its wheels.</summary>
    private bool IsSimTurned(string id) =>
        Runtime.WaterWheels.ContainsKey(id) || Runtime.Windmills.ContainsKey(id) || Runtime.JetWheels.ContainsKey(id);

    private IShaft SimShaft(string id) =>
        Runtime.WaterWheels.TryGetValue(id, out var w) ? w
        : Runtime.Windmills.TryGetValue(id, out var m) ? m
        : Runtime.JetWheels[id];

    /// <summary>How far a sim-turned part has turned, rad forward, wrapped (each is kept within a turn).</summary>
    private double SimAngle(string id) =>
        Runtime.WaterWheels.TryGetValue(id, out var w) ? w.Angle
        : Runtime.Windmills.TryGetValue(id, out var m) ? m.Angle
        : Runtime.JetWheels[id].Angle;

    /// <summary>The Jolt wheel that leads an arbor: its first wheel, passing over a part the sim turns.</summary>
    private string ArborLead(ArborSpec arbor) => arbor.Parts.First(p => !IsSimTurned(p));

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
        if (Runtime.Def.Meshes.Count == 0 && !Runtime.Def.Arbors.Any(a => a.Parts.Any(IsSimTurned))) return;
        // Wheels on one arbor turn together: work in terms of each arbor's first wheel (a sim part on it is coupled to that, below).
        var leader = Runtime.Def.Parts.Where(p => p.Kind == "wheel").ToDictionary(p => p.Id, p => p.Id);
        foreach (var arbor in Runtime.Def.Arbors)
            foreach (var p in arbor.Parts.Where(p => !IsSimTurned(p))) leader[p] = ArborLead(arbor);

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

        BuildDrivenTrains(leader, edges, factor.Keys.ToHashSet());
        foreach (var m in Runtime.Def.Meshes)
            _meshTeeth.Add((m, _bodiesById[m.A], _bodiesById[m.B], Runtime.Def.Part(m.A)!.Number("teeth"), Runtime.Def.Part(m.B)!.Number("teeth")));
    }

    /// <summary>Each mesh's two bodies and tooth counts, to read where its teeth stand (#85).</summary>
    private readonly List<(MeshSpec Mesh, RigidBody3D A, RigidBody3D B, double TeethA, double TeethB)> _meshTeeth = [];

    /// <summary>
    /// How far a mesh's second gear stands from its partner's gaps as the bodies are now, degrees of its turn
    /// (<see cref="GearPhase.ErrorDegrees"/>): 0 tooth in gap, ±180/z tooth on tooth. Read from the bodies'
    /// own frames, a generated gear's tooth on its local +X, so it shows the teeth as drawn, at rest or turning.
    /// </summary>
    private static double MeshError(RigidBody3D a, RigidBody3D b, double za, double zb)
    {
        static double Toward(RigidBody3D from, RigidBody3D to)
        {
            var local = from.GlobalTransform.Basis.Inverse() * (to.GlobalPosition - from.GlobalPosition);
            return Math.Atan2(local.Y, local.X);
        }
        double sense = a.GlobalTransform.Basis.Z.Dot(b.GlobalTransform.Basis.Z) >= 0 ? 1 : -1;
        return GearPhase.ErrorDegrees(za, 0, Toward(a, b), zb, 0, Toward(b, a), sense);
    }

    /// <summary>The trains no crank turns (issue #113): their meshes, and the sim parts keyed on their arbors, as ShaftLinks.</summary>
    private void BuildDrivenTrains(Dictionary<string, string> leader, Dictionary<string, List<(string To, double Factor)>> edges, HashSet<string> cranked)
    {
        var shafts = new Dictionary<string, IShaft>();
        IShaft Shaft(string id) => shafts.TryGetValue(id, out var s) ? s
            : shafts[id] = IsSimTurned(id) ? SimShaft(id)
                : ShaftEnd(id) ?? throw new MachineFormatException(
                    // a cart's wheel rides on its chassis, not on an axle fixed in the world (the hodometer, #127, needs that)
                    $"{id} doesn't turn on an axle fixed in the world, so it can't drive or be driven by a gear train yet", Runtime.Def.Part(id)?.Location);

        // the sim parts on arbors: each a link of ratio ±1 to its arbor's lead, and a node of that lead's train
        var simEdges = new List<(string Sim, string Lead, double Sense, ArborSpec Arbor)>();
        foreach (var arbor in Runtime.Def.Arbors)
            foreach (var sim in arbor.Parts.Where(IsSimTurned))
            {
                string lead = ArborLead(arbor);
                simEdges.Add((sim, lead, SimSense(sim, lead, arbor.Location), arbor));
            }

        // each driven train's members, with their speeds as multiples of its first's; a loop of meshes that disagrees locks up
        var adjacency = new Dictionary<string, List<(string To, double Factor)>>();
        foreach (var (from, list) in edges.Where(e => !cranked.Contains(e.Key)))
            adjacency[from] = [.. list];
        foreach (var (sim, lead, sense, _) in simEdges.Where(e => !cranked.Contains(e.Lead)))
        {
            adjacency.TryAdd(lead, []);
            adjacency.TryAdd(sim, []);
            adjacency[sim].Add((lead, sense));       // ω_lead = sense · ω_sim
            adjacency[lead].Add((sim, sense));       // and back: sense is ±1
        }
        var factor = new Dictionary<string, double>();
        foreach (var start in adjacency.Keys)
        {
            if (factor.ContainsKey(start)) continue;
            var members = new List<string> { start };
            factor[start] = 1;
            var queue = new Queue<string>([start]);
            while (queue.Count > 0)
            {
                var here = queue.Dequeue();
                foreach (var (to, f) in adjacency[here])
                {
                    double want = factor[here] * f;
                    if (factor.TryGetValue(to, out var had))
                    {
                        if (Math.Abs(had - want) > 1e-9 * Math.Abs(want))
                            throw new MachineFormatException($"gear {to} is driven two ways at once: the train locks up", Runtime.Def.Part(to)!.Location);
                        continue;
                    }
                    factor[to] = want;
                    members.Add(to);
                    queue.Enqueue(to);
                }
            }
            // what each Jolt body in it feels when it is slowed: the train's inertia, seen from it
            var train = members.Select(id => (Shaft(id), factor[id])).ToList();
            foreach (var id in members.Where(id => !IsSimTurned(id)))
            {
                var lead = _bodiesById[id];
                foreach (var body in _arborMates.GetValueOrDefault(lead, []).Prepend(lead))
                {
                    _trainOf[body] = (train, factor[id]);
                    Undamped(body);   // nothing slows it but what the machine says
                }
            }
        }

        foreach (var m in Runtime.Def.Meshes)
        {
            string a = leader[m.A], b = leader[m.B];
            if (cranked.Contains(a) || a == b) continue;
            double ratio = -Runtime.Def.Part(m.A)!.Number("teeth") / Runtime.Def.Part(m.B)!.Number("teeth");
            RigidBody3D bodyA = _bodiesById[a], bodyB = _bodiesById[b];
            AddDrivenLink(new ShaftLink(Shaft(a), Shaft(b), ratio, m.Efficiency), m.A, m.B, bodyA, bodyB);
        }
        foreach (var (sim, lead, sense, _) in simEdges)
            AddDrivenLink(new ShaftLink(Shaft(sim), Shaft(lead), sense), sim, lead, null, _bodiesById[lead]);
    }

    private void AddDrivenLink(ShaftLink link, string fromId, string toId, RigidBody3D? from, RigidBody3D to)
    {
        // what the link carries, shown under the wheel it drives
        float below = (float)(Runtime.Def.Part(toId)?.Number("radius", 0.2) ?? 0.2) + 0.15f;
        var label = new Label3D
        {
            Position = to.GlobalPosition + new Vector3(0, -below, 0), FontSize = 22, OutlineSize = 6, PixelSize = 0.0022f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            Modulate = new Color(1f, 0.85f, 0.55f),
        };
        AddChild(label);
        var d = new DrivenLink { Link = link, FromId = fromId, ToId = toId, FromBody = from, ToBody = to, Label = label };
        d.LastFrom = from is not null ? RawAngle(from, _hinges[from].Axis) : SimAngle(fromId);
        d.LastTo = RawAngle(to, _hinges[to].Axis);
        _drivenLinks.Add(d);
        _drivenShaftLinks.Add(link);
    }

    /// <summary>
    /// Which way a sim-turned part turns its arbor's lead wheel, about the lead's
    /// hinge axis: +1 if the part's forward turn is a positive turn about it. A
    /// water wheel's and a windmill's axle runs along Z; an overshot wheel turns
    /// clockwise seen from +Z (its loaded buckets go down the +X side), an
    /// undershot one, a windmill and a jet wheel anticlockwise. The wheel must sit
    /// on the part's own axle line.
    /// </summary>
    private double SimSense(string sim, string lead, SourceLocation? at)
    {
        var part = Runtime.Def.Part(sim)!;
        var body = _bodiesById[lead];
        var (pivot, axis) = _hinges[body];
        if (Math.Abs(axis.Z) < 0.99f)
            throw new MachineFormatException($"{lead} turns about {axis}; on {sim}'s axle it must turn about Z, as {sim} does", at);
        var off = pivot - V(part.At);
        if (new Vector2(off.X, off.Y).Length() > 0.05f)
            throw new MachineFormatException($"{lead} is {new Vector2(off.X, off.Y).Length():F2} m off {sim}'s axle; an arbor's wheels share one axle line", at);
        double forward = Runtime.WaterWheels.TryGetValue(sim, out var w) && w.Buckets > 0 ? -1 : 1;
        return forward * Math.Sign(axis.Z);
    }

    /// <summary>The inertia a body of a driven train has to stop, kg·m² about its own axle: every member's, scaled by the square of its speed against this one's; null outside a driven train.</summary>
    private double? TrainInertia(RigidBody3D body)
    {
        if (!_trainOf.TryGetValue(body, out var t)) return null;
        return t.Train.Sum(m => m.Shaft.ShaftInertia * (m.Factor / t.Factor) * (m.Factor / t.Factor));
    }

    /// <summary>
    /// How fast the train turns a body of it, rad/s about the body's axle: the
    /// train's angular momentum over its inertia, both seen from this body, so
    /// the speed the whole train would share if every pair were exactly on its
    /// ratio (the angle pull keeps a light wheel a little ahead of it); null
    /// outside a driven train.
    /// </summary>
    private double? TrainSpeed(RigidBody3D body)
    {
        if (!_trainOf.TryGetValue(body, out var t)) return null;
        double momentum = 0, inertia = 0;
        foreach (var (shaft, f) in t.Train)
        {
            double k = f / t.Factor, i = shaft.ShaftInertia;
            momentum += i * k * shaft.AngularVelocity;
            inertia += i * k * k;
        }
        return inertia > 0 ? momentum / inertia : 0;
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
        // the distance between their axles, in the plane square to them: x and y for an axle along z, as written; turned with the heading
        var axle = YawOf(a) * AxleOf(a);
        var between = new Vector3((float)(a.At.X - b.At.X), (float)(a.At.Y - b.At.Y), (float)(a.At.Z - b.At.Z));
        double apart = (between - axle * axle.Dot(between)).Length();
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

    /// <summary>
    /// Couples the driven trains for one tick: after the sim has stepped its
    /// turning parts, before Jolt steps its bodies. Meshes between Jolt wheels
    /// are pulled back onto the ratio's angle as well as its speed; the trains'
    /// bearings rub before the exchange, so it shares their friction out and
    /// every gear starts Jolt's step on its ratio (#85).
    /// </summary>
    private void CoupleDrivenTrains(double dt)
    {
        if (_drivenLinks.Count == 0) return;
        foreach (var d in _drivenLinks)
        {
            double to = RawAngle(d.ToBody!, _hinges[d.ToBody!].Axis);
            d.ToAngle += Unwrap(to - d.LastTo);
            d.LastTo = to;
            double from = d.FromBody is null ? SimAngle(d.FromId) : RawAngle(d.FromBody, _hinges[d.FromBody].Axis);
            d.FromAngle += Unwrap(from - d.LastFrom);
            d.LastFrom = from;
            // a gentle pull, at most a tenth of the speed the ratio asks for: a train
            // that friction has stopped stays stopped, its teeth a little off true; a
            // crank on a water wheel's axle keeps the wheel's angle, so its pin's mean
            // speed is the wheel's, though its load slows it within every step
            double most = 0.1 * Math.Abs(d.Link.Ratio * d.Link.From.AngularVelocity);
            d.Link.Bias = Math.Clamp(20 * (d.Link.Ratio * d.FromAngle - d.ToAngle), -most, most);
        }
        FrictionDrivenTrains(dt);
        ShaftLink.StepAll(_drivenShaftLinks, dt, 8);
    }

    private void DrawGearTrains()
    {
        foreach (var d in _drivenLinks)
        {
            var l = d.Link;
            double rpm = l.To.AngularVelocity * 60 / Math.Tau;
            d.Label.Text = d.FromBody is null
                ? $"{d.FromId} turns {d.ToId}\n{Math.Abs(rpm):F1} rpm · {-l.DriverTorque:F2} N·m · {l.Power:F1} W"
                : $"{d.FromId} → {d.ToId}\n{Math.Abs(rpm):F1} rpm · {l.Torque:F2} N·m";
        }
    }

    /// <summary>
    /// For the trace: what each driven link carries. On the driven part,
    /// drive-torque (N·m it is turned with, forward about its axle) and
    /// drive-power (W); on the driving part, load-torque (N·m the link holds
    /// it back with, ratio × torque / η while it drives).
    /// </summary>
    private IEnumerable<(string Key, double Value)> GearTraceFields()
    {
        // where each mesh's teeth stand (#85): degrees the second gear is off its partner's gaps
        foreach (var (m, a, b, za, zb) in _meshTeeth)
            yield return ($"{m.B}.mesh-error", MeshError(a, b, za, zb));
        foreach (var d in _drivenLinks)
        {
            yield return ($"{d.ToId}.drive-torque", d.Link.Torque);
            yield return ($"{d.ToId}.drive-power", d.Link.Power);            // the driver's own sense: a sim part turns forward positive; a Jolt gear, positive about its axle
            yield return ($"{d.FromId}.load-torque", -d.Link.DriverTorque * Math.Sign(d.Link.From.AngularVelocity == 0 ? 1 : d.Link.From.AngularVelocity));
        }
    }

    /// <summary>For telemetry: each driven gear's turns against what its ratio says.</summary>
    private IEnumerable<string> GearReport() =>
        _gearFollowers.Select(g => $"{g.Body.Name} turned {g.Angle / Math.Tau:F3} (ratio says {g.Factor * g.RootAngle / Math.Tau:F3})")
            .Concat(_drivenLinks.Select(d => $"{d.FromId}->{d.ToId} {d.Link.To.AngularVelocity * 60 / Math.Tau:F2}rpm τ={d.Link.Torque:F3}N·m driver τ={d.Link.DriverTorque:F3}N·m"))
            .Concat(_meshTeeth.Select(t => $"{t.Mesh.A}/{t.Mesh.B} teeth {MeshError(t.A, t.B, t.TeethA, t.TeethB):F3}° off the gaps (half a tooth is {180 / t.TeethB:F2}°)"));
}
