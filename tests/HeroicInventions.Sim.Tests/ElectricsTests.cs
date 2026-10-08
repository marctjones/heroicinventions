using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #64, the found electrics: the generator's curve, the battery bank's charging window, the gear train between a slow prime
/// mover and a fast rotor, and the win, each against a number worked out by hand first (racket/machines/bank-bench.rkt and
/// found-electrics.rkt carry the working).
/// </summary>
public class ElectricsTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private const double Rpm = 2 * Math.PI / 60;

    private sealed class Spinner(double inertia, double omega) : IShaft
    {
        public double AngularVelocity { get; set; } = omega;
        public double ShaftInertia => inertia;
        public void AddAngularImpulse(double impulse) => AngularVelocity += impulse / inertia;
    }

    private static MachineRuntime Bench()
    {
        var rt = new MachineRuntime(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "bank-bench.machine"))), Materials);
        rt.SetField("scene", "clock-rate", 0);   // the clock is held and set by hand
        return rt;
    }

    // ---- the generator ----

    [Fact]
    public void ADefaultGeneratorGivesNothingUnder1500RpmAndRisesInAStraightLineToItsRatedTorqueAt2500()
    {
        var g = new Generator("g") { Bank = new BatteryBank("b", 3600) };
        double cut = 1500 * Rpm, rated = 2500 * Rpm;
        Assert.Equal(157.0796, cut, 4);
        Assert.Equal(261.7994, rated, 4);
        Assert.Equal(0, g.TorqueAt(1000 * Rpm));
        Assert.Equal(0, g.TorqueAt(cut));                       // the EMF only just equals the battery's voltage
        // k = 12 / (261.7994 - 157.0796) = 0.114592 N.m per rad/s; at 1,666.1 rpm (174.479 rad/s) 1.9938 N.m
        Assert.Equal(0.114592, g.Stiffness, 6);
        Assert.Equal(g.Stiffness * (174.479 - cut), g.TorqueAt(174.479), 9);
        Assert.Equal(1.9938, g.TorqueAt(174.479), 3);
        Assert.Equal(4.9183, g.TorqueAt(200), 3);               // 0.114592 x 42.9204
        Assert.Equal(12, g.TorqueAt(rated), 9);
        Assert.Equal(12, g.TorqueAt(5000 * Rpm), 9);            // held at the rated torque
        Assert.Equal(g.TorqueAt(200), g.TorqueAt(-200), 12);    // either way round
    }

    [Fact]
    public void ThePowerIntoTheBankIsEtaTauOmega()
    {
        // at 200 rad/s: tau 4.9183 N.m, shaft 983.7 W, 0.8 of it 786.9 W
        var bank = new BatteryBank("b", 3600 * 1000);
        var g = new Generator("g") { Bank = bank };
        g.Step(200, 1);
        Assert.Equal(4.9183 * 200, g.ShaftPower, 1);
        Assert.Equal(0.8 * 4.9183 * 200, g.Power, 1);
        Assert.Equal(786.9, bank.Charge, 0);                     // one second of it, in joules
        Assert.Equal(g.Power, g.Delivered, 9);
        g.Efficiency = 0.7;
        g.Step(200, 1);
        Assert.Equal(0.7 * 4.9183 * 200, g.Power, 1);
    }

    [Fact]
    public void NoChargeBelowTheCutInAndTheGearRatioNeededToReachItIsTheCutInOverThePrimeMoversSpeed()
    {
        var bank = new BatteryBank("b", 3600);
        var g = new Generator("g") { Bank = bank };
        // a windmill turns 13.33 rpm loaded, 28.65 free: 1,500 / 13.33 = 112.5, 1,500 / 28.65 = 52.4
        Assert.Equal(112.5, g.RatioForCutIn(13.333), 1);
        Assert.Equal(52.36, g.RatioForCutIn(28.648), 2);
        // two 5:1 stages turn the free sails (3.0 rad/s) at 25 x 3.0 = 75 rad/s = 716 rpm: nothing
        double rotor = 25 * 3.0;
        Assert.Equal(716.2, rotor / Rpm, 1);
        g.Step(rotor, 1);
        Assert.Equal(0, g.Torque);
        Assert.Equal(0, bank.Charge);
        // three stages: 125 x 3.0 = 375 rad/s (3,581 rpm) is over it
        Assert.True(g.TorqueAt(125 * 3.0) > 0);
    }

    [Fact]
    public void AnOpenCircuitLoadsTheShaftWithNothing()
    {
        var full = new BatteryBank("b", 3600, 3600);
        var g = new Generator("g") { Bank = full };
        Assert.Equal(0, g.LoadTorque(200));
        full.Charge = 1800;
        Assert.True(g.LoadTorque(200) > 0);
        full.AssumedTemperature = 60;    // too hot: no current, no torque
        Assert.Equal(0, g.LoadTorque(200));
        // and the torque is never more than the cap the view gives (what would stop the rotor in a tick)
        full.AssumedTemperature = 20;
        Assert.Equal(0.5, g.LoadTorque(200, 0.5), 12);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AfterNStagesAtNinetySevenPerCentTheGeneratorGets097ToTheNOfWhatTheDriverGave(int stages)
    {
        // a driver turned by a steady 100 N.m, n meshes of 5:1 at eta 0.97, a generator (cut-in lowered so every n reaches it) on the last shaft.
        // Steady, the power into the generator's shaft is 0.97^n of the driver's.
        const double dt = 1.0 / 120, eta = 0.97, input = 100;
        var driver = new Spinner(50, 0);
        var shafts = new List<Spinner> { driver };
        var links = new List<ShaftLink>();
        double ratio = 1;
        for (int i = 0; i < stages; i++)
        {
            var next = new Spinner(0.1 / Math.Pow(5, 2 * i), 0);
            links.Add(new ShaftLink(shafts[^1], next, -5, eta));
            shafts.Add(next);
            ratio *= 5;
        }
        var gen = new Generator("g") { Bank = new BatteryBank("b", 1e12), CutInRpm = 5, RatedRpm = 20000, RatedTorque = 100000 };
        double lastTorque = 0;
        for (double t = 0; t < 400; t += dt)
        {
            driver.AngularVelocity += input / driver.ShaftInertia * dt;
            var rotor = shafts[^1];
            double most = rotor.ShaftInertia * Math.Abs(rotor.AngularVelocity) / dt;
            lastTorque = gen.Step(rotor.AngularVelocity, dt, most);
            rotor.AngularVelocity -= Math.Sign(rotor.AngularVelocity) * lastTorque / rotor.ShaftInertia * dt;
            ShaftLink.StepAll(links, dt, 8);
        }
        double inPower = input * Math.Abs(driver.AngularVelocity);
        double outPower = lastTorque * Math.Abs(shafts[^1].AngularVelocity);
        Assert.Equal(Math.Pow(eta, stages), outPower / inPower, 2);
        Assert.Equal(ratio, Math.Abs(shafts[^1].AngularVelocity / driver.AngularVelocity), 3);
    }

    [Fact]
    public void ALoadedWindmillSlowsFromItsFreeSpeedToWhereItsTorqueMeetsTheGeneratorsAndTheBankGets160Watts()
    {
        // bank-bench's windmill: 2 m sails, 30 kg, 5 m/s; free 12.5 rad/s; against k = 3.1831 above 3.1416 rad/s: 9.6484 rad/s
        var rt = Bench();
        for (int i = 0; i < 600; i++) rt.Step(0.1);       // 60 s: settled (tau 3.8 s) and not yet full (about 119 s)
        var sails = rt.Windmills["w-warm"];
        double tauStar = 0.5 * Physics.AirDensity * Math.PI * 4 * 25 * 2 * 0.3 / 2.5;
        Assert.Equal(45.394, tauStar, 3);
        Assert.Equal(9.6484, sails.AngularVelocity, 3);
        Assert.Equal(12.5 - 2.8516, sails.AngularVelocity, 3);               // the free speed less what the load takes
        Assert.Equal(20.712, rt.GetField("g-warm", "torque"), 2);
        Assert.Equal(rt.GetField("g-warm", "torque"), sails.Torque, 2);      // the sails give what the generator takes: steady
        Assert.Equal(159.87, rt.GetField("g-warm", "power"), 1);
        Assert.Equal(5.7095, rt.GetField("g-warm", "current"), 3);
        // the load is the generator's alone: the sails' own load is still 0
        Assert.Equal(rt.GetField("g-warm", "torque"), sails.Load, 9);
    }

    [Fact]
    public void ABankOf5WhFillsInItsCapacityOverThePowerAndThenTheGeneratorGoesOpenCircuitAndTheSailsRunFree()
    {
        var rt = Bench();
        double t = 0;
        while (rt.GetField("warm", "full") == 0 && t < 400) { rt.Step(0.05); t += 0.05; }
        // 18,000 J / 159.87 W = 112.6 s of charging, after the sails' start-up: they take 6.5 s to reach the cut-in's 3.14 rad/s and settle
        Assert.InRange(t, 112.6, 120);
        Assert.Equal(5, rt.GetField("warm", "charge"), 6);
        Assert.Equal(1, rt.GetField("warm", "full"));
        for (int i = 0; i < 1000; i++) rt.Step(0.1);                       // 100 s: the free sails settle in 5.5 s (I / (tau* / w*))
        Assert.Equal(0, rt.GetField("g-warm", "torque"));                    // open circuit
        Assert.Equal(12.5, rt.Windmills["w-warm"].AngularVelocity, 3);        // free again: lambda 5
        Assert.Equal(5, rt.GetField("warm", "charge"), 9);                   // and it holds
    }

    [Fact]
    public void TheBankRecordsWhatDroveTheGeneratorThatChargedItAndTheTotalsAgree()
    {
        var rt = Bench();
        for (int i = 0; i < 1600; i++) rt.Step(0.5);   // 800 s: the hot bank cools through 45 C at 583 s and fills by about 700
        Assert.Equal(5, rt.GetField("warm", "from-wind"), 6);                // a windmill: wind
        Assert.Equal(5, rt.GetField("hot", "from-falling-weight"), 6);       // named by the generator (#:driven-by)
        Assert.Equal(5, rt.GetField("warm", "charge"), 6);
        Assert.Equal(5, rt.Banks["warm"].Sources["wind"] / 3600, 6);
    }

    // ---- the bank ----

    [Fact]
    public void ABankTakesChargeOnlyFromZeroTo45CelsiusInclusive()
    {
        var bank = new BatteryBank("b", 3600 * 10);
        foreach (var (t, ok) in new[] { (-40.0, false), (-0.01, false), (0.0, true), (20.0, true), (45.0, true), (45.01, false), (200.0, false) })
        {
            bank.AssumedTemperature = t;
            double before = bank.Charge;
            double taken = bank.Offer(100, "wind");
            Assert.Equal(ok ? 100 : 0, taken);
            Assert.Equal(before + taken, bank.Charge);
        }
        Assert.Equal(300, bank.Charge);                                     // 0, 20 and 45 C
    }

    [Fact]
    public void ABankDoesNotTakeMoreThanItsCapacityAndHoldsWhatItHasInTheColdAndTheHeat()
    {
        var bank = new BatteryBank("b", 1000, 900);
        Assert.Equal(100, bank.Offer(500, "wind"));
        Assert.True(bank.Full);
        Assert.Equal(0, bank.Offer(1, "wind"));
        foreach (double t in new[] { -80.0, 200.0 }) { bank.AssumedTemperature = t; Assert.Equal(1000, bank.Charge); }   // no discharge, no damage
    }

    [Fact]
    public void ABankInAColdStoreRefusesChargeUntilItWarmsThroughZeroAndAHotOneUntilItCoolsThrough45()
    {
        // bank-bench: cold cells at -10 C in 20 C air through 5 W/K on 16 kJ/K: 0 C at 3,200 ln(30/20) = 1,297.5 s;
        // hot at 50 C: 45 C at 3,200 ln(30/25) = 583.4 s
        var rt = Bench();
        double coldCrossed = -1, hotCrossed = -1;
        for (double t = 0; t < 1500; t += 0.5)
        {
            rt.Step(0.5);
            double coldT = rt.GetField("cold", "temperature"), hotT = rt.GetField("hot", "temperature");
            if (coldT < 0) Assert.Equal(0, rt.GetField("cold", "charge"));   // no charge below 0 C
            if (hotT > 45) Assert.Equal(0, rt.GetField("hot", "charge"));    // none above 45 C
            if (coldCrossed < 0 && coldT >= 0) coldCrossed = t + 0.5;   // (t is the start of the step)
            if (hotCrossed < 0 && hotT <= 45) hotCrossed = t + 0.5;
        }
        Assert.InRange(coldCrossed, 1297.4, 1298.0);   // worked 1,297.5, to the step
        Assert.InRange(hotCrossed, 583.4, 584.0);      // worked 583.4
        Assert.Equal(5, rt.GetField("hot", "charge"), 6);                   // resumed and filled (about 113 s later)
        Assert.InRange(rt.GetField("cold", "charge"), 4.9, 5);              // the cold one has had 1,500 - 1,297.5 = 202 s: full
    }

    [Fact]
    public void WhatTheBankIsInSetsItsTemperatureAnEnclosureOrTheCellsStore()
    {
        var rt = Bench();
        Assert.Equal(-10, rt.GetField("cold", "temperature"), 9);
        Assert.Equal(rt.HeatStores["cold-cells"].Temperature, rt.Banks["cold"].Temperature, 12);
        Assert.Equal(0, rt.GetField("cold", "accepting"));
        Assert.Equal(1, rt.GetField("warm", "accepting"));
    }

    // ---- the win ----

    [Theory]
    [InlineData(2.98333, false)]   // 02:59
    [InlineData(3.0, true)]        // 03:00, the pass opens
    [InlineData(3.1, true)]        // 03:06
    [InlineData(3.16, true)]       // 03:09:36
    [InlineData(3.17167, false)]   // 03:10:18, shut
    [InlineData(3.18333, false)]   // 03:11
    [InlineData(15.0, false)]      // 15:00: the other pass is not the call
    public void TheWindowIsTenMinutesFrom0300(double hour, bool open)
    {
        Assert.Equal(open, new BatteryBank("b", 100).InWindow(hour));
    }

    [Fact]
    public void AFullWarmBankWinsAt0300ButNotAt0259()
    {
        var rt = Bench();
        rt.SetField("scene", "time", 2.98333);
        rt.Step(1);
        Assert.Equal(0, rt.GetField("ok", "won"));        // (the easy bank has already won at 02:59: any hour)
        Assert.Equal(1, rt.GetField("scene", "won"));
        rt.SetField("scene", "time", 3.0);
        rt.Step(1);
        Assert.Equal(1, rt.GetField("ok", "won"));
        Assert.Equal(1, rt.GetField("scene", "won"));
        Assert.True(rt.Won);
        Assert.Equal(1, rt.Banks["ok"].WonAtSol);
        Assert.Equal(3.0, rt.Banks["ok"].WonAtHour, 3);
    }

    [Fact]
    public void TheCallDoesNotGoOutAt0311EvenWithAFullBankAndGoesAt0306()
    {
        var rt = Bench();
        rt.SetField("scene", "time", 3.18333);   // the pass has shut
        rt.Step(1);
        rt.SetField("late", "charge", 5);        // full at 03:11
        rt.Step(1);
        Assert.Equal(1, rt.GetField("late", "full"));
        Assert.Equal(0, rt.GetField("late", "won"));
        rt.SetField("scene", "time", 3.1);       // 03:06
        rt.Step(1);
        Assert.Equal(1, rt.GetField("late", "won"));
    }

    [Fact]
    public void AFullBankOutsideZeroTo45NeverMakesTheCallInTheWindow()
    {
        var rt = Bench();
        for (double hour = 3.0; hour < 3.16; hour += 0.02)
        {
            rt.SetField("scene", "time", hour);
            rt.Step(1);
            Assert.Equal(0, rt.GetField("hot-w", "won"));     // 50 C
            Assert.Equal(0, rt.GetField("cold-w", "won"));    // -5 C
        }
        Assert.Equal(1, rt.GetField("hot-w", "full"));
        Assert.Equal(0, rt.GetField("hot-w", "ready"));
        Assert.Equal(1, rt.GetField("ok", "won"));            // while the warm one went out in the same window
    }

    [Fact]
    public void TheEasyCallAnyTimeSettingWinsAtNoonAndCanBeSetOnAnyBank()
    {
        var rt = Bench();                                      // noon
        rt.Step(1);
        Assert.Equal(1, rt.GetField("easy", "won"));
        Assert.Equal(0, rt.GetField("ok", "won"));
        rt.SetField("ok", "call-any-time", 1);
        rt.Step(1);
        Assert.Equal(1, rt.GetField("ok", "won"));
    }

    [Fact]
    public void TheWinLatchesAndTheBankCanBeResetByAPersonButNotBrokenByHeatOrCold()
    {
        var rt = Bench();
        rt.SetField("scene", "time", 3.0); rt.Step(1);
        Assert.Equal(1, rt.GetField("ok", "won"));
        rt.SetField("scene", "time", 9.0); rt.Step(1);
        Assert.Equal(1, rt.GetField("ok", "won"));
        // heat and cold stop it charging and nothing else: its charge is whatever it was
        rt.HeatStores["ok-cells"].AddHeat(1e7);
        for (int i = 0; i < 10; i++) rt.Step(1);
        Assert.True(rt.GetField("ok", "temperature") > 100);
        Assert.Equal(5, rt.GetField("ok", "charge"), 9);
        Assert.Equal(0, rt.GetField("ok", "accepting"));
    }

    [Fact]
    public void ABankAndItsSourceRecordSurviveASaveAndALoad()
    {
        var rt = Bench();
        for (int i = 0; i < 300; i++) rt.Step(0.5);
        var saved = RuntimeState.Capture(rt);
        var again = new MachineRuntime(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "bank-bench.machine"))), Materials);
        var unmatched = RuntimeState.Restore(again, saved);
        Assert.Empty(unmatched);
        Assert.Equal(rt.GetField("warm", "charge"), again.GetField("warm", "charge"), 9);
        Assert.Equal(rt.Banks["warm"].Sources["wind"], again.Banks["warm"].Sources["wind"], 6);
        Assert.Equal(rt.GetField("hot", "charge"), again.GetField("hot", "charge"), 9);
    }

    // ---- the editor ----

    [Fact]
    public void TheEditorPlacesAGeneratorAndABankAndExportsThem()
    {
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        s.Execute("(heat-store cells #:at (0 0 0) #:mass 16 #:material iron)");
        s.Execute("(windmill sails #:at (0 3 0) #:material oak)");
        s.Execute("(battery-bank bank #:at (0.5 0 0) #:material iron)");
        s.Execute("(generator motor #:at (0 3 -0.4) #:material iron)");
        s.Execute("(set bank #:in cells)");
        s.Execute("(set motor #:on sails)");
        s.Execute("(set motor #:charges bank)");
        Assert.Throws<InvalidOperationException>(() => s.Execute("(set motor #:charges nothing-here)"));
        s.Execute("(set bank #:capacity 10)");
        string rkt = RktExporter.Write(s.Document.ToMachineDef());
        Assert.Contains("(battery-bank bank #:at (0.5 0 0) #:in cells #:capacity 10 #:charge 0 #:volts 28 #:call-hour 3 #:call-minutes 10 #:material iron)", rkt);
        Assert.Contains("(generator motor #:at (0 3 -0.4) #:on sails #:charges bank #:efficiency 0.8 #:cut-in-rpm 1500 #:rated-rpm 2500 #:rated-torque 12 #:material iron)", rkt);
        Assert.Contains("generator", PartTemplates.PrimitiveKinds);
        Assert.Contains("battery-bank", PartTemplates.PrimitiveKinds);
    }

    [Fact]
    public void AMachineWithAGeneratorOnSomethingThatDoesNotTurnIsRefused()
    {
        var def = MachineDef.Parse("""
            (machine m
              (part cells heat-store (material iron) (at 0 0 0) (props (mass 1) (contents iron) (temperature #f) (area #f) (emissivity 0.9) (conductance 0)) (ports))
              (part bank battery-bank (material iron) (at 0 0 0) (props (in cells) (capacity 1) (charge 0)) (ports))
              (part g generator (material iron) (at 0 0 0) (props (on cells) (charges bank)) (ports)))
            """);
        var e = Assert.Throws<MachineFormatException>(() => new MachineRuntime(def, Materials));
        Assert.Contains("not a wheel, windmill", e.Message);
    }
}
