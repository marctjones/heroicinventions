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
    public Tank? Catch { get; } = catchTank;
    public double Evaporation { get; init; }              // m³/s

    public double Flow { get; internal set; }             // m³/s out of the hole, last step
    public double Lost { get; internal set; }             // m³ leaked out of the hole so far
    public double Evaporated { get; internal set; }       // m³ taken from the surface so far

    public double HoleElevation => Tank.BaseElevation + Height;

    /// <summary>Metres of water (plus any gas pressure) above the hole; none once the surface is down to it.</summary>
    public double Head => Tank.IsSubmerged(HoleElevation) ? Math.Max(0, Tank.HeadAt(HoleElevation) - HoleElevation) : 0;

    public double Discharge() => Cd * Area * Math.Sqrt(2 * Physics.Gravity * Head);
}
