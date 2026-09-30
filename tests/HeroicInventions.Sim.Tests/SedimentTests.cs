using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #53: water that scours, carries and sorts the ground. Predictions worked out first.</summary>
public class SedimentTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static string Dir(string d, string f) => Path.Combine(AppContext.BaseDirectory, d, f);
    private const double G = 9.81;

    private static Terrain Slope(int nx, int nz, double slope, bool open, double grain = 0.0005) => new()
    {
        Name = "slope", Cell = 1, Nx = nx, Nz = nz,
        Heights = Enumerable.Range(0, nx * nz).Select(k => 5 - slope * (k % nx + 0.5)).ToArray(),
        Soil = new int[nx * nz], Soils = [new SoilSpec("sand", 0, 0, 0.62, 1600, grain, 2650)], OpenEdges = open,
    };

    /// <summary>
    /// Water running steadily down a slope S drags on the bed with ρ g h S, so
    /// grains of size d move only where it runs deeper than
    /// h_c = θ_c (ρ_s − ρ) d / (ρ S): for 0.5 mm quartz on 1 in 1000,
    /// 0.047 × 1650 × 0.0005 / (1000 × 0.001) = 3.88 cm. A sheet 0.9 h_c deep
    /// carries nothing; one 2 h_c deep carries Meyer-Peter and Müller's
    /// 8 (θ_c)^1.5 √(1.65 g d³) = 3.67e-6 m²/s.
    /// </summary>
    [Fact]
    public void GrainsMoveOnlyInFlowDeeperThanTheShieldsThreshold()
    {
        const double s = 0.001, d = 0.0005;
        double hc = ShallowWater2D.ShieldsThreshold * 1650 * d / (1000 * s);
        Assert.Equal(0.0388, hc, 4);
        foreach (var (depth, rate) in new[] { (0.9 * hc, 0.0), (2 * hc, 8 * Math.Pow(ShallowWater2D.ShieldsThreshold, 1.5) * Math.Sqrt(1.65 * G * d * d * d)) })
        {
            var w = new ShallowWater2D(Slope(10, 3, s, open: true));
            // steady uniform flow at that depth: Manning's speed for a wide sheet, h^(2/3) S^(1/2) / n
            w.Fill((_, _) => depth, (_, _) => (Math.Pow(depth, 2.0 / 3) * Math.Sqrt(s) / w.Manning, 0));
            Assert.Equal(1000 * G * depth * s, w.BedShear(5), 9);
            Assert.Equal(rate, w.SedimentRate(5), 12);
        }
        Assert.Equal(3.67e-6, 8 * Math.Pow(ShallowWater2D.ShieldsThreshold, 1.5) * Math.Sqrt(1.65 * G * d * d * d), 8);
    }

    /// <summary>
    /// The hushing scene (hushing.world): the pond let go onto the sand slope
    /// scours a gully below its spout and lays the sand down again where the
    /// flow spreads and slows. Every grain is accounted for (the bed's
    /// volume, pores and all, changes only by what runs off the map's edge),
    /// and it is the ground itself that moved, not the water.
    /// </summary>
    [Fact]
    public void AReleasedPondCutsAGullyAndBuildsAFanAndNoSandIsLost()
    {
        var world = WorldDef.Parse(File.ReadAllText(Dir("worlds", "hushing.world")), "hushing.world");
        var map = Terrain.Parse(File.ReadAllText(Dir("maps", world.Map + ".map")), world.Map!);
        var before = (double[])map.Heights.Clone();
        var ground = new WorldGround(map);
        var place = Assert.Single(world.Placements);
        var pond = new MachineRuntime(WorldDef.Placed(MachineDef.Parse(File.ReadAllText(Dir("machines", place.Machine + ".machine"))), place, map), Materials);
        ground.Attach(place.Label, pond);
        for (int k = 0; k < 18000; k++) { pond.Step(0.01); ground.Step(0.01); }   // 3 minutes

        var w = ground.Water;
        double change = map.Heights.Zip(before, (a, b) => a - b).Sum();   // m³ over 1 m² cells
        Assert.True(w.BedMoved > 0.01, $"the water moved {w.BedMoved:F3} m³ of sand");
        Assert.Equal(-w.BedLost, change, 9);
        double deepestCut = map.Heights.Zip(before, (a, b) => a - b).Min();
        double highestFill = map.Heights.Zip(before, (a, b) => a - b).Max();
        Assert.True(deepestCut < -0.01, $"a gully: the bed cut down {-deepestCut * 100:F1} cm");
        Assert.True(highestFill > 0.005, $"a fan: the bed built up {highestFill * 100:F1} cm");
        // the gully lies upslope of the fan
        int cut = Array.IndexOf(map.Heights.Zip(before, (a, b) => a - b).ToArray(), deepestCut);
        int fill = Array.IndexOf(map.Heights.Zip(before, (a, b) => a - b).ToArray(), highestFill);
        Assert.True(map.CellX(cut % map.Nx) < map.CellX(fill % map.Nx), "the fan lies below the gully");
    }

    /// <summary>
    /// placer-sluice.rkt: at the race's Manning depth for its 2 L/s the flow
    /// drags on the box's floor with ρ g R S = 1.46 Pa, so it keeps grains
    /// denser than 1000 + τ / (0.047 g d) = 7340 kg/m³: all the gold, none of
    /// the sand. Worked from the channel's own steady depth once the pool is
    /// passing the spring. (While the race is still a trickle the flow drags
    /// too little to lift even the sand, and some stays: the start-up's
    /// clean-up.)
    /// </summary>
    [Fact]
    public void ASluiceBoxKeepsTheGoldAndWashesOnTheSand()
    {
        var rt = new MachineRuntime(MachineDef.Parse(File.ReadAllText(Dir("machines", "placer-sluice.machine"))), Materials);
        for (int k = 0; k < 30000; k++) rt.Step(0.01);   // 5 minutes: the pool has come up to pass the spring
        var race = rt.Channels["race"];
        Assert.Equal(2, race.Flow * 1000, 2);
        double h = Channel.NormalDepth(race.Flow, 0.3, 0.01), r = 0.3 * h / (0.3 + 2 * h);
        double tau = 1000 * G * r * 0.01;
        Assert.Equal(1.46, tau, 2);
        Assert.Equal(tau, rt.GetField("riffles", "shear"), 6);
        Assert.Equal(7340, rt.GetField("riffles", "cutoff"), 10.0);   // to the nearest 10 kg/m³
        // a minute more at that flow: every gram of gold fed is kept, every gram of sand washed on
        var box = rt.SluiceBoxes["riffles"];
        var (fed, gold, sand, lostGold) = (box.Fed, box.KeptHeavy, box.KeptLight, box.PassedHeavy);
        for (int k = 0; k < 6000; k++) rt.Step(0.01);
        Assert.Equal(6, box.Fed - fed, 6);
        Assert.Equal(0.02 * 6, box.KeptHeavy - gold, 9);
        Assert.Equal(0, box.KeptLight - sand, 9);
        Assert.Equal(0, box.PassedHeavy - lostGold, 9);
        // while the race was still a trickle, filling, the box caught some sand too: a slower flow lifts less
        Assert.True(sand > 0, "the first trickle leaves sand behind the riffles");
    }
}
