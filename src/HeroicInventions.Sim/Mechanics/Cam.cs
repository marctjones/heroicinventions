namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A peg wheel or cam and the follower it works (issue #48): n pegs round a wheel, each lifting a
/// follower of mass m by h as the wheel turns, then letting it go to fall back on its anvil. The
/// follower is followed analytically — a lift that is a function of the wheel's angle, not a mesh
/// collision — and the wheel is loaded by the follower's weight through virtual work,
/// τ = F·dy/dθ, so the work the wheel does each turn is n·m·g·h.
///
/// One peg lifts the follower over <see cref="Rise"/> of its pitch (2π/n of the wheel), on a cosine
/// (y = h·(1 − cos(π·φ/α))/2 for φ turned into the peg, α the rise's angle): slope zero at both ends,
/// steepest half way, where dy/dθ = π·h/(2α). At the top the peg lets go and the follower falls freely
/// at g, striking the anvil (or the next peg, if it comes first) at √(2·g·h) after a drop of h.
/// </summary>
public sealed class Cam
{
    public Cam(string id, int pegs, double lift, double rise, double mass, double gravity)
    {
        if (pegs < 1) throw new ArgumentOutOfRangeException(nameof(pegs));
        Id = id; Pegs = pegs; Lift = lift; Rise = Math.Clamp(rise, 0.05, 1); Mass = mass; Gravity = gravity;
    }

    public string Id { get; }
    public int Pegs { get; }
    /// <summary>How far one peg lifts the follower, m.</summary>
    public double Lift { get; }
    /// <summary>The share of a peg's pitch over which it lifts, 0..1 (a gentle cam is near 1, a sharp one small).</summary>
    public double Rise { get; }
    /// <summary>The follower's mass, kg.</summary>
    public double Mass { get; set; }
    public double Gravity { get; set; }

    /// <summary>The angle between one peg and the next, rad.</summary>
    public double Pitch => 2 * Math.PI / Pegs;
    /// <summary>The angle the wheel turns while a peg lifts, rad.</summary>
    public double RiseAngle => Rise * Pitch;

    /// <summary>The height, m, of the cam's surface under the follower when the wheel has turned θ: the follower can be no lower.</summary>
    public double Profile(double theta)
    {
        double phi = ((theta % Pitch) + Pitch) % Pitch;
        return phi >= RiseAngle ? 0 : Lift * (1 - Math.Cos(Math.PI * phi / RiseAngle)) / 2;
    }

    /// <summary>The slope of that surface, m per rad of the wheel: what turns the follower's weight into a torque.</summary>
    public double Slope(double theta)
    {
        double phi = ((theta % Pitch) + Pitch) % Pitch;
        return phi >= RiseAngle ? 0 : Lift * Math.PI / (2 * RiseAngle) * Math.Sin(Math.PI * phi / RiseAngle);
    }

    /// <summary>The steepest slope of the profile, m/rad: π·h/(2α). The wheel needs m·g times this, in N·m, to lift the follower there.</summary>
    public double SteepestSlope => Lift * Math.PI / (2 * RiseAngle);

    // ---- state
    /// <summary>The follower's height above its anvil, m.</summary>
    public double Height { get; private set; }
    public double Velocity { get; private set; }
    /// <summary>Whether the cam is holding the follower up (it rests on the cam's surface) rather than in free fall.</summary>
    public bool InContact { get; private set; } = true;
    /// <summary>The torque, N·m, the follower puts on the wheel against its turning; 0 when it is falling.</summary>
    public double Torque { get; private set; }
    /// <summary>Times it has struck the anvil.</summary>
    public int Strikes { get; private set; }
    /// <summary>Speed, m/s, of the last strike.</summary>
    public double LastStrikeSpeed { get; private set; }
    /// <summary>Speed of the fastest strike so far.</summary>
    public double FastestStrike { get; private set; }
    /// <summary>Net work the wheel has done lifting the follower, J.</summary>
    public double Work { get; private set; }

    /// <summary>
    /// The same follower worked together with a real wheel that has moment of inertia I (kg·m²) and is turning at ω (rad/s)
    /// with the follower resting on it at angle θ. While the follower rides the cam it is tied to the wheel by
    /// ẏ = Y'(θ)·ω, so wheel and follower are solved as one step at the velocity level:
    /// (I + m·Y'²)·ω' = I·ω + m·Y'·(ẏ − g·dt). That puts the follower's weight on the wheel (through Y'), the
    /// follower's own inertia (m·Y'², which slows the wheel to lift it), and cannot go unstable however the
    /// wheel jitters. Returns the angular impulse, N·m·s, to give the wheel this step: negative when the
    /// follower is loading it. The push of the cam on the follower must stay above zero: below that it has left the cam.
    /// </summary>
    public double StepCoupled(double dt, double theta, double omega, double inertia)
    {
        double surface = Profile(theta);
        double previousHeight = Height;
        bool landed = false;
        if (InContact)
        {
            // riding, unless the surface falls away faster than gravity could take it (a peg letting go)
            double freeFall = Height + (Velocity - Gravity * dt) * dt;
            if (surface < freeFall - 1e-12) { InContact = false; Velocity = 0; }
        }
        if (!InContact)
        {
            Velocity -= Gravity * dt;
            Height += Velocity * dt;
            if (Height <= surface)
            {
                if (Velocity < -1e-6)
                {
                    Strikes++;
                    LastStrikeSpeed = -Velocity;
                    FastestStrike = Math.Max(FastestStrike, LastStrikeSpeed);
                }
                Height = surface;
                Velocity = 0;
                InContact = true;
                landed = true;
            }
        }
        double impulse = 0;
        Torque = 0;
        if (InContact && !landed && dt > 0)
        {
            double slope = Slope(theta);
            double omegaNew = (inertia * omega + Mass * slope * (Velocity - Gravity * dt)) / (inertia + Mass * slope * slope);
            double push = Mass * (slope * omegaNew - Velocity) + Mass * Gravity * dt;      // impulse of cam on follower, N·s
            if (push < 0 && slope * omega * dt > 0) { InContact = false; }                 // it would have to be pulled: it has left the cam
            else
            {
                impulse = inertia * (omegaNew - omega);
                Velocity = slope * omegaNew;
                Height = surface;
                Torque = -impulse / dt;
                Work += push / dt * (Height - previousHeight);
            }
        }
        else if (InContact) Height = surface;
        _lastTheta = theta;
        _started = true;
        return impulse;
    }

    private double _lastTheta;
    private bool _started;

    /// <summary>
    /// Advances the follower by dt with the wheel now at θ (total angle turned, rad, growing as it turns forward).
    /// Returns the torque, N·m, the follower puts on the wheel: apply it against the wheel's turning.
    /// </summary>
    public double Step(double dt, double theta)
    {
        if (!_started) { _lastTheta = theta; _started = true; }
        double dTheta = theta - _lastTheta;
        _lastTheta = theta;
        double surface = Profile(theta);
        double previousVelocity = Velocity, previousHeight = Height;

        if (InContact)
        {
            // It rides on the surface unless the surface falls away faster than gravity could take it (a peg letting go),
            // which is when the push of the cam on it would have to be negative. A wheel that wobbles back down a smooth
            // ramp keeps the follower on it.
            double freeFall = Height + (Velocity - Gravity * dt) * dt;
            if (surface >= freeFall - 1e-12)
            {
                Velocity = dt > 0 ? (surface - Height) / dt : 0;
                Height = surface;
            }
            else
            {
                InContact = false;
                Velocity = 0;
            }
        }
        bool landed = false;
        if (!InContact)
        {
            // free fall, onto the cam's surface or the anvil, whichever it meets
            Velocity -= Gravity * dt;
            Height += Velocity * dt;
            if (Height <= surface)
            {
                if (Velocity < -1e-6)
                {
                    Strikes++;
                    LastStrikeSpeed = -Velocity;
                    FastestStrike = Math.Max(FastestStrike, LastStrikeSpeed);
                }
                Height = surface;
                Velocity = 0;
                InContact = true;
                landed = true;
            }
        }

        // What the cam has to put up while it holds the follower: its weight and what it accelerates it by. The torque
        // that asks of the wheel is that force times how far the follower rose for the angle turned this step (dy/dθ, the
        // step's own, so the work F·Δy adds up exactly); a wheel held still is asked for m·g times the profile's slope there.
        // The acceleration is centred, from the speed a step back and the speed a step on (the wheel taken to keep its
        // speed): a one-sided difference times Δy would count ½·m·Σ(Δv)² too much or too little over a lift.
        if (InContact && !landed && dt > 0)
        {
            double next = dTheta > 1e-9 ? Math.Max(0, (Profile(theta + dTheta) - Height) / dt) : 0;
            double accel = (next - previousVelocity) / (2 * dt);
            double force = Math.Max(0, Mass * (Gravity + accel));
            double dy = Height - previousHeight;
            double slope = dTheta > 1e-9 ? dy / dTheta : Slope(theta);
            Torque = force * slope;
            Work += force * dy;                       // net: what a wheel wobbling back down a ramp is given back comes off
        }
        else Torque = 0;
        return Torque;
    }
}
