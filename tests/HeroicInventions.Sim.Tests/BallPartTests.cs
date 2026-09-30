using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>The ball as a placeable part (issue #52). Its rolling is Jolt's, measured in heroic/tests/machine-behavior-test.rkt.</summary>
public class BallPartTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    [Fact]
    public void ARollingSolidSphereGainsSpeedAtFiveSeventhsOfGSinThetaAndSlidingBlockAtAll()
    {
        double g = 9.81, theta = 15 * Math.PI / 180;
        double rolling = g * Math.Sin(theta) / (1 + 2.0 / 5), sliding = g * Math.Sin(theta);
        Assert.Equal(5.0 / 7 * g * Math.Sin(theta), rolling, precision: 12);
        Assert.Equal(1.8136, rolling, precision: 4);
        Assert.Equal(2.539, sliding, precision: 3);
        // off a ramp of height h it leaves at sqrt(10 g h / 7), against sqrt(2 g h)
        double h = 1.5 * Math.Sin(theta);
        Assert.Equal(Math.Sqrt(10 * g * h / 7), Math.Sqrt(2 * rolling * 1.5), precision: 12);
        // on Mars both scale with g, so the path is the same and only the timing stretches, by sqrt(9.81/3.71) = 1.626
        double marsRolling = 3.71 * Math.Sin(theta) / (1 + 2.0 / 5);
        Assert.Equal(3.71 / 9.81, marsRolling / rolling, precision: 12);
        Assert.Equal(Math.Sqrt(9.81 / 3.71), Math.Sqrt(2 * 1.5 / marsRolling) / Math.Sqrt(2 * 1.5 / rolling), precision: 12);
    }

    [Fact]
    public void TheEditorPlacesABallAndItRunsAndExports()
    {
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        s.Execute("(ball marble #:at (0 1 0) #:radius 0.02 #:material steel)");
        Assert.Equal(0.02, s.Document.Parts["marble"].Number("radius"));
        s.Execute("(set marble #:radius 0.03)");
        Assert.StartsWith("ok:", s.Execute("(check)"));
        string rkt = RktExporter.Write(s.Document.ToMachineDef());
        Assert.Contains("(ball marble", rkt);
        Assert.Contains("#:radius 0.03", rkt);
        Assert.Contains("#:material steel", rkt);
        Assert.Contains("ball", PartTemplates.PrimitiveKinds);
        Assert.NotNull(new MachineRuntime(s.Document.ToMachineDef(), Materials));
    }

    [Fact]
    public void ShippedBallRampSurvivesTheEditorRoundTrip()
    {
        var def = MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "ball-ramp.machine")));
        Assert.Equal(["iron-ball", "bronze-ball"], def.Parts.Where(p => p.Kind == "ball").Select(p => p.Id));
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        foreach (string line in CommandScript.For(def)) s.Execute(line);
        Assert.Equal(0.06, s.Document.Parts["iron-ball"].Number("radius"));
        Assert.Equal(0.03, s.Document.Parts["bronze-ball"].Number("radius"));
    }
}
