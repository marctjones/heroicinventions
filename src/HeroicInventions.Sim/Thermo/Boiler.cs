namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// A sealed boiler of water over a fire, treated as liquid and vapour in
/// equilibrium: its pressure is the saturation pressure at its temperature.
/// Steam that leaves carries away latent heat, which is what limits an
/// aeolipile's top speed for a given fire.
///
/// Safety valves (<see cref="Valves"/>) vent steam once the pressure
/// reaches their lift. A boiler rated to <see cref="BurstPressure"/> (gauge
/// Pa; 0 is unrated and never bursts) bursts when it gets there: the shell
/// gives way, the water over 100 °C flashes to steam at once,
/// M·c·(T − 100)/L of it, the blast throws out the rest, and the boiler is
/// open and empty from then on.
/// </summary>
public sealed class Boiler(double waterMassKg, double temperatureC = 20, double heatInputW = 2000) : IHeated
{
    public double WaterMass { get; private set; } = waterMassKg; // kg
    public double Temperature { get; private set; } = temperatureC; // °C
    public double HeatInput { get; set; } = heatInputW;             // W, from the fire
    public double HeatLossCoefficient { get; init; } = 2.0;         // W/K to surrounding air at 20 °C
    public double HeatDelivered { get; private set; }               // J, cumulative — the energy-dashboard's "input" term
    public double HeatLost { get; private set; }                    // J, cumulative, to the air

    public double AbsolutePressure => Burst ? Physics.AtmosphericPressure : SaturationPressure(Temperature);
    public double GaugePressure => Math.Max(0, AbsolutePressure - Physics.AtmosphericPressure);

    public List<SafetyValve> Valves { get; } = [];
    public double BurstPressure { get; init; }                      // gauge Pa it is rated to; 0 never bursts
    public double Time { get; private set; }                        // s this boiler has been stepped
    public bool Burst { get; private set; }
    public double BurstTime { get; private set; }                   // s, when it burst
    public double BurstGauge { get; private set; }                  // gauge Pa it burst at
    public double Flashed { get; private set; }                     // kg of water flashed to steam as it burst
    public double Vented => Valves.Sum(v => v.Vented);              // kg let out by its safety valves
    public bool IsDry => WaterMass <= 0;

    /// <summary>Density of the steam in the boiler (ideal gas), kg/m³.</summary>
    public double SteamDensity =>
        AbsolutePressure * Physics.WaterMolarMass / (Physics.GasConstant * Physics.ToKelvin(Temperature));

    /// <summary>Advance the boiler, given how much steam the consumers drew this step (kg/s).</summary>
    public void Step(double dt, double steamOutflowKgPerS)
    {
        HeatDelivered += HeatInput * dt;
        Time += dt;
        foreach (var v in Valves) { v.Opening = 0; v.Flow = 0; }
        if (IsDry) return;
        double steamOut = Math.Min(steamOutflowKgPerS * dt, WaterMass);
        double lost = HeatLossCoefficient * (Temperature - 20) * dt;
        HeatLost += lost;
        double netHeat = HeatInput * dt - lost - steamOut * Physics.LatentHeatVaporization;
        if (Valves.Count > 0)
        {
            double vent = Vent(dt, netHeat, WaterMass - steamOut);
            steamOut += vent;
            netHeat -= vent * Physics.LatentHeatVaporization;
        }
        WaterMass -= steamOut;
        if (WaterMass > 0)
            Temperature += netHeat / (WaterMass * Physics.WaterSpecificHeat);
        if (BurstPressure > 0 && GaugePressure >= BurstPressure) BurstNow();
    }

    /// <summary>
    /// Each valve's flow at this step's pressure, kg. An explicit step could
    /// vent past the lift and chatter, so no more goes than would bring the
    /// water back down to the lowest lift's saturation temperature.
    /// </summary>
    private double Vent(double dt, double netHeat, double water)
    {
        double gauge = GaugePressure, absolute = AbsolutePressure, want = 0;
        foreach (var v in Valves)
        {
            v.Opening = v.OpeningAt(gauge);
            v.Flow = v.Discharge(gauge, absolute, Temperature);
            want += v.Flow * dt;
        }
        if (want <= 0) return 0;
        double seat = SaturationTemperature(Physics.AtmosphericPressure + Valves.Min(v => v.LiftPressure));
        double room = (netHeat + water * Physics.WaterSpecificHeat * (Temperature - seat)) / Physics.LatentHeatVaporization;
        double vent = Math.Clamp(Math.Min(want, room), 0, water);
        foreach (var v in Valves)
        {
            v.Flow *= vent / want;
            v.Vented += v.Flow * dt;
        }
        return vent;
    }

    private void BurstNow()
    {
        Burst = true;
        BurstTime = Time;
        BurstGauge = SaturationPressure(Temperature) - Physics.AtmosphericPressure;
        Flashed = Math.Min(WaterMass, WaterMass * Physics.WaterSpecificHeat * Math.Max(0, Temperature - 100) / Physics.LatentHeatVaporization);
        WaterMass = 0;
        Temperature = 100;
    }

    /// <summary>
    /// Pour in feed water. It mixes at once: the boiler's heat is shared
    /// over the larger mass, T = (M·T + m·Tfeed) / (M + m).
    /// </summary>
    public void AddWater(double kg, double temperatureC)
    {
        if (kg <= 0 || Burst) return; // a burst boiler holds nothing
        Temperature = (WaterMass * Temperature + kg * temperatureC) / (WaterMass + kg);
        WaterMass += kg;
        WaterFed += kg;
    }

    public double WaterFed { get; private set; }                    // kg of feed water taken in

    /// <summary>
    /// Saturation pressure of water (Pa) from the Antoine equation, using the
    /// two standard coefficient sets for 1–100 °C and 99–374 °C.
    /// </summary>
    public static double SaturationPressure(double celsius)
    {
        var (a, b, c) = celsius < 100
            ? (8.07131, 1730.63, 233.426)
            : (8.14019, 1810.94, 244.485);
        double mmHg = Math.Pow(10, a - b / (c + celsius));
        return mmHg * 133.322;
    }

    /// <summary>The Antoine equation turned round: the temperature (°C) at which water boils under an absolute pressure (Pa).</summary>
    public static double SaturationTemperature(double absolutePa)
    {
        var (a, b, c) = absolutePa < SaturationPressure(100)
            ? (8.07131, 1730.63, 233.426)
            : (8.14019, 1810.94, 244.485);
        return b / (a - Math.Log10(absolutePa / 133.322)) - c;
    }
}
