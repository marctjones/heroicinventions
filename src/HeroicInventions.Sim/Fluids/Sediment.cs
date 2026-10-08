namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// Water that carries the ground away (issue #53). Where water runs over a
/// soil with grains (SoilSpec.GrainSize), the flow drags on the bed with a
/// shear stress τ; the grains move once the Shields number
/// θ = τ / ((ρ_s − ρ) g d) passes θ_c = 0.047, and then travel with the flow
/// at Meyer-Peter and Müller's rate (1948), per metre of width
///   q_s = 8 (θ − θ_c)^1.5 √((ρ_s/ρ − 1) g d³),
/// grains' own volume. The bed follows the Exner equation,
/// (1 − λ) ∂z/∂t = −∇·q_s with λ = 0.4 the pores between the grains:
/// where more is carried out of a cell than into it the bed goes down (a
/// jet cuts a gully), where the flow slackens and drops what it carried it
/// builds up (a fan below it). Sediment is carried to the next cell the
/// way the water goes, and off an open edge counted as lost with the water.
///
/// The bed stress is Manning's, the drag the solver's own friction puts on
/// the water: τ = ρ g n² |u|² / h^(1/3), which in steady flow down a slope
/// S is ρ g h S (for a sheet much wider than it is deep).
/// </summary>
public sealed partial class ShallowWater2D
{
    public const double ShieldsThreshold = 0.047;
    public const double Porosity = 0.4;

    private double[]? _qsx, _qsz, _fsx, _fsz;

    /// <summary>m³ of bed (grains and the pores between them) carried off an open edge, all told.</summary>
    public double BedLost { get; private set; }
    /// <summary>m³ of bed that has moved: dug out of one cell and laid down in another, all told.</summary>
    public double BedMoved { get; private set; }

    /// <summary>Pa, the flow's drag on the bed at a cell.</summary>
    public double BedShear(int c)
    {
        double h = _h[c];
        if (h <= 1e-3) return 0;
        double u = Math.Sqrt(_qx[c] * _qx[c] + _qz[c] * _qz[c]) / h;
        double n2 = Manning * Manning * Physics.Gravity / Gravity;
        return Physics.WaterDensity * Gravity * n2 * u * u / Math.Cbrt(h);
    }

    /// <summary>The Shields number at a cell: the bed's drag over the weight of one layer of its grains.</summary>
    public double Shields(int c)
    {
        var soil = Ground.SoilOf(c);
        return soil.Erodible ? BedShear(c) / ((soil.GrainDensity - Physics.WaterDensity) * Gravity * soil.GrainSize) : 0;
    }

    /// <summary>m²/s of grains a metre of the flow's width carries at a cell (Meyer-Peter and Müller), 0 below the threshold.</summary>
    public double SedimentRate(int c)
    {
        var soil = Ground.SoilOf(c);
        double theta = Shields(c);
        if (theta <= ShieldsThreshold) return 0;
        double s = soil.GrainDensity / Physics.WaterDensity;
        return 8 * Math.Pow(theta - ShieldsThreshold, 1.5) * Math.Sqrt((s - 1) * Gravity * Math.Pow(soil.GrainSize, 3));
    }

    private void MoveSediment(double dt)
    {
        int nx = Ground.Nx, nz = Ground.Nz, n = Ground.Count;
        _qsx ??= new double[n]; _qsz ??= new double[n];
        _fsx ??= new double[(nx + 1) * nz]; _fsz ??= new double[nx * (nz + 1)];
        bool any = false;
        for (int c = 0; c < n; c++)
        {
            double q = SedimentRate(c);
            if (q <= 0) { _qsx[c] = _qsz[c] = 0; continue; }
            double speed = Math.Sqrt(_qx[c] * _qx[c] + _qz[c] * _qz[c]);
            _qsx[c] = q * _qx[c] / speed;
            _qsz[c] = q * _qz[c] / speed;
            any = true;
        }
        if (!any) return;
        bool open = Ground.OpenEdges;
        // across each face, the grains of the cell the water comes from
        for (int j = 0; j < nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                int f = i + j * (nx + 1);
                double m = _massX[f];
                if (i == 0 || i == nx)
                {
                    int c = i == 0 ? j * nx : nx - 1 + j * nx;
                    double q = _qsx[c];
                    _fsx[f] = open && (i == 0 ? q < 0 : q > 0) ? q : 0;
                    continue;
                }
                _fsx[f] = _inactive is { } offX && (offX[i - 1 + j * nx] || offX[i + j * nx]) ? 0   // a patch's fine water carries no sand (#200)
                        : m > 0 ? Math.Max(0, _qsx[i - 1 + j * nx]) : m < 0 ? Math.Min(0, _qsx[i + j * nx]) : 0;
            }
        for (int i = 0; i < nx; i++)
            for (int j = 0; j <= nz; j++)
            {
                int f = j + i * (nz + 1);
                double m = _massZ[f];
                if (j == 0 || j == nz)
                {
                    int c = i + (j == 0 ? 0 : nz - 1) * nx;
                    double q = _qsz[c];
                    _fsz[f] = open && (j == 0 ? q < 0 : q > 0) ? q : 0;
                    continue;
                }
                _fsz[f] = _inactive is { } offZ && (offZ[i + (j - 1) * nx] || offZ[i + j * nx]) ? 0
                        : m > 0 ? Math.Max(0, _qsz[i + (j - 1) * nx]) : m < 0 ? Math.Min(0, _qsz[i + j * nx]) : 0;
            }
        // Exner: the bed goes down by what leaves, up by what arrives, pores and all
        double k = dt / (Ground.Cell * (1 - Porosity)), area = Ground.Cell * Ground.Cell;
        var z = Ground.Heights;
        double moved = 0;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int c = i + j * nx, fw = i + j * (nx + 1), fs = j + i * (nz + 1);
                double dz = -k * (_fsx[fw + 1] - _fsx[fw] + _fsz[fs + 1] - _fsz[fs]);
                if (dz == 0) continue;
                z[c] += dz;
                moved += Math.Abs(dz) * area;
            }
        BedMoved += moved / 2;
        // what left over an open edge
        double lost = 0;
        for (int j = 0; j < nz; j++) lost += -_fsx[j * (nx + 1)] + _fsx[nx + j * (nx + 1)];
        for (int i = 0; i < nx; i++) lost += -_fsz[i * (nz + 1)] + _fsz[nz + i * (nz + 1)];
        BedLost += lost * Ground.Cell * dt / (1 - Porosity);
        Ground.Touch();
    }
}
