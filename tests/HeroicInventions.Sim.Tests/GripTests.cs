using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #55: hooks and tongs. The Jolt-side carrying and dropping (crate-tongs) is in heroic/tests/machine-behavior-test.rkt.</summary>
public class GripTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static BuildSession Fresh() => new(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");

    [Fact]
    public void TongsCarryTwoMuNAndHoldsLoadsUpToThat()
    {
        // bronze jaws (mu 0.30) on iron (0.40): the poorer surface rules
        var tongs = new Grip("t", 0.15, tongs: true, force: 100, strength: 0, mu: 0.30) { LoadMu = 0.40 };
        Assert.Equal(60, tongs.Capacity, precision: 9);                  // 2 x 0.30 x 100
        double light = Grip.LoadOf(5.07, 9.81), heavy = Grip.LoadOf(7.47, 9.81);
        Assert.True(light < tongs.Capacity);                             // 49.7 N: holds
        Assert.True(heavy > tongs.Capacity);                             // 73.3 N: slips
        tongs.Force = 140;
        Assert.Equal(84, tongs.Capacity, precision: 9);
        Assert.True(heavy < tongs.Capacity);                             // the firmer tongs hold it
    }

    [Fact]
    public void ALoadOfMassMNeedsMgOverTwoMuOfGrip()
    {
        Assert.Equal(7.47 * 9.81 / 0.6, Grip.ForceToHold(7.47, 9.81, 0.30), precision: 9);   // 122.1 N
        Assert.Equal(122.1, Grip.ForceToHold(7.47, 9.81, 0.30), precision: 1);
        // exactly that force just holds it
        var tongs = new Grip("t", 0.15, true, Grip.ForceToHold(7.47, 9.81, 0.30), 0, 0.30) { LoadMu = 0.4 };
        Assert.Equal(Grip.LoadOf(7.47, 9.81), tongs.Capacity, precision: 9);
    }

    [Fact]
    public void OnMarsTheSameTongsHold2Point6TimesTheMass()
    {
        const double earth = 9.81, mars = 3.71;
        var tongs = new Grip("t", 0.15, true, 100, 0, 0.30) { LoadMu = 0.4 };
        double heaviestOnEarth = tongs.Capacity / earth, heaviestOnMars = tongs.Capacity / mars;
        Assert.Equal(6.116, heaviestOnEarth, precision: 3);              // 60 N / 9.81
        Assert.Equal(earth / mars, heaviestOnMars / heaviestOnEarth, precision: 12);
        Assert.Equal(2.64, heaviestOnMars / heaviestOnEarth, precision: 2);
        // and it takes 2.64 times less force to hold the same crate
        Assert.Equal(earth / mars, Grip.ForceToHold(5, earth, 0.3) / Grip.ForceToHold(5, mars, 0.3), precision: 12);
    }

    [Fact]
    public void AHookCarriesItsStrengthAndAcceleratingUpAddsToTheLoad()
    {
        var hook = new Grip("h", 0.1, tongs: false, force: 0, strength: 500, mu: 0.3);
        Assert.Equal(500, hook.Capacity);
        Assert.Equal(50 * 9.81, Grip.LoadOf(50, 9.81), precision: 9);                       // 50 kg hanging still
        Assert.Equal(50 * (9.81 + 2), Grip.LoadOf(50, 9.81, upwardAcceleration: 2), precision: 9);   // hoisted at 2 m/s2
        Assert.True(Grip.LoadOf(50, 9.81) < hook.Capacity && Grip.LoadOf(50, 9.81, 2) > hook.Capacity);   // held still, torn off hoisting
    }

    [Fact]
    public void TheRuntimeOffersTheGripsFieldsAndOpensAndClosesIt()
    {
        var s = Fresh();
        s.Execute("(block crate #:at (0 1 0) #:size 0.1 #:material iron)");
        s.Execute("(grip tongs #:at (0 1 0) #:force 100 #:material bronze)");
        var runtime = new MachineRuntime(s.Document.ToMachineDef(), Materials);
        Assert.Equal(0, runtime.GetField("tongs", "closed"));                  // starts open
        var person = OperatorRun.Of(runtime, "(at 0 (tongs closed 1)) (at 0.01 (tongs force 200))");   // a person closes the tongs, then squeezes harder
        Assert.Equal(1, runtime.GetField("tongs", "closed"));
        Assert.Equal(0, runtime.GetField("tongs", "held"));                    // nothing is held without the engine's bodies
        Assert.Equal(0.3 * 2 * 100, runtime.GetField("tongs", "capacity"), precision: 9);   // its own jaws' friction until it meets a load
        person.Run(0.01, 0.01);
        Assert.Equal(120, runtime.GetField("tongs", "capacity"), precision: 9);
    }

    [Fact]
    public void ABadGripIsReportedAtTheClause()
    {
        string Check(string grip)
        {
            var s = Fresh();
            s.Execute("(block crate #:at (0 1 0))");
            s.Execute("(tank pot #:at (3 0 0))");
            s.Execute(grip);
            return s.Execute("(check)");
        }
        Assert.Contains("not a body it can hang from", Check("(grip g #:at (0 1 0) #:on pot #:force 100)"));
        Assert.Contains("#:kind is tongs or hook", Check("(grip g #:at (0 1 0) #:force 100)".Replace("#:at", "#:kind rope #:at")));
        Assert.Contains("need a #:force above 0", Check("(grip g #:at (0 1 0) #:force 0)"));
        Assert.StartsWith("ok:", Check("(grip g #:at (0 1 0) #:force 100)"));
        Assert.StartsWith("ok:", Check("(grip g #:at (0 1 0) #:on crate #:force 100)"));
    }

    [Fact]
    public void TheEditorSetsAGripsKindAndWhatItHangsOn()
    {
        var s = Fresh();
        s.Execute("(block crate #:at (0 1 0))");
        s.Execute("(grip g #:at (0 1 0) #:force 100)");
        s.Execute("(set g #:kind hook)");
        s.Execute("(set g #:strength 300)");
        s.Execute("(set g #:on crate)");
        s.Execute("(set g #:on world)");                                          // back to hanging on the world
        var g = s.Document.Parts["g"];
        Assert.Equal(("hook", "world"), (g.Symbol("kind", ""), g.Symbol("on", "")));
        Assert.Equal(300, g.Number("strength"));
        Assert.Throws<InvalidOperationException>(() => s.Execute("(set g #:on nowhere)"));
        Assert.Contains("(grip g", RktExporter.Write(s.Document.ToMachineDef()));
        Assert.StartsWith("ok:", s.Execute("(check)"));
    }
}
