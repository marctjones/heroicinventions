namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// A sealed boiler of water over a fire, treated as liquid and vapour in
/// equilibrium: its pressure is the saturation pressure at its temperature.
/// Steam that leaves carries away latent heat, which is what limits an
/// aeolipile's top speed for a given fire.
/// </summary>
public sealed class Boiler(double waterMassKg, double temperatureC = 20, double heatInputW = 2000)
{
    public double WaterMass { get; private set; } = waterMassKg; // kg
    public double Temperature { get; private set; } = temperatureC; // °C
    public double HeatInput { get; set; } = heatInputW;             // W, from the fire
    public double HeatLossCoefficient { get; init; } = 2.0;         // W/K to surrounding air at 20 °C

    public double AbsolutePressure => SaturationPressure(Temperature);
    public double GaugePressure => Math.Max(0, AbsolutePressure - Physics.AtmosphericPressure);
    public bool IsDry => WaterMass <= 0;

    /// <summary>Density of the steam in the boiler (ideal gas), kg/m³.</summary>
    public double SteamDensity =>
        AbsolutePressure * Physics.WaterMolarMass / (Physics.GasConstant * Physics.ToKelvin(Temperature));

    /// <summary>Advance the boiler, given how much steam the consumers drew this step (kg/s).</summary>
    public void Step(double dt, double steamOutflowKgPerS)
    {
        if (IsDry) return;
        double steamOut = Math.Min(steamOutflowKgPerS * dt, WaterMass);
        double netHeat = HeatInput * dt
                         - HeatLossCoefficient * (Temperature - 20) * dt
                         - steamOut * Physics.LatentHeatVaporization;
        WaterMass -= steamOut;
        if (WaterMass > 0)
            Temperature += netHeat / (WaterMass * Physics.WaterSpecificHeat);
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
}
