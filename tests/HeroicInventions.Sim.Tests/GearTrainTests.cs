using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #113: a gear train driven by a part already turning, and loaded. The
/// mesh is a <see cref="ShaftLink"/> of ratio −(teeth driving / teeth driven)
/// and efficiency η, stepped as the game steps it: each end turned under its
/// own drive and load, then the mesh's exchange. The Jolt-side train
/// (geared-brake.rkt) is checked in heroic/tests/machine-behavior-test.rkt.
/// </summary>
public class GearTrainTests
{
    /// <summary>A shaft turning freely, as a sim part would between exchanges.</summary>
    private sealed class Spinner(double inertia, double omega) : IShaft
    {
        public double AngularVelocity { get; set; } = omega;
        public double ShaftInertia => inertia;
        public void AddAngularImpulse(double impulse) => AngularVelocity += impulse / inertia;
    }

    /// <summary>
    /// A brake: dry friction of <paramref name="torque"/> N·m on its shaft at any
    /// speed. It can stop the train it is geared into (whose inertia, felt at
    /// the brake, is <paramref name="trainInertia"/>), never turn it back.
    /// </summary>
    private sealed class Brake(Spinner shaft, double torque, double trainInertia)
    {
        public void Step(double dt)
        {
            double w = shaft.AngularVelocity;
            double most = trainInertia * Math.Abs(w);
            shaft.AddAngularImpulse(-Math.Sign(w) * Math.Min(torque * dt, most));
        }
    }

    /// <summary>
    /// The issue's prediction, worked before running: a 1 kg·m² flywheel at 60
    /// rpm (2π rad/s) drives a brake shaft through one mesh stepping up 10:1;
    /// the brake holds 0.1 N·m. The flywheel feels 10 × 0.1 / η N·m and its
    /// speed falls in a straight line to a stop at I·ω/τ: 6.283 s with η = 1,
    /// 5.655 s with η = 0.9 (1.11 N·m reflected). The brake shaft's own
    /// 1e-6 kg·m², ×100 seen from the flywheel, adds 0.6 ms.
    /// </summary>
    [Theory]
    [InlineData(1.0, 6.2838)]
    [InlineData(0.9, 5.6555)]
    public void AFlywheelGearedTenToOneUpIntoABrakeStopsInIOmegaOverTheReflectedTorque(double eta, double stops)
    {
        const double dt = 1.0 / 120, w0 = 2 * Math.PI, ratio = -10;      // 100 teeth driving 10: ten times as fast, the other way
        double predicted = (eta * 1.0 + 100 * 1e-6) * w0 / (10 * 0.1);   // (η I_f + n² I_b) ω0 / (n τ)
        Assert.Equal(stops, predicted, precision: 4);

        var flywheel = new Spinner(1.0, w0);
        var shaft = new Spinner(1e-6, ratio * w0);
        var brake = new Brake(shaft, 0.1, 1e-6 + 1.0 / 100);
        var mesh = new ShaftLink(flywheel, shaft, ratio, eta);
        double t = 0, half = double.NaN, stopped = double.NaN;
        var torques = new List<double>();
        while (t < 10)
        {
            brake.Step(dt);                         // the brake shaft's own step: its friction
            ShaftLink.StepAll([mesh], dt, 4);       // then the mesh: the flywheel takes the load
            t += dt;
            if (double.IsNaN(half) && flywheel.AngularVelocity <= w0 / 2) half = t;
            if (double.IsNaN(stopped) && flywheel.AngularVelocity <= 1e-9) stopped = t;
            if (flywheel.AngularVelocity > 0.1) torques.Add(mesh.DriverTorque);
        }
        // a straight-line fall: half speed at half the time, stopped at the predicted time, within a tick
        Assert.Equal(predicted / 2, half, 2 * dt);
        Assert.Equal(predicted, stopped, 2 * dt);
        Assert.Equal(0, flywheel.AngularVelocity, precision: 9);
        Assert.Equal(0, shaft.AngularVelocity, precision: 6);
        // the torque the flywheel feels while it turns: n τ / η, against its turning
        Assert.Equal(-10 * 0.1 / eta, torques.Average(), precision: 3);
    }

    [Fact]
    public void AMeshPassesPowerForwardAtEtaAndKeepsTheRatio()
    {
        const double dt = 1.0 / 120;
        var a = new Spinner(2.0, 3.0);
        var b = new Spinner(0.5, 0.0);
        var mesh = new ShaftLink(a, b, -2, 0.8);
        double before = 0.5 * 2.0 * 9.0;
        ShaftLink.StepAll([mesh], dt, 1);
        Assert.Equal(-2 * a.AngularVelocity, b.AngularVelocity, precision: 12);
        // what b gained is η of what a lost
        double gained = 0.5 * 0.5 * b.AngularVelocity * b.AngularVelocity;
        double lost = before - 0.5 * 2.0 * a.AngularVelocity * a.AngularVelocity;
        Assert.True(gained < lost);
        // the angular impulses: b took λ, a gave n λ / η
        Assert.Equal(-(-2) * mesh.Torque / 0.8, mesh.DriverTorque, precision: 9);   // −n λ / η, n = −2
    }

    [Fact]
    public void ATrainOfMeshesSolvedTogetherHoldsEveryRatio()
    {
        // a driver, an idler and a pinion: −3 then −4, so the pinion turns 12 times the driver's speed, the same way
        var a = new Spinner(1.0, 1.0);
        var b = new Spinner(0.01, 0.0);
        var c = new Spinner(0.001, 0.0);
        var links = new[] { new ShaftLink(a, b, -3), new ShaftLink(b, c, -4) };
        ShaftLink.StepAll(links, 1.0 / 120, 30);
        Assert.Equal(-3 * a.AngularVelocity, b.AngularVelocity, precision: 6);
        Assert.Equal(12 * a.AngularVelocity, c.AngularVelocity, precision: 5);
        // and angular momentum is traded, not made: the driver's energy pays for the rest (η = 1, so it all arrives)
        double energy = 0.5 * (1.0 * a.AngularVelocity * a.AngularVelocity + 0.01 * b.AngularVelocity * b.AngularVelocity + 0.001 * c.AngularVelocity * c.AngularVelocity);
        Assert.True(energy <= 0.5 + 1e-9);
    }
}
