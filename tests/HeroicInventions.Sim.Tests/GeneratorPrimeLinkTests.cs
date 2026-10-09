using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// GAP 9: a generator in one machine driven over a world shaft link (#78, #191) from another machine's windmill and train. Before, its own
/// machine had no prime mover, so it filed its charge under "shaft" and had no prime-mover speed (Gear up could not see the ratio). The works
/// are found-electrics without its motor: the 5 m sails and three 100:20 meshes at 0.97 to pinion-d (125:1). The crate is a rotor on an axle
/// with the motor on it, its cells and bank in a vault. The shaft joins pinion-d to the rotor at ratio 1; in the game both are Jolt bodies,
/// here stand-ins (the link's prime-mover walk reads only the machines' files).
/// </summary>
public class GeneratorPrimeLinkTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private sealed class Axle : IShaft
    {
        public double AngularVelocity { get; set; }
        public double ShaftInertia => 0.1;
        public void AddAngularImpulse(double impulse) => AngularVelocity += impulse / ShaftInertia;
    }

    private static (MachineRuntime Works, MachineRuntime Crate, WorldLinks Links) Build(double ratio = 1)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "machines", "found-electrics.machine"));
        bool Crate(string l) => l.Contains("(part rotor-disc ") || l.Contains("(part motor ") || l.Contains("(part shelf ") || l.Contains("(part vault ")
                                || l.Contains("(part cells ") || l.Contains("(part bank ");
        string works = string.Join("\n", lines.Where(l => !Crate(l) && !l.Contains("(arbor (parts pinion-d rotor-disc)"))).TrimEnd() + ")";
        if (works.Count(c => c == '(') != works.Count(c => c == ')')) works = works[..^1];
        string crate = "(machine motor-crate\n" + string.Join("\n", lines.Where(Crate)) + ")";
        var w = new MachineRuntime(MachineDef.Parse(works), Materials);
        var c = new MachineRuntime(MachineDef.Parse(crate), Materials);
        var axles = new Dictionary<string, IShaft> { ["works.pinion-d"] = new Axle(), ["crate.rotor-disc"] = new Axle() };
        var links = WorldLinks.Build(
            [new LinkSpec("shaft-1", "shaft", new LinkEnd("works", "pinion-d"), new LinkEnd("crate", "rotor-disc")) { Ratio = ratio }],
            label => label == "works" ? w : label == "crate" ? c : null,
            end => axles.GetValueOrDefault($"{end.Label}.{end.Part}"));
        return (w, c, links);
    }

    [Fact]
    public void AMotorShaftLinkedToAnotherMachinesWindmillFilesItsChargeUnderTheWind()
    {
        var (works, crate, links) = Build();
        Assert.Null(Assert.Single(links.All).Unfinished);
        Assert.Empty(works.Generators);
        var gen = crate.Generators["motor"];
        Assert.Null(gen.Prime);                                              // its own machine has no prime mover
        Assert.Equal("wind", gen.DrivenBy);                                  // across the link: the works' sails
        var m = Assert.IsType<PrimeMover>(gen.Mover);
        Assert.Equal("sails", m.Id);
        Assert.Equal(125, m.Ratio, 9);
        Assert.Equal(0.97 * 0.97 * 0.97, m.Eta, 9);
        Assert.Equal(278.30, gen.EstimatePower(), 0.003 * 278.30);           // the found-electrics operating point, as if one machine

        // the sails at the header's loaded 1.39583 rad/s, the rotor at 125 x that: the prime mover's speed and the train's ratio read right
        var sails = works.Windmills["sails"];
        sails.AddAngularImpulse(1.39583 * sails.MomentOfInertia);
        Assert.Equal(1.39583, gen.PrimeOmega!(), 9);
        crate.Banks["bank"].Charge = 0;
        gen.Step(125 * 1.39583, 1.0 / 120);
        Assert.Equal(125, gen.TrainRatio!.Value, 6);
        var bank = crate.Banks["bank"];
        Assert.True(bank.Charge > 0);
        Assert.Equal(bank.Charge, bank.Sources["wind"], 9);
        Assert.False(bank.Sources.ContainsKey("shaft"));
        Assert.Equal(bank.Charge / BatteryBank.JoulesPerWattHour, crate.GetField("bank", "from-wind"), 9);

        // the links taken down: back to its own machine's answer
        links.Release();
        Assert.Equal("shaft", gen.DrivenBy);
        Assert.Null(gen.PrimeOmega);
    }

    [Fact]
    public void AGearboxOnTheLinkCountsInTheRatio()
    {
        // ratio 2: the rotor turns at twice pinion-d's speed, 250:1 from the sails
        var (_, crate, _) = Build(ratio: 2);
        Assert.Equal(250, crate.Generators["motor"].Mover!.Ratio, 9);
    }
}
