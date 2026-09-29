namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A windmill: sails of <see cref="Radius"/> turning a millstone that grinds
/// against a steady <see cref="Load"/> torque. The wind blowing through the
/// swept disc A = πR² at speed v carries ½ρAv³ of kinetic power; the sails
/// take the fraction Cp of it. How much depends on how fast they turn for the
/// wind — the tip-speed ratio λ = ωR/v. Standing still they catch the wind
/// but do no work; spinning too fast they let it through untouched. In
/// between Cp peaks, at <see cref="CpMax"/> for λ = <see cref="TipSpeedRatio"/>:
///
///   Cp(λ) = CpMax · (λ/λ*) · (2 − λ/λ*)
///
/// No rotor can take more than 16/27 of the wind's power (Betz, 1919): to
/// take it all the air would have to stop dead behind the sails, and air
/// that stops can't make way for the air behind it. <see cref="CpMax"/> is
/// held below that. The sails' torque, P/ω, comes out linear in λ,
///
///   τ = ½ρAv²R · (CpMax/λ*) · (2 − λ/λ*),
///
/// most at a standstill, so against a millstone of load τ_L the sails
/// settle at λ = λ*(2 − τ_L/τ₀), τ₀ = ½ρAv²R·CpMax/λ*. Set the stones to
/// τ₀ and they run at λ*, taking CpMax of the wind: power that grows as
/// the cube of the wind speed.
/// </summary>
public sealed class Windmill(string name, double radius, double momentOfInertia)
{
    /// <summary>The most any rotor can take from the wind: 16/27 (Betz).</summary>
    public const double BetzLimit = 16.0 / 27.0;

    public string Name { get; } = name;
    public double Radius { get; } = radius;                   // m, hub to sail tip
    public double MomentOfInertia { get; } = momentOfInertia; // kg·m²
    public double Wind { get; set; }                          // m/s through the sails
    public double Load { get; set; }                          // N·m the millstone resists with while turning
    public double AirDensity { get; set; } = Physics.AirDensity; // kg/m³: cold air is denser and carries more power

    private readonly double _cpMax = 0.3;
    public double CpMax
    {
        get => _cpMax;
        init => _cpMax = value > 0 && value <= BetzLimit ? value
            : throw new ArgumentOutOfRangeException(nameof(CpMax), value, "must be above 0 and at most the Betz limit, 16/27");
    }
    public double TipSpeedRatio { get; init; } = 2.5;         // λ* where Cp peaks; traditional four-sail mills run at 2–3

    public double AngularVelocity { get; private set; }      // rad/s
    public double Angle { get; private set; }                 // rad
    public double Torque { get; private set; }                // N·m from the wind, last step
    public double Work { get; private set; }                  // J done on the load
    public double Rpm => AngularVelocity * 60 / (2 * Math.PI);
    public double Power => Load * AngularVelocity;             // W into the millstone
    public double KineticEnergy => 0.5 * MomentOfInertia * AngularVelocity * AngularVelocity;

    public double SweptArea => Math.PI * Radius * Radius;
    /// <summary>The wind's kinetic power through the swept disc, ½ρAv³, W.</summary>
    public double WindPower => 0.5 * AirDensity * SweptArea * Wind * Wind * Wind;
    public double TipSpeedRatioNow => Wind > 0 ? AngularVelocity * Radius / Wind : 0;
    /// <summary>The fraction of the wind's power the sails are taking.</summary>
    public double PowerCoefficient => WindPower > 0 ? Torque * AngularVelocity / WindPower : 0;

    /// <summary>The sails' torque at angular velocity ω in the current wind.</summary>
    public double TorqueAt(double omega)
    {
        if (Wind <= 0) return 0;
        double stall = 0.5 * AirDensity * SweptArea * Wind * Wind * Radius * CpMax / TipSpeedRatio;
        return Math.Max(0, stall * (2 - omega * Radius / Wind / TipSpeedRatio));
    }

    public void Step(double dt)
    {
        Torque = TorqueAt(AngularVelocity);
        // the millstone holds still until the wind can turn it
        if (AngularVelocity <= 1e-9 && Torque <= Load) { AngularVelocity = 0; return; }
        double before = AngularVelocity;
        AngularVelocity = Math.Max(0, AngularVelocity + (Torque - Load) / MomentOfInertia * dt);
        Work += Load * (before + AngularVelocity) / 2 * dt;
        Angle = (Angle + AngularVelocity * dt) % (2 * Math.PI);
    }
}
