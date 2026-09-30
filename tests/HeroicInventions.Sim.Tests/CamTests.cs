using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #48: peg wheels and cams. The Jolt-side trip-hammer (a wheel turning, loaded by its follower) is in heroic/tests/machine-behavior-test.rkt.</summary>
public class CamTests
{
    private const double G = 9.81, Dt = 1.0 / 120;

    /// <summary>Turns the wheel steadily at omega for the given number of revolutions.</summary>
    private static (Cam Cam, double MeanTorque, double PeakTorque) Run(Cam cam, double omega, double turns)
    {
        double theta = 0, peak = 0, sum = 0;
        int steps = (int)Math.Round(turns * 2 * Math.PI / omega / Dt);
        for (int i = 0; i < steps; i++)
        {
            theta += omega * Dt;
            double torque = cam.Step(Dt, theta);
            sum += torque;
            peak = Math.Max(peak, torque);
        }
        return (cam, sum / steps, peak);
    }

    [Fact]
    public void AProfileRisesOnACosineAndDropsAtTheTop()
    {
        var cam = new Cam("hammer", pegs: 4, lift: 0.1, rise: 0.5, mass: 5, gravity: G);
        Assert.Equal(Math.PI / 2, cam.Pitch, precision: 12);
        Assert.Equal(Math.PI / 4, cam.RiseAngle, precision: 12);
        Assert.Equal(0, cam.Profile(0), precision: 12);
        Assert.Equal(0.05, cam.Profile(cam.RiseAngle / 2), precision: 12);                    // half way up at half the rise
        Assert.Equal(0.1, cam.Profile(cam.RiseAngle - 1e-9), precision: 8);                   // the top
        Assert.Equal(0, cam.Profile(cam.RiseAngle + 1e-9), precision: 12);                    // let go
        Assert.Equal(cam.Profile(0.3), cam.Profile(0.3 + cam.Pitch), precision: 12);          // and again at the next peg
        // steepest half way: pi h / (2 alpha) = pi (0.1) / (2 pi/4) = 0.2 m/rad
        Assert.Equal(0.2, cam.SteepestSlope, precision: 12);
        Assert.Equal(0.2, cam.Slope(cam.RiseAngle / 2), precision: 12);
        Assert.Equal(0, cam.Slope(0), precision: 12);
    }

    [Fact]
    public void AFollowerStrikesOncePerPegAndLandsAtRootTwoGH()
    {
        var (cam, _, _) = Run(new Cam("hammer", 4, 0.1, 0.5, 5, G), omega: Math.PI, turns: 5);   // 30 rpm, five turns
        Assert.InRange(cam.Strikes, 4 * 5 - 1, 4 * 5);                                     // n strikes a turn (the last may be mid-fall)
        double predicted = Math.Sqrt(2 * G * 0.1);                                         // 1.401 m/s from a drop of h
        Assert.Equal(predicted, cam.LastStrikeSpeed, precision: 1);
        Assert.Equal(predicted, cam.FastestStrike, precision: 1);
    }

    [Fact]
    public void OnMarsTheDropIsSlowerByRootOfTheGravityRatio()
    {
        var earth = Run(new Cam("e", 4, 0.1, 0.5, 5, 9.81), Math.PI, 3).Cam;
        var mars = Run(new Cam("m", 4, 0.1, 0.5, 5, 3.71), Math.PI, 3).Cam;
        Assert.Equal(Math.Sqrt(2 * 3.71 * 0.1), mars.LastStrikeSpeed, precision: 1);          // 0.861 m/s
        Assert.Equal(Math.Sqrt(9.81 / 3.71), earth.LastStrikeSpeed / mars.LastStrikeSpeed, precision: 1);
    }

    [Fact]
    public void TheWheelDoesNPegsMGHOfWorkATurnSoTheMeanTorqueIsNMGHOver2Pi()
    {
        var (cam, mean, peak) = Run(new Cam("hammer", 4, 0.1, 0.5, 5, G), omega: Math.PI, turns: 5);
        double workPerTurn = 4 * 5 * G * 0.1;                                              // 19.62 J
        Assert.Equal(workPerTurn * 5, cam.Work, 0);                                        // 98.1 J: to the joule over five turns
        Assert.Equal(workPerTurn / (2 * Math.PI), mean, precision: 1);                     // 3.123 N.m
        Assert.Equal(3.123, mean, precision: 1);
        // at speed the peak is above the quasi-static m g x steepest slope (9.81 N.m): the follower is also being accelerated,
        // so the wheel gives m (g + w^2 y'') y' at each angle; its maximum, scanned over the rise
        double omega = Math.PI, best = 0;
        for (double phi = 0; phi < cam.RiseAngle; phi += 1e-4)
        {
            double y2 = 0.1 * Math.PI * Math.PI / (2 * cam.RiseAngle * cam.RiseAngle) * Math.Cos(Math.PI * phi / cam.RiseAngle);
            best = Math.Max(best, 5 * (G + omega * omega * y2) * cam.Slope(phi));
        }
        Assert.InRange(best, 11.5, 12.5);
        Assert.Equal(best, peak, tolerance: 0.03 * best);
        Assert.True(peak > 5 * G * cam.SteepestSlope);
    }

    [Theory]
    [InlineData(8.0, 13.7)]      // a wheel giving 8 N.m stalls where m g slope = 8: sin(pi phi / alpha) = 0.8155, phi = 13.7 degrees into the rise
    [InlineData(5.0, 7.7)]
    public void AWheelDrivenByLessStallsWhereTheLiftIsSteepest(double driveTorque, double stallDegrees)
    {
        var cam = new Cam("hammer", 4, 0.1, 0.5, 5, G);
        double peak = 5 * G * cam.SteepestSlope;
        Assert.True(driveTorque < peak);
        // quasi-static: the torque the follower asks for at angle phi into the rise, until it matches what the wheel gives
        double phi = cam.RiseAngle / Math.PI * Math.Asin(driveTorque / peak);
        Assert.Equal(stallDegrees, phi * 180 / Math.PI, precision: 1);
        Assert.Equal(driveTorque, 5 * G * cam.Slope(phi), precision: 9);
    }

    [Fact]
    public void AFollowerFallsAtGAndIsHeldByThePegWhileItIsLifted()
    {
        var cam = new Cam("hammer", 1, 0.1, 0.5, 5, G);                                    // one peg: pitch 2 pi, rise pi
        double theta = 0;
        // slow: the wheel turns 0.5 rad/s, so the rise takes 2 pi s and the follower rides up with the surface
        for (int i = 0; i < 100; i++) { theta += 0.5 * Dt; cam.Step(Dt, theta); }
        Assert.True(cam.InContact);
        Assert.Equal(cam.Profile(theta), cam.Height, precision: 12);
        // over the top: it falls free, and each step it gains g dt of speed downward
        double lastHeld = cam.Height;
        while (cam.InContact) { lastHeld = cam.Height; theta += 0.5 * Dt; cam.Step(Dt, theta); }
        for (int i = 0; i < 11; i++) { theta += 0.5 * Dt; cam.Step(Dt, theta); }           // 12 steps of fall in all
        Assert.Equal(-G * Dt * 12, cam.Velocity, precision: 9);
        Assert.Equal(lastHeld - G * Dt * Dt * 12 * 13 / 2, cam.Height, precision: 9);      // g t^2 / 2 for the semi-implicit steps
        Assert.Equal(-0.5 * G * (12 * Dt) * (12 * Dt), cam.Height - lastHeld, precision: 2);
        Assert.False(cam.InContact);
    }

    /// <summary>A wheel of inertia I, given a constant torque and a little bearing drag, working the follower through the coupled step.</summary>
    private static (double Theta, Cam Cam) Coupled(double driveTorque, double seconds, double inertia = 0.043, double omega0 = 0, double drag = 0.2)
    {
        var cam = new Cam("hammer", 4, 0.1, 0.5, 5, G);
        double theta = 0, omega = omega0;
        for (int i = 0; i < (int)(seconds / Dt); i++)
        {
            double impulse = cam.StepCoupled(Dt, theta, omega, inertia);
            omega += (driveTorque * Dt + impulse) / inertia;
            omega *= 1 - drag * Dt;                                                         // bearing drag: a little, as the wheels have, or a lot
            theta += omega * Dt;
        }
        return (theta, cam);
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(3.0)]
    public void AWheelGivingLessThanItsWorkStallsWhereTheFollowersWeightMatchesItsTorque(double driveTorque)
    {
        // constant torque T on the wheel: it settles where m g y'(phi) = T, the follower's weight matching what the wheel gives
        var cam = new Cam("hammer", 4, 0.1, 0.5, 5, G);
        double phi = cam.RiseAngle / Math.PI * Math.Asin(driveTorque / (5 * G * cam.SteepestSlope));
        // (a wheel with little drag would swing through that point and back, letting the hammer go on the backswing; a
        // damped one, like the motor-driven wheels, comes to rest on it)
        var (theta, run) = Coupled(driveTorque, seconds: 30, drag: 8);
        Assert.Equal(phi, theta, precision: 2);                                              // 7.7 degrees at 5 N.m
        Assert.True(run.InContact);                                                           // the hammer is held up on the peg,
        Assert.Equal(cam.Profile(phi), run.Height, precision: 3);                             // at the height that angle gives (0.70 cm at 5 N.m)
    }

    [Fact]
    public void AWheelGivingMoreThanItsWorkCarriesThePegOverAndTheHammerFalls()
    {
        // 20 N.m from rest against a 5 kg hammer: over the top, the hammer let go and struck
        var (_, run) = Coupled(20, seconds: 3);
        Assert.True(run.Strikes >= 3, $"{run.Strikes} strikes");
        Assert.Equal(Math.Sqrt(2 * G * 0.1), run.FastestStrike, precision: 1);
    }

    [Fact]
    public void TheFollowersInertiaSlowsTheWheelThatLiftsIt()
    {
        // the same steady push on a light wheel and a heavy one: the follower adds m y'^2 (0.2 kg.m2 at the steepest) to whichever it loads
        var (_, light) = Coupled(6, 0.6, inertia: 0.043);
        var (_, heavy) = Coupled(6, 0.6, inertia: 0.5);
        Assert.True(light.Work > 0 && heavy.Work > 0);
        double inertia = 0.043;
        var cam = new Cam("c", 4, 0.1, 0.5, 5, G);
        // Two steps at rest near the steepest point, worked by hand. The follower's speed follows the wheel's, y' w; each step
        // (I + m y'^2) w' = I w + m y' (ydot - g dt), so the wheel is pushed back by the weight's impulse, shared with the
        // follower's inertia: a heavy follower on a light wheel takes much of it on itself.
        double theta = cam.RiseAngle / 2, s1 = cam.Slope(theta - 0.0001), s2 = cam.Slope(theta);
        cam.StepCoupled(Dt, theta - 0.0001, 0, inertia);
        double impulse = cam.StepCoupled(Dt, theta, 0, inertia);
        double w1 = 5 * s1 * (0 - G * Dt) / (inertia + 5 * s1 * s1), v1 = s1 * w1;
        double w2 = 5 * s2 * (v1 - G * Dt) / (inertia + 5 * s2 * s2);
        Assert.Equal(inertia * w2, impulse, precision: 9);
        double slope = s2;
        // the lighter the wheel against the follower's m y'^2 = 0.2, the more of the weight's impulse the wheel takes on itself
        Assert.True(Math.Abs(impulse) < 5 * slope * G * Dt);
    }
}
