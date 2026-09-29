namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// A fire under a boiler. It burns a store of fuel at a steady heat output
/// (W); the fraction of that heat that reaches the water is its efficiency
/// (an open hearth loses most of it up the chimney). Fuel burns at
/// power ÷ energy density, and the fire goes out when it is gone — so the
/// total heat a load of fuel can ever give is fuel × energy × efficiency.
/// </summary>
public sealed class Hearth(Boiler boiler, double powerW, double fuelKg, string fuelKind = "wood", double efficiency = 0.5)
{
    /// <summary>Lower heating values, J/kg: air-dried wood, charcoal, bituminous coal.</summary>
    public static double EnergyDensity(string kind) => kind switch
    {
        "wood" => 15e6,
        "charcoal" => 29e6,
        "coal" => 24e6,
        _ => throw new ArgumentException($"unknown fuel {kind} (wood, charcoal, coal)"),
    };

    public Boiler Boiler { get; } = boiler;
    public string FuelKind { get; } = fuelKind;
    public double Efficiency { get; } = efficiency;
    public double Power { get; set; } = powerW;                    // W of heat released while burning
    public double Fuel { get; set; } = fuelKg;                     // kg left
    public double FuelBurned { get; private set; }                 // kg
    public double EnergyReleased { get; private set; }             // J
    public bool Lit => Fuel > 0 && Power > 0;

    /// <summary>Burn for dt seconds, setting what the boiler is heated by this step.</summary>
    public void Step(double dt)
    {
        if (!Lit) { Boiler.HeatInput = 0; return; }
        double density = EnergyDensity(FuelKind);
        double burn = Math.Min(Fuel, Power * dt / density);
        double released = burn * density;
        Fuel -= burn;
        FuelBurned += burn;
        EnergyReleased += released;
        Boiler.HeatInput = released * Efficiency / dt;
    }
}
