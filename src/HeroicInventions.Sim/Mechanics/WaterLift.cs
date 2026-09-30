using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// Water carried up by a turning machine — an Archimedes' screw or a
/// noria — from one tank to another. Each turn delivers
/// <see cref="VolumePerTurn"/> while the intake is under water; the engine
/// side sets <see cref="Rpm"/> from how fast the machine actually turns,
/// and applies <see cref="LoadTorque"/> back against it, so lifting water
/// is work the machine's driver has to do.
/// </summary>
public sealed class WaterLift(string name, Tank from, Tank to, double volumePerTurn,
                              double intakeElevation, double intakeDepth, double dischargeElevation)
{
    /// <summary>The air and gravity it stands in: the planet's open air unless it is inside an enclosure.</summary>
    public Zone Zone { get; set; } = new();
    public string Name { get; } = name;
    public Tank From { get; } = from;
    public Tank To { get; } = to;
    public double VolumePerTurn { get; } = volumePerTurn;      // m³
    public double IntakeElevation { get; } = intakeElevation;  // m: the intake's lowest point
    public double IntakeDepth { get; } = intakeDepth;          // m of water over it for a full scoop
    public double DischargeElevation { get; } = dischargeElevation; // m: where the water leaves

    /// <summary>Turning speed in the lifting sense (set by the engine); backwards counts as zero.</summary>
    public double Rpm { get; set; }

    /// <summary>
    /// For a pump, which works by stroke rather than turning: water per
    /// metre its piston rises (its bore's area). The engine side calls
    /// <see cref="Stroke"/> with how far the piston rose each step.
    /// </summary>
    public double VolumePerMetre { get; init; }
    public bool IsPump => VolumePerMetre > 0;
    private double _strokeThisStep;
    public void Stroke(double metresUp) => _strokeThisStep += Math.Max(0, metresUp);

    /// <summary>A pump's rod carries the weight of the water column it's raising: ρ·g·H·A. N.</summary>
    public double LoadForce => IsPump ? Physics.WaterDensity * Zone.Gravity * Head * VolumePerMetre * Fill : 0;
    public double Flow { get; private set; }                   // m³/s, last step

    /// <summary>How full each scoop is: the intake's depth under the source's surface, over what a full scoop needs.</summary>
    public double Fill => Math.Clamp((From.SurfaceElevation - IntakeElevation) / IntakeDepth, 0, 1);

    /// <summary>
    /// The torque it takes to lift the water: power ρ·g·Q·H over the speed
    /// ω, and since Q = V·ω/2π that is ρ·g·H·V/2π — the same at any speed.
    /// </summary>
    public double LoadTorque => Rpm > 0 ? Physics.WaterDensity * Zone.Gravity * Head * VolumePerTurn * Fill / (2 * Math.PI) : 0;

    /// <summary>From the source's surface up to where the water leaves the machine.</summary>
    public double Head => Math.Max(0, DischargeElevation - From.SurfaceElevation);

    public double Power => Physics.WaterDensity * Zone.Gravity * Flow * Head;

    public void Step(double dt)
    {
        double want = IsPump
            ? VolumePerMetre * Fill * _strokeThisStep
            : VolumePerTurn * Fill * Math.Max(0, Rpm) / 60 * dt;
        _strokeThisStep = 0;
        double moved = Math.Min(want, Math.Min(From.WaterVolume, To.Capacity - To.WaterVolume));
        moved = Math.Max(0, moved);
        From.WaterVolume -= moved;
        To.WaterVolume += moved;
        Flow = moved / dt;
    }

    /// <summary>
    /// The water one pocket of an inclined Archimedes' screw holds, per
    /// channel (one channel per start). Tilted, each helical channel dips
    /// and rises as it winds round; water collects in the dips, and a pocket
    /// can fill only to the lowest crest on its downhill side — past that,
    /// it spills back down the channel. So the pocket is the part of one
    /// turn of channel below that spill level. Integrated numerically over
    /// radius ρ, angle φ round the axle and axial position t across the
    /// channel's width (volume element ρ·dρ·dφ·dt).
    ///
    /// Height of a point in a channel, relative to its start:
    ///   h = (t + p·φ/2π)·sin θ + ρ·cos φ·cos θ
    /// Crests (dh/dφ = 0, h″ &lt; 0) sit at sin φ = p·tan θ / (2π·ρ); if that
    /// exceeds 1 at the core, the channel never dips there, water runs
    /// straight back down, and the screw lifts nothing — too steep.
    /// </summary>
    public static double ScrewPocketVolume(double outerRadius, double coreRadius, double pitch, int starts,
                                           double bladeThickness, double tiltDeg)
    {
        double theta = tiltDeg * Math.PI / 180;
        double width = pitch / starts - bladeThickness;
        double k = pitch * Math.Tan(theta) / (2 * Math.PI);
        if (width <= 0 || k >= coreRadius) return 0;
        double Height(double t, double phi, double rho) =>
            (t + pitch * phi / (2 * Math.PI)) * Math.Sin(theta) + rho * Math.Cos(phi) * Math.Cos(theta);
        double Crest(double rho) => Math.Asin(k / rho);

        const int nRho = 48, nPhi = 360, nT = 16;
        double dRho = (outerRadius - coreRadius) / nRho, dPhi = 2 * Math.PI / nPhi, dT = width / nT;
        double level = double.PositiveInfinity;
        for (int i = 0; i <= nRho; i++)
        {
            double rho = coreRadius + i * dRho;
            level = Math.Min(level, Height(0, Crest(rho), rho));
        }
        double volume = 0;
        for (int i = 0; i < nRho; i++)
        {
            double rho = coreRadius + (i + 0.5) * dRho;
            double start = Crest(rho);
            for (int j = 0; j < nPhi; j++)
            {
                double phi = start + (j + 0.5) * dPhi;
                for (int m = 0; m < nT; m++)
                    if (Height((m + 0.5) * dT, phi, rho) < level) volume += rho * dRho * dPhi * dT;
            }
        }
        return volume;
    }
}
