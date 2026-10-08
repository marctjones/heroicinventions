using System.Reflection;
using System.Text.Json;

namespace HeroicInventions.Sim.Materials;

public enum MaterialCategory { Wood, Stone, Metal, Fiber, Soil }

/// <summary>
/// Physical properties of a building material. Strengths are in MPa,
/// modulus in GPa, density in kg/m³. Wood is anisotropic, so it carries a
/// separate (much lower) tensile strength across the grain. Restitution
/// is the fraction of closing speed a collision returns (0 to 1).
/// </summary>
public sealed record MaterialDef(
    string Id,
    string Name,
    MaterialCategory Category,
    double Density,
    double YoungsModulus,
    double TensileStrength,
    double TensileStrengthAcrossGrain,
    double CompressiveStrength,
    double Friction,
    double Restitution)
{
    /// <summary>How it looks: an sRGB hex colour such as "#CC8F4A", or null for the game's fallback.</summary>
    public string? Color { get; init; }

    /// <summary>
    /// The surface the game draws on it (grain, cast, wrought, veined, …), or null to take the one
    /// its category implies — so a material added without a look still gets a fitting one.
    /// </summary>
    public string? Finish { get; init; }

    /// <summary>Melting point in °C for the metals (null for the rest); a pressure shell loses strength towards it.</summary>
    public double? MeltingPoint { get; init; }

    /// <summary>Specific heat in J/(kg·K) (issue #71), or null where the table gives none.</summary>
    public double? SpecificHeat { get; init; }

    /// <summary>Thermal conductivity in W/(m·K) (issue #71), or null where the table gives none.</summary>
    public double? Conductivity { get; init; }

    /// <summary>Linear thermal expansion in 10⁻⁶ per kelvin (issue #97), near 20 °C, or null where the table gives none.</summary>
    public double? Expansion { get; init; }

    /// <summary>Mass in kg of a solid of this material with the given volume in m³.</summary>
    public double MassOf(double volumeM3) => Density * volumeM3;

    /// <summary>Returns true if a member of the given cross-section breaks under an axial tensile load.</summary>
    public bool FailsInTension(double forceN, double areaM2, bool acrossGrain = false)
    {
        double stressMPa = forceN / areaM2 / 1e6;
        return stressMPa > (acrossGrain ? TensileStrengthAcrossGrain : TensileStrength);
    }
}

public sealed class MaterialLibrary
{
    private readonly Dictionary<string, MaterialDef> _byId;

    private MaterialLibrary(IEnumerable<MaterialDef> materials) =>
        _byId = materials.ToDictionary(m => m.Id);

    public IReadOnlyCollection<MaterialDef> All => _byId.Values;

    public MaterialDef this[string id] => _byId[id];

    public bool TryGet(string id, out MaterialDef material) => _byId.TryGetValue(id, out material!);

    /// <summary>Loads the built-in material table embedded in this assembly.</summary>
    public static MaterialLibrary LoadDefault()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("materials.json")
            ?? throw new InvalidOperationException("materials.json resource missing");
        return Load(stream);
    }

    public static MaterialLibrary Load(Stream json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var file = JsonSerializer.Deserialize<MaterialFile>(json, options)
            ?? throw new InvalidDataException("Empty material file");
        return new MaterialLibrary(file.Materials);
    }

    private sealed record MaterialFile(List<MaterialDef> Materials);
}
