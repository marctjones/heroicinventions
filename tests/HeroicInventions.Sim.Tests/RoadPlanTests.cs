using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// The rover's road planner (docs/plans/road-plan.md). Predicted by hand before running:
/// <list type="bullet">
/// <item>a soil plane at 25 degrees, the target 8 m straight up it: one straight stretch facing it (yaw 270, +x), graded to 20
/// degrees; the cut grows by tan 25 − tan 20 = 0.102 m a metre, so about 0.5 · 6 m · 0.6 m · 1.7 m ≈ 3 m³ (the road ends 2.1 m
/// short of the target, and the pad runs a metre on);</item>
/// <item>along the contour of a 14 degree cross slope: level along, level across, a bench cut 0.21 m into the uphill edge
/// (0.85 tan 14) and as much filled at the downhill one;</item>
/// <item>a rock ridge 0.2 m high across a gentle slope is ridden over as it lies (nothing of it is cut); one 1 m high, steeper than
/// 27 degrees a metre and the whole width of the ground, leaves no road at all;</item>
/// <item>spoil from such a bench is cast left (downhill, landing 1.4 tan 14 = 0.35 m below the road, more than the 0.3 needed;
/// the last station, where the road is held level into the slope, lands only 0.24 below and is hauled) unless a crate lies down its
/// fall line: then it is hauled;</item>
/// <item>a 32 degree plane 16 m long: straight up at 20 degrees the cut would grow to (tan 32 − tan 20) 13.9 = 3.6 m (≈ 43 m³),
/// so the road switches back across it, turning on a landing;</item>
/// <item>the Lonely Rover's opening (victoria.map settled): from the rover at (249.8, 137.9) to the bank's crate at (254.5, 137.9),
/// one straight stretch facing +x over the rock the rover already climbs and onto the rock shelf at −44.98 under the rubble; four
/// stations, 13 to 22 buckets, every one past x 251.5 hauled back to the shoulder (a left heap on the shelf would spill back).</item>
/// </list>
/// </summary>
public class RoadPlanTests
{
    private static readonly RoadLimits Limits = new();
    private static double Tan(double deg) => Math.Tan(deg * Math.PI / 180);
    private static double NoFloor(double x, double z) => double.NegativeInfinity;

    [Fact]
    public void A_25_degree_plane_is_cut_straight_up_at_20_degrees()
    {
        var plan = RoadPlanner.Plan((x, z) => Tan(25) * x, NoFloor, [], (0, 0), (8, 0), Limits);
        Console.WriteLine($"PLANE25 {plan.Why}");
        Assert.True(plan.Found, plan.Why);
        var leg = Assert.Single(plan.Segments);
        Assert.Equal(270, leg.HeadingDeg, 6);
        Assert.InRange(leg.GradeDeg, 19.0, 20.0 + 1e-9);
        Assert.InRange(plan.Volume, 1.5, 4.5);
        Assert.All(plan.Stations, s => Assert.Equal(270, s.HeadingDeg, 6));
        Assert.All(plan.Stations, s => Assert.Equal(0, s.TeethZ, 6));
    }

    [Fact]
    public void Along_a_14_degree_cross_slope_the_road_is_a_level_bench()
    {
        var plan = RoadPlanner.Plan((x, z) => Tan(14) * z, NoFloor, [], (0, 0), (8, 0), Limits);
        Console.WriteLine($"CROSS14 {plan.Why}");
        Assert.True(plan.Found, plan.Why);
        var leg = Assert.Single(plan.Segments);
        Assert.Equal(0, leg.GradeDeg, 6);
        Assert.All(plan.Stations, s => Assert.Equal(0, s.CutTo, 6));   // level across: the line's own height
        // the uphill half of a 1.7 m road: 0.85 · 0.21 / 2 ≈ 0.09 m³ a metre (sampled every 0.25 m: 0.081)
        double perMetre = plan.Volume / (plan.Stations[^1].TeethX - plan.Stations[0].TeethX + Limits.Advance);
        Assert.InRange(perMetre, 0.06, 0.11);
    }

    [Fact]
    public void A_low_rock_hump_is_driven_over_and_a_high_one_leaves_no_road()
    {
        double Ridge(double x, double h) => x >= 3 && x <= 3.5 ? h : x > 2.5 && x < 3 ? h * (x - 2.5) / 0.5 : x > 3.5 && x < 4 ? h * (4 - x) / 0.5 : 0;
        Func<double, double, double> surface(double h) => (x, z) => Tan(10) * x + Ridge(x, h);
        Func<double, double, double> floor(double h) => (x, z) => Ridge(x, h) > 0 ? Tan(10) * x + Ridge(x, h) : double.NegativeInfinity;
        var low = RoadPlanner.Plan(surface(0.2), floor(0.2), [], (0, 0), (8, 0), Limits);
        Console.WriteLine($"HUMP {low.Why}");
        Assert.True(low.Found, low.Why);
        Assert.Single(low.Segments);
        Assert.All(low.Stations, s => Assert.False(s.TeethX > 2.6 && s.TeethX < 3.9, $"station {s.Index} cuts the rock at {s.TeethX:0.00}"));
        var high = RoadPlanner.Plan(surface(1.0), floor(1.0), [], (0, 0), (8, 0), Limits);
        Assert.False(high.Found);
        Console.WriteLine($"HIGH {high.Why}");
    }

    [Fact]
    public void Spoil_is_cast_left_downhill_unless_a_crate_lies_down_its_fall_line()
    {
        // a road along +x, climbing 5 degrees, on a slope rising 14 degrees to +z: a bench cut into the uphill side, its line on the
        // ground, so a heap 1.4 m to the left lands 0.35 m below it
        Func<double, double, double> ground = (x, z) => Tan(14) * z + Tan(5) * x;
        var clear = RoadPlanner.Plan(ground, NoFloor, [], (0, 0), (7, 0), Limits);
        Console.WriteLine($"SPOIL {clear.Why}: {string.Join(", ", clear.Stations.Select(s => s.Spoil))}");
        Assert.True(clear.Found, clear.Why);
        Assert.NotEmpty(clear.Stations);
        Assert.True(clear.Stations.Count(s => !s.Spoil.Haul) >= clear.Stations.Count - 1, "cast left where it lands well below the road");
        Assert.DoesNotContain(clear.Stations, s => s.Spoil.Why.Contains("crate"));
        // a crate 3 m down the slope from the road: every left heap would run onto it
        var crate = new[] { (X: 3.0, Z: -3.0, R: 1.0) };
        var blocked = RoadPlanner.Plan(ground, NoFloor, crate.Select(c => (c.X, c.Z, c.R)).ToList(), (0, 0), (7, 0), Limits);
        Console.WriteLine($"SPOIL-CRATE {blocked.Why}: {string.Join(", ", blocked.Stations.Select(s => s.Spoil))}");
        Assert.True(blocked.Found, blocked.Why);
        Assert.Contains(blocked.Stations, s => s.Spoil.Haul && s.Spoil.Why.Contains("crate"));
        Assert.All(blocked.Stations.Where(s => s.Spoil.Haul), s => Assert.False(double.IsNaN(s.Spoil.X), $"station {s.Index} has a haul spot"));
    }

    [Fact]
    public void A_long_32_degree_plane_is_climbed_by_two_legs_and_a_landing()
    {
        Func<double, double, double> ground = (x, z) => Tan(32) * x;
        var plan = RoadPlanner.Plan(ground, NoFloor, [], (0, 0), (16, 0), Limits with { Margin = 18 });
        Console.WriteLine($"PLANE32 {plan.Why}");
        Assert.True(plan.Found, plan.Why);
        Assert.True(plan.Segments.Count >= 2, plan.Why);
        Assert.Contains(plan.Segments, g => g.TurnsAtStart);
        Assert.All(plan.Segments, g => Assert.True(Math.Abs(g.GradeDeg) <= Limits.StraightDeg + 1e-9, $"a stretch at {g.GradeDeg:0.0} deg"));
        // each turn is on a landing: the stretch into it ends level over its last metre
        for (int k = 1; k < plan.Segments.Count; k++)
            if (plan.Segments[k].TurnsAtStart)
            {
                var into = plan.Segments[k - 1];
                Assert.Equal(into.Level1, plan.Segments[k].Level0, 9);
            }
    }

    [Fact]
    public void Terrain_FloorAt_reads_the_rock_under_the_rubble_as_a_patch_would()
    {
        var t = Victoria();
        // x 250-255 (the cell over the bank): rubble on rock whose top was -44.98 (the probe in the game found -44.98 under every tooth)
        Assert.Equal(-44.98, t.FloorAt(252, 137.9)!.Value, 2);
        Assert.Equal(-44.98, t.FloorAt(253.5, 137.0)!.Value, 2);
        // x 250.3: the ground is below that rock top, so it is the rock itself: its floor is its surface
        Assert.Equal(t.CoarseSurfaceAt(250.3, 137.9), t.FloorAt(250.3, 137.9)!.Value, 9);
        // and a patch made there says the same, node for node
        var w = t.WorkAt(252, 137.9)!;
        foreach (var (x, z) in new[] { (251.0, 137.9), (252.0, 137.0), (250.25, 137.75), (248.0, 137.9) })
            Assert.Equal(w.Floor[(int)Math.Round((x - w.MinX) / WorkedGround.FineCell) + (int)Math.Round((z - w.MinZ) / WorkedGround.FineCell) * w.Nx], t.FloorAt(x, z)!.Value, 9);
        Assert.Null(t.FloorAt(1e6, 0));
    }

    [Fact]
    public void The_opening_road_runs_straight_onto_the_rock_shelf_and_hauls_its_spoil()
    {
        var t = Victoria();
        var crate = new List<(double X, double Z, double R)> { (254.5, 137.9, 0.35) };
        for (double x = 247; x <= 254.51; x += 0.5)
            Console.WriteLine($"  x {x:0.0}: surface {t.HeightAt(x, 137.9):0.00} floor {t.FloorAt(x, 137.9):0.00}; at z 137.05 {t.HeightAt(x, 137.05):0.00}/{t.FloorAt(x, 137.05):0.00}, z 138.75 {t.HeightAt(x, 138.75):0.00}/{t.FloorAt(x, 138.75):0.00}");
        var plan = RoadPlanner.Plan(t.HeightAt, (x, z) => t.FloorAt(x, z) ?? double.NegativeInfinity, crate, (249.8, 137.9), (254.5, 137.9), Limits);
        Console.WriteLine($"VICTORIA {plan.Why}");
        foreach (var s in plan.Stations)
            Console.WriteLine($"  station {s.Index} at ({s.X:0.00} {s.Z:0.00}) teeth ({s.TeethX:0.00} {s.TeethZ:0.00}) cut to {s.CutTo:0.00}: {s.Volume:0.00} m3, {s.Buckets} buckets, {s.Spoil}");
        Assert.True(plan.Found, plan.Why);
        var leg = Assert.Single(plan.Segments);
        Assert.Equal(270, leg.HeadingDeg, 6);
        Assert.Equal(4, plan.Stations.Count);
        Assert.InRange(plan.Buckets, 13, 22);
        Assert.All(plan.Stations.Where(s => s.TeethX > 251.5), s => Assert.True(s.Spoil.Haul, $"station {s.Index}: {s.Spoil}"));
        Assert.All(plan.Stations.Where(s => s.TeethX > 251.5), s => Assert.Equal(-44.98, s.CutTo, 2));   // the shelf: the rock's own level
    }

    /// <summary>victoria.map as the opening loads it: settled, so the rubble the rim's slide left on the bedrock apron is there to be dug (#72).</summary>
    private static Terrain Victoria()
    {
        var t = Terrain.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "maps", "victoria.map")), "victoria.map");
        t.Settle(3.71);
        return t;
    }
}
