namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A compound pendulum on a <see cref="Bearing"/>: an iron rod of
/// <see cref="RodRadius"/> with a ball on its end, the same shape the game
/// draws. About the pivot I = m_rod·L²/3 + m_bob·(L² + ⅖·r_bob²), and gravity
/// turns it with m·g·d·sin θ, d the centre of mass's distance below the pivot:
///
///   I·θ'' = −m·g·d·sin θ − friction
///
/// The bearing carries the pendulum's weight, N = m·g (the extra load from
/// its swing, m·d·ω², is left out). Each turning point of the swing is kept
/// as <see cref="TurnedAt"/>, so the run-down can be read off swing by swing.
/// </summary>
public sealed class Pendulum
{
    public const double RodRadius = 0.01;                     // m
    public static double BobRadiusFor(double length) => Math.Max(0.03, length * 0.08);

    private const double MaxSubstep = 0.0005;                 // s

    public string Name { get; }
    public double Length { get; }                             // m, pivot to the ball's centre
    public Bearing Bearing { get; }
    public double Mass { get; }                               // kg
    public double MomentOfInertia { get; }                    // kg·m² about the pivot
    public double CentreOfMass { get; }                       // m below the pivot

    public double Angle { get; private set; }                 // rad from hanging, + toward +x
    public double AngularVelocity { get; private set; }       // rad/s
    public double TurnedAt { get; private set; }              // rad, θ at the last turning point
    public double Amplitude => Math.Abs(TurnedAt);
    public double PeakTime { get; private set; }              // s, when it turned there
    public int Swings { get; private set; }                   // turning points since release
    public double Time { get; private set; }

    private int _sense;                                        // sign of the last motion

    public Pendulum(string name, double length, double density, double startAngle, Bearing bearing)
    {
        Name = name;
        Length = length;
        Bearing = bearing;
        double bob = BobRadiusFor(length);
        double rodMass = density * Math.PI * RodRadius * RodRadius * length;
        double bobMass = density * 4.0 / 3.0 * Math.PI * bob * bob * bob;
        Mass = rodMass + bobMass;
        MomentOfInertia = rodMass * length * length / 3 + bobMass * (length * length + 0.4 * bob * bob);
        CentreOfMass = (rodMass * length / 2 + bobMass * length) / Mass;
        Angle = TurnedAt = startAngle;
    }

    public double Weight => Mass * Physics.Gravity;
    public double GravityTorque => -Weight * CentreOfMass * Math.Sin(Angle);
    /// <summary>At rest, and gravity can't turn it past the bearing's grip.</summary>
    public bool Stopped => AngularVelocity == 0 && Math.Abs(GravityTorque) <= Bearing.CoulombTorque(Weight);
    /// <summary>Swing energy above hanging at rest, J.</summary>
    public double Energy => 0.5 * MomentOfInertia * AngularVelocity * AngularVelocity
                            + Weight * CentreOfMass * (1 - Math.Cos(Angle));
    public double KineticEnergy => 0.5 * MomentOfInertia * AngularVelocity * AngularVelocity;

    public void Step(double dt)
    {
        int n = Math.Max(1, (int)Math.Ceiling(dt / MaxSubstep - 1e-9));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            Time += h;
            // Held by the pin: gravity does no work, so there is nothing for friction to turn into heat.
            if (Stopped) continue;
            double w = AngularVelocity + GravityTorque / MomentOfInertia * h;
            AngularVelocity = Bearing.Slow(w, MomentOfInertia, Weight, h);
            Angle += AngularVelocity * h;
            int sense = Math.Sign(AngularVelocity);
            if (sense == _sense) continue;
            if (_sense != 0)
            {
                TurnedAt = Angle;
                PeakTime = Time;
                Swings++;
            }
            _sense = sense;
        }
    }
}
