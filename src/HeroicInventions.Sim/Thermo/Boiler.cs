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
///
/// The rating is either given (<see cref="BurstPressure"/>) or the shell's
/// own: a boiler with a wall (<see cref="Wall"/>, m) of a material with
/// tensile strength σ_t and a radius r bursts, by the thin-wall hoop stress
/// σ = P·r/t, at P = σ_t·t/r (<see cref="ShellPressure"/>). The shell is at
/// the water's temperature, and a metal loses strength towards its melting
/// point T_m (°C): none up to T_m/2, then falling linearly to nothing at T_m
/// (<see cref="Derating"/>). A lead pot at 138 °C is still below its onset
/// (164 °C), so it holds what it holds cold; one fired on to 250 °C holds
/// about a third of that. Linear from half the melting point is a simple,
/// stated rule, not a fit to any one alloy's curve. Bronze and copper are in
/// truth already weaker above ~200 °C, which this rule leaves out.
/// </summary>
public sealed class Boiler(double waterMassKg, double temperatureC = 20, double heatInputW = 2000) : IHeated
{
    /// <summary>The air and gravity it stands in: the planet's open air unless it is inside an enclosure.</summary>
    public Zone Zone { get; set; } = new();
    public double WaterMass { get; private set; } = waterMassKg; // kg
    public double Temperature { get; private set; } = temperatureC; // °C
    public double HeatInput { get; set; } = heatInputW;             // W, from the fire
    public double HeatLossCoefficient { get; init; } = 2.0;         // W/K to the surrounding air
    public double AmbientTemperature { get; set; } = 20;           // °C of that air: with no fire it cools towards this (Newton)
    public double HeatDelivered { get; private set; }               // J, cumulative — the energy-dashboard's "input" term
    public double HeatLost { get; private set; }                    // J, cumulative, to the air

    public double AbsolutePressure => Burst ? Zone.Pressure : SaturationPressure(Temperature);
    public double GaugePressure => Math.Max(0, AbsolutePressure - Zone.Pressure);

    public List<SafetyValve> Valves { get; } = [];
    public double BurstPressure { get; init; }                      // gauge Pa it is explicitly rated to; wins over the shell; 0 means "not given"
    public double Wall { get; init; }                               // m of shell thickness; 0 means no wall given
    public double ShellRadius { get; init; }                        // m, the hoop radius
    public double ShellStrength { get; init; }                      // Pa, tensile strength of the shell's material
    public double? ShellMelting { get; init; }                      // °C, melting point of the shell's metal, or null if it does not soften

    /// <summary>The cold gauge pressure the shell holds: σ_t·t/r (0 with no wall).</summary>
    public double ShellPressure => Wall > 0 && ShellRadius > 0 ? ShellStrength * Wall / ShellRadius : 0;

    /// <summary>The share of cold strength a shell at <paramref name="celsius"/> keeps: 1 up to half its melting point, then linearly to 0 at it.</summary>
    public double Derating(double celsius) =>
        ShellMelting is not { } tm || tm <= 0 ? 1 : Math.Clamp((tm - celsius) / (tm / 2), 0, 1);

    /// <summary>The gauge pressure it is rated to when cold: the explicit rating if given, else the shell's; 0 is unrated and never bursts.</summary>
    public double Rating => BurstPressure > 0 ? BurstPressure : ShellPressure;

    /// <summary>The gauge pressure it bursts at now: an explicit rating is fixed; a shell's is derated by its temperature.</summary>
    public double BurstLimit => BurstPressure > 0 ? BurstPressure : ShellPressure * Derating(Temperature);
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
        double lost = HeatLossCoefficient * (Temperature - AmbientTemperature) * dt;
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
            Temperature = Math.Max(0, Temperature + netHeat / (WaterMass * Physics.WaterSpecificHeat)); // freezing a boiler isn't modelled: in a frost it stops at 0 °C
        if (Rating > 0 && GaugePressure >= BurstLimit) BurstNow();
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
            v.Flow = v.Discharge(gauge, absolute, Temperature, Zone.Pressure);
            want += v.Flow * dt;
        }
        if (want <= 0) return 0;
        double seat = SaturationTemperature(Zone.Pressure + Valves.Min(v => v.LiftPressure));
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
        BurstGauge = SaturationPressure(Temperature) - Zone.Pressure;
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
    /// A stoker's fill or bleed (issue #156): bring the water to <paramref name="kg"/>. More is poured in at the feed
    /// water's temperature and mixes at once (<see cref="AddWater"/>), so a boiler at the boil takes its heat back
    /// into the cold water; less is drawn off at the boiler's own temperature, which leaves that unchanged.
    /// </summary>
    public void SetWater(double kg, double feedTemperatureC)
    {
        kg = Math.Max(0, kg);
        if (kg > WaterMass) AddWater(kg - WaterMass, feedTemperatureC);
        else if (!Burst) WaterMass = kg;
    }

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
