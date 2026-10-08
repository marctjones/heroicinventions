namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// The water on a patch of worked ground (issue #200): a shallow-water grid of the patch's own fine cells, nested in the map's
/// coarse one, so a trench the rover digs holds water and a channel it cuts carries it.
///
/// The grid covers the map cells wholly inside the patch, <see cref="I0"/>..I0+Ci−1 by <see cref="J0"/>..J0+Cj−1, each cut into
/// K × K cells (0.25 m on the crater's 5 m map): its edges lie on the map's own cell faces, so what crosses a face between a
/// coarse cell and the fine ones is one number on both sides and the water is counted once. The patch's outer half-cells
/// (the ring the rover never works, <see cref="WorkedGround.Margin"/>) stay with the coarse grid. Each fine cell's bed is the
/// worked ground at its centre (the mean of its four nodes), its soil the node's there: it soaks water away at that soil's
/// rate, as a coarse cell does.
///
/// The coarse cells under the grid hold no water while it is there (<see cref="ShallowWater2D"/> moves what they held into it);
/// the water lives on the patch, so it is merged with patches and saved with them.
/// </summary>
public sealed class PatchWater
{
    public WorkedGround Patch { get; }
    /// <summary>The first map cell the grid covers, and how many it covers each way.</summary>
    public int I0 { get; }
    public int J0 { get; }
    public int Ci { get; }
    public int Cj { get; }
    /// <summary>Fine cells to a map cell's side.</summary>
    public int K { get; }
    /// <summary>The fine cells' beds and soils, as a terrain the nested solver runs on.</summary>
    public Terrain Bed { get; }
    public ShallowWater2D Water { get; }
    private int _bedVersion;

    public PatchWater(WorkedGround patch)
    {
        Patch = patch;
        var map = patch.Base;
        K = patch.K;
        (I0, J0) = (patch.Bi0 + 1, patch.Bj0 + 1);
        (Ci, Cj) = (Math.Max(0, patch.Bi1 - patch.Bi0 - 1), Math.Max(0, patch.Bj1 - patch.Bj0 - 1));
        int nx = Math.Max(1, Ci * K), nz = Math.Max(1, Cj * K);
        Bed = new Terrain
        {
            Name = patch.Fine.Name + "-water", X0 = map.X0 + I0 * map.Cell, Z0 = map.Z0 + J0 * map.Cell, Cell = map.Cell / K,
            Nx = nx, Nz = nz, Heights = new double[nx * nz], Soil = new int[nx * nz], Soils = patch.Fine.Soils,
            OpenEdges = false, Roughness = map.Roughness,
        };
        Resample();
        Water = new ShallowWater2D(Bed, nested: true);
    }

    /// <summary>Whether the patch's ground has changed since the beds were sampled.</summary>
    public bool Stale => _bedVersion != Patch.Version;

    /// <summary>
    /// Each fine cell's bed and soil from the worked ground as it now stands. A cell dug under water keeps its depth of water: the
    /// volume is kept, and the surface drops with the bed until the water runs level again. A cell whose bed rises under water
    /// pushes its water aside (<see cref="Displace"/>).
    /// </summary>
    public void Resample()
    {
        var fine = Patch.Fine;
        var before = Water is null ? null : (double[])Bed.Heights.Clone();
        for (int j = 0; j < Bed.Nz; j++)
            for (int i = 0; i < Bed.Nx; i++)
            {
                double x = Bed.CellX(i), z = Bed.CellZ(j);
                int c = i + j * Bed.Nx;
                Bed.Heights[c] = fine.CoarseHeightAt(x, z);
                Bed.Soil[c] = fine.Soil[fine.CellAt(x, z) ?? 0];
            }
        if (before is not null) Displace(before);
        _bedVersion = Patch.Version;
        Bed.Touch();
    }

    /// <summary>m³ that soil tipped under water has pushed aside since the grid was made, and of that, m³ that had nowhere to go
    /// and was left on the soil where it stood (a puddle the soil filled entirely).</summary>
    public double Displaced { get; private set; }
    public double Perched { get; private set; }

    /// <summary>
    /// Soil tipped into water (#72): where a cell's bed has risen under water, the water's surface there stays where it was and the
    /// water the soil now fills is pushed aside: onto the top of the rest of the same water (the wet cells joined to it, cell to
    /// cell, whose bed did not rise and whose surface is no higher than its own), spread evenly over them, as the pond's level rises
    /// when a stone goes in. Keeping each cell's depth instead would carry its whole column up on the soil, so that soil tipped into
    /// shallow water lifted the water onto the heap, above the pond: measured, the first 0.2 m³ bucket into 0.15 m of still water gave
    /// it 111 J at once, where the soil sinking from the surface gives up 89 J (DirtEnergyTests). Pushed aside, no parcel of water goes
    /// higher than the surface it joins (within <see cref="Level"/>), and the water gains what lifting the water the soil displaced to
    /// that surface takes, two thirds of what the soil gives up (61 J of 89). A puddle the soil fills
    /// entirely, with no water left beside it to take what it pushes out, keeps its water on top of the soil (<see cref="Perched"/>).
    /// Volume is kept exactly; currents keep their momentum, so no cell's water speeds up.
    /// </summary>
    /// <summary>m: a surface counts as no higher than another within this, so a still pond's ripples don't shut most of it out.</summary>
    private const double Level = 0.005;

    private void Displace(double[] before)
    {
        var water = Water;
        int n = Bed.Nx * Bed.Nz;
        double[]? pushed = null;
        var depth = new double[n];
        for (int c = 0; c < n; c++)
        {
            double h = depth[c] = water.CellState(c).H;
            if (h <= ShallowWater.Dry || Bed.Heights[c] <= before[c]) continue;
            double keep = Math.Max(0, before[c] + h - Bed.Heights[c]);
            if (h - keep <= 0) continue;
            (pushed ??= new double[n])[c] = h - keep;
            var (_, qx, qz) = water.CellState(c);
            water.SetCell(c, keep, keep > ShallowWater.Dry ? qx * keep / h : 0, keep > ShallowWater.Dry ? qz * keep / h : 0);
        }
        if (pushed is null) return;
        var seen = new int[n];
        int mark = 0;
        var queue = new Queue<int>();
        var takers = new List<int>();
        for (int src = 0; src < n; src++)
        {
            if (pushed[src] <= 0) continue;
            double top = before[src] + depth[src];
            // the same water: wet cells joined to it, side to side
            mark++;
            takers.Clear();
            queue.Clear();
            queue.Enqueue(src);
            seen[src] = mark;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                if (Bed.Heights[c] <= before[c] && before[c] + depth[c] <= top + Level) takers.Add(c);
                int i = c % Bed.Nx, j = c / Bed.Nx;
                foreach (var (ni, nj) in new[] { (i - 1, j), (i + 1, j), (i, j - 1), (i, j + 1) })
                {
                    if (ni < 0 || nj < 0 || ni >= Bed.Nx || nj >= Bed.Nz) continue;
                    int m = ni + nj * Bed.Nx;
                    if (seen[m] == mark || depth[m] <= ShallowWater.Dry) continue;
                    seen[m] = mark;
                    queue.Enqueue(m);
                }
            }
            double v = pushed[src];
            Displaced += v * Bed.Cell * Bed.Cell;
            if (takers.Count == 0)
            {
                var (h, qx, qz) = water.CellState(src);
                water.SetCell(src, h + v, qx, qz);
                Perched += v * Bed.Cell * Bed.Cell;
                continue;
            }
            double each = v / takers.Count;   // the cells are all one size: an even depth on each
            foreach (int c in takers)
            {
                var (h, qx, qz) = water.CellState(c);
                water.SetCell(c, h + each, qx, qz);
            }
        }
    }

    /// <summary>The fine cell (i, j) of map cell (ci, cj) of the grid (both counted from the grid's corner).</summary>
    internal int FineCell(int ci, int cj, int i, int j) => ci * K + i + (cj * K + j) * Bed.Nx;
}
