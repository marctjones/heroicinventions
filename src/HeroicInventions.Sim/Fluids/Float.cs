namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// A float riding on a tank's water (issue #29): a flat-bottomed body of
/// <see cref="Mass"/> kg and plan <see cref="Area"/> m². Afloat, it sinks
/// until the water it pushes aside weighs what it does (Archimedes): its
/// draft is m / (ρ A), its bottom that far below the surface. Water too
/// shallow for that and it sits on the tank's floor (grounded), lifting off
/// again once the water is deep enough. A float heavier than the water its
/// whole <see cref="Height"/> can displace never floats. The water it
/// displaces is taken as small beside the tank's, so the tank's level is
/// not raised by it.
/// </summary>
public sealed class Float(string name, Tank tank, double mass, double area, double height)
{
    public string Name { get; } = name;
    public Tank Tank { get; } = tank;
    public double Mass { get; } = mass;
    public double Area { get; } = area;
    public double Height { get; } = height;
    public double WaterDensity { get; set; } = Physics.WaterDensity;

    /// <summary>m it sinks to afloat: m / (ρ A).</summary>
    public double Draft => Mass / (WaterDensity * Area);
    /// <summary>Whether it can float at all: its draft is within its own height.</summary>
    public bool Floats => Draft < Height;
    /// <summary>m, the elevation of its bottom.</summary>
    public double Bottom => Grounded ? Tank.BaseElevation : Tank.SurfaceElevation - Draft;
    /// <summary>On the tank's floor: the water is shallower than its draft (or it can't float).</summary>
    public bool Grounded => !Floats || Tank.Level <= Draft;
    /// <summary>m of it under water.</summary>
    public double Submerged => Math.Min(Height, Math.Max(0, Tank.SurfaceElevation - Bottom));
}
