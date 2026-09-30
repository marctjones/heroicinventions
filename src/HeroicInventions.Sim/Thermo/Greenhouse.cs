using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// Wood as cellulose, C₆H₁₀O₅ (issue #42): what growing it takes and gives,
/// the reverse of burning it (<see cref="Hearth"/>), so a grow-then-burn
/// cycle hands back exactly the oxygen it took: 6 CO₂ + 5 H₂O → C₆H₁₀O₅ + 6 O₂.
/// A kilogram of wood holds 6/0.16214 = 37.0 mol of carbon; growing it takes
/// 1.63 kg of CO₂ and 0.556 kg of water and gives 1.18 kg of O₂ (1.07 would be
/// for glucose units, CH₂O).
/// </summary>
public static class Wood
{
    public const double MolarMass = 0.16214;                 // kg/mol of C₆H₁₀O₅
    public const double ChemicalEnergy = 18e6;               // J/kg, dry wood burned
    public static double CarbonDioxidePerKg => 6 * GasMix.CO2MolarMass / MolarMass;
    public static double WaterPerKg => 5 * GasMix.H2OMolarMass / MolarMass;
    public static double OxygenPerKg => 6 * GasMix.O2MolarMass / MolarMass;
}

/// <summary>
/// Plants in a bed of <see cref="Area"/> m² (issue #42), fast woody ones:
/// willow, poplar, bamboo. Under light they grow wood, keeping
/// <see cref="Efficiency"/> of the light they absorb as its chemical energy
/// (18 MJ/kg): 0.5% gives 1.5 kg/m² a year under Earth's sun, the 1–2 kg of
/// coppiced willow, and about 0.5 kg under Mars's sun through glass. They
/// take the CO₂ from their room's air and the water from their tank, and
/// give the O₂ back to the air; day and night they respire a little of their
/// wood away (<see cref="Respiration"/>, kg/(m²·s)), the same reaction
/// backwards. They grow only above freezing, with water and CO₂ to hand.
/// Their light is what comes in through their room's glass (#57), as much of
/// it as their bed can catch, or the sun itself in the open.
/// </summary>
public sealed class Plants(string name, double area, Tank water, Func<double> light)
{
    public const double DefaultEfficiency = 0.005;
    public const double DefaultRespiration = 0.01e-3 / 3600;   // 0.01 g/(m²·h)

    public string Name { get; } = name;
    public double Area { get; } = area;
    public Tank Water { get; } = water;
    public double Efficiency { get; set; } = DefaultEfficiency;
    public double Respiration { get; set; } = DefaultRespiration;
    public Zone Zone { get; set; } = new();
    public double Wood { get; set; }                         // kg of standing wood
    public double Grown { get; private set; }                // kg grown, all told (gross)
    public double Respired { get; private set; }             // kg respired away, all told
    public double OxygenMade { get; private set; }           // kg, net
    public double Light => light();                          // W absorbed
    public double GrowthRate { get; private set; }           // kg/s, gross, last step
    public Hearth? Store { get; init; }                      // where a harvest goes

    /// <summary>Cuts the wood (all of it, or kg) and stacks it on the store's fire; returns the kg.</summary>
    public double Harvest(double kg = double.PositiveInfinity)
    {
        double cut = Math.Clamp(kg, 0, Wood);
        Wood -= cut;
        if (Store is { } fire) fire.Fuel += cut;
        return cut;
    }

    public void Step(double dt)
    {
        var room = Zone as Enclosure;
        // growth: light kept as wood, while it is warm and has water and CO₂
        double grow = Zone.Temperature > 0 ? Efficiency * Math.Max(0, Light) / Wood_.ChemicalEnergy * dt : 0;
        grow = Math.Min(grow, Water.WaterVolume * Physics.WaterDensity / Wood_.WaterPerKg);
        if (room is not null) grow = Math.Min(grow, room.Moles[2] * GasMix.CO2MolarMass / Wood_.CarbonDioxidePerKg);
        else if (Zone.Air.CO2 <= 0) grow = 0;
        // respiration: some of the wood burned back to CO₂ and water, with the room's oxygen
        double breathe = Math.Min(Wood + grow, Respiration * Area * dt);
        if (room is not null) breathe = Math.Min(breathe, room.Moles[0] * GasMix.O2MolarMass / Wood_.OxygenPerKg);
        double net = grow - breathe;
        Wood += net;
        Grown += grow;
        Respired += breathe;
        GrowthRate = grow / dt;
        Water.WaterVolume = Math.Max(0, Water.WaterVolume - grow * Wood_.WaterPerKg / Physics.WaterDensity);
        OxygenMade += net * Wood_.OxygenPerKg;
        if (room is null) return;
        room.ChangeGas(2, -net * Wood_.CarbonDioxidePerKg / GasMix.CO2MolarMass);
        room.ChangeGas(0, net * Wood_.OxygenPerKg / GasMix.O2MolarMass);
        room.ChangeGas(3, breathe * Wood_.WaterPerKg / GasMix.H2OMolarMass);   // respired water goes to the air as vapour
    }

    private static class Wood_
    {
        public const double ChemicalEnergy = Thermo.Wood.ChemicalEnergy;
        public static double CarbonDioxidePerKg => Thermo.Wood.CarbonDioxidePerKg;
        public static double WaterPerKg => Thermo.Wood.WaterPerKg;
        public static double OxygenPerKg => Thermo.Wood.OxygenPerKg;
    }
}

/// <summary>
/// An ice drill and melter (issue #42): it cuts ice from the ground (at the
/// ground's temperature) and melts it into a tank, taking
/// c_ice·(0 − T_ice) + 334 kJ per kilogram: from Mars's −63 °C,
/// 2,100 × 63 + 334,000 = 466.3 kJ/kg, 7.7 kg an hour a kilowatt. Its heat is
/// its own <see cref="Power"/> plus any mirror or fire aimed at it.
/// </summary>
public sealed class Melter(string name, Tank into, double iceTemperatureC) : IHeated
{
    public const double IceSpecificHeat = 2100;   // J/(kg·K)
    public string Name { get; } = name;
    public Tank Into { get; } = into;
    public double IceTemperature { get; set; } = iceTemperatureC;
    public double Power { get; set; }             // W, its own
    public double HeatInput { get; set; }         // W from mirrors and fires
    public double Melted { get; private set; }    // kg
    public double Rate { get; private set; }      // kg/s, last step
    /// <summary>J a kilogram of ice takes to become water at 0 °C.</summary>
    public double HeatPerKg => IceSpecificHeat * Math.Max(0, -IceTemperature) + Tank.LatentHeatFusion;

    public void Step(double dt)
    {
        double kg = (Power + HeatInput) * dt / HeatPerKg;
        kg = Math.Min(kg, (Into.Capacity - Into.WaterVolume) * Physics.WaterDensity);   // stops when the tank is full
        Into.WaterVolume += Math.Max(0, kg) / Physics.WaterDensity;
        Rate = Math.Max(0, kg) / dt;
        Melted += Math.Max(0, kg);
    }
}

/// <summary>
/// An electrolyser (issue #42): splits water from its tank into oxygen, given
/// to the air it stands in, and hydrogen (vented), with electrical
/// <see cref="Power"/>: 2 H₂O → 2 H₂ + O₂ takes water's 286 kJ/mol, 17.9 MJ per
/// kg of O₂ at 100%, more by 1/<see cref="Efficiency"/>. Burning a kilogram of
/// wood (1.18 kg of O₂) with electrolysed oxygen costs about 21 MJ of
/// electricity for 18 MJ of heat: allowed, and usually a poor trade.
/// </summary>
public sealed class Electrolyser(string name, Tank water, double power, double efficiency)
{
    public const double JoulesPerKgOxygen = 286e3 * 2 / GasMix.O2MolarMass;   // 17.9 MJ
    public string Name { get; } = name;
    public Tank Water { get; } = water;
    public double Power { get; set; } = power;           // W of electricity
    public double Efficiency { get; set; } = efficiency;
    public Zone Zone { get; set; } = new();
    public double Oxygen { get; private set; }           // kg made
    public double Hydrogen { get; private set; }         // kg made (vented)
    public double Energy { get; private set; }           // J spent
    public double Rate { get; private set; }             // kg/s of O₂, last step

    public void Step(double dt)
    {
        double o2 = Math.Max(0, Power) * Efficiency * dt / JoulesPerKgOxygen;
        double water = o2 * 2 * GasMix.H2OMolarMass / GasMix.O2MolarMass;          // 1.126 kg of water a kg of O₂
        double have = Water.WaterVolume * Physics.WaterDensity;
        if (water > have) { o2 *= have / water; water = have; }
        Water.WaterVolume -= water / Physics.WaterDensity;
        if (Zone is Enclosure room) room.ChangeGas(0, o2 / GasMix.O2MolarMass);
        Oxygen += o2;
        Hydrogen += water - o2;
        Energy += o2 > 0 ? Math.Max(0, Power) * dt : 0;
        Rate = o2 / dt;
    }
}
