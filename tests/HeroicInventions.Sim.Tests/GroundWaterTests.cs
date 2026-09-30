using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #37: water loose on open ground, the 2-D shallow-water equations
/// on a map's cells. Each prediction was worked out before the run.
/// </summary>
public class GroundWaterTests
{
    private const double G = 9.81;

    private static Terrain Map(int nx, int nz, double cell, Func<double, double, double> height,
                               double infiltration = 0, bool open = false, double x0 = 0, double z0 = 0, IReadOnlyList<MapSource>? sources = null)
    {
        var heights = new double[nx * nz];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
                heights[i + j * nx] = height(x0 + (i + 0.5) * cell, z0 + (j + 0.5) * cell);
        return new Terrain
        {
            Name = "test", X0 = x0, Z0 = z0, Cell = cell, Nx = nx, Nz = nz, Heights = heights,
            Soil = new int[nx * nz], Soils = [("sand", infiltration)], OpenEdges = open, Sources = sources ?? [],
        };
    }

    private static void AssertLedger(ShallowWater2D w) =>
        Assert.Equal(w.Poured, w.Volume + w.Infiltrated + w.Leaked + w.Clipped, Math.Max(1e-9, w.Poured * 1e-9));

    /// <summary>
    /// The 2-D solver down a strip one cell wide is the channel's solver: the
    /// same dam break as ChannelWaveTests (h0 1 m on a flat frictionless bed,
    /// 25 cm cells, 5 s) must give the same numbers: 4/9 = 0.444 m at the dam,
    /// and the 5 cm depth 20.9 m past it, ±0.5 m.
    /// </summary>
    [Fact]
    public void AStripOfGroundBreaksLikeTheChannel()
    {
        var w = new ShallowWater2D(Map(400, 1, 0.25, (_, _) => 0)) { Manning = 0 };
        w.Fill((x, _) => x < 50 ? 1.0 : 0);
        for (int i = 0; i < 1000; i++) w.Step(0.005);
        Assert.Equal(4.0 / 9, w.DepthAt(50.1, 0.1), 0.009);
        int body = Enumerable.Range(0, 400).Last(i => w.Depths[i] > 0.05);
        double ritter5cm = (2 * Math.Sqrt(G) - 3 * Math.Sqrt(G * 0.05)) * 5;
        Assert.InRange((body + 0.5) * 0.25 - 50, ritter5cm - 0.5, ritter5cm + 0.5);
        AssertLedger(w);
    }

    /// <summary>A pond lying in a hollow on a hillside (a bowl on a 1-in-10 slope) stays still: no current, no change in volume over a minute.</summary>
    [Fact]
    public void StillWaterInAHollowOnAHillsideStaysStill()
    {
        static double Ground(double x, double z) => 0.1 * x + 0.05 * ((x - 10) * (x - 10) + (z - 10) * (z - 10));
        var w = new ShallowWater2D(Map(20, 20, 1, Ground));
        // the pond's surface level where it just fits in the hollow: fill to 1.2 m where the ground is lower
        w.Fill((x, z) => Math.Max(0, 1.2 - Ground(x, z)));
        double before = w.Volume;
        Assert.True(before > 1, $"a pond of {before:F2} m³");
        for (int i = 0; i < 600; i++) w.Step(0.1);
        Assert.Equal(before, w.Volume, 9);
        for (int c = 0; c < 400; c++)
        {
            var (u, v) = w.VelocityAt(c);
            Assert.True(Math.Abs(u) + Math.Abs(v) < 1e-9, $"cell {c} moves at ({u}, {v})");
        }
    }

    /// <summary>
    /// A hollow fills to its level: 50 m³ poured at the bottom of a
    /// paraboloid z = 0.01 r² (1 m cells) settles flat at the level L where
    /// the map's own cells hold it, Σ max(0, L − z)·1 m² = 50 m³ (worked out
    /// below by bisection on the heights, before the run), to 1 mm. The
    /// smooth bowl's answer, √(2kV/π) = 0.564 m, is the sanity check (the
    /// cells' answer is within a few mm of it).
    /// </summary>
    [Fact]
    public void AHollowFillsToTheLevelItsShapeGives()
    {
        const double k = 0.01, v = 50;
        var map = Map(41, 41, 1, (x, z) => k * ((x - 20.5) * (x - 20.5) + (z - 20.5) * (z - 20.5)));
        double lo = 0, hi = 5;
        for (int it = 0; it < 100; it++)
        {
            double mid = (lo + hi) / 2;
            if (map.Heights.Sum(z => Math.Max(0, mid - z)) < v) lo = mid; else hi = mid;
        }
        double level = (lo + hi) / 2;
        Assert.Equal(Math.Sqrt(2 * k * v / Math.PI), level, 0.01);

        var w = new ShallowWater2D(map);
        for (int i = 0; i < 100; i++) { w.AddWater(20.5, 20.5, v / 100); w.Step(0.1); }   // poured over 10 s
        for (int i = 0; i < 4000; i++) w.Step(0.1);                                        // then left 400 s to settle
        for (int c = 0; c < map.Count; c++)
            if (w.Depths[c] > 0.01) Assert.Equal(level, w.SurfaceAt(c), 0.001);
        AssertLedger(w);
    }

    /// <summary>A puddle 5 cm deep on flat sand that soaks 1e-5 m/s: gone in h0 / rate = 5000 s, not before.</summary>
    [Fact]
    public void APuddleSoaksAwayAtItsSoilsRate()
    {
        var w = new ShallowWater2D(Map(10, 10, 1, (_, _) => 0, infiltration: 1e-5));
        w.Fill((_, _) => 0.05);
        for (int i = 0; i < 4990; i++) w.Step(1);
        Assert.True(w.Volume > 0, "still wet at 4990 s");
        for (int i = 0; i < 20; i++) w.Step(1);
        Assert.Equal(0, w.Volume, 9);
        Assert.Equal(5, w.Infiltrated, 9);
        AssertLedger(w);
    }

    /// <summary>A spring on a slope with open edges: every cubic metre it gives is on the ground, soaked in, or run off the map's edge.</summary>
    [Fact]
    public void EveryCubicMetreIsAccountedFor()
    {
        var w = new ShallowWater2D(Map(30, 20, 1, (x, z) => 0.05 * (30 - x) + 0.01 * Math.Abs(z - 10), infiltration: 2e-6, open: true,
                                       sources: [new MapSource("spring", 3, 10, 0.2)]));
        for (int i = 0; i < 1200; i++) { w.Step(0.1); AssertLedger(w); }
        Assert.Equal(0.2 * 120, w.Poured, 6);
        Assert.True(w.Leaked > 0, "some has run off the low edge");
    }

    /// <summary>The flood-plain map, as built from Racket, has the ground its source describes: the hill and the hollow at the cells' centres.</summary>
    [Fact]
    public void TheFloodPlainMapIsTheGroundItsSourceDescribes()
    {
        var map = Terrain.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "maps", "flood-plain.map")), "flood-plain.map");
        Assert.Equal((60, 40, 1.0, -10.0, -20.0), (map.Nx, map.Nz, map.Cell, map.X0, map.Z0));
        static double Hill(double x, double z) => 3 - 0.06 * (x + 10) + 0.004 * z * z;
        static double Ground(double x, double z)
        {
            double s = ((x - 30) * (x - 30) + z * z) / 81;
            return Hill(x, z) + (s < 1 ? -1.5 * (1 - s) : 0);
        }
        for (int j = 0; j < map.Nz; j += 7)
            for (int i = 0; i < map.Nx; i += 5)
                Assert.Equal(Ground(map.CellX(i), map.CellZ(j)), map.Heights[i + j * map.Nx], 4);
        Assert.Equal(("loam", 2e-6), map.Soils[0]);
        Assert.True(map.OpenEdges);
    }

    [Fact]
    public void AMapFileReadsBackAsWritten()
    {
        var map = Map(4, 3, 2, (x, z) => x + z / 10, infiltration: 1e-5, open: true, x0: -4, z0: 1,
                      sources: [new MapSource("spring", 0, 2, 0.01)]);
        var again = Terrain.Parse(map.Write());
        Assert.Equal((map.Nx, map.Nz, map.Cell, map.X0, map.Z0, map.OpenEdges), (again.Nx, again.Nz, again.Cell, again.X0, again.Z0, again.OpenEdges));
        Assert.Equal(map.Heights, again.Heights);
        Assert.Equal(map.Soils, again.Soils);
        Assert.Equal(map.Sources, again.Sources);
        Assert.Equal(map.Heights[1 + 2 * 4], again.HeightAt(-4 + 3, 1 + 5), 9);   // at a cell centre, its own height
    }
}
