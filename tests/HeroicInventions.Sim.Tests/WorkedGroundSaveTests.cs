using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #199: the rover's worked ground (#63) survives a save. A run is saved mid-dig, loaded into a fresh copy of the map, and both
/// go on with the same dig, dump and settle steps: every node of every patch must come out the same, exactly (the save writes
/// numbers to the digit that reads back as the same double), and the carry ceilings must still stop spoil climbing.
/// </summary>
public class WorkedGroundSaveTests
{
    private const double G = 3.71;
    private static readonly SoilSpec Regolith = new("regolith", 0, Cohesion: 0, Friction: 0.7, Density: 1500);
    private static readonly SoilSpec Rock = new("bedrock", 0, Cohesion: 5e7, Friction: 0.6, Density: 2700);

    /// <summary>A 90 m square map of 5 m cells on a 6 degree slope, with a bar of bedrock across it.</summary>
    private static Terrain Map()
    {
        const int n = 18;
        var t = new Terrain
        {
            Name = "m", X0 = -45, Z0 = -45, Cell = 5, Nx = n, Nz = n, Heights = new double[n * n], Soil = new int[n * n],
            Soils = [Regolith, Rock], OpenEdges = false,
        };
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++) { t.Heights[i + j * n] = 0.1 * t.CellX(i) + 0.3 * Math.Sin(i * 1.7 + j); if (j == 9 && i is > 7 and < 11) t.Soil[i + j * n] = 1; }
        return t;
    }

    private static bool Place(WorkedGround w, double x, double z, WorkedGround.Scooped s)
    {
        var spots = new List<(double X, double Z, double H)>();
        for (double dx = -4; dx <= 4; dx += 0.5)
            for (double dz = -4; dz <= 4; dz += 0.5)
                if (w.Inside(x + dx, z + dz, WorkedGround.Margin) && w.LandingSurface(x + dx, z + dz) is { } h) spots.Add((x + dx, z + dz, h));
        foreach (var (px, pz, _) in spots.OrderBy(p => p.H))
            if (w.Pour(px, pz, s.Volume, s.Soil, s.Ceiling) is not null) return true;
        return false;
    }

    /// <summary>Round r of the dig: bucketfuls scraped along a line and tipped nearby (two places 40 m apart, so two patches merge), settled after each.</summary>
    private static void Dig(Terrain t, int from, int to)
    {
        for (int r = from; r < to; r++)
        {
            double x = -12 + (r % 7) * 0.9, z = 4 + (r / 7) * 0.7 - (r % 2) * 9;
            if (r % 5 == 4) x += 40;   // the second site, in the next patch over
            if (t.WorkAt(x, z) is not { } w) continue;
            if (w.Scoop(x, z, 0.2) is { } s && !Place(w, x, z + 2, s)) Place(w, x, z - 2, s);
            w.Settle(G);
        }
    }

    private static void AssertSame(Terrain a, Terrain b, double tol)
    {
        Assert.Equal(a.Worked.Count, b.Worked.Count);
        double worst = 0;
        foreach (var (p, q) in a.Worked.OrderBy(w => w.MinX).ThenBy(w => w.MinZ).Zip(b.Worked.OrderBy(w => w.MinX).ThenBy(w => w.MinZ)))
        {
            Assert.Equal((p.Bi0, p.Bj0, p.Bi1, p.Bj1), (q.Bi0, q.Bj0, q.Bi1, q.Bj1));
            Assert.Equal(p.Fine.Soil, q.Fine.Soil);
            Assert.Equal(p.Fine.Loose, q.Fine.Loose);
            Assert.Equal(p.Dug, q.Dug, tol);
            Assert.Equal(p.Dumped, q.Dumped, tol);
            for (int k = 0; k < p.Fine.Heights.Length; k++)
                foreach (var (x, y) in new[] { (p.Fine.Heights[k], q.Fine.Heights[k]), (p.Fine.Ceiling![k], q.Fine.Ceiling![k]), (p.Original[k], q.Original[k]), (p.Floor[k], q.Floor[k]) })
                {
                    if (double.IsNegativeInfinity(x)) { Assert.Equal(x, y); continue; }
                    worst = Math.Max(worst, Math.Abs(x - y));
                }
        }
        Assert.True(worst <= tol, $"worst node difference {worst:g3}");
    }

    private static Terrain RoundTrip(Terrain t)
    {
        var text = new WorldSave { Kind = "world", Name = "m", Machines = [], Worked = t.SaveWorked() }.ToText();
        var fresh = Map();
        fresh.LoadWorked(WorldSave.Parse(text).Worked!);
        return fresh;
    }

    [Fact]
    public void ASaveMadeMidDigTracesTheSameAsTheRunThatKeptGoing()
    {
        var kept = Map();
        Dig(kept, 0, 40);
        Assert.True(kept.Worked.Count >= 1);
        Assert.Contains(kept.Worked, w => w.Fine.Loose.Any(l => l));
        Assert.Contains(kept.Worked, w => w.Floor.Any(f => !double.IsNegativeInfinity(f)));   // bedrock under a patch
        var loaded = RoundTrip(kept);
        AssertSame(kept, loaded, 0);                       // exactly what was saved
        Dig(kept, 40, 100);
        Dig(loaded, 40, 100);
        AssertSame(kept, loaded, 1e-9);                    // and the same ground after 60 more rounds
        Assert.Equal(kept.Worked.Sum(w => w.Net()), loaded.Worked.Sum(w => w.Net()), 9);
        Assert.True(kept.Worked.Sum(w => w.Dug) > 1);
    }

    [Fact]
    public void TwoPatchesThatMergedAreSavedAsOneAndALoadMakesTheViewRebuild()
    {
        var t = Map();
        t.WorkAt(-30, 0)!.Scoop(-30, 0, 0.2);
        t.WorkAt(-5, 0)!.Scoop(-5, 0, 0.2);
        Assert.Single(t.Worked);                            // merged
        int seen = t.WorkedPatches;
        var fresh = Map();
        Assert.Equal(0, fresh.WorkedPatches);
        fresh.LoadWorked(t.SaveWorked()!);
        Assert.NotEqual(0, fresh.WorkedPatches);            // a view watching it sees the change
        AssertSame(t, fresh, 0);
        Assert.Equal(t.HeightAt(-30, 0), fresh.HeightAt(-30, 0), 12);   // HeightAt reads the patch first
        Assert.NotNull(fresh.WorkAt(-5, 0));
        Assert.Single(fresh.Worked);                // the patch it found is the loaded one, not a new one
        Assert.True(seen > 0);
    }

    [Fact]
    public void SpoilFromAMoundStillCannotBeTippedAboveWhereItWasDugAfterALoad()
    {
        var t = Map();
        var w = t.WorkAt(0, 0)!;
        double dugFrom = w.HeightAt(0, 0);
        var s = w.Scoop(0, 0, 0.2)!.Value;
        Assert.True(Place(w, 0, 0, s));                                     // a mound, on the lowest ground the rule allows
        w.Settle(G);
        int top = Enumerable.Range(0, w.Fine.Heights.Length).MaxBy(k => w.Fine.Heights[k] - w.Original[k]);
        double mx = w.NodeX(top % w.Nx), mz = w.NodeZ(top / w.Nx);
        var loaded = RoundTrip(t).WorkAt(0, 0)!;
        for (int k = 0; k < w.Fine.Heights.Length; k++) Assert.Equal(w.Fine.Ceiling![k], loaded.Fine.Ceiling![k]);
        // a load from the loaded mound's top keeps the lower level, so ground higher than where the soil began is refused (and unchanged)
        var again = loaded.Scoop(mx, mz, 0.2)!.Value;
        Assert.True(again.Ceiling <= dugFrom + 0.15, $"carry ceiling {again.Ceiling:0.000} against the dug-from level {dugFrom:0.000}");
        double before = loaded.Fine.Heights.Sum();
        double uphill = mx + 8;                                             // the slope rises 0.1 m a metre: 0.8 m above
        Assert.True(loaded.HeightAt(uphill, mz) > again.Ceiling + 0.3);
        Assert.Null(loaded.Pour(uphill, mz, again.Volume, again.Soil, again.Ceiling));
        Assert.Equal(before, loaded.Fine.Heights.Sum(), 12);
    }

    [Fact]
    public void AnOldSaveWithNoPatchesLoadsAsBeforeAndAWorldWithNoDiggingWritesNone()
    {
        Assert.Null(Map().SaveWorked());
        var save = new WorldSave { Kind = "world", Name = "m", Machines = [] };
        Assert.DoesNotContain("worked", save.ToText());
        Assert.Null(WorldSave.Parse(save.ToText()).Worked);
        Assert.Throws<FormatException>(() => Map().LoadWorked(WorldSave.Parse("(world-save 1 (kind world) (name m) (worked 9))").Worked!));
    }

    [Fact]
    public void SavedPatchesAreNotAlsoWalkedFieldByFieldIntoTheGroundState()
    {
        var t = Map();
        Dig(t, 0, 5);
        var text = RuntimeState.CaptureGround(new WorldGround(t)).ToString()!;
        Assert.DoesNotContain("Worked", text);
    }
}
