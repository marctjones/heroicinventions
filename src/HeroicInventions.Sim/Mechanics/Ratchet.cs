namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A ratchet and pawl (issue #49): a toothed wheel that turns one way only, a tooth at a time. The pawl
/// rests in the valley the wheel has reached, at angle <see cref="Locked"/>; the wheel may turn forward
/// freely, and each time it passes a whole tooth pitch (360°/N) the pawl drops into the next valley. It may
/// turn back only as far as the valley it is in, no farther: there its tooth meets the pawl, which
/// carries whatever torque is trying to turn the wheel back — a hanging load m turning a drum r gives
/// m·g·r, and the pawl at the tooth circle R pushes with m·g·r/R.
/// </summary>
public sealed class Ratchet(string id, int teeth, double toothRadius, bool reverse)
{
    public string Id { get; } = id;
    public int Teeth { get; } = teeth;
    /// <summary>Radius, m, of the circle the teeth stand on: where the pawl pushes.</summary>
    public double ToothRadius { get; } = toothRadius;
    /// <summary>False: the wheel may turn forward (positive about its axis). True: the other way.</summary>
    public bool Reverse { get; } = reverse;

    /// <summary>The angle between teeth, rad: 2π/N.</summary>
    public double Pitch => 2 * Math.PI / Teeth;
    private double Sign => Reverse ? -1 : 1;

    /// <summary>The angle, rad from the start, the wheel can turn back to: the valley the pawl is in (in the allowed direction's sense).</summary>
    public double Locked { get; private set; }
    /// <summary>Teeth the wheel has advanced.</summary>
    public int Steps { get; private set; }
    /// <summary>Where the wheel is, in the allowed direction's sense, rad.</summary>
    public double Angle { get; private set; }
    /// <summary>The torque, N·m, the pawl put on the wheel this tick (0 while the wheel is clear of it).</summary>
    public double Torque { get; private set; }
    /// <summary>The force, N, it pushes with at the tooth circle: torque / R, averaged over the last 20 ticks or so.</summary>
    public double Force { get; private set; }
    /// <summary>The most force it has pushed with.</summary>
    public double PeakForce { get; private set; }
    /// <summary>Whether the pawl is carrying a load: against a tooth, over the last 20 ticks or so.</summary>
    public bool Holding => Force > 0.5;
    /// <summary>Times the pawl has dropped over a tooth into the next valley (a click).</summary>
    public int Clicks => Steps;

    /// <summary>
    /// Works the pawl for one tick with the wheel at angle θ (rad turned from the start, positive about its axis),
    /// turning at ω (rad/s) with inertia I (kg·m²) about its axle. Returns the angular impulse, N·m·s, to give
    /// the wheel (about its axis): zero unless a tooth is against the pawl and the wheel is trying to go back.
    /// </summary>
    public double Step(double dt, double theta, double omega, double inertia)
    {
        double a = Sign * theta, w = Sign * omega;                    // in the allowed direction's sense
        Angle = a;
        while (a >= Locked + Pitch) { Locked += Pitch; Steps++; }      // the pawl drops into the next valley
        double impulse = 0;
        if (a <= Locked && w < 0.05 && dt > 0)
        {
            // a tooth against the pawl: stop the wheel going back, and push it gently off the tooth if it has got into it
            double target = a < Locked ? Math.Min(1.0 * (Locked - a) / dt, 3.0) : 0;
            double j = inertia * (target - w);
            if (j > 0) impulse = j;
        }
        Torque = dt > 0 ? impulse / dt : 0;
        // a moving average over the last 20 ticks or so, so it reads the mean load however the pawl's impulses alternate tick to tick
        Force = 0.95 * Force + 0.05 * (Torque / ToothRadius);
        if (Force < 1e-9) Force = 0;
        PeakForce = Math.Max(PeakForce, Force);
        return Sign * impulse;
    }
}
