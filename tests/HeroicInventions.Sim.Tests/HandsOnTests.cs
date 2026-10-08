using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Hands-on actions on the ground (#162): a person picks where a digger works. The mirror and the sand timer are in racket/heroic/tests/handson-test.rkt.</summary>
public class HandsOnTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static string Dir(string d, string f) => Path.Combine(AppContext.BaseDirectory, d, f);

    private static (MachineRuntime Crew, WorldGround Ground, Terrain Map) Trench()
    {
        var world = WorldDef.Parse(File.ReadAllText(Dir("worlds", "trench.world")), "trench.world");
        var map = Terrain.Parse(File.ReadAllText(Dir("maps", world.Map + ".map")), world.Map!);
        var ground = new WorldGround(map);
        var place = Assert.Single(world.Placements);
        var crew = new MachineRuntime(WorldDef.Placed(MachineDef.Parse(File.ReadAllText(Dir("machines", place.Machine + ".machine"))), place, map), Materials);
        ground.Attach(place.Label, crew);
        return (crew, ground, map);
    }

    /// <summary>
    /// trench.world on level clay: from (-2, -3) the gang's trench falls in at 3.25 m after 13 m3 and about 211 s
    /// (EarthworksTests). A click on the ground at (10, -3) at t = 40 s sends the gang there: it climbs out of what it had
    /// cut (the old trench stays), and cuts a new one from the same clay: the same 3.25 m, 13 m3 more, 211 s later.
    /// The old site keeps what was cut, the new one is cut to 3.25 m, and the totals add: dug = (cut by 40 s) + 13.
    /// </summary>
    [Fact]
    public void ADiggerSentToANewSiteStartsThereAndLeavesTheOldTrenchAlone()
    {
        var (crew, ground, map) = Trench();
        var gang = crew.Diggers["gang"];
        Assert.Equal(-2, crew.GetField("gang", "site-x"));
        Assert.Equal(-3, crew.GetField("gang", "site-z"));
        double t = 0;
        while (t < 40) { crew.Step(0.1); ground.Step(0.1); t += 0.1; }
        double cutBefore = gang.Dug, depthBefore = gang.Depth;
        Assert.InRange(cutBefore, 0.5, 5);
        Assert.True(depthBefore > 0 && !gang.Done);
        double oldFloor = map.HeightAt(-1, -3);
        Assert.Equal(-depthBefore, oldFloor, 0.3);

        crew.SetField("gang", "site-x", 10);                       // the click on the ground: two fields, as the log has them
        crew.SetField("gang", "site-z", -3);
        Assert.Equal(10, crew.GetField("gang", "site-x"));
        Assert.Equal(0, gang.Depth);
        Assert.Equal(cutBefore, gang.Dug, 9);                       // what it had dug stays counted
        Assert.Equal(16, gang.Cells.Count);                         // a whole trench of cells at the new place
        Assert.Equal(0, map.HeightAt(12, -3), 9);                  // and the ground there untouched

        double from = t;
        while (!gang.Done && t - from < 1000) { crew.Step(0.1); ground.Step(0.1); t += 0.1; }
        Assert.True(gang.Collapsed);
        Assert.Equal(3.25, gang.CollapseDepth, 9);
        Assert.Equal(211, t - from, 1.5);
        Assert.Equal(cutBefore + 13, gang.Dug, 9);
        Assert.True(map.HeightAt(12, -3) < -1.5, $"cut at the new site: {map.HeightAt(12, -3)}");
        Assert.Equal(oldFloor, map.HeightAt(-1, -3), 9);            // the old trench is as it was left
    }

    /// <summary>A digger moved off the map's ground (or in a machine alone, with none) has nothing to dig and is done at once.</summary>
    [Fact]
    public void ADiggerSentOffTheMapHasNothingToDig()
    {
        var (crew, ground, _) = Trench();
        crew.SetField("gang", "site-x", 500);
        Assert.Empty(crew.Diggers["gang"].Cells);
        crew.Step(0.1);
        Assert.Equal(1, crew.GetField("gang", "done"));
    }
}
