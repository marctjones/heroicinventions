using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #72's energy audit, the water's side (the bodies' side is game/scripts/DirtEnergyEval.cs, checked by
/// racket/heroic/tests/dirt-energy-test.rkt). The rover may tip soil anywhere (owner decision 2026-10-08), into water too, and soil
/// tipped into a pond does push its water up: that is displacement, and real. What it must not do is give the water more energy
/// than the soil itself gives up sinking into it. Poured in, each bit of soil meets the water at its surface as it then stands,
/// which rises from s to s + Δs as the soil goes in, and sinks from there to where it lies: the soil gives up at least
/// ρ_s g ∫ (s + Δs/2 − z) dV over the part of it under water, at ρ_s = 1500 kg/m³. Of that, the most the water can take is what
/// lifting the water it pushes aside to that surface takes, ρ_w g ∫ (s + Δs/2 − z) dV: two thirds of it.
/// No machine draws on the ground's water except a drain into a tank (#90), so the water's energy bounds what any machine can get
/// from dirt tipped into it.
/// </summary>
public class DirtEnergyTests
{
    private const double G = 3.71, RhoW = 1000, RhoS = 1500;

    private static Terrain Map()
    {
        const int n = 16;
        return new Terrain
        {
            Name = "m", X0 = -40, Z0 = -40, Cell = 5, Nx = n, Nz = n, Heights = new double[n * n], Soil = new int[n * n],
            // a little cohesion (2 kPa: an intact face stands 2.8 m) so the pit's cut walls stand and only the tipped soil goes in
            Soils = [new SoilSpec("regolith", 0, Cohesion: 2000, Friction: 0.7, Density: RhoS)], OpenEdges = false,
        };
    }

    /// <summary>The water's energy on the patch's fine grid (J): ρ A h [g (b + h/2) + ½ |u|²] over its cells.</summary>
    private static double WaterEnergy(PatchWater p)
    {
        double a = p.Bed.Cell * p.Bed.Cell, e = 0;
        for (int c = 0; c < p.Bed.Count; c++)
        {
            double h = p.Water.Depths[c];
            if (h <= 0) continue;
            var (u, v) = p.Water.VelocityAt(c);
            e += RhoW * a * h * (G * (p.Bed.Heights[c] + h / 2) + 0.5 * (u * u + v * v));
        }
        return e;
    }

    private static double Ledger(ShallowWater2D w) => w.Poured - (w.Volume + w.Infiltrated + w.Leaked + w.Drained + w.Clipped);

    /// <summary>
    /// A pit 4 m by 2 m cut 0.6 m down in level ground holds still water 0.4 m deep (surface −0.2 m). Bucketfuls of 0.2 m³ dug on
    /// dry ground are tipped into it, one after another, each left to still for 60 s. After each tip the water's energy, the
    /// moment the beds are resampled and once it has stilled, is set against what the soil gave up sinking from the surface:
    /// ρ_s g A Σ ∫ from b to min(b', s) of (s − z) dz over the fine cells whose bed rose from b to b' under water standing at s.
    /// </summary>
    [Theory]
    [InlineData(0.4)]
    [InlineData(0.15)]
    public void SoilTippedIntoStandingWaterRaisesItNoMoreThanTheSoilsFallPays(double depth)
    {
        var t = Map();
        var w = new ShallowWater2D(t) { Gravity = G };
        var patch = t.WorkAt(0, 0)!;
        w.Step(1e-3);
        var grid = w.Patches.Single(p => p.Patch == patch);
        double xc = patch.NodeX(patch.Nx / 2), zc = patch.NodeZ(patch.Nz / 2);
        patch.Shape((x, z) => Math.Abs(x - xc) <= 2 + 1e-9 && Math.Abs(z - zc) <= 1 + 1e-9 ? -0.6 : 0);
        w.Step(1e-3);
        var bed = grid.Bed;
        double area = bed.Cell * bed.Cell;
        w.Fill((x, z) => bed.CellAt(x, z) is { } c ? depth - 0.6 - bed.Heights[c] : 0);
        double standing = grid.Water.Volume;
        for (int i = 0; i < 60 * 10; i++) w.Step(1.0 / 60);

        double worstTransient = double.NegativeInfinity, worstStill = double.NegativeInfinity, paidAll = 0, gainedAll = 0;
        double startEnergy = WaterEnergy(grid);
        var spots = new[] { (0.0, 0.0), (-1.2, 0.4), (1.2, -0.4), (0.6, 0.6), (-0.6, -0.6) };
        for (int n = 0; n < spots.Length; n++)
        {
            double e0 = WaterEnergy(grid);
            var oldBed = (double[])bed.Heights.Clone();
            var oldSurface = Enumerable.Range(0, bed.Count).Select(c => bed.Heights[c] + grid.Water.Depths[c]).ToArray();
            var oldDepth = Enumerable.Range(0, bed.Count).Select(c => grid.Water.Depths[c]).ToArray();
            var s = patch.Scoop(xc + 6, zc - 6 + n * 0.9, 0.2)!.Value;          // dry ground, 6 m off
            patch.Settle(G);
            Assert.NotNull(patch.Pour(xc + spots[n].Item1, zc + spots[n].Item2, s.Volume, s.Soil));
            patch.Settle(G);
            w.Step(1e-6);                                                          // the beds resampled
            double e1 = WaterEnergy(grid);
            for (int i = 0; i < 60 * 60; i++) w.Step(1.0 / 60);
            double e2 = WaterEnergy(grid);
            // how far the still water rose: its mean surface over the cells wet before and after
            var both = Enumerable.Range(0, bed.Count).Where(c => oldDepth[c] > 1e-3 && grid.Water.Depths[c] > 1e-3).ToList();
            double rise = both.Average(c => bed.Heights[c] + grid.Water.Depths[c] - oldSurface[c]);
            // poured in, the soil meets the water at a surface that rises as it goes in, from s to s + rise: on average s + rise/2
            double paid = 0;
            for (int c = 0; c < bed.Count; c++)
            {
                double b = oldBed[c], b2 = bed.Heights[c], top = oldSurface[c] + rise / 2;
                if (b2 <= b || oldDepth[c] <= ShallowWater.Dry) continue;
                double hi = Math.Min(b2, top);                                     // the part of the new soil under the water
                if (hi > b) paid += RhoS * G * area * ((top - b) * (top - b) - (top - hi) * (top - hi)) / 2;
            }
            Console.WriteLine($"DIRT-WATER {depth} tip {n}: rise {rise * 1000:F1} mm, paid {paid:F1} J, water +{e1 - e0:F1} J at once, +{e2 - e0:F1} J stilled; displaced {grid.Displaced:F4} perched {grid.Perched:F4}");
            worstTransient = Math.Max(worstTransient, (e1 - e0) - paid);
            worstStill = Math.Max(worstStill, (e2 - e0) - paid);
            paidAll += paid;
            Assert.True(Math.Abs(Ledger(w)) <= 1e-9 * Math.Max(standing, 1e-6), $"ledger off by {Ledger(w):E3}");
        }
        gainedAll = WaterEnergy(grid) - startEnergy;
        Console.WriteLine($"DIRT-WATER {depth} all: paid {paidAll:F1} J, water gained {gainedAll:F1} J; worst excess at once {worstTransient:F1} J, stilled {worstStill:F1} J");
        Assert.True(worstTransient <= 1e-6, $"a tip gave the water {worstTransient:F1} J more than the soil's fall paid, at once");
        Assert.True(worstStill <= 1e-6, $"a tip gave the water {worstStill:F1} J more than the soil's fall paid, once still");
        Assert.True(gainedAll > 0, "the soil did push the water up: displacement is real");
    }
}
