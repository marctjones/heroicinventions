namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A plain bearing: a journal (pin) of <see cref="JournalRadius"/> turning in
/// its eye, carrying a load N. Two kinds of friction slow it:
///
///   Coulomb   τ = μ·N·r, whatever the speed — dry metal on metal. It holds a
///             part still (stiction) until something turns it harder than τ.
///   viscous   τ = c·ω — a greased or oiled pin, dragging in proportion to speed.
///
/// Whatever energy friction takes becomes <see cref="Heat"/>. The pin wears
/// by Archard's law, V = K·N·s: s the distance its surface slides, r·∫|ω|dt,
/// and K the specific wear rate (mm³ per N·m) of the pair — about 1e-4 for
/// dry iron on iron, 1e-7 and less when lubricated.
/// </summary>
public sealed class Bearing(double journalRadius)
{
    public double JournalRadius { get; } = journalRadius;  // m
    public double Mu { get; set; }                          // Coulomb coefficient
    public double Drag { get; set; }                        // N·m·s/rad
    public double WearRate { get; init; }                   // mm³/(N·m)

    public double Heat { get; private set; }                // J
    public double Sliding { get; private set; }             // m the pin's surface has slid
    public double Wear { get; private set; }                // mm³ worn away

    public double CoulombTorque(double load) => Mu * load * JournalRadius;

    /// <summary>
    /// A body of <paramref name="inertia"/> turning at <paramref name="omega"/>
    /// (after everything else has pushed it this step) on this bearing under
    /// <paramref name="load"/> N: how fast it turns after friction's share of
    /// the step. Coulomb friction can bring it to rest, never reverse it.
    /// </summary>
    public double Slow(double omega, double inertia, double load, double dt)
    {
        double w = omega * Math.Exp(-Drag * dt / inertia);
        double stop = CoulombTorque(load) / inertia * dt;
        w = Math.Abs(w) <= stop ? 0 : w - Math.Sign(w) * stop;
        Heat += 0.5 * inertia * (omega * omega - w * w);
        double slid = JournalRadius * Math.Abs(omega + w) / 2 * dt;
        Sliding += slid;
        Wear += WearRate * load * slid;
        return w;
    }
}
