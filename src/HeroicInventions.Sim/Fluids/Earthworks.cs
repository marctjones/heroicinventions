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

    /// <summary>
    /// For ground the rover works (#63), null elsewhere: the highest level (m) the soil on top of each cell may be carried
    /// to, +infinity for ground never covered with spoil (then it is the surface itself). Soil that is dumped, or slides down,
    /// gets the lower of the levels it came from and the one it lands on, so a mound remembers where its soil was dug.
    /// </summary>
    public double[]? Ceiling { get; set; }

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

    /// <summary>
    /// How fast settling plays out, in relaxation passes a second of simulated time (issue #61): each pass lets
    /// failed ground slide one cell, so a slide's front moves <c>SettleRate · Cell</c> metres a second, and the
    /// collapse is watched, not skipped. 0 settles everything on the first tick, as #54 does.
    /// </summary>
    public double SettleRate { get; init; }

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
        var (failures, passes) = Passes(i0, j0, i1, j1, gravity, maxPasses, failed);
        var (f, p) = FinishCollapse(before, failed, new Dictionary<int, int>(), i0, j0, i1, j1, gravity, maxPasses);
        return (failures + f, passes + p);
    }

    /// <summary>
    /// What follows a collapse that left boulders (#88). The boulders' share comes out of the debris, which then
    /// props the face a little less: the ground settles again, and takes out the share of anything more that comes
    /// down (a few rounds: each takes out less); the boulders are laid on the ground as it ends up, and the volume
    /// the failed faces lost is written down. <paramref name="made"/> counts the boulders made so far, by soil.
    /// </summary>
    private (int Failures, int Passes) FinishCollapse(double[] before, HashSet<int> failed, Dictionary<int, int> made,
                                                      int i0, int j0, int i1, int j1, double gravity, int maxPasses)
    {
        int failures = 0, passes = 0;
        var toLay = new List<BoulderSpec>();
        for (int round = 0; round < 4 && failed.Count > 0 && Timed("collapse-take", () => TakeBoulders(before, failed, made, toLay)); round++)
        {
            long t = StepProfile.Start();
            var (f, p) = Passes(i0, j0, i1, j1, gravity, maxPasses, failed);
            StepProfile.Stop("collapse-passes", t);
            failures += f; passes += p;
        }
        if (toLay.Count > 0) Timed("collapse-lay", () => { LayBoulders(before, toLay); return true; });   // on the ground as it has settled
        if (failed.Count > 0) Collapsed += failed.Sum(c => Math.Max(0, before[c] - Heights[c])) * Cell * Cell;
        return (failures, passes);
    }

    private static bool Timed(string name, Func<bool> f) { long t = StepProfile.Start(); bool r = f(); StepProfile.Stop(name, t); return r; }

    /// <summary>
    /// Relaxation passes over a region until nothing moves more than a micron. The first pass looks at the whole
    /// region; each after it only at the box round what moved in the one before (#188), as <see cref="SettleStep"/>
    /// does. That is the same ground, bit for bit: a pair of cells can only need a move if one of them changed
    /// (a pair that needed none, and whose cells did not change, still needs none), and every pair with a
    /// changed cell lies inside that box widened by one, in the same raster order. A slide's finish used to
    /// scan the whole 170 x 170 crater for each of its ~250 passes (78 ms in one tick).
    /// </summary>
    private (int Failures, int Passes) Passes(int i0, int j0, int i1, int j1, double gravity, int maxPasses, HashSet<int>? failed)
    {
        int failures = 0, pass = 0;
        int bi0 = i0, bj0 = j0, bi1 = i1, bj1 = j1;
        for (; pass < maxPasses; pass++)
        {
            var changed = new Changed();
            double moved = RelaxPass(bi0, bj0, bi1, bj1, gravity, ref failures, changed, failed);
            if (moved < 1e-6) break;
            Version++;
            (bi0, bj0, bi1, bj1) = (Math.Max(i0, changed.I0 - 1), Math.Max(j0, changed.J0 - 1), Math.Min(i1, changed.I1 + 1), Math.Min(j1, changed.J1 + 1));
        }
        return (failures, pass);
    }

    private sealed class Changed
    {
        public int I0 = int.MaxValue, J0 = int.MaxValue, I1 = -1, J1 = -1;
        public void Add(int i, int j) { I0 = Math.Min(I0, i); I1 = Math.Max(I1, i); J0 = Math.Min(J0, j); J1 = Math.Max(J1, j); }
    }

    /// <summary>One pass over cells [i0, i1] × [j0, j1]: returns the most any cell moved, and the box of cells that did.</summary>
    private double RelaxPass(int i0, int j0, int i1, int j1, double gravity, ref int failures, Changed changed, HashSet<int>? failed)
    {
        var loose = Loose;
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
                    if (Ceiling is { } ceiling)
                    {
                        // soil that slides down rests lower, and may be carried no higher than where it now lies (#63)
                        ceiling[b] = Math.Min(Math.Min(ceiling[a], Heights[a]), Math.Min(ceiling[b], Heights[b]));
                    }
                    Heights[a] -= shift;
                    Heights[b] += shift;
                    loose[b] = true;
                    moved = Math.Max(moved, shift);
                    changed.Add(a % Nx, a / Nx);
                    changed.Add(b % Nx, b / Nx);
                }
        return moved;
    }

    private (int I0, int J0, int I1, int J1)? _active;
    private bool _stood;
    private double[]? _stepBefore;                 // the ground as the collapse found it, when it will leave boulders (#88)
    private HashSet<int>? _stepFailed;
    private readonly Dictionary<int, int> _stepMade = [];

    /// <summary>Whether <see cref="SettleStep"/> has found the whole map standing.</summary>
    public bool Stood => _stood;

    /// <summary>
    /// Settling a few passes at a time (issue #61), so a collapse can be watched: the first pass looks at the
    /// whole map, each after it only at the box round what moved in the one before, so a slide on a big map
    /// costs what the slide covers. Returns the faces that failed in these passes, the passes run (fewer than
    /// asked once the ground stands) and whether it now stands. The same relaxation as <see cref="Relax"/>, so
    /// stepped to the end it gives the same ground; a soil with rock in it (#88) leaves its boulders once the
    /// ground stands, as <see cref="Relax"/> does at its end.
    /// </summary>
    public (int Failures, int Passes, bool Stood) SettleStep(int passes, double gravity)
    {
        int failures = 0, ran = 0;
        bool rocky = Soils.Any(s => s.Boulders is { Fraction: > 0 });
        if (rocky && _stepBefore is null) { _stepBefore = (double[])Heights.Clone(); _stepFailed = []; }
        while (ran < passes && !_stood)
        {
            var (i0, j0, i1, j1) = _active ?? (0, 0, Nx - 1, Nz - 1);
            var changed = new Changed();
            double moved = RelaxPass(i0, j0, i1, j1, gravity, ref failures, changed, _stepFailed);
            ran++;
            if (moved < 1e-6)
            {
                _stood = true;
                if (rocky)
                {
                    long t = StepProfile.Start();
                    var (f, p) = FinishCollapse(_stepBefore!, _stepFailed!, _stepMade, 0, 0, Nx - 1, Nz - 1, gravity, 20000);
                    failures += f; ran += p;
                    StepProfile.Stop("finish-collapse", t);
                }
                break;
            }
            Version++;
            _active = (Math.Max(0, changed.I0 - 1), Math.Max(0, changed.J0 - 1), Math.Min(Nx - 1, changed.I1 + 1), Math.Min(Nz - 1, changed.J1 + 1));
        }
        return (failures, ran, _stood);
    }
}
