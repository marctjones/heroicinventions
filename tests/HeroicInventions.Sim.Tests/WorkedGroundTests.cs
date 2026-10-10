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

    /// <summary>Tips a load on the lowest workable ground round a point, trying a grid of spots; false if there is none.</summary>
    private static bool Place(WorkedGround w, double x, double z, WorkedGround.Scooped s, double reach = 4)
    {
        var spots = new List<(double X, double Z, double H)>();
        for (double dx = -reach; dx <= reach; dx += 0.5)
            for (double dz = -reach; dz <= reach; dz += 0.5)
                if (w.Inside(x + dx, z + dz, WorkedGround.Margin) && w.LandingSurface(x + dx, z + dz) is { } h) spots.Add((x + dx, z + dz, h));
        foreach (var (px, pz, _) in spots.OrderBy(p => p.H))
            if (w.Pour(px, pz, s.Volume, s.Soil) is not null) return true;
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
        Assert.NotNull(w.Pour(2, 0, load.Volume, load.Soil));
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
        Assert.NotNull(w.Pour(0, 0, 0.1, 0));
        w.Settle(G);
        var back = w.Scoop(0, 0, 0.1)!.Value;
        Assert.InRange(back.Volume, 0.05, 0.1);
        Assert.All(w.Fine.Heights, h => Assert.True(h >= -1e-9, "never below the rock"));
    }

    /// <summary>A hole dug bucket by bucket goes down as far as the soil lets the arm take it (there is no carry ceiling, #72), its spoil on the rim.</summary>
    [Fact]
    public void AHoleADepthDeepDugByRepeatedBucketsHasItsSpoilOnTheRim()
    {
        // worked first: a loose crater to 1 m at repose 0.7 is a cone of radius 1/0.7 = 1.43 m, V = π r² h / 3 = 2.14 m³, 10.7 buckets
        var t = Map();
        var w = t.WorkAt(0, 0)!;
        int buckets = 0;
        var rng = new Random(2);
        double inBucket = 0;
        var rim = new List<(double X, double Z)>();
        foreach (double r in new[] { 3.5, 4.5, 5.5 })
            for (int a = 0; a < 16; a++) rim.Add((r * Math.Cos(a * Math.PI / 8), r * Math.Sin(a * Math.PI / 8)));
        while (-w.Fine.Heights.Min() < 1.0 && buckets < 30)
        {
            var s = w.Scoop(0, 0, 0.2)!.Value;
            buckets++;
            bool put = false;
            foreach (var (x, z) in rim)
                if (w.LandingSurface(x, z) is { } h && Math.Abs(h) < 0.02 && w.Pour(x, z, s.Volume, s.Soil) is not null) { put = true; break; }
            if (!put) inBucket += s.Volume;
            w.Settle(G);
        }
        Console.WriteLine($"HOLE buckets {buckets} depth {-w.Fine.Heights.Min():0.000} heap {w.Fine.Heights.Max():0.000}");
        Assert.Equal(0, inBucket);
        Assert.InRange(-w.Fine.Heights.Min(), 1.0, 1.3);
        Assert.InRange(buckets, 9, 14);                                    // worked: 10.7
        Assert.Equal(-inBucket, w.Net(), 8);
        Assert.True(w.Fine.Heights.Max() > 0.2, "the spoil stands on the rim");
        Assert.True(w.PotentialEnergy(G, -5) > 0, "a hole and its heap hold energy: the soil's own, which the backhoe paid for");
    }

    /// <summary>
    /// The rover moves soil wherever its arm reaches (#72, owner decision 2026-10-08, superseding #63's carry ceiling): a pile grows
    /// by dump after dump on itself, as high as its sides stand at repose. Worked first: twenty bucketfuls, 4 m³, tipped on one spot
    /// make a cone at repose 0.7 of V = π (h/0.7)² h / 3, h = (3 V 0.49 / π)^(1/3) = 1.23 m (lower on a square grid, whose
    /// diagonals round it off).
    /// </summary>
    [Fact]
    public void APileGrowsByDumpAfterDumpOnItselfAsHighAsItsSidesStand()
    {
        var t = Map();
        var w = t.WorkAt(0, 0)!;
        double top = 0;
        for (int n = 0; n < 20; n++)
        {
            var s = w.Scoop(-6 + (n % 5) * 0.9, -6 + (n / 5) * 0.9, 0.2)!.Value;
            Assert.NotNull(w.Pour(4, 4, s.Volume, s.Soil));   // on the pile's own top, every time
            w.Settle(G);
            double now = w.HeightAt(4, 4);
            Assert.True(now >= top - 1e-9, $"the pile grows: {now:0.000} after {top:0.000}");
            top = now;
        }
        Console.WriteLine($"PILE top {top:0.000} m");
        Assert.InRange(top, 0.9, 1.25);
        AssertStands(w);
        Assert.Equal(0, w.Net(), 9);
    }

    [Fact]
    public void SoilIsCarriedUpAHillBucketByBucket()
    {
        // a 6 degree hillside: a bucketful dug at the foot is tipped 1.5 m up it, 0.15 m higher, then dug from that heap and
        // tipped further up, round after round: what #63's carry ceiling forbade, and what the rover may now do
        var t = Map(0.1);
        var w = t.WorkAt(0, 0)!;
        double x = -5;
        var s = w.Scoop(x, 0, 0.2)!.Value;
        Assert.NotNull(w.Pour(x + 1.5, 0, s.Volume, s.Soil));
        w.Settle(G);
        for (int round = 0; round < 6; round++)
        {
            x += 1.5;
            var load = w.Scoop(x, 0, 0.2)!.Value;
            Assert.NotNull(w.Pour(x + 1.5, 0, load.Volume, load.Soil));
            w.Settle(G);
        }
        Assert.True(w.HeightAt(x + 1.5, 0) > t.CoarseSurfaceAt(x + 1.5, 0) + 0.1, "a heap stands 10 m up the hill from where the first soil was dug");
        Assert.Equal(0, w.Net(), 9);
    }

    [Fact]
    public void SoilFromADeepPitIsTippedOnTheRimOrAnywhereTheArmReaches()
    {
        var t = Map();
        var w = t.WorkAt(0, 0)!;
        for (int j = 0; j < w.Nz; j++)                                   // a pit 3 m deep and 6 m across, as if dug
            for (int i = 0; i < w.Nx; i++)
                if (Math.Sqrt(w.NodeX(i) * w.NodeX(i) + w.NodeZ(j) * w.NodeZ(j)) < 3) w.Fine.Heights[i + j * w.Nx] = -3;
        var s = w.Scoop(0, 0, 0.2)!.Value;
        Assert.InRange(s.Level, -3.4, -3);                                // the soil's middle, below the pit's floor
        Assert.NotNull(w.Pour(5, 0, s.Volume, s.Soil));                   // on the rim, 3 m up: no ceiling now
        var t2 = Map(0.2);
        var w2 = t2.WorkAt(0, 0)!;
        var s2 = w2.Scoop(-3, 0, 0.2)!.Value;
        double before = w2.Fine.Heights.Sum();
        Assert.NotNull(w2.Pour(3, 0, s2.Volume, s2.Soil));               // up a 11 degree slope, 1.2 m higher
        Assert.Equal(before + s2.Volume / (w2.Fine.Cell * w2.Fine.Cell), w2.Fine.Heights.Sum(), 9);
    }

    /// <summary>
    /// The soil's own energy ledger: every bucket's lift is what the backhoe paid for, and nothing else gives the soil energy. Each
    /// dig takes ρ g V (Level + 5) out of the patch's potential energy (Level the middle of the slabs it scraped, weighted; 5 m the
    /// datum's depth), each dump puts in ρ g V (s + r/2 + 5) (s the mean surface it lands on, r the rise), exactly; settling only
    /// lowers it. So the energy the patch gains over 200 random digs and dumps is the lifts paid for, less what the slides let go.
    /// </summary>
    [Fact]
    public void ThePatchsEnergyIsTheBucketsPaidLiftsLessWhatSlidesLetGo()
    {
        var t = Map(0.08);
        var w = t.WorkAt(0, 0)!;
        var rng = new Random(11);
        double paid = 0, released = 0, start = w.PotentialEnergy(G, -5);
        for (int n = 0; n < 200; n++)
        {
            double x = rng.NextDouble() * 12 - 6, z = rng.NextDouble() * 12 - 6;
            double e0 = w.PotentialEnergy(G, -5);
            var s = w.Scoop(x, z, 0.2);
            if (s is null) continue;
            double e1 = w.PotentialEnergy(G, -5);
            Assert.Equal(-1500 * G * s.Value.Volume * (s.Value.Level + 5), e1 - e0, 6);
            double px = x + rng.NextDouble() * 5 - 2.5, pz = z + rng.NextDouble() * 5 - 2.5;
            if (!w.Inside(px, pz, WorkedGround.Margin)) (px, pz) = (x, z + 1.5);
            double landing = w.LandingSurface(px, pz)!.Value;
            int nodes = 0;
            for (int k = 0; k < w.Fine.Heights.Length; k++)
            {
                double dx = w.NodeX(k % w.Nx) - px, dz = w.NodeZ(k / w.Nx) - pz;
                if (dx * dx + dz * dz <= WorkedGround.PourRadius * WorkedGround.PourRadius + 1e-9 && k % w.Nx is > 0 && k % w.Nx < w.Nx - 1 && k / w.Nx is > 0 && k / w.Nx < w.Nz - 1) nodes++;
            }
            Assert.NotNull(w.Pour(px, pz, s.Value.Volume, s.Value.Soil));
            double rise = s.Value.Volume / (nodes * w.Fine.Cell * w.Fine.Cell);
            double e2 = w.PotentialEnergy(G, -5);
            Assert.Equal(1500 * G * s.Value.Volume * (landing + rise / 2 + 5), e2 - e1, 6);
            paid += e2 - e0;                   // ρ g V (where it lies now − where it lay): the bucket's lift
            w.Settle(G);
            double e3 = w.PotentialEnergy(G, -5);
            Assert.True(e3 <= e2 + 1e-6, "slumping lowers the ground's energy, never raises it");
            released += e2 - e3;
        }
        Assert.Equal(paid - released, w.PotentialEnergy(G, -5) - start, 4);
        Assert.True(released > 0);
    }

    /// <summary>
    /// <see cref="WorkedGround.Try"/>: a change the check refuses leaves the ground as it was, to the bit (heights, loose flags,
    /// soils, totals and version, so a view has nothing to redraw); one it allows stays, and the check is shown every node the
    /// change raised, the slide's skirt included.
    /// </summary>
    [Fact]
    public void ATriedChangeThatIsRefusedLeavesTheGroundAsItWasToTheBit()
    {
        var t = Map(0, (5, 5));
        var w = t.WorkAt(0, 0)!;
        var s = w.Scoop(3, 3, 0.2)!.Value;
        w.Settle(G);
        var (h, l, soil, v, dug, dumped) = ((double[])w.Fine.Heights.Clone(), (bool[])w.Fine.Loose.Clone(), (int[])w.Fine.Soil.Clone(), w.Version, w.Dug, w.Dumped);
        IReadOnlyList<WorkedGround.Raised>? seen = null;
        Assert.False(w.Try(() => { w.Pour(-2, -2, s.Volume, s.Soil); w.Settle(G); }, r => { seen = r; return false; }));
        Assert.Equal(h, w.Fine.Heights);
        Assert.Equal(l, w.Fine.Loose);
        Assert.Equal(soil, w.Fine.Soil);   // spoil tipped on the rock had made it soil
        Assert.Equal((v, dug, dumped), (w.Version, w.Dug, w.Dumped));
        // the heap and its skirt: more nodes than the five the bucket tips on, each raised from where it was
        Assert.True(seen!.Count > 5, $"{seen.Count} nodes raised");
        Assert.All(seen, r => Assert.True(r.After > r.Before));
        Assert.Contains(seen, r => Math.Sqrt((r.X + 2) * (r.X + 2) + (r.Z + 2) * (r.Z + 2)) > WorkedGround.PourRadius + 0.1);
        // allowed, it stays; a change that raises nothing is never put to the check
        Assert.True(w.Try(() => { w.Pour(-2, -2, s.Volume, s.Soil); w.Settle(G); }, _ => true));
        Assert.True(w.Fine.Heights.Max() > 0.3);
        Assert.True(w.Try(() => w.Scoop(-6, -6, 0.2), _ => false));
    }

    /// <summary>
    /// The rubble of a slide is soil, whatever it came down on (#72, owner decision 2026-10-08: the rover digs wherever it goes).
    /// Victoria's weakened rim collapses as the opening loads and runs out over the bedrock apron below it, burying the battery
    /// bank's crate at (264, 143) under 4.74 m of sublimed regolith (the cell was −42.00 m, it is −37.26 after). The map still calls
    /// that cell bedrock (it keeps one soil, so the slide comes out as it always has, bit for bit: SlideFinishTests); the patch the
    /// rover lays over it reads the rubble as sublimed regolith down to the rock's old top. Before, every bucket there was refused as
    /// bedrock. (A patch's nodes start intact, as everywhere: 16 kPa holds a cut 22 m high on Mars, so the shaft stands.) Worked:
    /// the bucket takes the cell's centre down the full 4.74 m to the rock and no further, every bucket full until the last.
    /// </summary>
    [Fact]
    public void RubbleASlideLeftOnBedrockIsSoilTheRoverDigsDownToTheRock()
    {
        var t = Terrain.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "maps", "victoria.map")), "victoria.map");
        int cell = t.CellAt(264, 143)!.Value;
        double was = t.Heights[cell];
        Assert.Null(t.Covering(cell));
        t.Settle(3.71);
        Assert.Equal("bedrock", t.SoilOf(cell).Material);                       // the map's own soil, unchanged
        var covered = t.Covering(cell)!.Value;
        Assert.Equal(was, covered.RockTop, 12);
        Assert.Equal("sublimed-regolith", t.Soils[covered.Soil].Material);
        Assert.InRange(t.Heights[cell] - was, 4.5, 5.0);
        var w = t.WorkAt(264, 143)!;
        double x = t.CellX(cell % t.Nx), z = t.CellZ(cell / t.Nx);
        int node = (int)Math.Round((x - w.MinX) / WorkedGround.FineCell) + (int)Math.Round((z - w.MinZ) / WorkedGround.FineCell) * w.Nx;
        Assert.Equal(was, w.Floor[node], 12);                                   // the rock's top: never cut
        Assert.Equal("sublimed-regolith", w.Fine.SoilOf(node).Material);
        double top = w.HeightAt(x, z), last = top, dug = 0;
        int buckets = 0;
        while (buckets < 100 && w.Scoop(x, z, 0.2) is { } s)
        {
            buckets++;
            dug += s.Volume;
            w.Settle(3.71);
            Assert.True(w.HeightAt(x, z) <= last + 1e-9, "the cover only goes down as it is dug");
            last = w.HeightAt(x, z);
        }
        Console.WriteLine($"RUBBLE {buckets} buckets, {dug:F2} m3, the ground at the cell's centre from {top:F2} to {last:F2}, rock at {was:F2}");
        Assert.Equal(was, last, 6);                                              // down to the rock, and not into it
        Assert.InRange(buckets, 10, 20);
        Assert.All(Enumerable.Range(0, w.Fine.Heights.Length), k => Assert.True(w.Fine.Heights[k] >= w.Floor[k] - 1e-9));
    }

    /// <summary>
    /// <see cref="WorkedGround.Around"/> (#72, owner decision 2026-10-08: nothing is refused): soil goes round a body. A 0.5 m block's
    /// footprint stands for the body here (the game asks Jolt). Tipped beside it, so the heap's skirt would run under its edge, the
    /// heap piles against it and no node under it rises; tipped half over it, the soil lands on the open half; slid off a pit's wall
    /// toward it, the deposit stops at its base. Each time the volume is kept to 1e-9, and nothing is refused. Only a bucket right
    /// over it, every node of its footprint under the block, keeps its load.
    /// </summary>
    [Fact]
    public void SoilGoesRoundABodyAndNothingIsRefused()
    {
        static Func<WorkedGround.Raised, bool> Block(double bx, double bz) =>
            r => Math.Abs(r.X - bx) <= 0.25 + WorkedGround.FineCell / 2 && Math.Abs(r.Z - bz) <= 0.25 + WorkedGround.FineCell / 2;
        bool Under(WorkedGround w, int k, double bx, double bz) => Block(bx, bz)(new WorkedGround.Raised(k, w.NodeX(k % w.Nx), w.NodeZ(k / w.Nx), 0, 0));

        foreach (var (px, label) in new[] { (0.75, "beside"), (0.3, "half over") })
        {
            var w = Map().WorkAt(0, 0)!;
            var before = (double[])w.Fine.Heights.Clone();
            var s = w.Scoop(-4, 0, 0.2)!.Value;
            double? landed = null;
            for (int n = 0; n < 3; n++)
            {
                Assert.True(w.Around(() => { landed = w.Pour(px, 0, 0.2, s.Soil); w.Settle(G); }, Block(px - (px == 0.75 ? 0.75 : 0.3), 0)), label);
                Assert.NotNull(landed);
            }
            for (int k = 0; k < before.Length; k++)
                if (Under(w, k, 0, 0) && w.Fine.Heights[k] > before[k] + 1e-6) Assert.Fail($"{label}: a node under the block rose {w.Fine.Heights[k] - before[k]:0.000} m");
            Assert.Equal(0.4, w.Net(), 9);   // three tips of 0.2 less the one scoop
            Assert.True(w.Fine.Heights.Max() > 0.3, $"{label}: the soil heaped beside it");
        }
        // right over it: the bucket keeps its load
        var o = Map().WorkAt(0, 0)!;
        double? on = 0;
        Assert.True(o.Around(() => on = o.Pour(0, 0, 0.2, 0), Block(0, 0)));
        Assert.Null(on);
        Assert.Equal(0, o.Net(), 12);
        // a pit's wall slumping toward a block on its floor: the deposit stops at its base
        var p = Map().WorkAt(0, 0)!;
        p.Shape((x, z) => -Math.Clamp(2.5 - Math.Sqrt(x * x + z * z), 0, 1));
        var floor = (double[])p.Fine.Heights.Clone();
        Assert.True(p.Around(() => { p.Scoop(2.7, 0, 0.2); p.Settle(G); }, Block(1.2, 0)));
        bool slid = false;
        for (int k = 0; k < floor.Length; k++)
        {
            if (Under(p, k, 1.2, 0)) Assert.True(p.Fine.Heights[k] <= floor[k] + 1e-6, "the block's base holds the slide back");
            else if (p.Fine.Heights[k] > floor[k] + 1e-3) slid = true;
        }
        Assert.True(slid, "the walls did slump elsewhere");
        Assert.Equal(-p.Dug, p.Net(), 9);
    }

    [Fact]
    public void PatchesMergeAndKeepTheWorkAlreadyDone()
    {
        var t = Map();
        var a = t.WorkAt(-15, 0)!;
        var s = a.Scoop(-15, 0, 0.2)!.Value;
        a.Pour(-15, 1.5, s.Volume, s.Soil);
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
        w.Pour(0, 2, s.Volume, s.Soil);
        Assert.Equal(plain.Heights, worked.Heights);
        Assert.Equal(plain.Version, worked.Version);
        Assert.Equal(plain.HeightAt(-20, 20), worked.HeightAt(-20, 20));
        Assert.NotEqual(plain.HeightAt(0, 0), worked.HeightAt(0, 0));
    }

    /// <summary>
    /// #54, owner question 2026-10-10: a buried block is freed where it lies, not lifted. A 0.5 m block at (0.1, 0.07) under 0.1 m of
    /// flat regolith (lid at -0.1, base at -0.6): every node of every fine cell its footprint touches (x from -0.25 to 0.5, z from
    /// -0.25 to 0.5: 4 x 4 nodes, 1 m2 of node squares) goes to its base. They give 0.6 m over 1 m2, 0.6 m3: the block's own
    /// 0.125 m3 (the surface stood for it) and 0.475 m3 of soil: 0.025 m3 the crust on its lid, tipped loose round the pit's lip,
    /// and 0.45 m3 beside it that only the grid takes, spread as a skin under 3 cm deep; every bit of it put back.
    /// </summary>
    [Fact]
    public void ABlockFreedInPlaceStandsOnItsBaseAndItsCrustIsTippedRoundItVolumeKept()
    {
        var t = Map();
        var w = t.WorkAt(0, 0)!;
        double before = w.Net();
        var freed = w.FreeBlock(0.1, 0.07, 0.25, 0.25, -0.6, -0.1, G, _ => false)!.Value;
        Assert.Equal(16, freed.Nodes);
        Assert.Equal(0.6, freed.Given, 9);
        Assert.Equal(0.125, freed.Block, 9);
        Assert.Equal(0.475, freed.Soil, 9);
        Assert.Equal(0.025, freed.Crust, 9);   // 0.1 m over the 0.25 m2 lid; the other 0.45 m3 stood beside it, within a fine cell
        Assert.Equal(0.45, freed.Skin, 9);      // the grid's share: not loosened, an even skin no deeper than SkinDepth
        Assert.True(freed.SkinRise > 0 && freed.SkinRise <= Terrain.SkinDepth + 1e-12, $"skin {freed.SkinRise} m");
        Assert.Equal(freed.Soil, freed.Spoil, 12);
        Assert.Equal(before - freed.Block, w.Net(), 9);   // the ground lost the block's volume and no soil
        Assert.Equal(w.Dumped - w.Dug, w.Net(), 9);
        // the footprint is flat at the base, so the block stands on it and the surface crosses no part of it
        for (double x = -0.15; x <= 0.35; x += 0.05)
            for (double z = -0.18; z <= 0.32; z += 0.05)
                Assert.Equal(-0.6, w.HeightAt(x, z), 9);
        // the lip, a fine cell round the pit, is the ground as it was: no spoil on it to slide in
        Assert.Equal(0, w.HeightAt(-0.5, 0.07), 9);
        Assert.Equal(0, w.HeightAt(0.75, 0.25), 9);
        Assert.True(w.Fine.Heights.Max() > 0, "the spoil lies round it, heaped");
    }

    [Fact]
    public void SpoilThatWouldReachABodyGoesRoundIt()
    {
        var t = Map();
        var w = t.WorkAt(0, 0)!;
        // a body stands on the ground to the pit's east (x 0.9 to 1.6): no node under it may rise
        bool Under(WorkedGround.Raised r) => r.X >= 0.75 && r.X <= 1.75 && Math.Abs(r.Z) <= 0.5;
        var freed = w.FreeBlock(0, 0, 0.25, 0.25, -0.6, -0.1, G, Under)!.Value;
        Assert.Equal(freed.Soil, freed.Spoil, 12);
        Assert.Equal(0, w.HeightAt(1.25, 0), 12);
    }
}
