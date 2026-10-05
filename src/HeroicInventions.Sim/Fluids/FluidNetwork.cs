namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// A vertical-walled vessel. Open tanks sit at atmospheric pressure; sealed
/// tanks share an <see cref="AirPocket"/> whose pressure follows Boyle's law.
///
/// In a frost ice grows down from the surface (<see cref="Freeze"/>); the
/// water under it is taken to be at 0 °C, as a pond's is. Only the liquid
/// flows: <see cref="WaterVolume"/> is what is still water, and a tank
/// frozen solid lets nothing out. (The floating ice's own weight is left
/// out of the head a pipe sees.)
/// </summary>
public sealed class Tank(string name, double baseElevation, double area, double height, double waterVolume = 0)
{
    public string Name { get; } = name;
    public double BaseElevation { get; internal set; } = baseElevation; // m, bottom of the tank (a hanging vessel moves)
    public double Area { get; } = area;                   // m², horizontal cross-section
    public double Height { get; } = height;               // m
    public double WaterVolume { get; internal set; } = waterVolume; // m³
    public AirPocket? Air { get; internal set; }
    /// <summary>The air and gravity it stands in: the planet's open air unless it is inside an enclosure.</summary>
    public Zone Zone { get; set; } = new();
    public double Ice { get; private set; }               // m thick, grown down from the surface
    public bool FrozenSolid => Ice > 0 && WaterVolume <= 1e-12;

    // Stefan's problem for a sheet of ice on water at 0 °C under air at T < 0:
    // the latent heat of each new layer leaves by conduction through the ice
    // above it, ρ_i·L·dh/dt = k·(0 − T)/h, so h² grows as 2k(−T)t/(ρ_i·L).
    public const double IceConductivity = 2.22;           // W/(m·K)
    public const double IceDensity = 917;                 // kg/m³
    public const double LatentHeatFusion = 334_000;       // J/kg
    /// <summary>Warm air melting ice from above: W/(m²·K), still air on a flat surface.</summary>
    public const double MeltCoefficient = 10;

    /// <summary>Freeze (below 0 °C) or thaw (above) for dt seconds under air at <paramref name="ambient"/> °C.</summary>
    public void Freeze(double dt, double ambient)
    {
        double before = Ice;
        if (ambient < 0 && WaterVolume > 0)
            Ice = Math.Sqrt(Ice * Ice + 2 * IceConductivity * -ambient * dt / (IceDensity * LatentHeatFusion));
        else if (ambient > 0 && Ice > 0)
            Ice = Math.Max(0, Ice - MeltCoefficient * ambient * dt / (IceDensity * LatentHeatFusion));
        else return;
        // water frozen (or melted) this step, as liquid volume
        double water = (Ice - before) * Area * IceDensity / Physics.WaterDensity;
        if (water > WaterVolume)
        {
            // frozen solid: only as thick as the water there was
            Ice = before + WaterVolume * Physics.WaterDensity / (IceDensity * Area);
            water = WaterVolume;
        }
        WaterVolume = Math.Min(Capacity, WaterVolume - water);
    }

    /// <summary>
    /// Where water over its brim goes (issue #90): a tank standing on a map's ground pours it there. Unset (the plain
    /// floor), water offered to a full tank is simply not taken, as it always was.
    /// </summary>
    public Action<double>? Spill { get; set; }
    /// <summary>m³ that has run over its brim onto the ground, all told.</summary>
    public double Spilled { get; private set; }

    /// <summary>Lets <paramref name="m3"/> that found no room run over the brim, if anything is under it to take it; true if it went.</summary>
    public bool Overflow(double m3)
    {
        if (m3 <= 0 || Spill is null) return false;
        Spilled += m3;
        Spill(m3);
        return true;
    }

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
        Zone.PressureToHead(SurfaceGaugePressure + ZoneOverpressure) + Math.Max(SurfaceElevation, portElevation);

    /// <summary>Pa its zone's air stands above the planet's open air: a tank inside a pressurised enclosure pushes that much harder down a pipe through the wall.</summary>
    public double ZoneOverpressure => Zone.Pressure - Zone.Planet.Pressure;

    /// <summary>°C of the air over it: its enclosure's, or the scene's (<paramref name="scene"/>) in the open.</summary>
    public double AirTemperature(double scene) => Zone is Thermo.Enclosure room ? room.Temperature : scene;
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

    public AirPocket(IEnumerable<Tank> tanks, double tubeVolume = 0, double sealedAtC = 20, Zone? zone = null)
    {
        Zone = zone ?? new Zone();
        GasConstant = Zone.AirGasConstant;
        _tanks = tanks.ToList();
        TubeVolume = tubeVolume;
        foreach (var t in _tanks) t.Air = this;
        SealedAt = Temperature = Ambient = sealedAtC;
        _lastVolume = Volume;
        Mass = Zone.Pressure * Volume / (GasConstant * Physics.ToKelvin(sealedAtC));   // sealed at the zone's pressure
    }

    public double TubeVolume { get; }
    /// <summary>The air outside the vessel's walls: what it was sealed at, and what its gauge pressure is measured against.</summary>
    public Zone Zone { get; set; }
    /// <summary>J/(kg·K) of the air sealed in: the zone's air as it was when sealed.</summary>
    public double GasConstant { get; }
    public double SealedAt { get; }                               // °C
    public double Ambient { get; set; }                           // °C of the air outside the walls; sealed at it
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
    public double AbsolutePressure => Mass * GasConstant * Physics.ToKelvin(Temperature) / Volume;
    public double GaugePressure => AbsolutePressure - Zone.Pressure;

    /// <summary>
    /// Heat for dt seconds. The walls' loss pulls it toward where heat in
    /// equals heat out, Ambient + HeatInput / HeatLoss, exponentially with
    /// time constant C / HeatLoss — solved exactly, since for air alone that
    /// can be far shorter than a step.
    /// </summary>
    public void Step(double dt)
    {
        if (Isothermal) return;
        double c = HeatCapacity;
        if (HeatLoss > 0)
        {
            double steady = Ambient + HeatInput / HeatLoss;
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
    /// <summary>The air round the tanks, °C: below 0 they freeze; a seep evaporates as water's vapour pressure at it.</summary>
    public double Ambient { get; set; } = 20;

    /// <summary>
    /// A seep's #:evaporation is its rate at 20 °C. Evaporation runs as the
    /// surface's vapour pressure against the air (dry air, Dalton, 1802), so
    /// at another temperature it goes as p_sat(T)/p_sat(20 °C); an iced-over
    /// surface gives none.
    /// </summary>
    public double EvaporationFactor(Tank t) =>
        t.Ice > 0 || t.AirTemperature(Ambient) <= 0 ? 0 : Thermo.Boiler.SaturationPressure(t.AirTemperature(Ambient)) / Thermo.Boiler.SaturationPressure(20);

    public void Step(double dt)
    {
        int n = Math.Max(1, (int)Math.Ceiling(dt / MaxSubstep));
        double h = dt / n;
        for (int i = 0; i < n; i++) Substep(h);
    }

    private void Substep(double dt)
    {
        foreach (var t in Tanks) t.Freeze(dt, t.AirTemperature(Ambient));
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
            else l.Pour?.Invoke(moved);      // the jet falls on the ground under it (#90)
            l.Lost += moved;
            double seep = Math.Min(l.Evaporation * EvaporationFactor(l.Tank) * dt, l.Tank.WaterVolume);
            l.Tank.WaterVolume -= seep;
            l.Evaporated += seep;
        }
    }
}
