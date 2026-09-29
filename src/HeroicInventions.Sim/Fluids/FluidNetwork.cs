namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// A vertical-walled vessel. Open tanks sit at atmospheric pressure; sealed
/// tanks share an <see cref="AirPocket"/> whose pressure follows Boyle's law.
/// </summary>
public sealed class Tank(string name, double baseElevation, double area, double height, double waterVolume = 0)
{
    public string Name { get; } = name;
    public double BaseElevation { get; internal set; } = baseElevation; // m, bottom of the tank (a hanging vessel moves)
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
/// join them), sealed at atmospheric pressure and <see cref="SealedAt"/> °C.
/// Its mass never changes, so its pressure is the ideal gas law,
/// P = m·R·T / V: squeezed by water it rises as 1/V (Boyle, while its
/// temperature holds), and heated it rises as T.
///
/// Heat reaches it from a fire (<see cref="HeatInput"/>) and leaks to the
/// air outside through the vessel's walls, <see cref="HeatLoss"/> W per
/// kelvin above <see cref="SealedAt"/>. Its heat capacity is the air's own,
/// m·c_v, plus the vessel's (<see cref="VesselHeatCapacity"/>): a bronze
/// altar holds far more heat than the air in it, and sets how slowly it
/// warms. When it expands, pushing water out, it does P·dV of work and
/// cools by that much. Given no walls and no fire it stays at the
/// temperature it was sealed at (<see cref="Isothermal"/>).
/// </summary>
public sealed class AirPocket : IHeated
{
    private readonly List<Tank> _tanks;
    private double _lastVolume;

    public AirPocket(IEnumerable<Tank> tanks, double tubeVolume = 0, double sealedAtC = 20)
    {
        _tanks = tanks.ToList();
        TubeVolume = tubeVolume;
        foreach (var t in _tanks) t.Air = this;
        SealedAt = Temperature = sealedAtC;
        _lastVolume = Volume;
        Mass = Physics.AtmosphericPressure * Volume / (Physics.AirGasConstant * Physics.ToKelvin(sealedAtC));
    }

    public double TubeVolume { get; }
    public double SealedAt { get; }                               // °C, and the air outside
    public double Mass { get; }                                   // kg of air
    public double Temperature { get; private set; }               // °C
    public double HeatInput { get; set; }                         // W from a fire
    public double HeatLoss { get; init; }                         // W/K through the walls
    public double VesselHeatCapacity { get; init; }               // J/K of the vessel itself
    public double HeatCapacity => Mass * Physics.AirSpecificHeatCv + VesselHeatCapacity;
    /// <summary>
    /// With no walls described and no fire, the vessel is taken to hold the
    /// air at the temperature it was sealed at, whatever it is squeezed to —
    /// Boyle's law, as Heron's fountain has always run.
    /// </summary>
    public bool Isothermal => HeatLoss == 0 && VesselHeatCapacity == 0 && HeatInput == 0 && Temperature == SealedAt;

    public double Volume => TubeVolume + _tanks.Sum(t => t.Capacity - t.WaterVolume);
    public double AbsolutePressure => Mass * Physics.AirGasConstant * Physics.ToKelvin(Temperature) / Volume;
    public double GaugePressure => AbsolutePressure - Physics.AtmosphericPressure;

    /// <summary>
    /// Heat for dt seconds. The walls' loss pulls it toward where heat in
    /// equals heat out, SealedAt + HeatInput / HeatLoss, exponentially with
    /// time constant C / HeatLoss — solved exactly, since for air alone that
    /// can be far shorter than a step.
    /// </summary>
    public void Step(double dt)
    {
        if (Isothermal) return;
        double c = HeatCapacity;
        if (HeatLoss > 0)
        {
            double steady = SealedAt + HeatInput / HeatLoss;
            Temperature = steady + (Temperature - steady) * Math.Exp(-HeatLoss * dt / c);
        }
        else Temperature += HeatInput * dt / c;
        double v = Volume;
        Temperature -= AbsolutePressure * (v - _lastVolume) / c;   // work done pushing water out
        _lastVolume = v;
    }
}

/// <summary>
/// A pipe between two tank ports. Flow is linear in head difference
/// (laminar approximation); swap in an orifice law later for jets.
/// </summary>
public sealed class Pipe(string name, Tank from, double fromPortElevation, Tank to, double toPortElevation, double conductance)
{
    // ports are fixed in their tanks, so they move with a tank that moves (a hanging bucket)
    private readonly double _fromPort = fromPortElevation - from.BaseElevation, _toPort = toPortElevation - to.BaseElevation;

    public string Name { get; } = name;
    public Tank From { get; } = from;
    public double FromPortElevation => From.BaseElevation + _fromPort;
    public Tank To { get; } = to;
    public double ToPortElevation => To.BaseElevation + _toPort;
    public double Conductance { get; } = conductance; // m³/s per metre of head
    public double Flow { get; internal set; }         // m³/s, positive = From → To
    /// <summary>A float valve in the tank it feeds, throttling it, if it has one.</summary>
    public FloatValve? Valve { get; set; }

    /// <summary>For a pipe ending in a nozzle, how far above the outlet the jet would rise.</summary>
    public double JetHeight => Math.Max(0, From.HeadAt(FromPortElevation) - ToPortElevation);
}

/// <summary>Tanks and pipes stepped with explicit Euler on a fixed substep.</summary>
public sealed class FluidNetwork
{
    public List<Tank> Tanks { get; } = [];
    public List<Pipe> Pipes { get; } = [];
    public List<TankLeak> Leaks { get; } = [];
    public double MaxSubstep { get; init; } = 0.005; // s

    public Tank AddTank(Tank t) { Tanks.Add(t); return t; }
    public Pipe AddPipe(Pipe p) { Pipes.Add(p); return p; }
    public TankLeak AddLeak(TankLeak l) { Leaks.Add(l); return l; }

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
            p.Flow = q * (p.Valve?.Opening ?? 1);
        }
        foreach (var l in Leaks) l.Flow = l.Discharge();

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

        foreach (var l in Leaks)
        {
            double available = Math.Max(0, (l.Tank.SurfaceElevation - l.HoleElevation) * l.Tank.Area);
            double room = l.Catch is { } c ? c.Capacity - c.WaterVolume : double.PositiveInfinity;
            double moved = Math.Min(l.Flow * dt, Math.Min(available, room));
            l.Tank.WaterVolume -= moved;
            if (l.Catch is not null) l.Catch.WaterVolume += moved;
            l.Lost += moved;
            double seep = Math.Min(l.Evaporation * dt, l.Tank.WaterVolume);
            l.Tank.WaterVolume -= seep;
            l.Evaporated += seep;
        }
    }
}
