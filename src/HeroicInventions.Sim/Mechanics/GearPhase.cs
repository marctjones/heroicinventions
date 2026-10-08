using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// Where a gear must be turned so its teeth fall in its partner's gaps
/// (issue #85). Every generated gear (racket/heroic/geometry/gear.rkt) has a
/// tooth centred on its own +X, its axle along its own +Z; it stands turned
/// #:angle-deg about that axle, in the frame its axis and heading give it
/// (the view's BuildOnAxle). Rolling without slip at the pitch point means
/// the arcs the two pitch circles turn through match, in opposite senses,
/// and a tooth of one must face a gap of the other. With φ the angle of the
/// pitch point in each gear's own frame (the line of centres from A, and the
/// line back from B) and θ each gear's turn, a tooth of A sits zA·(θA − φA)
/// pitch-radians from the contact; B must then stand at
///   θB = φB + (π − s·zA·(θA − φA)) / zB     (mod 2π/zB)
/// where s is +1 when their axles point the same way (the usual case: the
/// senses reverse across the mesh) and −1 when one gear is turned over.
/// For two gears in one frame (φB = φA + π, s = 1) this is gear.rkt's
/// mate-angle exactly. The phase only places the teeth: the ratio and the
/// torque a mesh carries are the tooth counts', as before.
/// </summary>
public static class GearPhase
{
    /// <summary>gear.rkt's mate-angle, rad: B's turn for A turned <paramref name="angleA"/>, B's centre in direction <paramref name="line"/> from A's, both in one frame.</summary>
    public static double MateAngle(double za, double angleA, double zb, double line) =>
        line + Math.PI + (Math.PI - za * (angleA - line)) / zb;

    /// <summary>B's turn, rad, for its teeth to fall in A's gaps: φ the pitch point's angle in each gear's frame, <paramref name="sense"/> +1 for axles the same way.</summary>
    public static double Mate(double za, double thetaA, double phiA, double zb, double phiB, double sense) =>
        phiB + (Math.PI - sense * za * (thetaA - phiA)) / zb;

    /// <summary>How far B stands from where its teeth fall in A's gaps, degrees of B's turn, in (−half a tooth, half a tooth]: 0 interleaved, ±180/zB tooth on tooth.</summary>
    public static double ErrorDegrees(double za, double thetaA, double phiA, double zb, double thetaB, double phiB, double sense)
    {
        double pitch = 2 * Math.PI / zb;
        double off = thetaB - Mate(za, thetaA, phiA, zb, phiB, sense);
        off -= pitch * Math.Round(off / pitch);
        if (off <= -pitch / 2 + 1e-12) off += pitch;
        return off * 180 / Math.PI;
    }

    // ------------------------------------------------------------ a part's frame

    /// <summary>A wheel's axle before its heading: #:axis, raised #:tilt-deg (as the view's AxleOf).</summary>
    public static Vec3 AxleOf(PartSpec part)
    {
        double tilt = part.Number("tilt-deg", 0) * Math.PI / 180;
        return part.Symbol("axis", "z") switch
        {
            "x" => new Vec3(Math.Cos(tilt), Math.Sin(tilt), 0),
            "y" => new Vec3(Math.Sin(tilt), Math.Cos(tilt), 0),
            _ => new Vec3(0, Math.Sin(tilt), Math.Cos(tilt)),
        };
    }

    /// <summary>
    /// The gear's frame before its own turn, as rows of a rotation (local→world
    /// is its transpose): the heading about +Y after the turn taking +Z onto the
    /// axle (the view's YawOf · AxleBasis).
    /// </summary>
    private static double[,] FrameOf(PartSpec part)
    {
        var a = AxleOf(part);
        double[,] axle;
        double cx = -a.Y, cy = a.X;                // z × axis
        double sin = Math.Sqrt(cx * cx + cy * cy);
        if (sin < 1e-5)
            axle = a.Z >= 0 ? Rotation(0, 1, 0, 0) : Rotation(0, 1, 0, Math.PI);
        else
            axle = Rotation(cx / sin, cy / sin, 0, Math.Atan2(sin, a.Z));
        double h = MachineDef.HeadingOf(part) * Math.PI / 180;
        return Multiply(Rotation(0, 1, 0, h), axle);
    }

    /// <summary>A right-handed turn of <paramref name="angle"/> about the unit axis (x, y, z) (Rodrigues).</summary>
    private static double[,] Rotation(double x, double y, double z, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle), t = 1 - c;
        return new[,]
        {
            { t * x * x + c, t * x * y - s * z, t * x * z + s * y },
            { t * x * y + s * z, t * y * y + c, t * y * z - s * x },
            { t * x * z - s * y, t * y * z + s * x, t * z * z + c },
        };
    }

    private static double[,] Multiply(double[,] p, double[,] q)
    {
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[i, j] = p[i, 0] * q[0, j] + p[i, 1] * q[1, j] + p[i, 2] * q[2, j];
        return r;
    }

    /// <summary>The direction of <paramref name="to"/> from <paramref name="part"/>'s centre, as an angle about its axle in its own frame (unturned), rad.</summary>
    private static double LocalAngle(PartSpec part, Vec3 to)
    {
        var f = FrameOf(part);
        double dx = to.X - part.At.X, dy = to.Y - part.At.Y, dz = to.Z - part.At.Z;
        // world → local is the transpose: local = Fᵀ · d
        double lx = f[0, 0] * dx + f[1, 0] * dy + f[2, 0] * dz;
        double ly = f[0, 1] * dx + f[1, 1] * dy + f[2, 1] * dz;
        return Math.Atan2(ly, lx);
    }

    private static Vec3 WorldAxle(PartSpec part)
    {
        var f = FrameOf(part);
        return new Vec3(f[0, 2], f[1, 2], f[2, 2]);
    }

    private static double Sense(PartSpec a, PartSpec b)
    {
        Vec3 x = WorldAxle(a), y = WorldAxle(b);
        return x.X * y.X + x.Y * y.Y + x.Z * y.Z >= 0 ? 1 : -1;
    }

    private static bool IsGear(PartSpec? p) => p is not null && p.Kind == "wheel" && p.Symbol("shape", "") == "gear" && p.Number("teeth", 0) > 0;

    private static double Turn(PartSpec p) => p.Number("angle-deg", 0) * Math.PI / 180;

    /// <summary>How far a mesh's second gear stands from its partner's gaps, as written: degrees of its turn (see <see cref="ErrorDegrees"/>).</summary>
    public static double MeshErrorDegrees(PartSpec a, PartSpec b) =>
        ErrorDegrees(a.Number("teeth"), Turn(a), LocalAngle(a, b.At), b.Number("teeth"), Turn(b), LocalAngle(b, a.At), Sense(a, b));

    /// <summary>
    /// The #:angle-deg each gear of the trains touching <paramref name="touched"/>
    /// (all trains when null) needs for every mesh to interleave. A train is
    /// the gears joined by meshes (an arbor's wheels turn together, but the
    /// teeth of one have nothing to do with the other's). It keeps its root
    /// where it stands — the gear a crank turns (#:drive-rpm), else the first
    /// mesh's first gear — and phases the rest outward from it, each from the
    /// partner that reached it first; a closed loop of meshes keeps whatever
    /// its last mesh then gets. Only the gears that need a new angle are
    /// returned, each the nearest to its written angle that interleaves
    /// (so it moves less than half a tooth).
    /// </summary>
    public static Dictionary<string, double> Rephase(IReadOnlyDictionary<string, PartSpec> parts, IReadOnlyList<MeshSpec> meshes, IEnumerable<string>? touched = null)
    {
        var edges = new Dictionary<string, List<string>>();
        foreach (var m in meshes)
        {
            if (!IsGear(parts.GetValueOrDefault(m.A)) || !IsGear(parts.GetValueOrDefault(m.B))) continue;
            (edges.TryGetValue(m.A, out var la) ? la : edges[m.A] = []).Add(m.B);
            (edges.TryGetValue(m.B, out var lb) ? lb : edges[m.B] = []).Add(m.A);
        }
        var wanted = touched?.ToHashSet();
        var angle = new Dictionary<string, double>();   // each phased gear's turn, rad (the root's as written)
        var result = new Dictionary<string, double>();
        var seen = new HashSet<string>();
        foreach (var m in meshes)
        {
            if (!edges.ContainsKey(m.A) || seen.Contains(m.A)) continue;
            // the train: every gear meshed with this one, through any number of meshes
            var train = new List<string> { m.A };
            seen.Add(m.A);
            for (int i = 0; i < train.Count; i++)
                foreach (var n in edges[train[i]])
                    if (seen.Add(n)) train.Add(n);
            if (wanted is not null && !train.Any(wanted.Contains)) continue;
            string root = train.FirstOrDefault(id => parts[id].Number("drive-rpm", 0) != 0) ?? m.A;
            angle[root] = Turn(parts[root]);
            var queue = new Queue<string>([root]);
            while (queue.Count > 0)
            {
                string here = queue.Dequeue();
                var a = parts[here] with { Props = WithAngle(parts[here], angle[here]) };
                foreach (var n in edges[here])
                {
                    if (angle.ContainsKey(n)) continue;
                    // the nearest angle to the one written that interleaves: a gear already in its
                    // partner's gaps (as gear.rkt's mate-angle places them) keeps its angle
                    double had = parts[n].Number("angle-deg", 0);
                    double off = MeshErrorDegrees(a, parts[n]);
                    angle[n] = (had - off) * Math.PI / 180;
                    if (Math.Abs(off) > 1e-9) result[n] = ((had - off) % 360 + 360) % 360;
                    queue.Enqueue(n);
                }
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, SExpr> WithAngle(PartSpec p, double rad) =>
        new Dictionary<string, SExpr>(p.Props) { ["angle-deg"] = new SNumber(rad * 180 / Math.PI) };
}
