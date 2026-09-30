using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// Giovanni Branca's steam wheel (Le Machine, 1629): a jet of steam from a
/// boiler's spout blows on the flat paddles of a wheel. Where the aeolipile
/// is pushed by the reaction of steam leaving it, this wheel is pushed by
/// steam striking it: an impulse turbine, the ancestor of the Pelton wheel.
///
/// The jet leaves the spout at v = √(2Δp/ρ) (capped where it would choke),
/// carrying ṁ = Cd·A·ρ·v. A flat paddle moving at u = ωr stops the jet's
/// speed relative to it, so the push is F = ṁ(v − u): full at a standstill,
/// nothing once the paddles run as fast as the steam. Against a steady
/// load torque L the wheel runs up until F·r balances L plus its bearing,
/// so u = v − (L + friction)/(ṁ·r). The power it gives, F·u, peaks at
/// u = v/2 at ṁv²/4: half the jet's kinetic energy, the rest splashing off
/// flat paddles. (Pelton's cupped buckets turn the jet round and get twice
/// that; the difference is the reason they replaced flat paddles.)
///
/// The same wheel can be driven by hot air instead: a smoke jack, the vane
/// wheel set in a kitchen chimney that turned the roasting spit. Then the
/// driving flow is the chimney's draught: the heat going up the flue warms
/// the air by ΔT = Q/(ṁ·cp), and a column of warm air H tall rises at
/// v = Cd·√(2·g·H·ΔT/T_hot) (the stack effect), carrying ṁ = ρ_hot·A·v. Those
/// two are solved together each step. The air is slow, a metre or two a
/// second, so a smoke jack turns slowly and weakly, but for as long as the
/// fire burns.
/// </summary>
public sealed class JetWheel(Boiler? boiler)
{
    /// <summary>The boiler whose spout drives it, or null for a smoke jack.</summary>
    public Boiler? Boiler { get; } = boiler;

    /// <summary>Smoke jack only: the heat going up the chimney, W (a hearth's share not taken by its pot).</summary>
    public Func<double>? ChimneyHeat { get; init; }
    public double ChimneyHeight { get; init; } = 2.0;         // m of warm air column
    public double ChimneyArea { get; init; } = 0.05;          // m² of flue
    public double AmbientTemperature { get; set; } = 20;      // °C of the air drawn in
    public double SpoutArea { get; init; } = 2e-5;            // m² (≈5 mm bore)
    public double DischargeCoefficient { get; init; } = 0.7;
    public double Radius { get; init; } = 0.15;               // m, axle to where the jet strikes the paddles
    public double MomentOfInertia { get; init; } = 0.011;     // kg·m²
    public double Load { get; set; }                          // N·m the wheel drives against (a spit, a small mill)
    public double BearingFriction { get; init; } = 2e-3;      // N·m, Coulomb friction in the axle
    /// <summary>
    /// N·m per (rad/s)²: the paddles beating the air. Each flat paddle of
    /// area a at radius r feels ½·ρ·Cd·a·(ωr)² at r, so the wheel's
    /// windage is ½·ρ·Cd·n·a·r³·ω² (Cd ≈ 1.2 for a flat plate). On a small
    /// wheel this, not the load, is what limits the speed.
    /// </summary>
    public double AirDrag { get; init; } = 1e-6;

    /// <summary>
    /// A chimney's draught for Q watts going up it: the warming ΔT and the
    /// rising speed v found together, since the faster the air rises the less
    /// each kilogram is warmed. Returns (v m/s, ṁ kg/s, ΔT K).
    /// </summary>
    public static (double Velocity, double MassFlow, double Warming) Draught(double heatW, double height, double area, double ambientC)
    {
        if (heatW <= 0) return (0, 0, 0);
        const double cp = 1005, cd = 0.7;
        double tAmb = Physics.ToKelvin(ambientC), dT = 100, v = 0, mdot = 0;
        for (int i = 0; i < 60; i++)
        {
            double tHot = tAmb + dT;
            v = cd * Math.Sqrt(2 * Physics.Gravity * height * dT / tHot);
            mdot = Physics.AtmosphericPressure / (Physics.AirGasConstant * tHot) * area * v;
            dT = 0.5 * dT + 0.5 * heatW / (mdot * cp);   // damped, so it settles rather than oscillates
        }
        return (v, mdot, dT);
    }

    public static double Windage(double airDensity, int paddles, double paddleArea, double radius) =>
        0.5 * airDensity * 1.2 * paddles * paddleArea * radius * radius * radius;

    /// <summary>Speed of sound in steam at ~100 °C; the jet chokes above this (as the aeolipile's).</summary>
    private const double MaxJetVelocity = 470;

    public double AngularVelocity { get; private set; }  // rad/s
    public double Angle { get; private set; }             // rad
    public double JetVelocity { get; private set; }       // m/s
    public double SteamFlow { get; private set; }         // kg/s
    public double Push { get; private set; }              // N on the paddles
    public double Rpm => AngularVelocity * 60 / (2 * Math.PI);
    public double PaddleSpeed => AngularVelocity * Radius;
    /// <summary>Power delivered to the load, W.</summary>
    public double Power => Load * AngularVelocity;
    public double KineticEnergy => 0.5 * MomentOfInertia * AngularVelocity * AngularVelocity;

    /// <summary>Smoke jack: how much the chimney air is warmed, K.</summary>
    public double DraughtWarming { get; private set; }

    public void Step(double dt)
    {
        double v;
        if (Boiler is { } boiler)
        {
            double dp = boiler.GaugePressure;
            double rho = boiler.SteamDensity;
            v = dp > 0 ? Math.Min(Math.Sqrt(2 * dp / rho), MaxJetVelocity) : 0;
            SteamFlow = DischargeCoefficient * SpoutArea * rho * v;
            boiler.Step(dt, SteamFlow);
        }
        else
        {
            (v, SteamFlow, DraughtWarming) = Draught(ChimneyHeat?.Invoke() ?? 0, ChimneyHeight, ChimneyArea, AmbientTemperature);
        }
        JetVelocity = v;

        Push = SteamFlow * Math.Max(0, v - PaddleSpeed);
        double drive = Push * Radius;
        double resist = Load + AirDrag * AngularVelocity * AngularVelocity;

        // Coulomb friction: the wheel stays put until the jet beats load and bearing.
        if (AngularVelocity <= 1e-9 && drive <= resist + BearingFriction)
        {
            AngularVelocity = 0;
            return;
        }
        double net = drive - resist - BearingFriction;
        AngularVelocity = Math.Max(0, AngularVelocity + net / MomentOfInertia * dt);
        Angle = (Angle + AngularVelocity * dt) % (2 * Math.PI);
    }
}
