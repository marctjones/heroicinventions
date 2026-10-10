using HeroicInventions.Sim.Game;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Routes to the win (#227): route data (#228), the reveal rule (#229), the gate (#231) and the playtest record (#233).
///
/// PREDICTED BEFORE THE FIRST RUN (route order: windmill, gear-up, cut-in, wired, warm, full; "next" = first step never met, "after" = the
/// second, "last" = highest-numbered step ever met; shown in route order):
///
/// (a) The automated route (docs/e2e-route.md; the save carries the built windmill, its 256:1 train and both links, so three steps are met at
///     t=0; the bank warms at 26,857 s and fills at 32,062 s, both with the Jolt rotor still at 0 rpm because the paused sleep does not turn
///     it; the sleep wakes at 54,800 s with the rotor free-running at 3,000 rpm; the call at 55,484 s):
///       t=0, 10,000   cut-in NEXT, wired MET, warm AFTER             (windmill and gear-up are met but older than 'last')
///       t=26,900      cut-in NEXT, warm MET, full AFTER
///       t=32,100      cut-in NEXT, full MET                          (counter-intuitive: the last step is shown before the cut-in)
///       t=54,800      full MET                                       (every step met)
///       t=55,490      full MET, and the final goal met
/// (b) A failed build (a windmill with the generator on its own shaft, no gear train: ratio 1, rotor 12 rpm, wired):
///       t=0           nothing built: no route shown (the final goal only)
///       t=100         gear-up NEXT, cut-in AFTER, wired MET          (started by the windmill and the wire)
///       t=26,900      gear-up NEXT, cut-in AFTER, warm MET
///       t=32,100      gear-up NEXT, cut-in AFTER, full MET
///       t=55,490      the same: it never wins, the final goal stays unmet
/// (d) #251, a windmill and a built 256:1 train that drive nothing (no generator joined; reading-level, bank at -63 C), then the same train read
///     from a real runtime (found-electrics with its motor taken out: the 125:1 train, a bank at 20 C):
///       reading, t=0      windmill and gear-up met, nothing else: gear-up MET, cut-in NEXT, wired AFTER   (the gear-up step is no longer 'next' for want of a generator)
///       real, t=0         the bank is warm at the start, so warm is the last met:  cut-in NEXT, wired AFTER, warm MET
///     The gear-up reason prints 'yours is not there yet.' with no train, 'yours is 40:1.' for a 40:1 generator train, 'yours is 256:1.' for 256:1
///     (the ':1' belongs to the number, so the fallback has none). A joined generator's own ratio wins over the built train's: a 1:1 generator
///     beside a 256:1 train reads 1:1 and the step stays unmet.
/// (c) A vault warming a bank with no windmill anywhere starts the route through the shared 'warm' step (rule 2 as the owner wrote it):
///       warm MET, windmill NEXT, gear-up AFTER.
/// </summary>
public class RoutesTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static string RoutesDir => Path.Combine(AppContext.BaseDirectory, "routes");

    private static IReadOnlyList<Route> Shipped() =>
        RouteLibrary.Parse(Directory.GetFiles(RoutesDir, "*.route", SearchOption.AllDirectories).Order().Select(f => (f, File.ReadAllText(f))));
    private static Route Windmill() => Shipped().Single(r => r.Id == "windmill");

    // ---- readings built from the sim's field names ----
    private static GeneratorReading Gen(double rpm, double? ratio, bool wired = true, double? watts = null) =>
        new("motor", rpm, 1500, wired, ratio, watts is { } w ? () => w : null);
    private static BankReading Bank(double c, bool full = false, double capacityWh = 25) => new("bank", c, full, capacityWh, full ? capacityWh : 0);
    private static RouteReading R(int windmills = 0, GeneratorReading? gen = null, BankReading? bank = null, bool called = false) =>
        new(windmills, gen is null ? [] : [gen], bank is null ? [] : [bank], called);

    private static string Shown(RouteView v, string route = "windmill") =>
        string.Join(" ", v.Routes.Where(r => r.Id == route).SelectMany(r => r.Steps).Select(s => $"{s.StepId}:{s.Status.ToString().ToUpperInvariant()}"));

    // ---- #228: route data ----

    [Fact]
    public void TheWindmillRouteLoadsWithItsStepsInOrderAndNamesItsProof()
    {
        var route = Windmill();
        Assert.Equal("The windmiller", route.Name);
        Assert.Equal(["windmill", "gear-up", "cut-in", "wired", "warm", "full"], route.Steps.Select(s => s.Id));
        Assert.All(route.Steps, s => { Assert.False(string.IsNullOrWhiteSpace(s.Title)); Assert.NotEmpty(s.Alternatives); });
        Assert.Equal("racket/heroic/tests/e2e-route-test.rkt", route.ProvenBy);
        Assert.Equal(100, route.Steps[1].Alternatives.Single().Args.Single());
        Assert.Equal([0.0, 45.0], route.Steps[4].Alternatives.Single().Args);
    }

    [Fact]
    public void EachStepFlipsFromUnmetToMetWhenItsStateIsSetUp()
    {
        var route = Windmill();
        // the state that meets each step, and the state one notch short of it
        var cases = new (string Step, RouteReading Short, RouteReading Met)[]
        {
            ("windmill", R(0), R(1)),
            ("gear-up", R(1, Gen(0, 99.9)), R(1, Gen(0, 100))),                 // 100:1 is the least
            ("cut-in", R(1, Gen(1500, 256)), R(1, Gen(1500.5, 256))),           // over, not at, the cut-in
            ("wired", R(1, Gen(0, 256, wired: false)), R(1, Gen(0, 256, wired: true))),
            ("warm", R(bank: Bank(-0.1)), R(bank: Bank(0))),
            ("warm", R(bank: Bank(45.1)), R(bank: Bank(45))),
            ("full", R(bank: Bank(20)), R(bank: Bank(20, full: true))),
        };
        foreach (var (step, notYet, met) in cases)
        {
            var s = route.Steps.Single(x => x.Id == step);
            Assert.False(s.IsMet(notYet), $"{step} should be unmet short of its state");
            Assert.True(s.IsMet(met), $"{step} should be met in its state");
        }
        Assert.False(route.Steps[1].IsMet(R(1, Gen(0, null))), "a generator with no prime mover has no train");
    }

    [Fact]
    public void AStepWithAlternativesIsMetByAnyOneOfThem()
    {
        var route = Route.Parse("""
            (route two (name "Two") (description "d") (proven-by "x")
              (step spin (title "Spin") (reason "r") (met (rotor-over-cut-in) (windmill-exists))))
            """, "two.route");
        var step = route.Steps.Single();
        Assert.False(step.IsMet(R()));
        Assert.True(step.IsMet(R(1)));
        Assert.True(step.IsMet(R(0, Gen(2000, 1))));
    }

    [Theory]
    [InlineData("(route r (name \"N\") (description \"d\") (proven-by \"x\") (step s (title \"T\") (reason \"r\") (met (spins-fast))))", "step s", "no condition named spins-fast")]
    [InlineData("(route r (name \"N\") (description \"d\") (proven-by \"x\") (step s (title \"T\") (reason \"r\") (met (train-ratio-at-least))))", "step s", "takes 1 number")]
    [InlineData("(route r (name \"N\") (description \"d\") (proven-by \"x\") (step s (title \"T\") (reason \"r\")))", "step s", "needs (met")]
    [InlineData("(route r (name \"N\") (description \"d\") (proven-by \"x\") (step s (title \"T\") (reason \"{wind-speed}\") (met (bank-full))))", "step s", "wind-speed")]
    [InlineData("(route r (name \"N\") (description \"d\") (proven-by \"x\") (step s (reason \"r\") (met (bank-full))))", "step s", "needs (title")]
    [InlineData("(route r (name \"N\") (description \"d\") (proven-by \"x\") (step s (title \"T\") (reason \"r\") (met (bank-full))) (step s (title \"T\") (reason \"r\") (met (bank-full))))", "step s", "used twice")]
    [InlineData("(route r (name \"N\") (description \"d\") (step s (title \"T\") (reason \"r\") (met (bank-full))))", "route r", "proven-by")]
    [InlineData("(route r (name \"N\") (description \"d\") (proven-by \"x\") (step the-call (title \"T\") (reason \"r\") (met (called))))", "step the-call", "final goal")]
    [InlineData("(route r (name \"N\") (description \"d\") (proven-by \"x\") (step s (title \"T\") (reason \"r\") (met (bank-temperature-between 45 0))))", "step s", "LO under HI")]
    [InlineData("(route r (name \"N\"", "", "never closed")]
    public void AMalformedRouteFailsLoudlyNamingTheFileAndTheStep(string text, string where, string why)
    {
        var e = Assert.Throws<MachineFormatException>(() => Route.Parse(text, "games/bad.route"));
        Assert.Contains("games/bad.route", e.Message);
        Assert.Contains(where, e.Message);
        Assert.Contains(why, e.Message);
    }

    [Fact]
    public void TwoFilesNamingTheSameRouteAreRefused()
    {
        const string text = "(route r (name \"N\") (description \"d\") (proven-by \"x\") (step s (title \"T\") (reason \"r\") (met (bank-full))))";
        var e = Assert.Throws<MachineFormatException>(() => RouteLibrary.Parse([("a.route", text), ("b.route", text)]));
        Assert.Contains("b.route", e.Message);
    }

    [Fact]
    public void EveryConditionKindSaysWhatItReads()
    {
        Assert.All(RouteCondition.Kinds, k => Assert.False(string.IsNullOrWhiteSpace(k.Value.Reads), k.Key));
    }

    // ---- #229: the reveal rule, table-driven over sequences of states ----

    private sealed record Row(double T, RouteReading Reading, string Expect, bool FinalMet = false);

    private static List<RouteView> Play(RouteTracker tracker, IEnumerable<Row> rows, string route = "windmill")
    {
        var views = new List<RouteView>();
        foreach (var row in rows)
        {
            tracker.Update(row.Reading, row.T, 1 + (int)(row.T / 88775));
            var view = tracker.View(row.Reading);
            Assert.Equal(row.Expect, Shown(view, route));
            Assert.Equal(row.FinalMet, view.Final.Met);
            views.Add(view);
        }
        return views;
    }

    private static RouteTracker TrackerOf(params Route[] routes) { var t = new RouteTracker(); t.Load(routes); return t; }

    private static Row[] AutomatedRoute() =>
    [
        new(0,      R(1, Gen(0, 256), Bank(-63)), "cut-in:NEXT wired:MET warm:AFTER"),
        new(10000,  R(1, Gen(0, 256), Bank(-30)), "cut-in:NEXT wired:MET warm:AFTER"),
        new(26900,  R(1, Gen(0, 256), Bank(0.1)), "cut-in:NEXT warm:MET full:AFTER"),
        new(32100,  R(1, Gen(0, 256), Bank(8.9, full: true)), "cut-in:NEXT full:MET"),
        new(54800,  R(1, Gen(3000, 256), Bank(24.5, full: true)), "full:MET"),
        new(55490,  R(1, Gen(3000, 256), Bank(24.6, full: true), called: true), "full:MET", FinalMet: true),
    ];

    private static Row[] FailedBuild() =>
    [
        new(0,      R(0, null, Bank(-63)), ""),
        new(100,    R(1, Gen(12, 1), Bank(-63)), "gear-up:NEXT cut-in:AFTER wired:MET"),
        new(26900,  R(1, Gen(12, 1), Bank(0.1)), "gear-up:NEXT cut-in:AFTER warm:MET"),
        new(32100,  R(1, Gen(12, 1), Bank(8.9, full: true)), "gear-up:NEXT cut-in:AFTER full:MET"),
        new(55490,  R(1, Gen(12, 1), Bank(24.6, full: true)), "gear-up:NEXT cut-in:AFTER full:MET"),
    ];

    [Fact]
    public void TheAutomatedRoutesTraceRevealsExactlyTheStepsPredicted()
    {
        var views = Play(TrackerOf(Windmill()), AutomatedRoute());
        Assert.Equal("The windmiller", Assert.Single(views[0].Routes).Name);
    }

    [Fact]
    public void AFailedBuildNeverMovesPastItsGearTrainAndNeverWins()
    {
        Play(TrackerOf(Windmill()), FailedBuild());
    }

    [Fact]
    public void BeforeAnythingIsDoneTheRouteIsHiddenAndOnlyTheFinalGoalShows()
    {
        var t = TrackerOf(Windmill());
        var view = t.View(R(0, null, Bank(-63)));
        Assert.Empty(view.Routes);
        Assert.False(view.Final.Met);
        Assert.Equal("Call Earth with a full, warm bank", view.Final.Title);
        Assert.Equal("Call Earth with a full, warm bank", TrackerOf().View(RouteReading.Empty).Final.Title);   // with no routes at all
    }

    [Fact]
    public void AVaultWarmingAndFillingTheBankDoesNotStartOrNameTheRoute()
    {
        var t = TrackerOf(Windmill());
        var log = new List<RouteEvent>();
        foreach (var r in new[] { R(0, null, Bank(5)), R(0, null, Bank(5, full: true)) })
        {
            log.AddRange(t.Update(r, 26900, 1));
            Assert.Empty(t.View(r).Routes);
        }
        Assert.Empty(log);                                              // the rover log names nothing either
        Assert.True(t.History.HasMet("windmill", "warm"));              // but the shared steps count as met, and the playtest keeps their time
        Assert.Equal(-1, t.TraceFields()["route.windmill.started"]());
    }

    [Fact]
    public void ASharedStepMetBeforeAStartedStepShowsAndCountsOnceTheRouteIsStarted()
    {
        var t = TrackerOf(Windmill());
        t.Update(R(0, null, Bank(5, full: true)), 100, 1);              // warm and full, quietly
        Play(t, [new(200, R(1, null, Bank(5, full: true)), "gear-up:NEXT cut-in:AFTER full:MET")]);   // the windmill starts it: last met = full, next = gear-up
        Assert.Equal(100, t.History.MetAt("windmill", "full")!.Value.Time);
        Assert.Equal(200, t.History.StartedAt("windmill")!.Value.Time);
    }

    [Fact]
    public void ARouteOfOnlySharedStepsIsRefused()
    {
        var e = Assert.Throws<MachineFormatException>(() => Route.Parse("(route r (name \"N\") (description \"d\") (proven-by \"x\") (step s (title \"T\") (reason \"r\") (shared #t) (met (bank-full))))", "s.route"));
        Assert.Contains("only shared", e.Message);
    }

    [Fact]
    public void AMetStepStaysMetWhenTheStateUndoesItAndShowsAsDoneEarlier()
    {
        var t = TrackerOf(Windmill());
        // windmill, gear-up, wired and warm are met at once: next = cut-in, after = full, last = warm
        Play(t, [new(0, R(1, Gen(0, 256), Bank(5)), "cut-in:NEXT warm:MET full:AFTER")]);
        // the windmill is dismantled and the night freezes the bank: nothing is un-met, and the warm step reads 'done earlier'
        var undone = R(0, null, Bank(-40));
        t.Update(undone, 100, 1);
        var view = t.View(undone);
        Assert.Equal("cut-in:NEXT warm:DONEEARLIER full:AFTER", Shown(view));
        Assert.Equal(StepStatus.DoneEarlier, view.Routes.Single().Steps.Single(s => s.StepId == "warm").Status);
        Assert.True(t.History.HasMet("windmill", "windmill"));
    }

    [Fact]
    public void AnUnstartedRouteIsNeverInTheOutputOrNamedInAnyText()
    {
        var hotAir = Route.Parse("""
            (route hot-air (name "Hot air engine") (description "A Stirling engine on mirror heat.") (proven-by "x")
              (step engine (title "A hot-air engine") (reason "It needs {bank-wh|?} Wh from the engine.") (met (train-ratio-at-least 5000)))
              (step hot (title "Heat the rock") (reason "r") (met (bank-temperature-between 100 200))))
            """, "hot-air.route");
        foreach (var rows in new[] { AutomatedRoute(), FailedBuild() })
        {
            var t = TrackerOf(Windmill(), hotAir);
            foreach (var row in rows)
            {
                t.Update(row.Reading, row.T, 1);
                var view = t.View(row.Reading);
                Assert.DoesNotContain(view.Routes, r => r.Id == "hot-air");
                string all = string.Join("\n", view.Routes.SelectMany(r => r.Steps.Select(s => $"{r.Name} {r.Description} {s.Title} {s.Reason}")))
                             + view.Final.Title + view.Final.Reason;
                Assert.DoesNotContain("Hot air", all);
                Assert.DoesNotContain("Stirling", all);
                Assert.DoesNotContain("Heat the rock", all);
            }
        }
        // the moment one of its steps is met it is started and shows
        var t2 = TrackerOf(Windmill(), hotAir);
        t2.Update(R(0, null, Bank(150)), 5, 1);
        Assert.Contains(t2.View(R()).Routes, r => r.Id == "hot-air");
    }

    [Fact]
    public void AReasonCarriesNumbersFilledFromState()
    {
        var now = R(1, Gen(12, 1, watts: 90), Bank(5, capacityWh: 500));
        string Reason(string step) => Windmill().Steps.Single(s => s.Id == step).ReasonFor(now);
        Assert.Contains("500 Wh", Reason("full"));
        Assert.Contains("about 90 W", Reason("full"));
        Assert.Contains("12 rpm", Reason("cut-in"));
        Assert.Contains("1,500", Reason("cut-in"));
        Assert.Contains("yours is 1:1", Reason("gear-up"));
        Assert.Contains("5 °C", Reason("warm"));
        // with nothing built the fallbacks read, not a blank or a stray brace
        string bare = Windmill().Steps.Single(s => s.Id == "full").ReasonFor(RouteReading.Empty);
        Assert.DoesNotContain("{", bare);
        Assert.Contains("about some W", bare);
    }

    // ---- #233: the playtest record ----

    [Fact]
    public void EachFirstTimeIsRecordedOnceWithTheSceneClockAndExposedAsTraceFields()
    {
        var t = TrackerOf(Windmill());
        var trace = t.TraceFields();
        Assert.Equal(-1, trace["route.windmill.started"]());
        Assert.Equal(-1, trace["route.windmill.full"]());
        var log = new List<RouteEvent>();
        foreach (var row in AutomatedRoute()) log.AddRange(t.Update(row.Reading, row.T, 1));
        foreach (var row in AutomatedRoute()) Assert.Empty(t.Update(row.Reading, row.T + 100000, 2));   // never twice
        Assert.Equal(
            [("windmill", 0.0), ("gear-up", 0.0), ("wired", 0.0), ("warm", 26900.0), ("full", 32100.0), ("cut-in", 54800.0), ("the-call", 55490.0)],
            log.Where(e => e.StepId is not null).Select(e => (e.StepId!, e.Time)));
        var started = Assert.Single(log, e => e.StepId is null);
        Assert.Equal((0.0, "windmill"), (started.Time, started.RouteId));
        Assert.Equal(log.IndexOf(started) + 1, log.FindIndex(e => e.StepId == "windmill"));   // the route's start is logged before its first step
        Assert.Equal(0, trace["route.windmill.started"]());
        Assert.Equal(26900, trace["route.windmill.warm"]());
        Assert.Equal(54800, trace["route.windmill.cut-in"]());
        Assert.Equal(55490, trace["route.final.the-call"]());
        Assert.All(log, e => Assert.False(string.IsNullOrWhiteSpace(e.Text)));
    }

    [Fact]
    public void ARestartedRunStartsTheHistoryOver()
    {
        var t = TrackerOf(Windmill());
        t.Update(R(1), 5, 1);
        Assert.NotEqual(0, t.History.Count);
        t.RestartRun();
        Assert.Equal(0, t.History.Count);
        Assert.Empty(t.View(R()).Routes);
    }

    // ---- the history persists in the world save ----

    [Fact]
    public void TheHistoryRoundTripsThroughTheWorldSaveAndAnOlderSaveLoadsWithNone()
    {
        var t = TrackerOf(Windmill());
        foreach (var row in AutomatedRoute().Take(4)) t.Update(row.Reading, row.T, 1 + (int)(row.T / 88775));
        var save = new WorldSave { Kind = "world", Name = "w", Machines = [], Goals = new GoalTracker().ToForm(), Routes = t.ToForm() };
        string text = save.ToText();
        Assert.Contains("(routes 1", text);
        var back = WorldSave.Parse(text);
        Assert.NotNull(back.Routes);
        Assert.NotNull(back.Goals);

        var again = TrackerOf(Windmill());
        again.Restore(back.Routes!);
        foreach (var step in Windmill().Steps)
            Assert.Equal(t.History.MetAt("windmill", step.Id), again.History.MetAt("windmill", step.Id));
        Assert.Equal(t.History.StartedAt("windmill"), again.History.StartedAt("windmill"));
        Assert.Equal(32100, again.History.MetAt("windmill", "full")!.Value.Time);
        Assert.Equal(1, again.History.MetAt("windmill", "full")!.Value.Sol);
        // restored, the state is gone but the steps stay met: the bank is cold and the windmill dismantled
        Assert.Equal("cut-in:NEXT full:DONEEARLIER", Shown(again.View(R(0, null, Bank(-60)))));

        // an older save has no (routes ...) at all, and loads as before
        string older = new WorldSave { Kind = "world", Name = "w", Machines = [], Goals = new GoalTracker().ToForm() }.ToText();
        Assert.DoesNotContain("(routes", older);
        var old = WorldSave.Parse(older);
        Assert.Null(old.Routes);
        Assert.NotNull(old.Goals);
        var fresh = TrackerOf(Windmill());
        if (old.Routes is { } r) fresh.Restore(r);
        Assert.Empty(fresh.View(R()).Routes);
    }

    // ---- the reading is the sim's own fields ----

    [Fact]
    public void TheReadingOfARealRuntimeSeesTheWindmillItsTrainAndItsWire()
    {
        var def = MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "found-electrics.machine")));
        var rt = new MachineRuntime(def, Materials);
        var reading = RouteReading.Read([rt]);
        Assert.Equal(1, reading.Windmills);
        var gen = Assert.Single(reading.Generators);
        Assert.True(gen.Wired);
        Assert.Equal(125, gen.Ratio!.Value, 6);                      // three 100:20 meshes (GeneratorPrimeLinkTests)
        Assert.Equal(1500, gen.CutInRpm);
        var t = TrackerOf(Windmill());
        t.Update(reading, rt.Time, 1);
        var steps = t.View(reading).Routes.Single().Steps;
        Assert.Contains(steps, s => s.StepId == "cut-in" && s.Status == StepStatus.Next);     // the sails have not turned the rotor yet
        Assert.False(t.View(reading).Final.Met);
        Assert.True(t.History.HasMet("windmill", "windmill") && t.History.HasMet("windmill", "gear-up") && t.History.HasMet("windmill", "wired"));
        Assert.False(t.History.HasMet("windmill", "cut-in"));
    }

    // ---- #231: only routes shown to win may be shipped ----

    private static string? RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "racket", "heroic", "tests"))) return d.FullName;
        return null;
    }

    /// <summary>The routes whose proof is not a file in the repository.</summary>
    private static IEnumerable<string> MissingProofs(IEnumerable<Route> routes, string root) =>
        routes.Where(r => !File.Exists(Path.Combine(root, r.ProvenBy))).Select(r => $"route {r.Id} names {r.ProvenBy}");

    [Fact]
    public void EveryShippedRouteNamesATestThatExists()
    {
        string root = RepoRoot() ?? throw new InvalidOperationException("the repository's racket/heroic/tests was not found above " + AppContext.BaseDirectory);
        var routes = Shipped();
        Assert.NotEmpty(routes);
        Assert.Empty(MissingProofs(routes, root));
        Assert.Contains(routes, r => r.Id == "windmill" && r.ProvenBy == "racket/heroic/tests/e2e-route-test.rkt");
    }

    [Fact]
    public void ARouteNamingAMissingTestIsCaught()
    {
        string root = RepoRoot()!;
        var bad = Route.Parse("(route r (name \"N\") (description \"d\") (proven-by \"racket/heroic/tests/no-such-test.rkt\") (step s (title \"T\") (reason \"r\") (met (bank-full))))", "bad.route");
        Assert.Single(MissingProofs([bad], root));
    }

    // ---- #251: the gear-up step reads the built train when no generator is joined, and its reason has no stray ':1' ----

    private static RouteReading Built(double ratio, params GeneratorReading[] gens) => new(1, gens, [Bank(-63)], false, [ratio]);

    [Fact]
    public void ABuiltTrainWithNoGeneratorMeetsGearUpAndTheCutInStepIsNext()
    {
        var t = TrackerOf(Windmill());
        Play(t, [new(0, Built(256), "gear-up:MET cut-in:NEXT wired:AFTER")]);
        Assert.True(t.History.HasMet("windmill", "gear-up"));
        Assert.False(t.History.HasMet("windmill", "wired"));
    }

    [Fact]
    public void AShortBuiltTrainLeavesGearUpNextWithItsRatioInTheReason()
    {
        var t = TrackerOf(Windmill());
        var views = Play(t, [new(0, Built(40), "windmill:MET gear-up:NEXT cut-in:AFTER")]);
        Assert.Contains("yours is 40:1.", views[0].Routes.Single().Steps.Single(s => s.StepId == "gear-up").Reason);
    }

    [Fact]
    public void AJoinedGeneratorsOwnRatioWinsOverTheBuiltTrain()
    {
        var t = TrackerOf(Windmill());
        Play(t, [new(0, Built(256, Gen(12, 1)), "gear-up:NEXT cut-in:AFTER wired:MET")]);
    }

    [Fact]
    public void TheGearUpReasonHasNoColonOneAfterItsFallback()
    {
        var step = Windmill().Steps.Single(s => s.Id == "gear-up");
        Assert.EndsWith("yours is not there yet.", step.ReasonFor(R(1)));
        Assert.DoesNotContain("yet:1", step.ReasonFor(R(1)));
        Assert.EndsWith("yours is 40:1.", step.ReasonFor(R(1, Gen(0, 40))));
        Assert.EndsWith("yours is 256:1.", step.ReasonFor(Built(256)));
        Assert.True(step.IsMet(Built(256)));
        Assert.False(step.IsMet(R(1)));
    }

    [Fact]
    public void ARealWindmillAndTrainWithTheMotorTakenOutReadsItsBuiltRatio()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "found-electrics.machine"));
        var lines = text.Split('\n').Where(l => !l.TrimStart().StartsWith("(part motor generator")).ToList();
        var rt = new MachineRuntime(MachineDef.Parse(string.Join('\n', lines)), Materials);
        var reading = RouteReading.Read([rt]);
        Assert.Empty(reading.Generators);
        Assert.Equal(125, Assert.Single(reading.BuiltTrains!), 6);                // three 100:20 meshes, from the teeth
        var t = TrackerOf(Windmill());
        t.Update(reading, rt.Time, 1);
        Assert.Equal("cut-in:NEXT wired:AFTER warm:MET", Shown(t.View(reading)));
    }
}
