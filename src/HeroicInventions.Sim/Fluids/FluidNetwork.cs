namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// A vertical-walled vessel. Open tanks sit at atmospheric pressure; sealed
/// tanks share an <see cref="AirPocket"/> whose pressure follows Boyle's law.
/// </summary>
public sealed class Tank(string name, double baseElevation, double area, double height, double waterVolume = 0)
{
    public string Name { get; } = name;
    public double BaseElevation { get; } = baseElevation; // m, bottom of the tank
    public double Area { get; } = area;                   // m², horizontal cross-section
    public double Height { get; } = height;               // m
    public double WaterVolume { get; internal set; } = waterVolume; // m³
    public AirPocket? Air { get; internal set; }

    public double Capacity => Area * Height;
    public double Level => WaterVolume / Area;
    public double SurfaceElevation => BaseElevation + Level;
    public double SurfaceGaugePressure => Air?.GaugePressure ?? 0;

    /// <summary>Is there water above this elevation, so a pipe port here can draw from the tank?</summary>
    public bool IsSubmerged(double portElevation) => SurfaceElevation > portElevation + 1e-9;

    /// <summary>
    /// Hydraulic head (m) seen by a pipe port at the given elevation. A port
    /// above the water line only sees the gas pressure plus its own height.
    /// </summary>
    public double HeadAt(double portElevation) =>
        Physics.PressureToHead(SurfaceGaugePressure) + Math.Max(SurfaceElevation, portElevation);
}

/// <summary>
/// A sealed body of air shared by one or more tanks (plus the tubes that
/// join them). Isothermal: P·V is constant from the moment it is sealed.
/// </summary>
public sealed class AirPocket
{
    private readonly List<Tank> _tanks;
    private readonly double _pvConstant;

    public AirPocket(IEnumerable<Tank> tanks, double tubeVolume = 0)
    {
        _tanks = tanks.ToList();
        TubeVolume = tubeVolume;
        foreach (var t in _tanks) t.Air = this;
        _pvConstant = Physics.AtmosphericPressure * Volume;
    }

    public double TubeVolume { get; }
    public double Volume => TubeVolume + _tanks.Sum(t => t.Capacity - t.WaterVolume);
    public double AbsolutePressure => _pvConstant / Volume;
    public double GaugePressure => AbsolutePressure - Physics.AtmosphericPressure;
}

/// <summary>
/// A pipe between two tank ports. Flow is linear in head difference
/// (laminar approximation); swap in an orifice law later for jets.
/// </summary>
public sealed class Pipe(string name, Tank from, double fromPortElevation, Tank to, double toPortElevation, double conductance)
{
    public string Name { get; } = name;
    public Tank From { get; } = from;
    public double FromPortElevation { get; } = fromPortElevation;
    public Tank To { get; } = to;
    public double ToPortElevation { get; } = toPortElevation;
    public double Conductance { get; } = conductance; // m³/s per metre of head
    public double Flow { get; internal set; }         // m³/s, positive = From → To

    /// <summary>For a pipe ending in a nozzle, how far above the outlet the jet would rise.</summary>
    public double JetHeight => Math.Max(0, From.HeadAt(FromPortElevation) - ToPortElevation);
}

/// <summary>Tanks and pipes stepped with explicit Euler on a fixed substep.</summary>
public sealed class FluidNetwork
{
    public List<Tank> Tanks { get; } = [];
    public List<Pipe> Pipes { get; } = [];
    public double MaxSubstep { get; init; } = 0.005; // s

    public Tank AddTank(Tank t) { Tanks.Add(t); return t; }
    public Pipe AddPipe(Pipe p) { Pipes.Add(p); return p; }

    public double TotalWater => Tanks.Sum(t => t.WaterVolume);

    public void Step(double dt)
    {
        int n = Math.Max(1, (int)Math.Ceiling(dt / MaxSubstep));
        double h = dt / n;
        for (int i = 0; i < n; i++) Substep(h);
    }

    private void Substep(double dt)
    {
        // Compute every flow from the same snapshot before moving any water,
        // so the result doesn't depend on pipe order.
        foreach (var p in Pipes)
        {
            double q = p.Conductance * (p.From.HeadAt(p.FromPortElevation) - p.To.HeadAt(p.ToPortElevation));
            if (q > 0 && !p.From.IsSubmerged(p.FromPortElevation)) q = 0;
            if (q < 0 && !p.To.IsSubmerged(p.ToPortElevation)) q = 0;
            p.Flow = q;
        }

        foreach (var p in Pipes)
        {
            var (src, srcPort, dst) = p.Flow >= 0
                ? (p.From, p.FromPortElevation, p.To)
                : (p.To, p.ToPortElevation, p.From);
            double available = Math.Max(0, (src.SurfaceElevation - srcPort) * src.Area);
            double room = dst.Capacity - dst.WaterVolume;
            double moved = Math.Min(Math.Abs(p.Flow) * dt, Math.Min(available, room));
            src.WaterVolume -= moved;
            dst.WaterVolume += moved;
        }
    }
}
