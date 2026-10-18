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
/// the air around it. Of that heat its gas cycle turns a <see cref="CarnotFraction"/>
/// of Carnot's limit into work, the indicated power:
///
///   P = f (1 − T_c / T_h) Q,
///
/// and rejects the rest to the cold air. The hot end warms at C dT_h/dt =
/// εH − radiation − Q, <see cref="HeatCapacity"/> C in J/K.
///
/// A small K leaves the receiver hot but draws little; a large one draws
/// much but at a hot end hardly warmer than the cold, where Carnot allows
/// little. Between them the power peaks, well below the receiver's
/// stagnation temperature (about 100 °C for ten heliostats in the crater,
/// #220). And the colder the cold side, the more of the heat becomes work:
/// Mars at −63 °C is a better sink than a room.
///
/// The shaft is a flywheel of <see cref="MomentOfInertia"/> turned by
/// P / ω (capped at stall by <see cref="StallSpeed"/>) against a
/// <see cref="Load"/> torque and the engine's own <see cref="Drag"/>, the
/// friction and gas losses of its pistons, seals and heat exchangers (#269):
/// a torque k ω², so a power k ω³ that grows fast with speed. It settles
/// where P = (Load + k ω²) ω; unloaded, at its free speed (P / k)^⅓.
///
/// The numbers (#269; docs/plans/stirling-drag-plan.md). Published fractions
/// of Carnot are brake (shaft) measurements, friction included (the NASA
/// GPU-3 at a 677 °C heater gas, 6.9 MPa and 1,500 rpm: a peak brake thermal
/// efficiency of 26.4% on hydrogen, 21.3% on helium, Thieme et al., TRID
/// 171585; research engines about 50% of Carnot, the Pasco manual; 20–50%,
/// a 2011 study). So the template engine gives 0.35 of Carnot at its shaft at
/// its rated 1,500 rpm, the generator's cut-in, and loses a fifth of its
/// indicated work to drag there (a rule of thumb, the owner's choice of the
/// simplest model): the indicated fraction is 0.35 / 0.8 = 0.4375 and k is
/// 0.2 × 267.9 W / (157.08 rad/s)³ = 1.38e-5 N·m·s² (267.9 W indicated under
/// ten 1 m² heliostats at the crater's noon). k is a rule of thumb for that
/// engine, not a measured coefficient; a machine can set its own #:drag. The
/// free speed grows with the cube root of the indicated power, (P / k)^⅓:
/// 2,565 rpm at that noon's 267.9 W, and at Jolt's 3,000 rpm limit (314 rad/s)
/// at about 428 W (1.6 times it; twice the heliostats would pass it). Over that
/// the physics engine's clamp limits it as it does a windmill's open circuit: it
/// brakes the shaft and destroys the excess; no energy is made.
/// Reading 0.35 as the gas cycle's instead is one number: #:carnot-fraction 0.35.
/// </summary>
public sealed class StirlingEngine(string name, double aperture, double conductance, double temperatureC) : IHeated, IShaft
{
    public string Name { get; } = name;
    public double Aperture { get; } = aperture;                     // m²
    public double Conductance { get; set; } = conductance;          // W/K through the heater to the working gas
    public double CarnotFraction { get; set; } = DefaultCarnotFraction;
    /// <summary>The indicated fraction of Carnot a template engine has: 0.35 at the shaft at 1,500 rpm over the 0.8 left by its drag there (#269).</summary>
    public const double DefaultCarnotFraction = 0.4375;
    /// <summary>N·m·s²: the drag torque is <see cref="Drag"/> ω² (a power k ω³). The default loses a fifth of the template engine's 267.9 W at 1,500 rpm (#269).</summary>
    public double Drag { get; set; } = DefaultDrag;
    public const double DefaultDrag = 1.38e-5;
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
    public double IndicatedPower { get; private set; }              // W of work the gas cycle makes now, f (1 − Tc/Th) Q
    public double ShaftPower { get; private set; }                  // W left at the shaft after the drag: what the flywheel and the load get
    public double DragPower => Drag * AngularVelocity * AngularVelocity * AngularVelocity;   // W the drag takes now, k ω³
    public double DragHeat { get; private set; }                    // J the drag has turned to heat
    public double Work { get; private set; }                        // J done on the load
    /// <summary>rad/s it would settle at with no load, (P / k)^⅓ at its present indicated power; infinite with no drag.</summary>
    public double FreeSpeed => Drag > 0 ? Math.Cbrt(Math.Max(0, IndicatedPower) / Drag) : double.PositiveInfinity;
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
            IndicatedPower = work;
            double drag = Drag * AngularVelocity * AngularVelocity;   // N·m: the engine's own losses, k ω²
            ShaftPower = work - drag * AngularVelocity;
            HotTemperature += (Emissivity * HeatInput - Radiation - drawn) * h / HeatCapacity;

            double torque = work / Math.Max(AngularVelocity, StallSpeed);
            double net = torque - drag - Load;
            AngularVelocity = Math.Max(0, AngularVelocity + net * h / MomentOfInertia);
            Angle += AngularVelocity * h;
            Work += Load * AngularVelocity * h;
            DragHeat += drag * AngularVelocity * h;
        }
    }
}
