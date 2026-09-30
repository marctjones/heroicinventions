using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// A hot-air (Stirling) engine (issue #65; Robert Stirling, 1816): a sealed
/// charge of gas shuttled between a hot end and a cold end, so it is driven
/// by heat, not by the outside air — it works on Mars, where Newcomen's
/// atmospheric engine can't.
///
/// Its hot end is a receiver behind an aperture of <see cref="Aperture"/> m²:
/// it absorbs (as a grey body of <see cref="Emissivity"/>) what fires and
/// mirrors land on it, re-radiates through the aperture
/// ε σ A (T_h⁴ − T_air⁴), and gives the engine the heat its heater passes,
/// Q = K (T_h − T_c), <see cref="Conductance"/> K in W/K, the cold end at
/// the air around it. Of that heat it turns a <see cref="CarnotFraction"/>
/// of Carnot's limit into work at the shaft:
///
///   P = f (1 − T_c / T_h) Q,
///
/// and rejects the rest to the cold air. The hot end warms at C dT_h/dt =
/// εH − radiation − Q, <see cref="HeatCapacity"/> C in J/K.
///
/// A small K leaves the receiver hot but draws little; a large one draws
/// much but at a hot end hardly warmer than the cold, where Carnot allows
/// little. Between them the shaft power peaks, well below the receiver's
/// stagnation temperature (docs/lonely-rover.html: 100–150 °C for ten
/// heliostats on Mars). And the colder the cold side, the more of the heat
/// becomes work: Mars at −63 °C is a better sink than a room.
///
/// The shaft is a flywheel of <see cref="MomentOfInertia"/> turned by
/// P / ω (capped at stall by <see cref="StallSpeed"/>) against a
/// <see cref="Load"/> torque: it settles where P = Load × ω.
/// </summary>
public sealed class StirlingEngine(string name, double aperture, double conductance, double temperatureC) : IHeated, IShaft
{
    public string Name { get; } = name;
    public double Aperture { get; } = aperture;                     // m²
    public double Conductance { get; set; } = conductance;          // W/K through the heater to the working gas
    public double CarnotFraction { get; set; } = 0.35;
    public double Emissivity { get; init; } = 0.9;
    public double HeatCapacity { get; init; } = 10_000;             // J/K of the hot end
    public double MomentOfInertia { get; init; } = 0.5;             // kg·m², the flywheel
    public double Load { get; set; }                                // N·m the driven machine resists with
    public const double StallSpeed = 1.0;                           // rad/s: below it the torque is P / StallSpeed
    public Zone Zone { get; set; } = new();
    public double HeatInput { get; set; }                           // W landing on the aperture

    public double HotTemperature { get; private set; } = temperatureC;   // °C
    public double AngularVelocity { get; private set; }             // rad/s
    public double Angle { get; private set; }
    public double HeatDrawn { get; private set; }                   // W the engine takes from the hot end now
    public double ShaftPower { get; private set; }                  // W of work the engine makes now
    public double Work { get; private set; }                        // J done on the load
    public double ShaftInertia => MomentOfInertia;
    public double Rpm => AngularVelocity * 60 / (2 * Math.PI);
    public double Power => Load * AngularVelocity;                  // W into the load
    public double ColdTemperature => Zone.Temperature;              // °C

    /// <summary>W the aperture radiates away now.</summary>
    public double Radiation => Emissivity * Crucible.StefanBoltzmann * Aperture *
        (Math.Pow(Physics.ToKelvin(HotTemperature), 4) - Math.Pow(Physics.ToKelvin(Zone.Temperature), 4));

    /// <summary>The share of the heat drawn that becomes work at this hot end: f (1 − T_c/T_h), 0 if it isn't hotter.</summary>
    public double Efficiency
    {
        get
        {
            double th = Physics.ToKelvin(HotTemperature), tc = Physics.ToKelvin(ColdTemperature);
            return th > tc ? CarnotFraction * (1 - tc / th) : 0;
        }
    }

    public void AddAngularImpulse(double impulse) => AngularVelocity = Math.Max(0, AngularVelocity + impulse / MomentOfInertia);

    public void Step(double dt)
    {
        // the T⁴ loss is stiff: small steps
        int n = Math.Max(1, (int)Math.Ceiling(dt / 0.05));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            double drawn = Math.Max(0, Conductance * (HotTemperature - ColdTemperature));
            double work = Efficiency * drawn;
            HeatDrawn = drawn;
            ShaftPower = work;
            HotTemperature += (Emissivity * HeatInput - Radiation - drawn) * h / HeatCapacity;

            double torque = work / Math.Max(AngularVelocity, StallSpeed);
            double net = torque - Load;
            AngularVelocity = Math.Max(0, AngularVelocity + net * h / MomentOfInertia);
            Angle += AngularVelocity * h;
            Work += Load * AngularVelocity * h;
        }
    }
}
