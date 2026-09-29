namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A rope wrapped <see cref="Turns"/> times round a fixed post — a bollard, a
/// snubbing post, the barrel of a capstan held still — with a load hanging
/// from one end and someone pulling on the other with <see cref="Hold"/>
/// newtons. Where the rope slides on the post, every bit of wrap takes a
/// little tension off by friction, in proportion to the tension there, so it
/// falls off exponentially round the post (Euler, 1762; Eytelwein, 1808):
///
///   T_tight = T_slack · e^(μθ),   θ = 2π × turns.
///
/// So the load stays put for any pull between m·g·e^(−μθ) and m·g·e^(μθ):
/// a few turns let a hand hold a ship. Pull less and the load runs out,
/// its end the tight one, T_load = Hold·e^(μθ); pull more and it comes in,
/// the hauler's end tight, T_load = Hold·e^(−μθ). Sliding friction is taken
/// equal to sticking friction, as the capstan equation does.
/// </summary>
public sealed class Capstan(string name, double turns, double mu, double loadMass, double startHeight, double postHeight)
{
    public string Name { get; } = name;
    public double Turns { get; } = turns;
    public double Mu { get; } = mu;
    public double LoadMass { get; set; } = loadMass;         // kg
    public double Hold { get; set; }                         // N the hauler pulls with

    public double Height { get; private set; } = startHeight; // m, the load above the ground
    public double PostHeight { get; } = postHeight;          // m: a load hauled right up stops against the post
    public double Velocity { get; private set; }             // m/s, + up
    public double LoadTension { get; private set; }          // N in the rope at the load
    public bool Grounded => Height <= 0 && Velocity <= 0;
    public bool Held => !Grounded && Velocity == 0;
    public double Lowered { get; private set; }              // m run out, all told
    public double Hauled { get; private set; }               // m brought in, all told

    public double WrapAngle => 2 * Math.PI * Turns;
    /// <summary>e^(μθ): how many times the pull the wrap can hold, or needs to haul.</summary>
    public double Ratio => Math.Exp(Mu * WrapAngle);
    public double Weight => LoadMass * Physics.Gravity;
    /// <summary>The least pull that keeps the load from running out, m·g·e^(−μθ).</summary>
    public double LeastHold => Weight / Ratio;
    /// <summary>The pull it takes to haul the load in, m·g·e^(μθ).</summary>
    public double HaulingPull => Weight * Ratio;

    public void Step(double dt)
    {
        double hold = Math.Max(0, Hold), ratio = Ratio;
        if (Velocity == 0)
        {
            if (Height <= 0)
            {
                // resting on the ground: the rope needs only e^(−μθ) of the pull at the load
                LoadTension = hold / ratio;
                if (LoadTension <= Weight) return;
            }
            else if (hold >= Weight / ratio && (hold <= Weight * ratio || Height >= PostHeight)) { LoadTension = Weight; return; }
        }

        // sliding: whichever end is being pulled round the post is the tight one
        bool rising = Velocity > 0 || (Velocity == 0 && hold / ratio > Weight);
        LoadTension = rising ? hold / ratio : hold * ratio;
        double a = LoadTension / LoadMass - Physics.Gravity;
        double v = Velocity + a * dt;
        if (rising ? v <= 0 : v >= 0)
        {
            // friction has stopped it within the step
            double t = -Velocity / a;
            Move(Velocity * t + 0.5 * a * t * t);
            Velocity = 0;
            return;
        }
        Move(Velocity * dt + 0.5 * a * dt * dt);
        Velocity = v;
        if ((Height <= 0 && Velocity < 0) || (Height >= PostHeight && Velocity > 0)) Velocity = 0;
    }

    private void Move(double dy)
    {
        if (dy < 0) Lowered += Math.Min(-dy, Height); else Hauled += Math.Min(dy, PostHeight - Height);
        Height = Math.Clamp(Height + dy, 0, PostHeight);
    }
}
