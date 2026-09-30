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
/// </summary>
public sealed class JetWheel(Boiler boiler)
{
    public Boiler Boiler { get; } = boiler;
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

    public void Step(double dt)
    {
        double dp = Boiler.GaugePressure;
        double rho = Boiler.SteamDensity;
        double v = dp > 0 ? Math.Min(Math.Sqrt(2 * dp / rho), MaxJetVelocity) : 0;
        JetVelocity = v;
        SteamFlow = DischargeCoefficient * SpoutArea * rho * v;
        Boiler.Step(dt, SteamFlow);

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
