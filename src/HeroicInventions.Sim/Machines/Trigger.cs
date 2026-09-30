namespace HeroicInventions.Sim.Machines;

/// <summary>
/// A trigger at run time (issue #32): whether it has fired, and when. It fires
/// once. A body trigger is tested by whoever owns the bodies (the Godot view,
/// through <see cref="MachineRuntime.TestBodyTrigger"/>); a field trigger is
/// tested by <see cref="MachineRuntime.Step"/> itself.
/// </summary>
public sealed class Trigger(TriggerSpec spec)
{
    public TriggerSpec Spec { get; } = spec;
    public bool Fired;
    /// <summary>Machine time (s) at which it fired; -1 until then.</summary>
    public double FiredAt = -1;

    /// <summary>True when a point at (x, y, z) lies inside the trigger's box.</summary>
    public bool Contains(double x, double y, double z)
    {
        if (Spec.At is not { } at || Spec.Size is not { } size) return false;
        return Math.Abs(x - at.X) <= size.X / 2 && Math.Abs(y - at.Y) <= size.Y / 2 && Math.Abs(z - at.Z) <= size.Z / 2;
    }
}
