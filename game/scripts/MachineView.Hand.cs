using Godot;

namespace HeroicInventions;

/// <summary>
/// What a hand does to a body (#159): pulls the point it grabbed toward a target through a stiff, damped spring.
/// The spring is solved here, once a tick before the step, like the ropes, so the body keeps its own mass and
/// inertia, collides with everything, and swings on whatever it is hinged to. Main.Drag.cs owns when a hand
/// takes hold and where it goes; this only turns a target into a force.
/// </summary>
public sealed class HandSpring
{
    public required RigidBody3D Body;
    public required Vector3 GrabLocal;      // the grabbed point, in the body's frame
    public Vector3 Target;                  // where the hand wants it, in the world
    public Vector3 TargetVelocity;          // how fast that is moving, so a steady push doesn't lag behind the hand
    public float Force;                     // last tick's pull, N
    public Vector3 GrabWorld => Body.GlobalTransform * GrabLocal;
}

public partial class MachineView
{
    /// <summary>The hand holding something in this machine, if there is one.</summary>
    public HandSpring? Hand;

    /// <summary>Natural frequency of the hand's spring, rad/s: stiff enough to follow, soft enough to be stable at 120 Hz (ω·dt = 0.17).</summary>
    public const float HandOmega = 20f;
    /// <summary>The spring never reaches further than this, so a cursor behind a wall pulls with a bounded force instead of an exploding one.</summary>
    public const float HandMaxStretch = 0.5f;

    /// <summary>
    /// The pull on the grabbed point: F = m_eff (ω² e − 2ω (v − v_target)), e the error, m_eff the mass that point
    /// answers with along it (the same effective mass the ropes use: a pendulum's tip answers with I/L², a free
    /// body's with its mass). Critically damped, so a release at rest leaves it at rest.
    /// </summary>
    private void ApplyHand()
    {
        if (Hand is not { } hand) return;
        var body = hand.Body;
        if (!IsInstanceValid(body) || body.Freeze) { hand.Force = 0; return; }
        body.Sleeping = false;
        var at = hand.GrabWorld;
        var error = hand.Target - at;
        if (error.Length() > HandMaxStretch) error = error.Normalized() * HandMaxStretch;
        var relative = PointVelocity(body, at) - hand.TargetVelocity;
        var along = error.LengthSquared() > 1e-8f ? error.Normalized() : relative.LengthSquared() > 1e-8f ? relative.Normalized() : Vector3.Up;
        float w = InverseMassAlong(body, at, along);
        float mass = w > 1e-6f ? Mathf.Min(1 / w, 10 * body.Mass) : body.Mass;
        var force = mass * (HandOmega * HandOmega * error - 2 * HandOmega * relative);
        hand.Force = force.Length();
        body.ApplyForce(force, at - body.GlobalPosition);
    }
}
