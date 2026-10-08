using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #191: a shaft between two machines must behave as a rigid shaft. It
/// trades angular momentum and never makes or loses energy once its ends turn
/// on its ratio; and a shaft the physics engine also locks within its step
/// (<see cref="ShaftLink.Locked"/>) still reports the torque it carried.
/// </summary>
public class ShaftLinkEnergyTests
{
    private sealed class Spinner(double inertia, double omega) : IShaft
    {
        public double AngularVelocity { get; set; } = omega;
        public double ShaftInertia => inertia;
        public void AddAngularImpulse(double impulse) => AngularVelocity += impulse / inertia;
        public double Energy => 0.5 * inertia * AngularVelocity * AngularVelocity;
    }

    /// <summary>
    /// Worked: two flywheels, no drive, no damping, joined off their ratio. The
    /// first tick's exchange is an inelastic catch: it loses ½ μ s², with s the
    /// slip and μ = 1 / (1/I_b + R²/I_a) the inertia the slip is felt through;
    /// for 3 and 0.5 kg·m² at 10 and 4 rad/s, ratio 1: s = −6, μ = 0.4286, 7.714 J
    /// of the 154 J. After that the ends turn on the ratio, every exchange is
    /// zero and the kinetic energy stays where it is, to rounding, for 10⁵ ticks.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(-0.5)]
    [InlineData(4.0)]
    public void AFreePairKeepsItsKineticEnergyOnceOnItsRatio(double ratio)
    {
        const double dt = 1.0 / 120, ia = 3, ib = 0.5;
        var a = new Spinner(ia, 10);
        var b = new Spinner(ib, 4);
        var shaft = new ShaftLink(a, b, ratio);
        double before = a.Energy + b.Energy;
        double slip = b.AngularVelocity - ratio * a.AngularVelocity;
        double mu = 1 / (1 / ib + ratio * ratio / ia);
        shaft.Step(dt);
        double after = a.Energy + b.Energy;
        Assert.Equal(0.5 * mu * slip * slip, before - after, precision: 9);
        Assert.Equal(ratio * a.AngularVelocity, b.AngularVelocity, precision: 12);
        // momentum about the shaft: what one end gains the other gives up (Ratio × at the driving end)
        Assert.Equal(ia * 10 + ratio * ib * 4, ia * a.AngularVelocity + ratio * ib * b.AngularVelocity, precision: 9);

        for (int i = 0; i < 100_000; i++)
        {
            shaft.Step(dt);
            Assert.Equal(0, shaft.Torque, precision: 9);
        }
        Assert.Equal(after, a.Energy + b.Energy, precision: 9);
    }

    /// <summary>
    /// A shaft the physics engine also locks (the game's split crane): each tick
    /// a load takes τ dt from the driven end, the exchange shares that over both,
    /// and the engine's step (a motor holding the driving end at its speed, the
    /// lock carrying it to the driven end) brings both back. The torque read
    /// after the step is the whole τ, 1430 N·m, not the exchange's share alone
    /// (I_a / (I_a + I_b) of it, 1426.5 N·m for the crane's 1818.3 and 4.5 kg·m²),
    /// and the speeds are the step's.
    /// </summary>
    [Fact]
    public void ALockedShaftReadsTheWholeTorqueItCarriedAfterTheStep()
    {
        const double dt = 1.0 / 120, cap = 0.314159, tau = 1430;
        var wheel = new Spinner(1818.3, cap);
        var drum = new Spinner(4.5, cap);
        var shaft = new ShaftLink(wheel, drum, 1) { Locked = true };
        for (int i = 0; i < 10; i++)
        {
            shaft.ReadLocked();                              // after the last step
            drum.AddAngularImpulse(-tau * dt);               // the machines: the rope's pull
            shaft.Step(dt);                                  // the exchange
            Assert.Equal(cap - tau * dt / (1818.3 + 4.5), wheel.AngularVelocity, precision: 9);
            wheel.AngularVelocity = cap;                     // the engine's step: the motor and the lock
            drum.AngularVelocity = cap;
        }
        shaft.ReadLocked();
        Assert.Equal(tau, shaft.Torque, precision: 6);
        Assert.Equal(-tau, shaft.DriverTorque, precision: 6);
        Assert.Equal(cap, shaft.AngularVelocity, precision: 12);
        Assert.Equal(cap, shaft.DrivenAngularVelocity, precision: 12);

        // the same shaft unlocked reads only what its exchange moved
        var plain = new ShaftLink(new Spinner(1818.3, cap), drum = new Spinner(4.5, cap), 1);
        drum.AddAngularImpulse(-tau * dt);
        plain.Step(dt);
        Assert.Equal(tau * 1818.3 / (1818.3 + 4.5), plain.Torque, precision: 6);
    }
}
