using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// Hero of Alexandria's aeolipile: a hollow sphere on a horizontal axle,
/// fed steam from a boiler, spun by the reaction of two tangential jets.
/// This is the reference coupling between the thermal and mechanical
/// domains: pressure → jet mass flow → thrust → torque → spin.
/// </summary>
public sealed class Aeolipile(Boiler boiler)
{
    public Boiler Boiler { get; } = boiler;
    public int NozzleCount { get; init; } = 2;
    public double NozzleArea { get; init; } = 3e-6;          // m² (≈2 mm bore)
    public double DischargeCoefficient { get; init; } = 0.7;
    public double ArmRadius { get; init; } = 0.08;           // m, axle to nozzle tip
    public double MomentOfInertia { get; init; } = 1.5e-3;   // kg·m², thin bronze shell + arms
    public double BearingFriction { get; init; } = 2e-4;     // N·m, Coulomb friction in the pivots
    public double AirDrag { get; init; } = 2e-7;             // N·m per (rad/s)², drag on the sphere

    /// <summary>Speed of sound in steam at ~100 °C; the jets choke above this.</summary>
    private const double MaxJetVelocity = 470;

    public double AngularVelocity { get; private set; } // rad/s
    public double Angle { get; private set; }            // rad
    public double Thrust { get; private set; }           // N, total from all nozzles
    public double SteamFlow { get; private set; }        // kg/s, total

    public double Rpm => AngularVelocity * 60 / (2 * Math.PI);

    public void Step(double dt)
    {
        double dp = Boiler.GaugePressure;
        double rho = Boiler.SteamDensity;

        // Incompressible orifice law, capped at choked velocity. Good enough
        // for the low pressures a soldered bronze sphere can hold.
        double v = dp > 0 ? Math.Min(Math.Sqrt(2 * dp / rho), MaxJetVelocity) : 0;
        double mdotPerNozzle = DischargeCoefficient * NozzleArea * rho * v;
        SteamFlow = NozzleCount * mdotPerNozzle;
        Thrust = SteamFlow * v;
        Boiler.Step(dt, SteamFlow);

        double drive = Thrust * ArmRadius;
        double drag = AirDrag * AngularVelocity * AngularVelocity;
        double net = drive - drag;

        // Coulomb friction: holds the rotor still until the drive overcomes it.
        if (AngularVelocity <= 1e-9 && net <= BearingFriction)
        {
            AngularVelocity = 0;
            return;
        }
        net -= BearingFriction;
        AngularVelocity = Math.Max(0, AngularVelocity + net / MomentOfInertia * dt);
        Angle = (Angle + AngularVelocity * dt) % (2 * Math.PI);
    }
}
