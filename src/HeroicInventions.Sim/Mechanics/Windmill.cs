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
///
/// The wind has a heading (issue #193): <see cref="WindFromDeg"/>, the azimuth it
/// blows from, in degrees from +x toward +z (the map's convention). The sails
/// face <see cref="FacingDeg"/>; both start at 90, +z, where every mill was built
/// to face. Only the part of the wind along the axle goes through the disc, v·cos θ
/// for θ the angle between them, so a fixed mill's power is ½ρAv³·cos³θ: cos θ in the
/// speed, and the cube of it in the power. Across the wind (θ = 90°) or with its back
/// to it (cos θ ≤ 0) it takes nothing. A mill with a <see cref="Vane"/> turns itself
/// toward the wind at most <see cref="YawRate"/> degrees a second; without one it
/// stays as built.
/// </summary>
public sealed class Windmill(string name, double radius, double momentOfInertia) : IShaft
{
    /// <summary>The most any rotor can take from the wind: 16/27 (Betz).</summary>
    public const double BetzLimit = 16.0 / 27.0;

    public string Name { get; } = name;
    public double Radius { get; } = radius;                   // m, hub to sail tip
    public double MomentOfInertia { get; } = momentOfInertia; // kg·m²
    public double Wind { get; set; }                          // m/s of the wind itself
    public double WindFromDeg { get; set; } = 90;             // azimuth the wind blows from (from +x toward +z); 90 = from +z
    public double FacingDeg { get; set; } = 90;               // azimuth the sails face; 90 = +z
    public bool Vane { get; init; }                           // a tail vane (or fantail) that yaws the sails into the wind
    public double YawRate { get; init; } = 2;                 // deg/s the vane can turn the mill (a tail-pole mill is slow)
    public double Veer { get; init; }                         // deg/hour the wind's heading turns (positive: from +x toward +z)
    /// <summary>The angle from the sails' axis to the wind's, degrees, in (-180, 180].</summary>
    public double MisalignmentDeg
    {
        get
        {
            double d = (WindFromDeg - FacingDeg) % 360;
            return d > 180 ? d - 360 : d <= -180 ? d + 360 : d;
        }
    }
    /// <summary>cos θ, the share of the wind's speed along the axle; none when the mill has its back to it.</summary>
    public double AlignmentFactor => Math.Max(0, Math.Cos(MisalignmentDeg * Math.PI / 180));
    /// <summary>The wind speed through the sails (m/s): v·cos θ.</summary>
    public double ThroughWind => Wind * AlignmentFactor;
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

    double IShaft.ShaftInertia => MomentOfInertia;
    /// <summary>A shaft from another machine turning it (issue #78).</summary>
    public void AddAngularImpulse(double impulse) => AngularVelocity += impulse / MomentOfInertia;

    public double SweptArea => Math.PI * Radius * Radius;
    /// <summary>The wind's kinetic power through the swept disc, ½ρAv³, W.</summary>
    public double WindPower => 0.5 * AirDensity * SweptArea * ThroughWind * ThroughWind * ThroughWind;
    public double TipSpeedRatioNow => ThroughWind > 0 ? AngularVelocity * Radius / ThroughWind : 0;
    /// <summary>The fraction of the wind's power the sails are taking.</summary>
    public double PowerCoefficient => WindPower > 0 ? Torque * AngularVelocity / WindPower : 0;

    /// <summary>The sails' torque at angular velocity ω in the current wind.</summary>
    public double TorqueAt(double omega)
    {
        double wind = ThroughWind;
        if (wind <= 0) return 0;
        double stall = 0.5 * AirDensity * SweptArea * wind * wind * Radius * CpMax / TipSpeedRatio;
        return Math.Max(0, stall * (2 - omega * Radius / wind / TipSpeedRatio));
    }

    public void Step(double dt)
    {
        if (Veer != 0) WindFromDeg += Veer / 3600 * dt;
        if (Vane)
        {
            double step = YawRate * dt, d = MisalignmentDeg;
            FacingDeg += Math.Abs(d) <= step ? d : Math.Sign(d) * step;
        }
        Torque = TorqueAt(AngularVelocity);
        // the millstone holds still until the wind can turn it
        if (AngularVelocity <= 1e-9 && Torque <= Load) { AngularVelocity = 0; return; }
        double before = AngularVelocity;
        AngularVelocity = Math.Max(0, AngularVelocity + (Torque - Load) / MomentOfInertia * dt);
        Work += Load * (before + AngularVelocity) / 2 * dt;
        Angle = (Angle + AngularVelocity * dt) % (2 * Math.PI);
    }
}
