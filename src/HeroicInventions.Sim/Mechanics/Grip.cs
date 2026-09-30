namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A hook, tongs or a claw that picks up a loose body and lets it go on cue (issue #55).
/// While it is closed it takes hold of any loose body within <see cref="Reach"/> of it, and
/// keeps hold while the load stays under its <see cref="Capacity"/>. Tongs squeeze the load
/// from two sides with force N, so friction μ·N on each jaw carries 2·μ·N (a load of mass m
/// needs N ≥ m·g/(2μ), and g is the planet's); a hook carries what it is strong enough for.
/// Opened, or overloaded, it lets go.
/// </summary>
public sealed class Grip(string id, double reach, bool tongs, double force, double strength, double mu)
{
    public string Id { get; } = id;
    /// <summary>How far, m, from the grip's point a body's centre may be for it to be taken hold of.</summary>
    public double Reach { get; set; } = reach;
    /// <summary>Tongs squeeze; a hook hangs.</summary>
    public bool Tongs { get; } = tongs;
    /// <summary>Tongs: the force, N, each jaw presses with.</summary>
    public double Force { get; set; } = force;
    /// <summary>A hook's strength, N: the most it carries before it opens or breaks.</summary>
    public double Strength { get; set; } = strength;
    /// <summary>Friction coefficient of the jaws' material.</summary>
    public double Mu { get; } = mu;

    /// <summary>Whether the grip is shut (or, for a hook, hanging ready). Set it from a lever, a rope, or a trigger.</summary>
    public bool Closed { get; set; }
    /// <summary>Whether it is holding something, and what.</summary>
    public bool Held => HeldBody is not null;
    public string? HeldBody { get; set; }
    /// <summary>Seconds it has held its present load.</summary>
    public double HeldFor { get; set; }
    /// <summary>The force, N, the held load presses with: its mass times how hard gravity and the grip's own motion pull it.</summary>
    public double Load { get; set; }
    /// <summary>True once a load was too much for it and it let go; cleared when it is opened.</summary>
    public bool Overloaded { get; set; }
    /// <summary>The friction coefficient of the body last considered (tongs grip as well as the poorer surface allows).</summary>
    public double LoadMu { get; set; } = mu;

    /// <summary>The load, N, it can hold: tongs 2·μ·N, with μ the lower of the jaws' and the load's friction; a hook its strength.</summary>
    public double Capacity => Tongs ? 2 * Math.Min(Mu, LoadMu) * Force : Strength;

    /// <summary>The load, N, of mass m (kg) at rest on a planet of gravity g, or accelerating at a upward (m/s²): m·(g + a).</summary>
    public static double LoadOf(double mass, double gravity, double upwardAcceleration = 0) => mass * (gravity + upwardAcceleration);

    /// <summary>The grip force, N, tongs of friction μ need to hold mass m at rest under gravity g: m·g / (2μ).</summary>
    public static double ForceToHold(double mass, double gravity, double mu) => mass * gravity / (2 * mu);
}
