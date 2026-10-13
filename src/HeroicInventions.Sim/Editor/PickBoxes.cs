namespace HeroicInventions.Sim.Editor;

/// <summary>An axis-aligned pick box, metres.</summary>
public readonly record struct PickBox(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
    public double Volume => Math.Max(0, MaxX - MinX) * Math.Max(0, MaxY - MinY) * Math.Max(0, MaxZ - MinZ);

    /// <summary>True when <paramref name="o"/> lies inside this box (touching faces count).</summary>
    public bool Encloses(PickBox o, double eps = 1e-6) =>
        o.MinX >= MinX - eps && o.MinY >= MinY - eps && o.MinZ >= MinZ - eps && o.MaxX <= MaxX + eps && o.MaxY <= MaxY + eps && o.MaxZ <= MaxZ + eps;

    public static PickBox Around(double x, double y, double z, double side) =>
        new(x - side / 2, y - side / 2, z - side / 2, x + side / 2, y + side / 2, z + side / 2);
}

/// <summary>A part whose pick box a click's ray crossed, <paramref name="T"/> metres along the ray. An <paramref name="Opaque"/> part is drawn as a solid body that hides what is inside its box (<see cref="PickBoxes.HidesWhatIsInside"/>): the click picks it, not a smaller box inside.</summary>
public readonly record struct PickHit(string Name, PickBox Box, double T, bool Opaque = false);

/// <summary>
/// Which part a click on the Join machines tool picks (#225), kept out of the Godot script so the rule is tested without a window.
/// Boxes that do not contain one another: the nearest takes the click, as before. When the nearest box contains others the ray also
/// crossed (the gears inside the sails' box, the bank inside its crate's), the smaller box wins, and so on inwards. Every part
/// stays pickable: <see cref="List"/> is what the rare overlapping click offers to choose from. (A solid body that hides what is
/// inside its box takes the click itself: <see cref="HidesWhatIsInside"/>.)
/// </summary>
public static class PickBoxes
{
    /// <summary>Boxes whose volumes differ by less than this fraction count as the same size (two pinions of one catalogue gear, one a rotation of the other); the nearer wins.</summary>
    private const double SameSize = 0.01;

    /// <summary>Side of the box, metres, given to a part the view draws no body for, by kind; null for any other kind (it gets no box).</summary>
    public static double? SimPartSide(string kind) => kind switch
    {
        "generator" => 0.44,      // the motor can the view draws round the rotor (0.22 m radius): a cube that holds it whichever way the axle points, at the part's place; it holds the rotor's box too, and it is opaque (HidesWhatIsInside), so a click anywhere on the can, its dial's centre too, picks the motor (#247)
        "battery-bank" => 0.3,    // the cells in their crate, inside the crate's own 0.5 m box
        _ => null,
    };

    /// <summary>True for a kind the view draws as a solid body round whatever else lies in its box (the generator's can round its rotor): a click on it picks it, not the smaller box inside, which is not to be seen. The part inside is still in <see cref="List"/>.</summary>
    public static bool HidesWhatIsInside(string kind) => kind == "generator";

    /// <summary>
    /// True when a ray from (<paramref name="ox"/>, <paramref name="oy"/>, <paramref name="oz"/>) along (<paramref name="dx"/>, <paramref name="dy"/>, <paramref name="dz"/>)
    /// enters <paramref name="box"/> through its end on the +axis side, for an axle (<paramref name="ax"/>, <paramref name="ay"/>, <paramref name="az"/>) along a coordinate
    /// axis (within 20 degrees): a click on a generator's back cap, where the rotor's wheel lies flush with the can's end (the dial is on the -axis end),
    /// is a click on the rotor, so the rotor is reachable by its own face without the list (#247). False for a ray that misses, or an axle not along an axis.
    /// </summary>
    public static bool EntersByBack(PickBox box, double ox, double oy, double oz, double dx, double dy, double dz, double ax, double ay, double az)
    {
        double[] o = [ox, oy, oz], d = [dx, dy, dz], lo = [box.MinX, box.MinY, box.MinZ], hi = [box.MaxX, box.MaxY, box.MaxZ], a = [ax, ay, az];
        double len = Math.Sqrt(ax * ax + ay * ay + az * az);
        int k = Array.IndexOf(a, a.MaxBy(Math.Abs));
        if (len < 1e-9 || Math.Abs(a[k]) / len < Math.Cos(20 * Math.PI / 180)) return false;
        double tMin = 0, tMax = double.MaxValue; int enteredBy = -1;
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(d[i]) < 1e-12) { if (o[i] < lo[i] || o[i] > hi[i]) return false; continue; }
            double t1 = (lo[i] - o[i]) / d[i], t2 = (hi[i] - o[i]) / d[i];
            if (t1 > t2) (t1, t2) = (t2, t1);
            if (t1 > tMin) { tMin = t1; enteredBy = i; }
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        // the face entered has its outward normal against the ray's direction on that coordinate; the +axis end is the one whose normal is along the axle's sign
        return enteredBy == k && -Math.Sign(d[k]) == Math.Sign(a[k]);
    }

    /// <summary>Every crossed box, nearest first (the order the log's "click crossed" line prints, and the pick list shows).</summary>
    public static IReadOnlyList<PickHit> List(IEnumerable<PickHit> hits) => hits.OrderBy(h => h.T).ToList();

    /// <summary>The part the click picks, or null when no box was crossed.</summary>
    public static PickHit? Choose(IEnumerable<PickHit> hits)
    {
        var all = List(hits);
        if (all.Count == 0) return null;
        var cur = all[0];
        while (true)
        {
            if (cur.Opaque) return cur;
            if (all.FirstOrDefault(h => h.Opaque && h != cur && h.Box.Encloses(cur.Box)) is { Name: not null } cover) return cover;   // the ray met a part inside the solid body first (from behind it): the body is what is drawn
            var inside = all.Where(h => h != cur && cur.Box.Encloses(h.Box) && h.Box.Volume < cur.Box.Volume * (1 - 1e-9)).ToList();
            if (inside.Count == 0) return cur;
            double smallest = inside.Min(h => h.Box.Volume);
            cur = inside.First(h => h.Box.Volume <= smallest * (1 + SameSize));   // inside is nearest first
        }
    }
}
