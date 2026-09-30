using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Machines;

/// <summary>
/// A shaft between two machines' turning parts (issue #78). Each tick,
/// before the machines step, it makes the driven end turn at
/// <see cref="Ratio"/> times the driving end's speed by trading angular
/// momentum between them: an impulse λ on the driven end and −Ratio·λ on the
/// driving end, the least that meets ω_to = Ratio·ω_from. What one end gains
/// the other gives up, so power in equals power out and, for a plain shaft
/// (Ratio 1), the torque is the same on both sides. Between ticks each
/// machine turns its own end under its own drive and load; the next tick's
/// exchange is the torque the shaft carried.
/// </summary>
public sealed class ShaftLink(IShaft from, IShaft to, double ratio)
{
    public IShaft From { get; } = from;
    public IShaft To { get; } = to;
    public double Ratio { get; } = ratio;
    /// <summary>N·m the shaft turns the driven end with, forward positive (the last tick's exchange over its length).</summary>
    public double Torque { get; private set; }
    /// <summary>rad/s of the driving end.</summary>
    public double AngularVelocity => From.AngularVelocity;
    public double Rpm => AngularVelocity * 60 / (2 * Math.PI);
    /// <summary>W delivered to the driven end.</summary>
    public double Power => Torque * To.AngularVelocity;

    public void Step(double dt)
    {
        double ia = From.ShaftInertia, ib = To.ShaftInertia;
        if (!(ia > 0 && ib > 0) || dt <= 0) { Torque = 0; return; }
        double slip = To.AngularVelocity - Ratio * From.AngularVelocity;
        double lambda = -slip / (1 / ib + Ratio * Ratio / ia);
        To.AddAngularImpulse(lambda);
        From.AddAngularImpulse(-Ratio * lambda);
        Torque = lambda / dt;
    }
}

/// <summary>
/// The links of a world (issue #78) made live against its machines'
/// runtimes: cross-machine pipes, stepped by the same law and substep as a
/// pipe inside one machine (<see cref="FluidNetwork"/>), and shafts. Built
/// again whenever a machine is rebuilt (a live edit), so each link holds the
/// new machine's tanks and wheels, which have already taken over the old
/// ones' water and speed. A link whose machine, part or port has gone is
/// kept, unfinished, with the reason, and does nothing until it is mended.
/// Each tick: StepPipes, then the machines, then StepShafts.
/// </summary>
public sealed class WorldLinks
{
    public sealed class Link
    {
        public required LinkSpec Spec { get; init; }
        /// <summary>Why the link can't work as it stands, or null when it is joined up.</summary>
        public string? Unfinished { get; init; }
        public Pipe? Pipe { get; init; }
        public ShaftLink? Shaft { get; init; }
    }

    private readonly FluidNetwork _pipes = new();
    private readonly List<Link> _links = [];
    private readonly Dictionary<string, Func<double>> _getters = [];

    public IReadOnlyList<Link> All => _links;
    public IReadOnlyDictionary<string, Func<double>> FieldGetters => _getters;

    /// <summary>
    /// Resolves every link. <paramref name="machine"/> finds a placement's
    /// runtime by label; <paramref name="shaftFor"/>, if given, is asked first
    /// for a shaft end (the game offers its Jolt bodies on axles through it),
    /// then the sim's own turning parts are tried: windmills, water wheels and
    /// jet wheels.
    /// </summary>
    public static WorldLinks Build(IEnumerable<LinkSpec> specs, Func<string, MachineRuntime?> machine, Func<LinkEnd, IShaft?>? shaftFor = null)
    {
        var w = new WorldLinks();
        foreach (var spec in specs)
        {
            Link link;
            try
            {
                link = spec.Kind switch
                {
                    "pipe" => w.BuildPipe(spec, machine),
                    "shaft" => BuildShaft(spec, machine, shaftFor),
                    _ => throw new MachineFormatException($"link {spec.Id}: a link is a pipe or a shaft, not {spec.Kind}"),
                };
            }
            catch (MachineFormatException e)
            {
                link = new Link { Spec = spec, Unfinished = e.Message };
            }
            w._links.Add(link);
            w.Register(link);
        }
        return w;
    }

    private Link BuildPipe(LinkSpec spec, Func<string, MachineRuntime?> machine)
    {
        var (from, fromY) = Port(spec, spec.From, machine);
        var (to, toY) = Port(spec, spec.To, machine);
        var pipe = _pipes.AddPipe(new Pipe(spec.Id, from, fromY, to, toY, spec.Conductance));
        return new Link { Spec = spec, Pipe = pipe };
    }

    private static (Tank Tank, double Elevation) Port(LinkSpec spec, LinkEnd end, Func<string, MachineRuntime?> machine)
    {
        var rt = machine(end.Label) ?? throw new MachineFormatException($"link {spec.Id}: no machine is placed as {end.Label}");
        if (!rt.Tanks.TryGetValue(end.Part, out var tank))
            throw new MachineFormatException($"link {spec.Id}: {end.Label} has no tank {end.Part}");
        var part = rt.Def.Part(end.Part)!;
        var port = part.Ports.FirstOrDefault(p => p.Name == end.Port)
            ?? throw new MachineFormatException($"link {spec.Id}: {end.Label}'s tank {end.Part} has no port {end.Port}");
        if (port.Kind != "water")
            throw new MachineFormatException($"link {spec.Id}: {end} is a {port.Kind} port; a pipe joins water ports");
        return (tank, part.At.Y + port.Height);
    }

    private static Link BuildShaft(LinkSpec spec, Func<string, MachineRuntime?> machine, Func<LinkEnd, IShaft?>? shaftFor)
    {
        IShaft End(LinkEnd end)
        {
            if (shaftFor?.Invoke(end) is { } given) return given;
            var rt = machine(end.Label) ?? throw new MachineFormatException($"link {spec.Id}: no machine is placed as {end.Label}");
            if (rt.Windmills.TryGetValue(end.Part, out var mill)) return mill;
            if (rt.WaterWheels.TryGetValue(end.Part, out var wheel)) return wheel;
            if (rt.JetWheels.TryGetValue(end.Part, out var jet)) return jet;
            throw new MachineFormatException(rt.Def.Part(end.Part) is { } p
                ? $"link {spec.Id}: {end.Label}'s {end.Part} is a {p.Kind}, which doesn't turn on an axle a shaft can take"
                : $"link {spec.Id}: {end.Label} has no part {end.Part}");
        }
        var from = End(spec.From);
        var to = End(spec.To);
        if (ReferenceEquals(from, to)) throw new MachineFormatException($"link {spec.Id}: a shaft needs two different ends");
        return new Link { Spec = spec, Shaft = new ShaftLink(from, to, spec.Ratio) };
    }

    private void Register(Link link)
    {
        string id = link.Spec.Id;
        _getters[$"{id}.unfinished"] = () => link.Unfinished is null ? 0 : 1;
        if (link.Pipe is { } p)
            _getters[$"{id}.flow"] = () => p.Flow * 1000;                  // L/s, + from → to
        if (link.Shaft is { } s)
        {
            _getters[$"{id}.rpm"] = () => s.Rpm;                            // the driving end
            _getters[$"{id}.driven-rpm"] = () => s.To.AngularVelocity * 60 / (2 * Math.PI);
            _getters[$"{id}.torque"] = () => s.Torque;                      // N·m into the driven end
            _getters[$"{id}.power"] = () => s.Power;                        // W
        }
    }

    /// <summary>
    /// Moves water through the cross-machine pipes for dt. Call it before the
    /// machines step, so a machine's readings after its step include this
    /// tick's flow, as they would for a pipe of its own.
    /// </summary>
    public void StepPipes(double dt)
    {
        if (_pipes.Pipes.Count > 0) _pipes.Step(dt);
    }

    /// <summary>
    /// Couples the shafts' ends after the machines have stepped (each turning
    /// its own end under its own drive and load), so what the machines read
    /// out next is the shaft's one speed. In the game this also comes before
    /// the physics engine steps its bodies.
    /// </summary>
    public void StepShafts(double dt)
    {
        foreach (var l in _links) l.Shaft?.Step(dt);
    }
}
