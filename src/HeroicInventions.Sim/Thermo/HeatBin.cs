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
public sealed class HeatBin(string name, HeatStore? store, double leak)
{
    public string Name { get; } = name;
    /// <summary>The store built into it (issue #71), or null for a bin that takes what is put in it (issue #206).</summary>
    public HeatStore? Fixed { get; } = store;
    /// <summary>The movable store lying in it now (issue #206): a rock pushed in becomes this bin's store. Not saved: it is found again from where the body lies.</summary>
    [field: NonSerialized] public HeatStore? Occupant { get; set; }
    /// <summary>The store it holds now, built in or put in, or null while it is empty.</summary>
    public HeatStore? Store => Fixed ?? Occupant;
    /// <summary>Where the middle of its floor stands, m, and the side of its square cavity: a movable store whose middle is inside it, standing on the floor, has been put in (<see cref="Admits"/>).</summary>
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double Inner { get; init; } = 0.3;
    /// <summary>Taller than this above the floor, m, is not in it: a load that stands on the floor of its cavity (the rover cannot lift one over the lid).</summary>
    public const double Height = 0.35;
    /// <summary>Whether only the movable store of this name goes in it (the machine said <c>#:holds</c> of one), or null for any.</summary>
    public string? Only { get; init; }

    /// <summary>Whether a movable store lying at its position is inside this bin's cavity (middle within its footprint, base on its floor).</summary>
    public bool Admits(HeatStore s) =>
        (Only is null || Only == s.Name) && Fixed is null
        && Math.Abs(s.X - X) <= Inner / 2 && Math.Abs(s.Z - Z) <= Inner / 2
        && s.Y - Y is >= -0.05 and <= Height - 0.05;
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
