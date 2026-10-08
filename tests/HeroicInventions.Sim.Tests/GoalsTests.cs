using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Game;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #68: goals and achievements fire from readable state. Each implemented trigger is run on a machine built for it, against the
/// time or number worked out first (the machines' headers carry the working).
/// </summary>
public class GoalsTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static MachineDef Load(string name) => MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", $"{name}.machine")));
    private static MachineRuntime Build(string name, MachineDef? def = null) => new(def ?? Load(name), Materials);

    /// <summary>Steps one runtime and the tracker, returning what the tracker earned and when (the runtime's clock).</summary>
    private static Dictionary<string, double> Run(GoalTracker goals, MachineRuntime rt, double seconds, double dt, Action<double>? each = null)
    {
        var when = new Dictionary<string, double>();
        for (double t = 0; t < seconds; t += dt)
        {
            rt.Step(dt);
            each?.Invoke(dt);
            foreach (var g in goals.Update([rt], rt.Time)) when[g.Id] = rt.Time;
        }
        return when;
    }

    [Fact]
    public void EveryGoalIsNamedOnceAndSaysExactlyWhatItReads()
    {
        Assert.Equal(GoalTracker.Catalogue.Count, GoalTracker.Catalogue.Select(g => g.Id).Distinct().Count());
        Assert.All(GoalTracker.Catalogue, g => { Assert.False(string.IsNullOrWhiteSpace(g.Trigger)); Assert.False(string.IsNullOrWhiteSpace(g.Story)); });
        Assert.Equal(5, GoalTracker.Catalogue.Count(g => g.Kind == GoalKind.Path));
    }

    // ---- the path ----

    [Fact]
    public void TheBanksCrateIsFoundWhenTheGroundHasSettledAndItsTopIsClearAndFreeWhenHauledTwoMetres()
    {
        var rt = Build("bank-bench");
        var goals = new GoalTracker();
        double cover = 1.0, x = 100;
        bool settled = false;
        goals.BankCrate = () => new CrateReading(cover, 0.5, x, 50);
        goals.GroundSettled = () => settled;
        Assert.Empty(goals.Update([rt], 0));
        cover = -0.1;                                                      // the top is clear, but the rim is still falling
        Assert.Empty(goals.Update([rt], 1));
        settled = true;
        Assert.Equal(["find-bank"], goals.Update([rt], 2).Select(g => g.Id));
        x = 101.9;                                                         // dragged 1.9 m: not yet
        Assert.Empty(goals.Update([rt], 3));
        x = 102.1;
        Assert.Equal(["free-bank"], goals.Update([rt], 4).Select(g => g.Id));
        Assert.Empty(goals.Update([rt], 5));                               // earned once
    }

    [Fact]
    public void HaulingACrateWhileItsTopIsStillUnderGroundDoesNotFreeIt()
    {
        var rt = Build("bank-bench");
        var goals = new GoalTracker();
        double cover = -0.1, x = 0;
        goals.BankCrate = () => new CrateReading(cover, 0.5, x, 0);
        goals.Update([rt], 0);
        Assert.True(goals.Has("find-bank"));
        cover = 0.3; x = 5;
        Assert.Empty(goals.Update([rt], 1));
        cover = -0.1;
        Assert.Equal(["free-bank"], goals.Update([rt], 2).Select(g => g.Id));
    }

    [Fact]
    public void TheWholePathAndItsRoutesOnFoundElectricsFireAtTheirWorkedTimes()
    {
        // found-electrics: a 125:1 windmill train charges a 10 Wh bank in a vault at 20 C. The run starts at 17:00 on Earth's 3,600 s hour:
        // the bank fills in about 143 s of traced charging (here the rotor is turned at 125 x the sails by hand, so a little sooner),
        // the sun sets about 19:00 and rises about 05:00 (keep-warm at the dawn, 12 h in), and the 03:00 pass is 10 h in: the call.
        var rt = Build("found-electrics");
        rt.SetField("scene", "time", 17);
        var gen = rt.Generators["motor"];
        var goals = new GoalTracker();
        var when = Run(goals, rt, 14 * 3600, 5, dt => gen.Step(125 * rt.Windmills["sails"].AngularVelocity, dt));
        Assert.Equal(5, when["find-bank"], 6);                     // a bank built into the scene is found at the first look (after the first 5 s step)
        Assert.Equal(5, when["free-bank"], 6);
        Assert.InRange(when["gear-up"], 5, 40);                    // the rotor passes its cut-in as the sails reach 1.26 rad/s
        Assert.InRange(when["charge-bank"], when["gear-up"], 400); // full (10 Wh) soon after
        Assert.InRange(when["the-call"], 10 * 3600 - 10, 10 * 3600 + 610);   // 03:00 to 03:10
        Assert.InRange(when["keep-warm"], 11.5 * 3600, 13 * 3600); // sunrise, with the bank at 20 C all night
        Assert.True(goals.Has("route-windmiller"));                // all of it wind
        Assert.False(goals.Has("route-hot-air"));
        Assert.True(goals.Has("route-three-sols"));                // 10 h in, inside 4 sols
        Assert.Equal(1, rt.GetField("scene", "won"));
    }

    [Fact]
    public void AColdNightBreaksTheWarmthStreakAndAHalfWatchedNightDoesNotCount()
    {
        var rt = Build("found-electrics");
        rt.SetField("scene", "time", 22);                           // already dark when the tracker first looks: this night is not whole
        var goals = new GoalTracker();
        var when = Run(goals, rt, 9 * 3600, 10);
        Assert.False(when.ContainsKey("keep-warm"));
        // a whole night, but the bank is chilled to -5 C in the middle of it
        var cold = Build("found-electrics");
        cold.SetField("scene", "time", 17);
        var g2 = new GoalTracker();
        var chill = Run(g2, cold, 14 * 3600, 10, _ => { if (cold.Time > 8 * 3600 && cold.Time < 8 * 3600 + 100) cold.SetField("cells", "temperature", -5); });
        Assert.False(chill.ContainsKey("keep-warm"));
    }

    [Fact]
    public void GearUpNeedsAHundredToOneAndASlowTrainDoesNotEarnIt()
    {
        var fast = Build("found-electrics");
        var slow = Build("found-electrics");
        var gf = new GoalTracker(); var gs = new GoalTracker();
        var when = Run(gf, fast, 120, 1, dt => fast.Generators["motor"].Step(125 * fast.Windmills["sails"].AngularVelocity, dt));
        Run(gs, slow, 120, 1, dt => slow.Generators["motor"].Step(90 * slow.Windmills["sails"].AngularVelocity, dt));   // 270 rad/s: over the cut-in, but only 90:1
        Assert.True(when.ContainsKey("gear-up"));
        Assert.True(slow.Generators["motor"].Delivered > 0 || slow.Banks["bank"].Full);       // it did charge the bank
        Assert.False(gs.Has("gear-up"));
        Assert.InRange(slow.Generators["motor"].TrainRatio ?? 0, 89, 91);
        Assert.InRange(fast.Generators["motor"].TrainRatio ?? 0, 124, 126);
    }

    // ---- concepts ----

    [Fact]
    public void TheThermostatAndHarrisonFireWhenAStripHoldsTheBankUnder45BesideARockOver100ForAnHour()
    {
        var rt = Build("thermostat-bank");
        var goals = new GoalTracker();
        var when = Run(goals, rt, 2 * 3600, 5);
        Assert.InRange(when["thermostat"], 3600, 3610);
        Assert.Equal(when["thermostat"], when["harrison"], 6);
        Assert.InRange(rt.GetField("bank", "temperature"), 29, 45);
        Assert.True(rt.HeatStores["rock"].Temperature > 100);
        // with the lid pinned shut by hand (no throttling), the claim is not made: take the strip's drive away by holding the bank cold
        var cold = Build("thermostat-bank");
        cold.SetField("cells", "temperature", -30);
        var g2 = new GoalTracker();
        Assert.DoesNotContain("thermostat", Run(g2, cold, 1800, 5).Keys);
    }

    [Fact]
    public void ADurationCannotBeClaimedFromTwoSamplesATwoHourGapApart()
    {
        // the contract a sleep depends on: the tracker must see the scene as often as it steps. Two looks 2 h apart prove nothing about the hour between.
        var sparse = Build("thermostat-bank");
        var goals = new GoalTracker();
        goals.Update([sparse], 0);
        for (int i = 0; i < 1440; i++) sparse.Step(5);                 // 2 h, unwatched
        Assert.Empty(goals.Update([sparse], sparse.Time));
        Assert.False(goals.Has("thermostat"));
        // watched every half second of the scene's clock, as a sleep does, it fires at the hour
        var watched = Build("thermostat-bank");
        var g2 = new GoalTracker();
        var when = Run(g2, watched, 3700, 0.5);
        Assert.InRange(when["thermostat"], 3600, 3601);
        // a night is no different: sampled at dusk and at dawn only, the bank could have frozen in between
        var rt = Build("found-electrics");
        rt.SetField("scene", "time", 17);
        var g3 = new GoalTracker();
        g3.Update([rt], 0);
        for (int i = 0; i < 2800; i++) rt.Step(5);                     // 14 h
        g3.Update([rt], rt.Time);
        Assert.False(g3.Has("keep-warm"));
    }

    [Fact]
    public void BoilerBurstFiresOnABurstAndSafetyValveOnALiftThatKeptTheBoiler()
    {
        var rt = Build("boiler-safety");
        var goals = new GoalTracker();
        var when = Run(goals, rt, 900, 0.1);
        Assert.Equal(1, rt.GetField("unguarded", "burst"));
        Assert.Equal(0, rt.GetField("guarded", "burst"));
        Assert.True(when.ContainsKey("boiler-burst"));
        Assert.True(when.ContainsKey("safety-valve"));
        Assert.True(when["boiler-burst"] > 0 && when["safety-valve"] > 10);       // the valve had to lift for 10 s first
        // the guarded boiler alone: valve earned, no burst
        var only = Build("boiler-safety");
        var g2 = new GoalTracker();
        var w2 = Run(g2, only, 120, 0.1, _ => { });
        Assert.Equal(0, only.GetField("guarded", "burst"));
        if (only.GetField("unguarded", "burst") == 1) Assert.True(w2.ContainsKey("boiler-burst"));
    }

    [Fact]
    public void ASafetyValveOnABoilerThatBurstWhileItLiftedEarnsNothingAfterTheBurst()
    {
        // an unguarded boiler's own record: no valve, so no safety-valve achievement however long it runs
        var rt = Build("boiler-safety");
        rt.SetField("guard", "lift", 1e9);                                          // the valve tied down: it never lifts, the boiler it guards bursts
        var goals = new GoalTracker();
        var when = Run(goals, rt, 900, 0.1);
        Assert.Equal(1, rt.GetField("guarded", "burst"));
        Assert.False(when.ContainsKey("safety-valve"));
        Assert.True(when.ContainsKey("boiler-burst"));
    }

    [Fact]
    public void ArchimedesScrewAndNoriaFireWhenTheRaisedStoreIsFull()
    {
        foreach (var (machine, lift, id) in new[] { ("archimedes-screw", "raise", "archimedes-screw"), ("hama-noria", "raise", "noria") })
        {
            var rt = Build(machine);
            var goals = new GoalTracker();
            var l = rt.Lifts[lift];
            Assert.False(goals.Has(id));
            var when = Run(goals, rt, 6 * 3600, 5, _ => l.Rpm = 20);          // turned at 20 rpm by hand (the view sets it in the game)
            Assert.True(l.To.WaterVolume >= 0.99 * l.To.Capacity, $"{machine}: store {l.To.WaterVolume} of {l.To.Capacity}");
            Assert.True(when.ContainsKey(id), machine);
            Assert.Single(when);                                                // and not the other one
            // the moment it fires: the store full, half its capacity or more carried
            Assert.True(l.Moved >= 0.5 * l.To.Capacity);
        }
    }

    [Fact]
    public void AScrewTurnedTooLittleToFillTheStoreEarnsNothing()
    {
        var rt = Build("archimedes-screw");
        var goals = new GoalTracker();
        var l = rt.Lifts["raise"];
        Run(goals, rt, 120, 1, _ => l.Rpm = 20);
        Assert.False(l.To.WaterVolume >= 0.99 * l.To.Capacity);
        Assert.False(goals.Has("archimedes-screw"));
    }

    [Fact]
    public void CtesibiusFiresOnAForcePumpThatLiftsPastTheSuctionLimitAndNotOnAShortLift()
    {
        // newcomen-engine's pump rod (a piston lift) raises water about 48 m, well over the 10.09 m a lift pump can on Earth
        var rt = Build("newcomen-engine");
        var goals = new GoalTracker();
        var pump = rt.Lifts["drainage"];
        Assert.True(pump.IsPump);
        Assert.True(pump.Head > 20);
        Assert.Empty(goals.Update([rt], 0));
        for (int i = 0; i < 10; i++) { pump.Stroke(1.8); rt.Step(0.1); }
        Assert.True(pump.Moved >= 0.001);
        Assert.Equal(["ctesibius"], goals.Update([rt], rt.Time).Select(g => g.Id));
        // the same pump raised only 5 m would be an ordinary suction lift: the trigger compares the head with the air's limit
        double limit = LiftPump.SuctionLimit(20, pump.Zone);
        Assert.InRange(limit, 10, 10.2);
    }

    [Fact]
    public void NewcomensMistakeFiresOnAStrokeInMarsAirAndNotInEarths()
    {
        var onMars = new MachineRuntime(Load("newcomen-engine").Translated(new Vec3(0, 0, 0), planet: Planet.Mars), Materials);
        var onEarth = Build("newcomen-engine");
        double stroke = 1.8;
        foreach (var rt in new[] { onMars, onEarth })
        {
            rt.SetField("cylinder", "piston-height", stroke);       // at the top of its stroke: the tappet opens the jet
            rt.Step(0.01);
        }
        var gm = new GoalTracker(); var ge = new GoalTracker();
        Assert.Equal(["newcomens-mistake"], gm.Update([onMars], 1).Select(g => g.Id));
        Assert.Empty(ge.Update([onEarth], 1));
    }

    [Fact]
    public void MeasureMarsFiresOnAShortSwingOnMarsAndNotOnEarth()
    {
        // earth-machines-on-mars' 1 m pendulum from 15 degrees on Mars: half period 1.6215 s traced, g = 4 pi^2 L / T^2 = 3.7538 m/s2, 1.2% over 3.71
        var rt = Build("earth-machines-on-mars");
        var goals = new GoalTracker();
        var when = Run(goals, rt, 60, 0.01);
        Assert.Equal(1.6215, rt.Pendulums["pivot"].HalfPeriod, 3);
        Assert.True(when.TryGetValue("measure-mars", out double at));
        Assert.InRange(at, 3 * 1.62, 5 * 1.62 + 0.1);                // from the fourth turning point
        // the same pendulum on Earth gives 9.8, nowhere near
        var earth = new MachineRuntime(Load("earth-machines-on-mars").Translated(new Vec3(0, 0, 0), planet: Planet.Earth), Materials);
        var g2 = new GoalTracker();
        Run(g2, earth, 60, 0.01);
        Assert.False(g2.Has("measure-mars"));
    }

    [Fact]
    public void ConstantHeadFiresAfterAFullSolOfThrottling()
    {
        // constant-head: 2 L/s through a float valve into a cistern whose tap draws a little; the level settles with the valve throttling
        // (a hair open: the feed matches the draw). A "sol" here is an hour, a planet number (Planet.Sol), so the check takes 4,000 s, not a day.
        var shortDay = Load("constant-head").Translated(new Vec3(0, 0, 0), planet: Planet.Earth with { Sol = 3600 });
        var rt = Build("constant-head", shortDay);
        var goals = new GoalTracker();
        var when = Run(goals, rt, 4000, 5);
        var (valve, _) = rt.FloatValves["ball"];
        Assert.InRange(valve.Opening, 0, 1);
        Assert.True(when.TryGetValue("constant-head", out double at), "never held a sol");
        Assert.InRange(at, 3600, 3600 + 400);                       // an hour of throttling after the first fill (the level settles within a few minutes)
        // and on the day-long Earth sol the same machine has not yet earned it after an hour
        var rt2 = Build("constant-head");
        var g2 = new GoalTracker();
        Assert.False(Run(g2, rt2, 4000, 5).ContainsKey("constant-head"));
    }

    // ---- the routes ----

    [Fact]
    public void RouteAchievementsReadTheBanksRecordAtTheCall()
    {
        // bank-bench's 'late' bank: 3 Wh from a Stirling engine and 2 Wh from wind fill it; the easy call at any hour
        var rt = Build("bank-bench");
        rt.SetField("scene", "clock-rate", 0);
        var bank = rt.Banks["late"];
        bank.CallAnyTime = true;
        bank.Offer(3 * BatteryBank.JoulesPerWattHour, "stirling");
        bank.Offer(2 * BatteryBank.JoulesPerWattHour, "wind");
        var goals = new GoalTracker();
        rt.Step(0.1);
        var got = goals.Update([rt], rt.Time).Select(g => g.Id).ToList();
        Assert.Contains("route-hot-air", got);                      // 60% from the Stirling engine
        Assert.DoesNotContain("route-windmiller", got);             // 40% wind, not 90%
        Assert.Contains("route-three-sols", got);
        Assert.Contains("the-call", got);
    }

    [Fact]
    public void TheTrapsFireOnTheirSourcesAlone()
    {
        var rt = Build("bank-bench");
        var goals = new GoalTracker();
        var bank = rt.Banks["late"];
        goals.Update([rt], 0);
        Assert.False(goals.Has("route-water-wheel"));
        bank.Offer(100, "water-wheel");
        Assert.Equal(["route-water-wheel"], goals.Update([rt], 1).Select(g => g.Id));
        bank.Offer(100, "falling-weight");
        Assert.Equal(["route-gravity-battery"], goals.Update([rt], 2).Select(g => g.Id));
        bank.Offer(100, "aeolipile");
        Assert.Equal(["route-heron-purist"], goals.Update([rt], 3).Select(g => g.Id));
    }

    [Fact]
    public void ABankChargedByAFallingWeightGeneratorRecordsItAndEarnsTheGravityBattery()
    {
        var rt = Build("bank-bench");                              // g-hot is named #:driven-by falling-weight
        var goals = new GoalTracker();
        var when = Run(goals, rt, 800, 0.5);
        Assert.True(when.ContainsKey("route-gravity-battery"));
        Assert.InRange(when["route-gravity-battery"], 583, 800);   // the hot bank cools through 45 C at 583 s
        Assert.False(when.ContainsKey("route-water-wheel"));
    }

    // ---- persistence ----

    [Fact]
    public void EarnedGoalsRoundTripThroughTheWorldSaveAndAreNotTakenBack()
    {
        var rt = Build("bank-bench");
        var goals = new GoalTracker();
        rt.Banks["late"].Offer(100, "water-wheel");
        rt.Banks["late"].Offer(100, "falling-weight");
        goals.BankCrate = () => new CrateReading(-1, 0.5, 12.5, -3.25);
        goals.Update([rt], 77);
        Assert.True(goals.Has("route-water-wheel"));
        var save = new WorldSave { Kind = "world", Name = "w", Machines = [], Goals = goals.ToForm() };
        var text = save.ToText();
        var back = WorldSave.Parse(text);
        Assert.NotNull(back.Goals);
        var again = new GoalTracker();
        again.Restore(back.Goals!);
        Assert.Equal(goals.EarnedGoals.Select(e => (e.Id, e.Time, e.Sol)).OrderBy(x => x.Id), again.EarnedGoals.Select(e => (e.Id, e.Time, e.Sol)).OrderBy(x => x.Id));
        Assert.Equal(77, again.Get("route-water-wheel")!.Time);
        // an old save without goals loads
        Assert.Null(WorldSave.Parse(text.Replace(back.Goals!.Items.Count > 0 ? SExprWriter.Print(back.Goals) : "", "")).Goals);
        // the found position survived: hauling 2 m from it frees the crate even in a new tracker
        again.BankCrate = () => new CrateReading(-1, 0.5, 14.6, -3.25);
        Assert.Contains("free-bank", again.Update([rt], 1).Select(g => g.Id));
        // a restart keeps concept and route achievements and clears the path
        again.RestartRun();
        Assert.True(again.Has("route-water-wheel"));
        Assert.False(again.Has("find-bank"));
    }
}
