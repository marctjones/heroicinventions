using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #46: an opening that follows a lever's angle or a rope's pull. The Jolt-side coin dispenser is in heroic/tests/machine-behavior-test.rkt.</summary>
public class FollowTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private const double Bore = 0.012, G = 9.81, Cd = 0.6;

    private static BuildSession Fresh() => new(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "valves");

    /// <summary>An urn of 20 L over a 0.05 m2 floor (40 cm deep), a plugged spout 5 cm up: a head of 0.35 m.</summary>
    private static BuildSession Urn()
    {
        var s = Fresh();
        s.Execute("(tank urn #:at (0 0.5 0) #:area 0.05 #:height 0.6 #:water 0.02)");
        s.Execute("(leak spout #:at (0.16 0.55 0) #:on urn #:height 0.05 #:bore 0.012)");
        s.Execute("(lever beam #:at (1 0.7 0) #:length 1)");
        s.Execute("(follow plug #:lever beam #:from 0 #:to 5.73 #:set (spout lift) #:low 0 #:high 10)");
        return s;
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.001, 3.7699e-5)]                      // pi d lift: a third of the bore at 1 mm
    [InlineData(0.002, 7.5398e-5)]
    [InlineData(0.003, 1.13097e-4)]                     // a quarter of the bore: the curtain equals the bore
    [InlineData(0.006, 1.13097e-4)]                     // lifting further opens nothing more
    public void APlugPassesTheCurtainItLeavesUntilAQuarterOfTheBoreThenTheBore(double lift, double area) =>
        Assert.Equal(area, TankLeak.PlugArea(Bore, lift), precision: 8);

    [Fact]
    public void AnUrnSpoutPassesTorricellisFlowThroughThePartOpenedPlug()
    {
        var runtime = new MachineRuntime(Urn().Document.ToMachineDef(), Materials);
        Assert.Equal(0, runtime.GetField("spout", "area"));            // shut: nothing flows
        runtime.Step(0.01);
        Assert.Equal(0, runtime.GetField("spout", "flow"));

        foreach (double liftMm in new[] { 0.5, 1, 2, 3, 5, 10 })
        {
            runtime.SetField("spout", "lift", liftMm);
            runtime.Step(1e-4);                                        // one short step, the head has hardly moved
            double head = runtime.GetField("spout", "head") / 100;     // m over the hole
            double area = TankLeak.PlugArea(Bore, liftMm / 1000);
            double predicted = Cd * area * Math.Sqrt(2 * G * head) * 1000;   // L/s
            Assert.Equal(predicted, runtime.GetField("spout", "flow"), precision: 5);
        }
        // wide open at the full 35 cm head: 0.1778 L/s
        Assert.Equal(0.1778, Cd * Math.PI * Bore * Bore / 4 * Math.Sqrt(2 * G * 0.35) * 1000, precision: 4);
    }

    [Fact]
    public void AFollowMapsALeversAngleOntoTheFieldAndHoldsAtTheEnds()
    {
        var runtime = new MachineRuntime(Urn().Document.ToMachineDef(), Materials);
        // the view reads the lever's angle each tick and hands it over: 0 to 5.73 degrees is 0 to 10 mm
        var expected = new (double Degrees, double LiftMm)[] { (-3, 0), (0, 0), (1.0, 10 / 5.73), (2.865, 5), (5.73, 10), (30, 10) };
        foreach (var (degrees, liftMm) in expected)
        {
            runtime.ApplyFollow("plug", degrees);
            Assert.Equal(liftMm, runtime.GetField("spout", "lift"), precision: 2);
            Assert.Equal(degrees, runtime.GetField("plug", "input"));
        }
        runtime.ApplyFollow("plug", 1.0);
        Assert.Equal(TankLeak.PlugArea(Bore, 0.01 / 5.73) * 10000, runtime.GetField("spout", "area"), precision: 4);   // cm2
    }

    [Fact]
    public void AFollowRoundTripsThroughTheFileFormatCommandScriptAndExporter()
    {
        var original = Urn().Document.ToMachineDef();
        var reparsed = MachineDef.Parse(MachineWriter.Write(original));
        var f = Assert.Single(reparsed.Follows);
        Assert.Equal(("plug", "beam", null, 0.0, 5.73), (f.Id, f.Lever, f.Rope, f.From, f.To));
        Assert.Equal(("spout", "lift", 0.0, 10.0), (f.Target, f.Field, f.Low, f.High));

        var again = Fresh();
        foreach (string line in CommandScript.For(reparsed)) again.Execute(line);
        Assert.Equal(MachineWriter.Write(original), MachineWriter.Write(again.Document.ToMachineDef()));
        string rkt = RktExporter.Write(original);
        Assert.Contains("(follow plug #:lever beam", rkt);
        Assert.Contains("#:bore 0.012", rkt);
    }

    [Fact]
    public void ABadFollowIsReportedAtTheClause()
    {
        string Check(string follow)
        {
            var s = Fresh();
            s.Execute("(tank urn #:at (0 0.5 0) #:area 0.05 #:height 0.6)");
            s.Execute("(leak spout #:at (0.16 0.55 0) #:on urn #:height 0.05 #:bore 0.012)");
            s.Execute("(lever beam #:at (1 0.7 0) #:length 1)");
            s.Execute("(block stone #:at (2 0 0))");
            s.Execute(follow);
            return s.Execute("(check)");
        }
        Assert.Contains("not a lever", Check("(follow f #:lever stone #:from 0 #:to 5 #:set (spout lift))"));
        Assert.Contains("not a rope", Check("(follow f #:rope nothing #:from 0 #:to 5 #:set (spout lift))"));
        Assert.Contains("not a settable field", Check("(follow f #:lever beam #:from 0 #:to 5 #:set (spout colour))"));
        Assert.Contains("nothing changes", Check("(follow f #:lever beam #:from 5 #:to 5 #:set (spout lift))"));
        Assert.Contains("either a lever", Check("(follow f #:from 0 #:to 5 #:set (spout lift))"));
        Assert.StartsWith("ok:", Check("(follow f #:lever beam #:from 0 #:to 5 #:set (spout lift))"));
    }

    [Fact]
    public void RemovingTheLeverOrTheRopeTakesTheFollowWithIt()
    {
        var s = Urn();
        s.Execute("(remove beam)");
        Assert.Empty(s.Document.Follows);
        s.Execute("(undo)");
        Assert.Single(s.Document.Follows);

        s.Execute("(block a #:at (3 0 0))");
        s.Execute("(block b #:at (4 0 0))");
        s.Execute("(rope pull #:from (a 0 0 0) #:to (b 0 0 0) #:length 1)");
        s.Execute("(follow tug #:rope pull #:from 0 #:to 100 #:set (spout lift) #:low 0 #:high 10)");
        Assert.Equal(2, s.Document.Follows.Count);
        s.Execute("(remove pull)");                                 // the rope by its own id
        Assert.Equal("plug", Assert.Single(s.Document.Follows).Id);
    }
}
