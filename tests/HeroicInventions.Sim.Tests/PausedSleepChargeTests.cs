using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// A sleep that charges (owner's ruling, 2026-10-09): during a sleep that pauses the physics engine, a generator on a rigid body charges
/// its bank at its last settled power, by the bank's own rules. The machine is found-electrics (racket/machines/found-electrics.rkt): the
/// generator "motor" sits on rotor-disc, a wheel only the engine turns, so headless its rotor is turned here by hand, as the view does
/// (Generator.Step), at the speed the header works out for the loaded train.
///
/// Worked before the run: the rotor at 174.479 rad/s, over the 157.08 rad/s cut-in by 17.399; k = 12 / (261.80 - 157.08) = 0.114592,
/// so tau = 1.99381 N.m and P = 0.8 x 1.99381 x 174.479 = 278.30 W. Settled after ten whole seconds of charging (each second's mean
/// within 5% of the ten's). The bank, sized to 100 Wh, takes 278.30 x 300 s = 83,490 J = 23.19 Wh in a 300 s paused sleep; 50 Wh from
/// empty wakes after 180,000 / 278.30 = 646.8 s.
/// </summary>
public class PausedSleepChargeTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private const double Dt = 1.0 / 120;
    private const double Rotor = 174.479;                                  // rad/s, the header's loaded rotor
    private static readonly double P = 0.8 * (12 / ((2500 - 1500) * 2 * Math.PI / 60)) * (Rotor - 1500 * 2 * Math.PI / 60) * Rotor;

    private static MachineRuntime Build(string name = "found-electrics") =>
        new(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", $"{name}.machine"))), Materials);

    /// <summary>The machine watched for <paramref name="seconds"/>: the view turns the rotor and loads it (Generator.Step), the sim steps.</summary>
    private static void Watch(MachineRuntime rt, double seconds, Func<double, double>? omega = null)
    {
        var gen = rt.Generators["motor"];
        int n = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < n; i++)
        {
            gen.Step(omega?.Invoke(i * Dt) ?? Rotor, Dt);
            rt.Step(Dt);
        }
    }

    /// <summary>A paused sleep: only the sim steps (SleepControl.Advance); nothing turns the rotor.</summary>
    private static void Sleep(MachineRuntime rt, double seconds, Action<double>? each = null)
    {
        int n = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < n; i++) { rt.Step(Dt); each?.Invoke(i * Dt); }
    }

    private static MachineRuntime Settled(double capacityWh = 100)
    {
        var rt = Build();
        rt.SetField("bank", "capacity", capacityWh);
        Watch(rt, 12);
        return rt;
    }

    [Fact]
    public void TheWorkedPowerIs278Point3W()
    {
        Assert.Equal(278.30, P, 2);
    }

    [Fact]
    public void ItSettlesAfterTenSecondsOfSteadyCharging_NotWhileTheRotorIsStillSpeedingUp()
    {
        var rt = Build();
        rt.SetField("bank", "capacity", 100);
        var gen = rt.Generators["motor"];
        Watch(rt, 9.5);
        Assert.True(double.IsNaN(gen.SettledPower), "nine and a half seconds: not ten whole seconds yet");
        Watch(rt, 1);
        Assert.Equal(P, gen.SettledPower, 6);
        Assert.Equal(P, rt.GetField("motor", "settled-power"), 6);

        Assert.Equal(0, rt.GetField("motor", "held-estimated"));

        // a rotor still speeding up (160 to 260 rad/s over 30 s: its power climbs far more than 5% across ten seconds) never settles
        var ramp = Build();
        ramp.SetField("bank", "capacity", 100);
        Watch(ramp, 30, t => 160 + 100 * t / 30);
        Assert.True(double.IsNaN(ramp.Generators["motor"].SettledPower));
        // held, it charges at the estimate from the sails and the train, marked so (the ruling of 2026-10-09)
        var held = ramp.HoldGenerators();
        Assert.Equal(P, Assert.Single(held).Watts, 0.01 * P);
        Assert.True(ramp.Generators["motor"].HeldEstimated);
        Assert.Equal(1, ramp.GetField("motor", "held-estimated"));
    }

    [Fact]
    public void TheEstimateIsTheWorkedOperatingPoint_FromTheSailsTorqueCurveAndTheTrain()
    {
        // the header: 255.34 (2 - w / 1.5) = 125 k (125 w - 157.08) / 0.912673 at w = 1.39583 rad/s, the rotor at 174.479, 278.30 W
        var rt = Build();
        var gen = rt.Generators["motor"];
        var m = Assert.IsType<PrimeMover>(gen.Mover);
        Assert.Equal("wind", m.Name);
        Assert.Equal(125, m.Ratio, 9);                                       // three 100:20 meshes
        Assert.Equal(0.97 * 0.97 * 0.97, m.Eta, 9);
        Assert.Equal(278.30, gen.EstimatePower(), 0.003 * 278.30);
        Assert.Equal(gen.EstimatePower(), rt.GetField("motor", "estimated-power"), 9);
        // a still day: the sails give nothing, and nothing is estimated
        rt.Windmills["sails"].Wind = 0;
        Assert.Equal(0, gen.EstimatePower());
        // 1.5 m/s: the free sails turn at 2 x 2.5 x 1.5 / 5 = 1.5 rad/s, x 125 = 187.5 rad/s over the 157.08 cut-in: some charge, far less (v^3 would be 1/8)
        rt.Windmills["sails"].Wind = 1.5;
        Assert.InRange(gen.EstimatePower(), 1, 278.30 / 4);
    }

    [Fact]
    public void ABankColdAtTheSleepsStartIsChargedAtTheEstimateOnceTheVaultHasWarmedIt()
    {
        var rt = Build();
        rt.SetField("bank", "capacity", 1000);
        var bank = rt.Banks["bank"];
        rt.HeatStores["cells"].Temperature = -2;                              // the 20 C vault warms 16 kg of cells over hours: past 0 C in about 2,400 s
        Watch(rt, 30);                                                        // watched with the bank cold: open circuit, nothing settles
        var gen = rt.Generators["motor"];
        Assert.True(double.IsNaN(gen.SettledPower));
        Assert.Equal(0, bank.Charge);
        var (_, _, watts) = Assert.Single(rt.HoldGenerators());
        Assert.True(gen.HeldEstimated);
        Assert.Equal(278.30, watts, 0.003 * 278.30);
        // asleep: nothing while the cells are under 0 C; the 20 C vault warms them, and from then on the estimate, step by step
        double warmAt = double.NaN, inRange = 0;
        Sleep(rt, 6000, t =>
        {
            if (bank.Temperature < 0) Assert.Equal(0, bank.Charge);
            else { if (double.IsNaN(warmAt)) warmAt = t; inRange += Dt; }
        });
        Assert.False(double.IsNaN(warmAt), $"the vault warmed the cells past 0 C (at {bank.Temperature:0.0} C)");
        Assert.InRange(warmAt, 600, 5400);                                    // cold for a good part of the sleep, then in range
        Assert.Equal(watts * inRange, bank.Charge, 0.01 * watts * inRange);
        Assert.Equal(bank.Charge, bank.Sources["wind"], 6);
        // against the rate a watched, settled run charges at (the C# rotor held at the header's 174.479 rad/s: 278.30 W): within 5%
        Assert.Equal(Settled().Generators["motor"].SettledPower, watts, 0.05 * watts);
    }

    [Fact]
    public void APausedSleepChargesPTimesTWithinOnePercent_UnderTheWind()
    {
        var rt = Settled();
        var bank = rt.Banks["bank"];
        var held = rt.HoldGenerators();
        var (id, gen, watts) = Assert.Single(held);
        Assert.Equal("motor", id);
        Assert.Equal(P, watts, 6);
        Assert.Equal(1, rt.GetField("motor", "held"));
        double charge0 = bank.Charge, wind0 = bank.Sources["wind"], t0 = rt.Time;
        Sleep(rt, 300);
        double added = bank.Charge - charge0;
        Assert.Equal(P * (rt.Time - t0), added, 1e-6 * added);              // P x T: exact, so well within 1%
        Assert.InRange(added / BatteryBank.JoulesPerWattHour, 23.19 * 0.99, 23.19 * 1.01);
        Assert.Equal(added, bank.Sources["wind"] - wind0, 6);                // filed under the prime mover, the wind
        Assert.Single(bank.Sources);
        Assert.Equal(P, gen.Delivered, 6);                                   // what the wire and the HUD read
        rt.ReleaseGenerators();
        Assert.False(gen.Held);
        double after = bank.Charge;
        Sleep(rt, 10);                                                        // released, and nothing turns the rotor: nothing
        Assert.Equal(after, bank.Charge);
    }

    [Fact]
    public void ASleepWakesWhenTheHeldChargeReachesItsCondition()
    {
        var rt = Settled();
        rt.Banks["bank"].Charge = 0;
        rt.HoldGenerators();
        var result = SleepSession.FastForward(rt, new WakeSpec("w", [new WakeTerm("bank", "charge", true, 50)], true, 3600, []));
        Assert.Equal(WakeReason.Condition, result.Reason);
        Assert.InRange(result.Elapsed, 50 * 3600 / P, 50 * 3600 / P + Dt + 1e-9);   // 646.8 s
    }

    [Fact]
    public void FullStopsIt()
    {
        var rt = Settled(capacityWh: 20);
        var bank = rt.Banks["bank"];
        var gen = rt.Generators["motor"];
        rt.HoldGenerators();
        double toFull = (bank.Capacity - bank.Charge) / P;                    // s
        Sleep(rt, toFull + 60);
        Assert.True(bank.Full);
        Assert.Equal(bank.Capacity, bank.Charge, 6);
        Assert.Equal(bank.Capacity, bank.Sources["wind"], 6);
        Assert.Equal(0, gen.Delivered);
        Assert.Equal(0, gen.Power);                                           // open circuit: nothing offered
    }

    [Fact]
    public void OutOfZeroTo45CStopsIt_AndBackInRangeItResumes()
    {
        var rt = Settled();
        var bank = rt.Banks["bank"];
        var cells = rt.HeatStores["cells"];
        rt.HoldGenerators();
        double c0 = bank.Charge;
        Sleep(rt, 60);
        double charge = bank.Charge;
        Assert.Equal(P * 60, charge - c0, 1e-6 * P * 60);                    // in range at 20 C: charging

        // cold: the cells at -10 C in the 20 C vault warm slowly; while under 0 C nothing is taken, step by step
        cells.Temperature = -10;
        Sleep(rt, 120, _ => { if (bank.Temperature < 0) Assert.Equal(charge, bank.Charge); });
        Assert.True(bank.Temperature < 0, $"still cold: {bank.Temperature:0.00} C");
        Assert.Equal(charge, bank.Charge);

        // hot: over 45 C, the same
        cells.Temperature = 60;
        Sleep(rt, 60, _ => { if (bank.Temperature > 45) Assert.Equal(charge, bank.Charge); });
        Assert.True(bank.Temperature > 45);
        Assert.Equal(charge, bank.Charge);

        // back in range: it resumes at the held power
        cells.Temperature = 20;
        Sleep(rt, 100);
        Assert.Equal(P * 100, bank.Charge - charge, 1e-6 * P * 100);
        Assert.Equal(bank.Charge, bank.Sources["wind"], 6);
    }

    [Fact]
    public void AGeneratorTheSimTurnsIsNotHeld_ItKeepsItsRealPowerAndNothingIsCountedTwice()
    {
        // bank-bench: small windmills the sim steps, straight on bench motors (ElectricsTests: 159.87 W into the warm bank)
        var held = Build("bank-bench");
        var plain = Build("bank-bench");
        Assert.Empty(held.HoldGenerators());
        for (int i = 0; i < 60 * 10; i++) { held.Step(0.1); plain.Step(0.1); }
        Assert.Equal(plain.Banks["warm"].Charge, held.Banks["warm"].Charge);
        Assert.Equal(plain.Generators["g-warm"].Power, held.Generators["g-warm"].Power);
        Assert.False(held.GeneratorsHeld);
    }

    [Fact]
    public void TheRecordChangesNothingAWatchedRunDoes()
    {
        // the record only reads: a generator that keeps one charges exactly as Apply always has (P x dt a tick) and loads the same torque
        var rt = Settled();
        var gen = rt.Generators["motor"];
        double charge = rt.Banks["bank"].Charge;
        double torque = gen.Step(Rotor, Dt);
        Assert.Equal(0.114592 * (Rotor - 157.0796), torque, 4);
        Assert.Equal(P * Dt, rt.Banks["bank"].Charge - charge, 9);
        Assert.False(gen.Held);
    }
}
