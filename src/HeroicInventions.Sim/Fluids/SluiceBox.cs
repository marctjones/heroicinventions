namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// A sluice box (issue #53): a riffled trough set in a channel, fed crushed
/// ore at <see cref="Feed"/> kg/s while the water runs, grains all
/// <see cref="GrainSize"/> across, a <see cref="HeavyFraction"/> of them
/// dense (gold, <see cref="HeavyDensity"/>) and the rest light (sand,
/// <see cref="LightDensity"/>). The water drags on the grains with the bed
/// shear stress of the channel's flow, τ = ρ g R S_f (R the hydraulic
/// radius, S_f the friction slope, Manning's). A grain goes on down the
/// channel if the flow can lift it, Shields number τ / ((ρ_s − ρ) g d) above
/// 0.047; if not, it drops behind a riffle and stays. So the box keeps every
/// grain denser than the cut-off ρ + τ / (0.047 g d) and passes the rest:
/// placer miners' sorting, the gold pan's.
/// </summary>
public sealed class SluiceBox(string name, Channel channel, double feed, double grainSize, double heavyDensity, double heavyFraction, double lightDensity)
{
    public string Name { get; } = name;
    public Channel Channel { get; } = channel;
    public double Feed { get; set; } = feed;              // kg/s of ore while water runs
    public double GrainSize { get; } = grainSize;         // m
    public double HeavyDensity { get; } = heavyDensity;   // kg/m³
    public double HeavyFraction { get; } = heavyFraction; // of the ore's mass
    public double LightDensity { get; } = lightDensity;   // kg/m³

    public double KeptHeavy { get; private set; }     // kg caught behind the riffles
    public double KeptLight { get; private set; }
    public double PassedHeavy { get; private set; }   // kg washed on down the channel
    public double PassedLight { get; private set; }
    public double Fed => KeptHeavy + KeptLight + PassedHeavy + PassedLight;

    /// <summary>Pa, the flow's drag on the box's floor: ρ g R S_f, Manning's friction slope over the hydraulic radius.</summary>
    public double Shear
    {
        get
        {
            double h = Channel.Depth, u = Channel.Velocity, b = Channel.Width;
            if (h <= 0 || u <= 0) return 0;
            double r = b * h / (b + 2 * h), g = Channel.Gravity;
            double n = Channel.Dynamic ? Channel.Manning : Channel.Roughness;
            double n2 = n * n * Physics.Gravity / g;
            double sf = n2 * u * u / Math.Pow(r, 4.0 / 3);
            return Physics.WaterDensity * g * r * sf;
        }
    }

    /// <summary>kg/m³: grains denser than this can't be lifted by the flow and stay; lighter ones go on.</summary>
    public double Cutoff => Physics.WaterDensity + Shear / (ShallowWater2D.ShieldsThreshold * Channel.Gravity * GrainSize);

    public void Step(double dt)
    {
        if (Channel.Flow <= 1e-9 || Feed <= 0) return;
        double ore = Feed * dt, cut = Cutoff;
        double heavy = ore * HeavyFraction, light = ore - heavy;
        if (HeavyDensity > cut) KeptHeavy += heavy; else PassedHeavy += heavy;
        if (LightDensity > cut) KeptLight += light; else PassedLight += light;
    }
}
