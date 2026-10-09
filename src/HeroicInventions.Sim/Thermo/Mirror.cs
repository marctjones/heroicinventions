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
    /// <summary>The point it is aimed at. A person may aim it elsewhere (#162); the heat goes only to what stands at <see cref="Receiver"/>.</summary>
    public Vec3 Target { get; set; } = target;
    /// <summary>Where the receiver it was built to light stands now (a movable heat store carries it, issue #206).</summary>
    public Vec3 Receiver { get; set; } = target;
    /// <summary>How far from <see cref="Receiver"/>'s centre the aim may fall and still light it, m.</summary>
    public double ReceiverRadius { get; init; } = 0.5;
    /// <summary>
    /// Whether the plate follows the sun (the default, a heliostat). Off, it stays at the face it had when tracking stopped
    /// (<see cref="FixedNormal"/>) and the sun walks the reflected spot away from the aim.
    /// </summary>
    public bool Track { get; private set; } = true;
    public Vec3? FixedNormal { get; private set; }

    public void SetTracking(bool on)
    {
        if (on == Track) return;
        if (!on) FixedNormal = IdealNormal();
        Track = on;
    }

    /// <summary>The unit normal that sends the sun's light to the target: halfway between the sun and the target (the sun itself for a burning mirror).</summary>
    public Vec3 IdealNormal()
    {
        var s = Sun.Direction;
        if (Focusing) return new Vec3(s.X, s.Y, s.Z);
        double tx = Target.X - At.X, ty = Target.Y - At.Y, tz = Target.Z - At.Z;
        double len = Math.Sqrt(tx * tx + ty * ty + tz * tz);
        if (len < 1e-9) return new Vec3(s.X, s.Y, s.Z);
        var h = new Vec3(s.X + tx / len, s.Y + ty / len, s.Z + tz / len);
        double hl = Math.Sqrt(h.X * h.X + h.Y * h.Y + h.Z * h.Z);
        return hl < 1e-9 ? new Vec3(s.X, s.Y, s.Z) : new Vec3(h.X / hl, h.Y / hl, h.Z / hl);
    }

    /// <summary>The face it presents now: following the sun, or held where it was left.</summary>
    public Vec3 Normal => Track || FixedNormal is not { } n ? IdealNormal() : n;

    /// <summary>The unit direction its reflected light leaves in: 2(n.s)n - s.</summary>
    public Vec3 Reflected
    {
        get
        {
            var n = Normal; var s = Sun.Direction;
            double d = n.X * s.X + n.Y * s.Y + n.Z * s.Z;
            return new Vec3(2 * d * n.X - s.X, 2 * d * n.Y - s.Y, 2 * d * n.Z - s.Z);
        }
    }

    /// <summary>Whether its light lands on the receiver it was built for: following the sun, the aim must be within the receiver's radius of it; held still, the reflected ray must pass that close.</summary>
    public bool OnReceiver
    {
        get
        {
            double rx = Receiver.X - At.X, ry = Receiver.Y - At.Y, rz = Receiver.Z - At.Z;
            double dist = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            if (dist < 1e-9) return true;
            if (Track)
            {
                double ex = Target.X - Receiver.X, ey = Target.Y - Receiver.Y, ez = Target.Z - Receiver.Z;
                return Math.Sqrt(ex * ex + ey * ey + ez * ez) <= ReceiverRadius;
            }
            var r = Reflected;
            // distance from the receiver's centre to the reflected ray
            double along = rx * r.X + ry * r.Y + rz * r.Z;
            if (along <= 0) return false;
            double cx = rx - along * r.X, cy = ry - along * r.Y, cz = rz - along * r.Z;
            return Math.Sqrt(cx * cx + cy * cy + cz * cz) <= ReceiverRadius;
        }
    }
    public double Area { get; set; } = area;                   // m²
    public double Reflectivity { get; } = reflectivity;        // polished bronze ~0.6, silvered glass ~0.9
    public double Collected { get; private set; }              // J thrown onto the target so far
    /// <summary>
    /// A burning mirror (issue #56): curved, it faces the sun square with its
    /// target at its focus (cos = 1) and gathers its light into a spot of
    /// <see cref="Image"/> m². A flat heliostat's image is its own size.
    /// </summary>
    public bool Focusing { get; init; }
    private double? _image;
    /// <summary>m² of the patch its light lands on: a flat mirror's own area, a burning mirror's focal spot.</summary>
    public double Image { get => _image ?? Area; init => _image = value; }
    private double _dust;
    /// <summary>The share of its light a coat of dust stops (issue #69): 0 clean. Settles in a storm; set it to 0 to clean it.</summary>
    public double Dust { get => _dust; set => _dust = Math.Clamp(value, 0, 1); }

    /// <summary>cos(θ/2): the share of the mirror's area square to the sun while it aims at the target.</summary>
    public double Cosine
    {
        get
        {
            var s = Sun.Direction;
            if (!Track && FixedNormal is { } n) return Math.Max(0, n.X * s.X + n.Y * s.Y + n.Z * s.Z);
            if (Focusing) return Sun.Elevation > 0 ? 1 : 0;
            double tx = Target.X - At.X, ty = Target.Y - At.Y, tz = Target.Z - At.Z;
            double len = Math.Sqrt(tx * tx + ty * ty + tz * tz);
            if (len < 1e-9) return 0;
            double cos = (s.X * tx + s.Y * ty + s.Z * tz) / len;
            return Math.Sqrt(Math.Max(0, (1 + cos) / 2));
        }
    }

    /// <summary>W landing on the target now.</summary>
    public double Power => Sun.DirectNormal * Area * Reflectivity * (1 - Dust) * Cosine;

    /// <summary>W that reach the receiver: all of <see cref="Power"/> while it is aimed at it, nothing once aimed away (#162).</summary>
    public double Delivered => OnReceiver ? Power : 0;

    public void Step(double dt) => Collected += Delivered * dt;
}
