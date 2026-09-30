namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// Water arriving from outside the scene — a spring, a river coming in from
/// upstream — at a steady rate into a tank. The world beyond the machine
/// isn't modelled; this is what it provides.
/// </summary>
public sealed class WaterSource(string name, Tank into, double flow)
{
    public string Name { get; } = name;
    public Tank Into { get; } = into;
    public double Rate { get; } = flow;            // m³/s offered
    public double Flow { get; private set; }       // m³/s that found room, last step
    /// <summary>A float valve in the tank it fills, throttling it, if it has one.</summary>
    public FloatValve? Valve { get; set; }

    public void Step(double dt)
    {
        double added = Math.Min(Rate * (Valve?.Opening ?? 1) * dt, Into.Capacity - Into.WaterVolume);
        Into.WaterVolume += Math.Max(0, added);
        Flow = Math.Max(0, added) / dt;
    }
}

/// <summary>
/// An open channel — a millrace, an aqueduct's conduit, a tailrace — leading
/// water from a tank over the lip of its port, down a gentle slope, into
/// another tank or away out of the scene.
///
/// How much flows: water spilling over a lip is a broad-crested weir,
/// Q = 1.705·b·h^1.5 (SI units), b the channel's width and h how far the
/// water stands above the lip — less, if the tank it runs into is backed up
/// above the lip too. How deep and fast it runs down the channel: at its
/// "normal depth", where the slope's pull balances friction on the walls
/// and bed, given by Manning's equation Q = (1/n)·A·R^(2/3)·S^(1/2), with A
/// the water's cross-section, R that area over the wetted perimeter, S the
/// slope and n the roughness (0.015 for dressed stone). That speed is what
/// pushes on a wheel's paddles standing in the flow.
/// </summary>
public sealed class Channel(string name, Tank from, double lipElevation, Tank? to, double endElevation,
                            double width, double length)
{
    public const double Roughness = 0.015;         // Manning's n, dressed stone
    private const double WeirCoefficient = 1.705;  // broad-crested weir, SI, under Earth's 9.81 m/s²: (2/3)^1.5·√g

    public string Name { get; } = name;
    public Tank From { get; } = from;
    public Tank? To { get; } = to;                 // null: runs out of the scene
    public double LipElevation { get; } = lipElevation;
    public double EndElevation { get; } = endElevation;
    public double Width { get; } = width;
    public double Length { get; } = length;
    public double Slope => Math.Max(1e-4, (LipElevation - EndElevation) / Length);
    /// <summary>m/s²: water falls over a lip and runs down a slope as √g.</summary>
    public double Gravity { get; set; } = Physics.Gravity;

    /// <summary>A sluice gate across the head of the channel, if it has one.</summary>
    public SluiceGate? Gate { get; set; }
    /// <summary>A float valve in the tank it runs into, throttling it, if it has one.</summary>
    public FloatValve? Valve { get; set; }
    /// <summary>Where water run off the scene lands, if on something that cares (m³ each step).</summary>
    public Action<double>? Pour { get; set; }

    public double Flow { get; private set; }       // m³/s
    public double Depth { get; private set; }      // m, running down the channel
    public double Velocity { get; private set; }   // m/s

    /// <summary>Water over the lip, less any the far end is backed up to.</summary>
    public double Head => Math.Max(0, From.SurfaceElevation - Math.Max(LipElevation, To?.SurfaceElevation ?? double.NegativeInfinity));

    public static double WeirFlow(double width, double head, double gravity = Physics.Gravity) =>
        WeirCoefficient * Math.Sqrt(gravity / Physics.Gravity) * width * Math.Pow(Math.Max(0, head), 1.5);

    /// <summary>The depth at which a flow Q runs steadily down a channel of this width and slope (Manning), by bisection.</summary>
    public static double NormalDepth(double flow, double width, double slope, double gravity = Physics.Gravity)
    {
        // Manning's 1/n is fitted under Earth's gravity; the speed a slope gives goes as √g (Chézy's C = √(8g/f))
        double g = Math.Sqrt(gravity / Physics.Gravity);
        if (flow <= 0) return 0;
        double Carries(double d)
        {
            double area = width * d, radius = area / (width + 2 * d);
            return g * area * Math.Pow(radius, 2.0 / 3) * Math.Sqrt(slope) / Roughness;
        }
        double lo = 0, hi = 1;
        while (Carries(hi) < flow) hi *= 2;
        for (int i = 0; i < 60; i++)
        {
            double mid = (lo + hi) / 2;
            if (Carries(mid) < flow) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    // ------------------------------------------------------------------
    // A channel that holds water (issue #36): the reach cut into Cells cells
    // along its length, each with its own depth and discharge, solved by the
    // 1-D shallow-water equations (ShallowWater). Water poured in at the
    // head takes time to run down it, a wave travels and flattens, and the
    // reach can run dry below a shut gate. The steady channel above stays
    // what a channel with no cells does, so old scenes keep their numbers.
    // ------------------------------------------------------------------

    /// <summary>How many cells the reach is cut into; 0, the steady channel (weir at the head, Manning depth all along).</summary>
    public int Cells
    {
        get => _cells;
        init { _cells = value; _h = new double[value]; _q = new double[value]; }   // sized at once, so a live edit finds arrays to carry
    }
    private readonly int _cells;
    public bool Dynamic => Cells > 0;
    /// <summary>Manning's n for a dynamic reach (dressed stone unless changed; 0, frictionless).</summary>
    public double Manning { get; set; } = Roughness;

    // not readonly: a live edit carries them over (StateCopy copies plain arrays of the same length)
    private double[] _h = [], _q = [];
    private double _clock;
    private bool _wasDryAtFoot = true;

    /// <summary>Metres of water standing in each cell, head to foot.</summary>
    public IReadOnlyList<double> Depths { get { EnsureCells(); return _h; } }
    /// <summary>Each cell's speed down the reach, m/s.</summary>
    public double VelocityAt(int cell) { EnsureCells(); return ShallowWater.Velocity(_h[cell], _q[cell]); }
    public double CellLength => Length / Math.Max(1, Cells);
    /// <summary>The bed's elevation at a cell: falling evenly from the lip (first cell) to the end (last).</summary>
    public double BedAt(int cell) => Cells <= 1 ? LipElevation : LipElevation + (EndElevation - LipElevation) * cell / (Cells - 1);
    public int CellAt(double metres) => Math.Clamp((int)(metres / CellLength), 0, Math.Max(0, Cells - 1));
    /// <summary>m³ of water in the reach.</summary>
    public double Stored => _h.Sum() * Width * CellLength;
    /// <summary>m³/s leaving the foot (into the far tank, or off the scene), last step; negative, running back up.</summary>
    public double Outflow { get; private set; }
    /// <summary>m from the head to the far edge of the last wet cell (over 1 mm), 0 dry.</summary>
    public double Front { get; private set; }
    /// <summary>s after the reach was built when water first reached its foot; −1 until it has.</summary>
    public double Arrival { get; private set; } = -1;
    /// <summary>m³ taken out of the arithmetic by clipping a cell's depth at zero, all told (should stay ~0: the mass ledger's error).</summary>
    public double Clipped { get; private set; }

    private void EnsureCells()
    {
        if (_h.Length == Cells) return;
        _h = new double[Cells];
        _q = new double[Cells];
    }

    /// <summary>Sets the water standing in the reach: a depth (and optionally a speed) at each distance from the head, m.</summary>
    public void Fill(Func<double, double> depthAt, Func<double, double>? velocityAt = null)
    {
        EnsureCells();
        for (int i = 0; i < Cells; i++)
        {
            double x = (i + 0.5) * CellLength;
            _h[i] = Math.Max(0, depthAt(x));
            _q[i] = _h[i] * (velocityAt?.Invoke(x) ?? 0);
        }
        _wasDryAtFoot = _h[^1] <= 1e-3;
    }

    /// <summary>The unit discharge and momentum flux where the reach meets its head tank (positive: into the reach).</summary>
    private (double Q, double P) HeadFlux(double g)
    {
        double h0 = _h[0], u0 = ShallowWater.Velocity(h0, _q[0]);
        double head = From.SurfaceElevation - LipElevation;
        double hb, ub;
        (hb, ub) = head > 0 ? ShallowWater.Inlet(head, h0, u0, g) : (0, -1);
        if (ub < 0)
        {
            // running back: the reach spills over the lip into its tank (or is held by the tank's level)
            var (ho, vo) = ShallowWater.Outlet(h0, -u0, From.SurfaceElevation - BedAt(0), g);
            (hb, ub) = (ho, -vo);
            if (Gate is { } shut && shut.Opening * shut.Height <= 0 && ub < 0) (hb, ub) = (h0, 0);   // a shut gate is a wall both ways
        }
        double q = hb * ub;
        if (q > 0 && Gate is { } gate)
        {
            double through = gate.Discharge(From.SurfaceElevation, LipElevation, BedAt(0) + h0, q * Width) / Width;
            if (through < q) { ub = hb > 0 ? through / hb : 0; q = through; }
        }
        return (q, hb * ub * ub + g * hb * hb / 2);
    }

    /// <summary>The unit discharge and momentum flux where the reach meets its far tank, or falls off the scene (positive: out of the reach).</summary>
    private (double Q, double P) FootFlux(double g)
    {
        int n = Cells - 1;
        double h = _h[n], u = ShallowWater.Velocity(h, _q[n]);
        double downstream = To is null ? double.NegativeInfinity : To.SurfaceElevation - BedAt(n);
        var (hb, vb) = ShallowWater.Outlet(h, u, downstream, g);
        double q = hb * vb;
        if (q > 0 && Valve is { } valve) { q *= valve.Opening; vb = hb > 0 ? q / hb : 0; }
        return (q, hb * vb * vb + g * hb * hb / 2);
    }

    private void StepDynamic(double dt)
    {
        EnsureCells();
        double g = Gravity, dx = CellLength;
        int n = Cells;
        var mass = new double[n + 1];
        var momL = new double[n + 1];   // momentum flux through face j, as the cell on its left sees it
        var momR = new double[n + 1];   // ... and as the cell on its right sees it
        double inflow = 0, outflow = 0;
        for (double t = 0; t < dt - 1e-12;)
        {
            double fastest = Math.Sqrt(g * Math.Max(From.SurfaceElevation - LipElevation, 0));
            for (int i = 0; i < n; i++)
                if (_h[i] > ShallowWater.Dry) fastest = Math.Max(fastest, Math.Abs(_q[i] / _h[i]) + Math.Sqrt(g * _h[i]));
            if (To is not null) fastest = Math.Max(fastest, Math.Sqrt(g * Math.Max(0, To.SurfaceElevation - BedAt(n - 1))));
            double sub = Math.Min(dt - t, fastest > 0 ? 0.45 * dx / fastest : dt - t);

            var (qh, ph) = HeadFlux(g);
            var (qf, pf) = FootFlux(g);
            // the tanks can only give what stands above the lip / end, and take what fits
            if (qh > 0) qh = Math.Min(qh, Math.Max(0, (From.SurfaceElevation - LipElevation) * From.Area) / (Width * sub));
            else if (qh < 0) qh = -Math.Min(-qh, Math.Max(0, From.Capacity - From.WaterVolume) / (Width * sub));
            if (To is not null)
            {
                if (qf > 0) qf = Math.Min(qf, Math.Max(0, To.Capacity - To.WaterVolume) / (Width * sub));
                else if (qf < 0) qf = -Math.Min(-qf, Math.Max(0, (To.SurfaceElevation - EndElevation) * To.Area) / (Width * sub));
            }
            (mass[0], momR[0]) = (qh, ph);
            (mass[n], momL[n]) = (qf, pf);
            for (int j = 1; j < n; j++)
                (mass[j], momL[j], momR[j]) = ShallowWater.Face(BedAt(j - 1), _h[j - 1], ShallowWater.Velocity(_h[j - 1], _q[j - 1]),
                                                                  BedAt(j), _h[j], ShallowWater.Velocity(_h[j], _q[j]), g);
            for (int i = 0; i < n; i++)
            {
                double h = _h[i] - sub / dx * (mass[i + 1] - mass[i]);
                double q = _q[i] - sub / dx * (momL[i + 1] - momR[i]);
                if (h < ShallowWater.Dry) { Clipped -= Math.Min(h, 0) * Width * dx; h = Math.Max(h, 0); q = 0; }
                _h[i] = h;
                _q[i] = ShallowWater.Friction(h, q, sub, Manning, Width, g);
            }
            From.WaterVolume -= qh * Width * sub;
            inflow += qh * Width * sub;
            if (To is not null) To.WaterVolume += qf * Width * sub;
            else if (qf > 0) Pour?.Invoke(qf * Width * sub);
            outflow += qf * Width * sub;
            t += sub;
            _clock += sub;
        }
        Flow = inflow / dt;
        Outflow = outflow / dt;
        int mid = n / 2;
        Depth = _h[mid];
        Velocity = ShallowWater.Velocity(_h[mid], _q[mid]);
        int last = Array.FindLastIndex(_h, h => h > 1e-3);
        Front = last < 0 ? 0 : (last + 1) * dx;
        bool dryAtFoot = _h[n - 1] <= 1e-3;
        if (Arrival < 0 && _wasDryAtFoot && !dryAtFoot) Arrival = _clock;
        _wasDryAtFoot = dryAtFoot;
    }

    public void Step(double dt)
    {
        if (Dynamic) { StepDynamic(dt); return; }
        double downstream = To?.SurfaceElevation ?? double.NegativeInfinity;
        double q = Gate is { } gate
            ? gate.Discharge(From.SurfaceElevation, LipElevation, downstream, WeirFlow(Width, Head, Gravity))
            : WeirFlow(Width, Head, Gravity);
        q *= Valve?.Opening ?? 1;
        // can't take more than stands above the lip, nor put more than fits
        double available = Math.Max(0, (From.SurfaceElevation - LipElevation) * From.Area);
        double room = To is null ? double.PositiveInfinity : To.Capacity - To.WaterVolume;
        double moved = Math.Max(0, Math.Min(q * dt, Math.Min(available, room)));
        From.WaterVolume -= moved;
        if (To is not null) To.WaterVolume += moved;
        else Pour?.Invoke(moved);
        Flow = moved / dt;
        Depth = NormalDepth(Flow, Width, Slope, Gravity);
        Velocity = Depth > 0 ? Flow / (Width * Depth) : 0;
    }
}

/// <summary>
/// A sluice gate: a wooden plate across the head of a channel, sliding up
/// and down in grooves. Raised by Opening × Height it leaves a slot that
/// high across the gate's Width, and the water runs out under it as an
/// orifice, Q = Cd·a·√(2·g·h) with a the slot's area, Cd ≈ 0.6 (the jet
/// contracts as it leaves the sharp lower edge) and h how far the water
/// stands above the middle of the slot — or above the water on the far
/// side, if that is higher (a drowned gate). While the water stands below
/// the plate's lower edge the gate doesn't touch it and the channel's lip
/// is a plain weir again; once it touches, the lesser of the two flows
/// (at the edge the orifice would pass slightly more than the weir), so
/// the two join without a jump. The plate is Height tall: water rising above its top
/// pours over it as a broad-crested weir, so a shut gate is a dam whose
/// crest stands Height above the lip. Shut, nothing passes underneath and
/// the channel below it runs dry.
/// </summary>
public sealed class SluiceGate(double width, double height, double opening)
{
    public const double DischargeCoefficient = 0.6;

    public double Width { get; } = width;             // m, across the channel
    public double Height { get; } = height;           // m, the plate itself
    private double _opening = Math.Clamp(opening, 0, 1);
    /// <summary>How far the plate is raised, as a fraction of its height: 0 shut, 1 fully drawn.</summary>
    public double Opening { get => _opening; set => _opening = Math.Clamp(value, 0, 1); }

    public double Flow { get; private set; }          // m³/s through the slot, last step
    public double OverFlow { get; private set; }      // m³/s over the top, last step
    public double OrificeHead { get; private set; }   // m of water above the slot's middle (or the tailwater)

    /// <summary>Q = Cd·w·a·√(2gh): an orifice a high and w wide under a head h.</summary>
    public static double OrificeFlow(double width, double slot, double head, double gravity = Physics.Gravity) =>
        DischargeCoefficient * width * slot * Math.Sqrt(2 * gravity * Math.Max(0, head));

    /// <summary>m/s² the water falls through the slot under.</summary>
    public double Gravity { get; set; } = Physics.Gravity;

    /// <summary>
    /// What leaves over the lip through this gate, given the water's surface
    /// upstream, the lip (the gate's sill), the water level downstream and
    /// what the lip alone would pass as a weir.
    /// </summary>
    public double Discharge(double upstream, double lip, double downstream, double weirFlow)
    {
        double slot = Opening * Height;
        OrificeHead = Math.Max(0, upstream - Math.Max(lip + slot / 2, downstream));
        Flow = slot <= 0 ? 0
             : upstream <= lip + slot ? weirFlow                    // the plate hangs clear of the water
             : Math.Min(weirFlow, OrificeFlow(Width, slot, OrificeHead, Gravity));
        double crest = lip + slot + Height;
        OverFlow = Channel.WeirFlow(Width, upstream - Math.Max(crest, downstream), Gravity);
        return Flow + OverFlow;
    }
}
