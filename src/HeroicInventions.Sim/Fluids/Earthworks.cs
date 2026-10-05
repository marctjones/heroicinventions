namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// Earthworks on a map (issue #44): digging into the heightfield and
/// heaping the spoil, and the ground settling afterwards by Mohr–Coulomb.
///
/// Soil fails in shear and then flows like a granular material. Every cell
/// is either intact (its soil's cohesion c holds it) or loose (spoil, or
/// ground that has slumped: no cohesion). Between two neighbouring cells,
/// across and diagonally, the ground stands if
/// <list type="bullet">
/// <item>the higher is intact and the step down is no taller than the
/// soil's critical height, 4c/γ · tan(45° + φ/2): a cut face holds up by
/// its cohesion (Terzaghi); or</item>
/// <item>the slope between them is no steeper than the angle of repose,
/// tan φ.</item>
/// </list>
/// Otherwise it gives: a cohesive wall past its height fails and turns
/// loose, and loose soil slides down, half the excess over repose at a time,
/// until everything stands. Volume is kept exactly: soil only moves between
/// cells of the map, never off it.
/// </summary>
public sealed partial class Terrain
{
    private bool[]? _loose;
    /// <summary>Whether each cell's top is loose soil (spoil, or a wall that has slumped): no cohesion.</summary>
    public bool[] Loose => _loose ??= new bool[Count];

    /// <summary>Goes up by one each time the ground's shape changes, so a view knows to redraw it.</summary>
    public int Version { get; private set; }

    public SoilSpec SoilOf(int cell) => Soils[Soil[cell]];

    /// <summary>Marks the ground's shape as changed (water moving its bed, #53).</summary>
    public void Touch() => Version++;

    /// <summary>Takes <paramref name="depth"/> m off a cell's top; returns the m³ taken.</summary>
    public double Dig(int cell, double depth)
    {
        if (depth <= 0) return 0;
        Heights[cell] -= depth;
        Version++;
        return depth * Cell * Cell;
    }

    /// <summary>Tips <paramref name="m3"/> of loose soil onto a cell.</summary>
    public void Heap(int cell, double m3)
    {
        if (m3 <= 0) return;
        Heights[cell] += m3 / (Cell * Cell);
        Loose[cell] = true;
        Version++;
    }

    /// <summary>
    /// Lets the whole map settle (issue #54): every face taller than its
    /// soil holds fails, every loose slope steeper than repose slides, until
    /// the ground stands. A map that asks for it (#:settle) settles as it is
    /// loaded; what the slides bury, stays buried.
    /// </summary>
    public (int Failures, int Passes) Settle(double gravity) => Relax(0, 0, Nx - 1, Nz - 1, gravity, 20000);

    /// <summary>Whether the map settles as it is loaded (a slope or cliff too steep to stand collapses first).</summary>
    public bool SettleOnLoad { get; init; }

    private static readonly (int Di, int Dj, double Run)[] Neighbours =
        [(1, 0, 1), (0, 1, 1), (1, 1, Math.Sqrt(2)), (1, -1, Math.Sqrt(2))];

    /// <summary>
    /// Lets the ground settle within cells [i0, i1] × [j0, j1] (clamped to the
    /// map): walls past their critical height fail, loose soil slides to its
    /// angle of repose. Returns how many cut faces failed. Stops when nothing
    /// moves more than a micron, or after <paramref name="maxPasses"/>.
    /// </summary>
    public (int Failures, int Passes) Relax(int i0, int j0, int i1, int j1, double gravity, int maxPasses = 5000)
    {
        i0 = Math.Max(0, i0); j0 = Math.Max(0, j0); i1 = Math.Min(Nx - 1, i1); j1 = Math.Min(Nz - 1, j1);
        // a soil with rock in it leaves boulders where a face of it fails (#88): note the ground as it stood, and which faces fail
        if (!Soils.Any(s => s.Boulders is { Fraction: > 0 })) return Passes(i0, j0, i1, j1, gravity, maxPasses, null);
        var before = (double[])Heights.Clone();
        var failed = new HashSet<int>();
        var made = new Dictionary<int, int>();   // boulders made so far in this collapse, by soil
        var (failures, passes) = Passes(i0, j0, i1, j1, gravity, maxPasses, failed);
        // the boulders' share comes out of the debris, which then props the face a little less: let it settle again,
        // and take out the share of anything more that comes down (a few rounds: each takes out less)
        var toLay = new List<BoulderSpec>();
        for (int round = 0; round < 4 && failed.Count > 0 && TakeBoulders(before, failed, made, toLay); round++)
        {
            var (f, p) = Passes(i0, j0, i1, j1, gravity, maxPasses, failed);
            failures += f; passes += p;
        }
        if (toLay.Count > 0) LayBoulders(before, toLay);   // on the ground as it has settled
        if (failed.Count > 0) Collapsed += failed.Sum(c => Math.Max(0, before[c] - Heights[c])) * Cell * Cell;
        return (failures, passes);
    }

    private (int Failures, int Passes) Passes(int i0, int j0, int i1, int j1, double gravity, int maxPasses, HashSet<int>? failed)
    {
        int failures = 0, pass = 0;
        var loose = Loose;
        for (; pass < maxPasses; pass++)
        {
            double moved = 0;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    foreach (var (di, dj, run) in Neighbours)
                    {
                        int ni = i + di, nj = j + dj;
                        if (ni < i0 || nj < j0 || ni > i1 || nj > j1) continue;
                        int a = i + j * Nx, b = ni + nj * Nx;
                        if (Heights[a] < Heights[b]) (a, b) = (b, a);            // a is the higher
                        double drop = Heights[a] - Heights[b];
                        var soil = SoilOf(a);
                        double repose = soil.Friction * run * Cell;
                        if (drop <= repose + 1e-9) continue;
                        if (!loose[a])
                        {
                            if (drop <= soil.CriticalHeight(gravity) + 1e-9) continue;   // the cut face holds
                            loose[a] = true;                                           // it fails
                            failures++;
                            failed?.Add(a);
                        }
                        double shift = (drop - repose) / 2;
                        Heights[a] -= shift;
                        Heights[b] += shift;
                        loose[b] = true;
                        moved = Math.Max(moved, shift);
                    }
            if (moved < 1e-6) break;
            Version++;
        }
        return (failures, pass);
    }
}
