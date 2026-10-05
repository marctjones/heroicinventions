using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #61: the Lonely Rover's crater (racket/maps/victoria.rkt). The numbers are worked out in that
/// file's header: soils by where they lie, a rim section the storm has weakened to a critical height just
/// under the cliff it stands as, the wind a notch in the rim funnels across the floor. The slide's
/// burying of crates, which needs Jolt, is checked in racket/heroic/tests with the headless game.
/// </summary>
public class CraterTests
{
    private const double Mars = 3.71, Earth = 9.81;

    private static string MapText() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "maps", "victoria.map"));
    private static Terrain Load(string? text = null) => Terrain.Parse(text ?? MapText(), "victoria");

    /// <summary>The cell at a polar point of the crater: radius in metres, azimuth in degrees from +x toward +z.</summary>
    private static int At(Terrain t, double r, double azimuthDeg)
    {
        double a = azimuthDeg * Math.PI / 180;
        return t.CellAt(r * Math.Cos(a), r * Math.Sin(a)) ?? throw new InvalidOperationException("off the map");
    }

    private static string SoilAt(Terrain t, double r, double azimuthDeg) => t.Soils[t.Soil[At(t, r, azimuthDeg)]].Material;

    /// <summary>4c/(ρg)·tan(45° + φ/2), worked out here from a soil's cohesion, density and friction.</summary>
    private static double Critical(double cohesion, double density, double tanPhi, double g) =>
        4 * cohesion / (density * g) * Math.Tan(Math.PI / 4 + Math.Atan(tanPhi) / 2);

    private static double Volume(Terrain t) => t.Heights.Sum() * t.Cell * t.Cell;

    [Fact]
    public void TheCraterIsAboutEightHundredMetresAcrossAndSeventyDeepInsideTheMapBudget()
    {
        var t = Load();
        Assert.True(t.Count <= 40_000, $"{t.Count} cells");
        Assert.Equal(850, t.Width);                                       // the crater and its plain's margin
        // the floor, 70 m under the plain (the dunes 1.2 m either way), seen from the rim's crest
        double floor = t.Heights[At(t, 20, 0)];
        Assert.InRange(floor, -71.3, -68.7);
        Assert.InRange(t.Heights.Min(), -71.3, -70);
        Assert.InRange(t.HeightAt(0, 400), 3, 5.5);                       // the crest at the radius, 5 m over the plain
        Assert.InRange(t.HeightAt(0, -400), 3, 5.5);                      // and the same across: 800 m from crest to crest
        Assert.InRange(t.HeightAt(0, 420), 4, 5);                         // the ejecta falling away outside (the map ends 25 m past the rim)
    }

    [Fact]
    public void TheGroundLiesInLayers_DunesBedrockIceSilicaAndThePlain()
    {
        var t = Load();
        Assert.Equal("basalt-sand", SoilAt(t, 20, 0));                    // the dunes on the floor
        Assert.Equal("basalt-sand", SoilAt(t, 220, 100));                 // and the apron of talus up the foot of the wall
        Assert.Equal("bedrock", SoilAt(t, 350, 100));                     // the wall
        Assert.Equal("ice-cemented-regolith", SoilAt(t, 350, -90));       // the cold, pole-facing wall
        Assert.Equal("silica-sand", SoilAt(t, 320, 162.5));               // the pale layer in one bay
        Assert.Equal("sublimed-regolith", SoilAt(t, 350, 30));            // the rim section the storm has weakened
        Assert.Equal("regolith", SoilAt(t, 480, 45));                    // the plain outside the rim
        // cohesion: bedrock stands to tens of kilometres, and everything is its own number
        var c = t.Soils.ToDictionary(s => s.Material, s => s.Cohesion);
        Assert.Equal(5e7, c["bedrock"]);
        Assert.Equal(40_000, c["ice-cemented-regolith"]);
        Assert.Equal(16_000, c["sublimed-regolith"]);
        Assert.Equal(5_000, c["silica-sand"]);
        Assert.Equal(0, c["basalt-sand"]);
    }

    [Fact]
    public void TheWeakBlockStandsAsACliffPastWhatSublimedRegolithHolds_ButNotWhatTheIceHeld()
    {
        var t = Load();
        var sublimed = t.Soils.Single(s => s.Material == "sublimed-regolith");
        var ice = t.Soils.Single(s => s.Material == "ice-cemented-regolith");
        // 1.921 × 4c/(ρg): 22.1 m and 8.4 m for 16 kPa of 1500 kg/m³ at tan φ 0.70; 55.2 m and 20.9 m for 40 kPa
        Assert.Equal(Critical(16_000, 1500, 0.70, Mars), sublimed.CriticalHeight(Mars), 1e-9);
        Assert.InRange(sublimed.CriticalHeight(Mars), 22.0, 22.2);
        Assert.InRange(sublimed.CriticalHeight(Earth), 8.3, 8.4);
        Assert.InRange(ice.CriticalHeight(Mars), 55.1, 55.3);
        Assert.InRange(ice.CriticalHeight(Earth), 20.8, 21.0);
        // the cliff the weakened block stands as: the steepest step between a cell of it and one that is not
        double steepest = 0;
        for (int j = 0; j < t.Nz; j++)
            for (int i = 0; i < t.Nx; i++)
                foreach (var (di, dj) in new[] { (1, 0), (0, 1) })
                {
                    if (i + di >= t.Nx || j + dj >= t.Nz) continue;
                    int a = i + j * t.Nx, b = i + di + (j + dj) * t.Nx;
                    bool weakA = t.Soils[t.Soil[a]].Material == "sublimed-regolith", weakB = t.Soils[t.Soil[b]].Material == "sublimed-regolith";
                    if (weakA != weakB) steepest = Math.Max(steepest, weakA ? t.Heights[a] - t.Heights[b] : t.Heights[b] - t.Heights[a]);
                }
        Assert.InRange(steepest, 24, 30);                                 // 24 m proud of the wall, and the wall's own slope
        Assert.True(steepest > sublimed.CriticalHeight(Mars), "it is past what the sublimed regolith holds, even on Mars");
        Assert.True(steepest < ice.CriticalHeight(Mars), "and stood on the ice that held it before the storm");
    }

    [Fact]
    public void TheWeakenedRimComesDownWhenTheMapSettlesOnMars_AndNothingElseDoes()
    {
        var t = Load();
        var before = (double[])t.Heights.Clone();
        double volume = Volume(t);
        var (failures, passes) = t.Settle(Mars);
        Assert.InRange(failures, 10, 60);                                 // columns of the face, 5 m of rim each
        Assert.InRange(passes, 40, 200);
        // volume is kept exactly: soil only moves between cells, but for the rock that comes down as boulders (below)
        Assert.Equal(volume - t.BoulderVolume, Volume(t), 1e-3);
        // every cell that moved is the block's, its edges' or the apron it ran out over: east, within 30 degrees of azimuth 30
        int moved = 0;
        for (int k = 0; k < t.Count; k++)
        {
            if (Math.Abs(t.Heights[k] - before[k]) < 1e-9) continue;
            moved++;
            double x = t.CellX(k % t.Nx), z = t.CellZ(k / t.Nx), r = Math.Sqrt(x * x + z * z);
            double az = Math.Atan2(z, x) * 180 / Math.PI;
            Assert.InRange(az, 30 - 30, 30 + 30);
            Assert.InRange(r, 260, 470);
        }
        Assert.InRange(moved, 100, 2000);
        // the rim is rocky: 2% of what the failed faces lost comes down as 2 m cubes of granite, floor(0.02 V / 8 m3) of them
        Assert.InRange(t.Collapsed, 5_000, 12_000);
        int boulders = (int)Math.Floor(0.02 * t.Collapsed / 8 + 1e-9);
        Assert.Equal(boulders, t.Boulders.Count);
        Assert.InRange(boulders, 10, 30);
        Assert.Equal(boulders * 8.0, t.BoulderVolume, 1e-9);
        Assert.All(t.Boulders, b => { Assert.Equal(2.0, b.Size); Assert.Equal("granite", b.Material); });
        // it stands now: another look at the whole map finds nothing to give
        var (again, more) = t.Relax(0, 0, t.Nx - 1, t.Nz - 1, Mars);
        Assert.Equal(0, again);
        Assert.True(more <= 1, $"{more} more passes");
    }

    [Fact]
    public void TheSameCraterWithItsIceStillHoldingStandsWhole()
    {
        // 16000 -> 40000 Pa on the weakened soil: the crater as the storm found it
        string text = MapText().Replace("(sublimed-regolith 0.0 16000.0 ", "(sublimed-regolith 0.0 40000.0 ");
        Assert.NotEqual(MapText(), text);
        var t = Load(text);
        Assert.Equal(0, t.Settle(Mars).Failures);
    }

    [Fact]
    public void OnEarthTheCraterCrumblesFurther_AndTheGroundEarthLeavesStandsOnMars()
    {
        var mars = Load();
        int onMars = mars.Settle(Mars).Failures;
        var earth = Load();
        int onEarth = earth.Settle(Earth).Failures;
        Assert.True(onEarth > 2 * onMars, $"{onEarth} faces fail under Earth's gravity, {onMars} under Mars's");   // cliffs stand 2.6 times higher on Mars
        Assert.Equal(0, earth.Settle(Mars).Failures);                    // an Earth map on Mars stands, overbuilt
    }

    [Fact]
    public void SettlingInStepsPlaysTheSameCollapseOutOverTheSecondsTheRateSays()
    {
        var at_once = Load();
        at_once.Settle(Mars);
        var stepped = Load();
        Assert.Equal(20, stepped.SettleRate);
        Assert.True(stepped.SettleOnLoad);
        int passes = 0, failures = 0, ticks = 0;
        while (!stepped.Stood && ticks < 100_000)
        {
            // as the world does: 20 passes a second at 120 ticks a second, so a pass every sixth tick
            var step = stepped.SettleStep(ticks % 6 == 0 ? 1 : 0, Mars);
            passes += step.Passes; failures += step.Failures; ticks++;
        }
        Assert.True(stepped.Stood);
        for (int k = 0; k < at_once.Count; k++) Assert.Equal(at_once.Heights[k], stepped.Heights[k], 1e-5);
        Assert.Equal(at_once.Boulders.Count, stepped.Boulders.Count);     // the rock comes down once the ground stands, the same rock
        for (int b = 0; b < at_once.Boulders.Count; b++)
        {
            Assert.Equal(at_once.Boulders[b].X, stepped.Boulders[b].X, 1e-5);
            Assert.Equal(at_once.Boulders[b].Z, stepped.Boulders[b].Z, 1e-5);
        }
        double seconds = passes / stepped.SettleRate;
        Assert.InRange(seconds, 3, 6);                                    // a few seconds to watch, not a jump
        Assert.InRange(failures, 10, 60);
    }

    [Fact]
    public void WaterReleasedOnTheWallRunsDownToTheFloorsLowestPoint()
    {
        var t = Load();
        double lowest = t.Heights.Min();
        var water = new ShallowWater2D(t) { Gravity = Mars, Manning = t.Roughness };
        // 100 m³ let out in small pours on the cold north wall, 300 m from the centre
        for (int pour = 0; pour < 20; pour++)
        {
            Assert.True(water.AddWater(0, -300, 5));
            for (int s = 0; s < 60; s++) water.Step(0.5);
        }
        for (int s = 0; s < 2 * 1200; s++) water.Step(0.5);               // and 20 minutes more
        double volume = 0, bed = 0;
        for (int k = 0; k < t.Count; k++) { volume += water.Depths[k]; bed += water.Depths[k] * t.Heights[k]; }
        Assert.Equal(100, volume * t.Cell * t.Cell, 3);                   // every cubic metre of it
        Assert.InRange(bed / volume, lowest - 0.01, lowest + 0.15);       // lying in the floor's dune trough, 71 m below the rim
        Assert.Equal(0, water.Leaked);
    }

    [Fact]
    public void AMapWithAWindFieldAndASettleRateRoundTripsThroughItsFile()
    {
        var t = Load();
        Assert.NotNull(t.Wind);
        Assert.Equal(new WindField(0, 0, 200, 6, 80, 0.3, 0.35, 2, 0.25), t.Wind);
        var again = Terrain.Parse(t.Write(), "again");
        Assert.Equal(t.Wind, again.Wind);
        Assert.Equal(20, again.SettleRate);
        Assert.True(again.SettleOnLoad);
        Assert.Contains("(settle 20.0)", t.Write());
        Assert.Equal(new BoulderSpec(0.02, 2.0, "granite"), again.Soils.Single(s => s.Material == "sublimed-regolith").Boulders);
        // a map with a plain true still settles at once, as in #54
        var cliff = Terrain.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "maps", "cliff.map")), "cliff");
        Assert.True(cliff.SettleOnLoad);
        Assert.Equal(0, cliff.SettleRate);
        Assert.Null(cliff.Wind);
    }

    [Fact]
    public void TheWindIsAProductOfCorridorDayAndGusts_StrongestBelowTheNotchAndAtNight()
    {
        var w = Load().Wind!;
        // on the line from the notch (azimuth 200) through the centre the corridor is whole; 160 m across it, 0.3 + 0.7 e^-4
        Assert.Equal(1, w.Corridor(-94.0, -34.2), 1e-3);
        Assert.Equal(0.3 + 0.7 * Math.Exp(-4), w.Corridor(54.7, -150.4), 1e-3);
        Assert.Equal(0.3, w.Corridor(0, -400), 1e-6);                     // far across it, only the base
        // the crater's walls drain cold air down at night (peak at 2 h) and draw warm air up by day
        Assert.Equal(1.35, w.DailyFactor(2), 1e-12);
        Assert.Equal(0.65, w.DailyFactor(14), 1e-12);
        Assert.Equal(1 + 0.35 * Math.Cos(2 * Math.PI * (8 - 2) / 24), w.DailyFactor(8), 1e-12);
        // the gusts are the same every run
        Assert.Equal(w.Gusts(100), w.Gusts(100));
        Assert.Equal(1 + 0.25 * (Math.Sin(2 * Math.PI * 10 / 37) + Math.Sin(2 * Math.PI * 10 / 91 + 1.3)) / 2, w.Gusts(10), 1e-12);
        Assert.Equal(6 * 1.35 * w.Gusts(0), w.SpeedAt(-94.0, -34.2, 2, 0), 0.02);
    }

    [Fact]
    public void AMillOnTheFloorTakesTheWindOfTheFieldWhereItStands_AndItsPowerFollowsTheCube()
    {
        var materials = MaterialLibrary.LoadDefault();
        var world = WorldDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "worlds", "crater-wind.world")), "crater-wind");
        var ground = new WorldGround(Load());
        var mills = new Dictionary<string, MachineRuntime>();
        foreach (var p in world.Placements)
        {
            var def = WorldDef.Placed(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", p.Machine + ".machine"))), p, ground.Ground);
            mills[p.Label] = new MachineRuntime(def, materials);
            ground.Attach(p.Label, mills[p.Label]);
        }
        var on = mills["in-corridor"];
        var off = mills["off-corridor"];
        for (int tick = 0; tick < 600; tick++)
        {
            foreach (var m in mills.Values) m.Step(0.1);
            ground.Step(0.1);
        }
        // worked out by hand from the file's numbers: 6 m/s x corridor x daily x gusts
        double hour = on.Sun.Time, seconds = on.Sun.Sols * on.Sun.SolLength;
        double daily = 1 + 0.35 * Math.Cos(2 * Math.PI * (hour - 2) / 24);
        double gusts = 1 + 0.25 * (Math.Sin(2 * Math.PI * seconds / 37) + Math.Sin(2 * Math.PI * seconds / 91 + 1.3)) / 2;
        double corridor(double across) => 0.3 + 0.7 * Math.Exp(-across * across / 6400);
        double vOn = 6 * corridor(0.01) * daily * gusts, vOff = 6 * corridor(160.04) * daily * gusts;
        Assert.Equal(vOn, on.Windmills["mill"].Wind, 0.01);
        Assert.Equal(vOff, off.Windmills["mill"].Wind, 0.02);
        Assert.True(vOn > 2.5 * vOff, "the corridor is the place for a mill");
        // the wind carries 1/2 rho A v^3 through the sails: the same mill in a wind 0.313 times as strong takes 0.0306 times the power
        double ratio = vOff / vOn;
        Assert.Equal(ratio * ratio * ratio, off.Windmills["mill"].WindPower / on.Windmills["mill"].WindPower, 1e-3);
        Assert.Equal(0.5 * on.Windmills["mill"].AirDensity * Math.PI * 100 * Math.Pow(vOn, 3), on.Windmills["mill"].WindPower, 1e-3 * on.Windmills["mill"].WindPower);
    }
}
