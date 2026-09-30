using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #49: ratchet and pawl. The Jolt-side windlass is in heroic/tests/machine-behavior-test.rkt.</summary>
public class RatchetTests
{
    private const double G = 9.81, Dt = 1.0 / 120;

    [Fact]
    public void AWheelOfNTeethAdvancesTwoPiOverNAStep()
    {
        var r = new Ratchet("r", 12, 0.15, reverse: false);
        Assert.Equal(30, r.Pitch * 180 / Math.PI, precision: 9);
        // turning steadily forward through 200 degrees: six whole teeth
        double theta = 0;
        for (int i = 0; i < 1000 && theta < 200 * Math.PI / 180; i++) { theta += 0.02; r.Step(Dt, theta, 0.02 / Dt, 0.05); }
        Assert.Equal(6, r.Steps);
        Assert.Equal(6 * 30, r.Locked * 180 / Math.PI, precision: 9);                       // the pawl is 180 degrees on, a tooth behind the wheel
        Assert.False(r.Holding);                                                             // clear of the pawl while going forward
    }

    [Fact]
    public void ItHoldsAHangingLoadWithTheForceMGROverBigR()
    {
        // 20 kg on a drum of 10 cm radius, the ratchet's teeth on a 15 cm circle: the pawl carries 19.62 N.m, 130.8 N at its tooth
        const double mass = 20, drum = 0.10, toothCircle = 0.15;
        const double inertia = 0.05 + mass * drum * drum;                                    // the drum, and the load it has to turn (m r^2, through the rope)
        var r = new Ratchet("r", 12, toothCircle, false);
        double theta = 0, omega = 0;
        // as the engine does each tick: the pawl acts first, then the load and the step move the wheel
        for (int i = 0; i < 600; i++)
        {
            omega += r.Step(Dt, theta, omega, inertia) / inertia;
            omega += -mass * G * drum / inertia * Dt;                                        // the load turns the wheel back
            theta += omega * Dt;
        }
        Assert.Equal(mass * G * drum / toothCircle, r.Force, precision: 0);                  // 130.8 N
        Assert.Equal(130.8, r.Force, precision: 0);
        Assert.Equal(mass * G * drum, r.Torque, precision: 0);                               // 19.62 N.m
        Assert.True(r.Holding);
        Assert.InRange(theta, -0.02, 0.0);                                                   // and the load has not fallen: it sags a tenth of a tooth at most
        Assert.Equal(0, r.Steps);
    }

    [Fact]
    public void ItAllowsBackTravelToTheValleyItIsInButNoFurther()
    {
        var r = new Ratchet("r", 12, 0.15, false);
        double theta = 0;
        // advance 2.5 teeth: valleys at 0, 30, 60 degrees; the wheel at 75
        for (int i = 0; i < 100; i++) { theta += 0.015; r.Step(Dt, theta, 0.015 / Dt, 0.05); if (theta > 75 * Math.PI / 180) break; }
        Assert.Equal(2, r.Steps);
        double locked = r.Locked;
        Assert.Equal(60, locked * 180 / Math.PI, precision: 9);
        // now pull it back hard: it comes to rest at the valley, 60 degrees, and no lower
        double omega = 0;
        for (int i = 0; i < 400; i++)
        {
            omega += r.Step(Dt, theta, omega, 0.05) / 0.05;
            omega += -5.0 / 0.05 * Dt;
            theta += omega * Dt;
        }
        Assert.InRange(theta * 180 / Math.PI, 58.0, 60.5);                                    // at the valley (a hair below: the sag)
        Assert.Equal(2, r.Steps);                                                            // and it has not undone a step
    }

    [Fact]
    public void AReverseRatchetTurnsTheOtherWay()
    {
        var r = new Ratchet("r", 8, 0.1, reverse: true);
        double theta = 0, omega = 0;
        for (int i = 0; i < 200; i++) { omega -= 3.0 * Dt; theta += omega * Dt; r.Step(Dt, theta, omega, 0.05); }   // turning negative, the allowed way
        Assert.True(r.Steps > 0 && !r.Holding);
        // pushed positive, the forbidden way, it is held
        omega = 0;
        double at = theta;
        for (int i = 0; i < 300; i++) { omega += r.Step(Dt, theta, omega, 0.05) / 0.05; omega += 4.0 * Dt; theta += omega * Dt; }
        Assert.True(theta - at < 0.5 * r.Pitch + 0.02 && r.Holding);
    }
}
