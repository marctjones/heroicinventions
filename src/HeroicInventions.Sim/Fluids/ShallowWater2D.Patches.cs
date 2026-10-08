namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// The water on the rover's worked ground (issue #200): over each patch of fine ground a nested grid of its fine cells
/// (<see cref="PatchWater"/>), stepped with the map's coarse grid in the same substeps.
///
/// Where a coarse cell meets the fine grid, its face is K fine faces (a 5 m face, twenty of 0.25 m). Across each the flux is
/// the same hydrostatically reconstructed HLL flux as everywhere else, between the coarse cell's water on its bed and the fine
/// cell's on its own: the fine cell takes it as its face's flux, and the coarse cell takes the mean of the K as its face's.
/// What leaves one side is what arrives on the other, K · (C/K) = C, so not a drop is made or lost at the seam (to rounding);
/// and a pond at rest stays at rest across it, each side's pressure being its own depth's. The substep is the shorter of the
/// two grids' stable steps; the fine one's counts the coarse cells round the patch, whose water the fine cells meet.
/// </summary>
public sealed partial class ShallowWater2D
{
    [NonSerialized] private bool[]? _inactive;                    // coarse cells under a patch's fine water: theirs is held there
    [NonSerialized] private readonly List<PatchWater> _nested = [];
    [NonSerialized] private int _syncedPatches;

    /// <summary>The fine water on the ground's worked patches, one grid each.</summary>
    public IReadOnlyList<PatchWater> Patches { get { Sync(); return _nested; } }

    /// <summary>Whether a map cell's water is held by a patch's fine grid (its own depth is then 0).</summary>
    public bool UnderPatch(int cell) => _inactive is { } off && off[cell];

    /// <summary>
    /// Brings the nested grids into line with the ground's patches after one is made, merged or loaded: each patch gets its
    /// grid (a merged or loaded one has brought its water with it), the coarse cells under them are marked, and whatever
    /// water those coarse cells held is poured into the fine cells under them, its depth and current with it.
    /// </summary>
    private void Sync()
    {
        if (Ground.WorkedPatches == _syncedPatches) return;
        _syncedPatches = Ground.WorkedPatches;
        _nested.Clear();
        _inactive = null;
        foreach (var w in Ground.Worked)
        {
            var p = w.Water ??= new PatchWater(w);
            if (p.Ci == 0 || p.Cj == 0) continue;
            _inactive ??= new bool[Ground.Count];
            _nested.Add(p);
            p.Water.Gravity = Gravity;
            p.Water.Manning = Manning;
            var fine = p.Water;
            for (int cj = 0; cj < p.Cj; cj++)
                for (int ci = 0; ci < p.Ci; ci++)
                {
                    int c = p.I0 + ci + (p.J0 + cj) * Ground.Nx;
                    _inactive[c] = true;
                    if (_h[c] == 0 && _qx[c] == 0 && _qz[c] == 0) continue;
                    // K² fine cells of (C/K)² each hold what one coarse cell of C² held, at the same depth
                    for (int j = 0; j < p.K; j++)
                        for (int i = 0; i < p.K; i++)
                        {
                            int f = p.FineCell(ci, cj, i, j);
                            fine._h[f] += _h[c]; fine._qx[f] += _qx[c]; fine._qz[f] += _qz[c];
                        }
                    _h[c] = _qx[c] = _qz[c] = 0;
                }
        }
        if (_nested.Count == 0) _inactive = null;
    }

    /// <summary>Runs <paramref name="act"/> on each coarse cell under the patch's grid.</summary>
    private void ForUnder(PatchWater p, Action<int> act)
    {
        for (int cj = 0; cj < p.Cj; cj++)
            for (int ci = 0; ci < p.Ci; ci++) act(p.I0 + ci + (p.J0 + cj) * Ground.Nx);
    }

    /// <summary>The fine cell a world point is over, if it is over a patch's grid.</summary>
    private (ShallowWater2D Grid, int Cell)? FineAt(double x, double z)
    {
        Sync();
        foreach (var p in _nested)
            if (p.Bed.CellAt(x, z) is { } c) return (p.Water, c);
        return null;
    }

    /// <summary>Before a step: beds resampled where the rover has dug since, and the nested grids falling and dragging as the map's water does.</summary>
    private void PrepareNested()
    {
        foreach (var p in _nested)
        {
            if (p.Stale) p.Resample();
            p.Water.Gravity = Gravity;
            p.Water.Manning = Manning;
        }
    }

    private double CoupledStableStep()
    {
        double sub = StableStep();
        foreach (var p in _nested) sub = Math.Min(sub, p.Water.StableStep(RingSpeed(p)));
        return sub;
    }

    /// <summary>The fastest wave among the coarse cells that border a patch's grid (m/s): the fine cells along its edge meet their water.</summary>
    private double RingSpeed(PatchWater p)
    {
        double fastest = 0;
        void See(int c)
        {
            double h = _h[c];
            if (h <= ShallowWater.Dry) return;
            double s = Math.Max(Math.Abs(_qx[c]), Math.Abs(_qz[c])) / h + Math.Sqrt(Gravity * h);
            if (s > fastest) fastest = s;
        }
        int nx = Ground.Nx;
        for (int cj = 0; cj < p.Cj; cj++) { See(p.I0 - 1 + (p.J0 + cj) * nx); See(p.I0 + p.Ci + (p.J0 + cj) * nx); }
        for (int ci = 0; ci < p.Ci; ci++) { See(p.I0 + ci + (p.J0 - 1) * nx); See(p.I0 + ci + (p.J0 + p.Cj) * nx); }
        return fastest;
    }

    private void CoupledSubstep(double dt)
    {
        _pendingDt = dt;
        Faces();
        foreach (var p in _nested)
        {
            p.Water._pendingDt = dt;
            p.Water.Faces();
            Couple(p);
        }
        Update(dt);
        foreach (var p in _nested)
        {
            var fine = p.Water;
            fine.Update(dt);
            // the fine grid's soaking and clipping go on this ledger
            Infiltrated += fine.Infiltrated; fine.Infiltrated = 0;
            Clipped += fine.Clipped; fine.Clipped = 0;
        }
        if (_erodes ??= Ground.Soils.Any(s => s.Erodible)) MoveSediment(dt);
    }

    /// <summary>
    /// The faces along a patch's grid: each fine face between a coarse cell and a fine one gets the flux between them, and the
    /// coarse face they make up the mean of its K (the grid's closed-edge faces, worked out by its own <see cref="Faces"/>,
    /// are overwritten).
    /// </summary>
    private void Couple(PatchWater p)
    {
        var fine = p.Water;
        int nx = Ground.Nx, nz = Ground.Nz, fx = fine.Ground.Nx, fz = fine.Ground.Nz, k = p.K;
        var zc = Ground.Heights; var zf = fine.Ground.Heights;
        double g = Gravity;
        // west and east: x-faces, the normal discharge qx, the tangential qz
        for (int cj = 0; cj < p.Cj; cj++)
        {
            int j = p.J0 + cj;
            for (int side = 0; side < 2; side++)
            {
                bool west = side == 0;
                int c = (west ? p.I0 - 1 : p.I0 + p.Ci) + j * nx;
                int fc = (west ? p.I0 : p.I0 + p.Ci) + j * (nx + 1);
                double sm = 0, sl = 0, sr = 0, st = 0;
                for (int b = cj * k; b < (cj + 1) * k; b++)
                {
                    int cell = (west ? 0 : fx - 1) + b * fx, ff = (west ? 0 : fx) + b * (fx + 1);
                    var (m, pl, pr, t) = west
                        ? Flux(zc[c], _h[c], _qx[c], _qz[c], zf[cell], fine._h[cell], fine._qx[cell], fine._qz[cell], g)
                        : Flux(zf[cell], fine._h[cell], fine._qx[cell], fine._qz[cell], zc[c], _h[c], _qx[c], _qz[c], g);
                    (fine._massX[ff], fine._momX[ff], fine._momXr[ff], fine._tanX[ff]) = (m, pl, pr, t);
                    sm += m; sl += pl; sr += pr; st += t;
                }
                (_massX[fc], _momX[fc], _momXr[fc], _tanX[fc]) = (sm / k, sl / k, sr / k, st / k);
            }
        }
        // south and north: z-faces, the normal discharge qz, the tangential qx
        for (int ci = 0; ci < p.Ci; ci++)
        {
            int i = p.I0 + ci;
            for (int side = 0; side < 2; side++)
            {
                bool south = side == 0;
                int c = i + (south ? p.J0 - 1 : p.J0 + p.Cj) * nx;
                int fc = (south ? p.J0 : p.J0 + p.Cj) + i * (nz + 1);
                double sm = 0, sl = 0, sr = 0, st = 0;
                for (int a = ci * k; a < (ci + 1) * k; a++)
                {
                    int cell = a + (south ? 0 : fz - 1) * fx, ff = (south ? 0 : fz) + a * (fz + 1);
                    var (m, pl, pr, t) = south
                        ? Flux(zc[c], _h[c], _qz[c], _qx[c], zf[cell], fine._h[cell], fine._qz[cell], fine._qx[cell], g)
                        : Flux(zf[cell], fine._h[cell], fine._qz[cell], fine._qx[cell], zc[c], _h[c], _qz[c], _qx[c], g);
                    (fine._massZ[ff], fine._momZ[ff], fine._momZr[ff], fine._tanZ[ff]) = (m, pl, pr, t);
                    sm += m; sl += pl; sr += pr; st += t;
                }
                (_massZ[fc], _momZ[fc], _momZr[fc], _tanZ[fc]) = (sm / k, sl / k, sr / k, st / k);
            }
        }
    }

    /// <summary>The flux across one face, left to right: mass, each side's momentum, and the sideways current the water carries across.</summary>
    private static (double Mass, double MomL, double MomR, double Tangential) Flux(
        double zL, double hL, double qnL, double qtL, double zR, double hR, double qnR, double qtR, double g)
    {
        if (hL <= ShallowWater.Dry && hR <= ShallowWater.Dry) return (0, 0, 0, 0);
        var (m, pl, pr) = ShallowWater.Face(zL, hL, ShallowWater.Velocity(hL, qnL), zR, hR, ShallowWater.Velocity(hR, qnR), g);
        return (m, pl, pr, m * (m >= 0 ? ShallowWater.Velocity(hL, qtL) : ShallowWater.Velocity(hR, qtR)));
    }

    // ------------------------------------------------------------------ saving with the patch (#200)

    internal IEnumerable<(int Cell, double H, double Qx, double Qz)> WetCells()
    {
        for (int c = 0; c < _h.Length; c++)
            if (_h[c] != 0 || _qx[c] != 0 || _qz[c] != 0) yield return (c, _h[c], _qx[c], _qz[c]);
    }

    internal void SetCell(int c, double h, double qx, double qz) => (_h[c], _qx[c], _qz[c]) = (h, qx, qz);

    internal (double H, double Qx, double Qz) CellState(int c) => (_h[c], _qx[c], _qz[c]);
}
