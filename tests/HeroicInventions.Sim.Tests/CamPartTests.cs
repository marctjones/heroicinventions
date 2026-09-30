using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>The cam as a placeable part (issue #48): fields, checks and the editor. The physics is in CamTests.</summary>
public class CamPartTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static MachineDef TripHammer() =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "trip-hammer.machine")));

    [Fact]
    public void TheRuntimeOffersTheCamsFields()
    {
        var runtime = new MachineRuntime(TripHammer(), Materials);
        Assert.Equal(90, runtime.GetField("strong-hammer", "pitch"), precision: 9);        // four pegs: 90 degrees apart
        Assert.Equal(0, runtime.GetField("strong-hammer", "strikes"));
        Assert.Equal(1, runtime.GetField("strong-hammer", "contact"));                     // resting on its anvil
        var cam = runtime.Cams["strong-hammer"];
        Assert.Equal(5 * 9.81, cam.Mass * cam.Gravity, precision: 9);
        runtime.SetField("strong-hammer", "mass", 10);                                     // a heavier hammer
        Assert.Equal(10, cam.Mass);
    }

    [Fact]
    public void ABadCamIsReportedAtTheClause()
    {
        var def = TripHammer();
        string Message(PartSpec change) => Assert.Throws<MachineFormatException>(() => new MachineRuntime(
            new MachineDef { Name = def.Name, Parts = def.Parts.Select(p => p.Id == change.Id ? change : p).ToList(), Pipes = [], Connects = [], SealedAir = [] }, Materials)).Message;
        var hammer = def.Part("strong-hammer")!;
        PartSpec With(string key, SExpr value) => hammer with { Props = new Dictionary<string, SExpr>(hammer.Props) { [key] = value } };
        Assert.Contains("not a wheel, pulley or drum", Message(With("on", new SSymbol("nowhere"))));
        Assert.Contains("at least one peg", Message(With("pegs", new SNumber(0))));
        Assert.Contains("#:lift and #:mass must be more than 0", Message(With("lift", new SNumber(0))));
        Assert.Contains("share of a peg's pitch", Message(With("rise", new SNumber(1.5))));
    }

    [Fact]
    public void TheEditorPlacesACamAndPicksItsWheel()
    {
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        foreach (string line in CommandScript.For(TripHammer())) s.Execute(line);
        s.Execute("(remove strong-hammer)");
        s.Execute("(cam hammer #:at (0.4 0.3 0))");
        Assert.Equal("?", s.Document.Parts["hammer"].Symbol("on", ""));                     // not yet on a wheel
        Assert.Throws<InvalidOperationException>(() => s.Execute("(set hammer #:on nowhere)"));
        s.Execute("(set hammer #:on strong-wheel)");
        s.Execute("(set hammer #:pegs 6)");
        Assert.StartsWith("ok:", s.Execute("(check)"));
        Assert.Contains("(cam hammer", RktExporter.Write(s.Document.ToMachineDef()));
        Assert.Contains("#:pegs 6", RktExporter.Write(s.Document.ToMachineDef()));
    }
}
