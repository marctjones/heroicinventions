using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Game;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #209: the bank found in the crater is the real battery-bank part, built into its crate (racket/machines/found-bank.rkt). The
/// crate is the rigid body the slide buries and the rover moves; the bank's state lives in the machine, so it goes where the crate goes.
/// The numbers are worked out from the machine's header: 16 kg of cells at -63 C (Mars's ambient), 4,000 Wh = 14.4 MJ.
/// (The Jolt side, the slide and a push, is racket/heroic/tests/found-bank-test.rkt; no generator is wired here: Generator.Apply
/// charges the bank directly, standing in for the wire of #208.)
/// </summary>
public class FoundBankTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static MachineDef Def() => MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "found-bank.machine")));
    private static MachineRuntime Build(ScenarioTuning? tuning = null) => new(Def(), Materials, tuning);

    [Fact]
    public void TheFoundBankIsTheRealPartInItsCrate_ColdAtFirstAndSizedByTheScenario()
    {
        var rt = Build();
        var bank = rt.Banks["bank"];
        Assert.Equal("cells", bank.InName);
        Assert.Equal("crate", Def().Part("bank")!.Symbol("on", ""));
        Assert.Equal(5000 * BatteryBank.JoulesPerWattHour, bank.Capacity, 6);   // the design doc's 5 kWh
        Assert.Equal(0, bank.Charge);
        Assert.Equal(-63, bank.Temperature, 6);                  // Mars's ambient: the cells start as cold as the air
        Assert.False(bank.Accepting);                            // under 0 C it refuses charge until it is warmed
        Assert.Equal(0, bank.Offer(1000, "wind"));
        rt.HeatStores["cells"].Temperature = 20;                 // warmed by the player's works
        Assert.True(bank.Accepting);
        // the scenario's bank-capacity multiplier (#60) applies to the found bank
        Assert.Equal(1250 * BatteryBank.JoulesPerWattHour, Build(new ScenarioTuning { BankCapacity = 0.25 }).Banks["bank"].Capacity, 6);
    }

    [Fact]
    public void ChargeTemperatureAndSourcesSurviveASaveAndALoad()
    {
        var rt = Build();
        rt.HeatStores["cells"].Temperature = 20;
        var gen = new Generator("g") { Bank = rt.Banks["bank"], DrivenBy = "wind" };
        gen.Step(200, 1);                                                        // a generator at 200 rad/s for 1 s: 0.8 x 4.9183 x 200 = 786.9 J (ElectricsTests)
        Assert.Equal(786.9, rt.Banks["bank"].Charge, 0);
        Assert.Equal(0.2e6, rt.Banks["bank"].Offer(0.2e6, "stirling"), 6);       // 55.6 Wh from a heat engine
        rt.Step(1);
        var saved = RuntimeState.Capture(rt);
        var again = Build();
        Assert.Empty(RuntimeState.Restore(again, saved));
        var bank = again.Banks["bank"];
        Assert.Equal(rt.Banks["bank"].Charge, bank.Charge, 6);
        Assert.Equal(786.9 + 0.2e6, bank.Charge, 0);
        Assert.Equal(rt.Banks["bank"].Sources["wind"], bank.Sources["wind"], 6);
        Assert.Equal(786.9, bank.Sources["wind"], 0);
        Assert.Equal(0.2e6, bank.Sources["stirling"], 3);
        Assert.Equal(rt.Banks["bank"].Temperature, bank.Temperature, 6);
        Assert.True(bank.Temperature > 19);
    }

    [Fact]
    public void FindFreeChargeAndTheCallAllReadTheSamePart()
    {
        var rt = Build(new ScenarioTuning { BankCapacity = 0.0001 });             // 0.5 Wh: 1,800 J
        var bank = rt.Banks["bank"];
        Assert.Equal(1800, bank.Capacity, 6);
        double cover = 3.98, x = 264, z = 143;
        var goals = new GoalTracker { BankCrate = () => new CrateReading(cover, 0.5, x, z) };
        rt.SetField("scene", "clock-rate", 0);
        rt.Step(0.1);
        Assert.DoesNotContain("find-bank", goals.Update([rt], rt.Time).Select(g => g.Id));      // buried: the bank is in the runtime, but not found
        cover = -0.1;                                                                          // dug out
        Assert.Contains("find-bank", goals.Update([rt], rt.Time).Select(g => g.Id));
        Assert.DoesNotContain("free-bank", goals.Update([rt], rt.Time).Select(g => g.Id));
        x += 2.5;                                                                              // pushed 2.5 m: its state goes with it
        Assert.Contains("free-bank", goals.Update([rt], rt.Time).Select(g => g.Id));
        // warmed, charged by a generator and called: the same part's record
        rt.HeatStores["cells"].Temperature = 20;
        bank.CallAnyTime = true;
        bank.Offer(2000, "wind");
        rt.Step(0.1);
        var got = goals.Update([rt], rt.Time).Select(g => g.Id).ToList();
        Assert.Contains("charge-bank", got);
        Assert.Contains("the-call", got);
        Assert.True(rt.Won);
        Assert.Equal(1800, bank.Sources["wind"], 6);
    }
}
