namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// A float valve, as Ctesibius and Philo built one for the constant-head
/// vessel of a water clock: a float riding on a tank's water carries a
/// conical plug up into the mouth of the pipe that feeds the tank. As the
/// water rises the plug closes the mouth; as it falls the plug drops away
/// and lets the feed in again.
///
/// The gap round the plug opens in proportion to how far the float has
/// dropped, so the feed passes Opening × what it would unchecked, with
/// Opening = (Shut − level) / Travel between 0 and 1: shut once the level
/// reaches <see cref="ShutLevel"/> above the tank's floor, wide open once
/// it has fallen <see cref="Travel"/> below it. Whatever the tank is
/// drawing, the level settles where the feed matches it,
/// h = Shut − Travel·Q_out / Q_feed, which is always inside that band as
/// long as the open feed can outrun the draw.
/// </summary>
public sealed class FloatValve
{
    public FloatValve(Tank tank, double shutLevel, double travel)
    {
        if (travel <= 0) throw new ArgumentOutOfRangeException(nameof(travel), "a float valve needs some travel");
        Tank = tank;
        ShutLevel = shutLevel;
        Travel = travel;
    }

    public Tank Tank { get; }                 // the tank the float rides in, which the feed fills
    public double ShutLevel { get; set; }     // m above the tank's floor where the plug seats
    public double Travel { get; }             // m the level falls below that to open it fully

    /// <summary>How far open, 0 seated .. 1 fully clear, from where the water stands now.</summary>
    public double Opening => Math.Clamp((ShutLevel - Tank.Level) / Travel, 0, 1);
}
