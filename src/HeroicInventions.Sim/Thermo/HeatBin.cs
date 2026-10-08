namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// A lidded bin round a heat store (issue #71), the storage heater's damper: an insulated box with a lid that is
/// opened to let the store's heat into the room and closed to hold it back. Shut, the bin still leaks through its lid,
/// <see cref="Leak"/> W/K (the lid must leak 0.1 W/K or less to hold a vault's bank at 40 °C: a 0.5 W/K lid lets it
/// reach 60 °C); open, the store is exposed to the room in full. The lid is <see cref="Open"/> 0 (shut) … 1 (wide),
/// and the heat passed is the blend
///
///   G = (1 − open)·G_leak + open·G_exposed.
///
/// A thermostat works it: a <see cref="BimetalStrip"/> (issue #97), the physical one, which sets <see cref="Open"/> in proportion to
/// where its tip is. The bin also keeps its ideal stand-in (issue #71), the limit the strip is compared with: it may <see cref="Sense"/>
/// a store (the bank) and swing its lid by hysteresis, wide open when the bank has cooled to <see cref="OpenBelow"/> °C, shut when it
/// has warmed to <see cref="CloseAbove"/>, and left as it is between; an ideal switch that senses the bank's own temperature with no
/// lag. A strip on the lid takes its place (the runtime clears <see cref="Sense"/>).
/// </summary>
public sealed class HeatBin(string name, HeatStore store, double leak)
{
    public string Name { get; } = name;
    public HeatStore Store { get; } = store;
    public double Leak { get; set; } = leak;                       // W/K through the shut lid
    public double Open { get => _open; set => _open = Math.Clamp(value, 0, 1); }
    private double _open;
    /// <summary>The store whose temperature works the lid, or null for a lid worked by hand.</summary>
    public HeatStore? Sense { get; set; }
    public double OpenBelow { get; set; } = 5;                     // °C
    public double CloseAbove { get; set; } = 40;                   // °C
    /// <summary>How many times the lid has swung open.</summary>
    public int Openings { get; private set; }

    /// <summary>W/K the store passes to the room now, given what it would pass bare.</summary>
    public double Blend(double exposed) => (1 - _open) * Leak + _open * exposed;

    /// <summary>Works the lid from the sensed temperature, if there is a thermostat.</summary>
    public void Step()
    {
        if (Sense is null) return;
        double t = Sense.Temperature;
        if (_open < 0.5 && t <= OpenBelow) { _open = 1; Openings++; }
        else if (_open >= 0.5 && t >= CloseAbove) _open = 0;
    }
}
