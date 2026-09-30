using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Wake conditions in a machine file (issue #59): parsing, checking, the editor, and sleeping on them. The sleep engine is in SleepTests.</summary>
public class WakePartTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static MachineDef WakeClock() =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "wake-clock.machine")));

    [Fact]
    public void AMachineFileOffersItsWakeConditionsAndTheyRoundTrip()
    {
        var def = WakeClock();
        Assert.Equal(["filled", "both", "either", "guarded", "never"], def.Wakes.Select(w => w.Id));
        var both = def.Wakes.Single(w => w.Id == "both");
        Assert.True(both.All);
        Assert.Equal(2, both.Terms.Count);
        Assert.False(def.Wakes.Single(w => w.Id == "either").All);
        var guarded = def.Wakes.Single(w => w.Id == "guarded");
        Assert.Equal(new WakeTerm("cistern", "water", true, 30), Assert.Single(guarded.Events));
        Assert.Equal(60, def.Wakes.Single(w => w.Id == "never").Limit);

        var reparsed = MachineDef.Parse(MachineWriter.Write(def));
        Assert.Equal(def.Wakes.Select(w => (w.Id, w.All, w.Limit, w.Terms.Count, w.Events.Count)), reparsed.Wakes.Select(w => (w.Id, w.All, w.Limit, w.Terms.Count, w.Events.Count)));
    }

    [Fact]
    public void TheRuntimeSleepsOnANamedWake()
    {
        var runtime = new MachineRuntime(WakeClock(), Materials);
        Assert.Equal(5, runtime.Wakes.Count);
        var result = SleepSession.FastForward(runtime, runtime.Wakes["filled"]);
        Assert.Equal(25.0, result.Elapsed, precision: 1);
        Assert.Equal(WakeReason.Event, SleepSession.FastForward(new MachineRuntime(WakeClock(), Materials), runtime.Wakes["guarded"]).Reason);
    }

    [Fact]
    public void ABadWakeIsReportedAtTheClause()
    {
        var def = WakeClock();
        string Message(WakeSpec bad) => Assert.Throws<MachineFormatException>(() => new MachineRuntime(
            new MachineDef { Name = def.Name, Parts = def.Parts, Pipes = [], Connects = [], SealedAir = [], Sources = def.Sources, Wakes = [bad] }, Materials)).Message;
        Assert.Contains("not a readable field", Message(new WakeSpec("w", [new WakeTerm("cistern", "colour", true, 1)], true, 60, [])));
        Assert.Contains("not a readable field", Message(new WakeSpec("w", [new WakeTerm("cistern", "water", true, 1)], true, 60, [new WakeTerm("nowhere", "water", true, 1)])));
        Assert.Contains("waits for nothing", Message(new WakeSpec("w", [], true, 60, [])));
        Assert.Contains("cannot sleep for ever", Message(new WakeSpec("w", [new WakeTerm("cistern", "water", true, 1)], true, 0, [])));
    }

    [Fact]
    public void TheEditorMakesWakesAndTheyGoWhenWhatTheyWatchGoes()
    {
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        foreach (string line in CommandScript.For(WakeClock())) s.Execute(line);
        Assert.Equal(5, s.Document.Wakes.Count);
        s.Execute("(wake full #:when ((cistern water above 400)) #:join or #:limit 900 #:events ((cistern frozen-solid above 0.5)))");
        var full = s.Document.Wakes.Single(w => w.Id == "full");
        Assert.False(full.All);
        Assert.Equal(900, full.Limit);
        Assert.Contains("wake full", RktExporter.Write(s.Document.ToMachineDef()));
        Assert.StartsWith("ok:", s.Execute("(check)"));
        Assert.Throws<InvalidOperationException>(() => s.Execute("(wake full #:when ((cistern water above 1)))"));

        s.Execute("(remove full)");
        Assert.DoesNotContain(s.Document.Wakes, w => w.Id == "full");
        s.Execute("(remove cistern)");                                            // the tank they all watch
        Assert.Empty(s.Document.Wakes);
    }
}
