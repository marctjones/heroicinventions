using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #29: a float riding a draining cistern (floats.rkt's prediction).</summary>
public class FloatTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    /// <summary>
    /// Draft m / (rho A) = 0.5 / (1000 x 0.01) = 5 cm; the float rides 5 cm
    /// under the surface while the cistern drains, and grounds when the water
    /// is 5 cm deep, at 307.7 s by Torricelli.
    /// </summary>
    [Fact]
    public void AFloatRidesTheSurfaceAtItsDraftThenGrounds()
    {
        var rt = new MachineRuntime(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "floats.machine"))), Materials);
        var bob = rt.Floats["bob"];
        Assert.Equal(5, rt.GetField("bob", "draft"), 9);
        double groundedAt = -1;
        for (int k = 1; k <= 40000; k++)
        {
            rt.Step(0.01);
            var cistern = rt.Tanks["cistern"];
            if (groundedAt < 0 && bob.Grounded) groundedAt = k * 0.01;
            if (!bob.Grounded) Assert.Equal(cistern.SurfaceElevation - 0.05, bob.Bottom, 9);
            else Assert.Equal(cistern.BaseElevation, bob.Bottom, 9);
        }
        Assert.Equal(307.7, groundedAt, 0.5);
        Assert.Equal(1, rt.GetField("bob", "grounded"));
    }

    /// <summary>A float heavier than the water its whole height could push aside never floats.</summary>
    [Fact]
    public void AFloatTooHeavyForItsHeightSitsOnTheFloor()
    {
        var def = MachineDef.Parse("""
            (machine sinker
              (part pond tank (material oak) (at 0 0 0) (props (area 1) (height 1) (water 0.8)) (ports))
              (part lump float (material iron) (at 0 0 0) (props (in pond) (mass 20) (area 0.01) (height 0.1)) (ports)))
            """);
        var rt = new MachineRuntime(def, Materials);
        Assert.Equal(1, rt.GetField("lump", "grounded"));
        Assert.Equal(0, rt.Floats["lump"].Bottom, 9);
        Assert.Equal(10, rt.GetField("lump", "submerged"), 9);
    }
}
