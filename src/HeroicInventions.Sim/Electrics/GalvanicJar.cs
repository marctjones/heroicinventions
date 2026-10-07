namespace HeroicInventions.Sim.Electrics;

/// <summary>
/// A galvanic jar, the "Baghdad battery": a clay pot holding a copper tube
/// round an iron rod, filled with vinegar. The jars found at Khujut Rabu near
/// Ctesiphon (Parthian or Sasanian) match this shape, but whether they were
/// batteries is doubted: no wires were found, the bitumen seal shuts the
/// electrolyte in, and nothing plated in that period has turned up. The
/// replicas built since do make electricity, which is what this part is.
///
/// Deliberately no chemistry and no circuit (as for the found electrics of
/// issue #64): each of <see cref="Cells"/> jars in series gives
/// <see cref="Volts"/> at a steady <see cref="Milliamps"/> while it is
/// <see cref="On"/>, until its acid is spent. Defaults are Eggebrecht's
/// replica in 5% vinegar, 0.5 V at 0.15 mA: 75 µW a jar.
///
/// Its acid is the one number worked from the chemistry: 5% vinegar holds
/// 50 g/L of acetic acid (60.05 g/mol), and each acid molecule gives one
/// hydrogen ion to take one electron from the iron, so the replicas'
/// 45 mL (the copper tube, 26 mm across and 9 cm tall) passes 3,615 C,
/// 1.8 kJ at 0.5 V, over about 280 days. A 5 kWh bank would need one jar
/// for some 7,600 years, and the vinegar of about 10,000.
/// </summary>
public sealed class GalvanicJar
{
    public const double Faraday = 96485.33;                 // C/mol
    public const double AcidPerCubicMetre = 50_000 / 60.05;  // mol/m³ of acetic acid in 5% vinegar (50 g/L)

    public GalvanicJar(string name, int cells, double volts, double milliamps, double electrolyte)
    {
        if (cells < 1) throw new ArgumentException("needs at least one cell");
        if (volts <= 0 || milliamps < 0 || electrolyte <= 0) throw new ArgumentException("volts and electrolyte must be above 0, milliamps 0 or more");
        Name = name;
        Cells = cells;
        Volts = volts;
        Milliamps = milliamps;
        Electrolyte = electrolyte;
        Capacity = electrolyte * AcidPerCubicMetre * Faraday;
        ChargeLeft = Capacity;
    }

    public string Name { get; }
    public int Cells { get; }                     // jars in series: their volts add, the same current runs through each
    public double Volts { get; set; }             // V a jar while it delivers
    public double Milliamps { get; set; }         // mA it delivers
    public double Electrolyte { get; }            // m³ of 5% vinegar a jar
    public double Capacity { get; }               // C a jar can pass before its acid is spent
    public bool On { get; set; } = true;          // delivering (to whatever it would feed: a plating bath, a tongue, one day the bank)

    public double ChargeLeft { get; private set; }  // C left in each jar
    public double Delivered { get; private set; }   // J given out, all told
    public double Time { get; private set; }

    public bool Spent => ChargeLeft <= 0;
    /// <summary>The share of the acid used, 0 to 1.</summary>
    public double SpentFraction => 1 - ChargeLeft / Capacity;
    /// <summary>A: what runs now.</summary>
    public double Current => On && !Spent ? Milliamps / 1000 : 0;
    /// <summary>V across the whole stack while it delivers, 0 once spent.</summary>
    public double Voltage => Spent ? 0 : Cells * Volts;
    /// <summary>W delivered now.</summary>
    public double Power => Current * Voltage;
    /// <summary>s until the acid is spent at this current; infinite while off.</summary>
    public double TimeLeft => Current > 0 ? ChargeLeft / Current : double.PositiveInfinity;

    public void Step(double dt)
    {
        Time += dt;
        double current = Current;
        if (current <= 0) return;
        double q = Math.Min(current * dt, ChargeLeft);
        ChargeLeft -= q;
        Delivered += q * Cells * Volts;
    }
}
