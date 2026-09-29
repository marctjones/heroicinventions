using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A vessel hanging on a rope wound round a spindle, against a
/// counterweight on a rope wound the other way — how Heron opened his
/// temple doors (Pneumatica I.38). While the vessel and its water outweigh
/// the counterweight, the vessel sinks and the spindle turns; lighter, the
/// counterweight wins and turns it back. The spindle turns between 0 and
/// <see cref="MaxTurn"/> (the doors shut, and wide open); each radian lets
/// the vessel down by <see cref="Radius"/>.
///
///   torque  (m_vessel + m_water − M) · g · r − friction
///   inertia (m_vessel + m_water + M) · r² + the doors' own
/// </summary>
public sealed class Counterpoise(string name, Tank vessel, double vesselMass, double counterweight, double radius, double maxTurn)
{
    public string Name { get; } = name;
    public Tank Vessel { get; } = vessel;
    public double VesselMass { get; } = vesselMass;           // kg, empty
    public double Counterweight { get; } = counterweight;     // kg
    public double Radius { get; } = radius;                   // m, the spindle the ropes wind on
    public double MaxTurn { get; } = maxTurn;                 // rad
    public double Friction { get; init; }                     // N·m in the spindle's pivots
    public double LeafInertia { get; init; }                  // kg·m², the doors themselves

    private readonly double _hungAt = vessel.BaseElevation;

    public double Angle { get; private set; }                 // rad, 0 shut
    public double AngularVelocity { get; private set; }       // rad/s
    public double Hanging => VesselMass + Vessel.WaterVolume * Physics.WaterDensity;
    public double Torque => (Hanging - Counterweight) * Physics.Gravity * Radius;

    public void Step(double dt)
    {
        double inertia = (Hanging + Counterweight) * Radius * Radius + LeafInertia;
        double drive = Torque;
        if (AngularVelocity == 0 && Math.Abs(drive) <= Friction) { }
        else
        {
            double net = drive - Friction * Math.Sign(AngularVelocity != 0 ? AngularVelocity : drive);
            AngularVelocity += net / inertia * dt;
            Angle += AngularVelocity * dt;
            // the doors stop dead against their stops, shut or wide open
            if (Angle <= 0) { Angle = 0; AngularVelocity = Math.Max(0, AngularVelocity); if (drive <= 0) AngularVelocity = 0; }
            if (Angle >= MaxTurn) { Angle = MaxTurn; AngularVelocity = Math.Min(0, AngularVelocity); if (drive >= 0) AngularVelocity = 0; }
        }
        Vessel.BaseElevation = _hungAt - Radius * Angle;
    }
}
