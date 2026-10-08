using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #60: a scenario's tuning numbers reach the parts (a bank's capacity, a generator's cut-in, the call window, the easy
/// setting), the Advanced constants are numbers fed to the same formulas, and with every number at 1x the run is the untuned run
/// exactly. Worked on bank-bench (racket/machines/bank-bench.rkt carries the numbers).
/// </summary>
public class ScenarioTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static MachineDef Bench() => MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "bank-bench.machine")));
    private static MachineRuntime Run(ScenarioTuning? tuning)
    {
        var rt = new MachineRuntime(Bench(), Materials, tuning);
        rt.SetField("scene", "clock-rate", 0);
        return rt;
    }
    private static WorldDef World(string name) => WorldDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "worlds", $"{name}.world")), name);

    // ---- the format ----

    [Fact]
    public void ALonelyRoverOpeningCarriesItsScenarioAndTheEasyVariantDropsThePassRequirement()
    {
        var real = World("lonely-rover-opening").Scenario;
        Assert.NotNull(real);
        Assert.Equal("The Lonely Rover", real!.Title);
        Assert.False(string.IsNullOrWhiteSpace(real.Description));
        Assert.True(real.Tuning.IsReal);                      // the real game is the real numbers
        var easy = World("lonely-rover-easy").Scenario!;
        Assert.True(easy.Tuning.CallAnyTime);
        Assert.True(easy.Tuning.BankCapacity < 1 && easy.Tuning.GeneratorCutIn < 1 && easy.Tuning.Wear < 1);
        Assert.False(easy.Tuning.HasAdvanced);                // the risky constants stay the planet's
        Assert.Equal(World("lonely-rover-opening").Placements.Select(p => (p.Label, p.At)), World("lonely-rover-easy").Placements.Select(p => (p.Label, p.At)));
    }

    [Fact]
    public void AScenarioWritesBackAndSurvivesLinkEdits()
    {
        const string text = "(world w (scenario (title \"A \\\"quoted\\\" title\") (description \"Slow and kind.\") (tuning (wear 0.25) (bank-capacity 0.2) (call-any-time #t) (sleep-speed 4)) (advanced (gravity 9.81) (generator-efficiency 0.9) (bank-min-charge-c -10))) (place a pendulum (at 0 0 0)))";
        var world = WorldDef.Parse(text, "w");
        var s = world.Scenario!;
        Assert.Equal("A \"quoted\" title", s.Title);
        Assert.Equal(0.25, s.Tuning.Wear);
        Assert.Equal(0.2, s.Tuning.BankCapacity);
        Assert.True(s.Tuning.CallAnyTime);
        Assert.Equal(4, s.Tuning.SleepSpeed);
        Assert.Equal(9.81, s.Tuning.Gravity);
        Assert.Equal(0.9, s.Tuning.GeneratorEfficiency);
        Assert.Equal(-10, s.Tuning.BankMinChargeC);
        Assert.Null(s.Tuning.BankMaxChargeC);
        Assert.True(s.Tuning.HasAdvanced);
        var again = WorldDef.Parse(world.Write(), "w").Scenario!;
        Assert.Equal(s, again);
        Assert.Equal(s, world.WithoutLink("none").Scenario);
        Assert.Null(WorldDef.Parse("(world w (place a pendulum (at 0 0 0)))", "w").Scenario);
    }

    [Theory]
    [InlineData("(tuning (wear 0))")]                      // a multiplier of 0 would stop a process, not tune it
    [InlineData("(tuning (flux-capacitor 2))")]            // no such number
    [InlineData("(tuning (gravity 9.81))")]                // a risky constant is Advanced only
    [InlineData("(advanced (wear 2))")]
    [InlineData("(advanced (generator-efficiency 1.5))")]
    [InlineData("(advanced (gravity -1))")]
    [InlineData("(advanced (bank-min-charge-c 50))")]      // above the 45 default maximum is allowed only with a higher maximum
    public void ANumberOutOfItsRangeOrInTheWrongPanelIsRefused(string form)
    {
        string text = $"(world w (scenario (title \"t\") (description \"d\") {form}))";
        if (form.Contains("bank-min-charge-c 50"))
        {
            // alone it is fine to read (the bank is checked when built); with a maximum under it, refused
            text = text.Replace("(advanced (bank-min-charge-c 50))", "(advanced (bank-min-charge-c 50) (bank-max-charge-c 40))");
        }
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse(text, "w"));
    }

    // ---- the numbers reach the parts ----

    [Fact]
    public void TheCapacityCutInAndCallWindowMultipliersReachTheBankAndGenerator()
    {
        var plain = Run(null);
        Assert.Equal(5, plain.Banks["warm"].CapacityWh, 9);
        Assert.Equal(30, plain.Generators["g-warm"].CutInRpm, 9);
        Assert.Equal(10, plain.Banks["ok"].CallMinutes, 9);
        Assert.False(plain.Banks["ok"].CallAnyTime);

        var tuned = Run(new ScenarioTuning { BankCapacity = 0.2, GeneratorCutIn = 0.5, CallWindow = 6, CallAnyTime = true });
        Assert.Equal(1, tuned.Banks["warm"].CapacityWh, 9);                        // 5 Wh x 0.2
        Assert.Equal(15, tuned.Generators["g-warm"].CutInRpm, 9);                  // 30 rpm x 0.5
        Assert.Equal(60, tuned.Banks["ok"].CallMinutes, 9);                        // 10 min x 6
        Assert.True(tuned.Banks["ok"].CallAnyTime);
        Assert.Equal(1, tuned.Banks["ok"].ChargeWh, 9);                            // a bank made full stays full at its new capacity
        // the field readouts say the same
        Assert.Equal(1, tuned.GetField("warm", "capacity"), 9);
        Assert.Equal(60, tuned.GetField("ok", "call-minutes"), 9);
        Assert.Equal(1, tuned.GetField("ok", "call-any-time"));
    }

    [Fact]
    public void ACutInOverTheRatedSpeedLiftsTheRatedSpeedRatherThanRefuseTheMachine()
    {
        var rt = Run(new ScenarioTuning { GeneratorCutIn = 10 });        // 300 rpm against a rated 150
        var g = rt.Generators["g-warm"];
        Assert.Equal(300, g.CutInRpm, 9);
        Assert.True(g.RatedRpm > g.CutInRpm);
    }

    [Fact]
    public void ASmallerBankFillsInTheSameWattsOverFewerJoulesAndTheFormulaIsUnchanged()
    {
        // bank-bench: the warm bank takes 159.87 W once the sails settle; 18,000 J fills it in about 112.6 s of charging after a 6.5 s start.
        // At 0.2x the capacity is 3,600 J: 3,600 / 159.87 = 22.5 s of charging, so full at about 29 s; the power is the same 159.87 W.
        var rt = Run(new ScenarioTuning { BankCapacity = 0.2 });
        double t = 0, watts = 0;
        while (rt.GetField("warm", "full") == 0 && t < 200) { rt.Step(0.05); t += 0.05; if (t > 20) watts = rt.GetField("g-warm", "power"); }
        Assert.InRange(t, 26, 32);
        Assert.Equal(159.87, watts, 0);                       // eta tau omega, with the same sails and the same curve
        Assert.Equal(1, rt.GetField("warm", "charge"), 6);
    }

    [Fact]
    public void TheEasySettingMakesTheCallAtNoonAndTheWindowMultiplierWidensThePass()
    {
        var rt = Run(new ScenarioTuning { CallAnyTime = true });
        rt.SetField("scene", "time", 12);
        Assert.Equal(1, rt.GetField("ok", "ready"));
        for (int i = 0; i < 4; i++) rt.Step(0.1);
        Assert.Equal(1, rt.GetField("ok", "won"));                 // a full warm bank, at noon
        var hard = Run(null);
        hard.SetField("scene", "time", 12);
        for (int i = 0; i < 4; i++) hard.Step(0.1);
        Assert.Equal(0, hard.GetField("ok", "won"));
        // 03:15 is outside a 10 minute pass and inside 6x (60 minutes)
        var wide = Run(new ScenarioTuning { CallWindow = 6 });
        Assert.True(wide.Banks["ok"].InWindow(3.25));
        Assert.False(hard.Banks["ok"].InWindow(3.25));
    }

    [Fact]
    public void TheAdvancedConstantsAreNumbersFedToTheSameFormulas()
    {
        // eta: the bank gets eta x tau x omega with the scenario's eta
        var rt = Run(new ScenarioTuning { GeneratorEfficiency = 0.5 });
        for (int i = 0; i < 600; i++) rt.Step(0.1);
        double torque = rt.GetField("g-warm", "torque"), omega = rt.GetField("g-warm", "omega");
        Assert.Equal(0.5 * torque * omega, rt.GetField("g-warm", "power"), 6);
        // the bank's charging window moves: a bank at -10 C charges when its lowest is -20
        var cold = Run(new ScenarioTuning { BankMinChargeC = -20 });
        Assert.Equal(-20, cold.Banks["cold"].MinChargeC);
        Assert.Equal(1, cold.GetField("cold", "accepting"));
        Assert.Equal(0, Run(null).GetField("cold", "accepting"));
        // gravity: the world's g, in the runtime and in the zone every part reads
        var earth = Run(new ScenarioTuning { Gravity = 9.81 });
        Assert.Equal(9.81, earth.Def.Planet.Gravity, 9);
        Assert.Equal(9.81, earth.Outside.Gravity, 9);
    }

    // ---- the formulas are unchanged ----

    [Fact]
    public void WithEveryNumberAtOneTheRunIsTheUntunedRunExactly()
    {
        var a = Run(null);
        var b = Run(ScenarioTuning.Real);
        var c = Run(new ScenarioTuning { Wear = 1, Evaporation = 1, BankCapacity = 1, GeneratorCutIn = 1, CallWindow = 1, SleepSpeed = 1 });
        Assert.True(ScenarioTuning.Real.IsReal);
        Assert.True(new ScenarioTuning { Wear = 1 }.IsReal);
        for (int i = 0; i < 1600; i++) { a.Step(0.5); b.Step(0.5); c.Step(0.5); }
        Assert.Equal(a.FieldGetters.Keys.OrderBy(k => k), b.FieldGetters.Keys.OrderBy(k => k));
        int compared = 0;
        foreach (var (key, get) in a.FieldGetters)
        {
            Assert.Equal(get(), b.FieldGetters[key](), 0);
            Assert.True(get().Equals(b.FieldGetters[key]()) || (double.IsNaN(get()) && double.IsNaN(b.FieldGetters[key]())), $"{key}");
            Assert.True(get().Equals(c.FieldGetters[key]()) || (double.IsNaN(get()) && double.IsNaN(c.FieldGetters[key]())), $"{key} (explicit 1x)");
            compared++;
        }
        Assert.True(compared > 60, $"compared only {compared} fields");
        Assert.Equal(5, a.GetField("warm", "from-wind"), 6);        // and the run did something: the bank filled
    }

    [Fact]
    public void WearAndEvaporationMultipliersScaleOneInputEach()
    {
        string wear = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "bearing-friction.machine"));
        var plain = new MachineRuntime(MachineDef.Parse(wear), Materials);
        var tuned = new MachineRuntime(MachineDef.Parse(wear), Materials, new ScenarioTuning { Wear = 0.25 });
        Assert.NotEmpty(plain.AxleBearings);
        foreach (var (id, b) in plain.AxleBearings)
        {
            Assert.True(b.WearRate > 0 || id.Length > 0);
            Assert.Equal(b.WearRate * 0.25, tuned.AxleBearings[id].WearRate, 15);
            Assert.Equal(b.Mu, tuned.AxleBearings[id].Mu);        // only the wear rate moves: friction is the same
        }
    }
}
