using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #97, the bimetal strip: Timoshenko's curvature and tip deflection against hand numbers, the time constant, the lid it works,
/// and the strip on a machine.
/// </summary>
public class BimetalTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static BimetalStrip BrassSteel(double travel = 0.0021) =>
        new("strip", Materials["brass"], Materials["steel"], 0.1, 0.001, 0.01) { Travel = travel };

    [Fact]
    public void TwoEqualLayersOfEqualModulusBendOnThreeDeltaAlphaDeltaTOverTwoT()
    {
        // d_alpha = 7e-6, dT = 25 K, t = 1 mm: kappa = 3 x 7e-6 x 25 / (2 x 0.001) = 0.2625 /m, and a 60 mm strip's tip moves
        // kappa L^2 / 2 = 0.2625 x 0.0036 / 2 = 0.4725 mm = 3 d_alpha dT L^2 / (4 t)
        double kappa = BimetalStrip.Curvature(19e-6, 12e-6, 100e9, 100e9, 0.0005, 0.0005, 25);
        Assert.Equal(0.2625, kappa, 9);
        double tip = BimetalStrip.TipDeflection(kappa, 0.06);
        Assert.Equal(0.4725e-3, tip, 6);
        Assert.Equal(3 * 7e-6 * 25 * 0.06 * 0.06 / (4 * 0.001), 0.4725e-3, 12);
        Assert.InRange(tip / 0.4725e-3, 0.9999, 1.0);                       // the arc is a hair under the small-angle value
    }

    [Fact]
    public void BrassOnSteelBends0012023PerMetrePerKelvinAndTheTipMoves60Micrometres()
    {
        // brass 19.9e-6, E 110 GPa (layer 2); steel 11.7e-6, E 200 GPa (layer 1); 0.5 mm each: m = 1, n = 200/110 = 1.81818,
        // kappa/dT = 6 x 8.2e-6 x 4 / (0.001 x (12 + (1 + 1.81818)(1 + 1/1.81818))) = 1.968e-4 / (0.001 x 16.3682) = 0.012023 /m/K
        var strip = BrassSteel();
        Assert.Equal(0.012023, strip.CurvatureAt(21), 6);
        // tip at 1 K: 0.012023 x 0.1^2 / 2 = 60.12 um
        Assert.Equal(60.12e-6, strip.DeflectionAt(21), 8);
        Assert.Equal(60.12e-6 * 20, strip.DeflectionAt(40), 6);              // 1.202 mm at the shut temperature, 20 K over flat
        Assert.Equal(-60.12e-6 * 75, strip.DeflectionAt(-55), 5);            // and 4.5 mm the other way at -55 C
        Assert.Equal(0, strip.DeflectionAt(20), 12);                         // flat where it was made
    }

    [Fact]
    public void TheLidIsShutAt40AndWideOpenAfter2Point1MmOfTipMovementWhichIs34Point93K()
    {
        var strip = BrassSteel();
        Assert.Equal(34.93, strip.Span, 1);                                   // 2.1 mm / 60.12 um/K
        Assert.Equal(40 - 34.93, strip.OpenAt, 1);                            // wide open at 5.07 C
        strip.Temperature = 40; Assert.Equal(0, strip.Opening, 9);
        strip.Temperature = 50; Assert.Equal(0, strip.Opening, 9);            // shut stays shut
        strip.Temperature = 40 - strip.Span / 2; Assert.Equal(0.5, strip.Opening, 3);
        strip.Temperature = 5; Assert.Equal(1, strip.Opening, 9);
        strip.Temperature = -55; Assert.Equal(1, strip.Opening, 9);
        strip.Temperature = 30; Assert.Equal(10 / 34.93, strip.Opening, 3);   // 29% open
    }

    [Fact]
    public void TheStripTakesTheStoresTemperatureWithATimeConstantOf160Seconds()
    {
        // C = (0.5 x 8530 x 380 + 0.5 x 7850 x 490) x 1e-6 m3 = 1.6207 + 1.9233 = 3.544 J/K; A = 2(0.1 x 0.01 + 0.1 x 0.001 + 0.01 x 0.001) = 2.22e-3 m2;
        // tau = C / (h A) = 3.544 / (10 x 2.22e-3) = 159.6 s
        var strip = BrassSteel();
        Assert.Equal(3.544, strip.HeatCapacity, 3);
        Assert.Equal(2.22e-3, strip.Surface, 9);
        Assert.Equal(159.6, strip.TimeConstant, 1);
        // put against something at 60 C from 0 C: after one tau it has risen 63.2% of the way, 37.93 C; after five, 99.3%
        double sensed = 0;
        strip.Sensed = () => sensed; strip.Reset();
        sensed = 60;
        double tau = strip.TimeConstant;
        for (double t = 0; t < tau - 1e-9; t += 1) strip.Step(Math.Min(1, tau - t));
        Assert.Equal(60 * (1 - Math.Exp(-1)), strip.Temperature, 6);
        for (double t = 0; t < 4 * tau - 1e-9; t += 1) strip.Step(Math.Min(1, 4 * tau - t));
        Assert.Equal(60 * (1 - Math.Exp(-5)), strip.Temperature, 6);
    }

    [Fact]
    public void ALongerThinnerStripBendsMoreAndTheLayersWrongWayRoundBendTheOtherWay()
    {
        var strip = BrassSteel();
        var longer = new BimetalStrip("long", Materials["brass"], Materials["steel"], 0.2, 0.001, 0.01);
        Assert.Equal(4, longer.DeflectionAt(30) / strip.DeflectionAt(30), 2);   // tip movement goes as L^2
        var thinner = new BimetalStrip("thin", Materials["brass"], Materials["steel"], 0.1, 0.0005, 0.01);
        Assert.Equal(2, thinner.DeflectionAt(30) / strip.DeflectionAt(30), 2);  // and as 1 / t
        var backwards = new BimetalStrip("back", Materials["steel"], Materials["brass"], 0.1, 0.001, 0.01);
        Assert.True(backwards.DeflectionAt(30) < 0 && strip.DeflectionAt(30) > 0);
        Assert.Equal(-strip.DeflectionAt(30), backwards.DeflectionAt(30), 5);    // equal and opposite; steel opens the lid when warm, which is a poor thermostat
    }

    [Fact]
    public void AStripWorksTheLidOfTheBinOnAMachineAndTakesThePlaceOfTheIdealSwitch()
    {
        var rt = new MachineRuntime(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "bimetal-night.machine"))), Materials);
        var strip = rt.Bimetals["strip-strip"];
        Assert.Same(rt.HeatBins["strip-bin"], strip.Bin);
        Assert.Null(rt.HeatBins["strip-bin"].Sense);
        Assert.NotNull(rt.HeatBins["ideal-bin"].Sense);
        Assert.Equal(159.6, rt.GetField("strip-strip", "tau"), 1);
        // starts at the bank's -55 C: dT = -75 K, the tip 4.5 mm back from flat, the lid wide open
        Assert.Equal(-55, rt.GetField("strip-strip", "temperature"), 9);
        Assert.Equal(-4.51, rt.GetField("strip-strip", "deflection"), 1);
        Assert.Equal(1, rt.GetField("strip-bin", "open"), 9);
        // the warm bank (30 C) starts 29% open
        Assert.Equal(10 / 34.93, rt.GetField("warm-bin", "open"), 3);
        // and the adjusting screw: shut at 35 C instead
        rt.SetField("warm-strip", "shut-at", 35);
        rt.Step(1);
        Assert.Equal(5 / 34.93, rt.GetField("warm-bin", "open"), 2);
    }

    [Fact]
    public void TheEditorPlacesAStripAndExportsIt()
    {
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        s.Execute("(heat-store rock #:at (0 0 0) #:mass 40 #:material basalt)");
        s.Execute("(heat-store bank #:at (0.2 0 0) #:mass 16 #:material iron)");
        s.Execute("(heat-bin bin #:at (0 0 0) #:holds rock #:material oak)");
        s.Execute("(bimetal strip #:at (0 0.5 0) #:material steel)");
        s.Execute("(set strip #:senses bank)");
        s.Execute("(set strip #:drives bin)");
        Assert.Throws<InvalidOperationException>(() => s.Execute("(set strip #:drives nothing-here)"));
        string rkt = RktExporter.Write(s.Document.ToMachineDef());
        Assert.Contains("(bimetal strip #:at (0 0.5 0) #:senses bank #:drives bin #:layers (brass steel) #:length 0.1 #:thickness 0.001 #:width 0.01 #:high-share 0.5 #:shut-at 40 #:straight-at 20 #:travel 0.0021 #:contact 10 #:material steel)", rkt);
        Assert.Contains("bimetal", PartTemplates.PrimitiveKinds);
    }
}
