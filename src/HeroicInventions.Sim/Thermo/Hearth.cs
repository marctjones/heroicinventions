namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// A fire under a boiler, or under a vessel of sealed air. It burns a store of fuel at a steady heat output
/// (W); the fraction of that heat that reaches the water is its efficiency
/// (an open hearth loses most of it up the chimney). Fuel burns at
/// power ÷ energy density, and the fire goes out when it is gone — so the
/// total heat a load of fuel can ever give is fuel × energy × efficiency.
///
/// Water poured on it (<see cref="Douse"/>) lies on the fuel. The fire's
/// heat goes first into boiling that water off — warming it from
/// <see cref="WaterTemperature"/> to 100 °C and evaporating it,
/// <see cref="QuenchHeat"/> J/kg — and only what is left reaches the
/// boiler. Water arriving faster than the fire can boil it off soaks in,
/// and once the soaked water outweighs the fuel left the fire is drowned:
/// wet wood won't burn, and it stays out.
///
/// A bellows (<see cref="Airflow"/>, m³/s) forces extra air into the fire.
/// The steady burn rate power/density already implies a natural draught —
/// the air it draws on its own, power/density × <see cref="AirFuelRatio"/>
/// — so the bellows' own air (its volume × <see cref="AirDensity"/>)
/// is that much more air than the fire draws unforced. <see cref="Draught"/>
/// scales the burn rate by that ratio: total air ÷ natural air alone. It
/// only ever raises how fast the fuel burns (and so, while any is left,
/// the heat given off) — the energy a load of fuel can ever release stays
/// fuel × density, whatever rate it is burned at.
/// </summary>
public sealed class Hearth(IHeated target, double powerW, double fuelKg, string fuelKind = "wood", double efficiency = 0.5)
{
    /// <summary>Lower heating values, J/kg: air-dried wood, charcoal, bituminous coal.</summary>
    public static double EnergyDensity(string kind) => kind switch
    {
        "wood" => 15e6,
        "charcoal" => 29e6,
        "coal" => 24e6,
        _ => throw new ArgumentException($"unknown fuel {kind} (wood, charcoal, coal)"),
    };

    /// <summary>Stoichiometric air, kg per kg fuel burned: wood (mostly cellulose), charcoal (nearly pure carbon), bituminous coal.</summary>
    public static double AirFuelRatio(string kind) => kind switch
    {
        "wood" => 6.0,
        "charcoal" => 11.6,
        "coal" => 10.5,
        _ => throw new ArgumentException($"unknown fuel {kind} (wood, charcoal, coal)"),
    };

    /// <summary>The air round the fire, °C: water poured on it arrives at this (or at 0 °C, just thawed, if it is freezing).</summary>
    public double AmbientTemperature { get; set; } = 20;
    public double WaterTemperature => Math.Max(0, AmbientTemperature);
    /// <summary>The air the fire draws, kg/m³: a bellows forcing cold dense air in feeds it more by mass.</summary>
    public double AirDensity => Physics.AirDensityAt(AmbientTemperature);

    /// <summary>Heat to take a kilogram of that water to steam, J/kg.</summary>
    public double QuenchHeat =>
        Physics.WaterSpecificHeat * (100 - WaterTemperature) + Physics.LatentHeatVaporization;

    public IHeated Target { get; } = target;
    public string FuelKind { get; } = fuelKind;
    public double Efficiency { get; } = efficiency;
    public double Power { get; set; } = powerW;                    // W of heat released while burning
    public double Airflow { get; set; }                            // m3/s a bellows forces in, on top of natural draught
    public double Fuel { get; set; } = fuelKg;                     // kg left
    public double FuelBurned { get; private set; }                 // kg
    public double EnergyReleased { get; private set; }             // J
    public double Soak { get; private set; }                       // kg of water lying on the fuel
    public double Doused { get; private set; }                     // kg of water poured on, all told
    public double Boiled { get; private set; }                     // kg of it the fire boiled away
    public bool Drowned { get; private set; }
    public bool Lit => Fuel > 0 && Power > 0 && !Drowned;

    /// <summary>Total air the fire draws ÷ the air its own steady burn rate draws unforced: 1 with no bellows, more as it works harder.</summary>
    public double Draught
    {
        get
        {
            double naturalAir = Power / EnergyDensity(FuelKind) * AirFuelRatio(FuelKind); // kg/s air the nominal burn rate already draws
            double bellowsAir = AirDensity * Airflow;                                     // kg/s a bellows adds
            return (naturalAir + bellowsAir) / naturalAir;
        }
    }

    /// <summary>Pour kg of water onto the fire.</summary>
    public void Douse(double kg)
    {
        if (kg <= 0) return;
        Soak += kg;
        Doused += kg;
    }

    /// <summary>Burn for dt seconds, setting what the boiler is heated by this step.</summary>
    public void Step(double dt)
    {
        if (!Lit) { Target.HeatInput = 0; return; }
        double density = EnergyDensity(FuelKind);
        double burn = Math.Min(Fuel, Power * Draught * dt / density);
        double released = burn * density;
        Fuel -= burn;
        FuelBurned += burn;
        EnergyReleased += released;
        double boiled = Math.Min(Soak, released / QuenchHeat);
        Soak -= boiled;
        Boiled += boiled;
        Target.HeatInput = (released - boiled * QuenchHeat) * Efficiency / dt;
        if (Soak > 0 && Soak >= Fuel) Drowned = true;
    }
}
