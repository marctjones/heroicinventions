using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// A flat mirror turned through the day to keep throwing the sun onto a
/// boiler or a sealed vessel (a heliostat; Archimedes' burning mirrors, as
/// the story has it). To send light arriving from the sun towards the target
/// the mirror must face halfway between them, so the beam strikes it at half
/// the angle θ between the two directions and the mirror presents only
/// cos(θ/2) of its area to the sun:
///
///   P = DNI · area · reflectivity · cos(θ/2).
///
/// That cosine loss is why a heliostat on the far side of its target from
/// the sun does best, and one between the target and the sun does worst.
/// The mirror is taken to be good enough to land all it reflects on the
/// target; nothing when the sun is down.
/// </summary>
public sealed class Mirror(Sun sun, Vec3 at, Vec3 target, double area, double reflectivity)
{
    public Sun Sun { get; } = sun;
    public Vec3 At { get; } = at;
    public Vec3 Target { get; } = target;
    public double Area { get; set; } = area;                   // m²
    public double Reflectivity { get; } = reflectivity;        // polished bronze ~0.6, silvered glass ~0.9
    public double Collected { get; private set; }              // J thrown onto the target so far
    private double _dust;
    /// <summary>The share of its light a coat of dust stops (issue #69): 0 clean. Settles in a storm; set it to 0 to clean it.</summary>
    public double Dust { get => _dust; set => _dust = Math.Clamp(value, 0, 1); }

    /// <summary>cos(θ/2): the share of the mirror's area square to the sun while it aims at the target.</summary>
    public double Cosine
    {
        get
        {
            var s = Sun.Direction;
            double tx = Target.X - At.X, ty = Target.Y - At.Y, tz = Target.Z - At.Z;
            double len = Math.Sqrt(tx * tx + ty * ty + tz * tz);
            if (len < 1e-9) return 0;
            double cos = (s.X * tx + s.Y * ty + s.Z * tz) / len;
            return Math.Sqrt(Math.Max(0, (1 + cos) / 2));
        }
    }

    /// <summary>W landing on the target now.</summary>
    public double Power => Sun.DirectNormal * Area * Reflectivity * (1 - Dust) * Cosine;

    public void Step(double dt) => Collected += Power * dt;
}
