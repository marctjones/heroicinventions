using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #59: sleeping until a condition, by fast-forwarding the simulation. The headless-host and game halves are tested elsewhere.</summary>
public class SleepTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private const double Dt = 1.0 / 120;

    /// <summary>A 500 L cistern with a spring of 2 L/s running into it, empty.</summary>
    private static MachineRuntime Cistern()
    {
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "sleepy");
        s.Execute("(tank cistern #:at (0 0 0) #:area 0.5 #:height 1)");
        s.Execute("(inflow spring #:into cistern #:flow 0.002)");
        return new MachineRuntime(s.Document.ToMachineDef(), Materials);
    }

    private static WakeSpec Wait(double litres, double limit = 600, bool all = true, params WakeTerm[] events) =>
        new("w", [new WakeTerm("cistern", "water", true, litres)], all, limit, events);

    [Fact]
    public void ATankFilledAtAKnownRateWakesAtVolumeOverRateWithinOneStep()
    {
        var runtime = Cistern();
        var result = SleepSession.FastForward(runtime, Wait(50));        // 50 L at 2 L/s: 25 s
        Assert.Equal(WakeReason.Condition, result.Reason);
        Assert.InRange(result.Elapsed, 25.0 - 1e-9, 25.0 + Dt + 1e-9);
        Assert.Equal(25.0, result.Elapsed, precision: 1);
        Assert.True(runtime.GetField("cistern", "water") >= 50 && runtime.GetField("cistern", "water") < 50 + 2 * Dt * 1.01);
    }

    [Fact]
    public void ASleepWhoseStepsAreTakenByTheCallerWakesOnTheSameStepAsOneThatTakesThem()
    {
        // issue #207: a live sleep lets the game's own loop step the machine and only asks whether it is time to wake (Observe)
        var own = Cistern();
        var theirs = Cistern();
        var plan = Wait(50);
        var result = SleepSession.FastForward(own, plan);                // 50 L at 2 L/s: 25 s
        var session = new SleepSession(theirs, plan);
        while (!session.Done) { theirs.Step(Dt); session.Observe(); }    // the caller's step, then the check
        Assert.Equal(result.Steps, session.Result!.Steps);
        Assert.Equal(result.Elapsed, session.Result.Elapsed, precision: 9);
        Assert.Equal(own.GetField("cistern", "water"), theirs.GetField("cistern", "water"));
        Assert.Equal(WakeReason.Condition, session.Result.Reason);
    }

    [Fact]
    public void ThePredictionByRateMatchesTheWakeAndDoesNotDisturbTheMachine()
    {
        var runtime = Cistern();
        runtime.Step(1.0);                                               // it has been running a second: 2 L
        double timeBefore = runtime.Time, waterBefore = runtime.GetField("cistern", "water");
        var plan = Wait(50);
        var prediction = SleepPlanner.Predict(runtime, plan);
        Assert.NotNull(prediction.Seconds);
        Assert.Equal(24.0, prediction.Seconds!.Value, precision: 1);     // 48 L still to go at 2 L/s
        Assert.Equal(timeBefore, runtime.Time);                          // the real machine was not run
        Assert.Equal(waterBefore, runtime.GetField("cistern", "water"));
        var result = SleepSession.FastForward(runtime, plan);
        Assert.Equal(prediction.Seconds.Value, result.Elapsed, precision: 1);
    }

    [Fact]
    public void ASleepLeavesTheSameStateAsRunningItInRealTime()
    {
        var slept = Cistern();
        var watched = Cistern();
        var result = SleepSession.FastForward(slept, Wait(50));
        for (long i = 0; i < result.Steps; i++) watched.Step(Dt);        // the same number of the same steps, one at a time
        Assert.Equal(watched.Time, slept.Time, precision: 12);
        foreach (var (key, get) in slept.FieldGetters)
            Assert.Equal(watched.FieldGetters[key](), get(), precision: 12);
        Assert.Equal(Dt, new SleepSession(Cistern(), Wait(1)).Dt);       // and it never takes a bigger step than the machine's own
    }

    [Fact]
    public void AConditionThatCannotBeMetStopsAtTheSafetyLimitAndSaysSo()
    {
        var runtime = Cistern();
        var plan = Wait(5000, limit: 60);                                // more than the 500 L the cistern holds
        var prediction = SleepPlanner.Predict(runtime, plan);
        Assert.Contains("beyond the 60", prediction.Note);               // the estimate says it will not make it
        var result = SleepSession.FastForward(runtime, plan);
        Assert.Equal(WakeReason.Limit, result.Reason);
        Assert.Equal(60.0, result.Elapsed, precision: 1);
        Assert.Contains("safety limit", result.ToString());
    }

    [Fact]
    public void ANeverIsFlaggedNeverAtTheCurrentRate()
    {
        var runtime = Cistern();
        runtime.Step(10);                                                // 20 L in
        var falling = new WakeSpec("w", [new WakeTerm("cistern", "water", false, 10)], true, 600, []);   // wait for it to drop below 10 L: it is rising
        var prediction = SleepPlanner.Predict(runtime, falling);
        Assert.Null(prediction.Seconds);
        Assert.Equal("never, at the current rate", prediction.Note);
    }

    [Fact]
    public void AnEventWakesItEarlyAndSaysWhatWokeIt()
    {
        var runtime = Cistern();
        var plan = Wait(50, events: new WakeTerm("cistern", "water", true, 30));   // the cistern reaching 30 L is worth waking for
        var result = SleepSession.FastForward(runtime, plan);
        Assert.Equal(WakeReason.Event, result.Reason);
        Assert.Equal(15.0, result.Elapsed, precision: 1);                // 30 L at 2 L/s
        Assert.Contains("cistern.water", result.Detail);
        Assert.Contains("woken early", result.ToString());
    }

    [Fact]
    public void AndTakesTheLaterTermAndOrTheEarlier()
    {
        WakeTerm at(double litres) => new("cistern", "water", true, litres);
        var both = SleepSession.FastForward(Cistern(), new WakeSpec("w", [at(20), at(40)], All: true, 600, []));
        var either = SleepSession.FastForward(Cistern(), new WakeSpec("w", [at(20), at(40)], All: false, 600, []));
        Assert.Equal(20.0, both.Elapsed, precision: 1);                  // both true at 40 L
        Assert.Equal(10.0, either.Elapsed, precision: 1);                // one true at 20 L
        var runtime = Cistern();
        var and = SleepPlanner.Predict(runtime, new WakeSpec("w", [at(20), at(40)], true, 600, []));
        var or = SleepPlanner.Predict(runtime, new WakeSpec("w", [at(20), at(40)], false, 600, []));
        Assert.Equal(20.0, and.Seconds!.Value, precision: 1);
        Assert.Equal(10.0, or.Seconds!.Value, precision: 1);
    }

    [Fact]
    public void AConditionAlreadyTrueWakesAtOnceAndProgressRunsToOne()
    {
        var runtime = Cistern();
        var done = new SleepSession(runtime, Wait(0));
        Assert.True(done.Done);
        Assert.Equal(0, done.Result!.Elapsed);

        var s = new SleepSession(runtime, Wait(50), predicted: 25);
        double last = -1;
        while (!s.Done) { s.Run(300); Assert.True(s.Progress >= last); last = s.Progress; }
        Assert.Equal(1.0, s.Progress);
        Assert.Equal(0, new SleepSession(Cistern(), Wait(50)).Steps);
    }
}
