using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Game;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// The ground colour rule (#259, docs/art-direction.md 12.23, docs/plans/ground-colour-plan.md section 3A): "dark means light". Ground is
/// darker only where less light falls on it (hillshade, shadow, dusk, standing water); what a soil is and what has been done to it change hue
/// and marks inside one band of lightness; a warning changes hue, never lightness. <see cref="GroundCues"/> holds every number that darkens
/// or lightens ground, so these tests can pin the rule.
/// <para>
/// Milestone 16 lands the rule in slices. The tests below that pass at today's values are live; the ones that only pass after a later slice
/// are skipped, each naming the issue whose change makes it pass (#260 bedrock by hue, #261 warnings by hue, #263 disturbance inside the
/// band, #265 the six soils in one band): that slice removes the skip, so a skipped test is the slice's acceptance check, already written.
/// </para>
/// </summary>
public class GroundCuesTests
{
    private const string Warnings = "#261: warnings by hue, not lightness (the luminance-preserving tint)";
    private const string Disturbance = "#263: disturbance inside the band (the cue constants move to 12.23's values)";
    private const string Retable = "#265: the soils in one band of lightness (the owner-gated re-table, floor lifted to 80)";

    private static readonly string[] CraterSoils = ["basalt-sand", "bedrock", "regolith", "sublimed-regolith", "ice-cemented-regolith", "silica-sand"];

    private static Terrain Victoria() => Terrain.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "maps", "victoria.map")), "victoria");

    private static (float R, float G, float B) Drawn(string material)
    {
        var hex = MaterialLibrary.LoadDefault()[material].Color!;
        return SoilLook.Drawn(Convert.ToInt32(hex[1..3], 16) / 255f, Convert.ToInt32(hex[3..5], 16) / 255f, Convert.ToInt32(hex[5..7], 16) / 255f);
    }

    private static double Lum((float R, float G, float B) c) => SoilLook.Luminance(c.R, c.G, c.B);

    private static (double Hue, double Sat) HueSat((float R, float G, float B) c)
    {
        float max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B)), d = max - min;
        if (d <= 0) return (0, 0);
        double h = max == c.R ? ((c.G - c.B) / d + (c.G < c.B ? 6 : 0)) : max == c.G ? (c.B - c.R) / d + 2 : (c.R - c.G) / d + 4;
        return (h * 60, d / max);
    }

    private static double Distance((float R, float G, float B) a, (float R, float G, float B) b) =>
        255 * Math.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));

    private static double HueGap(double a, double b) { double d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }

    // ---- live: what is already true, and the formulas the shader and the cell texture share ---------------------------------------------

    [Fact]
    public void TheHillshadeIsKeptInItsBand()
    {
        // 12.23: the hillshade stays x0.68 (turned right away from the map light) to x1.12 (facing it), a lift of the raw 0.45 to 1
        Assert.Equal(0.45f, GroundCues.HillShade(-1), 5);
        Assert.Equal(0.45f, GroundCues.HillShade(0), 5);
        Assert.Equal(1f, GroundCues.HillShade(1), 5);
        Assert.Equal(0.68f, GroundCues.HillShadeFactor(GroundCues.HillShade(0)), 5);
        Assert.Equal(1.12f, GroundCues.HillShadeFactor(GroundCues.HillShade(1)), 5);
        Assert.Equal(1.12 / 0.68, GroundCues.HillShadeFactor(1f) / (double)GroundCues.HillShadeFactor(0.45f), 4);
        // level ground, by hand: the map light (0.55, 0.8, 0.4) has length sqrt(0.3025 + 0.64 + 0.16) = 1.05, so n.L = 0.8 / 1.05 = 0.7619 for n = up;
        // raw 0.45 + 0.55 x 0.7619 = 0.8690; lifted 0.68 + 0.44 x (0.8690 - 0.45) / 0.55 = 0.68 + 0.44 x 0.7619 = 1.0152
        Assert.Equal(1.0152f, GroundCues.HillShadeFactor(GroundCues.HillShade(0.8f / 1.05f)), 3);
        // and the ground is never lit below its unlit share: 16% of lit, at night too
        Assert.Equal(0.16f, GroundCues.UnlitShare, 5);
    }

    [Fact]
    public void WetGroundIsDarkerAndGlossyButNotBlack()
    {
        // standing water is a light effect (12.23 1.1): x0.6 today, x0.75 after the owner's call (plan section 5.4); either way a visible but modest step
        float factor = 1 - GroundCues.Wet;
        Assert.InRange(factor, 0.55f, 0.8f);
        Assert.InRange(GroundCues.WetRoughness, 0.1f, 0.4f);
    }

    [Fact]
    public void ACellsCuesAreTheFormulasTheShaderAndTheTextureShare()
    {
        var grey = (R: 0.5f, G: 0.5f, B: 0.5f);
        // untouched, and a change under 2 mm, leave the soil's colour as it is
        Assert.Equal(grey, GroundCues.Cell(grey, false, 0));
        Assert.Equal(grey, GroundCues.Cell(grey, false, 0.0015));
        // loose: 18% of the way to white (0.5 + 0.5 x 0.18 = 0.59)
        Assert.Equal(0.59f, GroundCues.Cell(grey, true, 0).R, 5);
        // scoured: darker by the lowered depth over 5 cm, to 45% at most (2.5 cm: 0.5 x (1 - 0.45) from 0.5 x 0.5; 1 cm lowered: 20%)
        Assert.Equal(0.5f * (1 - 0.2f), GroundCues.Cell(grey, false, -0.01).R, 4);
        Assert.Equal(0.5f * (1 - GroundCues.Scoured), GroundCues.Cell(grey, false, -0.5).R, 5);
        // deposited: toward cream by the raised height over 5 cm, to 60% at most (1 cm: 20% of the way to 0.98 from 0.5 = 0.596)
        Assert.Equal(0.5f + 0.2f * (0.98f - 0.5f), GroundCues.Cell(grey, false, 0.01).R, 4);
        Assert.Equal(0.5f + GroundCues.Deposited * (0.98f - 0.5f), GroundCues.Cell(grey, false, 0.5).R, 5);
    }

    [Fact]
    public void AWorkedPatchsTintRunsFromDugToHeaped()
    {
        Assert.Equal(0, GroundCues.PatchTint(0, false), 9);
        Assert.Equal(-0.9, GroundCues.PatchTint(-0.2, false), 9);      // 0.2 m dug is a full dig, scaled by 0.9
        Assert.Equal(-0.9, GroundCues.PatchTint(-1.0, false), 9);      // and no deeper
        Assert.Equal(-0.45, GroundCues.PatchTint(-0.1, false), 9);
        Assert.Equal(1, GroundCues.PatchTint(0.1, false), 9);          // 0.1 m heaped is full
        Assert.Equal(0.5, GroundCues.PatchTint(0.05, false), 9);
        Assert.Equal(0.25, GroundCues.PatchTint(0, true), 9);          // loose spoil shows at least a quarter heaped
        Assert.Equal(1, GroundCues.PatchTint(0.1, true), 9);
        Assert.Equal(0.25, GroundCues.PatchTint(-0.1, true), 9);
    }

    [Fact]
    public void TheSlopeTintMixesTheGroundTowardTheBandByItsGrip()
    {
        // today's rule (12.16 row 1): lit = mix(lit, band, grip), grip 0.38 warm, 0.5 amber, 0.6 red at full strength
        Assert.Equal([0.38f, 0.5f, 0.6f], new[] { GroundCues.TintGrip(0), GroundCues.TintGrip(1), GroundCues.TintGrip(2) });
        var t = GroundCues.Tinted((0.2f, 0.2f, 0.2f), (0.9f, 0.1f, 0f), 0.6f);
        Assert.Equal(0.2f + 0.7f * 0.6f, t.R, 5); Assert.Equal(0.2f - 0.1f * 0.6f, t.G, 5); Assert.Equal(0.2f - 0.2f * 0.6f, t.B, 5);
        Assert.Equal((0.2f, 0.2f, 0.2f), GroundCues.Tinted((0.2f, 0.2f, 0.2f), (0.9f, 0.1f, 0f), 0f));
    }

    // ---- skipped until the slice that makes them true -------------------------------------------------------------------------------

    [Fact(Skip = Retable)]
    public void EverySoilsDrawnLuminanceLiesInTheBand()
    {
        // 12.23 1.2: every soil's base colour 70 to 150 drawn, at most 2.2 : 1. The silica sand (174 drawn, renders 168) is exempt until the
        // re-table, since the plan's recommended table brings it to 150; the floor (basalt sand, 67) stays under 70 until then too.
        var lum = CraterSoils.Where(m => m != "silica-sand").ToDictionary(m => m, m => Lum(Drawn(m)));
        foreach (var (m, l) in lum) Assert.InRange(l, 70, 150);
        Assert.True(lum.Values.Max() / lum.Values.Min() <= 2.2, $"{lum.Values.Max() / lum.Values.Min():0.00} to 1");
    }

    [Fact]
    public void BedrockAndRegolithDifferInHueByAtLeastFifteenDegrees()
    {
        // 12.23 1.3: bedrock drawn #6A6459, olive-grey (hue 40, saturation 0.16), against the regolith's rust (hue 23, 0.48); its luminance stays
        // inside the old pinned band (99 to 100.5, SoilLookTests) and the colours are at least 35 bytes apart
        var bedrock = Drawn("bedrock"); var regolith = Drawn("regolith");
        Assert.Equal("#6A6459", SoilLook.Hex(bedrock.R, bedrock.G, bedrock.B));
        Assert.InRange(Lum(bedrock), 99, 101);
        Assert.True(HueGap(HueSat(bedrock).Hue, HueSat(regolith).Hue) >= 15, $"{HueGap(HueSat(bedrock).Hue, HueSat(regolith).Hue):0} degrees");
        Assert.True(Distance(bedrock, regolith) >= 35, $"{Distance(bedrock, regolith):0} bytes");
    }

    [Fact]
    public void SoilsThatMeetOnTheMapAreToldApartByTheRule()
    {
        // 12.23 1.5, day: two grounds that touch differ by 25 or more in luminance, or by 35 or more bytes of colour with different patterns
        // (every pair of soils that touch on victoria, 4-neighbours; the drawn colours, which is what a Sim test can see). Night's rule (12 / 18) is
        // the same shape and is measured on frames (tools/ground-check.sh).
        var t = Victoria();
        var pairs = new HashSet<(string, string)>();
        for (int j = 0; j < t.Nz; j++)
            for (int i = 0; i < t.Nx; i++)
            {
                int k = i + j * t.Nx;
                foreach (int n in new[] { i + 1 < t.Nx ? k + 1 : -1, j + 1 < t.Nz ? k + t.Nx : -1 })
                    if (n >= 0 && t.Soil[n] != t.Soil[k])
                    {
                        var a = t.Soils[t.Soil[k]].Material; var b = t.Soils[t.Soil[n]].Material;
                        pairs.Add(string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a));
                    }
            }
        Assert.True(pairs.Count >= 7, string.Join(", ", pairs));
        foreach (var (a, b) in pairs)
        {
            double gap = Math.Abs(Lum(Drawn(a)) - Lum(Drawn(b))), distance = Distance(Drawn(a), Drawn(b));
            bool patterns = SoilLook.PatternFor(a) != SoilLook.PatternFor(b);
            Assert.True(gap >= 25 || (distance >= 35 && patterns), $"{a} / {b}: {gap:0} apart in luminance, {distance:0} bytes of colour, patterns differ {patterns}");
        }
    }

    [Fact(Skip = Disturbance)]
    public void NoDisturbanceLeavesTheBand()
    {
        // 12.23 1.2: loose, scoured, deposited, dug and heaped ground are the soil's colour x0.80 to x1.20 in luminance, for every soil
        foreach (var m in CraterSoils)
        {
            var soil = Drawn(m); double l0 = Lum(soil);
            // the cell texture: loose, and the largest scour and deposit
            foreach (var (name, c) in new[] { ("loose", GroundCues.Cell(soil, true, 0)), ("scoured", GroundCues.Cell(soil, false, -1)), ("deposited", GroundCues.Cell(soil, false, 1)) })
                Assert.InRange(Lum(c) / l0, 0.8, 1.2);
            // the shader's worked patch: a full dig darkens by PatchDug, a full heap goes PatchHeaped of the way to cream
            Assert.InRange(1 - GroundCues.PatchDug, 0.8, 1.2);
            var heaped = (soil.R + GroundCues.PatchHeaped * (GroundCues.Cream.R - soil.R), soil.G + GroundCues.PatchHeaped * (GroundCues.Cream.G - soil.G), soil.B + GroundCues.PatchHeaped * (GroundCues.Cream.B - soil.B));
            Assert.InRange(Lum(heaped) / l0, 0.8, 1.2);
        }
    }

    [Fact(Skip = Disturbance)]
    public void OnlyLightDarkensBeyondTheBand()
    {
        // 12.23 1.4: in daylight only slopes turned from the map light (hillshade, x0.68), shadow and standing water darken by more than 20%;
        // nothing a soil is or a person does to it (the scour, a dug patch) does
        Assert.True(GroundCues.Scoured <= 0.20f, $"scoured {GroundCues.Scoured}");
        Assert.True(GroundCues.PatchDug <= 0.20f, $"dug patch {GroundCues.PatchDug}");
        Assert.True(GroundCues.Deposited <= 0.20f, $"deposited {GroundCues.Deposited}");
        Assert.True(GroundCues.PatchHeaped <= 0.20f, $"heaped patch {GroundCues.PatchHeaped}");
        Assert.True(GroundCues.Loose <= 0.12f, $"loose {GroundCues.Loose}");
    }

    [Fact(Skip = Warnings)]
    public void TheSlopeTintsAreOneLightnessOnTheGround()
    {
        // 12.23 1.6: the tint rule on a grey of luminance 100 gives the three bands (warm, amber, red) within 2 of 115 (untinted x 1.15),
        // and on every soil's drawn colour the tinted ground is 1.0 to 1.2 of the untinted in luminance. The bands' colours are TerrainView.SlopeWarm,
        // SlopeAmber, SlopeRed (#E8975A, #F5B82E, #E5362A).
        (float, float, float) Hex(string h) => (Convert.ToInt32(h[0..2], 16) / 255f, Convert.ToInt32(h[2..4], 16) / 255f, Convert.ToInt32(h[4..6], 16) / 255f);
        var bands = new[] { Hex("E8975A"), Hex("F5B82E"), Hex("E5362A") };
        float g = 100 / 255f;
        for (int b = 0; b < 3; b++)
            Assert.InRange(Lum(GroundCues.Tinted((g, g, g), bands[b], GroundCues.TintGrip(b))), 113, 117);
        foreach (var m in CraterSoils)
        {
            var soil = Drawn(m);
            for (int b = 0; b < 3; b++)
                Assert.InRange(Lum(GroundCues.Tinted(soil, bands[b], GroundCues.TintGrip(b))) / Lum(soil), 1.0, 1.2);
        }
    }
}
