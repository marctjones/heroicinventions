using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #200: water on the rover's worked ground. A patch of 0.25 m cells over the map's 5 m ones carries its own nested
/// shallow-water grid, coupled to the coarse one face by face. Each prediction was worked out before the run, under Mars's
/// gravity (3.71 m/s²) as the game has it.
/// </summary>
public class TrenchWaterTests
{
    private const double G = 3.71;

    /// <summary>
    /// A map of 5 m cells, 16 a side (80 m), its ground z = −slope · x, closed or open at the edges. Patches are made by
    /// <see cref="Terrain.WorkAt"/> as the rover makes them.
    /// </summary>
    private static Terrain Map(double slope = 0, double infiltration = 0, bool open = false, IReadOnlyList<MapSource>? sources = null)
    {
        const int n = 16;
        var t = new Terrain
        {
            Name = "m", X0 = -40, Z0 = -40, Cell = 5, Nx = n, Nz = n, Heights = new double[n * n], Soil = new int[n * n],
            Soils = [new SoilSpec("regolith", infiltration, Friction: 0.7, Density: 1500)], OpenEdges = open, Sources = sources ?? [],
        };
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++) t.Heights[i + j * n] = -slope * t.CellX(i);
        return t;
    }

    private static double Ledger(ShallowWater2D w) => w.Poured - (w.Volume + w.Infiltrated + w.Leaked + w.Drained + w.Clipped);

    private static void AssertLedger(ShallowWater2D w) =>
        Assert.True(Math.Abs(Ledger(w)) <= 1e-9 * Math.Max(w.Poured, 1e-6), $"ledger off by {Ledger(w):E3} m³ of {w.Poured:F6} poured");

    /// <summary>The fine-water grid of the patch round a point, the water stepped once so the patch is joined to it.</summary>
    private static (WorkedGround Patch, PatchWater Grid) Patch(Terrain t, ShallowWater2D w, double x, double z)
    {
        var patch = t.WorkAt(x, z)!;
        w.Step(1e-3);
        var grid = w.Patches.Single(p => p.Patch == patch);
        return (patch, grid);
    }

    /// <summary>
    /// Volume is conserved across the seam to 1e-9 of what was poured, every step: a 100 L/s spring upslope of a patch on a
    /// 1-in-100 slope runs down onto the patch, along a channel cut in it, out of its far side onto the coarse slope below (the map's edges open),
    /// some soaking into the soil all the way, for 200 s. At the end water has crossed both seams: the fine grid holds some, the coarse
    /// cells below the patch hold some.
    /// </summary>
    [Fact]
    public void WaterRunsIntoAndOutOfAPatchAndNoneIsMadeOrLost()
    {
        const double S = 0.01;
        // the spring 4.5 m upslope of the patch the rover will make round (0, 0), on coarse ground
        var t = Map(S, infiltration: 2e-6, open: true, sources: [new MapSource("spring", -17, 0, 0.1)]);
        var wet = new ShallowWater2D(t) { Gravity = G };
        var (patch, grid) = Patch(t, wet, 0, 0);
        Assert.True(patch.MinX > -17 && patch.Inside(0, 0));
        double zc = 0;
        patch.Shape((x, z) => -S * x - (Math.Abs(z - zc) <= 0.5 + 1e-9 ? 0.15 : 0));   // a channel 1 m wide and 15 cm deep, end to end
        for (int i = 0; i < 60 * 200; i++)
        {
            wet.Step(1.0 / 60);
            AssertLedger(wet);
        }
        Console.WriteLine($"seam: poured {wet.Poured:F4} m³, standing {wet.Volume:F4} (patch {grid.Water.Volume:F4}), soaked {wet.Infiltrated:F4}, run off {wet.Leaked:F4}, ledger {Ledger(wet):E2}");
        double fine = grid.Water.Volume;
        double below = 0;
        for (int j = 0; j < t.Nz; j++)
            for (int i = 0; i < t.Nx; i++)
                if (t.CellX(i) > patch.MaxX) below += wet.Depths[i + j * t.Nx] * t.Cell * t.Cell;
        Console.WriteLine($"seam: below the patch {below:F4} m³");
        Assert.True(fine > 0.05, $"the patch holds {fine:F3} m³");
        Assert.True(below > 0.05, $"the coarse ground below it holds {below:F3} m³");
        Assert.True(wet.Infiltrated > 0);
        Assert.True(wet.Clipped < 1e-9 * wet.Poured, $"clipped {wet.Clipped:E2}");
        // and the coarse cells under the patch held none of it
        for (int c = 0; c < t.Count; c++) if (wet.UnderPatch(c)) Assert.Equal(0, wet.Depths[c]);
    }

    /// <summary>
    /// A dug trench fills in V/Q. On level ground, a pit 4 m by 1 m (nodes −2..2 by −0.5..0.5 m round its centre) cut 0.5 m
    /// down holds, below the rim, the dug volume: 17 × 5 nodes × 0.0625 m² × 0.5 m = 2.65625 m³ (the fine cells' beds are their
    /// four nodes' mean, so the cells hold exactly what the nodes lost). A 10 L/s inflow at its middle fills it in
    /// V/Q = 265.6 s, and only then does water stand on the level ground round it. Halfway, at 132.8 s, it is 1.328 m³ in:
    /// the 64 cells of full depth (4 m²) and the rim's half-depth cells (bed −0.25 m) are under water once the level passes
    /// −0.25 m, which holds 4 × 0.25 + the half-cells' 0 = 1.0 m³; the 0.328 m³ more over the 4 m² floor and the 40 rim cells
    /// (2.5 m²) and 4 corners (0.25 m², bed −0.125, still dry) is 0.328 / 6.5 = 5.05 cm: the level is −0.1995 m.
    /// </summary>
    [Fact]
    public void ADugTrenchFillsInItsVolumeOverTheInflow()
    {
        var t = Map();
        var w = new ShallowWater2D(t) { Gravity = G };
        var (patch, grid) = Patch(t, w, 0, 0);
        double xc = patch.NodeX(patch.Nx / 2), zc = patch.NodeZ(patch.Nz / 2);
        static bool In(double v, double half) => Math.Abs(v) <= half + 1e-9;
        patch.Shape((x, z) => In(x - xc, 2) && In(z - zc, 0.5) ? -0.5 : 0);
        w.Step(1e-3);
        var bed = grid.Bed;
        // what the pit holds below its rim, from the fine cells' own beds: the nodes' 2.65625 m³
        Assert.Equal(2.65625, Enumerable.Range(0, bed.Count).Sum(c => Math.Max(0, -bed.Heights[c])) * bed.Cell * bed.Cell, 9);
        const double q = 0.01, v = 2.65625;
        double dt = 1.0 / 60, time = 0, spilled = double.NaN, halfway = double.NaN;
        while (time < 1.2 * v / q && double.IsNaN(spilled))
        {
            w.AddWater(xc, zc, q * dt);
            w.Step(dt);
            time += dt;
            if (double.IsNaN(halfway) && time >= v / q / 2)
            {
                // the mean level over the pit's floor (the inflow's own cell stands a little higher)
                var floor = Enumerable.Range(0, bed.Count).Where(c => bed.Heights[c] <= -0.5 + 1e-9).ToList();
                halfway = floor.Average(c => bed.Heights[c] + grid.Water.Depths[c]);
            }
            for (int c = 0; c < bed.Count; c++)
                if (bed.Heights[c] >= -1e-9 && grid.Water.Depths[c] > 1e-3) { spilled = time; break; }
            AssertLedger(w);
        }
        Console.WriteLine($"trench: spilled at {spilled:F1} s against V/Q = {v / q:F1} s; level halfway {halfway:F4} m against -0.1995");
        Assert.Equal(v / q, spilled, 0.02 * v / q);
        Assert.Equal(-0.1995, halfway, 0.003);
    }

    /// <summary>
    /// Communicating vessels: two basins 2 m square and 0.5 m deep joined by a channel 6 m long and 0.5 m wide, as deep, all
    /// level. 0.6 m³ poured into one in the first 20 s stands, once still, at one level in both and all along the channel.
    /// Below −0.25 m only the cells of full depth are wet (a cell on the cut's edge has two nodes dug and two not: its bed is
    /// −0.25 m): the basins' floors are 8 × 8 cells each (4 m²) and the channel's 32 × 2 (4 m²),
    /// meeting them end to end: A = 4 + 4 + 4 = 12 m² (checked against the grid's own beds). The level is
    /// −0.5 + 0.6 / 12 = −0.4500 m, the same within a millimetre in both basins and the channel.
    /// </summary>
    [Fact]
    public void WaterInALevelDugChannelStandsAtOneLevel()
    {
        var t = Map();
        var w = new ShallowWater2D(t) { Gravity = G };
        var (patch, grid) = Patch(t, w, 0, 0);
        double xc = patch.NodeX(patch.Nx / 2), zc = patch.NodeZ(patch.Nz / 2);
        static bool In(double v, double half) => Math.Abs(v) <= half + 1e-9;
        bool Dug(double x, double z) =>
            In(x - (xc - 5), 1) && In(z - zc, 1) || In(x - (xc + 5), 1) && In(z - zc, 1) || In(x - xc, 4) && In(z - zc, 0.25);
        patch.Shape((x, z) => Dug(x, z) ? -0.5 : 0);
        double dt = 1.0 / 60;
        for (int i = 0; i < 60 * 20; i++)
        {
            w.AddWater(xc - 5, zc, 0.03 * dt);   // 0.6 m³ over 20 s into the west basin
            w.Step(dt);
        }
        for (int i = 0; i < 60 * 600; i++) w.Step(dt);
        AssertLedger(w);
        var bed = grid.Bed;
        double floor = Enumerable.Range(0, bed.Count).Count(c => bed.Heights[c] <= -0.5 + 1e-9) * bed.Cell * bed.Cell;
        Assert.Equal(12, floor, 9);
        double predicted = -0.5 + 0.6 / floor;
        double Level(double x) => bed.HeightAt(x, zc) + w.DepthAt(x, zc);
        double west = Level(xc - 5), middle = Level(xc), east = Level(xc + 5);
        Console.WriteLine($"vessels: floor {floor:F4} m², predicted {predicted:F4}; west {west:F4} channel {middle:F4} east {east:F4}");
        Assert.Equal(predicted, west, 0.001);
        Assert.Equal(predicted, middle, 0.001);
        Assert.Equal(predicted, east, 0.001);
        Assert.Equal(0.6, w.Volume, 9);
    }

    /// <summary>
    /// A channel down a slope carries its flow at the solver's friction law. The solver is depth-averaged and its friction is
    /// Manning's on the depth (a wide sheet, R = h; the channel's walls add none), n scaled for gravity as everywhere
    /// (n² · 9.81 / g): in steady uniform flow g S = 9.81 n² u² / h^(4/3), so per metre of width
    ///   q = h^(5/3) √(g S / 9.81) / n,  h = (q n / √(g S / 9.81))^(3/5).
    /// A channel 1 m wide whose bed falls at S = 0.02 (cut 0.2 m deep at its head into a 3-in-100 slope, running out onto it 20 m on),
    /// fed 10 L/s at its head, n = 0.03, g = 3.71: q = 0.01 m²/s, h = (0.01 · 0.03 / √(3.71 · 0.02 / 9.81))^0.6 = 3.33 cm,
    /// u = q / h = 0.300 m/s.
    /// </summary>
    [Fact]
    public void AChannelOnASlopeCarriesItsFlowByManning()
    {
        const double S = 0.03, Sc = 0.02, Q = 0.01, n = 0.03, b = 1;
        var t = Map(S, open: true);
        var w = new ShallowWater2D(t) { Gravity = G, Manning = n };
        var (patch, grid) = Patch(t, w, 0, 0);
        double zc = patch.NodeZ(patch.Nz / 2);
        double head = grid.Bed.X0 + 2, tail = head + 20, cut = (S - Sc) * (tail - head);
        static bool In(double v, double half) => Math.Abs(v) <= half + 1e-9;
        patch.Shape((x, z) => -S * x - (In(z - zc, b / 2) && x >= head - 1e-9 && x <= tail ? cut * (tail - x) / (tail - head) : 0));
        double dt = 1.0 / 60;
        for (int i = 0; i < 60 * 300; i++)
        {
            w.AddWater(head + 0.375, zc, Q * dt);
            w.Step(dt);
        }
        AssertLedger(w);
        double expected = Math.Pow(Q / b * n / Math.Sqrt(G * Sc / 9.81), 0.6);
        // the depth and the discharge along the middle of the channel, 6 to 12 m down it, across its width
        double depth = 0, discharge = 0; int samples = 0;
        var bed = grid.Bed;
        for (double x = head + 6.125; x < head + 12; x += 0.25)
        {
            double across = 0, deep = 0;
            for (double z = zc - 0.375; z <= zc + 0.375 + 1e-9; z += 0.25)
            {
                int c = bed.CellAt(x, z)!.Value;
                deep += grid.Water.Depths[c] / 4;
                across += grid.Water.VelocityAt(c).U * grid.Water.Depths[c] * 0.25;
            }
            depth += deep; discharge += across; samples++;
        }
        depth /= samples; discharge /= samples;
        Console.WriteLine($"channel: depth {depth * 100:F2} cm against Manning's {expected * 100:F2} cm; discharge {discharge * 1000:F2} L/s against {Q * 1000} L/s; u {discharge / depth:F3} m/s");
        Assert.Equal(Q, discharge, 0.03 * Q);
        Assert.Equal(expected, depth, 0.05 * expected);
    }

    /// <summary>
    /// The patch's soil soaks water away as the map's does: a pit 4 m by 1 m and 0.5 m deep on regolith that takes 10 µm/s,
    /// filled 0.3 m deep in its floor and its rim cells under water, loses 1e-5 m/s over every wet cell: over 100 s, with the
    /// wet area A from the grid, A · 1e-3 m³ (the level falls 1 mm, no cell dries).
    /// </summary>
    [Fact]
    public void ThePatchsSoilSoaksWaterAwayAtItsRate()
    {
        var t = Map(infiltration: 1e-5);
        var w = new ShallowWater2D(t) { Gravity = G };
        var (patch, grid) = Patch(t, w, 0, 0);
        double xc = patch.NodeX(patch.Nx / 2), zc = patch.NodeZ(patch.Nz / 2);
        static bool In(double v, double half) => Math.Abs(v) <= half + 1e-9;
        patch.Shape((x, z) => In(x - xc, 2) && In(z - zc, 0.5) ? -0.5 : 0);
        w.Step(1e-3);
        w.Fill((x, z) => Math.Max(0, -0.2 - grid.Bed.HeightAt(x, z)));
        double wetArea = w.WetArea, before = w.Volume;
        Assert.True(wetArea > 5, $"{wetArea} m² wet");
        for (int i = 0; i < 6000; i++) w.Step(1.0 / 60);
        Console.WriteLine($"soak: {w.Infiltrated:F6} m³ against {wetArea * 1e-3:F6}");
        Assert.Equal(wetArea * 1e-3, w.Infiltrated, 1e-6);
        Assert.Equal(before - w.Infiltrated, w.Volume, 9);
        AssertLedger(w);
    }

    /// <summary>
    /// A patch made where water already stands takes that water into its grid, every drop: a pond 0.2 m deep on level ground,
    /// then the rover starts a patch in it and another 20 m off whose box meets it (the two merge). The volume is the same to
    /// rounding throughout, the coarse cells under the patch hold none, and the fine water stands at the pond's level.
    /// </summary>
    [Fact]
    public void APatchMadeInStandingWaterTakesItAndMergedPatchesKeepIt()
    {
        var t = Map();
        var w = new ShallowWater2D(t) { Gravity = G };
        w.Fill((_, _) => 0.2);
        double before = w.Volume;
        var first = t.WorkAt(0, 0)!;
        w.Step(1.0 / 60);
        Assert.Equal(before, w.Volume, 9);
        Assert.Equal(0.2, w.DepthAt(first.NodeX(first.Nx / 2), first.NodeZ(first.Nz / 2)), 9);
        for (int i = 0; i < 600; i++) w.Step(1.0 / 60);
        var merged = t.WorkAt(first.MaxX + 1, 0)!;
        Assert.Single(t.Worked);
        Assert.NotSame(first, merged);
        Assert.Equal(before, w.Volume, 9);
        for (int i = 0; i < 600; i++) w.Step(1.0 / 60);
        Assert.Equal(before, w.Volume, 9);
        for (int c = 0; c < t.Count; c++) if (w.UnderPatch(c)) Assert.Equal(0, w.Depths[c]);
        Assert.Equal(0.2, w.DepthAt(merged.NodeX(merged.Nx / 2), merged.NodeZ(merged.Nz / 2)), 6);
        Assert.True(w.Patches.Single().Water.MaxDepth < 0.2 + 1e-6, "still: no wave from the seam or the merge");
    }

    /// <summary>
    /// The water in a patch survives a save: a world's ground saved as the game saves it (the coarse water by the state walk,
    /// the patch by its own form, now with its water) and loaded into a fresh world in the game's order, then both run on a
    /// minute: they hold the same water, cell for cell.
    /// </summary>
    [Fact]
    public void WaterInAPatchSurvivesASave()
    {
        (Terrain, WorldGround) World()
        {
            var t = Map(0.01, open: true);
            return (t, new WorldGround(t));
        }
        var (t1, g1) = World();
        g1.Water.Gravity = G;
        var (patch, _) = Patch(t1, g1.Water, 0, 0);
        double zc = patch.NodeZ(patch.Nz / 2);
        patch.Shape((x, z) => -0.01 * x - (Math.Abs(z - zc) <= 0.5 + 1e-9 ? 0.2 : 0));
        for (int i = 0; i < 600; i++) { g1.Water.AddWater(patch.MinX - 5, zc, 0.02 / 60); g1.Step(1.0 / 60); }
        var ground = RuntimeState.CaptureGround(g1);
        string text = SExprWriter.Print(t1.SaveWorked()!);
        Assert.Contains("(water ", text);
        var worked = SExprReader.ReadAll(text).OfType<SList>().Single();

        var (t2, g2) = World();
        g2.Water.Gravity = G;
        Assert.Empty(RuntimeState.RestoreGround(g2, SExprReader.ReadAll(SExprWriter.Print(ground)).OfType<SList>().Single()));
        t2.LoadWorked(worked);
        for (int i = 0; i < 3600; i++)
        {
            g1.Water.AddWater(patch.MinX - 5, zc, 0.02 / 60); g1.Step(1.0 / 60);
            g2.Water.AddWater(patch.MinX - 5, zc, 0.02 / 60); g2.Step(1.0 / 60);
        }
        var (a, b) = (g1.Water.Patches.Single().Water, g2.Water.Patches.Single().Water);
        Assert.True(a.Volume > 0.1);
        for (int c = 0; c < a.Depths.Count; c++) Assert.Equal(a.Depths[c], b.Depths[c], 12);
        for (int c = 0; c < t1.Count; c++) Assert.Equal(g1.Water.Depths[c], g2.Water.Depths[c], 12);
        Assert.Equal(g1.Water.Volume, g2.Water.Volume, 9);
    }
}
