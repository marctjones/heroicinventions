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

    public void Step(double dt)
    {
        double added = Math.Min(Rate * dt, Into.Capacity - Into.WaterVolume);
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
    private const double WeirCoefficient = 1.705;  // broad-crested weir, SI

    public string Name { get; } = name;
    public Tank From { get; } = from;
    public Tank? To { get; } = to;                 // null: runs out of the scene
    public double LipElevation { get; } = lipElevation;
    public double EndElevation { get; } = endElevation;
    public double Width { get; } = width;
    public double Length { get; } = length;
    public double Slope => Math.Max(1e-4, (LipElevation - EndElevation) / Length);

    /// <summary>A sluice gate across the head of the channel, if it has one.</summary>
    public SluiceGate? Gate { get; set; }

    public double Flow { get; private set; }       // m³/s
    public double Depth { get; private set; }      // m, running down the channel
    public double Velocity { get; private set; }   // m/s

    /// <summary>Water over the lip, less any the far end is backed up to.</summary>
    public double Head => Math.Max(0, From.SurfaceElevation - Math.Max(LipElevation, To?.SurfaceElevation ?? double.NegativeInfinity));

    public static double WeirFlow(double width, double head) => WeirCoefficient * width * Math.Pow(Math.Max(0, head), 1.5);

    /// <summary>The depth at which a flow Q runs steadily down a channel of this width and slope (Manning), by bisection.</summary>
    public static double NormalDepth(double flow, double width, double slope)
    {
        if (flow <= 0) return 0;
        double Carries(double d)
        {
            double area = width * d, radius = area / (width + 2 * d);
            return area * Math.Pow(radius, 2.0 / 3) * Math.Sqrt(slope) / Roughness;
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

    public void Step(double dt)
    {
        double downstream = To?.SurfaceElevation ?? double.NegativeInfinity;
        double q = Gate is { } gate
            ? gate.Discharge(From.SurfaceElevation, LipElevation, downstream, WeirFlow(Width, Head))
            : WeirFlow(Width, Head);
        // can't take more than stands above the lip, nor put more than fits
        double available = Math.Max(0, (From.SurfaceElevation - LipElevation) * From.Area);
        double room = To is null ? double.PositiveInfinity : To.Capacity - To.WaterVolume;
        double moved = Math.Max(0, Math.Min(q * dt, Math.Min(available, room)));
        From.WaterVolume -= moved;
        if (To is not null) To.WaterVolume += moved;
        Flow = moved / dt;
        Depth = NormalDepth(Flow, Width, Slope);
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
    private const double G = 9.81;

    public double Width { get; } = width;             // m, across the channel
    public double Height { get; } = height;           // m, the plate itself
    private double _opening = Math.Clamp(opening, 0, 1);
    /// <summary>How far the plate is raised, as a fraction of its height: 0 shut, 1 fully drawn.</summary>
    public double Opening { get => _opening; set => _opening = Math.Clamp(value, 0, 1); }

    public double Flow { get; private set; }          // m³/s through the slot, last step
    public double OverFlow { get; private set; }      // m³/s over the top, last step
    public double OrificeHead { get; private set; }   // m of water above the slot's middle (or the tailwater)

    /// <summary>Q = Cd·w·a·√(2gh): an orifice a high and w wide under a head h.</summary>
    public static double OrificeFlow(double width, double slot, double head) =>
        DischargeCoefficient * width * slot * Math.Sqrt(2 * G * Math.Max(0, head));

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
             : Math.Min(weirFlow, OrificeFlow(Width, slot, OrificeHead));
        double crest = lip + slot + Height;
        OverFlow = Channel.WeirFlow(Width, upstream - Math.Max(crest, downstream));
        return Flow + OverFlow;
    }
}
