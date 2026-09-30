using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// A warm pond (issue #58): a tank's water given a temperature, warmed by
/// whatever heats it (a <see cref="Heater"/>, a mirror, a fire) and cooled by
/// its own evaporation. Water evaporates into the air over it at a rate set by
/// the gap between the surface's vapour pressure and the air's (Dalton, 1802;
/// Carrier's still-air coefficient, ASHRAE: E = k·A·(p_sat(T_w) − p_v),
/// k = 0.089/2.45e6 ≈ 3.6e-8 kg/(s·m²·Pa)), and each kilogram takes the latent
/// heat, 2.257 MJ, with it. In an enclosure the vapour joins its air as H₂O;
/// in the open it is gone.
/// </summary>
public sealed class Pond(string name, Tank tank, double temperatureC) : IHeated
{
    public const double DefaultCoefficient = 3.6e-8;   // kg/(s·m²·Pa)

    public string Name { get; } = name;
    public Tank Tank { get; } = tank;
    public double Temperature { get; private set; } = temperatureC;   // °C of the water
    public double Heater { get; set; }                                // W, its own
    public double HeatInput { get; set; }                             // W from mirrors and fires, set each step
    public double Coefficient { get; set; } = DefaultCoefficient;
    public double Evaporation { get; private set; }                   // kg/s, last step
    public double Evaporated { get; private set; }                    // kg, all told
    public Zone Air => Tank.Zone;

    /// <summary>Pa of water vapour in the air over it: its zone's H₂O share.</summary>
    public double AirVapour => Air is Enclosure room ? room.PartialPressure(3) : Air.Pressure * Air.Air.H2O / Math.Max(1e-12, Air.Air.Total);

    public void Step(double dt)
    {
        double mass = Tank.WaterVolume * Physics.WaterDensity;
        Evaporation = 0;
        if (mass <= 1e-9) return;
        double q = Heater + HeatInput;
        double rate = Temperature > 0 ? Coefficient * Tank.Area * Math.Max(0, Boiler.SaturationPressure(Temperature) - AirVapour) : 0;
        double kg = Math.Min(rate * dt, mass);
        Temperature += (q * dt - kg * Physics.LatentHeatVaporization) / (Math.Max(mass - kg, 1e-6) * Physics.WaterSpecificHeat);
        Temperature = Math.Max(0, Temperature);   // it freezes at 0 (ice is Tank's business)
        Tank.WaterVolume -= kg / Physics.WaterDensity;
        if (Air is Enclosure room) room.ChangeGas(3, kg / GasMix.H2OMolarMass);
        Evaporation = kg / dt;
        Evaporated += kg;
    }
}

/// <summary>
/// A cold roof on an enclosure (issue #58): its inside is chilled by the
/// outside through <see cref="Conductance"/> W/K. Where it is colder than the
/// air's dew point vapour condenses on it, and a condensing surface stands at
/// the dew point, so the heat leaving through it is U·(T_dew − T_out) and it
/// condenses that heat's worth of water: ṁ = U·(T_dew − T_out)/L, 1 kW making
/// 1.6 kg an hour. The condensate runs to its <see cref="Gutter"/> (a tank at
/// roof height, raising water with no pump) or drips back to the floor.
/// </summary>
public sealed class Roof(string name, Enclosure room, double conductance)
{
    public string Name { get; } = name;
    public Enclosure Room { get; } = room;
    public double Conductance { get; set; } = conductance;            // W/K, inside to outside
    public Tank? Gutter { get; init; }
    public double Rain { get; private set; }                          // kg/s condensing, last step
    public double Rained { get; private set; }                        // kg, all told
    public double Heat => Rain * Physics.LatentHeatVaporization;      // W leaving through it

    /// <summary>°C at which the room's air would begin to fog a surface: water's saturation temperature at its vapour pressure.</summary>
    public double DewPoint => Room.PartialPressure(3) > 0 ? Boiler.SaturationTemperature(Room.PartialPressure(3)) : double.NegativeInfinity;
    public bool Fogged => Rain > 0;

    public void Step(double dt)
    {
        Rain = 0;
        double dew = DewPoint, outside = Room.Outside.Temperature;
        if (!(dew > outside)) return;
        double kg = Conductance * (dew - outside) / Physics.LatentHeatVaporization * dt;
        kg = Math.Min(kg, 0.5 * Room.Moles[3] * GasMix.H2OMolarMass);  // no more than half the vapour in a step
        Room.ChangeGas(3, -kg / GasMix.H2OMolarMass);
        if (Gutter is { } g) g.WaterVolume = Math.Min(g.Capacity, g.WaterVolume + kg / Physics.WaterDensity);
        Rain = kg / dt;
        Rained += kg;
    }
}
