using Godot;
using HeroicInventions.Sim;

namespace HeroicInventions;

/// <summary>
/// The sky and its light, worked out from the scene's conditions alone (issue #104, docs/art-direction.md
/// sections 3 and 6): which planet, where its sun stands, how much air and dust the beam crosses, how warm
/// the air is. <see cref="Of"/> is a pure function, so the same conditions always give the same sky, and
/// conditions no one planned for (a dust storm at dawn, frost under a low sun) still come out consistent.
///
/// The rules, each one physical enough to predict a frame before taking it:
/// <list type="bullet">
/// <item>The sun's colour is the beam left after crossing the air: each channel falls as e^(−k·AM), blue
/// fastest on Earth (Rayleigh) and on Mars (dust absorbs blue), so a low sun reddens by itself.</item>
/// <item>Its strength falls the same way, and in a storm by a further e^(−Δτ·AM) (the sim's own law), while
/// the sky's diffuse light stays: so in a storm shadows fade out and the scene is lit flat from the sky.</item>
/// <item>Daylight runs from civil twilight (sun 6° below the horizon) to full at 10° above.</item>
/// <item>The haze is the horizon's colour and thickens with dust.</item>
/// <item>The sun's disc is 0.53° across at Earth's distance and shrinks as the light weakens with distance
/// (angular size ∝ √ of the solar constant): 0.35° on Mars.</item>
/// <item>Mars's sky is butterscotch by day, and blue only round a low sun (dust scatters blue forward):
/// the one cool accent on Mars, drawn by a second, sky-only light (<see cref="Halo"/>).</item>
/// </list>
/// </summary>
public readonly record struct SkyLook(
    Color Zenith, Color Horizon, Color GroundHorizon, Color Ground,
    Color Light, float LightEnergy,
    float AmbientEnergy, Color NightAmbient, float NightShare,
    float Fog, float SunSizeDeg,
    Color Halo, float HaloEnergy)
{
    // Palette, art direction section 3
    // Earth's sky a clear blue, more saturated than section 3's #5B7FA8 / #C9D6E3, which measured as grey on
    // screen (owner feedback 2026-10-07: "hard to see many of the machines")
    private static readonly Color EarthZenith = Color.FromHtml("#3F74B5"), EarthHorizon = Color.FromHtml("#B4CDE6");
    private static readonly Color EarthGround = Color.FromHtml("#B9B4AA");
    private static readonly Color MarsZenith = Color.FromHtml("#B98B67"), MarsHorizon = Color.FromHtml("#D9A57C");
    private static readonly Color MarsLight = Color.FromHtml("#FFE2C0"), MarsHalo = Color.FromHtml("#8FA3B8");
    private static readonly Color StormDust = Color.FromHtml("#6E4B35");
    private static readonly Color NightZenith = new(0.02f, 0.03f, 0.07f), NightHorizon = new(0.06f, 0.07f, 0.11f);

    /// <summary>The studio's sun when a scene sets none: 50° up (the light's −50° pitch), clear air.</summary>
    public const double StudioElevation = 50;

    /// <param name="elevationDeg">The sun's height above the horizon, degrees; negative below it.</param>
    /// <param name="airMass">Thicknesses of air the beam crosses (the sim's Sun.AirMass; 1 overhead).</param>
    /// <param name="extraDust">Dust optical depth over a clear sky's, from a storm (Sun.ExtraDust).</param>
    /// <param name="airC">The air's temperature, °C: cold thins the light blue, heat yellows it.</param>
    /// <param name="partsValue">How light the machine's own materials look on average (luminance, 0 to 1), so the
    /// ground can stand apart from them; see <see cref="GroundFor"/>.</param>
    public static SkyLook Of(Planet planet, double elevationDeg, double airMass, double extraDust, double airC, double partsValue = 0.5)
    {
        bool earth = planet.IsEarth;
        float el = (float)elevationDeg;
        float am = Mathf.Clamp((float)airMass, 1, 38);              // past ~38 the sun is on the horizon
        float dust = Mathf.Max(0, (float)extraDust);

        // daylight: none below civil twilight, full by 10° up
        float day = Mathf.SmoothStep(-6, 10, el);
        float sunUp = Mathf.SmoothStep(-1, 3, el);                   // the beam itself: gone once the disc sets
        float low = day * (1 - Mathf.Clamp(el / 25, 0, 1));          // how much a low sun colours the sky
        float murk = 1 - Mathf.Exp(-dust / 2);                       // 0 clear, 0.99 by τ ≈ 10

        // the beam: per-channel extinction relative to the sun overhead, so noon light is the planet's own white
        var k = earth ? new Vector3(0.03f, 0.08f, 0.2f) : new Vector3(0.05f, 0.09f, 0.16f);
        var t = new Vector3(Mathf.Exp(-k.X * (am - 1)), Mathf.Exp(-k.Y * (am - 1)), Mathf.Exp(-k.Z * (am - 1)));
        float peak = Mathf.Max(t.X, Mathf.Max(t.Y, t.Z));
        var white = earth ? Colors.White : MarsLight;
        var light = new Color(white.R * t.X / peak, white.G * t.Y / peak, white.B * t.Z / peak);
        float cold = Mathf.Clamp((20 - (float)airC) / 30, 0, 1), hot = Mathf.Clamp(((float)airC - 20) / 15, 0, 1);
        light = light.Lerp(new Color(0.88f, 0.92f, 1f), cold * 0.5f).Lerp(new Color(1f, 0.9f, 0.74f), hot * 0.5f);
        light = light.Lerp(new Color(0.8f, 0.55f, 0.35f), murk * 0.5f);

        // Mars's sun is weaker by the ratio of solar constants (586 W/m² against 1,361), floored so Mars reads
        // dim, not murky (art direction section 6)
        float planetScale = earth ? 1 : Mathf.Clamp((float)(0.4 + 0.6 * planet.SolarConstant / Sim.Thermo.Sun.EarthSolarConstant), 0.45f, 1.2f);
        float beam = t.Y * Mathf.Exp(-dust * am);
        float energy = planetScale * sunUp * Mathf.Max(beam, 0) * (1 - 0.2f * cold);

        // the sky
        Color zenith = earth ? EarthZenith : MarsZenith, horizon = earth ? EarthHorizon : MarsHorizon;
        zenith = zenith.Lerp(new Color(0.55f, 0.62f, 0.72f), cold * 0.5f);
        horizon = horizon.Lerp(new Color(0.82f, 0.85f, 0.9f), cold * 0.5f).Lerp(new Color(0.9f, 0.82f, 0.68f), hot * 0.4f);
        // a low sun: Earth's horizon flushes orange and the zenith deepens; Mars's whole sky dims to a dusky brown
        horizon = horizon.Lerp(earth ? new Color(0.95f, 0.6f, 0.38f) : new Color(0.55f, 0.4f, 0.32f), low * 0.6f);
        zenith = zenith.Lerp(earth ? new Color(0.25f, 0.33f, 0.5f) : new Color(0.35f, 0.27f, 0.24f), low * 0.5f);
        // a storm thickens the sky to a dim brown, darker the deeper the dust (art direction section 3)
        float stormDim = 1 - 0.55f * murk;
        zenith = zenith.Lerp(StormDust * stormDim, murk * 0.85f);
        horizon = horizon.Lerp(StormDust.Lightened(0.15f) * stormDim, murk * 0.85f);
        // night
        zenith = NightZenith.Lerp(zenith, day);
        horizon = NightHorizon.Lerp(horizon, day);
        var groundHorizon = (earth ? EarthGround : new Color((float)planet.GroundColor.X, (float)planet.GroundColor.Y, (float)planet.GroundColor.Z))
            .Lerp(horizon, 0.5f) * Mathf.Lerp(0.15f, 1, day);

        // Ambient comes from the sky's own colours, so it already dims at night and browns in a storm. At night
        // a faint cool fill keeps machines silhouetted rather than gone (art direction section 6).
        float night = 1 - day;

        // haze: Mars's air carries dust even when clear; a storm closes the view to tens of metres
        float fog = (earth ? 0.0025f : 0.004f) + 0.003f * Mathf.Min(dust, 12);

        float sunSize = 0.53f * Mathf.Sqrt((float)(planet.SolarConstant / Sim.Thermo.Sun.EarthSolarConstant));

        // Mars's blue aureole round a low sun, gone in a storm (the dust that makes it then hides it)
        float halo = earth ? 0 : sunUp * (1 - Mathf.Clamp((el - 2) / 18, 0, 1)) * (1 - murk);

        return new SkyLook(zenith, horizon, groundHorizon, GroundFor(planet, partsValue), light, energy,
                           AmbientEnergy: 0.3f, NightAmbient: new Color(0.16f, 0.2f, 0.3f), NightShare: night,
                           Fog: fog, SunSizeDeg: sunSize, Halo: MarsHalo, HaloEnergy: halo * 1.2f);
    }

    /// <summary>
    /// The ground under a machine, chosen to stand apart from it (the figure-ground rule). A machine of mostly
    /// dark and mid-toned parts (oak, iron, bronze) stands on a light ground; one of mostly pale parts (limestone,
    /// marble) on a dark one, so whatever the mix, its parts differ from what is behind them by a clear step in
    /// value. The hue is the planet's, its saturation held down so the parts' warm colours stay their own: Earth's
    /// studio a cool grey, Mars a dark, greyed rust (Meridiani's plain is basaltic sand, darker than its dust).
    /// </summary>
    public static Color GroundFor(Planet planet, double partsValue)
    {
        // Light unless the parts are clearly pale: wood, bronze and iron, most machines, are dark to mid-toned
        bool light = partsValue < 0.66;
        // nearly neutral, so blue water and glass stand out from it by hue as well as value
        if (planet.IsEarth) return Color.FromHsv(0.1f, 0.03f, light ? 0.8f : 0.32f);
        // Mars: always dark, since its butterscotch sky is pale and a light ground would merge with it; the planet's
        // rust greyed well down, so wood and bronze stand lighter than it and aren't orange on orange
        var g = new Color((float)planet.GroundColor.X, (float)planet.GroundColor.Y, (float)planet.GroundColor.Z);
        return Color.FromHsv(g.H, g.S * 0.45f, 0.18f);
    }
}
