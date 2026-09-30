namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// An endless open belt between two drums on parallel axles (issue #51). While
/// it grips, the drums' rims move at the same speed, so the driven drum turns at
/// r1/r2 of the driver. It grips only so far: the tight and slack sides differ by
/// at most the capstan ratio, T1/T2 = e^(μθ), θ the wrap on the smaller drum, and
/// with the belt's pretension holding their sum at 2·T0 (a stretched belt gives up
/// on the slack side what it gains on the tight one), the most force it can
/// carry is F = (T1 − T2) = 2·T0·tanh(μθ/2). Past that it slips.
/// </summary>
public sealed class Belt(string id, double radiusA, double radiusB, double centres, double mu, double tension)
{
    public string Id { get; } = id;
    public double RadiusA { get; } = radiusA;
    public double RadiusB { get; } = radiusB;
    /// <summary>Distance between the axles, m.</summary>
    public double Centres { get; } = centres;
    /// <summary>Friction coefficient between belt and drum.</summary>
    public double Mu { get; } = mu;
    /// <summary>Pretension T0, N: the tension either side carries with the belt at rest. Tighten it and the belt carries more.</summary>
    public double Tension { get; set; } = tension;

    /// <summary>The angle the belt wraps the smaller drum, rad: π − 2·asin(|r2 − r1| / C) for an open belt.</summary>
    public double Wrap => Math.PI - 2 * Math.Asin(Math.Abs(RadiusA - RadiusB) / Centres);

    /// <summary>The most force, N, the belt can carry between the drums: 2·T0·tanh(μθ/2).</summary>
    public double MaxForce => 2 * Tension * Math.Tanh(Mu * Wrap / 2);

    /// <summary>Force carried between the drums last tick, N, from the driver to the driven; set by whoever owns the drums.</summary>
    public double Force { get; set; }
    /// <summary>How fast the belt slips over the drums, m/s: driver rim speed minus driven rim speed. Zero while it grips.</summary>
    public double Slip { get; set; }

    /// <summary>Length of the belt, m: two straight runs and the arcs round the drums.</summary>
    public double Length
    {
        get
        {
            double phi = Math.Asin((RadiusB - RadiusA) / Centres);
            return 2 * Centres * Math.Cos(phi) + RadiusA * (Math.PI - 2 * phi) + RadiusB * (Math.PI + 2 * phi);
        }
    }

    /// <summary>
    /// The angular impulses (about the two axles, N·m·s) that bring the drums' rims to the same speed:
    /// as many as it takes, up to the most the belt can carry in this step. <paramref name="rimSlip"/> is
    /// driver rim speed minus driven rim speed; <paramref name="inertiaA"/> and <paramref name="inertiaB"/> are what
    /// each drum (and everything fixed to its axle) has to turn. Returns the force this step put through the belt
    /// and the slip left over.
    /// </summary>
    public (double ImpulseA, double ImpulseB, double Force, double Slip) Grip(double rimSlip, double inertiaA, double inertiaB, double dt)
    {
        double softness = RadiusA * RadiusA / inertiaA + RadiusB * RadiusB / inertiaB;   // 1/kg: rim speed change per unit of belt impulse
        double wanted = rimSlip / softness;                                             // the impulse that removes all the slip
        double limit = MaxForce * dt;
        double j = Math.Clamp(wanted, -limit, limit);
        return (-j * RadiusA, j * RadiusB, j / dt, rimSlip - j * softness);
    }
}
