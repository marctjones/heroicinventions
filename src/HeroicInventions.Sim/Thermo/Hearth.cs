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
///
/// A fire breathes its zone's air (issue #40). It needs oxygen at its fuel's
/// stoichiometric rate (<see cref="OxygenPerKg"/>: 2.67 kg a kg of charcoal,
/// C + O₂ → CO₂; 1.19 of wood, taken as cellulose, C₆H₁₀O₅ + 6 O₂ → 6 CO₂ +
/// 5 H₂O; 2.45 of bituminous coal by its composition) and burns only while
/// the oxygen is above the limiting fraction, <see cref="OxygenLimit"/> (about
/// 15% by volume): outdoors on Mars, 0.17%, it never lights. In an
/// enclosure it takes that oxygen out of the room's air and puts back the
/// carbon dioxide and water vapour it makes, and the heat its pot doesn't
/// take (1 − efficiency) warms the room; so a sealed room's fire burns until
/// its oxygen has fallen to the limit, and goes out.
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

    /// <summary>Oxygen a kilogram of fuel burns, kg: charcoal as carbon (32/12), wood as cellulose (6 × 32/162), coal as 80% C, 5% H, 8% O.</summary>
    public static double OxygenPerKg(string kind) => kind switch
    {
        "wood" => 6 * 31.998 / 162.14,
        "charcoal" => 31.998 / 12.011,
        "coal" => 0.80 * 31.998 / 12.011 + 0.05 * 7.9367 - 0.08,
        _ => throw new ArgumentException($"unknown fuel {kind} (wood, charcoal, coal)"),
    };

    /// <summary>Carbon dioxide a kilogram of fuel makes, kg.</summary>
    public static double CarbonDioxidePerKg(string kind) => kind switch
    {
        "wood" => 6 * 44.01 / 162.14,
        "charcoal" => 44.01 / 12.011,
        "coal" => 0.80 * 44.01 / 12.011,
        _ => throw new ArgumentException($"unknown fuel {kind} (wood, charcoal, coal)"),
    };

    /// <summary>Water vapour a kilogram of fuel makes, kg: its hydrogen burned.</summary>
    public static double WaterPerKg(string kind) => kind switch
    {
        "wood" => 5 * 18.015 / 162.14,
        "charcoal" => 0,
        "coal" => 0.05 * 18.015 / 2.016,
        _ => throw new ArgumentException($"unknown fuel {kind} (wood, charcoal, coal)"),
    };

    /// <summary>The oxygen fraction (by volume) below which a fire goes out.</summary>
    public const double DefaultOxygenLimit = 0.15;
    public double OxygenLimit { get; set; } = DefaultOxygenLimit;
    /// <summary>Is there oxygen enough in its zone's air for it to burn?</summary>
    public bool Breathing => Zone.OxygenFraction >= OxygenLimit;
    public double OxygenUsed { get; private set; }                 // kg taken from its room's air, all told

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
    public double AirDensity => Zone.AirDensityAt(AmbientTemperature);
    /// <summary>The air it burns in: the planet's open air unless it is inside an enclosure.</summary>
    public Zone Zone { get; set; } = new();

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
    public bool Lit => Fuel > 0 && Power > 0 && !Drowned && Breathing;
    /// <summary>W reaching the target, last step. The runtime adds it to anything else heating the same target.</summary>
    public double HeatOut { get; private set; }

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
        if (!Lit) { Target.HeatInput = HeatOut = 0; return; }
        double density = EnergyDensity(FuelKind);
        double burn = Math.Min(Fuel, Power * Draught * dt / density);
        if (Zone is Enclosure room)
        {
            // it can take no more oxygen than its room has
            double o2 = burn * OxygenPerKg(FuelKind) / GasMix.O2MolarMass;
            if (o2 > room.Moles[0]) { burn *= room.Moles[0] / o2; o2 = room.Moles[0]; }
            room.ChangeGas(0, -o2);
            room.ChangeGas(2, burn * CarbonDioxidePerKg(FuelKind) / GasMix.CO2MolarMass);
            room.ChangeGas(3, burn * WaterPerKg(FuelKind) / GasMix.H2OMolarMass);
            OxygenUsed += o2 * GasMix.O2MolarMass;
        }
        double released = burn * density;
        Fuel -= burn;
        FuelBurned += burn;
        EnergyReleased += released;
        double boiled = Math.Min(Soak, released / QuenchHeat);
        Soak -= boiled;
        Boiled += boiled;
        Target.HeatInput = HeatOut = (released - boiled * QuenchHeat) * Efficiency / dt;
        // what its pot doesn't take warms the room it stands in
        if (Zone is Enclosure around) around.AddHeat((released - boiled * QuenchHeat) * (1 - Efficiency));
        if (Soak > 0 && Soak >= Fuel) Drowned = true;
    }
}
