using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #44: digging, spoil and wall collapse on a map, by Mohr–Coulomb. Predictions worked out first.</summary>
public class EarthworksTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static string Dir(string d, string f) => Path.Combine(AppContext.BaseDirectory, d, f);

    private static Terrain Flat(int n, double cell, SoilSpec soil) => new()
    {
        Name = "flat", X0 = -n * cell / 2, Z0 = -n * cell / 2, Cell = cell, Nx = n, Nz = n,
        Heights = new double[n * n], Soil = new int[n * n], Soils = [soil], OpenEdges = false,
    };

    /// <summary>
    /// 10 m³ of dry sand (tan φ = 0.62) tipped at one point on 0.5 m cells,
    /// a spadeful (25 L) at a time, each slumping until no step to a
    /// neighbour, across or diagonal, is steeper than repose. Its steepest possible shape is then a pyramid whose
    /// contours are octagons (the diagonal neighbours hold it round), volume
    /// 1.1046 h³/tan²φ, so h = (V tan²φ / 1.1046)^(1/3) = 1.515 m (a true cone
    /// would be 1.542). Tolerance set beforehand: half a cell's rise at
    /// repose, 0.16 m. Every m³ is still there.
    /// </summary>
    [Fact]
    public void ASpoilHeapSlumpsToItsAngleOfRepose()
    {
        var sand = new SoilSpec("sand", 0, Cohesion: 0, Friction: 0.62, Density: 1600);
        var g = Flat(40, 0.5, sand);
        int centre = g.CellAt(0.1, 0.1)!.Value;
        for (int k = 0; k < 400; k++)
        {
            g.Heap(centre, 0.025);
            var (_, passes) = g.Relax(0, 0, g.Nx - 1, g.Nz - 1, 9.81);
            Assert.True(passes < 5000, "it settled");
        }
        Assert.Equal(10, g.Heights.Sum() * 0.25, 9);
        double peak = g.Heights.Max();
        Assert.Equal(Math.Cbrt(10 * 0.62 * 0.62 / 1.1046), peak, 0.16);
        for (int j = 0; j < g.Nz; j++)
            for (int i = 0; i + 1 < g.Nx; i++)
                Assert.True(Math.Abs(g.Heights[i + j * g.Nx] - g.Heights[i + 1 + j * g.Nx]) <= 0.62 * 0.5 + 1e-6, "no step steeper than repose");
    }

    /// <summary>
    /// A cut face stands while it is no taller than 4c/γ · tan(45° + φ/2) and
    /// falls past it: in clay (c 10 kPa, tan φ 0.35, 1800 kg/m³) that is
    /// 3.193 m. A step of 3.19 m stands; one of 3.20 m fails and slumps.
    /// </summary>
    [Fact]
    public void ACutFaceStandsToItsCriticalHeight()
    {
        var clay = new SoilSpec("clay", 0, Cohesion: 10000, Friction: 0.35, Density: 1800);
        Assert.Equal(3.193, clay.CriticalHeight(9.81), 3);
        foreach (var (step, falls) in new[] { (3.19, false), (3.20, true) })
        {
            var g = Flat(20, 0.5, clay);
            for (int j = 0; j < g.Nz; j++)
                for (int i = 0; i < g.Nx / 2; i++) g.Dig(i + j * g.Nx, step);   // half the ground cut down by the step
            var (failures, _) = g.Relax(0, 0, g.Nx - 1, g.Nz - 1, 9.81);
            Assert.Equal(falls, failures > 0);
        }
    }

    /// <summary>
    /// The scene (trench-crew.rkt, predicted there): the gang cuts the
    /// trench a spit at a time; the walls fall in on the spit that takes it
    /// to 3.25 m, the first past 3.193 m; by then 13 m³ are out at 48.7 kJ/m³,
    /// 634 kJ, at 3 kW about 211 s; and the spoil lies at repose. All the
    /// clay is still on the map.
    /// </summary>
    [Fact]
    public void TheGangsTrenchFallsInPastTheClaysCriticalHeight()
    {
        var world = WorldDef.Parse(File.ReadAllText(Dir("worlds", "trench.world")), "trench.world");
        var map = Terrain.Parse(File.ReadAllText(Dir("maps", world.Map + ".map")), world.Map!);
        var ground = new WorldGround(map);
        var place = Assert.Single(world.Placements);
        var crew = new MachineRuntime(WorldDef.Placed(MachineDef.Parse(File.ReadAllText(Dir("machines", place.Machine + ".machine"))), place, map), Materials);
        ground.Attach(place.Label, crew);
        var gang = crew.Diggers["gang"];
        Assert.Equal(16, gang.Cells.Count);   // 8 cells along by 2 across: 4 m x 1 m of half-metre cells
        double volume = map.Heights.Sum();
        double t = 0;
        while (!gang.Done && t < 1000) { crew.Step(0.1); ground.Step(0.1); t += 0.1; }

        Assert.True(gang.Collapsed, "the walls fell in");
        Assert.Equal(3.25, gang.CollapseDepth, 9);
        Assert.Equal(13, gang.Dug, 9);
        Assert.Equal(48.74, gang.SpecificWork / 1000, 0.05);
        Assert.Equal(211, t, 1.5);
        Assert.Equal(volume, map.Heights.Sum(), 6);   // dug = heaped: nothing lost
        // the spoil ridge beside the trench stands at repose, no steeper
        for (int j = 0; j < map.Nz; j++)
            for (int i = 0; i + 1 < map.Nx; i++)
            {
                int a = i + j * map.Nx, b = a + 1;
                if (map.Loose[a] && map.Loose[b]) Assert.True(Math.Abs(map.Heights[a] - map.Heights[b]) <= 0.35 * 0.5 + 1e-6);
            }
    }
}
