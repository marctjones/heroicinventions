using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #32: sensors that act when something arrives. The Jolt-side fall test (a weight tripping a sluice) is in heroic/tests/machine-behavior-test.rkt.</summary>
public class TriggerTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static BuildSession Fresh() => new(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "sensors");

    /// <summary>A 10 L/s spring into a 0.1 m2 tank: the level climbs 10 cm a second, so 5 cm is reached at 0.5 s, and that switches a pot's fire on.</summary>
    private static BuildSession LevelSwitch()
    {
        var s = Fresh();
        s.Execute("(tank pool #:at (0 0 0) #:area 0.1 #:height 1)");
        s.Execute("(inflow spring #:into pool #:flow 0.01)");
        s.Execute("(boiler pot #:at (2 0 0))");
        s.Execute("(trigger switch #:when (pool level above 5) #:do ((pot fire 3000)))");
        return s;
    }

    [Fact]
    public void AFieldTriggerFiresWhenTheLevelCrossesItsValueAndSetsTheField()
    {
        var runtime = new MachineRuntime(LevelSwitch().Document.ToMachineDef(), Materials);
        double before = -1;
        while (runtime.Time < 1.0)
        {
            runtime.Step(0.01);
            if (runtime.GetField("switch", "fired") == 0) before = runtime.GetField("pot", "fire");
        }
        Assert.Equal(0, before);                                        // the fire stayed out until it fired
        Assert.Equal(3000, runtime.GetField("pot", "fire"));            // and the action landed
        Assert.Equal(0.5, runtime.GetField("switch", "fired-at"), precision: 1);   // 5 L at 10 L/s, to the step
        Assert.Equal(1, runtime.GetField("switch", "fired"));
    }

    [Fact]
    public void ATriggerFiresOnce()
    {
        var runtime = new MachineRuntime(LevelSwitch().Document.ToMachineDef(), Materials);
        while (runtime.Time < 0.7) runtime.Step(0.01);
        double at = runtime.GetField("switch", "fired-at");
        runtime.SetField("pot", "fire", 0);                    // put the fire out again by hand
        while (runtime.Time < 1.5) runtime.Step(0.01);         // the level is still above 5 cm
        Assert.Equal(at, runtime.GetField("switch", "fired-at"));
        Assert.Equal(0, runtime.GetField("pot", "fire"));      // it did not fire a second time
    }

    [Fact]
    public void ABodyTriggerFiresOnlyWhenTheBodyIsInsideItsBox()
    {
        var s = Fresh();
        s.Execute("(block weight #:at (0 2 0))");
        s.Execute("(boiler pot #:at (2 0 0))");
        s.Execute("(trigger catch #:at (0 1 0) #:size (0.5 0.1 0.5) #:body weight #:do ((pot fire 500)))");
        var runtime = new MachineRuntime(s.Document.ToMachineDef(), Materials);

        Assert.False(runtime.TestBodyTrigger("catch", 0, 2.0, 0));        // above it
        Assert.False(runtime.TestBodyTrigger("catch", 0.3, 1.0, 0));      // level with it, but beside it (box half-width 0.25)
        Assert.Equal(0, runtime.GetField("pot", "fire"));
        Assert.True(runtime.TestBodyTrigger("catch", 0.2, 1.04, 0.1));    // inside
        Assert.Equal(500, runtime.GetField("pot", "fire"));
        Assert.False(runtime.TestBodyTrigger("catch", 0, 1.0, 0));        // already fired
    }

    [Fact]
    public void TheTriggerClauseRoundTripsThroughTheFileFormatAndTheCommandScript()
    {
        var original = LevelSwitch().Document.ToMachineDef();
        var reparsed = MachineDef.Parse(MachineWriter.Write(original));
        var t = Assert.Single(reparsed.Triggers);
        Assert.Equal(("pool", "level", true, 5.0), (t.WatchTarget, t.WatchField, t.Rising, t.Threshold));
        Assert.Equal(new TriggerAction("pot", "fire", 3000), Assert.Single(t.Actions));

        var again = Fresh();
        foreach (string line in CommandScript.For(reparsed)) again.Execute(line);
        Assert.Equal(MachineWriter.Write(original).Replace("sensors", "x"), MachineWriter.Write(again.Document.ToMachineDef()).Replace("sensors", "x"));
        Assert.Contains("(trigger switch", RktExporter.Write(original));
    }

    [Fact]
    public void ABadTriggerIsReportedAtTheClause()
    {
        string Check(string trigger)
        {
            var s = Fresh();
            s.Execute("(tank pool #:at (0 0 0) #:area 0.1 #:height 1)");
            s.Execute("(block weight #:at (0 2 0))");
            s.Execute("(boiler pot #:at (2 0 0))");
            s.Execute(trigger);
            return s.Execute("(check)");
        }
        Assert.Contains("not a settable field", Check("(trigger t #:when (pool level above 5) #:do ((pot colour 1)))"));
        Assert.Contains("not a readable field", Check("(trigger t #:when (pool colour above 5) #:do ((pot fire 1)))"));
        Assert.Contains("which is not a part", Check("(trigger t #:at (0 1 0) #:size (1 1 1) #:body ghost #:do ((pot fire 1)))"));
        Assert.Contains("needs #:at and a positive #:size", Check("(trigger t #:body weight #:do ((pot fire 1)))"));
        Assert.Contains("either a body", Check("(trigger t #:do ((pot fire 1)))"));
        Assert.Contains("does nothing", Check("(trigger t #:when (pool level above 5) #:do ())"));
        Assert.StartsWith("ok:", Check("(trigger t #:when (pool level below 5) #:do ((pot fire 1)))"));
    }

    [Fact]
    public void RemovingAWatchedPartTakesItsTriggerWithIt_AndTranslationMovesTheBox()
    {
        var s = Fresh();
        s.Execute("(block weight #:at (0 2 0))");
        s.Execute("(boiler pot #:at (2 0 0))");
        s.Execute("(trigger catch #:at (0 1 0) #:size (0.5 0.1 0.5) #:body weight #:do ((pot fire 500)))");
        var moved = s.Document.ToMachineDef().Translated(new Vec3(10, 0, 5));
        Assert.Equal(new Vec3(10, 1, 5), Assert.Single(moved.Triggers).At);

        s.Execute("(remove pot)");                      // the thing it would have lit is gone
        Assert.Empty(s.Document.Triggers);
        s.Execute("(undo)");
        s.Execute("(remove catch)");                    // or take the trigger itself away by name
        Assert.Empty(s.Document.Triggers);
        Assert.Contains("weight", s.Document.Parts.Keys);
    }

    [Fact]
    public void TheInspectorListsATriggerOnThePartsItTouches()
    {
        var s = Fresh();
        s.Execute("(block weight #:at (0 2 0))");
        s.Execute("(boiler pot #:at (2 0 0))");
        s.Execute("(trigger catch #:at (0 1 0) #:size (0.5 0.1 0.5) #:body weight #:do ((pot fire 500)))");
        foreach (var part in new[] { "weight", "pot" })
        {
            var link = Assert.Single(LinkGestures.LinksOn(s.Document, part));
            Assert.Contains("trigger catch", link.Label);
            Assert.Equal("(remove catch)", link.Remove);
        }
    }
}
