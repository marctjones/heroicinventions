namespace HeroicInventions.Sim;

/// <summary>Shared physical constants. Everything in the sim core is SI (m, kg, s, Pa, J, W) except temperatures, which are °C. Gravity, the air's pressure and its density are Earth's here, for reference and defaults: parts read theirs from the <see cref="Zone"/> they stand in, set by the scene's <see cref="Planet"/> (issue #38).</summary>
public static class Physics
{
    public const double Gravity = 9.81;               // m/s²
    public const double WaterDensity = 1000.0;         // kg/m³
    public const double AtmosphericPressure = 101_325; // Pa (absolute)
    public const double GasConstant = 8.314;           // J/(mol·K)
    public const double WaterMolarMass = 0.018015;     // kg/mol
    public const double WaterSpecificHeat = 4186;      // J/(kg·K)
    public const double LatentHeatVaporization = 2.257e6; // J/kg at 100 °C
    public const double AirGasConstant = 287.05;       // J/(kg·K), specific gas constant of dry air
    public const double AirSpecificHeatCv = 718;       // J/(kg·K) at constant volume

    /// <summary>Density of dry air at atmospheric pressure and 20 °C, kg/m³ (ideal gas law).</summary>
    public static double AirDensity => AirDensityAt(20);

    /// <summary>Density of dry air at atmospheric pressure and <paramref name="celsius"/>, kg/m³: P / (R·T).</summary>
    public static double AirDensityAt(double celsius) => AtmosphericPressure / (AirGasConstant * ToKelvin(celsius));

    public static double ToKelvin(double celsius) => celsius + 273.15;

    /// <summary>Converts a gauge pressure into metres of water column (hydraulic head).</summary>
    public static double PressureToHead(double gaugePa) => gaugePa / (WaterDensity * Gravity);
}

/// <summary>Something a fire can heat: a boiler's water, a vessel's air. W, set each step.</summary>
public interface IHeated
{
    double HeatInput { get; set; }
}
