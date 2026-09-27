using HeroicInventions.Sim;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;
using HeroicInventions.Sim.Parts;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Tests;

public class MaterialTests
{
    [Fact]
    public void DefaultLibraryLoadsWoodAndStone()
    {
        var lib = MaterialLibrary.LoadDefault();
        Assert.Equal(MaterialCategory.Wood, lib["oak"].Category);
        Assert.Equal(MaterialCategory.Stone, lib["granite"].Category);
        Assert.True(lib["cedar"].Density < lib["oak"].Density);
    }

    [Fact]
    public void OneCubicMetreOfMarbleIsAboutTwoPointSevenTonnes() =>
        Assert.Equal(2700, MaterialLibrary.LoadDefault()["marble"].MassOf(1.0), precision: 0);

    [Fact]
    public void WoodIsWeakAcrossTheGrain()
    {
        var oak = MaterialLibrary.LoadDefault()["oak"];
        // 2 kN on a 1 cm² (1e-4 m²) section = 20 MPa: fine along the grain, fails across it.
        Assert.False(oak.FailsInTension(2000, 1e-4));
        Assert.True(oak.FailsInTension(2000, 1e-4, acrossGrain: true));
    }
}

public class FluidTests
{
    [Fact]
    public void ConnectedOpenTanksLevelOutAndConserveWater()
    {
        var net = new FluidNetwork();
        var a = net.AddTank(new Tank("A", 0, 1, 2, waterVolume: 1.5));
        var b = net.AddTank(new Tank("B", 0, 1, 2, waterVolume: 0.5));
        net.AddPipe(new Pipe("AB", a, 0, b, 0, conductance: 0.5));
        double before = net.TotalWater;

        for (int i = 0; i < 600; i++) net.Step(0.1);

        Assert.Equal(a.Level, b.Level, precision: 3);
        Assert.Equal(before, net.TotalWater, precision: 9);
    }

}

public class SteamTests
{
    [Fact]
    public void SaturationPressureIsOneAtmosphereAtBoiling() =>
        Assert.InRange(Boiler.SaturationPressure(100), 100_000, 102_500);

    [Fact]
    public void AeolipileSpinsUpOnceTheWaterBoils()
    {
        var aeolipile = new Aeolipile(new Boiler(waterMassKg: 0.3, heatInputW: 3000));

        for (int i = 0; i < 20_000; i++) aeolipile.Step(0.01); // 200 s

        Assert.True(aeolipile.Boiler.Temperature >= 100);
        Assert.True(aeolipile.Rpm > 100, $"expected a spinning rotor, got {aeolipile.Rpm:F0} rpm");
    }
}

public class AssemblyTests
{
    private static readonly PartDef Axle = new("axle", "Oak axle", "oak", 0.001,
        [new PortDef("end", PortKind.Axle, 0, 0, 0)]);
    private static readonly PartDef Pipe = new("pipe", "Bronze pipe", "bronze", 0.0001,
        [new PortDef("in", PortKind.PipeFitting, 0, 0, 0)]);

    [Fact]
    public void RejectsConnectingAnAxleToAPipe()
    {
        var asm = new Assembly();
        var axle = asm.Add(Axle);
        var pipe = asm.Add(Pipe);
        Assert.Throws<InvalidOperationException>(() => asm.Connect(axle, "end", pipe, "in"));
    }
}
