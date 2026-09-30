using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A water wheel on a horizontal axle, turning a load — a millstone,
/// grinding against a steady <see cref="Load"/> torque. Two ways to drive it:
///
/// Overshot: a channel pours onto the top (<see cref="Pour"/>) and the
/// buckets carry the water down the descending side, spilling it once they
/// have turned <see cref="SpillAngle"/> from the top. The water on the
/// wheel, W, lies spread evenly over that arc, so its weight turns the
/// wheel with τ = W·g·r·(1 − cos θ)/θ. Water leaves the arc as fast as the
/// wheel carries it off, W·ω/θ, so in steady running W = ρQθ/ω and the
/// wheel gives ρ·g·Q·r·(1 − cos θ) whatever its speed: the weight of the
/// water falling through the height the buckets hold it, r(1 − cos θ). The
/// buckets hold only so much, <see cref="Capacity"/>: more than that spills
/// as it arrives, and a wheel loaded past what full buckets can turn stalls.
///
/// Undershot: the wheel stands in a channel (<see cref="Race"/>), whose
/// current v pushes on the paddles dipping into it with ρ·A·(v − u)·|v − u|,
/// u = ω·r the paddles' own speed and A their wetted area — the momentum of
/// the water striking them relative to the paddle. That gives most power at
/// u = v/3: (4/27)·ρ·A·v³, 8/27 of the stream's kinetic energy through A —
/// the classic undershot ceiling of about 30%. The race's own flow isn't
/// slowed by the wheel; it runs as the channel sets it.
/// </summary>
public sealed class WaterWheel(string name, double radius, double width, double momentOfInertia) : IShaft
{
    /// <summary>The air and gravity it stands in: the planet's open air unless it is inside an enclosure.</summary>
    public Zone Zone { get; set; } = new();
    public string Name { get; } = name;
    public double Radius { get; } = radius;                   // m, to the buckets / paddles' middle
    public double Width { get; } = width;                     // m
    public double MomentOfInertia { get; } = momentOfInertia; // kg·m²
    public double Load { get; set; }                          // N·m the millstone resists with while turning

    // overshot
    public double SpillAngle { get; init; } = 120 * Math.PI / 180;  // rad from the top: buckets start tipping near 90°, are empty by 150°
    public double BucketVolume { get; init; }                 // m³ each
    public int Buckets { get; init; }
    public Tank? Tail { get; init; }                          // where spilled water lands, if in the scene
    /// <summary>The most water the loaded arc can hold, kg.</summary>
    public double Capacity => Buckets * BucketVolume * Physics.WaterDensity * SpillAngle / (2 * Math.PI);

    // undershot
    public Channel? Race { get; init; }
    public double PaddleDepth { get; init; }                  // m

    public double AngularVelocity { get; private set; }      // rad/s
    public double Angle { get; private set; }                 // rad
    public double Water { get; private set; }                 // kg on the descending arc
    public double Torque { get; private set; }                // N·m from the water, last step
    public double Work { get; private set; }                  // J done on the load
    public double Taken { get; private set; }                 // kg of water the buckets caught
    public double Overflow { get; private set; }              // kg that spilled on arrival: buckets full
    public double Rpm => AngularVelocity * 60 / (2 * Math.PI);
    public double Power => Load * AngularVelocity;             // W into the millstone
    public double KineticEnergy => 0.5 * MomentOfInertia * AngularVelocity * AngularVelocity;

    double IShaft.ShaftInertia => MomentOfInertia;
    /// <summary>A shaft from another machine turning it (issue #78).</summary>
    public void AddAngularImpulse(double impulse) => AngularVelocity += impulse / MomentOfInertia;

    private double _poured;                                    // kg arrived since the last step

    /// <summary>Water poured onto the wheel's top, m³.</summary>
    public void Pour(double m3) => _poured += Math.Max(0, m3) * Physics.WaterDensity;

    /// <summary>The undershot paddles' wetted area: as deep as the race runs, up to the paddle.</summary>
    public double WettedArea => Race is null ? 0 : Math.Min(Width, Race.Width) * Math.Min(PaddleDepth, Race.Depth);

    public void Step(double dt)
    {
        // what lands in the buckets, and what they can't hold
        double caught = Math.Min(_poured, Math.Max(0, Capacity - Water));
        double spilt = _poured - caught;
        _poured = 0;
        Water += caught;
        Taken += caught;
        Overflow += spilt;

        Torque = Water * Zone.Gravity * Radius * (1 - Math.Cos(SpillAngle)) / SpillAngle;
        if (Race is not null)
        {
            double slip = Race.Velocity - AngularVelocity * Radius;
            Torque += Physics.WaterDensity * WettedArea * slip * Math.Abs(slip) * Radius;
        }

        // the millstone holds still until the water can turn it
        if (AngularVelocity <= 1e-9 && Torque <= Load) AngularVelocity = 0;
        else
        {
            double before = AngularVelocity;
            AngularVelocity = Math.Max(0, AngularVelocity + (Torque - Load) / MomentOfInertia * dt);
            Work += Load * (before + AngularVelocity) / 2 * dt;
            Angle = (Angle + AngularVelocity * dt) % (2 * Math.PI);
        }

        // the buckets tip out as they pass the spill point
        double emptied = Math.Min(Water, Water * AngularVelocity * dt / SpillAngle);
        Water -= emptied;
        if (Tail is not null) Tail.WaterVolume = Math.Min(Tail.Capacity, Tail.WaterVolume + (emptied + spilt) / Physics.WaterDensity);
    }
}
