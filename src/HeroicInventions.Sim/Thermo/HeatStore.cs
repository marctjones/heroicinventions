using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Thermo;

/// <summary>What a heat store is made of (issue #71): its heat capacity, and for water the latent heat of freezing.</summary>
/// <param name="SpecificHeat">J/(kg·K), the liquid's or the solid's</param>
/// <param name="Density">kg/m³, to size the thing</param>
/// <param name="SolidSpecificHeat">J/(kg·K) below the melting point (ice), where the substance freezes</param>
/// <param name="Latent">J/kg to freeze it at 0 °C, 0 for a solid that stays solid</param>
public sealed record Substance(string Name, double SpecificHeat, double Density, double SolidSpecificHeat, double Latent)
{
    /// <summary>Water: 4,186 J/(kg·K) liquid, 2,100 as ice, 334 kJ/kg to freeze it (the figures the tanks use).</summary>
    public static readonly Substance Water = new("water", Physics.WaterSpecificHeat, Physics.WaterDensity, Melter.IceSpecificHeat, Tank.LatentHeatFusion);

    /// <summary>A solid from the material table: its specific heat and density, never frozen or melted.</summary>
    public static Substance Of(MaterialDef m) =>
        m.SpecificHeat is { } c ? new(m.Id, c, m.Density, c, 0)
        : throw new ArgumentException($"{m.Id} has no specific heat in the material table");

    /// <summary>"water", or a material of the table.</summary>
    public static Substance Named(string name, MaterialLibrary materials) =>
        name == "water" ? Water
        : materials.TryGet(name, out var m) ? Of(m)
        : throw new ArgumentException($"unknown substance {name}: water or a material of the table");
}

/// <summary>
/// A body that stores heat (issue #71): a bed of rocks, a block of iron, a tank of hot water. It has a temperature
/// and a heat capacity m·c, gains heat from whatever is aimed at it (<see cref="HeatInput"/>: a mirror, a hearth),
/// and exchanges heat with the zone it stands in:
///
///   Q = G·(T − T_zone),   G = h·A + 4·ε_eff·σ·A·T̄³  (linearised radiation),
///
/// h·A is <see cref="Conductance"/>, a Newton's-law film in air thick enough to carry heat (0 where the air is
/// too thin to, as in a vault at 610 Pa, where all the exchange is radiation). Radiation is the two-surface
/// enclosure exchange of a body A₁, ε₁ inside a room whose inner surface is A₂, ε₂ (Incropera, ch. 13):
///
///   Q = σ·(T₁⁴ − T₂⁴) / (1/(ε₁A₁) + (1 − ε₂)/(ε₂A₂)),
///
/// and with the room much bigger than the body (open air) just ε₁·σ·A₁·(T⁴ − T_air⁴). A store with a
/// <see cref="Bin"/> (a lidded bin) exchanges through its lid instead: with the lid closed only the lid's leak,
/// <see cref="HeatBin.Leak"/> W/K, with it open the full exposure, blended by how far it is open.
///
/// With only a conductance and a steady zone it cools exponentially, T = T_zone + (T₀ − T_zone)·e^(−t/τ), with
/// τ = m·c/G (a 33 kg tank at 60 °C is τ = 138 kJ/K ÷ G). Heat is kept as an enthalpy so that water can
/// freeze: at 0 °C the temperature holds while the latent heat leaves (334 kJ/kg), and it is frozen when it has all gone.
/// </summary>
public sealed class HeatStore(string name, Substance substance, double mass, double temperatureC) : IHeated
{
    public const double StefanBoltzmann = Crucible.StefanBoltzmann;

    public string Name { get; } = name;
    public Substance Substance { get; } = substance;
    public double Mass { get; } = mass;                          // kg
    public double Area { get; set; }                             // m² of surface it radiates from
    public double Emissivity { get; set; } = 0.9;
    public double Conductance { get; set; }                      // W/K (h·A), to the zone's air by contact
    public double HeatInput { get; set; }                        // W landing on it (a mirror, a hearth)
    public Zone Zone { get; set; } = new();
    /// <summary>The lidded bin round it, if any.</summary>
    public HeatBin? Bin { get; set; }

    // J above the solid at 0 °C: m c_s T when colder; 0 … m L at 0 °C while freezing/thawing; m (L + c T) when liquid
    private double _enthalpy = Enthalpy(substance, mass, temperatureC);
    /// <summary>W given to the zone through the walls or lid, last step (negative: taking heat).</summary>
    public double Exchange { get; private set; }
    /// <summary>J given to the zone, all told.</summary>
    public double Given { get; private set; }
    /// <summary>J gained from mirrors and fires, all told.</summary>
    public double Gained { get; private set; }

    private static double Enthalpy(Substance s, double mass, double t)
    {
        double latent = mass * s.Latent;
        return t < 0 ? mass * s.SolidSpecificHeat * t : latent + mass * s.SpecificHeat * t;
    }

    /// <summary>J/K, the capacity in its present phase (the liquid's for a thawing mix).</summary>
    public double HeatCapacity => Mass * (Temperature < 0 ? Substance.SolidSpecificHeat : Substance.SpecificHeat);

    /// <summary>°C. Setting it sets the heat the body holds (liquid at 0 °C).</summary>
    public double Temperature
    {
        get
        {
            double latent = Mass * Substance.Latent;
            if (_enthalpy < 0) return _enthalpy / (Mass * Substance.SolidSpecificHeat);
            if (_enthalpy <= latent) return 0;
            return (_enthalpy - latent) / (Mass * Substance.SpecificHeat);
        }
        set => _enthalpy = Enthalpy(Substance, Mass, value);
    }

    /// <summary>Share of its water that is ice, 0 to 1 (always 0 for a solid that cannot freeze).</summary>
    public double Frozen => Substance.Latent <= 0 || Mass <= 0 ? 0
        : _enthalpy < 0 ? 1 : _enthalpy >= Mass * Substance.Latent ? 0 : 1 - _enthalpy / (Mass * Substance.Latent);

    /// <summary>J it holds above 0 °C liquid (negative when colder).</summary>
    public double Heat => _enthalpy - Mass * Substance.Latent;

    public void AddHeat(double joules) => _enthalpy += joules;

    /// <summary>W/K by radiation (linearised about the present temperatures) and contact to a room whose inner surface is <paramref name="surroundArea"/> m² of emissivity <paramref name="surroundEmissivity"/>.</summary>
    public double ExposedConductance(double zoneC, double surroundArea = double.PositiveInfinity, double surroundEmissivity = 1)
    {
        double rad = 0;
        if (Area > 0 && Emissivity > 0)
        {
            double eff = double.IsInfinity(surroundArea) || surroundArea <= 0
                ? Emissivity
                : 1 / (1 / Emissivity + Area / surroundArea * (1 / Math.Max(1e-6, surroundEmissivity) - 1));
            double t1 = Physics.ToKelvin(Temperature), t2 = Physics.ToKelvin(zoneC);
            rad = eff * Area * StefanBoltzmann * (t1 * t1 + t2 * t2) * (t1 + t2);
        }
        return rad + Conductance;
    }

    /// <summary>W/K it passes to the room now: exposed to it, or through the lid of its bin as far as that is shut.</summary>
    public double CouplingTo(double zoneC, double surroundArea = double.PositiveInfinity, double surroundEmissivity = 1) =>
        Bin is null ? ExposedConductance(zoneC, surroundArea, surroundEmissivity)
                    : Bin.Blend(ExposedConductance(zoneC, surroundArea, surroundEmissivity));

    /// <summary>The heat the zone gives it this step (joules, may be negative); the room that solves the whole network calls this.</summary>
    public void Exchanged(double joulesFromZone, double dt)
    {
        _enthalpy += joulesFromZone;
        Exchange = -joulesFromZone / dt;
        Given -= joulesFromZone;
    }

    /// <summary>What mirrors and fires aimed at it give, for dt seconds.</summary>
    public void Absorb(double dt)
    {
        _enthalpy += HeatInput * dt;
        Gained += HeatInput * dt;
    }

    /// <summary>
    /// A store in the open air (not inside an enclosure's network): exchanges with the zone's air alone. Within a step the
    /// temperature relaxes towards the air's exactly, T = T_air + (T − T_air)·e^(−G·dt/C), at the capacity of its present phase.
    /// </summary>
    public void StepOpen(double dt)
    {
        double air = Zone.Temperature;
        int n = Math.Max(1, (int)Math.Ceiling(dt / 5.0));      // short steps where the radiation changes quickly
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            double g = CouplingTo(air), t = Temperature, c = HeatCapacity;
            double de;
            if (Substance.Latent > 0 && _enthalpy >= 0 && _enthalpy <= Mass * Substance.Latent)
                de = g * (air - 0) * h;                         // on the plateau the temperature holds at 0 °C
            else if (g <= 0 || c <= 0) de = 0;
            else de = c * (air + (t - air) * Math.Exp(-g * h / c) - t);
            _enthalpy += de;
            Exchange = -de / h;
            Given -= de;
        }
    }
}
