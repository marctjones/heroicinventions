using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #63: the rover's earthworks on a fine patch of ground laid over the map's 5 m cells. Predictions worked out first:
/// a bucketful is 0.2 m³; on 0.25 m cells it scrapes a disc of radius 0.45 m (the nodes within it) a third of a metre deep,
/// where one 5 m cell would drop 8 mm; tipped, it piles to a cone of repose, h = 0.7 r, V = π r² h / 3, so r = 0.65 m and
/// h = 0.455 m (octagonal contours on a square grid lower it a little).
/// </summary>
public class WorkedGroundTests
{
    private const double G = 3.71;
    private static readonly SoilSpec Regolith = new("regolith", 0, Cohesion: 0, Friction: 0.7, Density: 1500);
    private static readonly SoilSpec Rock = new("bedrock", 0, Cohesion: 5e7, Friction: 0.6, Density: 2700);

    /// <summary>A 60 m square map of 5 m cells, regolith, its surface z = <paramref name="slope"/> · x.</summary>
    private static Terrain Map(double slope = 0, params (int I, int J)[] bedrock)
    {
        const int n = 12;
        var t = new Terrain
        {
            Name = "m", X0 = -30, Z0 = -30, Cell = 5, Nx = n, Nz = n, Heights = new double[n * n], Soil = new int[n * n],
            Soils = [Regolith, Rock], OpenEdges = false,
        };
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++) t.Heights[i + j * n] = slope * t.CellX(i);
        foreach (var (i, j) in bedrock) t.Soil[i + j * n] = 1;
        return t;
    }

    /// <summary>Tips a load on the lowest ground the rule lets it onto, trying a grid of spots round a point; false if nowhere will take it.</summary>
    private static bool Place(WorkedGround w, double x, double z, WorkedGround.Scooped s, double reach = 4)
    {
        var spots = new List<(double X, double Z, double H)>();
        for (double dx = -reach; dx <= reach; dx += 0.5)
            for (double dz = -reach; dz <= reach; dz += 0.5)
                if (w.Inside(x + dx, z + dz, WorkedGround.Margin) && w.LandingSurface(x + dx, z + dz) is { } h) spots.Add((x + dx, z + dz, h));
        foreach (var (px, pz, _) in spots.OrderBy(p => p.H))
            if (w.Pour(px, pz, s.Volume, s.Soil, s.Ceiling) is not null) return true;
        return false;
    }

    [Fact]
    public void ANewPatchIsTheGroundThatWasThereToTheLastBit()
    {
        var t = Map();
        var rng = new Random(3);
        for (int k = 0; k < t.Heights.Length; k++) t.Heights[k] = rng.NextDouble() * 3;   // a lumpy map: bilinear and the drawn triangles differ
        var w = t.WorkAt(0, 0)!;
        for (double x = w.MinX; x <= w.MaxX; x += 0.37)
            for (double z = w.MinZ; z <= w.MaxZ; z += 0.41)
            {
                // on a node of the patch it is the map's drawn surface exactly; between nodes inside one coarse triangle, the same plane
                int i = (int)Math.Round((x - w.MinX) / WorkedGround.FineCell), j = (int)Math.Round((z - w.MinZ) / WorkedGround.FineCell);
                double nx = w.NodeX(i), nz = w.NodeZ(j);
                Assert.Equal(t.CoarseSurfaceAt(nx, nz), w.Fine.Heights[i + j * w.Nx], 12);
            }
        Assert.Equal(0, w.Net());
    }

    [Fact]
    public void ABucketOnFineCellsMakesATrenchAndAHeapThatCanBeSeen()
    {
        var t = Map();
        var w = t.WorkAt(0, 0)!;
        var load = w.Scoop(0, 0, 0.2)!.Value;
        Assert.Equal(0.2, load.Volume, 9);
        double deepest = -w.Fine.Heights.Min();
        Assert.InRange(deepest, 0.25, 0.45);                // 0.2 m³ over the ten or so nodes of a 0.45 m disc: about a third of a metre
        Assert.True(deepest > 30 * (0.2 / 25), "the same bucket from one 5 m cell is 8 mm: this is thirty-odd times that");
        // tipped two metres off, it piles and slumps to repose
        Assert.NotNull(w.Pour(2, 0, load.Volume, load.Soil, load.Ceiling));
        w.Settle(G);
        Assert.InRange(w.Fine.Heights.Max(), 0.3, 0.46);   // the cone's 0.455 m, lower on a square grid whose diagonals hold it round
        AssertStands(w);
        Assert.Equal(0, w.Net(), 9);
    }

    private static void AssertStands(WorkedGround w)
    {
        var f = w.Fine;
        for (int j = 1; j < f.Nz - 1; j++)
            for (int i = 1; i < f.Nx - 1; i++)
                foreach (var (di, dj, run) in new[] { (1, 0, 1.0), (0, 1, 1.0), (1, 1, Math.Sqrt(2)), (1, -1, Math.Sqrt(2)) })
                {
                    int a = i + j * f.Nx, b = i + di + (j + dj) * f.Nx;
                    if (i + di >= f.Nx - 1 || j + dj < 1 || j + dj >= f.Nz - 1) continue;
                    Assert.True(Math.Abs(f.Heights[a] - f.Heights[b]) <= f.SoilOf(a).Friction * run * f.Cell + 1e-6, "no step steeper than repose");
                }
    }

    [Fact]
    public void ManyBucketsConserveVolumeExactlyThroughDigsDumpsAndSlides()
    {
        var t = Map(0.05);
        var w = t.WorkAt(1, 1)!;
        var rng = new Random(5);
        double inBucket = 0;
        for (int n = 0; n < 150; n++)
        {
            double x = rng.NextDouble() * 10 - 5, z = rng.NextDouble() * 10 - 5;
            var s = w.Scoop(x, z, 0.2)!.Value;
            Assert.Equal(-w.Dug + w.Dumped, w.Net(), 9);
            if (!Place(w, x, z, s)) inBucket += s.Volume;    // nowhere low enough to tip it: it stays in the bucket
            w.Settle(G);
            Assert.Equal(-w.Dug + w.Dumped, w.Net(), 9);
        }
        Assert.Equal(-inBucket, w.Net(), 8);   // everything dug is either dumped or still in the bucket
        Assert.Equal(w.Dug - inBucket, w.Dumped, 9);
    }

    [Fact]
    public void BedrockIsNeverCutAndSpoilOnItIsSoilThatCanBeTakenBack()
    {
        // the cell holding (0,0) and its neighbours are bedrock: i, j of cells at x in [-2.5, 2.5)
        var t = Map(0, (5, 5), (6, 5), (5, 6), (6, 6));
        var w = t.WorkAt(0, 0)!;
        Assert.Null(w.Scoop(0, 0, 0.2));
        Assert.All(w.Fine.Heights, h => Assert.Equal(0, h));
        // soil tipped on it is soil (a bucketful of it can be dug up again), but the rock under it stays
        Assert.NotNull(w.Pour(0, 0, 0.1, 0, 10));
        w.Settle(G);
        var back = w.Scoop(0, 0, 0.1)!.Value;
        Assert.InRange(back.Volume, 0.05, 0.1);
        Assert.All(w.Fine.Heights, h => Assert.True(h >= -1e-9, "never below the rock"));
    }

    [Fact]
    public void AMoundRemembersWhereItsSoilWasDugSoItCannotBeWalkedUpAHill()
    {
        // a 6 degree hillside, rising with x; bucketfuls dug at the foot and dumped as far up as the rule allows, then dug from
        // the mound they made and dumped up again, round after round (what the 5 cm ground-level rule would let climb)
        var t = Map(0.1);
        var w = t.WorkAt(0, 0)!;
        double startX = -5;
        double ceiling = t.CoarseSurfaceAt(startX, 0);
        var first = w.Scoop(startX, 0, 0.2)!.Value;
        Assert.True(first.Ceiling <= ceiling + 1e-9);
        // dumping 1.5 m up the hill: the ground there is 0.15 m higher than where it was dug: refused
        Assert.Null(w.Pour(startX + 1.5, 0, first.Volume, first.Soil, first.Ceiling));
        // dumped beside the pit at the same level, it makes a mound
        Assert.NotNull(w.Pour(startX, 1.5, first.Volume, first.Soil, first.Ceiling));
        w.Settle(G);
        double mound = w.HeightAt(startX, 1.5);
        Assert.True(mound > t.CoarseSurfaceAt(startX, 1.5) + 0.1, "a mound stands higher than the hill under it");
        // dug from the mound's top, the load is still not allowed higher than the level it first came from
        double bestX = startX;
        for (int round = 0; round < 60; round++)
        {
            var s = w.Scoop(bestX, 1.5, 0.2);
            if (s is null) break;
            // the highest ground up the hill, within 2.5 m, that the rule lets it land on
            double? landed = null;
            for (double dx = 2.5; dx > 0 && landed is null; dx -= 0.25)
                if (w.Pour(bestX + dx, 1.5, s.Value.Volume, s.Value.Soil, s.Value.Ceiling) is { } l) { landed = l; bestX += dx; }
            if (landed is null && !Place(w, bestX, 1.5, s.Value)) break;   // it stays in the bucket
            w.Settle(G);
        }
        Assert.True(bestX < startX + 1.0, $"the mound walked {bestX - startX:0.0} m up the hill");
        // the mound's soil may be carried to the level it was dug from, give or take what a footprint that crept onto higher native
        // ground brought in (a bucket takes the mean of what it scraped: the credit is soil that really did come down from there)
        double over = 0;
        for (int k = 0; k < w.Fine.Heights.Length; k++)
            if (w.Fine.Loose[k]) over = Math.Max(over, Math.Min(w.Fine.Ceiling![k], w.Fine.Heights[k]) - ceiling);   // ground never covered is its own surface
        Assert.True(over < 0.1, $"spoil's ceiling crept {over:0.000} m above the level it was dug from");
    }

    [Fact]
    public void WithoutTheMoundRememberingItsOriginTheSameWalkClimbsTheHill()
    {
        // the rule as the ground's height at the teeth alone would give it: dig the mound's top (0.4 m above the hill), dump
        // up the hill where the hill is only a little above where the mound stands. This is the walk the memory stops.
        var t = Map(0.1);
        var w = t.WorkAt(0, 0)!;
        double x = -5;
        var s = w.Scoop(x, 0, 0.2)!.Value;
        Assert.NotNull(w.Pour(x, 1.5, s.Volume, s.Soil, s.Ceiling));
        w.Settle(G);
        double mx = x;
        for (int round = 0; round < 12; round++)
        {
            double top = w.HeightAt(mx, 1.5);                          // the surface at the teeth: the mound's top
            var load = w.Scoop(mx, 1.5, 0.2)!.Value;
            bool moved = false;
            for (double dx = 2.5; dx > 0 && !moved; dx -= 0.25)
                moved = w.Pour(mx + dx, 1.5, load.Volume, load.Soil, top) is not null;   // ceiling: the surface it was dug from
            if (moved) mx += 0; else break;
            w.Settle(G);
            // follow the mound up the hill
            double best = mx; double bestH = double.NegativeInfinity;
            for (double px = mx; px <= mx + 3; px += 0.25) if (w.HeightAt(px, 1.5) - t.CoarseSurfaceAt(px, 1.5) > 0.2 && px > best) { best = px; bestH = w.HeightAt(px, 1.5); }
            mx = best;
        }
        Assert.True(mx > x + 3, $"the rule without memory lets the mound walk {mx - x:0.0} m up the hill");
    }

    [Fact]
    public void EveryDumpLandsNoHigherThanTheLoadWasDugFromAndSettlingOnlyLowersEnergy()
    {
        var t = Map(0.08);
        var w = t.WorkAt(0, 0)!;
        var rng = new Random(11);
        double lift = 0;    // J: ρ g V (landing level - carried-to level), summed over every dump that was allowed
        for (int n = 0; n < 200; n++)
        {
            double x = rng.NextDouble() * 12 - 6, z = rng.NextDouble() * 12 - 6;
            var s = w.Scoop(x, z, 0.2);
            if (s is null) continue;
            double px = x + rng.NextDouble() * 5 - 2.5, pz = z + rng.NextDouble() * 5 - 2.5;
            if (w.Inside(px, pz, WorkedGround.Margin) && w.LandingSurface(px, pz) is { } landing)
            {
                if (w.Pour(px, pz, s.Value.Volume, s.Value.Soil, s.Value.Ceiling) is not null)
                    lift += 1500 * G * s.Value.Volume * (landing - s.Value.Ceiling);
                    else Place(w, x, z, s.Value);
            }
            else Place(w, x, z, s.Value);
            double before = w.PotentialEnergy(G, -5);
            w.Settle(G);
            Assert.True(w.PotentialEnergy(G, -5) <= before + 1e-6, "slumping lowers the ground's energy, never raises it");
        }
        Assert.True(lift <= 1e-6, $"no allowed dump raised its load: {lift} J");
    }

    [Fact]
    public void ADumpAboveWhereItWasDugIsRefusedAndChangesNothing()
    {
        var t = Map(0.2);
        var w = t.WorkAt(0, 0)!;
        var s = w.Scoop(-3, 0, 0.2)!.Value;
        var before = (double[])w.Fine.Heights.Clone();
        Assert.Null(w.Pour(3, 0, s.Volume, s.Soil, s.Ceiling));       // 1.2 m higher
        Assert.Equal(before, w.Fine.Heights);
        Assert.Null(w.Pour(-2, 0, s.Volume, s.Soil, s.Ceiling));      // 0.2 m higher
        var flat = Map(0.05).WorkAt(0, 0)!;
        var f = flat.Scoop(-3, 0, 0.2)!.Value;
        Assert.Null(flat.Pour(-2, 0, f.Volume, f.Soil, f.Ceiling));   // 5 cm higher: there is no 5 cm allowance
        Assert.NotNull(w.Pour(-3, 0.5, s.Volume, s.Soil, s.Ceiling)); // along the contour: level
    }

    [Fact]
    public void PatchesMergeAndKeepTheWorkAlreadyDone()
    {
        var t = Map();
        var a = t.WorkAt(-15, 0)!;
        var s = a.Scoop(-15, 0, 0.2)!.Value;
        a.Pour(-15, 1.5, s.Volume, s.Soil, s.Ceiling);
        a.Settle(G);
        double peak = a.Fine.Heights.Max();
        Assert.Single(t.Worked);
        // a point near the patch's edge: the patch grows to take it in, and the work is where it was
        var b = t.WorkAt(a.MaxX - 1, 0)!;
        Assert.Single(t.Worked);
        Assert.True(b.Nx > a.Nx);
        Assert.Equal(peak, b.Fine.Heights.Max(), 12);
        Assert.Equal(0, b.Net(), 9);
        Assert.Equal(a.Dug, b.Dug, 12);
        Assert.Equal(a.Dumped, b.Dumped, 12);
        Assert.Equal(b.HeightAt(-15, 1.5), t.HeightAt(-15, 1.5), 12);
    }

    [Fact]
    public void TheMapAboveWorkedGroundIsUntouchedAndTheMapWithNoneIsAsItWas()
    {
        var plain = Map(0.1);
        var worked = Map(0.1);
        var w = worked.WorkAt(0, 0)!;
        var s = w.Scoop(0, 0, 0.2)!.Value;
        w.Pour(0, 2, s.Volume, s.Soil, s.Ceiling);
        Assert.Equal(plain.Heights, worked.Heights);
        Assert.Equal(plain.Version, worked.Version);
        Assert.Equal(plain.HeightAt(-20, 20), worked.HeightAt(-20, 20));
        Assert.NotEqual(plain.HeightAt(0, 0), worked.HeightAt(0, 0));
    }
}
