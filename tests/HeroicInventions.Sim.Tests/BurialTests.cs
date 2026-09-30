using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #54: buried bodies and slides. Predictions in cliff.rkt and buried-crate.rkt, worked out first.</summary>
public class BurialTests
{
    private static Terrain Load(string name) =>
        Terrain.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "maps", name + ".map")), name);

    /// <summary>
    /// The pull to draw a 0.5 m, 90 kg crate straight up out of loam (c 5 kPa,
    /// tan φ 0.55, 1400 kg/m³): 14.2 kN from under 1 m, 10.5 kN from under
    /// 0.5 m; and out of the cliff's loose debris (no cohesion, clay's
    /// 0.35 and 1800) under 0.33 m, 4.77 kN.
    /// </summary>
    [Fact]
    public void ThePullToDrawACrateOutOfTheGround()
    {
        var loam = Load("buried-field").Soils[0];
        Assert.Equal(5000, loam.Cohesion);
        Assert.Equal(14.2, Burial.PullOut(1.0, 0.5, 90, loam, 9.81) / 1000, 1);
        Assert.Equal(10.5, Burial.PullOut(0.5, 0.5, 90, loam, 9.81) / 1000, 1);
        var clay = Load("cliff").Soils[0];
        Assert.Equal(4.77, Burial.PullOut(0.333, 0.5, 90, clay, 9.81, loose: true) / 1000, 0.05);
        Assert.True(Burial.Held(0.13, 0.5));
        Assert.False(Burial.Held(0.12, 0.5));
    }

    /// <summary>
    /// The cliff (4 m of clay that stands to 3.19 m) settles: its crest column
    /// fails and 2 m² a metre of loose clay lies against the new face at
    /// repose, 1.18 m deep there and running out 3.38 m, to x = 7.88; the face
    /// left, under 3.19 m of drop, stands; not a cubic metre is lost.
    /// </summary>
    [Fact]
    public void ACliffTooTallForItsClaySlidesToRepose()
    {
        var map = Load("cliff");
        Assert.True(map.SettleOnLoad);
        double before = map.Heights.Sum();
        var (failures, passes) = map.Settle(9.81);
        Assert.True(failures > 0 && passes < 20000);
        Assert.Equal(before, map.Heights.Sum(), 6);
        int row = map.Nx * (map.Nz / 2);
        double Height(double x) => map.Heights[row + (int)((x - map.X0) / map.Cell)];
        Assert.Equal(4, Height(4.25), 9);                              // the face behind stands
        Assert.Equal(1.18, Height(4.75) + 0.35 * 0.25, 0.1);          // the wedge against it, at the face (x = 4.5)
        double toe = Enumerable.Range(0, map.Nx).Select(i => map.X0 + (i + 0.5) * map.Cell).Where(x => x > 4.5 && Height(x) > 0.005).Max();
        Assert.InRange(toe, 7.88 - 0.5, 7.88 + 0.25);                  // run-out, to a cell
        Assert.Equal(0.83, map.HeightAt(5.5, 0), 0.02);                 // over the crate at x = 5.5
    }
}
