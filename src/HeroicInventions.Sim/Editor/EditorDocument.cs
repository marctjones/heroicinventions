using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>A port, named by the part that carries it — what the editor snaps to and links between.</summary>
public readonly record struct PortHandle(string PartId, string PortName)
{
    public PortRef ToRef() => new(PartId, PortName);
}

/// <summary>
/// The build-mode editor's in-memory machine: everything a session of
/// dragging parts, snapping ports and picking materials does, kept as
/// plain data so it can be driven by tests as easily as by the Godot UI.
/// Godot's job (BuildMode.cs) is thin — turning clicks and drags into
/// calls here, and this document's state into meshes on screen.
///
/// A document round-trips through <see cref="MachineDef"/>: <see cref="Load"/>
/// starts one from a parsed file (so an existing game/machines/*.machine
/// can be reopened and edited), and <see cref="ToMachineDef"/> turns the
/// current state back into one MachineRuntime can run and MachineWriter
/// can save — the same object graph either path produces.
/// </summary>
public sealed class EditorDocument
{
    private readonly Dictionary<string, PartSpec> _parts = [];
    private readonly Dictionary<string, PipeSpec> _pipes = [];
    private readonly List<ConnectSpec> _connects = [];
    private int _nextPipeId = 1;

    public string Name { get; set; } = "untitled";
    public string? Source { get; set; }
    public double Ambient { get; set; } = 20;   // °C
    public SunSpec? Sun { get; set; }
    public Planet Planet { get; set; } = Planet.Earth;
    public WeatherSpec? Weather { get; set; }
    public IReadOnlyDictionary<string, PartSpec> Parts => _parts;
    public IReadOnlyDictionary<string, PipeSpec> Pipes => _pipes;
    public IReadOnlyList<ConnectSpec> Connects => _connects;
    public IReadOnlyList<RopeSpec> Ropes => _ropes;
    public IReadOnlyList<MeshSpec> Meshes => _meshes;
    public IReadOnlyList<ArborSpec> Arbors => _arbors;
    public IReadOnlyList<SealedAirSpec> SealedAir => _sealedAir;
    public IReadOnlyList<CylinderSpec> Cylinders => _cylinders;
    public IReadOnlyList<TriggerSpec> Triggers => _triggers;
    public IReadOnlyList<FollowSpec> Follows => _follows;
    public IReadOnlyList<BeltSpec> Belts => _belts;
    public IReadOnlyList<WakeSpec> Wakes => _wakes;
    public IReadOnlyList<JointSpec> Joints => _joints;
    public IReadOnlyList<SourceSpec> Sources => _sources;
    public IReadOnlyList<ChannelSpec> Channels => _channels;
    public IReadOnlyList<LiftSpec> Lifts => _lifts;

    // Links between parts: each has an Add method here and goes through
    // RemoveLink / RemovePart, so an edit never leaves a rope or mesh naming a part that is gone.
    private IReadOnlyList<RopeSpec> _ropes = [];
    private IReadOnlyList<SourceSpec> _sources = [];
    private IReadOnlyList<ChannelSpec> _channels = [];
    private IReadOnlyList<CylinderSpec> _cylinders = [];
    private IReadOnlyList<LiftSpec> _lifts = [];
    private IReadOnlyList<MeshSpec> _meshes = [];
    private IReadOnlyList<ArborSpec> _arbors = [];
    private IReadOnlyList<SealedAirSpec> _sealedAir = [];
    private IReadOnlyList<TriggerSpec> _triggers = [];
    private IReadOnlyList<FollowSpec> _follows = [];
    private IReadOnlyList<BeltSpec> _belts = [];
    private IReadOnlyList<WakeSpec> _wakes = [];
    private IReadOnlyList<JointSpec> _joints = [];

    /// <summary>Starts a fresh, empty document.</summary>
    public static EditorDocument New(string name) => new() { Name = name };

    /// <summary>Loads an existing .machine file's parts, pipes and connects for editing; carries everything else through unchanged.</summary>
    public static EditorDocument Load(MachineDef def)
    {
        var doc = new EditorDocument { Name = def.Name, Source = def.Source, Ambient = def.Ambient, Sun = def.Sun, Planet = def.Planet, Weather = def.Weather };
        foreach (var p in def.Parts) doc._parts[p.Id] = p;
        foreach (var p in def.Pipes) doc._pipes[p.Id] = p;
        doc._connects.AddRange(def.Connects);
        doc._ropes = def.Ropes;
        doc._sources = def.Sources;
        doc._channels = def.Channels;
        doc._cylinders = def.Cylinders;
        doc._lifts = def.Lifts;
        doc._meshes = def.Meshes;
        doc._arbors = def.Arbors;
        doc._sealedAir = def.SealedAir;
        doc._triggers = def.Triggers;
        doc._follows = def.Follows;
        doc._belts = def.Belts;
        doc._wakes = def.Wakes;
        doc._joints = def.Joints;
        int maxPipe = def.Pipes.Select(p => int.TryParse(p.Id.AsSpan(p.Id.LastIndexOf('-') + 1), out int n) ? n : 0).DefaultIfEmpty(0).Max();
        doc._nextPipeId = maxPipe + 1;
        return doc;
    }

    /// <summary>Places a part, given a unique id — throws if the id is already taken.</summary>
    public PartSpec AddPart(PartSpec part)
    {
        if (_parts.ContainsKey(part.Id))
            throw new InvalidOperationException($"a part named {part.Id} is already in the machine");
        _parts[part.Id] = part;
        return part;
    }

    /// <summary>
    /// Removes a part and every link naming it — pipes, connects, ropes (as an end,
    /// drum or pulley), meshes, sealed-air groups, cylinders, lifts, inflows and
    /// channels — so a deleted part leaves nothing dangling for (check) to trip on.
    /// An arbor keeps its other wheels, unless fewer than two remain.
    /// </summary>
    public void RemovePart(string id)
    {
        _parts.Remove(id);
        foreach (var pipeId in _pipes.Where(kv => kv.Value.From.Part == id || kv.Value.To.Part == id).Select(kv => kv.Key).ToList())
            _pipes.Remove(pipeId);
        _connects.RemoveAll(c => c.A.Part == id || c.B.Part == id);
        _ropes = _ropes.Where(r => r.From.Part != id && r.To.Part != id && r.WindOn != id && r.Turns != id).ToList();
        _meshes = _meshes.Where(m => m.A != id && m.B != id).ToList();
        _arbors = _arbors.Select(a => a with { Parts = a.Parts.Where(p => p != id).ToList() }).Where(a => a.Parts.Count >= 2).ToList();
        _sealedAir = _sealedAir.Select(a => a with { Tanks = a.Tanks.Where(t => t != id).ToList() }).Where(a => a.Tanks.Count >= 2).ToList();
        _cylinders = _cylinders.Where(c => c.Piston != id && c.Boiler != id).ToList();
        _lifts = _lifts.Where(l => l.By != id && l.From != id && l.To != id).ToList();
        _sources = _sources.Where(s => s.Into != id).ToList();
        _channels = _channels.Where(c => c.From.Part != id && c.To?.Part != id).ToList();
        // a trigger that watches this body, or watches or sets a field on this part, has nothing left to do
        _triggers = _triggers.Where(t => t.Body != id && t.WatchTarget != id && t.Actions.All(a => a.Target != id)).ToList();
        // a follow with its lever gone, its rope gone or nothing left to set
        _follows = _follows.Where(f => f.Lever != id && f.Target != id).ToList();
        _follows = _follows.Where(f => f.Rope is null || _ropes.Any(r => r.Id == f.Rope)).ToList();
        _belts = _belts.Where(b => b.A != id && b.B != id).ToList();
        _joints = _joints.Where(j => j.A != id && j.B != id).ToList();
        // a wake that watches a field of the part, or is woken by one, has nothing to watch
        _wakes = _wakes.Where(w => w.Terms.Concat(w.Events).All(t => t.Target != id)).ToList();
    }

    /// <summary>True if a part or a link (pipe, rope, inflow, channel, lift, cylinder) has this id — what a prop like a sluice's #:on or a wheel's #:race may name.</summary>
    public bool HasName(string id) =>
        _parts.ContainsKey(id) || _pipes.ContainsKey(id) || _ropes.Any(r => r.Id == id) || _sources.Any(s => s.Id == id) ||
        _channels.Any(c => c.Id == id) || _lifts.Any(l => l.Id == id) || _cylinders.Any(c => c.Id == id) || _triggers.Any(t => t.Id == id) || _follows.Any(f => f.Id == id) || _belts.Any(b => b.Id == id) || _wakes.Any(w => w.Id == id) ||
        _joints.Any(j => j.Id == id);

    /// <summary>Removes the pipe, rope, inflow, channel, lift or cylinder with this id; false if none has it.</summary>
    public bool RemoveLink(string id)
    {
        if (_pipes.Remove(id)) return true;
        int before = _ropes.Count + _sources.Count + _channels.Count + _lifts.Count + _cylinders.Count + _triggers.Count + _follows.Count + _belts.Count + _joints.Count + _wakes.Count;
        _ropes = _ropes.Where(r => r.Id != id).ToList();
        _sources = _sources.Where(s => s.Id != id).ToList();
        _channels = _channels.Where(c => c.Id != id).ToList();
        _lifts = _lifts.Where(l => l.Id != id).ToList();
        _cylinders = _cylinders.Where(c => c.Id != id).ToList();
        _triggers = _triggers.Where(t => t.Id != id).ToList();
        _follows = _follows.Where(f => f.Id != id).ToList();
        _follows = _follows.Where(f => f.Rope is null || _ropes.Any(r => r.Id == f.Rope)).ToList();   // a rope removed takes its follows
        _belts = _belts.Where(b => b.Id != id).ToList();
        _wakes = _wakes.Where(w => w.Id != id).ToList();
        _joints = _joints.Where(j => j.Id != id).ToList();
        return _ropes.Count + _sources.Count + _channels.Count + _lifts.Count + _cylinders.Count + _triggers.Count + _follows.Count + _belts.Count + _joints.Count + _wakes.Count != before;
    }

    /// <summary>Removes the mesh joining these two gears; false if they are not meshed.</summary>
    public bool RemoveMesh(string a, string b)
    {
        int n = _meshes.Count;
        _meshes = _meshes.Where(m => !(m.A == a && m.B == b || m.A == b && m.B == a)).ToList();
        return _meshes.Count != n;
    }

    /// <summary>Takes a wheel off every arbor it is fixed on (an arbor left with one wheel goes).</summary>
    public bool RemoveFromArbor(string part)
    {
        int n = _arbors.Sum(a => a.Parts.Count);
        _arbors = _arbors.Select(a => a with { Parts = a.Parts.Where(p => p != part).ToList() }).Where(a => a.Parts.Count >= 2).ToList();
        return _arbors.Sum(a => a.Parts.Count) != n;
    }

    /// <summary>Adds or replaces a port on a part (a second outlet on a tank); ports are keyed by name.</summary>
    public PartSpec SetPort(string id, PortSpec port) =>
        Replace(id, p => p with { Ports = [.. p.Ports.Where(x => x.Name != port.Name), port] });

    public PartSpec RemovePort(string id, string name) =>
        Replace(id, p => p with { Ports = p.Ports.Where(x => x.Name != name).ToList() });

    /// <summary>Replaces a part outright — for a part whose shape came from a file, not from a template.</summary>
    public PartSpec SetProps(string id, IReadOnlyDictionary<string, SExpr> props) => Replace(id, p => p with { Props = props });

    public PartSpec Move(string id, Vec3 to) => Replace(id, p => p with { At = to });
    public PartSpec SetMaterial(string id, string material) => Replace(id, p => p with { Material = material });

    /// <summary>Sets one numeric prop (a tank's area, a lever's length, ...), keeping every other prop as is.</summary>
    public PartSpec SetProp(string id, string key, double value) =>
        Replace(id, p => p with { Props = Merge(p.Props, key, new SNumber(value)) });

    /// <summary>Sets a prop that names another part (a mirror's #:onto, a float valve's #:on), keeping every other prop as is.</summary>
    public PartSpec SetName(string id, string key, string target) =>
        Replace(id, p => p with { Props = Merge(p.Props, key, new SSymbol(target)) });

    /// <summary>Sets a prop to any value (a flag, a string, a list, a symbol that is not a part), keeping every other prop as is.</summary>
    public PartSpec SetValue(string id, string key, SExpr value) =>
        Replace(id, p => p with { Props = Merge(p.Props, key, value) });

    private static IReadOnlyDictionary<string, SExpr> Merge(IReadOnlyDictionary<string, SExpr> props, string key, SExpr value)
    {
        var next = new Dictionary<string, SExpr>(props) { [key] = value };
        return next;
    }

    private PartSpec Replace(string id, Func<PartSpec, PartSpec> update)
    {
        if (!_parts.TryGetValue(id, out var p))
            throw new InvalidOperationException($"no part named {id}");
        var next = update(p);
        _parts[id] = next;
        return next;
    }

    /// <summary>A port's position in world space: the part's origin, raised by the port's height (see MachineRuntime.TankPort).</summary>
    public static Vec3 PortWorldPosition(PartSpec part, PortSpec port) => part.At with { Y = part.At.Y + port.Height };

    /// <summary>Every port in the machine except those already belonging to <paramref name="excludePart"/>, with its world position.</summary>
    public IEnumerable<(PartSpec Part, PortSpec Port, Vec3 World)> AllPorts(string? excludePart = null) =>
        _parts.Values.Where(p => p.Id != excludePart).SelectMany(p => p.Ports.Select(pt => (p, pt, PortWorldPosition(p, pt))));

    /// <summary>
    /// The nearest port to <paramref name="from"/> that is compatible with it and within
    /// <paramref name="radius"/> metres — what a dragged part's port highlights and snaps
    /// to. Ties break by whichever port is closest; returns null with nothing in range.
    /// </summary>
    public (PartSpec Part, PortSpec Port, LinkKind Kind)? NearestCompatiblePort(string partId, string portName, double radius)
    {
        if (!_parts.TryGetValue(partId, out var part)) return null;
        var port = part.Ports.FirstOrDefault(p => p.Name == portName);
        if (port is null) return null;
        var origin = PortWorldPosition(part, port);

        (PartSpec Part, PortSpec Port, LinkKind Kind, double Dist)? best = null;
        foreach (var (otherPart, otherPort, world) in AllPorts(excludePart: partId))
        {
            var kind = PortRules.Compatibility(part, port, otherPart, otherPort);
            if (kind == LinkKind.None) continue;
            double dist = Distance(origin, world);
            if (dist > radius) continue;
            if (best is null || dist < best.Value.Dist) best = (otherPart, otherPort, kind, dist);
        }
        return best is { } b ? (b.Part, b.Port, b.Kind) : null;
    }

    private static double Distance(Vec3 a, Vec3 b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));

    /// <summary>
    /// Links two ports if <see cref="PortRules"/> allows it: a water pair becomes a pipe
    /// (given a conductance — the palette's default is fine to start, adjustable after),
    /// a boiler/rotor steam pair becomes a connect. Throws with the reason otherwise, so
    /// the UI can show why a drop didn't take.
    /// </summary>
    public void Connect(PortHandle a, PortHandle b, double pipeConductance = 0.001)
    {
        if (!_parts.TryGetValue(a.PartId, out var pa)) throw new InvalidOperationException($"no part named {a.PartId}");
        if (!_parts.TryGetValue(b.PartId, out var pb)) throw new InvalidOperationException($"no part named {b.PartId}");
        var portA = pa.Ports.FirstOrDefault(p => p.Name == a.PortName) ?? throw new InvalidOperationException($"{a.PartId} has no port {a.PortName}");
        var portB = pb.Ports.FirstOrDefault(p => p.Name == b.PortName) ?? throw new InvalidOperationException($"{b.PartId} has no port {b.PortName}");

        switch (PortRules.Compatibility(pa, portA, pb, portB))
        {
            case LinkKind.Pipe:
                string id = $"pipe-{_nextPipeId++}";
                _pipes[id] = new PipeSpec(id, a.ToRef(), b.ToRef(), pipeConductance, Jet: false, Location: null);
                break;
            case LinkKind.SteamConnect:
                string boilerId = pa.Kind == "boiler" ? pa.Id : pb.Id;
                if (PortRules.BoilerAlreadyFeedsARotor(ToMachineDef(), boilerId))
                    throw new InvalidOperationException($"boiler {boilerId} already feeds a rotor; one rotor per boiler");
                _connects.Add(new ConnectSpec(a.ToRef(), b.ToRef(), null));
                break;
            default:
                throw new InvalidOperationException($"{a.PartId}.{a.PortName} ({portA.Kind}) can't join {b.PartId}.{b.PortName} ({portB.Kind})");
        }
    }

    /// <summary>Removes a pipe by id (undoing a snap, or clearing one to rewire it).</summary>
    public void RemovePipe(string id) => _pipes.Remove(id);

    /// <summary>
    /// Adds a pipe exactly as the DSL's own <c>(pipe id from to #:conductance c)</c>
    /// clause would — no PortRules check here, the same way machine.rkt's clause
    /// patterns don't check either; <see cref="Editor.BuildSession"/>'s <c>(check)</c>
    /// command is the one place that validates a whole machine, by actually
    /// building it. <see cref="Connect"/> is the checked, UI-facing equivalent
    /// used for snapping two ports together.
    /// </summary>
    public PipeSpec AddPipe(string id, PortRef from, PortRef to, double conductance, bool jet = false)
    {
        if (_pipes.ContainsKey(id)) throw new InvalidOperationException($"a pipe named {id} already exists");
        var spec = new PipeSpec(id, from, to, conductance, jet, null);
        _pipes[id] = spec;
        return spec;
    }

    /// <summary>Adds a connect clause unchecked — see <see cref="AddPipe"/>.</summary>
    public ConnectSpec AddConnect(PortRef a, PortRef b)
    {
        var spec = new ConnectSpec(a, b, null);
        _connects.Add(spec);
        return spec;
    }

    /// <summary>Adds an inflow clause unchecked — see <see cref="AddPipe"/>.</summary>
    public SourceSpec AddInflow(string id, string into, double flow)
    {
        var spec = new SourceSpec(id, into, flow, null);
        _sources = [.. _sources, spec];
        return spec;
    }

    /// <summary>Adds a channel clause unchecked — see <see cref="AddPipe"/>.</summary>
    public ChannelSpec AddChannel(string id, PortRef from, PortRef? to, Vec3? end, double width, double? length,
                                IReadOnlyList<(double X, double Z)>? via = null, string? onto = null,
                                bool dynamic = false, int? cells = null)
    {
        var spec = new ChannelSpec(id, from, to, end, width, length, null, via, onto) { Dynamic = dynamic, Cells = cells };
        _channels = [.. _channels, spec];
        return spec;
    }

    /// <summary>Adds a lift clause unchecked — see <see cref="AddPipe"/>.</summary>
    public LiftSpec AddLift(string id, string by, string from, string to, double? current, string? currentFrom)
    {
        var spec = new LiftSpec(id, by, from, to, current, null) { CurrentFrom = currentFrom };
        _lifts = [.. _lifts, spec];
        return spec;
    }

    /// <summary>Adds a rope clause unchecked — see <see cref="AddPipe"/>. Ids are unique across ropes.</summary>
    public RopeSpec AddRope(RopeSpec rope)
    {
        if (_ropes.Any(r => r.Id == rope.Id)) throw new InvalidOperationException($"a rope named {rope.Id} already exists");
        _ropes = [.. _ropes, rope];
        return rope;
    }

    /// <summary>Replaces the rope with the same id.</summary>
    public void ReplaceRope(RopeSpec rope) => _ropes = _ropes.Select(r => r.Id == rope.Id ? rope : r).ToList();

    /// <summary>Takes a tank out of its sealed-air group (a group left with one tank goes).</summary>
    public bool RemoveFromSealedAir(string tank)
    {
        int n = _sealedAir.Sum(a => a.Tanks.Count);
        _sealedAir = _sealedAir.Select(a => a with { Tanks = a.Tanks.Where(t => t != tank).ToList() }).Where(a => a.Tanks.Count >= 2).ToList();
        return _sealedAir.Sum(a => a.Tanks.Count) != n;
    }

    /// <summary>Puts two gears in mesh (the same pair twice is a no-op).</summary>
    public MeshSpec AddMesh(string a, string b)
    {
        var spec = new MeshSpec(a, b, null);
        if (!_meshes.Any(m => m.A == a && m.B == b || m.A == b && m.B == a)) _meshes = [.. _meshes, spec];
        return spec;
    }

    /// <summary>Fixes wheels on one axle; the first carries the bearing and any drive.</summary>
    public ArborSpec AddArbor(IReadOnlyList<string> parts)
    {
        var spec = new ArborSpec(parts, null);
        _arbors = [.. _arbors, spec];
        return spec;
    }

    /// <summary>Joins tanks into one sealed air space.</summary>
    public SealedAirSpec AddSealedAir(IReadOnlyList<string> tanks, double tubeVolume, double heatLoss = 0, double heatCapacity = 0)
    {
        var spec = new SealedAirSpec(tanks, tubeVolume, null, heatLoss, heatCapacity);
        _sealedAir = [.. _sealedAir, spec];
        return spec;
    }

    /// <summary>Adds a trigger clause unchecked — see <see cref="AddPipe"/>.</summary>
    public TriggerSpec AddTrigger(TriggerSpec trigger)
    {
        if (_triggers.Any(t => t.Id == trigger.Id)) throw new InvalidOperationException($"a trigger named {trigger.Id} already exists");
        _triggers = [.. _triggers, trigger];
        return trigger;
    }

    /// <summary>Adds a joint clause unchecked — see <see cref="AddPipe"/>.</summary>
    public JointSpec AddJoint(JointSpec joint)
    {
        if (HasName(joint.Id)) throw new InvalidOperationException($"something named {joint.Id} already exists");
        _joints = [.. _joints, joint];
        return joint;
    }

    /// <summary>Adds a follow clause unchecked — see <see cref="AddPipe"/>.</summary>
    public FollowSpec AddFollow(FollowSpec follow)
    {
        if (_follows.Any(f => f.Id == follow.Id)) throw new InvalidOperationException($"a follow named {follow.Id} already exists");
        _follows = [.. _follows, follow];
        return follow;
    }

    /// <summary>Adds a belt clause unchecked — see <see cref="AddPipe"/>.</summary>
    public BeltSpec AddBelt(BeltSpec belt)
    {
        if (_belts.Any(b => b.Id == belt.Id)) throw new InvalidOperationException($"a belt named {belt.Id} already exists");
        _belts = [.. _belts, belt];
        return belt;
    }

    public WakeSpec AddWake(WakeSpec wake)
    {
        if (_wakes.Any(w => w.Id == wake.Id)) throw new InvalidOperationException($"a wake named {wake.Id} already exists");
        _wakes = [.. _wakes, wake];
        return wake;
    }

    public void ReplaceBelt(BeltSpec belt) => _belts = _belts.Select(b => b.Id == belt.Id ? belt : b).ToList();

    /// <summary>Joins a piston to the boiler that feeds its cylinder.</summary>
    public CylinderSpec AddCylinder(string id, string piston, string boiler, double? injectionTemperature)
    {
        if (_cylinders.Any(c => c.Id == id)) throw new InvalidOperationException($"a cylinder named {id} already exists");
        var spec = new CylinderSpec(id, piston, boiler, injectionTemperature, null);
        _cylinders = [.. _cylinders, spec];
        return spec;
    }

    /// <summary>The current state as a runnable, saveable <see cref="MachineDef"/>.</summary>
    public MachineDef ToMachineDef() => new()
    {
        Name = Name,
        Source = Source,
        Ambient = Ambient,
        Sun = Sun,
        Planet = Planet,
        Weather = Weather,
        Parts = _parts.Values.ToList(),
        Pipes = _pipes.Values.ToList(),
        Connects = _connects.ToList(),
        SealedAir = _sealedAir,
        Ropes = _ropes,
        Arbors = _arbors,
        Meshes = _meshes,
        Lifts = _lifts,
        Sources = _sources,
        Channels = _channels,
        Cylinders = _cylinders,
        Triggers = _triggers,
        Follows = _follows,
        Belts = _belts,
        Wakes = _wakes,
        Joints = _joints,
    };
}
