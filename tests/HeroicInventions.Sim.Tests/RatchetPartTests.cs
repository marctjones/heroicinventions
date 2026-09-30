using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>The ratchet as a placeable part (issue #49): fields, checks and the editor. The physics is in RatchetTests.</summary>
public class RatchetPartTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static MachineDef Windlass() =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "ratchet-windlass.machine")));

    [Fact]
    public void TheRuntimeOffersTheRatchetsFields()
    {
        var runtime = new MachineRuntime(Windlass(), Materials);
        Assert.Equal(30, runtime.GetField("hold-pawl", "pitch"), precision: 9);           // 360 / 12 teeth
        Assert.Equal(0, runtime.GetField("hold-pawl", "steps"));
        Assert.Equal(0, runtime.GetField("hold-pawl", "held"));
        var ratchet = runtime.Ratchets["hold-pawl"];
        Assert.Equal(0.15, ratchet.ToothRadius, precision: 12);
        Assert.False(ratchet.Reverse);
    }

    [Fact]
    public void ABadRatchetIsReportedAtTheClause()
    {
        var def = Windlass();
        string Message(PartSpec change) => Assert.Throws<MachineFormatException>(() => new MachineRuntime(
            new MachineDef { Name = def.Name, Parts = def.Parts.Select(p => p.Id == change.Id ? change : p).ToList(), Pipes = [], Connects = [], SealedAir = [] }, Materials)).Message;
        var pawl = def.Part("hold-pawl")!;
        PartSpec With(string key, SExpr value) => pawl with { Props = new Dictionary<string, SExpr>(pawl.Props) { [key] = value } };
        Assert.Contains("not a wheel, pulley or drum", Message(With("on", new SSymbol("hold-load"))));
        Assert.Contains("at least three teeth", Message(With("teeth", new SNumber(2))));
    }

    [Fact]
    public void TheDefaultToothCircleIsHalfAgainTheWheelsRadius()
    {
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        foreach (string line in CommandScript.For(Windlass())) s.Execute(line);
        s.Execute("(set hold-pawl #:radius 0)");                                           // 0 asks for the default
        var runtime = new MachineRuntime(s.Document.ToMachineDef(), Materials);
        Assert.Equal(0.15, runtime.Ratchets["hold-pawl"].ToothRadius, precision: 12);      // the drum is 10 cm
    }

    [Fact]
    public void TheEditorPlacesARatchetPicksItsWheelAndFlipsItsDirection()
    {
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        foreach (string line in CommandScript.For(Windlass())) s.Execute(line);
        s.Execute("(remove hold-pawl)");
        s.Execute("(ratchet pawl #:at (0 2 0))");
        Assert.Equal("?", s.Document.Parts["pawl"].Symbol("on", ""));
        Assert.Throws<InvalidOperationException>(() => s.Execute("(set pawl #:on nowhere)"));
        s.Execute("(set pawl #:on hold-drum)");
        s.Execute("(set pawl #:teeth 8)");
        s.Execute("(set pawl #:reverse #t)");
        Assert.StartsWith("ok:", s.Execute("(check)"));
        string rkt = RktExporter.Write(s.Document.ToMachineDef());
        Assert.Contains("(ratchet pawl", rkt);
        Assert.Contains("#:teeth 8", rkt);
        Assert.Contains("#:reverse #t", rkt);
        Assert.True(new MachineRuntime(s.Document.ToMachineDef(), Materials).Ratchets["pawl"].Reverse);
    }
}
