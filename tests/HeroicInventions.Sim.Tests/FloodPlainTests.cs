using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #37's scene: the hillside pond standing on the flood-plain map,
/// let go by its water clock, flooding down into the hollow.
/// </summary>
public class FloodPlainTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static string Dir(string d, string f) => Path.Combine(AppContext.BaseDirectory, d, f);

    /// <summary>
    /// Worked out beforehand (hillside-pond.rkt): the clock trips the sluice at
    /// 10.5 s; the pond runs down to its race's lip, 10 cm, letting go 7.2 m³;
    /// every litre stays on the ledger (pond + clock + race + poured onto the
    /// ground = the 8 m³ there was; poured = standing + soaked in + run off);
    /// after 10 minutes at least 95% of what stands on the ground is in the
    /// hollow, lying flat, at the level the hollow's own cells give for that
    /// much water (Σ max(0, L − z)·1 m² = V, solved on the heights), to 5 mm,
    /// and below the hollow's low lip, so none has run off.
    /// </summary>
    [Fact]
    public void ThePondFloodsDownTheHillAndPoolsInTheHollow()
    {
        var world = WorldDef.Parse(File.ReadAllText(Dir("worlds", "flood-plain.world")), "flood-plain.world");
        var map = Terrain.Parse(File.ReadAllText(Dir("maps", world.Map + ".map")), world.Map!);
        var ground = new WorldGround(map);
        var place = Assert.Single(world.Placements);
        var def = MachineDef.Parse(File.ReadAllText(Dir("machines", place.Machine + ".machine")));
        var pond = new MachineRuntime(WorldDef.Placed(def, place, map), Materials);
        ground.Attach(place.Label, pond);
        Assert.Equal(map.HeightAt(-5, 0) + 0.5, pond.Tanks["pond"].BaseElevation, 9);   // standing on the hillside

        double firedAt = -1;
        for (int k = 1; k <= 60000; k++)
        {
            pond.Step(0.01);
            ground.Step(0.01);
            if (firedAt < 0 && pond.GetField("let-go", "fired") > 0) firedAt = k * 0.01;
            if (k % 500 == 0)
            {
                double total = pond.Tanks["pond"].WaterVolume + pond.Tanks["clock"].WaterVolume + pond.Channels["race"].Stored + ground.Water.Poured;
                Assert.Equal(8, total, 6);
                var w = ground.Water;
                Assert.True(Math.Abs(w.Poured - (w.Volume + w.Infiltrated + w.Leaked + w.Clipped)) < 1e-9 * Math.Max(1, w.Poured), "the ground's ledger");
            }
        }
        Assert.Equal(10.5, firedAt, 0.1);
        Assert.Equal(0.10, pond.Tanks["pond"].Level, 0.01);
        Assert.Equal(0, ground.Water.Leaked, 6);

        // the hollow: cells within 9 m of (30, 0)
        var hollow = Enumerable.Range(0, map.Count).Where(c =>
        {
            double dx = map.CellX(c % map.Nx) - 30, dz = map.CellZ(c / map.Nx);
            return dx * dx + dz * dz < 81;
        }).ToList();
        double inHollow = hollow.Sum(c => ground.Water.Depths[c]);
        Assert.True(inHollow >= 0.95 * ground.Water.Volume, $"{inHollow:F2} of {ground.Water.Volume:F2} m³ in the hollow");
        double lo = -2, hi = 2;
        for (int it = 0; it < 100; it++)
        {
            double mid = (lo + hi) / 2;
            if (hollow.Sum(c => Math.Max(0, mid - map.Heights[c])) < inHollow) lo = mid; else hi = mid;
        }
        double level = (lo + hi) / 2;
        foreach (int c in hollow.Where(c => ground.Water.Depths[c] > 0.01))
            Assert.Equal(level, ground.Water.SurfaceAt(c), 0.005);
        double lip = map.HeightAt(39, 0);
        Assert.True(level < lip, $"the pool at {level:F3} m stands below the hollow's lip at {lip:F3} m");
    }
}
