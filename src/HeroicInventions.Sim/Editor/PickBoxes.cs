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

/// <summary>A part whose pick box a click's ray crossed, <paramref name="T"/> metres along the ray.</summary>
public readonly record struct PickHit(string Name, PickBox Box, double T);

/// <summary>
/// Which part a click on the Join machines tool picks (#225), kept out of the Godot script so the rule is tested without a window.
/// Boxes that do not contain one another: the nearest takes the click, as before. When the nearest box contains others the ray also
/// crossed (the gears inside the sails' box, the bank inside its crate's), the smaller box wins, and so on inwards. Every part
/// stays pickable: <see cref="List"/> is what the rare overlapping click offers to choose from.
/// </summary>
public static class PickBoxes
{
    /// <summary>Boxes whose volumes differ by less than this fraction count as the same size (two pinions of one catalogue gear, one a rotation of the other); the nearer wins.</summary>
    private const double SameSize = 0.01;

    /// <summary>Side of the box, metres, given to a part the view draws no body for, by kind; null for any other kind (it gets no box).</summary>
    public static double? SimPartSide(string kind) => kind switch
    {
        "generator" => 0.44,      // the motor can the view draws round the rotor (0.22 m radius): a cube that holds it whichever way the axle points, at the part's place; it holds the rotor's box too, so a click on the rotor's own place (the wheel's face under the can) picks the smaller rotor and a click on the can's body picks the motor (#247)
        "battery-bank" => 0.3,    // the cells in their crate, inside the crate's own 0.5 m box
        _ => null,
    };

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
            var inside = all.Where(h => h != cur && cur.Box.Encloses(h.Box) && h.Box.Volume < cur.Box.Volume * (1 - 1e-9)).ToList();
            if (inside.Count == 0) return cur;
            double smallest = inside.Min(h => h.Box.Volume);
            cur = inside.First(h => h.Box.Volume <= smallest * (1 + SameSize));   // inside is nearest first
        }
    }
}
