namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A hopper of grain draining through a round orifice at its foot (issue #50): sand, millet,
/// regolith. Grain does not drain like water. Its flow is independent of how deep it is, because
/// the grain packs into arches above the orifice and the pressure at the bottom stops growing
/// with depth (Janssen), so the hopper empties at a steady rate, Beverloo's law:
/// W = C·ρ_b·√g·(D − k·d)^(5/2), for an orifice D across and grains of diameter d, C ≈ 0.58
/// and k ≈ 1.5 for round grains. The level falls linearly, dh/dt = −W/(ρ_b·A), against water's
/// √h curve (Torricelli, <see cref="Fluids.TankLeak"/>). The flow scales as √g, and stops
/// altogether when the orifice is under about five grain diameters across: it arches over.
/// </summary>
public sealed class Hopper
{
    public const double C = 0.58, K = 1.5, ArchingDiameters = 5;

    public Hopper(string id, double area, double grainMass, double orifice, double grainSize, double bulkDensity, double gravity)
    {
        Id = id; Area = area; Mass = grainMass; Orifice = orifice; GrainSize = grainSize; BulkDensity = bulkDensity; Gravity = gravity;
        StartMass = grainMass;
    }

    public string Id { get; }
    /// <summary>Cross-section of the hopper, m².</summary>
    public double Area { get; }
    /// <summary>Orifice diameter, m.</summary>
    public double Orifice { get; set; }
    /// <summary>Grain diameter, m.</summary>
    public double GrainSize { get; set; }
    /// <summary>Bulk density of the grain, kg/m³ (sand is about 1600).</summary>
    public double BulkDensity { get; }
    public double Gravity { get; set; }

    /// <summary>Grain left, kg.</summary>
    public double Mass { get; set; }
    public double StartMass { get; }
    /// <summary>Grain that has run out, kg.</summary>
    public double Drained { get; private set; }

    /// <summary>How deep the grain stands, m: mass / (ρ_b·A).</summary>
    public double Level => Mass / (BulkDensity * Area);
    public bool Empty => Mass <= 0;
    /// <summary>True when the orifice is under five grain diameters: it arches over and nothing comes out.</summary>
    public bool Arched => Orifice < ArchingDiameters * GrainSize;

    /// <summary>The steady mass flow, kg/s, Beverloo's: C·ρ_b·√g·(D − k·d)^(5/2), zero when arched. It does not depend on the level.</summary>
    public double Flow => Arched || Empty ? 0 : FlowFor(Orifice, GrainSize, BulkDensity, Gravity);

    /// <summary>Beverloo's law for an orifice D and grain d, in a bulk density ρ under gravity g.</summary>
    public static double FlowFor(double orifice, double grainSize, double bulkDensity, double gravity)
    {
        double effective = orifice - K * grainSize;
        return effective <= 0 ? 0 : C * bulkDensity * Math.Sqrt(gravity) * Math.Pow(effective, 2.5);
    }

    /// <summary>How fast the surface, and anything resting on it, sinks, m/s: W / (ρ_b·A). Steady while there is grain.</summary>
    public double SurfaceSpeed => Flow / (BulkDensity * Area);

    public void Step(double dt)
    {
        double out_ = Math.Min(Mass, Flow * dt);
        Mass -= out_;
        Drained += out_;
        if (Mass < 1e-12) Mass = 0;
    }
}
