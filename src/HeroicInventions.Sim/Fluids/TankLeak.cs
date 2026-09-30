namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// A hole in a tank's wall, <see cref="Height"/> above its floor, draining
/// as an orifice: Torricelli's Q = Cd·A·√(2g·h), h the water standing above
/// the hole. It stops when the level falls to the hole, so what lies below
/// a low hole is never lost. The jet falls to the ground, or into
/// <see cref="Catch"/> if a tank stands under it (which then limits it when
/// full). A seep or evaporation, <see cref="Evaporation"/> m³/s off the
/// surface, is taken from the top and never stops while any water is left.
///
/// Level draws down as √(h − hole) falling linearly at Cd·A·√(2g)/(2·A_tank),
/// so it empties to the hole in T = (A_tank/(Cd·A))·√(2(H − hole)/g).
/// </summary>
public sealed class TankLeak(Tank tank, double height, double area, Tank? catchTank = null)
{
    public const double DischargeCoefficient = 0.6;

    public Tank Tank { get; } = tank;
    public double Height { get; } = height;               // m above the tank's floor
    private double _area = Math.Max(0, area);
    public double Area { get => _area; set => _area = Math.Max(0, value); } // m² of the hole; 0 is plugged
    public double Cd { get; init; } = DischargeCoefficient;

    /// <summary>
    /// The bore (m across) of a plugged hole; 0 for a plain hole with a fixed <see cref="Area"/>. A bored hole is
    /// closed by a plug that lifts by <see cref="Lift"/>; the water passes through the curtain the lifted plug
    /// leaves, π·d·lift, until the lift reaches a quarter of the bore, where that equals the bore's own
    /// area π·d²/4 and lifting further opens nothing more.
    /// </summary>
    public double Bore { get; init; }

    private double _lift;
    /// <summary>How far the plug is lifted off its seat, m. Setting it sets <see cref="Area"/> (bored holes only).</summary>
    public double Lift
    {
        get => _lift;
        set { _lift = Math.Max(0, value); if (Bore > 0) _area = PlugArea(Bore, _lift); }
    }

    /// <summary>Open area, m², of a hole of bore d with its plug lifted by lift: π·d·lift, up to the full π·d²/4.</summary>
    public static double PlugArea(double bore, double lift) =>
        lift >= bore / 4 ? Math.PI * bore * bore / 4 : Math.PI * bore * Math.Max(0, lift);
    public Tank? Catch { get; } = catchTank;
    public double Evaporation { get; init; }              // m³/s

    public double Flow { get; internal set; }             // m³/s out of the hole, last step
    public double Lost { get; internal set; }             // m³ leaked out of the hole so far
    public double Evaporated { get; internal set; }       // m³ taken from the surface so far

    public double HoleElevation => Tank.BaseElevation + Height;

    /// <summary>Metres of water (plus any gas pressure) above the hole; none once the surface is down to it.</summary>
    public double Head => Tank.IsSubmerged(HoleElevation) ? Math.Max(0, Tank.HeadAt(HoleElevation) - HoleElevation) : 0;

    public double Discharge() => Cd * Area * Math.Sqrt(2 * Tank.Zone.Gravity * Head);
}
