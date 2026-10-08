namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// A wall of earth that heat soaks into (issue #71): one-dimensional transient conduction,
///
///   ρ·c·∂T/∂t = k·∂²T/∂x²,   diffusivity α = k/(ρ·c),
///
/// through a slab <see cref="Thickness"/> m thick and <see cref="Area"/> m² across, its far face held at the
/// ground's temperature and its near face (the "surface node", the room's inner surface) joined to whatever is
/// in the room. A fresh wall is at ground temperature all through, so it is not in the steady state U·A·ΔT
/// that k·A·ΔT/t describes: it soaks heat up for weeks. Against a surface held ΔT warmer than it, a deep wall
/// takes in the heat flux
///
///   q = I·ΔT/√(π·t),   I = √(k·ρ·c) the thermal inertia (Mars soil: about 216 J/(m²·K·√s)),
///
/// that is 2·I·ΔT·√(t/π) J/m² by time t (the integral): 3 MJ/m² in a night at ΔT = 60 K, 60 times the steady loss through
/// 0.5 m in that time. A night's heat penetrates only √(α·t) ≈ 4 cm (α = 3.3e-8 m²/s for Mars soil). The flux falls
/// to the steady k·ΔT/t over a time of order t²/α (thickness 0.5 m: weeks).
///
/// Cells are thin at the surface (1 mm) and grow geometrically towards the far face, so the first night is
/// resolved and weeks of soaking are cheap. Each step is backward-Euler: one tridiagonal solve, stable at any step
/// the caller takes (a sleep's 1/120 s, a 60 s step in a test), and exactly energy-conserving.
/// </summary>
public sealed class HeatSlab
{
    private readonly double[] _t;          // °C of each cell's middle
    private readonly double[] _w;          // m, each cell's thickness
    private readonly double _heatCapacityPerM;   // J/(K·m) = ρ·c·A

    public HeatSlab(double conductivity, double density, double specificHeat, double thickness, double area, double groundC, double initialC,
                    int cells = 48, double firstCell = 0.001)
    {
        if (!(conductivity > 0 && density > 0 && specificHeat > 0 && thickness > 0 && area > 0 && cells >= 2))
            throw new ArgumentException("a wall needs positive conductivity, density, specific heat, thickness and area");
        Conductivity = conductivity; Density = density; SpecificHeat = specificHeat;
        Thickness = thickness; Area = area; GroundTemperature = groundC; InitialTemperature = initialC;
        firstCell = Math.Min(firstCell, thickness / cells);
        _heatCapacityPerM = density * specificHeat * area;
        _w = Grid(thickness, cells, firstCell);
        _t = new double[cells];
        Array.Fill(_t, initialC);
        SurfaceTemperature = initialC;
    }

    public double Conductivity { get; }          // W/(m·K)
    public double Density { get; }               // kg/m³
    public double SpecificHeat { get; }          // J/(kg·K)
    public double Thickness { get; }             // m
    public double Area { get; }                  // m²
    public double GroundTemperature { get; set; }    // °C, the far face
    public double InitialTemperature { get; }    // °C the wall started at

    /// <summary>m²/s: k / (ρ c).</summary>
    public double Diffusivity => Conductivity / (Density * SpecificHeat);
    /// <summary>J/(m²·K·√s): √(k ρ c).</summary>
    public double Inertia => Math.Sqrt(Conductivity * Density * SpecificHeat);
    /// <summary>W/K the wall would pass once soaked through, k·A/thickness.</summary>
    public double SteadyConductance => Conductivity * Area / Thickness;

    /// <summary>°C of the near face, the room's inner surface.</summary>
    public double SurfaceTemperature { get; private set; }
    /// <summary>W flowing from the surface into the wall, last step.</summary>
    public double Flux { get; private set; }
    /// <summary>J that have flowed from the surface into the wall, all told.</summary>
    public double Absorbed { get; private set; }
    /// <summary>J that have flowed out of the far face into the ground, all told.</summary>
    public double ToGround { get; private set; }

    public int Cells => _t.Length;
    /// <summary>°C of cell i, counting from the surface.</summary>
    public double CellTemperature(int i) => _t[i];
    /// <summary>m, the depth of the middle of cell i.</summary>
    public double CellDepth(int i) { double x = 0; for (int j = 0; j < i; j++) x += _w[j]; return x + _w[i] / 2; }

    /// <summary>J the wall holds above its starting state: Σ ρ c A w (T − T₀).</summary>
    public double StoredHeat
    {
        get { double s = 0; for (int i = 0; i < _t.Length; i++) s += _heatCapacityPerM * _w[i] * (_t[i] - InitialTemperature); return s; }
    }

    /// <summary>m, how deep the heat has gone: the depth where the wall is still less than 5% of the way from its start to the surface's temperature.</summary>
    public double PenetrationDepth
    {
        get
        {
            double dt = SurfaceTemperature - InitialTemperature;
            if (Math.Abs(dt) < 1e-9) return 0;
            double x = 0;
            for (int i = 0; i < _t.Length; i++)
            {
                if ((_t[i] - InitialTemperature) / dt < 0.05) return x;
                x += _w[i];
            }
            return x;
        }
    }

    /// <summary>
    /// One backward-Euler step of <paramref name="dt"/> seconds. The surface node holds <paramref name="surfaceCapacity"/> J/K
    /// (the room's air and walls' own, may be 0), is fed <paramref name="power"/> W, and is joined to the things in the room by
    /// conductances whose sum is <paramref name="sumG"/> W/K and whose sum of conductance × temperature is
    /// <paramref name="sumGT"/> (W): (C/dt + ΣG + G₀)·T₀' − G₀·T₁' = C/dt·T₀ + ΣG·T + P. Returns the surface's new temperature.
    /// </summary>
    public double Step(double dt, double surfaceCapacity, double sumG, double sumGT, double power)
    {
        int n = _t.Length;
        double c0 = surfaceCapacity / dt;
        Span<double> g = stackalloc double[n + 1];      // g[0]: surface to cell 0; g[i]: cell i-1 to cell i; g[n]: cell n-1 to the ground
        g[0] = Conductivity * Area / (_w[0] / 2);
        for (int i = 1; i < n; i++) g[i] = Conductivity * Area / ((_w[i - 1] + _w[i]) / 2);
        g[n] = Conductivity * Area / (_w[n - 1] / 2);
        // rows 0..n: node 0 is the surface, node i+1 is cell i
        Span<double> diag = stackalloc double[n + 1], rhs = stackalloc double[n + 1], cp = stackalloc double[n + 1], dp = stackalloc double[n + 1];
        diag[0] = c0 + sumG + g[0];
        rhs[0] = c0 * SurfaceTemperature + sumGT + power;
        for (int i = 0; i < n; i++)
        {
            double cap = _heatCapacityPerM * _w[i] / dt;
            diag[i + 1] = cap + g[i] + g[i + 1];
            rhs[i + 1] = cap * _t[i];
        }
        rhs[n] += g[n] * GroundTemperature;
        // Thomas algorithm; the off-diagonals are -g[i] between row i and row i+1
        if (diag[0] <= 0) return SurfaceTemperature;
        cp[0] = -g[0] / diag[0];
        dp[0] = rhs[0] / diag[0];
        for (int i = 1; i <= n; i++)
        {
            double m = diag[i] + g[i - 1] * cp[i - 1];
            cp[i] = i < n ? -g[i] / m : 0;
            dp[i] = (rhs[i] + g[i - 1] * dp[i - 1]) / m;
        }
        double next = dp[n];
        for (int i = n; i >= 1; i--)
        {
            _t[i - 1] = next;
            next = dp[i - 1] - cp[i - 1] * next;
        }
        SurfaceTemperature = next;
        Flux = g[0] * (SurfaceTemperature - _t[0]);
        Absorbed += Flux * dt;
        ToGround += g[n] * (_t[n - 1] - GroundTemperature) * dt;
        return SurfaceTemperature;
    }

    /// <summary>Cell widths: geometric from <paramref name="first"/>, adding to <paramref name="total"/>.</summary>
    private static double[] Grid(double total, int cells, double first)
    {
        double lo = 1, hi = 3;
        double Sum(double r) => Math.Abs(r - 1) < 1e-12 ? first * cells : first * (Math.Pow(r, cells) - 1) / (r - 1);
        if (Sum(1) >= total) { var even = new double[cells]; Array.Fill(even, total / cells); return even; }
        for (int i = 0; i < 100; i++) { double mid = (lo + hi) / 2; if (Sum(mid) < total) lo = mid; else hi = mid; }
        double ratio = (lo + hi) / 2;
        var w = new double[cells];
        double s = 0;
        for (int i = 0; i < cells; i++) { w[i] = first * Math.Pow(ratio, i); s += w[i]; }
        for (int i = 0; i < cells; i++) w[i] *= total / s;      // exactly the thickness
        return w;
    }
}
