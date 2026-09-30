namespace HeroicInventions.Sim.Machines;

/// <summary>
/// A field that follows a mechanism at run time (issue #46). Whoever owns the
/// lever or rope (the Godot view) reads its angle or tension and hands it to
/// <see cref="MachineRuntime.ApplyFollow"/>, which maps it onto the field.
/// </summary>
public sealed class Follow(FollowSpec spec)
{
    public FollowSpec Spec { get; } = spec;
    /// <summary>The mechanism's reading last tick: degrees turned, or newtons of pull.</summary>
    public double Input;
    /// <summary>The value last given to the field.</summary>
    public double Value = double.NaN;

    /// <summary>The field's value for an input: linear from Low at From to High at To, held at the ends.</summary>
    public double Map(double input)
    {
        double span = Spec.To - Spec.From;
        double t = span == 0 ? (input >= Spec.To ? 1 : 0) : Math.Clamp((input - Spec.From) / span, 0, 1);
        return Spec.Low + t * (Spec.High - Spec.Low);
    }
}
