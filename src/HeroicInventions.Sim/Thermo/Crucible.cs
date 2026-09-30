namespace HeroicInventions.Sim.Thermo;

/// <summary>What a kind of sand makes, melted (issue #56).</summary>
/// <param name="MeltingPoint">°C</param>
/// <param name="LatentHeat">J/kg of fusion</param>
/// <param name="SpecificHeat">J/(kg·K)</param>
/// <param name="Transmittance">the share of sunlight a pane of its glass lets through</param>
public sealed record SandKind(string Name, double MeltingPoint, double LatentHeat, double SpecificHeat, double Transmittance)
{
    /// <summary>
    /// Basalt, most of Mars's sand: melts near 1,200 °C (with about 0.4 MJ/kg
    /// of fusion) into dark, nearly opaque glass, cast basalt. Silica, like the
    /// nearly pure opaline silica Spirit found at Home Plate: fused silica
    /// needs about 1,700 °C (its heat of fusion ~0.14 MJ/kg, 9.6 kJ/mol) and
    /// makes clear glass. c ≈ 1 kJ/(kg·K) for both. Game numbers, by name.
    /// </summary>
    public static SandKind Named(string name) => name switch
    {
        "basalt" => new("basalt", 1200, 400_000, 1000, 0.05),
        "silica" => new("silica", 1700, 140_000, 1000, 0.90),
        _ => throw new ArgumentException($"unknown sand {name} (basalt, silica)"),
    };
}

/// <summary>
/// A crucible of sand at a focal spot (issue #56): whatever light the
/// mirrors land on its <see cref="Spot"/> it absorbs (as a grey body of
/// <see cref="Emissivity"/>), and its hot face radiates back out,
/// ε·σ·A·(T⁴ − T_air⁴). So it heats until it re-radiates what it receives:
/// its stagnation temperature satisfies σ·(T⁴ − T_air⁴) = C·I, the flux on
/// the spot, whatever its emissivity. Ten flat heliostats (C ≈ 10) under
/// Mars's ~430 W/m² stop near 240 °C and never melt basalt; a curved burning
/// mirror or a lens squeezing its light into a small spot (C near 1,000) can.
///
/// Its charge warms at m·c, then melts at the sand's melting point, taking
/// the latent heat, then the melt warms again. What has melted is glass
/// (<see cref="Glass"/>), as clear as its sand makes it.
/// </summary>
public sealed class Crucible(string name, SandKind sand, double charge, double spot, double temperatureC) : IHeated
{
    public const double StefanBoltzmann = 5.670374e-8;   // W/(m²·K⁴)

    public string Name { get; } = name;
    public SandKind Sand { get; } = sand;
    public double Charge { get; } = charge;                    // kg of sand
    public double Spot { get; } = spot;                        // m², the hot face the light lands on
    public double Emissivity { get; set; } = 0.9;
    public Zone Zone { get; set; } = new();
    public double HeatInput { get; set; }                      // W landing on the spot
    public double Temperature { get; private set; } = temperatureC;   // °C
    public double Melted { get; private set; }                 // kg melted so far: the glass
    public double Glass => Melted;
    public double Absorbed { get; private set; }               // J taken in, all told
    public double Radiated { get; private set; }               // J re-radiated, all told
    public double MeltTime { get; private set; } = double.NaN; // s, when the whole charge had melted
    public double Time { get; private set; }

    /// <summary>W/m² landing on the spot: C·I.</summary>
    public double Flux => Spot > 0 ? HeatInput / Spot : 0;
    /// <summary>W its hot face radiates now.</summary>
    public double Radiation => Emissivity * StefanBoltzmann * Spot *
        (Math.Pow(Physics.ToKelvin(Temperature), 4) - Math.Pow(Physics.ToKelvin(Zone.Temperature), 4));
    /// <summary>°C at which it would re-radiate all it receives now: σ(T⁴ − T_air⁴) = C·I.</summary>
    public double Stagnation => Math.Pow(Flux / StefanBoltzmann + Math.Pow(Physics.ToKelvin(Zone.Temperature), 4), 0.25) - 273.15;

    public void Step(double dt)
    {
        Time += dt;
        // a stiff radiator: small steps, so a tiny, fierce spot doesn't overshoot
        int n = Math.Max(1, (int)Math.Ceiling(dt / 0.05));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            double net = Emissivity * HeatInput - Radiation;
            Absorbed += Emissivity * HeatInput * h;
            Radiated += Radiation * h;
            double energy = net * h, cap = Charge * Sand.SpecificHeat;
            if (Charge <= 0) return;
            // warming to the melting point, then melting, then the melt warming
            if (energy > 0 && Temperature < Sand.MeltingPoint)
            {
                double toMelt = (Sand.MeltingPoint - Temperature) * cap;
                if (energy <= toMelt) { Temperature += energy / cap; continue; }
                Temperature = Sand.MeltingPoint;
                energy -= toMelt;
            }
            if (energy > 0 && Melted < Charge && Temperature >= Sand.MeltingPoint)
            {
                double melt = Math.Min(Charge - Melted, energy / Sand.LatentHeat);
                Melted += melt;
                energy -= melt * Sand.LatentHeat;
                if (Melted >= Charge && double.IsNaN(MeltTime)) MeltTime = Time - dt + (i + 1) * h;
            }
            Temperature += energy / cap;   // what is left warms the melt, or a loss cools it (the glass stays glass)
        }
    }
}
