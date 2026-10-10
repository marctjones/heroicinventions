using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Game;

/// <summary>
/// When the rover has FOUND a buried crate (owner decision on #240: show the rough area until then). Until found, build mode and the
/// scene show only the crate's rough area (<see cref="RoughArea"/>), never its place or depth; once found they show it exactly, and it
/// stays found (a save keeps it: <see cref="FoundCargo"/>). Found is any one of:
/// <list type="bullet">
/// <item>its top is exposed: somewhere over the crate's top face the ground is at most <see cref="ExposedCover"/> m above it (the
/// smallest cover over a grid of points on the top, not only over its middle: a corner showing in the bottom of a hole is a find; the
/// goals' find-bank asks for the middle clear, cover &lt;= 0);</item>
/// <item>it was freed: the ground held it once and has let it go (Burial.Held false after true), which stands it on the ground;</item>
/// <item>the backhoe's teeth struck it: a dig put the teeth inside the crate's box grown by <see cref="StrikeReach"/> m. The teeth are on
/// the ground when they dig, so this is also the ground worked down to the crate, but it catches a trench dug beside the crate past its top,
/// which the cover over the top does not see: whoever digs feels the teeth hit a lid or a side;</item>
/// <item>the rover touched it: one of the rover's bodies within <see cref="TouchGap"/> m of the crate's box. Buried, the crate can only be
/// touched where it shows, so this mostly agrees with the cover; it is kept as the plain rule ("the rover has touched it").</item>
/// </list>
/// None of these can be seen before the ground has settled after the slide (the crates fall with the rim), so the game only asks once it has.
/// </summary>
public static class CargoFind
{
    /// <summary>m of ground over some part of the crate's top at or under which it shows (any part of its top exposed).</summary>
    public const double ExposedCover = 0.05;

    /// <summary>m round the crate's box within which the teeth of a dig strike it.</summary>
    public const double StrikeReach = 0.05;

    /// <summary>m round the crate's box within which a body of the rover touches it.</summary>
    public const double TouchGap = 0.03;

    /// <summary>Points on a cube's top face of side <paramref name="size"/>, in its own frame (u along x, v along z), <paramref name="n"/> by <paramref name="n"/> from edge to edge.</summary>
    public static IEnumerable<(double U, double V)> TopSamples(double size, int n = 5)
    {
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                yield return (size * (i / (double)(n - 1) - 0.5), size * (j / (double)(n - 1) - 0.5));
    }

    /// <summary>The least cover over the top: the lowest ground over it less the top's height (negative where the ground is below the top).</summary>
    public static double TopCover(double top, IEnumerable<double> groundOverTop) => groundOverTop.Min() - top;

    /// <summary>Whether a point in the cube's own frame (its centre the origin) lies inside the cube grown by <paramref name="margin"/>.</summary>
    public static bool InBox(double x, double y, double z, double size, double margin)
    {
        double h = size / 2 + margin;
        return Math.Abs(x) <= h && Math.Abs(y) <= h && Math.Abs(z) <= h;
    }

    /// <summary>The rule: why the crate counts as found now, or null if it does not.</summary>
    public static string? Why(double topCover, bool freed, bool struck, bool touched) =>
        topCover <= ExposedCover ? $"its top shows ({topCover:0.00} m over its least-covered part)"
        : freed ? "freed: the ground has let it go"
        : struck ? "the backhoe's teeth struck it"
        : touched ? "the rover touched it"
        : null;

    public static bool Found(double topCover, bool freed, bool struck, bool touched) => Why(topCover, freed, struck, touched) is not null;
}

/// <summary>
/// The crates found so far, by placement label and block, as the world save keeps them: <c>(found-cargo 1 (crate LABEL BLOCK) …)</c>.
/// An older save has none and loads with nothing found (the crates are found again as they are dug).
/// </summary>
public sealed class FoundCargo
{
    private readonly SortedSet<(string Label, string Block)> _found = [];

    public IReadOnlyCollection<(string Label, string Block)> All => _found;
    public bool Contains(string label, string block) => _found.Contains((label, block));
    public bool Add(string label, string block) => _found.Add((label, block));

    /// <summary>The save's form, or null when nothing has been found (an older save looks the same).</summary>
    public SList? ToForm() => _found.Count == 0 ? null
        : new SList([new SSymbol("found-cargo"), new SNumber(1),
                     .. _found.Select(f => (SExpr)new SList([new SSymbol("crate"), new SSymbol(f.Label), new SSymbol(f.Block)]))]);

    public static FoundCargo Parse(SList? form)
    {
        var found = new FoundCargo();
        foreach (var c in form?.Items.Skip(2).OfType<SList>() ?? [])
            if (c.Items is [SSymbol { Name: "crate" }, SSymbol label, SSymbol block]) found.Add(label.Name, block.Name);
        return found;
    }
}
