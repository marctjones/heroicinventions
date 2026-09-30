using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #50: grain draining at a steady rate. The part in a machine is in HopperPartTests.</summary>
public class HopperTests
{
    /// <summary>Sand of 1600 kg/m³ and 0.3 mm grains through a 10 mm orifice, in 9.81 m/s²: W = 0.58 × 1600 × 3.132 × (10 - 0.45 mm)^(5/2).</summary>
    private static Hopper Sand(double orifice = 0.010, double grain = 0.0003, double gravity = 9.81) =>
        new("h", area: 0.01, grainMass: 5, orifice, grain, bulkDensity: 1600, gravity);

    [Fact]
    public void FlowIsBeverloosCTimesRhoRootGTimesDMinusKdToTheFiveHalves()
    {
        var sand = Sand();
        double effective = 0.010 - 1.5 * 0.0003;                                            // 9.55 mm
        double expected = 0.58 * 1600 * Math.Sqrt(9.81) * Math.Pow(effective, 2.5);
        Assert.Equal(expected, sand.Flow, precision: 12);
        Assert.Equal(0.0259, sand.Flow, precision: 4);                                      // 25.9 g/s, worked by hand: 928 x 3.132 x (9.55 mm)^2.5 = 2906 x 8.91e-6
        Assert.Equal(sand.Flow / (1600 * 0.01), sand.SurfaceSpeed, precision: 12);          // the surface sinks at W / (rho A)
    }

    [Fact]
    public void TheLevelFallsLinearlyAndTheHopperEmptiesAtAConstantRate()
    {
        var sand = Sand();
        double w = sand.Flow, level0 = sand.Level;
        Assert.Equal(5 / (1600 * 0.01), level0, precision: 12);                             // 31.25 cm
        var levels = new List<double>();
        for (int i = 0; i < 6; i++)
        {
            for (int k = 0; k < 400; k++) sand.Step(0.05);                                  // 20 s a time
            levels.Add(sand.Level);
        }
        // equal drops of level in equal times: linear, not the water clock's square-root curve
        double drop = levels[0] - levels[1];
        for (int i = 1; i < levels.Count - 1; i++) Assert.Equal(drop, levels[i] - levels[i + 1], precision: 12);
        Assert.Equal(w * 20 / (1600 * 0.01), level0 - levels[0], precision: 9);
        // and the same amount of grain runs out in the last second as in the first
        var again = Sand();
        again.Step(1); double firstSecond = again.Drained;
        for (int i = 0; i < 100; i++) again.Step(1);
        double before = again.Drained; again.Step(1);
        Assert.Equal(firstSecond, again.Drained - before, precision: 12);
        Assert.Equal(w, firstSecond, precision: 12);
    }

    [Fact]
    public void ItEmptiesInMassOverFlowAndThenStops()
    {
        var sand = Sand();
        double t = 5 / sand.Flow;                                                           // 206.6 s
        int steps = (int)Math.Ceiling(t / 0.1);
        for (int i = 0; i < steps + 10; i++) sand.Step(0.1);
        Assert.True(sand.Empty);
        Assert.Equal(5, sand.Drained, precision: 9);
        Assert.Equal(0, sand.Flow);
        Assert.Equal(0, sand.SurfaceSpeed);
    }

    [Fact]
    public void TheFlowScalesAsRootGSoMarsRunsSlowerByRootOfTheRatio()
    {
        var earth = Sand();
        var mars = Sand(gravity: 3.71);
        Assert.Equal(Math.Sqrt(3.71 / 9.81), mars.Flow / earth.Flow, precision: 12);
        Assert.Equal(0.615, mars.Flow / earth.Flow, precision: 3);
        // so a step of a program takes sqrt(9.81 / 3.71) = 1.626 times as long
        Assert.Equal(Math.Sqrt(9.81 / 3.71), earth.Flow / mars.Flow, precision: 12);
        Assert.Equal(1.626, earth.Flow / mars.Flow, precision: 3);
    }

    [Theory]
    [InlineData(0.010, 0.0003, false)]     // 33 grain diameters: flows
    [InlineData(0.012, 0.0024, false)]     // 5.0 diameters exactly: just flows
    [InlineData(0.008, 0.002, true)]       // 4 diameters: arches
    [InlineData(0.0049, 0.001, true)]      // 4.9 diameters: arches
    public void FlowStopsWhenTheOrificeIsUnderFiveGrainDiameters(double orifice, double grain, bool arched)
    {
        var h = Sand(orifice, grain);
        Assert.Equal(arched, h.Arched);
        if (arched) { Assert.Equal(0, h.Flow); for (int i = 0; i < 100; i++) h.Step(1); Assert.Equal(5, h.Mass, precision: 12); }
        else Assert.True(h.Flow > 0);
    }

    [Fact]
    public void WaterSlowsAsItsHeadFallsButGrainDoesNot()
    {
        // Torricelli: a tank of water through a hole slows as sqrt(head); the hopper does not. Compare halfway down.
        var sand = Sand();
        double flowFull = sand.Flow;
        for (int i = 0; i < 1000; i++) sand.Step(0.1);                                       // most of the way down
        Assert.True(sand.Mass > 0 && sand.Mass < 2.6);
        Assert.Equal(flowFull, sand.Flow, precision: 12);
        double head = 0.3125, halfHead = head / 2;
        Assert.Equal(1 / Math.Sqrt(2), Math.Sqrt(halfHead / head), precision: 12);          // a water clock at half depth runs at 71% of its first speed
    }
}
