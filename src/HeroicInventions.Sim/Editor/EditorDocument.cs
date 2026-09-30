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
    public string? Source { get; private set; }
    public double Ambient { get; set; } = 20;   // °C
    public SunSpec? Sun { get; set; }
    public IReadOnlyDictionary<string, PartSpec> Parts => _parts;
    public IReadOnlyDictionary<string, PipeSpec> Pipes => _pipes;
    public IReadOnlyList<ConnectSpec> Connects => _connects;

    // Everything this editor doesn't yet author (ropes, lifts, channels, ...)
    // is preserved unedited on a load/save round trip rather than dropped.
    private IReadOnlyList<RopeSpec> _ropes = [];
    private IReadOnlyList<SourceSpec> _sources = [];
    private IReadOnlyList<ChannelSpec> _channels = [];
    private IReadOnlyList<CylinderSpec> _cylinders = [];
    private IReadOnlyList<LiftSpec> _lifts = [];
    private IReadOnlyList<MeshSpec> _meshes = [];
    private IReadOnlyList<ArborSpec> _arbors = [];
    private IReadOnlyList<SealedAirSpec> _sealedAir = [];

    /// <summary>Starts a fresh, empty document.</summary>
    public static EditorDocument New(string name) => new() { Name = name };

    /// <summary>Loads an existing .machine file's parts, pipes and connects for editing; carries everything else through unchanged.</summary>
    public static EditorDocument Load(MachineDef def)
    {
        var doc = new EditorDocument { Name = def.Name, Source = def.Source, Ambient = def.Ambient, Sun = def.Sun };
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

    /// <summary>Removes a part and every pipe/connect touching it — dragging a part off the palette back out, or deleting one.</summary>
    public void RemovePart(string id)
    {
        _parts.Remove(id);
        foreach (var pipeId in _pipes.Where(kv => kv.Value.From.Part == id || kv.Value.To.Part == id).Select(kv => kv.Key).ToList())
            _pipes.Remove(pipeId);
        _connects.RemoveAll(c => c.A.Part == id || c.B.Part == id);
    }

    public PartSpec Move(string id, Vec3 to) => Replace(id, p => p with { At = to });
    public PartSpec SetMaterial(string id, string material) => Replace(id, p => p with { Material = material });

    /// <summary>Sets one numeric prop (a tank's area, a lever's length, ...), keeping every other prop as is.</summary>
    public PartSpec SetProp(string id, string key, double value) =>
        Replace(id, p => p with { Props = Merge(p.Props, key, new SNumber(value)) });

    /// <summary>Sets a prop that names another part (a mirror's #:onto, a float valve's #:on), keeping every other prop as is.</summary>
    public PartSpec SetName(string id, string key, string target) =>
        Replace(id, p => p with { Props = Merge(p.Props, key, new SSymbol(target)) });

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
                                IReadOnlyList<(double X, double Z)>? via = null, string? onto = null)
    {
        var spec = new ChannelSpec(id, from, to, end, width, length, null, via, onto);
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

    /// <summary>The current state as a runnable, saveable <see cref="MachineDef"/>.</summary>
    public MachineDef ToMachineDef() => new()
    {
        Name = Name,
        Source = Source,
        Ambient = Ambient,
        Sun = Sun,
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
    };
}
