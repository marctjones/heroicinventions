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
/// exchange is the torque the shaft carried. What acts on one end inside the
/// physics engine's step (a hinge motor holding its speed) reaches the other
/// only at that next exchange, a tick late: the split crane's drum ran a
/// tick of the shaft's torque behind the walkers' wheel. So in the game a
/// shaft between two Jolt bodies on one axle line is locked within the step
/// as well (<see cref="Locked"/>, #191). Between sim parts, whose drives and
/// loads are torques worked between exchanges, no such lag arises.
///
/// The same exchange couples the parts of one machine's gear train (issue
/// #113): each pair of gears in mesh is a link of Ratio −(teeth driving /
/// teeth driven), and a water wheel, windmill or jet wheel keyed on an arbor
/// with a Jolt wheel is a link of Ratio 1 between the two. A mesh loses a
/// share of what it carries, <see cref="Efficiency"/> η: passing power
/// forward the driving end gives up 1/η times the work the driven end
/// receives, so a load τ at the driven end is felt as Ratio·τ/η at the
/// driving end; driven backwards (the load overrunning), it gives back only
/// η of it. Several links sharing ends are solved together by
/// <see cref="StepAll"/>.
/// </summary>
public sealed class ShaftLink(IShaft from, IShaft to, double ratio, double efficiency = 1)
{
    public IShaft From { get; } = from;
    public IShaft To { get; } = to;
    public double Ratio { get; } = ratio;
    /// <summary>The share of the power passing forward that arrives, 0 &lt; η ≤ 1 (1 for a plain shaft).</summary>
    public double Efficiency { get; } = efficiency is > 0 and <= 1 ? efficiency : throw new ArgumentOutOfRangeException(nameof(efficiency), "an efficiency is more than 0 and at most 1");
    /// <summary>rad/s the driven end is to run ahead of Ratio × the driving end's speed this tick: a pull back onto the ratio's angle, so meshed teeth stay in each other's gaps. 0 for a plain shaft.</summary>
    public double Bias { get; set; }
    /// <summary>N·m the shaft turns the driven end with, forward positive (the last tick's exchange over its length; for a <see cref="Locked"/> shaft, with what the lock gave it in the step).</summary>
    public double Torque { get; private set; }
    /// <summary>N·m the driving end was turned with, forward positive: −Ratio·Torque/η while the link drives forward.</summary>
    public double DriverTorque { get; private set; }
    /// <summary>
    /// The physics engine also holds the two ends on the ratio inside its step (#191): in the game, a shaft
    /// between two Jolt bodies on one axle line is a lock between them as well as this exchange. The exchange
    /// still runs before the step; the lock carries what acts on either end within it (a motor holding one
    /// end at its speed). Its torque and speeds are then read after the step: see <see cref="ReadLocked"/>.
    /// </summary>
    public bool Locked { get; set; }
    private double _toAfterExchange = double.NaN, _exchanged, _dt, _fromAfterStep = double.NaN, _toAfterStep = double.NaN;
    /// <summary>rad/s of the driving end (a <see cref="Locked"/> shaft: as the last physics step left it).</summary>
    public double AngularVelocity => Locked && double.IsFinite(_fromAfterStep) ? _fromAfterStep : From.AngularVelocity;
    /// <summary>rad/s of the driven end, read as <see cref="AngularVelocity"/> is.</summary>
    public double DrivenAngularVelocity => Locked && double.IsFinite(_toAfterStep) ? _toAfterStep : To.AngularVelocity;
    public double Rpm => AngularVelocity * 60 / (2 * Math.PI);
    /// <summary>W delivered to the driven end.</summary>
    public double Power => Torque * DrivenAngularVelocity;

    /// <summary>
    /// For a <see cref="Locked"/> shaft, after the physics step and before anything else acts on its ends:
    /// the torque the shaft carried over the last tick is the exchange's impulse plus what the lock gave the
    /// driven end within the step, read as the change in its speed since the exchange (any drag of the driven
    /// end's own in the step is counted against it, so it reads that much low: the split crane's drum, 0.3 N·m
    /// of 1430). The speeds are the ones the step left, as a machine's own trace shows its bodies.
    /// </summary>
    public void ReadLocked()
    {
        if (!Locked || !double.IsFinite(_toAfterExchange) || !(_dt > 0)) return;
        double lockImpulse = To.ShaftInertia * (To.AngularVelocity - _toAfterExchange);
        Torque = (_exchanged + lockImpulse) / _dt;
        double k = Torque * Ratio * From.AngularVelocity >= 0 ? 1 / Efficiency : Efficiency;
        DriverTorque = -k * Ratio * Torque;
        _fromAfterStep = From.AngularVelocity;
        _toAfterStep = To.AngularVelocity;
    }

    public void Step(double dt) => StepAll([this], dt, 1);

    /// <summary>
    /// Couples a set of links that may share ends (a gear train) for one tick:
    /// <paramref name="sweeps"/> passes over the links, each bringing one
    /// link's ends onto its ratio with the least exchange of angular momentum
    /// (a Gauss–Seidel sweep, which for a chain of meshes converges in a few
    /// passes), worked on each end's speed as it stood at the tick's start;
    /// then each end is given the sum of its impulses once. A single link
    /// takes one pass and is exact.
    /// </summary>
    public static void StepAll(IReadOnlyList<ShaftLink> links, double dt, int sweeps)
    {
        var omega = new Dictionary<IShaft, double>(ReferenceEqualityComparer.Instance);
        var inertia = new Dictionary<IShaft, double>(ReferenceEqualityComparer.Instance);
        var impulse = new Dictionary<IShaft, double>(ReferenceEqualityComparer.Instance);
        foreach (var l in links)
            foreach (var end in new[] { l.From, l.To })
                if (!omega.ContainsKey(end))
                {
                    omega[end] = end.AngularVelocity;
                    inertia[end] = end.ShaftInertia;
                    impulse[end] = 0;
                }
        var given = new double[links.Count];
        var taken = new double[links.Count];
        if (links.Count > 1 && dt > 0 && SolveTogether(links, omega, inertia, given, taken))
        {
            for (int i = 0; i < links.Count; i++)
            {
                impulse[links[i].To] += given[i];
                impulse[links[i].From] += taken[i];
            }
        }
        else
        for (int s = 0; s < Math.Max(1, sweeps); s++)
            for (int i = 0; i < links.Count; i++)
            {
                var l = links[i];
                double ia = inertia[l.From], ib = inertia[l.To];
                if (!(ia > 0 && ib > 0) || dt <= 0) continue;
                double slip = omega[l.To] - l.Ratio * omega[l.From] - l.Bias;
                // forward (power from the driving end to the driven) when the driven end is pushed the way it should turn
                double plain = -slip / (1 / ib + l.Ratio * l.Ratio / ia);
                double k = plain * l.Ratio * omega[l.From] >= 0 ? 1 / l.Efficiency : l.Efficiency;
                double lambda = -slip / (1 / ib + k * l.Ratio * l.Ratio / ia);
                omega[l.To] += lambda / ib;
                omega[l.From] -= k * l.Ratio * lambda / ia;
                impulse[l.To] += lambda;
                impulse[l.From] -= k * l.Ratio * lambda;
                given[i] += lambda;
                taken[i] -= k * l.Ratio * lambda;
            }
        foreach (var (end, j) in impulse)
            if (j != 0) end.AddAngularImpulse(j);
        for (int i = 0; i < links.Count; i++)
        {
            var l = links[i];
            if (l.Locked)
            {
                // the step still to come adds the lock's share: the torque is read after it (ReadLocked)
                (l._toAfterExchange, l._exchanged, l._dt) = (omega[l.To], given[i], dt);
                continue;
            }
            l.Torque = dt > 0 ? given[i] / dt : 0;
            l.DriverTorque = dt > 0 ? taken[i] / dt : 0;
        }
    }

    /// <summary>
    /// A train's links solved at once (#187): the impulses λ that put every link on its ratio together, from
    /// one linear system, one row per link, ω'_to − Ratio·ω'_from = Bias with each end's ω' = ω + (what its links
    /// give it) / I. Sweeping link by link instead moves only a link's own two ends each time, so a heavy part
    /// at one end of a long train (a 1,667 kg·m² windmill behind a 0.09 kg·m² wheel and three 5:1 meshes) passes
    /// a few percent of its pull along per sweep and the far gears slip: a 125:1 train ran its first pinion at
    /// 0.4% of its ratio. Which way power passes each mesh (1/η or η) is settled by solving again with the
    /// directions the last solution gave, a few times. False if the system is singular (a closed loop of
    /// meshes repeats a constraint), and the sweeps are used.
    /// </summary>
    private static bool SolveTogether(IReadOnlyList<ShaftLink> links, Dictionary<IShaft, double> omega,
                                      Dictionary<IShaft, double> inertia, double[] given, double[] taken)
    {
        int n = links.Count;
        var live = new bool[n];
        for (int i = 0; i < n; i++) live[i] = inertia[links[i].From] > 0 && inertia[links[i].To] > 0;
        var k = new double[n];
        Array.Fill(k, 1.0);
        var lambda = new double[n];
        for (int pass = 0; pass < 6; pass++)
        {
            // what link m gives end e, per unit of its λ: +1 on its driven end, −k·Ratio on its driving end
            double Share(IShaft e, int m) =>
                (ReferenceEquals(e, links[m].To) ? 1 : 0) - (ReferenceEquals(e, links[m].From) ? k[m] * links[m].Ratio : 0);
            var a = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                var l = links[i];
                if (!live[i]) { a[i, i] = 1; continue; }
                for (int m = 0; m < n; m++)
                    if (live[m])
                        a[i, m] = Share(l.To, m) / inertia[l.To] - l.Ratio * Share(l.From, m) / inertia[l.From];
                a[i, n] = -(omega[l.To] - l.Ratio * omega[l.From] - l.Bias);
            }
            if (!Solve(a, n, lambda)) return false;
            bool settled = true;
            for (int i = 0; i < n; i++)
            {
                if (!live[i]) continue;
                double want = lambda[i] * links[i].Ratio * omega[links[i].From] >= 0 ? 1 / links[i].Efficiency : links[i].Efficiency;
                if (want != k[i]) { k[i] = want; settled = false; }
            }
            if (settled) break;
        }
        for (int i = 0; i < n; i++)
        {
            given[i] = live[i] ? lambda[i] : 0;
            taken[i] = live[i] ? -k[i] * links[i].Ratio * lambda[i] : 0;
        }
        return true;
    }

    /// <summary>Gaussian elimination with partial pivoting on the augmented n × (n+1) matrix; false if singular.</summary>
    private static bool Solve(double[,] a, int n, double[] x)
    {
        double scale = 0;
        for (int r = 0; r < n; r++) for (int j = 0; j < n; j++) scale = Math.Max(scale, Math.Abs(a[r, j]));
        for (int c = 0; c < n; c++)
        {
            int p = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
            if (!(Math.Abs(a[p, c]) > 1e-12 * scale)) return false;
            if (p != c) for (int j = c; j <= n; j++) (a[c, j], a[p, j]) = (a[p, j], a[c, j]);
            for (int r = c + 1; r < n; r++)
            {
                double f = a[r, c] / a[c, c];
                if (f == 0) continue;
                for (int j = c; j <= n; j++) a[r, j] -= f * a[c, j];
            }
        }
        for (int r = n - 1; r >= 0; r--)
        {
            double s = a[r, n];
            for (int j = r + 1; j < n; j++) s -= a[r, j] * x[j];
            x[r] = s / a[r, r];
        }
        return true;
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
/// Each tick: StepPipes, then the machines, then StepShafts; in the game,
/// ReadLockedShafts after the physics step (at the next tick's start).
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
            _getters[$"{id}.driven-rpm"] = () => s.DrivenAngularVelocity * 60 / (2 * Math.PI);
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

    /// <summary>
    /// After the physics engine's step, before the machines act on anything: what each shaft the engine
    /// also locks (<see cref="ShaftLink.Locked"/>, #191) carried over the last tick. Nothing for a shaft the
    /// exchange alone couples.
    /// </summary>
    public void ReadLockedShafts()
    {
        foreach (var l in _links) l.Shaft?.ReadLocked();
    }
}
