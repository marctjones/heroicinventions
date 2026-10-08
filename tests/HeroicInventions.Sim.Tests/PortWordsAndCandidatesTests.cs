using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issues #178 and #179: connection points in words, and what a selected part can join.</summary>
public class PortWordsAndCandidatesTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private const string Sample = """
        (
         (entry pulley-5cm "Pulley sheave, 5 cm radius" pulley "pulley-4482b6c318" 0.0001 (1e-8 1e-8 2e-8) ((radius 0.05) (width 0.025))))
        """;

    private static BuildSession Session(params string[] commands)
    {
        var s = new BuildSession(Materials, catalogue: CatalogueReader.Parse(Sample), machinesDir: Path.GetTempPath());
        foreach (var c in commands) s.Execute(c);
        return s;
    }

    [Fact]
    public void EveryPortOfEveryPrimitiveKindHasWords()
    {
        int ports = 0;
        foreach (var kind in PartTemplates.PrimitiveKinds)
        {
            var part = PartTemplates.Create(kind, "p", new Vec3(0, 0, 0), "bronze");
            foreach (var port in part.Ports)
            {
                ports++;
                Assert.True(PortWords.HasWords(kind, port.Name), $"{kind}.{port.Name} has no words in PortWords");
                Assert.False(string.IsNullOrWhiteSpace(PortWords.Words(part, port)), $"{kind}.{port.Name}");
            }
        }
        Assert.True(ports >= 5, "tank (2), boiler, rotor and jet wheel have ports");
    }

    [Fact]
    public void PortsAreCalledWhatTheyDo()
    {
        var tank = PartTemplates.Create("tank", "t", new Vec3(0, 0, 0), "bronze");
        Assert.Equal(["water in", "water out"], tank.Ports.Select(p => PortWords.Words(tank, p)));
        var boiler = PartTemplates.Create("boiler", "b", new Vec3(0, 0, 0), "bronze");
        Assert.Equal("steam out", PortWords.Words(boiler, boiler.Ports[0]));
        var rotor = PartTemplates.Create("rotor", "r", new Vec3(0, 0, 0), "bronze");
        Assert.Equal("steam in", PortWords.Words(rotor, rotor.Ports[0]));
    }

    [Fact]
    public void APortMadeByHandStillGetsWordsByItsKindOfConnection()
    {
        Assert.Equal("water", PortWords.Words("block", new PortSpec("x", "water", 0)));
        Assert.Equal("steam", PortWords.Words("block", new PortSpec("x", "steam", 0)));
        Assert.Equal("hot pipe", PortWords.Words("block", new PortSpec("hot-pipe", "heat", 0)));
    }

    [Fact]
    public void DotsOnOneSpotShareOneLabel()
    {
        Assert.Equal("water in / out", PortWords.Combine(["water in", "water out"]));
        Assert.Equal("steam in", PortWords.Combine(["steam in", "steam in"]));
        Assert.Equal("water in / steam out", PortWords.Combine(["water in", "steam out"]));
    }

    [Fact]
    public void ASelectedTankLightsTheOtherTanksAndTheirDots()
    {
        var s = Session("(tank t1 #:at (0 0 0))", "(tank t2 #:at (1 0 0))", "(boiler b1 #:at (0 0 1))", "(rotor r1 #:at (1 0 1))", "(block k1 #:at (-1 0 0))");
        var c = LinkGestures.Candidates(s.Document, "t1");
        Assert.Equal(["t2"], c.Select(x => x.PartId).Distinct());
        Assert.Contains(c, x => x.Via == "pipe" && x.OwnPort == "outlet" && x.TheirPort == "inlet" && x.Words == "water in");
        Assert.Contains(c, x => x.Via == "shared air");
        Assert.DoesNotContain(c, x => x.PartId is "b1" or "r1" or "k1");
    }

    [Fact]
    public void ABoilerLightsTheRotorAndARotorLightsItsBoilerUntilItHasOne()
    {
        var s = Session("(boiler b1 #:at (0 0 1))", "(rotor r1 #:at (1 0 1))", "(rotor r2 #:at (2 0 1))", "(tank t1 #:at (0 0 0))");
        Assert.Equal(["r1", "r2"], LinkGestures.Candidates(s.Document, "b1").Select(x => x.PartId).Distinct().Order());
        Assert.Contains(LinkGestures.Candidates(s.Document, "b1"), x => x.TheirPort == "steam-in" && x.Words == "steam in");
        Assert.Equal(["b1"], LinkGestures.Candidates(s.Document, "r1").Select(x => x.PartId).Distinct());
        s.Execute("(snap b1.steam r1.steam-in)");
        Assert.Empty(LinkGestures.Candidates(s.Document, "b1"));   // one rotor per boiler: nothing left to join
    }

    [Fact]
    public void APipeStartedFromOnePortLightsOnlyTheCompatibleDots()
    {
        var s = Session("(tank t1 #:at (0 0 0))", "(tank t2 #:at (1 0 0))", "(boiler b1 #:at (0 0 1))");
        var c = LinkGestures.Candidates(s.Document, "t1", onlyPort: "inlet");
        Assert.All(c, x => { Assert.Equal("t2", x.PartId); Assert.Equal("pipe", x.Via); Assert.Equal("inlet", x.OwnPort); });
    }

    [Fact]
    public void APulleyLightsTheWheelsItCouldMeshOrShareAnAxleWith()
    {
        var s = Session("(wheel w1 #:at (0 0 0) #:catalogue pulley-5cm)", "(wheel w2 #:at (0.2 0 0) #:catalogue pulley-5cm)", "(tank t1 #:at (0 0 1))", "(block k1 #:at (0 0 2))");
        var c = LinkGestures.Candidates(s.Document, "w1");
        Assert.Equal(["w2"], c.Select(x => x.PartId).Distinct());
        Assert.Equal(["axle", "belt", "gear mesh"], c.Select(x => x.Via).Order());
        s.Execute("(mesh w1 w2)");
        Assert.DoesNotContain(LinkGestures.Candidates(s.Document, "w1"), x => x.Via == "gear mesh");
        Assert.Empty(LinkGestures.Candidates(s.Document, "k1"));
    }
}
