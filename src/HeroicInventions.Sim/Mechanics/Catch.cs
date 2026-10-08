namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// Something that holds a machine until it is let go (issue #155): a catch on a lever's hinge (a trebuchet's
/// trigger, an onager's slip-hook), the pawl on a ratchet, or a tether rope. Its state is one command, held
/// (1) or let go (0), given through a field; until demo operators exist (#153) it may also let go by itself
/// at <see cref="ReleaseAt"/> seconds, so a machine opened from the menu still throws. Any command cancels
/// that timer: a person who sets it decides from then on.
/// The view owns the joint, pawl or rope that does the holding; it reads <see cref="HeldAt"/> every tick and
/// writes back the load it carried.
/// </summary>
public sealed class Catch(string id, string kind, double releaseAt, int side = 0)
{
    public string Id { get; } = id;
    /// <summary>"catch", "pawl" or "tether": the field it answers to.</summary>
    public string Kind { get; } = kind;
    /// <summary>
    /// Which way a lever's catch stops the arm (#161): 0 both ways, pinned at its angle; +1 only from turning past it
    /// toward larger angles, −1 toward smaller, while the arm turns freely the other way, as a ratchet's pawl lets a
    /// windlass wind an arm back past the catch and then holds it there (#:catch-side).
    /// </summary>
    public int Side { get; } = Math.Sign(side);
    /// <summary>The last command: held, or let go.</summary>
    public bool Set = true;
    /// <summary>Seconds into the run it lets go by itself; +∞ for never.</summary>
    public double ReleaseAt = releaseAt;
    /// <summary>What it carries now (N·m for a catch, N for a tether), averaged over the last 20 ticks or so; the view keeps it.</summary>
    public double Load;
    /// <summary>The most it has carried.</summary>
    public double PeakLoad;

    /// <summary>Whether it holds at <paramref name="time"/> s.</summary>
    public bool HeldAt(double time) => Set && time < ReleaseAt;

    /// <summary>A person's (or a trigger's) command: 1 holds, 0 lets go.</summary>
    public void Command(double value)
    {
        Set = value > 0.5;
        ReleaseAt = double.PositiveInfinity;
    }

    /// <summary>Takes this tick's load into the running average (the joint's push alternates tick to tick, as a pawl's does).</summary>
    public void Carry(double load)
    {
        Load = 0.95 * Load + 0.05 * load;
        PeakLoad = Math.Max(PeakLoad, Math.Abs(Load));
    }
}
