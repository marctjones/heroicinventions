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
/// <see cref="Margin"/> m inside it. Nothing else of the map changes: the coarse heights, soils and water are as they were,
/// so a world where no one digs is untouched.
///
/// Free effort must not make energy (#72). Every cell remembers the level its top soil may be carried to
/// (<see cref="Terrain.Ceiling"/>): ground never covered is the surface itself; spoil is the lower of the level it was dug from and
/// the one it landed on, and soil that slides down keeps the lower of the two. A bucket's load takes the mean of what it
/// scraped, and is dumped only where the surface is no higher than that, so soil can be moved down or along, never up, however
/// it is passed from heap to heap.
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
    /// <summary>The lowest each node may be cut: bedrock is never cut, and ground that was bedrock keeps its level under any spoil on it.</summary>
    public double[] Floor { get; }

    /// <summary>m³ taken out of the patch and put back into it since it was made (the totals of patches merged into it included).</summary>
    public double Dug { get; private set; }
    public double Dumped { get; private set; }

    public int Nx => Fine.Nx;
    public int Nz => Fine.Nz;
    public int Version => Fine.Version;
    public double NodeX(int i) => MinX + i * FineCell;
    public double NodeZ(int j) => MinZ + j * FineCell;
    private double Area => Fine.Cell * Fine.Cell;

    private bool Hard(int node) => Fine.SoilOf(node).Cohesion >= 1e7;

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
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                double x = MinX + i * cf, z = MinZ + j * cf;
                heights[i + j * nx] = ground.CoarseSurfaceAt(x, z);
                soil[i + j * nx] = ground.Soil[ground.CellAt(x, z)!.Value];
            }
        Fine = new Terrain
        {
            Name = ground.Name + "-worked", X0 = MinX - cf / 2, Z0 = MinZ - cf / 2, Cell = cf, Nx = nx, Nz = nz,
            Heights = heights, Soil = soil,
            Soils = ground.Soils.Select(s => s with { Boulders = null }).ToList(),   // a bucketful leaves no boulders
            OpenEdges = false, Roughness = ground.Roughness,
        };
        Fine.Ceiling = Enumerable.Repeat(double.PositiveInfinity, nx * nz).ToArray();
        Original = (double[])heights.Clone();
        Floor = new double[nx * nz];
        for (int k = 0; k < Floor.Length; k++) Floor[k] = Hard(k) ? heights[k] : double.NegativeInfinity;
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
                    w.Fine.Ceiling![to] = p.Fine.Ceiling![from];
                    w.Original[to] = p.Original[from];
                    w.Floor[to] = p.Floor[from];
                }
            w.Dug += p.Dug; w.Dumped += p.Dumped;
        }
        w.Fine.Touch();
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

    private double Eff(int k) => Math.Min(Fine.Ceiling![k], Fine.Heights[k]);

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

    /// <summary>What a bucket took: its volume (m³), the level it may be carried to, and the soil.</summary>
    public readonly record struct Scooped(double Volume, double Ceiling, int Soil, double MeanSurface);

    /// <summary>
    /// A bucket scrapes the ground within <see cref="DigRadius"/> of a point to one depth, deeper where it must to fill, but not
    /// below bedrock (a node of bedrock gives nothing) and not past <paramref name="volume"/> m³. Returns what it took, or null
    /// if there was nothing to take. The level the load may be carried to is the mean, weighted by what each node gave, of the
    /// levels their soil may be carried to.
    /// </summary>
    public Scooped? Scoop(double x, double z, double volume)
    {
        var nodes = Within(x, z, DigRadius);
        if (nodes.Count == 0 || volume <= 0) return null;
        var avail = nodes.Select(k => Math.Max(0, Fine.Heights[k] - Floor[k])).ToArray();
        double depth = FillDepth(avail, volume / Area);
        double taken = 0, level = 0;
        for (int n = 0; n < nodes.Count; n++)
        {
            double d = Math.Min(depth, avail[n]);
            if (d <= 0) continue;
            int k = nodes[n];
            level += d * Eff(k);
            taken += d;
            Fine.Heights[k] -= d;
        }
        if (taken <= 0) return null;
        Fine.Touch();
        double m3 = taken * Area;
        Dug += m3;
        return new Scooped(m3, level / taken, Fine.Soil[Node(x, z)], nodes.Average(k => Fine.Heights[k]));
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
    /// Tips <paramref name="m3"/> of loose soil onto the ground within <see cref="PourRadius"/> of a point, level over its nodes. It is
    /// done only if the ground there is no higher than <paramref name="ceiling"/> (the level the load may be carried to);
    /// otherwise nothing changes. Returns the surface it landed on, or null if refused.
    /// </summary>
    public double? Pour(double x, double z, double m3, int soil, double ceiling)
    {
        var nodes = Within(x, z, PourRadius);
        if (nodes.Count == 0 || m3 <= 0) return null;
        double surface = nodes.Average(k => Fine.Heights[k]);
        if (surface > ceiling + 1e-9) return null;
        double rise = m3 / (nodes.Count * Area);
        foreach (int k in nodes)
        {
            Fine.Ceiling![k] = Math.Min(Eff(k), ceiling);
            Fine.Heights[k] += rise;
            Fine.Loose[k] = true;
            if (Hard(k)) Fine.Soil[k] = soil;   // spoil on bedrock is soil, not rock (the rock under it is still there: Floor)
        }
        Fine.Touch();
        Dumped += m3;
        return surface;
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
                Fine.Ceiling![k] = double.PositiveInfinity;
                Fine.Loose[k] = false;
                Floor[k] = Hard(k) ? Fine.Heights[k] : double.NegativeInfinity;
            }
        Fine.Touch();
    }
}
