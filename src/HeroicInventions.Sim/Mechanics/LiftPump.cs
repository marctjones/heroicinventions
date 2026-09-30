using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A lift (suction) pump: a bucket — a piston with a flap valve in it —
/// working up and down a barrel whose foot stands <see cref="Barrel"/> m up,
/// over a suction pipe dropping into <see cref="From"/>. A crank turning at
/// <see cref="Rpm"/> moves the bucket x = S/2·(1 − cos θ) above the barrel's
/// foot. On each upstroke the water under the bucket follows it up, pushed
/// by the atmosphere on the source's surface; it is lifted out of the spout
/// into <see cref="To"/>, all but the (1 − <see cref="Efficiency"/>) that
/// slips back past the bucket's leather: A·S·η per stroke.
///
/// The atmosphere can only push the column so high: once the pressure under
/// the bucket, P_atm − ρ·g·(z − surface), falls to the water's vapour
/// pressure, the water boils away from the bucket and the column breaks,
/// h_max = (P_atm − P_v(T)) / (ρ·g) above the surface — 10.09 m at 20 °C.
/// However hard the rod is pulled, the bucket then draws only vapour; the
/// pull the column can take tops out at A·(P_atm − P_v). A bucket whose
/// lowest point is more than h_max above the water lifts nothing at all.
///
/// The rod's pull is the pressure difference across the bucket. Once
/// primed, the barrel above it stands full to <see cref="Spout"/>. On the
/// upstroke with water under it that difference is ρ·g·(spout − surface);
/// over a vapour pocket it is P_atm − P_v plus the water above, and the
/// pocket collapses on the downstroke, returning the same work. So the net
/// work a cycle is ρ·g·A·(spout − surface)·(wet travel), and the water
/// gains η of it.
/// </summary>
public sealed class LiftPump(string name, Tank from, Tank to, double barrel, double bore, double stroke)
{
    /// <summary>The air and gravity it stands in: the planet's open air unless it is inside an enclosure.</summary>
    public Zone Zone { get; set; } = new();
    public const double DefaultEfficiency = 0.8;

    public string Name { get; } = name;
    public Tank From { get; } = from;
    public Tank To { get; } = to;
    public double Barrel { get; } = barrel;       // m: the bucket's lowest point
    public double Bore { get; } = bore;           // m
    public double StrokeLength { get; } = stroke; // m
    public double Area => Math.PI * Bore * Bore / 4;
    public double Efficiency { get; init; } = DefaultEfficiency;
    public double Temperature { get; init; } = 20; // °C of the water drawn
    public double Spout => Barrel + StrokeLength;  // m, where the lifted water runs out

    public double Rpm { get; set; }
    public double Force { get; set; } = double.PositiveInfinity; // N the drive can pull the rod with

    public double Angle { get; private set; }      // rad of crank turned, all told
    public double Bucket => StrokeLength / 2 * (1 - Math.Cos(Angle)); // m above the barrel's foot
    public bool Primed { get; private set; }       // water has reached the barrel
    public bool Stalled { get; private set; }      // the drive can't pull the rod on
    public double Pull { get; private set; }       // N on the rod, last step
    public double MaxPull { get; private set; }    // N, the most it has needed
    public double Flow { get; private set; }       // m³/s, last step
    public double Delivered { get; private set; }  // m³ out of the spout
    public double Work { get; private set; }       // J, net, done on the rod
    public double Lifted { get; private set; }     // J, ρ·g·V·(spout − surface), given to the water
    public int Strokes => (int)Math.Floor(Angle / (2 * Math.PI) + 1e-9);

    private double _pocket = double.PositiveInfinity; // m: bucket height the column broke at on the last upstroke

    /// <summary>How high the atmosphere can hold water up a pipe, over the surface: (P_atm − P_v(T)) / (ρ·g).</summary>
    public static double SuctionLimit(double celsius) => SuctionLimit(celsius, new Zone());

    /// <summary>The same under <paramref name="zone"/>'s air and gravity: on Mars, 610 Pa over water whose vapour pressure is about the same holds up nothing.</summary>
    public static double SuctionLimit(double celsius, Zone zone) =>
        (zone.Pressure - Boiler.SaturationPressure(celsius)) / (Physics.WaterDensity * zone.Gravity);

    public double Limit => SuctionLimit(Temperature, Zone);
    public double VapourPressure => Boiler.SaturationPressure(Temperature);

    /// <summary>From the source's surface up to the bucket's lowest point.</summary>
    public double SuctionLift => Barrel - From.SurfaceElevation;

    /// <summary>Where the water stands over the source: up to the top of the stroke, or broken off at the limit.</summary>
    public double Column => Math.Max(0, Math.Min(Limit, Spout - From.SurfaceElevation));
    public bool Broken => Spout - From.SurfaceElevation > Limit;

    /// <summary>How far above the barrel's foot the water can follow the bucket.</summary>
    private double Reach => From.SurfaceElevation + Limit - Barrel;

    private double Above(double x) =>
        Zone.Pressure + (Primed ? Physics.WaterDensity * Zone.Gravity * (Spout - (Barrel + x)) : 0);

    /// <summary>The pull the rod needs with the bucket at x, rising (or falling).</summary>
    private double PullAt(double x, bool rising)
    {
        double below;
        if (rising)
            below = x < Reach
                ? Zone.Pressure - Physics.WaterDensity * Zone.Gravity * (Barrel + x - From.SurfaceElevation)
                : VapourPressure;
        else
            below = x > _pocket ? VapourPressure : Above(x);
        return Area * (Above(x) - below);
    }

    public void Step(double dt)
    {
        Flow = 0;
        double end = Angle + Math.Max(0, Rpm) * 2 * Math.PI / 60 * dt;
        double moved = 0;
        Stalled = false;
        // split at the dead centres, so each piece runs one way and a stroke delivers exactly
        while (Angle < end - 1e-12)
        {
            double centre = (Math.Floor(Angle / Math.PI + 1e-9) + 1) * Math.PI;
            double next = Math.Min(end, centre);
            double x0 = Bucket, x1 = StrokeLength / 2 * (1 - Math.Cos(next));
            bool rising = x1 > x0;
            if (rising && x0 < 1e-12) _pocket = double.PositiveInfinity;
            double pull = PullAt(x1, rising);
            if (rising && pull > Force) { Stalled = true; Pull = PullAt(x0, rising); break; }
            moved += Travel(x0, x1, rising);
            Angle = next;
            Pull = pull;
        }
        MaxPull = Math.Max(MaxPull, Pull);
        Flow = moved / dt;
    }

    /// <summary>Moves the bucket from x0 to x1: the work on the rod and, rising, the water drawn. m³.</summary>
    private double Travel(double x0, double x1, bool rising)
    {
        double rhoG = Physics.WaterDensity * Zone.Gravity;
        if (!rising)
        {
            // over the vapour pocket the atmosphere drives the bucket back down, returning the work
            double top = Math.Max(x0, x1), bottom = Math.Max(Math.Min(x0, x1), Math.Min(_pocket, top));
            if (top > bottom)
                Work -= Area * (Zone.Pressure - VapourPressure) * (top - bottom)
                        + (Primed ? Area * rhoG * ((Spout - Barrel) * (top - bottom) - (top * top - bottom * bottom) / 2) : 0);
            return 0;
        }
        double reach = Reach;
        double wet = Math.Max(0, Math.Min(x1, reach) - x0);
        double drawn = Math.Min(Area * Efficiency * wet, Math.Min(From.WaterVolume, To.Capacity - To.WaterVolume));
        drawn = Math.Max(0, drawn);
        double lift = Spout - From.SurfaceElevation;
        if (wet > 0)
        {
            Primed = true;
            Work += Area * rhoG * lift * wet;
            Lifted += rhoG * drawn * lift;
        }
        double dry = x1 - Math.Max(x0, Math.Min(x1, reach));
        if (dry > 0)
        {
            double a = x1 - dry;
            if (_pocket > a) _pocket = a;
            Work += Area * (Zone.Pressure - VapourPressure) * dry
                    + (Primed ? Area * rhoG * ((Spout - Barrel) * dry - (x1 * x1 - a * a) / 2) : 0);
        }
        From.WaterVolume -= drawn;
        To.WaterVolume += drawn;
        Delivered += drawn;
        return drawn;
    }
}
