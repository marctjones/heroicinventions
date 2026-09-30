using System.Reflection;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim;

/// <summary>
/// A gas mixture by volume (mole) fraction: oxygen, nitrogen, carbon
/// dioxide, water vapour and argon. By Dalton's law each gas's partial
/// pressure is its fraction of the whole; the mixture's mean molar mass
/// sets its density, ρ = P·M / (R·T).
/// </summary>
public sealed record GasMix(double O2, double N2, double CO2, double H2O, double Ar)
{
    // kg/mol
    public const double O2MolarMass = 0.031998, N2MolarMass = 0.028014, CO2MolarMass = 0.04401,
                        H2OMolarMass = 0.018015, ArMolarMass = 0.039948;

    public double Total => O2 + N2 + CO2 + H2O + Ar;

    /// <summary>Mean molar mass, kg/mol: Σ xᵢ·Mᵢ over Σ xᵢ.</summary>
    public double MolarMass =>
        (O2 * O2MolarMass + N2 * N2MolarMass + CO2 * CO2MolarMass + H2O * H2OMolarMass + Ar * ArMolarMass) / Total;

    public static readonly string[] Names = ["o2", "n2", "co2", "h2o", "ar"];

    public double this[string gas] => gas switch
    {
        "o2" => O2, "n2" => N2, "co2" => CO2, "h2o" => H2O, "ar" => Ar,
        _ => throw new ArgumentException($"no gas named {gas}"),
    };
}

/// <summary>
/// The planet a scene stands on (issue #38): the numbers the game's
/// formulas run on, and nothing else. The formulas never change: a pendulum
/// swings as 2π√(L/g), a pump lifts to (P − Pᵥ)/(ρg), a windmill's wind
/// carries ½ρAv³ — on Mars with Mars's g, P and ρ. Presets come from
/// racket/heroic/planets.rktd (embedded in this assembly); a .machine file
/// carries every number of a planet that isn't Earth, so it needs no table.
/// </summary>
public sealed record Planet
{
    public required string Id { get; init; }                 // earth, mars
    public required string Name { get; init; }               // "Earth"
    public required double Gravity { get; init; }            // m/s²
    public required double Pressure { get; init; }           // Pa, absolute, at the surface
    public required double Temperature { get; init; }        // °C, a scene's air unless it says otherwise
    public required GasMix Air { get; init; }
    /// <summary>kg/mol, when given instead of the mixture's mean (Earth: 8.314 / 287.05, the dry-air constant the game has always used).</summary>
    public double? MolarMassOverride { get; init; }
    public required double SolarConstant { get; init; }      // W/m² above the air
    public required double SkyTransmittance { get; init; }   // clear-sky beam: DNI = S·T^(AM^k)
    public required double AirMassExponent { get; init; }    // k
    public required double Sol { get; init; }                // s in a solar day
    public required double Year { get; init; }               // sols in a year
    public required double Obliquity { get; init; }          // degrees of axial tilt
    /// <summary>The air's daily curve (issue #69), °C before dawn and in the afternoon and the local hour of the warmest; null: none.</summary>
    public (double Min, double Max, double PeakHour)? DailyTemperature { get; init; }
    public Vec3 SkyColor { get; init; } = new(0.55, 0.7, 0.9);
    public Vec3 GroundColor { get; init; } = new(0.55, 0.53, 0.5);

    public double MolarMass => MolarMassOverride ?? Air.MolarMass;
    /// <summary>J/(kg·K) of the air: R / M.</summary>
    public double AirGasConstant => Physics.GasConstant / MolarMass;

    private static IReadOnlyDictionary<string, Planet>? _presets;

    /// <summary>The presets in racket/heroic/planets.rktd, by id.</summary>
    public static IReadOnlyDictionary<string, Planet> Presets => _presets ??= LoadPresets();

    public static Planet Earth => Presets["earth"];
    public static Planet Mars => Presets["mars"];

    public static Planet Named(string id) =>
        Presets.TryGetValue(id, out var p) ? p
        : throw new MachineFormatException($"no planet named {id}; the planets are: {string.Join(", ", Presets.Keys)}");

    private static IReadOnlyDictionary<string, Planet> LoadPresets()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("planets.rktd")
            ?? throw new InvalidOperationException("planets.rktd resource missing");
        using var reader = new StreamReader(stream);
        return SExprReader.ReadAll(reader.ReadToEnd()).OfType<SList>()
            .Select(e => Parse(e, 0))
            .ToDictionary(p => p.Id);
    }

    /// <summary>
    /// A planet from its fields: (mars (name "Mars") (gravity 3.71) …) as the
    /// preset file has it, or (planet mars (name "Mars") …) as a .machine file
    /// does — <paramref name="idAt"/> is where the id stands.
    /// </summary>
    public static Planet Parse(SList form, int idAt, SourceLocation? loc = null)
    {
        string id = form.Items.ElementAtOrDefault(idAt) is SSymbol s ? s.Name : throw new MachineFormatException("a planet needs a name", loc);
        double Num(string key) =>
            form.Field(key)?.Items.ElementAtOrDefault(1) is SNumber n ? n.Value
            : throw new MachineFormatException($"planet {id} needs a number for {key}", loc);
        Vec3 Color(string key, Vec3 fallback) =>
            form.Field(key) is { Items: [_, SNumber r, SNumber g, SNumber b] } ? new Vec3(r.Value, g.Value, b.Value) : fallback;
        var air = form.Field("air") ?? throw new MachineFormatException($"planet {id} needs its air", loc);
        double Gas(string g) => air.Field(g)?.Items.ElementAtOrDefault(1) is SNumber n ? n.Value : 0;
        var mix = new GasMix(Gas("o2"), Gas("n2"), Gas("co2"), Gas("h2o"), Gas("ar"));
        if (mix.Total <= 0) throw new MachineFormatException($"planet {id}'s air has no gas in it", loc);
        return new Planet
        {
            Id = id,
            Name = form.Field("name")?.Items.ElementAtOrDefault(1) is SString nm ? nm.Value : id,
            Gravity = Num("gravity"),
            Pressure = Num("pressure"),
            Temperature = Num("temperature"),
            Air = mix,
            MolarMassOverride = form.Field("molar-mass")?.Items.ElementAtOrDefault(1) is SNumber mm ? mm.Value : null,
            SolarConstant = Num("solar-constant"),
            SkyTransmittance = Num("sky-transmittance"),
            AirMassExponent = Num("air-mass-exponent"),
            Sol = Num("sol"),
            Year = Num("year"),
            Obliquity = Num("obliquity"),
            DailyTemperature = form.Field("daily-temperature") is { Items: [_, SNumber lo, SNumber hi, SNumber peak] } ? (lo.Value, hi.Value, peak.Value) : null,
            SkyColor = Color("sky-color", new Vec3(0.55, 0.7, 0.9)),
            GroundColor = Color("ground-color", new Vec3(0.55, 0.53, 0.5)),
        };
    }

    /// <summary>The (planet id (field …) …) clause a .machine file carries, every number resolved.</summary>
    public SList ToClause()
    {
        SList T(string head, params SExpr[] items) => new([new SSymbol(head), .. items]);
        SNumber N(double v) => new(v);
        var items = new List<SExpr>
        {
            new SSymbol("planet"), new SSymbol(Id),
            T("name", new SString(Name)),
            T("gravity", N(Gravity)), T("pressure", N(Pressure)), T("temperature", N(Temperature)),
            T("air", [.. GasMix.Names.Select(g => (SExpr)T(g, N(Air[g])))]),
            T("molar-mass", MolarMassOverride is { } m ? N(m) : new SBool(false)),
            T("solar-constant", N(SolarConstant)), T("sky-transmittance", N(SkyTransmittance)),
            T("air-mass-exponent", N(AirMassExponent)),
            T("sol", N(Sol)), T("year", N(Year)), T("obliquity", N(Obliquity)),
        };
        if (DailyTemperature is { } d) items.Add(T("daily-temperature", N(d.Min), N(d.Max), N(d.PeakHour)));
        items.Add(T("sky-color", N(SkyColor.X), N(SkyColor.Y), N(SkyColor.Z)));
        items.Add(T("ground-color", N(GroundColor.X), N(GroundColor.Y), N(GroundColor.Z)));
        return new SList(items);
    }

    /// <summary>The numbers a planet's preset can be overridden by name: (planet mars #:gravity 9.81), in Racket and in the editor alike.</summary>
    public static readonly string[] NumberKeys =
        ["gravity", "pressure", "temperature", "solar-constant", "sky-transmittance", "air-mass-exponent", "sol", "year", "obliquity"];

    public double Number(string key) => key switch
    {
        "gravity" => Gravity, "pressure" => Pressure, "temperature" => Temperature,
        "solar-constant" => SolarConstant, "sky-transmittance" => SkyTransmittance, "air-mass-exponent" => AirMassExponent,
        "sol" => Sol, "year" => Year, "obliquity" => Obliquity,
        _ => throw new FormatException($"a planet has no number {key}; it has {string.Join(", ", NumberKeys)}"),
    };

    /// <summary>This planet with one number changed, checked as define-machine's (planet …) checks it.</summary>
    public Planet With(string key, double v)
    {
        bool ok = key switch
        {
            "gravity" or "air-mass-exponent" or "sol" => v > 0,
            "pressure" or "solar-constant" => v >= 0,
            "temperature" => v > -273.15,
            "sky-transmittance" => v is >= 0 and <= 1,
            "year" => v >= 1,
            "obliquity" => v is >= 0 and <= 90,
            _ => throw new FormatException($"a planet has no number {key}; it has {string.Join(", ", NumberKeys)}"),
        };
        if (!ok) throw new FormatException($"planet #:{key} {v} is out of range");
        return key switch
        {
            "gravity" => this with { Gravity = v }, "pressure" => this with { Pressure = v },
            "temperature" => this with { Temperature = v }, "solar-constant" => this with { SolarConstant = v },
            "sky-transmittance" => this with { SkyTransmittance = v }, "air-mass-exponent" => this with { AirMassExponent = v },
            "sol" => this with { Sol = v }, "year" => this with { Year = v }, _ => this with { Obliquity = v },
        };
    }

    /// <summary>A new mixture, fractions adding up to 1; it has its own mean molar mass.</summary>
    public Planet WithAir(GasMix air)
    {
        if (Math.Abs(air.Total - 1) > 0.001) throw new FormatException($"a planet's #:air fractions must add up to 1, got {air.Total}");
        return this with { Air = air, MolarMassOverride = null };
    }

    /// <summary>Is it Earth, number for number? Then a machine writes no planet clause.</summary>
    public bool IsEarth => this == Earth;
}

/// <summary>
/// A region with its own air, which every part reads its conditions from:
/// gravity (the planet's), pressure, temperature, gas mix and so density.
/// The planet's open air is the outermost zone (issue #38); enclosures with
/// their own air are zones inside it (issue #39). A new zone is Earth's
/// open air at 20 °C, so a part built on its own behaves as it always has.
/// </summary>
public class Zone
{
    public Zone() : this(Planet.Earth, 20) { }

    public Zone(Planet planet, double temperatureC)
    {
        _planet = planet;
        Pressure = planet.Pressure;
        Temperature = temperatureC;
    }

    private Planet _planet;
    /// <summary>The planet this zone stands on. Changing it (live) keeps the zone's pressure only if it was the planet's own.</summary>
    public virtual Planet Planet
    {
        get => _planet;
        set
        {
            if (Pressure == _planet.Pressure) Pressure = value.Pressure;
            _planet = value;
        }
    }

    public double Gravity => Planet.Gravity;                         // m/s²
    public virtual double Pressure { get; set; }                     // Pa, absolute
    public virtual double Temperature { get; set; }                  // °C
    public virtual GasMix Air => Planet.Air;
    public virtual double MolarMass => Planet.MolarMass;             // kg/mol
    public double AirGasConstant => Physics.GasConstant / MolarMass; // J/(kg·K)

    /// <summary>kg/m³ at the zone's own temperature: P·M / (R·T).</summary>
    public double AirDensity => AirDensityAt(Temperature);

    /// <summary>kg/m³ of this zone's air at its pressure and <paramref name="celsius"/>.</summary>
    public double AirDensityAt(double celsius) => Pressure / (AirGasConstant * Physics.ToKelvin(celsius));

    /// <summary>The fraction of the air that is oxygen, by volume.</summary>
    public double OxygenFraction => Air.O2 / Air.Total;

    /// <summary>°C at which open water boils here: where its vapour pressure reaches the zone's.</summary>
    public double BoilingPoint => Thermo.Boiler.SaturationTemperature(Pressure);

    /// <summary>Metres of water a gauge pressure holds up here: P / (ρg).</summary>
    public double PressureToHead(double gaugePa) => gaugePa / (Physics.WaterDensity * Gravity);
}
