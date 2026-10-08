using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Fields a person operates a machine with (issues #154, #156): fill, drain, shut-off, turn over, and the
/// driven hinge's settings. Each prediction is worked out in a comment before the run.
/// </summary>
public class OperatingFieldsTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static MachineRuntime Load(string name) =>
        new(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", name + ".machine"))), Materials);

    private static void Run(MachineRuntime run, double seconds, double dt = 0.01)
    {
        for (int i = 0, n = (int)Math.Round(seconds / dt); i < n; i++) run.Step(dt);
    }

    /// <summary>
    /// aeolipile.rkt: a 3000 W fire under the kettle. Start it with 0.1 kg and it boils when 0.1 x 4186 x 80 =
    /// 33.5 kJ plus a little lost to the air (2 W/K) has gone in: about 11.4 s. Then fill it to 0.3 kg with feed at
    /// the air's 20 C: the 0.2 kg added mix in at once, and to bring the lot back to the boil takes the added
    /// mass x c x (100 - 20) = 0.2 x 4186 x 80 = 66,976 J. The fire gives 3000 W less the loss, 2 W/K on a kettle
    /// that climbs from 46.7 to 100 C (mean 73.3, so 106.7 W): 66,976 / (3000 - 106.7) = 23.15 s.
    /// </summary>
    [Fact]
    public void RefillingABoilingKettleTakesTheHeatBackAndTheNextBoilComesLater()
    {
        var run = Load("aeolipile");
        run.SetField("kettle", "water", 0.1);
        Assert.Equal(0.1, run.GetField("kettle", "water"), precision: 9);
        while (run.GetField("kettle", "temperature") < 99.99 && run.Time < 60) run.Step(0.01);
        Assert.InRange(run.Time, 11.0, 11.8);                                             // the 0.1 kg boiled first
        double boiling = run.GetField("kettle", "temperature"), had = run.GetField("kettle", "water");

        run.SetField("kettle", "water", 0.3);
        double added = 0.3 - had;
        Assert.Equal((had * boiling + added * 20) / 0.3, run.GetField("kettle", "temperature"), precision: 6);   // mixed at once, ~46.7 C
        double predicted = added * 4186 * (boiling - 20) / (3000 - 2.0 * ((run.GetField("kettle", "temperature") + boiling) / 2 - 20));
        double from = run.Time;
        while (run.GetField("kettle", "temperature") < 99.99 && run.Time - from < 100) run.Step(0.01);
        Assert.Equal(23.15, predicted, tolerance: 0.05);                                  // the hand figure
        Assert.Equal(predicted, run.Time - from, tolerance: 0.35);

        run.SetField("kettle", "water", 0.05);                                            // bled: less water, the same temperature
        Assert.Equal(0.05, run.GetField("kettle", "water"), precision: 9);
        Assert.InRange(run.GetField("kettle", "temperature"), 99.9, 100.5);
    }

    /// <summary>
    /// cistern.rkt: 400 L. Open the tap to 5 L/s for 20 s and 100 L have come out: 300 L left. Shut again and
    /// nothing more comes. Opened to 50 L/s, the 300 L go in 6 s and it stops at 0.
    /// </summary>
    [Fact]
    public void ALoneCisternCanBeDrawnThroughItsTap()
    {
        var run = Load("cistern");
        Run(run, 5);
        Assert.Equal(400, run.GetField("cistern", "water"), precision: 6);                // nobody draws it
        run.SetField("cistern", "tap", 5);
        Run(run, 20);
        Assert.Equal(300, run.GetField("cistern", "water"), precision: 4);
        Assert.Equal(100, run.GetField("cistern", "tapped"), precision: 4);
        run.SetField("cistern", "tap", 0);
        Run(run, 5);
        Assert.Equal(300, run.GetField("cistern", "water"), precision: 4);
        run.SetField("cistern", "tap", 50);
        Run(run, 10);
        Assert.Equal(0, run.GetField("cistern", "water"), precision: 9);
        Assert.Equal(400, run.GetField("cistern", "tapped"), precision: 4);
    }

    /// <summary>
    /// sand-timer.rkt: 5 kg through a 10 mm orifice at 25.9 g/s. Turned over at 30 s, 30 x 0.0259 = 0.777 kg
    /// is below, so the top has 0.777 kg and empties 30 s later, at 60 s: a timer turned part-way runs for
    /// the time it had run. Turned the moment it is empty it runs its full 5 / 0.0259 = 193.0 s again.
    /// </summary>
    [Fact]
    public void ATurnedSandTimerRunsAgainForTheTimeItHadRun()
    {
        double flow = Sim.Mechanics.Hopper.FlowFor(0.010, 0.0003, 1600, 9.81);
        Assert.Equal(0.0259, flow, precision: 4);
        var run = Load("sand-timer");
        Run(run, 30);
        double left = run.GetField("sand", "mass");
        Assert.Equal(5 - 30 * flow, left, precision: 6);                                   // 4.223 kg
        run.SetField("sand", "turn", 1);
        Assert.Equal(30 * flow, run.GetField("sand", "mass"), precision: 6);               // the 0.777 kg that had run out
        Assert.Equal(left, run.GetField("sand", "drained"), precision: 6);                 // and the rest is below
        while (run.GetField("sand", "empty") == 0 && run.Time < 200) run.Step(0.01);
        Assert.Equal(60.0, run.Time, tolerance: 0.05);

        // the whole 5 kg are below now: turn it and it runs the full time
        run.SetField("sand", "turn", 1);
        Assert.Equal(5.0, run.GetField("sand", "mass"), precision: 6);
        double again = run.Time;
        while (run.GetField("sand", "empty") == 0 && run.Time - again < 400) run.Step(0.01);
        Assert.Equal(5 / flow, run.Time - again, tolerance: 0.05);                         // 193.0 s
    }

    /// <summary>
    /// water-wheels.rkt: the 20 L/s spring runs the overshot wheel at 14.1 rpm. Shut at 60 s, the wheel still has
    /// what stands over the header's lip: the race is a broad-crested weir, Q = 1.705 b h^1.5 with b = 0.3 m, so
    /// 20 L/s is h0 = (0.02 / 0.5115)^(2/3) = 0.1151 m over the lip, and the 0.5 m2 header drains by dh/dt =
    /// -1.023 h^1.5, 1/sqrt(h) = 1/sqrt(h0) + 0.5115 t. After 6 s that is h = 0.0276 m, Q = 0.5115 h^1.5 =
    /// 2.35 L/s: the race is nearly dry, too little to hold 300 N.m, and the buckets (a bucket takes 120 deg /
    /// 1.47 rad/s = 1.42 s to carry its water round to the spill) empty out. So the wheel stops within
    /// 6 + 1.42 = 7.4 s of the shut-off.
    /// </summary>
    [Fact]
    public void ShuttingASpringStopsTheOvershotWheelWhenItsBucketsEmpty()
    {
        var run = Load("water-wheels");
        Run(run, 60);
        Assert.Equal(14.1, run.GetField("overshot", "rpm"), tolerance: 0.1);
        run.SetField("spring", "inflow", 0);
        Assert.Equal(0, run.GetField("spring", "inflow"));
        Run(run, 1);
        Assert.InRange(run.GetField("overshot", "rpm"), 12, 14.1);                        // the header's store still turns it
        Run(run, 5);                                                                       // t = 66
        Assert.Equal(2.35, run.GetField("race", "flow"), tolerance: 0.05);                 // the weir's figure
        Run(run, 1.4);                                                                     // 7.4 s after the shut-off
        Assert.Equal(0, run.GetField("overshot", "rpm"), tolerance: 0.01);
        Assert.Equal(0, run.GetField("spring", "flow"));                                   // and nothing is coming in
        Assert.Equal(22.3, run.GetField("undershot", "rpm"), tolerance: 0.1);              // the river's wheel on its own spring is not touched
    }

    /// <summary>Every driven hinge in the gallery offers drive-rpm and drive-torque.</summary>
    [Theory]
    [InlineData("roman-crane", "tympanus", 3, 1545.075)]
    [InlineData("ratchet-windlass", "wind-drum", 6, 40)]
    [InlineData("trip-hammer", "strong-wheel", 30, 20)]
    [InlineData("archimedes-screw", "cochlea", 12, 150)]
    [InlineData("gristmill", "weak", 120, 200)]
    public void DrivenHingesOfferTheirSpeedAndTorque(string machine, string wheel, double rpm, double torque)
    {
        var run = Load(machine);
        Assert.Contains(wheel, run.Drives.Keys);
        Assert.Equal(rpm, run.GetField(wheel, "drive-rpm"));
        Assert.Equal(torque, run.GetField(wheel, "drive-torque"), tolerance: 0.01);
        run.SetField(wheel, "drive-rpm", -rpm);
        Assert.Equal(-rpm, run.Drives[wheel].Rpm);
        run.SetField(wheel, "drive-torque", -5);                                            // below nothing is nothing: let go
        Assert.Equal(0, run.Drives[wheel].Torque);
    }

    [Fact]
    public void AMillstonesGrindCanBeLightenedAtRunTime()
    {
        var run = Load("gristmill");
        Assert.Equal(267, run.GetField("weak", "grind-torque"));
        run.SetField("weak", "grind-torque", 150);
        Assert.Equal(150, run.Drives["weak"].Grind);
    }
}
