namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// Something turning on an axle that a shaft can be coupled to (issue #78):
/// a windmill's windshaft, a water wheel's axle, a jet wheel's spindle, or,
/// in the game, a Jolt body on a hinge. A shaft joins two of them by
/// trading angular momentum: whatever one gains the other loses, so the
/// torque is the same on both sides.
/// </summary>
public interface IShaft
{
    /// <summary>rad/s about the axle, positive the way the part turns forward.</summary>
    double AngularVelocity { get; }
    /// <summary>kg·m² about the axle.</summary>
    double ShaftInertia { get; }
    /// <summary>Adds an angular impulse (N·m·s) about the axle: its speed changes by impulse / inertia.</summary>
    void AddAngularImpulse(double impulse);
}
