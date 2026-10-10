using System.Globalization;
using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Game;

/// <summary>
/// How the ground's soils look and what a player calls them (#243, #244): the one place that turns a soil's table colour into the
/// colour the ground is drawn in, and a soil's name into words. The ground's shader (TerrainView), the navigation map's ground and its
/// key (NavMap) and the Driving section's "ground here" line all read it, so a swatch in the key is the colour on the ground by
/// construction, not by a second copy of the number. Pure maths, no Godot types, so a test can pin it.
/// <para>
/// A soil is drawn <see cref="Saturation"/> of the way to its table colour's own saturation, same hue and value: a little greyer than
/// the table's, so crates, the rover and machines keep their own colour against what they stand on while each layer keeps its hue.
/// </para>
/// </summary>
public static class SoilLook
{
    public const double Saturation = 0.85;

    /// <summary>The colour a soil is drawn in on the ground, from its table colour (0 to 1 each), same hue and value, 85% of the saturation.</summary>
    public static (float R, float G, float B) Drawn(float r, float g, float b)
    {
        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (max <= 0 || d <= 0) return (r, g, b);
        float s = d / max * (float)Saturation, v = max;
        float h = max == r ? ((g - b) / d + (g < b ? 6 : 0)) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;   // 0 to 6
        float c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
        return (int)h switch
        {
            0 => (c + m, x + m, m),
            1 => (x + m, c + m, m),
            2 => (m, c + m, x + m),
            3 => (m, x + m, c + m),
            4 => (x + m, m, c + m),
            _ => (c + m, m, x + m),
        };
    }

    /// <summary>Luminance 0 to 255 of a colour given 0 to 1 each, as tools/legibility.py measures a frame: 0.2126 R + 0.7152 G + 0.0722 B of the sRGB bytes.</summary>
    public static double Luminance(float r, float g, float b) => 255 * (0.2126 * r + 0.7152 * g + 0.0722 * b);

    /// <summary>A colour as the 8-bit hex the ground's colour texture holds, "#RRGGBB".</summary>
    public static string Hex(float r, float g, float b) => string.Create(CultureInfo.InvariantCulture, $"#{Byte(r):X2}{Byte(g):X2}{Byte(b):X2}");

    public static byte Byte(float v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

    /// <summary>What a soil is called, in words for a player, and a short note of how it looks and behaves.</summary>
    public readonly record struct Words(string Name, string Look, string Note);

    private static readonly Dictionary<string, Words> Plain = new()
    {
        ["basalt-sand"] = new("basalt sand", "dark", "dark grey sand of the crater floor"),
        ["bedrock"] = new("bedrock", "grey-brown", "solid rock of the crater wall: it cannot be dug"),
        ["regolith"] = new("regolith", "rust-brown", "loose rusty-brown soil"),
        ["sublimed-regolith"] = new("dried regolith", "orange-brown", "soil the ice has left: the weak wall and the rubble it sheds"),
        ["ice-cemented-regolith"] = new("ice-cemented soil", "pale", "pale soil frozen hard"),
        ["silica-sand"] = new("silica sand", "palest", "the palest ground, a bay of clean sand"),
    };

    /// <summary>
    /// The mark a soil is drawn with on the ground, on the navigation map and in its key, as a geologic map draws a unit in a colour and a
    /// lithologic pattern (FGDC-STD-013-2006, section 37; docs/art-direction.md 12.22), so two soils of near the same colour still tell
    /// apart. The number is the pattern's slot in the shaders (game/scripts/SoilPatterns.cs): keep the two in step.
    /// </summary>
    public enum Pattern
    {
        None = 0,
        /// <summary>Stipple, dots: sand (FGDC 607, massive sand or sandstone).</summary>
        Sand = 1,
        /// <summary>Short dashes and dots: fine soil, silt or loam (FGDC 616, silt or siltstone).</summary>
        Silt = 2,
        /// <summary>Rows of long thin dashes: clay (FGDC 620, clay or clay shale).</summary>
        Clay = 3,
        /// <summary>Angular triangles: rubble, talus, breccia (FGDC 605, breccia).</summary>
        Rubble = 4,
        /// <summary>Jointed blocks with a cross in some: bedrock, massive rock (FGDC 627's blocks and 721-728's crosses).</summary>
        Rock = 5,
        /// <summary>Pale bluish flecks: soil held by ice (no FGDC lithology; a glacier's blue and the silt's dashes).</summary>
        Ice = 6,
        /// <summary>Open rings and dots: loose spoil the rover has dug or tipped (FGDC 681, till or diamicton: unsorted).</summary>
        Spoil = 7,
    }

    /// <summary>The number of pattern slots (two RGBA textures' channels in the shaders).</summary>
    public const int PatternSlots = 8;

    private static readonly Dictionary<string, Pattern> Patterns = new()
    {
        ["sand"] = Pattern.Sand,
        ["basalt-sand"] = Pattern.Sand,
        ["silica-sand"] = Pattern.Sand,
        ["regolith"] = Pattern.Silt,
        ["loam"] = Pattern.Silt,
        ["clay"] = Pattern.Clay,
        ["sublimed-regolith"] = Pattern.Rubble,
        ["bedrock"] = Pattern.Rock,
        ["ice-cemented-regolith"] = Pattern.Ice,
    };

    /// <summary>
    /// A soil's pattern, by its material. Ground that lies loose keeps its soil's pattern (the slide's rubble is the sublimed regolith, drawn as
    /// rubble wherever it lies); only the ground the rover has dug or heaped (a worked patch) is drawn as <see cref="Pattern.Spoil"/>.
    /// </summary>
    public static Pattern PatternFor(string material) => Patterns.GetValueOrDefault(material, Pattern.None);

    /// <summary>
    /// The pattern a cell of a map is drawn with: its soil's, or, where a slide's rubble lies on rock (<see cref="Terrain.Covering"/>: the
    /// crater's collapse runs out over the bedrock apron), the rubble's, since what lies on top is what the mark shows. (The cell's colour is
    /// still its own soil's, lightened as loose: TerrainView.)
    /// </summary>
    public static Pattern PatternOf(Terrain t, int cell) =>
        t.Covering(cell) is { } c && t.Heights[cell] > c.RockTop + 0.01 ? PatternFor(t.Soils[c.Soil].Material) : PatternFor(t.SoilOf(cell).Material);

    /// <summary>A pattern's name in lowercase words, for the key's print line and the test.</summary>
    public static string PatternName(Pattern p) => p.ToString().ToLowerInvariant();

    /// <summary>A soil's words; a soil with none written is named from its material id, spaced out.</summary>
    public static Words WordsFor(string material) =>
        Plain.TryGetValue(material, out var w) ? w : new(material.Replace('-', ' '), "", "");

    /// <summary>The Driving section's line: "ground here: basalt sand (dark)", with ", loose" where it lies loose; "ground here: off the map" past the edge.</summary>
    public static string GroundHere(Terrain terrain, double x, double z)
    {
        if (terrain.SoilAt(x, z) is not { } at) return "ground here: off the map";
        var w = WordsFor(terrain.Soils[at.Soil].Material);
        return $"ground here: {w.Name}{(w.Look.Length > 0 ? $" ({w.Look})" : "")}{(at.Loose ? ", loose" : "")}";
    }

    /// <summary>The soils a map's key lists: those that lie under at least one cell (not every soil a map defines is placed), in the order the map lists them.</summary>
    public static IReadOnlyList<int> SoilsPresent(Terrain terrain)
    {
        var seen = new bool[terrain.Soils.Count];
        foreach (int s in terrain.Soil) if (s >= 0 && s < seen.Length) seen[s] = true;
        return [.. Enumerable.Range(0, seen.Length).Where(i => seen[i])];
    }
}
