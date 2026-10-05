namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// Water loose on open ground (issue #37): the 2-D shallow-water equations
/// on a <see cref="Terrain"/>'s cells, by the same pieces as the 1-D channel
/// (<see cref="ShallowWater"/>): across every face between two cells the
/// flux normal to it is the channel's hydrostatically reconstructed HLL
/// flux, and the water crossing carries its own sideways speed with it
/// (taken from the side it comes from). A still pond in a hollow on a
/// hillside stays still; a front runs out over dry ground; no cell goes
/// below dry. Manning friction on the ground's roughness, and each cell's
/// soil soaks water away at its rate. Faces between two dry cells are
/// skipped, so dry ground costs next to nothing.
///
/// Every cubic metre is on the ledger: what was poured (springs, anything
/// run onto the ground, a tank spilling, #90) = what stands on it + what soaked
/// in + what ran off an open edge + what drains took into tanks (#90).
/// </summary>
public sealed partial class ShallowWater2D
{
    public Terrain Ground { get; }
    public double Gravity { get; set; } = Physics.Gravity;
    /// <summary>Manning's n; the map's own unless changed (0: frictionless).</summary>
    public double Manning { get; set; }

    // not readonly: a rebuilt world can carry them over like any part's state
    private double[] _h, _qx, _qz;
    private readonly double[] _massX, _momX, _momXr, _tanX;   // x-faces: (Nx + 1) per row
    private readonly double[] _massZ, _momZ, _momZr, _tanZ;   // z-faces: Nz + 1 per column

    public double Poured { get; private set; }      // m³ put onto the ground, all told
    public double Infiltrated { get; private set; } // m³ soaked into it
    public double Leaked { get; private set; }      // m³ run off an open edge
    public double Clipped { get; private set; }     // m³ lost to clipping a depth at zero (should stay ~0)
    public double Time { get; private set; }

    public ShallowWater2D(Terrain ground)
    {
        Ground = ground;
        Manning = ground.Roughness;
        int n = ground.Count;
        _h = new double[n]; _qx = new double[n]; _qz = new double[n];
        int fx = (ground.Nx + 1) * ground.Nz, fz = ground.Nx * (ground.Nz + 1);
        _massX = new double[fx]; _momX = new double[fx]; _momXr = new double[fx]; _tanX = new double[fx];
        _massZ = new double[fz]; _momZ = new double[fz]; _momZr = new double[fz]; _tanZ = new double[fz];
    }

    public IReadOnlyList<double> Depths => _h;
    public (double U, double V) VelocityAt(int cell) => (ShallowWater.Velocity(_h[cell], _qx[cell]), ShallowWater.Velocity(_h[cell], _qz[cell]));
    public double DepthAt(double x, double z) => Ground.CellAt(x, z) is { } c ? _h[c] : 0;
    public double SurfaceAt(int cell) => Ground.Heights[cell] + _h[cell];
    /// <summary>m³ standing on the ground.</summary>
    public double Volume => _h.Sum() * Ground.Cell * Ground.Cell;
    /// <summary>m² of ground under more than a millimetre of water.</summary>
    public double WetArea => _h.Count(h => h > 1e-3) * Ground.Cell * Ground.Cell;
    public double MaxDepth => _h.Max();

    /// <summary>Sets the water on the ground: a depth at each cell centre (x, z), m. Counted as poured.</summary>
    public void Fill(Func<double, double, double> depthAt)
    {
        double before = Volume;
        for (int j = 0; j < Ground.Nz; j++)
            for (int i = 0; i < Ground.Nx; i++)
            {
                int c = i + j * Ground.Nx;
                _h[c] = Math.Max(0, depthAt(Ground.CellX(i), Ground.CellZ(j)));
                _qx[c] = _qz[c] = 0;
            }
        Poured += Volume - before;
    }

    /// <summary>Sets the water on the ground and how it runs: a depth and a velocity (u along x, w along z) at each cell centre. Counted as poured.</summary>
    public void Fill(Func<double, double, double> depthAt, Func<double, double, (double U, double W)> velocityAt)
    {
        Fill(depthAt);
        for (int j = 0; j < Ground.Nz; j++)
            for (int i = 0; i < Ground.Nx; i++)
            {
                int c = i + j * Ground.Nx;
                var (u, w) = velocityAt(Ground.CellX(i), Ground.CellZ(j));
                (_qx[c], _qz[c]) = (_h[c] * u, _h[c] * w);
            }
    }

    /// <summary>Pours m³ onto the ground at a world point (a channel running off a machine onto it). Off the map, it runs away: false.</summary>
    public bool AddWater(double x, double z, double m3)
    {
        if (m3 <= 0) return true;
        if (Ground.CellAt(x, z) is not { } c) return false;
        _h[c] += m3 / (Ground.Cell * Ground.Cell);
        Poured += m3;
        return true;
    }

    /// <summary>
    /// Takes up to <paramref name="m3"/> of the water standing on the cell at a world point (a drain into a tank, #90),
    /// no more than is there; returns what it took, on the ledger as drained.
    /// </summary>
    public double TakeWater(double x, double z, double m3)
    {
        if (m3 <= 0 || Ground.CellAt(x, z) is not { } c) return 0;
        double area = Ground.Cell * Ground.Cell, taken = Math.Min(m3, _h[c] * area);
        if (taken <= 0) return 0;
        double left = _h[c] - taken / area;
        // what goes keeps the cell's current, so what stays runs as it did
        double keep = _h[c] > 0 ? left / _h[c] : 0;
        _h[c] = left;
        _qx[c] *= keep; _qz[c] *= keep;
        Drained += taken;
        return taken;
    }

    /// <summary>m³ taken off the ground by drains into tanks (#90), all told.</summary>
    public double Drained { get; private set; }

    /// <summary>Water poured towards the ground but off the map: on the ledger as poured and run away at once.</summary>
    public void Leak(double m3)
    {
        if (m3 <= 0) return;
        Poured += m3;
        Leaked += m3;
    }

    public void Step(double dt)
    {
        for (double t = 0; t < dt - 1e-12;)
        {
            double sub = Math.Min(dt - t, StableStep());
            foreach (var s in Ground.Sources) AddWater(s.X, s.Z, s.Flow * sub);
            Substep(sub);
            t += sub;
        }
        Time += dt;
    }

    /// <summary>The longest step the waves allow: 0.45 · cell / (fastest speed + wave speed).</summary>
    private double StableStep()
    {
        double fastest = 0;
        for (int c = 0; c < _h.Length; c++)
        {
            double h = _h[c];
            if (h <= ShallowWater.Dry) continue;
            double s = Math.Max(Math.Abs(_qx[c]), Math.Abs(_qz[c])) / h + Math.Sqrt(Gravity * h);
            if (s > fastest) fastest = s;
        }
        return fastest > 0 ? 0.45 * Ground.Cell / fastest : double.PositiveInfinity;
    }

    private void Substep(double dt)
    {
        int nx = Ground.Nx, nz = Ground.Nz;
        var z = Ground.Heights;
        double g = Gravity, dx = Ground.Cell;
        bool open = Ground.OpenEdges;
        _pendingDt = dt;

        // x-faces: face i of row j lies between cells (i-1, j) and (i, j)
        for (int j = 0; j < nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                int f = i + j * (nx + 1);
                int l = i - 1 + j * nx, r = i + j * nx;
                bool lIn = i > 0, rIn = i < nx;
                if ((!lIn || _h[l] <= ShallowWater.Dry) && (!rIn || _h[r] <= ShallowWater.Dry))
                { _massX[f] = _momX[f] = _momXr[f] = _tanX[f] = 0; continue; }
                if (lIn && rIn)
                {
                    var (m, pl, pr) = ShallowWater.Face(z[l], _h[l], ShallowWater.Velocity(_h[l], _qx[l]),
                                                        z[r], _h[r], ShallowWater.Velocity(_h[r], _qx[r]), g);
                    (_massX[f], _momX[f], _momXr[f]) = (m, pl, pr);
                    _tanX[f] = m * (m >= 0 ? ShallowWater.Velocity(_h[l], _qz[l]) : ShallowWater.Velocity(_h[r], _qz[r]));
                }
                else Edge(lIn ? l : r, lIn ? 1 : -1, _qx, _qz, out _massX[f], out _momX[f], out _momXr[f], out _tanX[f], g, open);
            }
        // z-faces: face j of column i lies between cells (i, j-1) and (i, j)
        for (int i = 0; i < nx; i++)
            for (int j = 0; j <= nz; j++)
            {
                int f = j + i * (nz + 1);
                int l = i + (j - 1) * nx, r = i + j * nx;
                bool lIn = j > 0, rIn = j < nz;
                if ((!lIn || _h[l] <= ShallowWater.Dry) && (!rIn || _h[r] <= ShallowWater.Dry))
                { _massZ[f] = _momZ[f] = _momZr[f] = _tanZ[f] = 0; continue; }
                if (lIn && rIn)
                {
                    var (m, pl, pr) = ShallowWater.Face(z[l], _h[l], ShallowWater.Velocity(_h[l], _qz[l]),
                                                        z[r], _h[r], ShallowWater.Velocity(_h[r], _qz[r]), g);
                    (_massZ[f], _momZ[f], _momZr[f]) = (m, pl, pr);
                    _tanZ[f] = m * (m >= 0 ? ShallowWater.Velocity(_h[l], _qx[l]) : ShallowWater.Velocity(_h[r], _qx[r]));
                }
                else Edge(lIn ? l : r, lIn ? 1 : -1, _qz, _qx, out _massZ[f], out _momZ[f], out _momZr[f], out _tanZ[f], g, open);
            }

        double k = dt / dx, area = dx * dx;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int c = i + j * nx;
                int fw = i + j * (nx + 1), fe = fw + 1, fs = j + i * (nz + 1), fn = fs + 1;
                if (_massX[fw] == 0 && _massX[fe] == 0 && _massZ[fs] == 0 && _massZ[fn] == 0 && _h[c] <= ShallowWater.Dry
                    && _momXr[fw] == 0 && _momX[fe] == 0 && _momZr[fs] == 0 && _momZ[fn] == 0) continue;
                double h = _h[c] - k * (_massX[fe] - _massX[fw] + _massZ[fn] - _massZ[fs]);
                double qx = _qx[c] - k * (_momX[fe] - _momXr[fw] + _tanZ[fn] - _tanZ[fs]);
                double qz = _qz[c] - k * (_momZ[fn] - _momZr[fs] + _tanX[fe] - _tanX[fw]);
                if (h <= ShallowWater.Dry)
                {
                    if (h < 0) Clipped -= h * area;
                    _h[c] = Math.Max(h, 0); _qx[c] = _qz[c] = 0;
                    continue;
                }
                // friction on the speed as a whole, semi-implicitly
                double speed = Math.Sqrt(qx * qx + qz * qz) / h;
                double n2 = Manning * Manning * Physics.Gravity / g;
                double damp = 1 / (1 + dt * g * n2 * speed / Math.Pow(h, 4.0 / 3));
                // the soil soaks some away
                double soak = Math.Min(h, Ground.Soils[Ground.Soil[c]].Infiltration * dt);
                Infiltrated += soak * area;
                _h[c] = h - soak;
                _qx[c] = qx * damp;
                _qz[c] = qz * damp;
            }
        if (_erodes ??= Ground.Soils.Any(s => s.Erodible)) MoveSediment(dt);
    }
    private bool? _erodes;

    /// <summary>
    /// A face on the map's edge, next to cell c, the edge lying in direction
    /// <paramref name="outward"/> (+1: past the cell's high side). Closed, a
    /// wall: only the water's pressure. Open, water moving towards the edge
    /// leaves at its own speed (counted as leaked); water at rest or moving
    /// away is held as by a wall.
    /// </summary>
    private void Edge(int c, int outward, double[] qn, double[] qt, out double mass, out double momL, out double momR, out double tangential, double g, bool open)
    {
        double h = _h[c], un = ShallowWater.Velocity(h, qn[c]);
        double pressure = g * h * h / 2;
        mass = tangential = 0;
        if (open && un * outward > 0)
        {
            mass = h * un;
            tangential = mass * ShallowWater.Velocity(h, qt[c]);
            pressure += h * un * un;
            Leaked += Math.Abs(mass) * Ground.Cell * _pendingDt;
        }
        momL = momR = pressure;
    }

    private double _pendingDt;
}
