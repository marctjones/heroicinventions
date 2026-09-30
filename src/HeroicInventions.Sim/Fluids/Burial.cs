namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// A body buried in the ground (issue #54): a crate or a boulder lying under
/// a map's surface, held where it is until the ground over it is dug,
/// scoured or slid away.
///
/// To pull a cube of side s straight up out from under a cover D deep takes
/// its own weight, the weight of the soil column on its lid, γ D s², and the
/// soil's grip on its four sides, whose shear strength at depth z is
/// c + K₀ γ z tan φ, the soil pressing on the sides at rest with K₀ = 1 − sin φ
/// (Jaky): over the sides, from z = D to D + s,
///   4 s [ c s + K₀ γ tan φ ((D + s)² − D²) / 2 ].
/// Once the cover over its lid is thinner than a quarter of its height it
/// is no longer held: it is an ordinary loose body again.
/// </summary>
public static class Burial
{
    /// <summary>A body is free once the cover over it is thinner than this fraction of its height.</summary>
    public const double FreeCover = 0.25;

    public static double RestPressureCoefficient(SoilSpec soil) => 1 - Math.Sin(Math.Atan(soil.Friction));

    /// <summary>N to pull a cube of side <paramref name="size"/> and mass <paramref name="mass"/> straight up from under <paramref name="cover"/> m of the soil.</summary>
    public static double PullOut(double cover, double size, double mass, SoilSpec soil, double gravity, bool loose = false)
    {
        double d = Math.Max(0, cover), s = size, gamma = soil.Density * gravity;
        double c = loose ? 0 : soil.Cohesion;
        double sides = 4 * s * (c * s + RestPressureCoefficient(soil) * gamma * soil.Friction * ((d + s) * (d + s) - d * d) / 2);
        return mass * gravity + gamma * d * s * s + sides;
    }

    public static bool Held(double cover, double size) => cover > FreeCover * size;
}
