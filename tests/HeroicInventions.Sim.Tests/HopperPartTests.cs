using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>The hopper as a placeable part (issue #50): fields, checks and the editor. The physics is in HopperTests.</summary>
public class HopperPartTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static MachineDef SandTimer() =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "sand-timer.machine")));

    [Fact]
    public void TheRuntimeOffersTheHoppersFieldsAndTakesARefillAndAGate()
    {
        var runtime = new MachineRuntime(SandTimer(), Materials);
        Assert.Equal(31.25, runtime.GetField("sand", "level"), precision: 9);                // cm
        Assert.Equal(25.9, runtime.GetField("sand", "flow"), precision: 1);                  // g/s
        Assert.Equal(1.619, runtime.GetField("sand", "speed"), precision: 3);                // mm/s
        Assert.Equal(1, runtime.GetField("arch", "arched"));
        runtime.SetField("sand", "orifice", 6);                                              // close the gate to 6 mm: (6 - 0.45)^2.5
        Assert.Equal(1000 * 0.58 * 1600 * Math.Sqrt(9.81) * Math.Pow(0.006 - 0.00045, 2.5), runtime.GetField("sand", "flow"), precision: 6);
        runtime.SetField("sand", "orifice", 1);                                              // to 1 mm: 3 grains across: it arches
        Assert.Equal(0, runtime.GetField("sand", "flow"));
        runtime.SetField("sand", "grain", 2);                                                // refill with 2 kg
        Assert.Equal(12.5, runtime.GetField("sand", "level"), precision: 9);
    }

    [Fact]
    public void AHoppersGravityFollowsTheScene()
    {
        var runtime = new MachineRuntime(SandTimer(), Materials);
        double earth = runtime.GetField("sand", "flow");
        runtime.SetField("scene", "gravity", 3.71);
        runtime.Step(0.01);
        Assert.Equal(Math.Sqrt(3.71 / 9.81), runtime.GetField("sand", "flow") / earth, precision: 9);
        Assert.Equal(3.71, runtime.GetField("sand", "gravity"), precision: 9);
    }

    [Fact]
    public void ABadHopperIsReportedAtTheClause()
    {
        var def = SandTimer();
        string Message(PartSpec change) => Assert.Throws<MachineFormatException>(() => new MachineRuntime(
            new MachineDef { Name = def.Name, Parts = def.Parts.Select(p => p.Id == change.Id ? change : p).ToList(), Pipes = [], Connects = [], SealedAir = [] }, Materials)).Message;
        var sand = def.Part("sand")!;
        PartSpec With(string key, SExpr value) => sand with { Props = new Dictionary<string, SExpr>(sand.Props) { [key] = value } };
        Assert.Contains("must be more than 0", Message(With("orifice", new SNumber(0))));
        Assert.Contains("must be more than 0", Message(With("grain-size", new SNumber(0))));
        Assert.Contains("cannot be negative", Message(With("grain", new SNumber(-1))));
    }

    [Fact]
    public void TheEditorPlacesAHopperAndItRoundTrips()
    {
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        s.Execute("(hopper h #:at (0 1 0))");
        s.Execute("(set h #:grain 3)");
        s.Execute("(set h #:orifice 0.012)");
        s.Execute("(set h #:grain-size 0.0004)");
        Assert.StartsWith("ok:", s.Execute("(check)"));
        string rkt = RktExporter.Write(s.Document.ToMachineDef());
        Assert.Contains("(hopper h", rkt);
        Assert.Contains("#:grain 3", rkt);
        var runtime = new MachineRuntime(s.Document.ToMachineDef(), Materials);
        Assert.Equal(3 / (1600 * 0.01) * 100, runtime.GetField("h", "level"), precision: 9);
    }
}
