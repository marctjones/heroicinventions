using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// Ground the rover has worked (issue #63): a patch of fine cells laid over the map's coarse ones where the backhoe digs.
///
/// The crater's cells are 5 m, so a 0.2 m³ bucketful is 8 mm of one cell and a trench or a heap cannot be seen, or driven on.
/// Refining the whole map is out: at 0.25 m it is 3.4 million cells and every map's settling would change (the angle of repose
/// is checked between neighbours, so a cell's size is in the numbers); at 1 m, 722,000 cells, still 25 times what the water,
/// the views and the slides scan today. So a patch is made where the rover first digs, 30 m a side (the map's own cells, a
/// whole number of them), of cells <see cref="FineCell"/> m across, and the ground there is read from it:
/// <see cref="Terrain.HeightAt"/> asks the patch first. It is a second <see cref="Terrain"/> (the same Dig, Heap, Relax and
/// Mohr–Coulomb as everywhere else), with its node (i, j) at the position of the coarse mesh's own surface to begin with, the
/// coarse mesh's triangles included, so on the day it is made it is the ground that was there, to the last bit.
///
/// The patch's outer ring of nodes sits on the coarse cell centres and is never changed; the rover works at least
/// <see cref="Margin"/> m inside it. The coarse heights and soils are as they were; the water on the map cells wholly inside the
/// patch moves onto the patch's own fine grid (<see cref="PatchWater"/>, #200), every drop of it. A world where no one digs is untouched.
///
/// The rover moves soil wherever its arm reaches (#72, owner decision 2026-10-08): it digs down to bedrock and tips spoil on any
/// ground, a heap on a heap, so a pile grows bucket by bucket until its sides stand at repose. Soil is not a load, and lifting it
/// is the backhoe's job. What it must not do is hand energy to a body by a quirk of the model: a dump or a dig whose ground (the
/// slide after it included) would rise under or into a body is refused (<see cref="Try"/>, the view's check), so raising soil
/// never lifts or shoves a load by the back door.
/// </summary>
public sealed class WorkedGround
{
    /// <summary>m, a fine cell's side.</summary>
    public const double FineCell = 0.25;
    /// <summary>m, how far inside the patch's edge the rover may dig or dump.</summary>
    public const double Margin = 3;
    /// <summary>m, the radius of the ground a bucket scrapes, and of the heap it makes when tipped (before it slumps).</summary>
    public const double DigRadius = 0.45, PourRadius = 0.3;
    /// <summary>A patch is this many metres a side to begin with (rounded up to the map's cells), and no more than three times that once patches merge.</summary>
    public const double Side = 30;

    public Terrain Base { get; }
    public Terrain Fine { get; }
    /// <summary>The coarse nodes the patch spans, inclusive.</summary>
    public int Bi0 { get; }
    public int Bj0 { get; }
    public int Bi1 { get; }
    public int Bj1 { get; }
    /// <summary>Fine cells to a coarse one.</summary>
    public int K { get; }
    /// <summary>The ground's extent (m): the first and last nodes' positions.</summary>
    public double MinX { get; }
    public double MaxX { get; }
    public double MinZ { get; }
    public double MaxZ { get; }

    /// <summary>The heights the patch started with (or was last shaped to): what a dug or heaped cell is measured against.</summary>
    public double[] Original { get; }
    /// <summary>The lowest each node may be cut: bedrock is never cut, ground that was bedrock keeps its level under any spoil on it, and
    /// rubble a slide left on rock (<see cref="Terrain.Covering"/>) is soil down to the rock's top as it was.</summary>
    public double[] Floor { get; }

    /// <summary>The water standing on the patch (#200), on a grid of its fine cells nested in the map's; null until the map's water first steps with the patch there.</summary>
    public PatchWater? Water { get; internal set; }

    /// <summary>m³ taken out of the patch and put back into it since it was made (the totals of patches merged into it included).</summary>
    public double Dug { get; private set; }
    public double Dumped { get; private set; }

    public int Nx => Fine.Nx;
    public int Nz => Fine.Nz;
    public int Version => Fine.Version;
    public double NodeX(int i) => MinX + i * FineCell;
    public double NodeZ(int j) => MinZ + j * FineCell;
    private double Area => Fine.Cell * Fine.Cell;

    private bool Hard(int node) => Fine.SoilOf(node).Cohesion >= Terrain.RockCohesion;

    /// <summary>A patch over coarse nodes [bi0, bi1] × [bj0, bj1] of <paramref name="ground"/>, the ground as it stands.</summary>
    public WorkedGround(Terrain ground, int bi0, int bj0, int bi1, int bj1)
    {
        Base = ground;
        (Bi0, Bj0, Bi1, Bj1) = (bi0, bj0, bi1, bj1);
        K = Math.Max(1, (int)Math.Round(ground.Cell / FineCell));
        double cf = ground.Cell / K;
        int nx = (bi1 - bi0) * K + 1, nz = (bj1 - bj0) * K + 1;
        MinX = ground.CellX(bi0); MaxX = ground.CellX(bi1);
        MinZ = ground.CellZ(bj0); MaxZ = ground.CellZ(bj1);
        var heights = new double[nx * nz];
        var soil = new int[nx * nz];
        var rock = new double[nx * nz];   // the rock's top under rubble a slide left on it (#72), else NaN
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                double x = MinX + i * cf, z = MinZ + j * cf;
                int k = i + j * nx, cell = ground.CellAt(x, z)!.Value;
                heights[k] = ground.CoarseSurfaceAt(x, z);
                soil[k] = ground.Soil[cell];
                rock[k] = double.NaN;
                if (ground.Covering(cell) is { } covered && heights[k] > covered.RockTop)
                    (soil[k], rock[k]) = (covered.Soil, covered.RockTop);
            }
        Fine = new Terrain
        {
            Name = ground.Name + "-worked", X0 = MinX - cf / 2, Z0 = MinZ - cf / 2, Cell = cf, Nx = nx, Nz = nz,
            Heights = heights, Soil = soil,
            Soils = ground.Soils.Select(s => s with { Boulders = null }).ToList(),   // a bucketful leaves no boulders
            OpenEdges = false, Roughness = ground.Roughness,
        };
        Original = (double[])heights.Clone();
        Floor = new double[nx * nz];
        for (int k = 0; k < Floor.Length; k++) Floor[k] = !double.IsNaN(rock[k]) ? rock[k] : Hard(k) ? heights[k] : double.NegativeInfinity;
    }

    /// <summary>A patch made by merging <paramref name="parts"/> (all of one map, on its lattice) over the nodes given: each node is taken from the part that has it, else from the map.</summary>
    internal static WorkedGround Merged(Terrain ground, int bi0, int bj0, int bi1, int bj1, IEnumerable<WorkedGround> parts)
    {
        var w = new WorkedGround(ground, bi0, bj0, bi1, bj1);
        foreach (var p in parts)
        {
            int di = (p.Bi0 - bi0) * w.K, dj = (p.Bj0 - bj0) * w.K;
            for (int j = 0; j < p.Nz; j++)
                for (int i = 0; i < p.Nx; i++)
                {
                    int from = i + j * p.Nx, to = i + di + (j + dj) * w.Nx;
                    w.Fine.Heights[to] = p.Fine.Heights[from];
                    w.Fine.Soil[to] = p.Fine.Soil[from];
                    w.Fine.Loose[to] = p.Fine.Loose[from];
                    w.Original[to] = p.Original[from];
                    w.Floor[to] = p.Floor[from];
                }
            w.Dug += p.Dug; w.Dumped += p.Dumped;
        }
        w.Fine.Touch();
        // the water on the parts goes with them, cell for cell: their grids lie on the same lattice as the merged one's (#200)
        foreach (var p in parts)
        {
            if (p.Water is not { Ci: > 0, Cj: > 0 } from) continue;
            var to = w.Water ??= new PatchWater(w);
            int di = (from.I0 - to.I0) * w.K, dj = (from.J0 - to.J0) * w.K;
            for (int j = 0; j < from.Bed.Nz; j++)
                for (int i = 0; i < from.Bed.Nx; i++)
                {
                    var (h, qx, qz) = from.Water.CellState(i + j * from.Bed.Nx);
                    to.Water.SetCell(i + di + (j + dj) * to.Bed.Nx, h, qx, qz);
                }
        }
        return w;
    }

    /// <summary>Whether a point is over the patch, <paramref name="inset"/> m or more in from its edge.</summary>
    public bool Inside(double x, double z, double inset = 0) =>
        x >= MinX + inset && x <= MaxX - inset && z >= MinZ + inset && z <= MaxZ - inset;

    /// <summary>The ground's height at a point of the patch, interpolated between its nodes.</summary>
    public double HeightAt(double x, double z) => Fine.HeightAt(x, z);

    /// <summary>
    /// The net volume (m³) the patch holds above what it started with: the sum over its nodes of the rise of each, times a
    /// node's area. Digging takes it down, tipping puts it back, sliding moves it from node to node and changes nothing.
    /// </summary>
    public double Net()
    {
        double sum = 0;
        for (int k = 0; k < Original.Length; k++) sum += Fine.Heights[k] - Original[k];
        return sum * Area;
    }

    /// <summary>
    /// The potential energy (J) of the soil the patch holds above its starting ground, taking each node's column as it stands:
    /// ρ g A (h² − h₀²) / 2 summed over nodes, h measured up from <paramref name="datum"/> (a level below the patch's lowest ground).
    /// </summary>
    public double PotentialEnergy(double gravity, double datum)
    {
        double sum = 0;
        for (int k = 0; k < Original.Length; k++)
        {
            double h = Fine.Heights[k] - datum, h0 = Original[k] - datum;
            sum += Fine.SoilOf(k).Density * (h * h - h0 * h0) / 2;
        }
        return sum * gravity * Area;
    }

    private int Node(double x, double z)
    {
        int i = (int)Math.Round((x - MinX) / FineCell), j = (int)Math.Round((z - MinZ) / FineCell);
        return Math.Clamp(i, 0, Nx - 1) + Math.Clamp(j, 0, Nz - 1) * Nx;
    }

    private List<int> Within(double x, double z, double radius)
    {
        var nodes = new List<int>();
        int i0 = Math.Max(1, (int)Math.Floor((x - radius - MinX) / FineCell)), i1 = Math.Min(Nx - 2, (int)Math.Ceiling((x + radius - MinX) / FineCell));
        int j0 = Math.Max(1, (int)Math.Floor((z - radius - MinZ) / FineCell)), j1 = Math.Min(Nz - 2, (int)Math.Ceiling((z + radius - MinZ) / FineCell));
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                double dx = NodeX(i) - x, dz = NodeZ(j) - z;
                if (dx * dx + dz * dz <= radius * radius + 1e-9) nodes.Add(i + j * Nx);
            }
        return nodes;
    }

    /// <summary>What a bucket took: its volume (m³), the mean level (m) of the soil it took as it lay (weighted by what each node gave), the soil, and the mean surface it left.</summary>
    public readonly record struct Scooped(double Volume, double Level, int Soil, double MeanSurface);

    /// <summary>
    /// A bucket scrapes the ground within <see cref="DigRadius"/> of a point to one depth, deeper where it must to fill, but not
    /// below bedrock (a node of bedrock gives nothing) and not past <paramref name="volume"/> m³. Returns what it took, or null
    /// if there was nothing to take.
    /// </summary>
    public Scooped? Scoop(double x, double z, double volume)
    {
        var nodes = Within(x, z, DigRadius);
        if (nodes.Count == 0 || volume <= 0) return null;
        var avail = nodes.Select(k => Math.Max(0, Fine.Heights[k] - Floor[k])).ToArray();
        double depth = FillDepth(avail, volume / Area);
        double taken = 0, from = 0;
        for (int n = 0; n < nodes.Count; n++)
        {
            double d = Math.Min(depth, avail[n]);
            if (d <= 0) continue;
            int k = nodes[n];
            from += d * (Fine.Heights[k] - d / 2);   // the middle of the slab it gave
            taken += d;
            Fine.Heights[k] -= d;
        }
        if (taken <= 0) return null;
        Fine.Touch();
        double m3 = taken * Area;
        Dug += m3;
        return new Scooped(m3, from / taken, Fine.Soil[Node(x, z)], nodes.Average(k => Fine.Heights[k]));
    }

    /// <summary>The depth d at which the sum of min(d, available) over the nodes is <paramref name="total"/> (m, cell-depths): all of it if it is less.</summary>
    private static double FillDepth(double[] avail, double total)
    {
        if (avail.Sum() <= total) return avail.Max();
        var sorted = avail.OrderBy(a => a).ToArray();
        double used = 0;
        for (int n = 0; n < sorted.Length; n++)
        {
            int left = sorted.Length - n;
            double d = (total - used) / left;
            if (d <= sorted[n]) return d;
            used += sorted[n];
        }
        return sorted[^1];
    }

    /// <summary>The mean height of the ground that <see cref="Pour"/> would tip onto (m), or null if there is none (off the patch's workable ground).</summary>
    public double? LandingSurface(double x, double z)
    {
        var nodes = Within(x, z, PourRadius);
        return nodes.Count == 0 ? null : nodes.Average(k => Fine.Heights[k]);
    }

    /// <summary>
    /// Tips <paramref name="m3"/> of loose soil onto the ground within <see cref="PourRadius"/> of a point, level over its nodes,
    /// whatever its height, but not on a node closed by a body (<see cref="Terrain.Blocked"/>): it heaps on the open ones. Returns
    /// the surface it landed on, or null if there is no open workable ground there.
    /// </summary>
    public double? Pour(double x, double z, double m3, int soil)
    {
        var nodes = Within(x, z, PourRadius);
        if (Fine.Blocked is { } closed) nodes.RemoveAll(k => closed[k]);
        if (nodes.Count == 0 || m3 <= 0) return null;
        double surface = nodes.Average(k => Fine.Heights[k]);
        double rise = m3 / (nodes.Count * Area);
        foreach (int k in nodes)
        {
            Fine.Heights[k] += rise;
            Fine.Loose[k] = true;
            if (Hard(k)) Fine.Soil[k] = soil;   // spoil on bedrock is soil, not rock (the rock under it is still there: Floor)
        }
        Fine.Touch();
        Dumped += m3;
        return surface;
    }

    /// <summary>A node a change to the ground raised: where it is (m), and its height before and after.</summary>
    public readonly record struct Raised(int Node, double X, double Z, double Before, double After);

    /// <summary>
    /// Makes <paramref name="change"/> to the ground (a dig or a dump, and the slide after it) and keeps it only if
    /// <paramref name="allow"/> lets it, given every node the change raised by more than a micron (the relaxation's own
    /// cut-off); otherwise the ground is put back as it was, to the bit: heights, loose flags, soils, the totals and the
    /// version, so a view sees no change. Returns whether the change was kept. (#72: a dump or a slide that would lift or
    /// shove a body is the rover lifting a load by the back door; the view knows where the bodies are, so it decides.)
    /// </summary>
    public bool Try(Action change, Func<IReadOnlyList<Raised>, bool> allow)
    {
        var heights = (double[])Fine.Heights.Clone();
        var loose = (bool[])Fine.Loose.Clone();
        var soil = (int[])Fine.Soil.Clone();
        var (dug, dumped, version) = (Dug, Dumped, Fine.Version);
        change();
        var raised = new List<Raised>();
        for (int k = 0; k < heights.Length; k++)
            if (Fine.Heights[k] > heights[k] + 1e-6) raised.Add(new Raised(k, NodeX(k % Nx), NodeZ(k / Nx), heights[k], Fine.Heights[k]));
        if (raised.Count == 0 || allow(raised)) return true;
        Array.Copy(heights, Fine.Heights, heights.Length);
        Array.Copy(loose, Fine.Loose, loose.Length);
        Array.Copy(soil, Fine.Soil, soil.Length);
        (Dug, Dumped) = (dug, dumped);
        Fine.PutBack(version);
        return false;
    }

    /// <summary>
    /// Makes <paramref name="change"/> (a dig or a dump, and the slide after it) with the soil going round the bodies standing on
    /// the ground, as gravel poured against a crate piles round it (#72, owner decision 2026-10-08: nothing is refused). The change
    /// is tried; every node it raised that <paramref name="inTheWay"/> says would rise under or against a body is closed
    /// (<see cref="Terrain.Blocked"/>: a tip lands only on open nodes, and a slide's deposit stops at a closed one, the body a
    /// retaining wall, the soil staying upslope at its repose); the ground is put back and the change made again, until it raises
    /// nothing that reaches a body. Volume is kept exactly; ground under a body may still go down (its support dug away, it falls).
    /// Returns false only if it could not be done in <paramref name="rounds"/> tries (the ground then as it was).
    /// </summary>
    public bool Around(Action change, Func<Raised, bool> inTheWay, int rounds = 24)
    {
        var closed = new bool[Fine.Heights.Length];
        for (int r = 0; r < rounds; r++)
        {
            bool kept = Try(() =>
            {
                Fine.Blocked = closed;
                try { change(); } finally { Fine.Blocked = null; }
            }, raised =>
            {
                bool clear = true;
                foreach (var n in raised)
                    if (inTheWay(n)) { closed[n.Node] = true; clear = false; }
                return clear;
            });
            if (kept) return true;
        }
        return false;
    }

    /// <summary>Lets the patch settle: loose soil steeper than its repose slides, cut faces taller than their soil's critical height fail, until it stands.</summary>
    public (int Failures, int Passes) Settle(double gravity) => Fine.Relax(1, 1, Nx - 2, Nz - 2, gravity, 20000);

    /// <summary>
    /// Sets the patch's ground to <paramref name="surface"/> (x, z → m), for a scenario that starts from a landform (a bank, a
    /// pit): the heights it then holds are what later digging is measured against.
    /// </summary>
    public void Shape(Func<double, double, double> surface)
    {
        for (int j = 0; j < Nz; j++)
            for (int i = 0; i < Nx; i++)
            {
                int k = i + j * Nx;
                Fine.Heights[k] = Original[k] = surface(NodeX(i), NodeZ(j));
                Fine.Loose[k] = false;
                Floor[k] = Hard(k) ? Fine.Heights[k] : double.NegativeInfinity;
            }
        Fine.Touch();
    }

    // ---------------------------------------------------------------- saving (#199)

    internal const int SaveVersion = 1;

    /// <summary>
    /// The patch as a save holds it: <c>(patch (box I0 J0 I1 J1) (dug V) (dumped V) (heights …) (original (k level) …) (rock (k level) …) (soil (value count) …) (loose k …) [(water (k depth qx qz) …)])</c>.
    /// Heights are all written; a node's original height only where it differs from its height (an untouched node has the two
    /// the same), the floor only where it is not minus infinity (bedrock), the soil as runs, loose flags as the nodes that are,
    /// and the water standing on it (#200), cell by cell of its grid where there is any (a dry patch has no water field).
    /// Numbers are written to the digit that reads back as the same double.
    /// </summary>
    internal SList Save()
    {
        static SList L(string head, IEnumerable<SExpr> v) => new([new SSymbol(head), .. v]);
        static SExpr N(double v) => new SNumber(v);
        var h = Fine.Heights;
        var original = new List<SExpr>();
        var rock = new List<SExpr>();
        var loose = new List<SExpr>();
        var soil = new List<SExpr>();
        for (int k = 0; k < h.Length; k++)
        {
            if (Original[k] != h[k]) original.Add(new SList([N(k), N(Original[k])]));
            if (!double.IsNegativeInfinity(Floor[k])) rock.Add(new SList([N(k), N(Floor[k])]));
            if (Fine.Loose[k]) loose.Add(N(k));
        }
        for (int k = 0, run; k < h.Length; k += run)
        {
            for (run = 1; k + run < h.Length && Fine.Soil[k + run] == Fine.Soil[k]; run++) { }
            soil.Add(new SList([N(Fine.Soil[k]), N(run)]));
        }
        var items = new List<SExpr> { new SSymbol("patch"),
            L("box", new[] { Bi0, Bj0, Bi1, Bj1 }.Select(x => N(x))),
            L("dug", [N(Dug)]), L("dumped", [N(Dumped)]),
            L("heights", h.Select(N)), L("original", original), L("rock", rock), L("soil", soil), L("loose", loose) };
        if (Water?.Water.WetCells().Select(c => (SExpr)new SList([N(c.Cell), N(c.H), N(c.Qx), N(c.Qz)])).ToList() is { Count: > 0 } wet)
            items.Add(L("water", wet));
        return new SList(items);
    }

    /// <summary>
    /// A patch rebuilt from <see cref="Save"/>'s form over <paramref name="ground"/> (whose own heights and soils are already the saved ones).
    /// A save made before #72's 2026-10-08 rule holds <c>(carry (k original ceiling) …)</c> in place of <c>(original …)</c>: its
    /// original heights are read and its carry ceilings, which nothing uses now, are dropped.
    /// </summary>
    internal static WorkedGround Load(Terrain ground, SList saved)
    {
        double[] Nums(string f) => saved.Field(f)?.Items.Skip(1).Select(e => e is SNumber n ? n.Value : throw new FormatException($"a worked patch's ({f} …) holds only numbers")).ToArray()
            ?? throw new FormatException($"a worked patch needs ({f} …)");
        var box = Nums("box").Select(x => (int)x).ToArray();
        if (box.Length != 4 || box[0] < 0 || box[1] < 0 || box[2] <= box[0] || box[3] <= box[1] || box[2] >= ground.Nx || box[3] >= ground.Nz)
            throw new FormatException("a worked patch's (box I0 J0 I1 J1) lies outside its map");
        var w = new WorkedGround(ground, box[0], box[1], box[2], box[3]);
        var heights = Nums("heights");
        if (heights.Length != w.Fine.Heights.Length) throw new FormatException($"a worked patch has {heights.Length} heights for {w.Fine.Heights.Length} nodes");
        Array.Copy(heights, w.Fine.Heights, heights.Length);
        Array.Copy(heights, w.Original, heights.Length);
        Array.Fill(w.Floor, double.NegativeInfinity);
        Array.Fill(w.Fine.Loose, false);
        int Node(double k) => k >= 0 && k < heights.Length && k == Math.Floor(k) ? (int)k : throw new FormatException("a worked patch names a node it does not have");
        var originals = saved.Field("original") ?? saved.Field("carry") ?? throw new FormatException("a worked patch needs (original …)");
        foreach (var e in originals.Items.Skip(1).OfType<SList>())
        {
            if (e.Items is not [SNumber k, SNumber original, ..] || e.Items.Count > 3 || e.Items.Count == 3 && e.Items[2] is not SNumber)
                throw new FormatException("an original entry is (NODE LEVEL)");
            w.Original[Node(k.Value)] = original.Value;
        }
        foreach (var e in saved.Field("rock")!.Items.Skip(1).OfType<SList>())
        {
            if (e.Items is not [SNumber k, SNumber level]) throw new FormatException("a rock entry is (NODE LEVEL)");
            w.Floor[Node(k.Value)] = level.Value;
        }
        int at = 0;
        foreach (var e in saved.Field("soil")!.Items.Skip(1).OfType<SList>())
        {
            if (e.Items is not [SNumber value, SNumber count] || value.Value < 0 || value.Value >= ground.Soils.Count || at + count.Value > heights.Length)
                throw new FormatException("a soil run is (SOIL COUNT) over the patch's nodes");
            for (int n = 0; n < (int)count.Value; n++) w.Fine.Soil[at++] = (int)value.Value;
        }
        if (at != heights.Length) throw new FormatException("a worked patch's soil runs do not cover its nodes");
        foreach (var e in saved.Field("loose")!.Items.Skip(1)) w.Fine.Loose[Node(e is SNumber k ? k.Value : -1)] = true;
        w.Dug = Nums("dug")[0]; w.Dumped = Nums("dumped")[0];
        w.Fine.Touch();
        if (saved.Field("water") is { } water)
        {
            var grid = w.Water = new PatchWater(w);
            int cells = grid.Bed.Nx * grid.Bed.Nz;
            foreach (var e in water.Items.Skip(1))
            {
                if (e is not SList { Items: [SNumber k, SNumber d, SNumber qx, SNumber qz] } || k.Value < 0 || k.Value >= cells || k.Value != Math.Floor(k.Value))
                    throw new FormatException("a water entry is (CELL DEPTH QX QZ) over the patch's grid");
                grid.Water.SetCell((int)k.Value, d.Value, qx.Value, qz.Value);
            }
        }
        return w;
    }
}

public sealed partial class Terrain
{
    /// <summary>The worked patches as a save holds them: <c>(worked 1 (patch …) …)</c>, or null if the rover has dug nowhere.</summary>
    public SList? SaveWorked() => Worked.Count == 0 ? null
        : new SList([new SSymbol("worked"), new SNumber(WorkedGround.SaveVersion), .. Worked.Select(w => (SExpr)w.Save())]);

    /// <summary>Puts back the patches a save holds in place of any there are now; a view sees <see cref="WorkedPatches"/> change and rebuilds its meshes and bodies.</summary>
    public void LoadWorked(SList saved)
    {
        if (saved.Items.ElementAtOrDefault(1) is not SNumber v || v.Value != WorkedGround.SaveVersion)
            throw new FormatException($"the save's worked ground is version {(saved.Items.ElementAtOrDefault(1) as SNumber)?.Value}; this game reads version {WorkedGround.SaveVersion}");
        var made = saved.Items.Skip(2).OfType<SList>().Where(e => e.Head == "patch").Select(e => WorkedGround.Load(this, e)).ToList();
        Worked.Clear();
        Worked.AddRange(made);
        WorkedPatches++;
    }
}
