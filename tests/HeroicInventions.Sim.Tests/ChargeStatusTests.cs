using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #212: the label that says why a bank is not charging. One case per reason, text asserted against the sim state set up by hand.</summary>
public class ChargeStatusTests
{
    private const double Rpm = 2 * Math.PI / 60;

    private static Generator Rig(double rotorRpm, double bankC = 20, double chargeWh = 0, bool wired = true)
    {
        var b = new BatteryBank("bank", 100 * 3600, chargeWh * 3600) { AssumedTemperature = bankC };
        var g = new Generator("motor") { Bank = wired ? b : null, DrivenBy = "wind", Prime = new PrimeMover("wind", "sails", () => 30 * Rpm, 1, 1, null) };
        g.Step(rotorRpm * Rpm, 0.01);
        return g;
    }

    [Fact]
    public void Unwired() =>
        Assert.Equal("motor · windmill 30 rpm\nnot charging: unwired", ChargeStatus.Label(Rig(2000, wired: false)));

    [Fact]
    public void RotorUnderTheCutIn() =>
        Assert.Equal("motor · windmill 30 rpm\nnot charging: rotor 800 rpm under 1,500", ChargeStatus.Label(Rig(800)));

    [Fact]
    public void BankTooCold_AndTheRotorToo_NamesBoth() =>
        Assert.Equal("motor · windmill 30 rpm\nnot charging: bank -63 °C outside 0-45 °C\n and rotor 800 rpm under 1,500",
                     ChargeStatus.Label(Rig(800, bankC: -63)));

    [Fact]
    public void BankTooColdWithTheRotorFast() =>
        Assert.Equal("motor · windmill 30 rpm\nnot charging: bank -63 °C outside 0-45 °C", ChargeStatus.Label(Rig(2000, bankC: -63)));

    [Fact]
    public void BankFull() =>
        Assert.Equal("motor · windmill 30 rpm\nnot charging: bank full", ChargeStatus.Label(Rig(2000, chargeWh: 100)));

    [Fact]
    public void SleepingWithMachinesPaused() =>
        Assert.Equal("motor · windmill 30 rpm\nnot charging: sleeping, machines paused", ChargeStatus.Label(Rig(2000), rotorAsleep: true));

    [Fact]
    public void ASleepHoldingTheLastSteadyRateSaysSo()
    {
        var g = Rig(2000);
        Assert.Equal("motor · windmill 30 rpm\nsleeping, machines paused: charging 90 W, at its last steady rate (approximate)",
                     ChargeStatus.Label(g, true, hold: new ChargeStatus.Hold(90, false)));
        Assert.Contains("estimated: no steady rate yet", ChargeStatus.Label(g, true, hold: new ChargeStatus.Hold(90, true)));
    }

    [Fact]
    public void AMissingHeldFieldReadsAsNotHeld() =>
        Assert.Null(ChargeStatus.Hold.Read(new Dictionary<string, Func<double>>(), "motor").Watts);

    [Fact]
    public void ChargingShowsThePower()
    {
        // 2,000 rpm = 209.44 rad/s: tau = 0.114592 x (209.44 - 157.08) = 6.00 N.m; P = 0.8 x 6.00 x 209.44 = 1005 W; 35.9 A at 28 V
        Assert.Equal("motor · windmill 30 rpm\ncharging 1005 W · 35.9 A · rotor 2,000 rpm", ChargeStatus.Label(Rig(2000)));
    }

    [Fact]
    public void AGeneratorInAnotherMachineSaysSoHonestly()
    {
        var g = new Generator("motor") { Bank = new BatteryBank("bank", 3600), DrivenBy = "shaft" };
        g.Step(800 * Rpm, 0.01);
        Assert.Equal("motor · prime mover rpm unknown\nnot charging: rotor 800 rpm under 1,500", ChargeStatus.Label(g));
    }

    // ---- against the real machine and the sleep's real fields (found-electrics: 278.30 W settled, worked in PausedSleepChargeTests) ----

    private static MachineRuntime FoundElectrics(double capacityWh = 100)
    {
        var rt = new MachineRuntime(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "found-electrics.machine"))), MaterialLibrary.LoadDefault());
        rt.SetField("bank", "capacity", capacityWh);
        return rt;
    }

    private static void Watch(MachineRuntime rt, double seconds, Func<double, double> omega)
    {
        var gen = rt.Generators["motor"];
        for (int i = 0; i < (int)Math.Round(seconds * 120); i++) { gen.Step(omega(i / 120.0), 1.0 / 120); rt.Step(1.0 / 120); }
    }

    [Fact]
    public void ASleepHoldingTheSettledRateReadsFromTheRealFields()
    {
        var rt = FoundElectrics();
        Watch(rt, 12, _ => 174.479);                               // settles at 278.30 W
        Assert.Null(ChargeStatus.Hold.Read(rt.FieldGetters, "motor").Watts);   // watched, not held
        rt.HoldGenerators();
        var hold = ChargeStatus.Hold.Read(rt.FieldGetters, "motor");
        Assert.Equal(278.30, hold.Watts!.Value, 2);
        Assert.False(hold.Estimated);
        Assert.EndsWith("\nsleeping, machines paused: charging 278 W, at its last steady rate (approximate)", ChargeStatus.Label(rt.Generators["motor"], true, hold));
        Assert.StartsWith("motor · windmill ", ChargeStatus.Label(rt.Generators["motor"], true, hold));
    }

    [Fact]
    public void ASleepHoldingAnEstimateSaysSo()
    {
        var rt = FoundElectrics();
        Watch(rt, 30, t => 160 + 100 * t / 30);                    // still speeding up: never settles
        rt.HoldGenerators();
        var hold = ChargeStatus.Hold.Read(rt.FieldGetters, "motor");
        Assert.True(hold.Estimated);
        Assert.Contains("charging 278 W, estimated: no steady rate yet, worked from the wind and the train", ChargeStatus.Label(rt.Generators["motor"], true, hold));
    }

    [Fact]
    public void AHeldGeneratorWithAColdBankStillSaysWhy()
    {
        var rt = FoundElectrics();
        Watch(rt, 12, _ => 174.479);
        rt.HoldGenerators();
        rt.HeatStores["cells"].Temperature = -63;
        Assert.EndsWith("not charging: sleeping, machines paused\n and bank -63 °C outside 0-45 °C",
                        ChargeStatus.Label(rt.Generators["motor"], true, ChargeStatus.Hold.Read(rt.FieldGetters, "motor")));
    }
}
