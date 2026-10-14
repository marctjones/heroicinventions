namespace HeroicInventions.Sim.Game;

/// <summary>
/// Every number that makes drawn ground darker or lighter than its soil's colour, in the one place (#259, docs/art-direction.md 12.23,
/// docs/plans/ground-colour-plan.md): the hillshade from the map light, the 16% unlit share, loose, scoured and deposited cells, a worked
/// patch's dug and heaped tint, wet ground, and the slope tint's strength. The ground's code (TerrainView, TerrainView.Worked) and its
/// shader (these go to it as uniforms) read them here and nowhere else, so the rule "dark means light" (12.23) is changed, and tested
/// (GroundCuesTests), in one file. Next to <see cref="SoilLook"/>, which turns a soil's table colour into the drawn colour these start from.
/// <para>
/// Every value below is what the ground was drawn with before #259 (nothing on screen moved); the later slices of milestone 16 change
/// them (#260 bedrock hue, #261 warnings by hue, #263 disturbance inside the band, #265 the soils' own band). Pure maths, no Godot
/// types, so a test can pin it; the ground's colour texture and the shader do the same sums on the same numbers.
/// </para>
/// </summary>
public static class GroundCues
{
    // ---- the map's hillshade (what slopes turned from the map light do) -----------------------------------------------------------

    /// <summary>The raw hillshade is <c>Base + Gain * max(0, n.L)</c>, 0.45 on a slope turned right away from the map light to 1 facing it.</summary>
    public const float HillShadeBase = 0.45f, HillShadeGain = 0.55f;
    /// <summary>The shader lifts that 0.45..1 to this: x0.68 away to x1.12 facing, so slopes facing away still stand apart.</summary>
    public const float HillShadeLow = 0.68f, HillShadeHigh = 1.12f;
    /// <summary>The share of the lit colour the ground always gives off (emission), so it never goes below this of lit, at night too.</summary>
    public const float UnlitShare = 0.16f;

    /// <summary>The raw hillshade of a surface whose normal makes cosine <paramref name="nDotLight"/> with the map light (TerrainView.HillShade).</summary>
    public static float HillShade(float nDotLight) => HillShadeBase + HillShadeGain * Math.Max(0, nDotLight);

    /// <summary>What the shader does with the raw hillshade: <c>mix(Low, High, clamp((raw - Base) / Gain, 0, 1))</c>, a multiplier of the lit colour.</summary>
    public static float HillShadeFactor(float raw) => HillShadeLow + (HillShadeHigh - HillShadeLow) * Math.Clamp((raw - HillShadeBase) / HillShadeGain, 0f, 1f);

    // ---- the map's cells (TerrainView.UpdateGround: the cell colour texture) ------------------------------------------------------

    /// <summary>Loose ground (spoil, slumped ground) is this much of the way to white.</summary>
    public const float Loose = 0.18f;
    /// <summary>Ground lowered since the start is darker by up to this, reached at <see cref="ChangeFullDepth"/> lowered.</summary>
    public const float Scoured = 0.45f;
    /// <summary>Ground raised since the start is up to this much of the way to <see cref="Cream"/>, reached at <see cref="ChangeFullDepth"/> raised.</summary>
    public const float Deposited = 0.6f;
    /// <summary>A change in height under this (m) leaves the cell as it was; one of <see cref="ChangeFullDepth"/> has the full effect.</summary>
    public const double ChangeThreshold = 0.002, ChangeFullDepth = 0.05;
    /// <summary>The pale cream deposits and heaps tend to (0 to 1 each).</summary>
    public static readonly (float R, float G, float B) Cream = (0.98f, 0.95f, 0.85f);

    /// <summary>A cell's drawn colour with the loose lightening and the scour or deposit of a change in height <paramref name="change"/> (m, negative: lowered).</summary>
    public static (float R, float G, float B) Cell((float R, float G, float B) soil, bool loose, double change)
    {
        var c = soil;
        if (loose) c = (c.R + (1 - c.R) * Loose, c.G + (1 - c.G) * Loose, c.B + (1 - c.B) * Loose);
        if (Math.Abs(change) > ChangeThreshold)
        {
            if (change < 0)
            {
                float k = 1 - Math.Clamp((float)(-change / ChangeFullDepth), 0f, Scoured);
                c = (c.R * k, c.G * k, c.B * k);
            }
            else
            {
                float t = Math.Clamp((float)(change / ChangeFullDepth), 0f, Deposited);
                c = (c.R + t * (Cream.R - c.R), c.G + t * (Cream.G - c.G), c.B + t * (Cream.B - c.B));
            }
        }
        return c;
    }

    // ---- a worked patch (TerrainView.Worked: the rover's dug and heaped ground, drawn by the same shader) -------------------------

    /// <summary>A patch node dug this deep (m) is dug fully; heaped this high, heaped fully. The dug tint is scaled by <see cref="PatchDugScale"/>.</summary>
    public const double PatchDugDepth = 0.2, PatchHeapedHeight = 0.1, PatchDugScale = 0.9;
    /// <summary>Spoil that has slumped a little, loose, shows at least this much as heaped (a floor on the tint, 0 to 1).</summary>
    public const double PatchLooseFloor = 0.25;
    /// <summary>The shader darkens fully dug ground by this (<c>lit * (1 + PatchDug * worked)</c>, worked from 0 to -1) and takes fully heaped ground this far to cream.</summary>
    public const float PatchDug = 0.55f, PatchHeaped = 0.45f;

    /// <summary>A patch node's tint from -1 (dug fully, scaled) to 1 (heaped fully), from its height change <paramref name="delta"/> (m) and whether it lies loose.</summary>
    public static double PatchTint(double delta, bool loose)
    {
        double tint = delta < 0 ? Math.Max(delta / PatchDugDepth, -1) * PatchDugScale : Math.Min(delta / PatchHeapedHeight, 1);
        return loose ? Math.Max(tint, PatchLooseFloor) : tint;
    }

    // ---- wet ground and the slope tint --------------------------------------------------------------------------------------------

    /// <summary>Ground with water on it is this much darker (the shader: <c>soil * (1 - Wet * damp)</c>) and as glossy as <see cref="WetRoughness"/>.</summary>
    public const float Wet = 0.4f, WetRoughness = 0.25f;
    /// <summary>
    /// The slope tint's share of the band colour in the ground's (grip), at its warm, amber and red ends: <c>lit = mix(lit, band, grip)</c>
    /// with grip up to this, and the band adds <see cref="TintEmission"/> of its colour as emission. (The bands' colours are
    /// TerrainView.SlopeWarm, SlopeAmber, SlopeRed, which stay the HUD's and the map's swatches.)
    /// </summary>
    public const float TintGripWarm = 0.38f, TintGripAmber = 0.5f, TintGripRed = 0.6f, TintEmission = 0.12f;

    /// <summary>The tint rule on a ground colour as it is today: the colour mixed <paramref name="grip"/> of the way to the band (the shader's <c>mix(lit, band, grip)</c>, without the emission).</summary>
    public static (float R, float G, float B) Tinted((float R, float G, float B) ground, (float R, float G, float B) band, float grip) =>
        (ground.R + (band.R - ground.R) * grip, ground.G + (band.G - ground.G) * grip, ground.B + (band.B - ground.B) * grip);

    /// <summary>The grip of the three bands at their full strength (warm, amber, red).</summary>
    public static float TintGrip(int band) => band switch { 0 => TintGripWarm, 1 => TintGripAmber, _ => TintGripRed };
}
