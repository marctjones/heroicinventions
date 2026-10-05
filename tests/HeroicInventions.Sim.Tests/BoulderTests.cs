using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #88: boulders from terrain collapse. Predictions worked out first, in racket/maps/talus.rkt.</summary>
public class BoulderTests
{
    private static string Dir(string d, string f) => Path.Combine(AppContext.BaseDirectory, d, f);
    private static Terrain Talus() => Terrain.Parse(File.ReadAllText(Dir("maps", "talus.map")), "talus.map");

    /// <summary>
    /// The talus map: a 3 m regolith cliff (c 4 kPa, tan φ 0.70, 1500 kg/m³), which stands to 2.088 m, 8 m along.
    /// Its crest column fails and comes down, 1.725 m × 0.5 m × 8 m = 6.90 m³ spread at repose; 8 % of that in 0.5 m
    /// cubes is floor(4.42) = 4 boulders, 0.5 m³, which come out of the debris, so the rest settles a little further:
    /// V = (3 − 1.244) × 0.5 × 8 = 7.03 m³ (to 3 %: the settling stops each step a little short of repose), still 4
    /// boulders (4 for any V from 6.25 to 7.80). What the ground lost is exactly what the debris gained and the
    /// boulders hold; the ground is 0.5 m³ less.
    /// </summary>
    [Fact]
    public void ACollapseOfVLeavesDebrisAndBouldersMakingUpV()
    {
        var g = Talus();
        Assert.Equal(2.088, g.Soils[0].CriticalHeight(9.81), 3);
        var before = (double[])g.Heights.Clone();
        double area = g.Cell * g.Cell, start = g.Heights.Sum() * area;
        var (failures, _) = g.Settle(9.81);

        Assert.Equal(16, failures);   // the crest column, all 16 cells of it along the cliff, and nothing more
        double lost = Enumerable.Range(0, g.Count).Sum(c => Math.Max(0, before[c] - g.Heights[c])) * area;
        double debris = Enumerable.Range(0, g.Count).Sum(c => Math.Max(0, g.Heights[c] - before[c])) * area;
        Assert.Equal(lost, g.Collapsed, 9);
        Assert.Equal(7.03, g.Collapsed, 7.03 * 0.03);
        Assert.Equal(4, g.Boulders.Count);
        Assert.Equal((int)Math.Floor(0.08 * g.Collapsed / 0.125), g.Boulders.Count);
        Assert.Equal(0.5, g.BoulderVolume, 12);
        Assert.Equal(0.5, g.Boulders.Sum(b => b.Volume), 12);
        Assert.Equal(g.Collapsed, debris + g.BoulderVolume, 9);      // V = what lies as debris + what came down as rock
        Assert.Equal(start - 0.5, g.Heights.Sum() * area, 9);      // the ground is short by the boulders, exactly
        // the face left behind stands
        for (int j = 0; j < g.Nz; j++) Assert.True(3 - g.Heights[9 + j * g.Nx] < 2.088, "the new face is under its critical height");
    }

    /// <summary>
    /// The boulders are laid on the debris where it is thickest, 0.75 m (1.5 sides) apart and clear of the map's
    /// edges, each resting on the slope (its centre a quarter-metre out along the ground's normal), on ground steeper
    /// than granite's friction angle on the ground, atan √(0.6 × 0.6) = 31.0°: so they will slide.
    /// </summary>
    [Fact]
    public void BouldersAreLaidOnTheDebrisWhereTheyWillSlide()
    {
        var g = Talus();
        g.Settle(9.81);
        foreach (var b in g.Boulders)
        {
            Assert.Equal("granite", b.Material);
            Assert.Equal(0.5, b.Size);
            Assert.True(g.SlopeAt(b.X, b.Z) > 31.0, $"{b.Id} laid on {g.SlopeAt(b.X, b.Z):F1}°, steeper than 31°");
            Assert.InRange(b.Z, g.Z0 + 1.5, g.Z0 + g.Depth - 1.5);
            Assert.Equal(1, b.Qx * b.Qx + b.Qy * b.Qy + b.Qz * b.Qz + b.Qw * b.Qw, 9);
            // the turn takes the cube's up to the ground's normal, and its bottom face's centre lies (a centimetre clear) on the ground
            double upX = 2 * (b.Qx * b.Qy - b.Qw * b.Qz), upY = 1 - 2 * (b.Qx * b.Qx + b.Qz * b.Qz), upZ = 2 * (b.Qy * b.Qz + b.Qw * b.Qx);
            double bx = b.X - upX * 0.26, by = b.Y - upY * 0.26, bz = b.Z - upZ * 0.26;
            Assert.Equal(g.HeightAt(bx, bz), by, 0.02);
            var (nx, ny, nz) = g.NormalAt(bx, bz);
            Assert.True(upX * nx + upY * ny + upZ * nz > Math.Cos(3 * Math.PI / 180), $"{b.Id} sits square on the slope");
            foreach (var o in g.Boulders.Where(o => o != b))
                Assert.True(Math.Sqrt((b.X - o.X) * (b.X - o.X) + (b.Z - o.Z) * (b.Z - o.Z)) >= 0.75 - 1e-9, $"{b.Id} clear of {o.Id}");
        }
    }

    /// <summary>A save keeps every boulder: where it lies, how it is turned and how it moves, and goes on numbering after them.</summary>
    [Fact]
    public void ASaveKeepsTheBoulders()
    {
        var g = Talus();
        g.Settle(9.81);
        g.Boulders[1].Vx = 0.5; g.Boulders[1].Wz = -1.25; g.Boulders[1].Y = 0.77;
        var save = new WorldSave
        {
            Kind = "world", Name = "talus", Machines = [],
            Ground = RuntimeState.CaptureGround(new WorldGround(g)), Boulders = g.SaveBoulders(),
        };
        var read = WorldSave.Parse(save.ToText());
        var fresh = Talus();
        var ground = new WorldGround(fresh);
        Assert.Empty(RuntimeState.RestoreGround(ground, read.Ground!));
        fresh.LoadBoulders(read.Boulders!);
        ground.RetraceBoulders();
        Assert.Equal(g.Boulders.Count, fresh.Boulders.Count);
        foreach (var (a, b) in g.Boulders.Zip(fresh.Boulders))
        {
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Material, b.Material);
            foreach (var (x, y) in new[] { (a.X, b.X), (a.Y, b.Y), (a.Z, b.Z), (a.Qx, b.Qx), (a.Qz, b.Qz), (a.Qw, b.Qw), (a.Vx, b.Vx), (a.Wz, b.Wz) })
                Assert.Equal(x, y, 9);
        }
        Assert.Equal(g.Heights, fresh.Heights);
        Assert.Equal(g.Collapsed, fresh.Collapsed, 9);
        Assert.Equal(0.77, ground.FieldGetters["boulder-2.y"](), 9);
        Assert.Equal(4, ground.FieldGetters["map.boulders"]());
    }

    /// <summary>A map's rock is written back with its soil and read again; a soil without it makes no boulders.</summary>
    [Fact]
    public void TheRockIsPartOfTheSoil()
    {
        var g = Talus();
        Assert.Equal(new BoulderSpec(0.08, 0.5, "granite"), g.Soils[0].Boulders);
        Assert.Equal(g.Soils[0].Boulders, Terrain.Parse(g.Write()).Soils[0].Boulders);
        var cliff = Terrain.Parse(File.ReadAllText(Dir("maps", "cliff.map")), "cliff.map");
        Assert.Null(cliff.Soils[0].Boulders);
        cliff.Settle(9.81);
        Assert.Empty(cliff.Boulders);
        Assert.Equal(0, cliff.Collapsed);
    }
}
