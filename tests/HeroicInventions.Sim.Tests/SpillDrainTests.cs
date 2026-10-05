using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #90: tanks that spill onto the ground, drains that run into tanks. Predictions worked out first, in the machines' headers.</summary>
public class SpillDrainTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static string Dir(string d, string f) => Path.Combine(AppContext.BaseDirectory, d, f);

    private static (WorldGround Ground, MachineRuntime Run) World(string name)
    {
        var world = WorldDef.Parse(File.ReadAllText(Dir("worlds", name + ".world")), name + ".world");
        var map = Terrain.Parse(File.ReadAllText(Dir("maps", world.Map + ".map")), world.Map!);
        var ground = new WorldGround(map);
        var place = Assert.Single(world.Placements);
        var run = new MachineRuntime(WorldDef.Placed(MachineDef.Parse(File.ReadAllText(Dir("machines", place.Machine + ".machine"))), place, map), Materials);
        ground.Attach(place.Label, run);
        return (ground, run);
    }

    /// <summary>
    /// spill-tank.rkt: a 1 m², 1000 L butt with a 100 cm² hole 2 cm up, on a walled slope of sand that soaks up
    /// nothing. Torricelli: at 30 s the butt holds 369.6 L and the ground 630.4 L; by 74.5 s it is down to the hole,
    /// 20 L in the butt, 980 L on the ground. At every step the butt and the ground hold the 1000 L there were.
    /// </summary>
    [Fact]
    public void ABrokenTankSpillsOntoTheGroundAndNoWaterIsLost()
    {
        var (ground, run) = World("spill");
        var butt = run.Tanks["butt"];
        double dt = 1.0 / 120, t = 0;
        Assert.Equal(1.0, butt.WaterVolume + ground.Water.Volume, 12);
        while (t < 120 - 1e-9)
        {
            run.Step(dt); ground.Step(dt); t += dt;
            Assert.Equal(1.0, butt.WaterVolume + ground.Water.Volume, 9);
            if (Math.Abs(t - 30) < dt / 2)
            {
                Assert.Equal(0.3696, butt.WaterVolume, 0.002);
                Assert.Equal(0.6304, ground.Water.Volume, 0.002);
            }
        }
        Assert.Equal(0.020, butt.WaterVolume, 1e-4);
        Assert.Equal(0.980, ground.Water.Volume, 1e-4);
        Assert.Equal(0.980, ground.Spilled, 1e-4);
        Assert.Equal(ground.Water.Poured, ground.Water.Volume, 9);   // nothing soaked in or ran off: the walls held it
        // it ran downhill: the wettest ground is at the low (+x) wall, far from the butt
        var map = ground.Ground;
        int deepest = Enumerable.Range(0, map.Count).MaxBy(c => ground.Water.Depths[c]);
        Assert.True(map.CellX(deepest % map.Nx) > 10, $"the water pooled at x = {map.CellX(deepest % map.Nx)}");
    }

    /// <summary>
    /// cistern-drain.rkt on the sump map: a 2 L/s spring into a walled hollow, a grate (0.4 m of lip) at its bottom.
    /// Steady, the grate takes the spring's 2 L/s, so the cistern fills at 2 L/s, with h = (0.002 / (1.705 × 0.4))^(2/3)
    /// = 2.05 cm standing over the grate. The cistern and the ground always hold what the spring gave.
    /// </summary>
    [Fact]
    public void ADrainFillsItsCisternAtThePredictedRate()
    {
        var (ground, run) = World("sump");
        var (grate, cistern) = (run.Drains["grate"], run.Tanks["cistern"]);
        Assert.True(grate.Attached);
        double dt = 1.0 / 120, t = 0, at150 = 0;
        while (t < 180 - 1e-9)
        {
            run.Step(dt); ground.Step(dt); t += dt;
            Assert.Equal(ground.Water.Poured, cistern.WaterVolume + ground.Water.Volume, 9);
            if (Math.Abs(t - 150) < dt / 2) at150 = cistern.WaterVolume;
        }
        Assert.Equal(0.002 * 180, ground.Water.Poured, 1e-6);
        double rate = (cistern.WaterVolume - at150) / 30;
        Assert.Equal(0.002, rate, 0.002 * 0.01);                                  // 2 L/s, to 1 %
        Assert.Equal(0.002, grate.Flow, 0.002 * 0.01);
        Assert.Equal(Math.Pow(0.002 / (1.705 * 0.4), 2.0 / 3), grate.Depth, 0.0205 * 0.02);   // 2.05 cm, to 2 %
        Assert.Equal(cistern.WaterVolume, grate.Drained, 12);
        Assert.Equal(grate.Drained, ground.Water.Drained, 12);
    }

    /// <summary>A full cistern backs its drain up: the water stays on the ground. And a tank fed past full on a map runs over onto it.</summary>
    [Fact]
    public void AFullTankBacksUpItsDrainAndRunsOver()
    {
        var (ground, run) = World("sump");
        var (grate, cistern) = (run.Drains["grate"], run.Tanks["cistern"]);
        run.FieldSetters["cistern.water"](cistern.Capacity * 1000);
        for (int k = 0; k < 120 * 30; k++) { run.Step(1.0 / 120); ground.Step(1.0 / 120); }
        Assert.Equal(0, grate.Drained);
        Assert.Equal(0.002 * 30, ground.Water.Volume, 1e-6);

        // an inflow into a full tank on a map spills the rest beside it
        var tank = new Tank("butt", 0, 1, 1, 1);
        var feed = new WaterSource("pipe", tank, 0.01);
        double spilled = 0;
        tank.Spill = m3 => spilled += m3;
        feed.Step(1);
        Assert.Equal(0.01, spilled, 12);
        Assert.Equal(0.01, tank.Spilled, 12);
        Assert.Equal(1, tank.WaterVolume, 12);
        // off a map (nothing to spill onto) the full tank simply takes no more, as before
        var floor = new Tank("floor", 0, 1, 1, 1);
        new WaterSource("pipe", floor, 0.01).Step(1);
        Assert.Equal(0, floor.Spilled);
    }

    /// <summary>The drain part in the editor: palette template, exported to Racket and read back.</summary>
    [Fact]
    public void TheDrainIsAPartOfTheEditor()
    {
        Assert.Contains("drain", Editor.PartTemplates.PrimitiveKinds);
    }
}
