namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// Papin's safety valve (1679): a disc held on a seat in the boiler's lid by
/// a weighted lever, lifting once the steam under it pushes harder than the
/// weight. It starts to open at <see cref="LiftPressure"/> (gauge Pa) and is
/// fully lifted <see cref="Accumulation"/> × that higher, opening in
/// proportion between; open, it vents steam through its bore as a
/// compressible nozzle from the boiler's pressure to the air,
/// m = Cd·A·P₀·√(2k/((k−1)·R·T₀)·(r^(2/k) − r^((k+1)/k))), r = Pₐ/P₀ but no
/// less than the critical ratio (2/(k+1))^(k/(k−1)), where the jet chokes.
/// The boiler holds where what it vents carries off what the fire brings:
/// m·L = Q − losses.
/// </summary>
public sealed class SafetyValve(double liftPressurePa, double boreDiameter)
{
    public const double SteamGamma = 1.3;
    public const double DefaultCoefficient = 0.8;
    public const double DefaultAccumulation = 0.1;
    public static double SteamGasConstant => Physics.GasConstant / Physics.WaterMolarMass; // J/(kg·K)

    private double _lift = Math.Max(0, liftPressurePa);
    public double LiftPressure { get => _lift; set => _lift = Math.Max(0, value); } // gauge Pa; a tied-down valve is a very high one
    public double Bore { get; } = boreDiameter;                                     // m
    public double Area => Math.PI * Bore * Bore / 4;
    public double Cd { get; init; } = DefaultCoefficient;
    public double Accumulation { get; init; } = DefaultAccumulation;

    public double Opening { get; internal set; }  // 0 seated … 1 fully lifted
    public double Flow { get; internal set; }     // kg/s of steam, last step
    public double Vented { get; internal set; }   // kg vented so far

    public double OpeningAt(double gaugePa) =>
        Math.Clamp((gaugePa - LiftPressure) / Math.Max(1, Accumulation * LiftPressure), 0, 1);

    /// <summary>Steam through the fully open bore from P₀ (absolute Pa) at T₀ (°C) into the air, kg/s.</summary>
    public double Capacity(double absolutePa, double celsius)
    {
        if (absolutePa <= Physics.AtmosphericPressure) return 0;
        double k = SteamGamma;
        double critical = Math.Pow(2 / (k + 1), k / (k - 1));
        double r = Math.Max(Physics.AtmosphericPressure / absolutePa, critical);
        double psi = 2 * k / ((k - 1) * SteamGasConstant * Physics.ToKelvin(celsius))
                     * (Math.Pow(r, 2 / k) - Math.Pow(r, (k + 1) / k));
        return Cd * Area * absolutePa * Math.Sqrt(psi);
    }

    public double Discharge(double gaugePa, double absolutePa, double celsius) =>
        OpeningAt(gaugePa) * Capacity(absolutePa, celsius);
}
