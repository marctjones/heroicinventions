using HeroicInventions.Sim.Electrics;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #212: the label that says why a bank is not charging. One case per reason, text asserted against the sim state set up by hand.</summary>
public class ChargeStatusTests
{
    private const double Rpm = 2 * Math.PI / 60;

    private static Generator Rig(double rotorRpm, double bankC = 20, double chargeWh = 0, bool wired = true)
    {
        var b = new BatteryBank("bank", 100 * 3600, chargeWh * 3600) { AssumedTemperature = bankC };
        var g = new Generator("motor") { Bank = wired ? b : null, DrivenBy = "wind", PrimeOmega = () => 30 * Rpm };
        g.Step(rotorRpm * Rpm, 0.01);
        return g;
    }

    [Fact]
    public void Unwired() =>
        Assert.Equal("motor rotor 2,000 rpm · windmill 30 rpm · 0.00 N·m\nnot charging: unwired", ChargeStatus.Label(Rig(2000, wired: false)));

    [Fact]
    public void RotorUnderTheCutIn() =>
        Assert.Equal("motor rotor 800 rpm · windmill 30 rpm · 0.00 N·m\nnot charging: rotor 800 rpm under 1,500", ChargeStatus.Label(Rig(800)));

    [Fact]
    public void BankTooCold_AndTheRotorToo_NamesBoth() =>
        Assert.Equal("motor rotor 800 rpm · windmill 30 rpm · 0.00 N·m\nnot charging: bank -63 °C outside 0-45 °C\n and rotor 800 rpm under 1,500",
                     ChargeStatus.Label(Rig(800, bankC: -63)));

    [Fact]
    public void BankTooColdWithTheRotorFast() =>
        Assert.Equal("motor rotor 2,000 rpm · windmill 30 rpm · 0.00 N·m\nnot charging: bank -63 °C outside 0-45 °C", ChargeStatus.Label(Rig(2000, bankC: -63)));

    [Fact]
    public void BankFull() =>
        Assert.Equal("motor rotor 2,000 rpm · windmill 30 rpm · 0.00 N·m\nnot charging: bank full", ChargeStatus.Label(Rig(2000, chargeWh: 100)));

    [Fact]
    public void SleepingWithMachinesPaused() =>
        Assert.Equal("motor rotor 2,000 rpm · windmill 30 rpm · 6.00 N·m\nnot charging: sleeping, machines paused", ChargeStatus.Label(Rig(2000), rotorAsleep: true));

    [Fact]
    public void ASleepHoldingTheLastSteadyRateSaysSo()
    {
        var g = Rig(2000);
        Assert.Equal("motor rotor 2,000 rpm · windmill 30 rpm · 6.00 N·m\nsleeping, machines paused: charging at its last steady rate, 90 W (approximate)",
                     ChargeStatus.Label(g, true, hold: new ChargeStatus.Hold(90, false)));
        Assert.Contains("estimated rate", ChargeStatus.Label(g, true, hold: new ChargeStatus.Hold(90, true)));
    }

    [Fact]
    public void AMissingHeldFieldReadsAsNotHeld() =>
        Assert.Null(ChargeStatus.Hold.Read(new Dictionary<string, Func<double>>(), "motor").Watts);

    [Fact]
    public void ChargingShowsThePower()
    {
        // 2,000 rpm = 209.44 rad/s: tau = 0.114592 x (209.44 - 157.08) = 6.00 N.m; P = 0.8 x 6.00 x 209.44 = 1005 W; 35.9 A at 28 V
        Assert.Equal("motor rotor 2,000 rpm · windmill 30 rpm · 6.00 N·m\ncharging 1005 W · 35.9 A at 28 V", ChargeStatus.Label(Rig(2000)));
    }

    [Fact]
    public void AGeneratorInAnotherMachineSaysSoHonestly()
    {
        var g = new Generator("motor") { Bank = new BatteryBank("bank", 3600), DrivenBy = "shaft" };
        g.Step(800 * Rpm, 0.01);
        Assert.Equal("motor rotor 800 rpm · prime mover rpm unknown · 0.00 N·m\nnot charging: rotor 800 rpm under 1,500", ChargeStatus.Label(g));
        Assert.StartsWith("motor rotor 800 rpm · shaft in 120 rpm", ChargeStatus.Label(g, linkedRpm: 120));
    }
}
