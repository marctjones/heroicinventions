namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// What turns a driven hinge, as a person could change it (issue #154): the stand-in for men in a
/// treadwheel or a hand on a crank. The sim core has no hinges (the game's physics engine owns
/// them), so this is only the settings: <see cref="Rpm"/> the speed asked for (signed; 0 holds the
/// wheel still, as men standing in it do), <see cref="Torque"/> the most they give (0 lets go, and
/// the wheel turns freely under whatever pulls on it), <see cref="Grind"/> the millstone's resisting
/// torque if it has one. The view reads them each tick and sets the hinge's motor to match.
/// </summary>
public sealed class HingeDrive(double rpm, double torque, double grind)
{
    /// <summary>The "no limit" torque a blueprint without #:drive-torque gets.</summary>
    public const double Unlimited = 1e8;

    /// <summary>Signed turns per minute asked of the wheel; positive is a positive turn about its axle.</summary>
    public double Rpm { get; set; } = rpm;
    /// <summary>Most torque the drive gives, N·m; 0 lets the wheel go.</summary>
    public double Torque { get; set; } = torque;
    /// <summary>The torque a millstone's stones resist the turn with, N·m; 0 when the wheel grinds nothing.</summary>
    public double Grind { get; set; } = grind;
    /// <summary>True if the blueprint drives this wheel (a motor to set); a millstone alone has only a grind.</summary>
    public bool Driven { get; init; }
}
