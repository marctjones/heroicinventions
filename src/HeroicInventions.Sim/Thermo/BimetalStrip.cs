using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// A bimetal strip (issue #97): two metals bonded face to face, a cantilever clamped at one end, the default thermostat for
/// a heat bin (John Harrison used one in the H3 marine clock of 1759). The two layers want to grow by different amounts when
/// they warm, so the strip bends toward the layer that expands less. Timoshenko (1925) gives the curvature of a strip of two
/// layers, layer 1 the one that expands less (α₁ &lt; α₂), thicknesses t₁ and t₂, moduli E₁ and E₂, total thickness t = t₁ + t₂,
/// warmed by ΔT from the temperature at which it is straight:
///
///   κ = 6 (α₂ − α₁) ΔT (1 + m)² / ( t [ 3 (1 + m)² + (1 + m n) (m² + 1/(m n)) ] ),   m = t₁/t₂,  n = E₁/E₂.
///
/// For two equal layers of equal modulus (m = n = 1) this is κ = 3 Δα ΔT / (2 t). A cantilever of length L that bends on that
/// curvature moves its tip across the chord by
///
///   δ = (1 − cos κL) / κ  ≈  κ L² / 2   (κL small),   so for equal layers   δ ≈ 3 Δα ΔT L² / (4 t).
///
/// (The "3 Δα ΔT L² / (2 t)" of the issue is the curvature times L²; the tip moves half that.) The sign of δ is positive toward
/// the low-expansion side when the strip is warmer than <see cref="StraightAt"/>, negative when colder.
///
/// The strip is thin and small, so it takes the temperature of what it senses (a heat store or a room) with a lag: its own heat
/// capacity C = Σ ρ c V against the film to its surroundings, h A (<see cref="Contact"/>, W/(m²·K), over its whole surface), so
/// dT/dt = (T_sensed − T)/τ with τ = C / (h A). It does not load what it senses (it holds a few joules per kelvin against a bank's 16 kJ/K).
///
/// It works a lid: the lid is shut when the strip is at <see cref="ShutAt"/> °C, and opens linearly as the tip moves back from the
/// deflection it has there, fully open after <see cref="Travel"/> m of tip movement. The temperature span of that is Travel / (dδ/dT)
/// (<see cref="Span"/>). A linear strip, not a snap disc: a damper wants a steady proportional response and the strip's own lag
/// is the only delay; the bin's own #:sense hysteresis switch stays as the ideal limit it is compared with.
/// </summary>
public sealed class BimetalStrip
{
    public string Name { get; }
    public MaterialDef High { get; }          // the layer that expands more
    public MaterialDef Low { get; }           // the layer that expands less (the strip bends toward it when warmed)
    public double Length { get; set; }        // m
    public double Thickness { get; set; }     // m, both layers
    public double Width { get; set; }         // m
    public double HighShare { get; set; } = 0.5;   // the share of the thickness that is the high-expansion layer
    public double Contact { get; set; } = 10;      // W/(m²·K) to what it senses
    public double StraightAt { get; set; } = 20;   // °C at which the strip is flat
    public double ShutAt { get; set; } = 40;       // °C at which the lid it works is just shut
    public double Travel { get; set; } = 0.0021;  // m of tip movement from shut to wide open
    public Func<double>? Sensed { get; set; }
    public HeatBin? Bin { get; set; }
    /// <summary>The strip's own temperature, °C.</summary>
    public double Temperature { get; set; }

    public BimetalStrip(string name, MaterialDef high, MaterialDef low, double length, double thickness, double width)
    {
        Name = name; High = high; Low = low; Length = length; Thickness = thickness; Width = width;
        if (high.Expansion is null || low.Expansion is null)
            throw new ArgumentException($"{(high.Expansion is null ? high.Id : low.Id)} has no expansion coefficient in the material table");
    }

    private double Alpha(MaterialDef m) => m.Expansion!.Value * 1e-6;

    /// <summary>Timoshenko's curvature in 1/m for a strip ΔT kelvin above flat. Positive: bending toward the low-expansion layer.</summary>
    public static double Curvature(double alphaHigh, double alphaLow, double modulusHigh, double modulusLow,
                                   double thicknessHigh, double thicknessLow, double dT)
    {
        double m = thicknessLow / thicknessHigh, n = modulusLow / modulusHigh, t = thicknessHigh + thicknessLow;
        double a = (1 + m) * (1 + m);
        return 6 * (alphaHigh - alphaLow) * dT * a / (t * (3 * a + (1 + m * n) * (m * m + 1 / (m * n))));
    }

    /// <summary>Tip movement of a cantilever of length L on a circular arc of curvature κ.</summary>
    public static double TipDeflection(double kappa, double length) =>
        Math.Abs(kappa * length) < 1e-9 ? kappa * length * length / 2 : (1 - Math.Cos(kappa * length)) / kappa;

    public double CurvatureAt(double tempC) =>
        Curvature(Alpha(High), Alpha(Low), High.YoungsModulus, Low.YoungsModulus, Thickness * HighShare, Thickness * (1 - HighShare), tempC - StraightAt);

    public double DeflectionAt(double tempC) => TipDeflection(CurvatureAt(tempC), Length);

    public double NowCurvature => CurvatureAt(Temperature);
    /// <summary>The tip's deflection now, m.</summary>
    public double Deflection => DeflectionAt(Temperature);
    /// <summary>The deflection the tip has with the lid just shut.</summary>
    public double ShutDeflection => DeflectionAt(ShutAt);
    /// <summary>The lid opening the tip commands, 0 shut … 1 wide.</summary>
    public double Opening => Travel <= 0 ? 0 : Math.Clamp((ShutDeflection - Deflection) / Travel, 0, 1);
    /// <summary>The temperature below which the lid is wide open.</summary>
    public double OpenAt => ShutAt - Span;
    /// <summary>K of temperature from lid shut to lid wide open: travel / (dδ/dT).</summary>
    public double Span
    {
        get
        {
            double slope = (DeflectionAt(ShutAt + 1) - DeflectionAt(ShutAt - 1)) / 2;   // m/K (the arc is all but linear)
            return Math.Abs(slope) < 1e-15 ? double.PositiveInfinity : Travel / Math.Abs(slope);
        }
    }

    /// <summary>J/K of the strip: the two layers' ρ c V.</summary>
    public double HeatCapacity
    {
        get
        {
            double area = Length * Width;
            return area * Thickness * (HighShare * High.Density * (High.SpecificHeat ?? 400) + (1 - HighShare) * Low.Density * (Low.SpecificHeat ?? 400));
        }
    }

    /// <summary>m² of the strip's whole surface: both faces, both edges, both ends.</summary>
    public double Surface => 2 * (Length * Width + Length * Thickness + Width * Thickness);

    /// <summary>s: C / (h A).</summary>
    public double TimeConstant => Contact * Surface <= 0 ? double.PositiveInfinity : HeatCapacity / (Contact * Surface);

    /// <summary>Takes up what it senses (it starts at that temperature) and sets the lid.</summary>
    public void Reset()
    {
        if (Sensed is not null) Temperature = Sensed();
        if (Bin is not null) Bin.Open = Opening;
    }

    public void Step(double dt)
    {
        if (Sensed is null) return;
        double tau = TimeConstant;
        Temperature += (Sensed() - Temperature) * (double.IsInfinity(tau) ? 0 : 1 - Math.Exp(-dt / tau));
        if (Bin is not null) Bin.Open = Opening;
    }
}
