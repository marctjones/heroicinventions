using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// The galvanic jar ("Baghdad battery"): Eggebrecht's replica in 5% vinegar,
/// 0.5 V at 0.15 mA, until the acid is spent. No chemistry or circuit beyond
/// counting the acid's charge: 50 g/L of acetic acid (60.05 g/mol), one
/// electron a molecule.
/// </summary>
public class GalvanicJarTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private const double Year = 365.25 * 86400;

    [Fact]
    public void AReplicaJarOfVinegarPasses3615CoulombsOver279Days()
    {
        var jar = new GalvanicJar("j", 1, 0.5, 0.15, 4.5e-5);
        Assert.Equal(3615.2, jar.Capacity, precision: 1);          // 45 mL × 832.6 mol/m³ × 96,485 C/mol
        Assert.Equal(75e-6, jar.Power, precision: 12);             // 0.5 V × 0.15 mA
        Assert.Equal(279.0, jar.TimeLeft / 86400, precision: 0);   // 3,615 C / 0.15 mA
    }

    [Fact]
    public void ADropOfVinegarIsSpentIn15HoursAndThenGivesNothing()
    {
        var jar = new GalvanicJar("drops", 1, 0.5, 0.15, 1e-7);
        double charge = 1e-7 * GalvanicJar.AcidPerCubicMetre * GalvanicJar.Faraday;   // 8.034 C
        double spentAt = charge / 0.15e-3;                                             // 53,558 s
        Assert.Equal(53558, spentAt, precision: 0);
        for (double t = 0; t < spentAt - 60; t += 60) jar.Step(60);
        Assert.False(jar.Spent);
        Assert.Equal(0.5, jar.Voltage);
        for (int i = 0; i < 3; i++) jar.Step(60);
        Assert.True(jar.Spent);
        Assert.Equal(0, jar.Voltage);
        Assert.Equal(0, jar.Power);
        Assert.Equal(1.0, jar.SpentFraction, precision: 12);
        Assert.Equal(0.5 * charge, jar.Delivered, precision: 9);     // 4.017 J, never more than the acid held
    }

    [Fact]
    public void AJarSwitchedOffKeepsItsCharge()
    {
        var jar = new GalvanicJar("j", 1, 0.5, 0.15, 4.5e-5) { On = false };
        for (int i = 0; i < 1000; i++) jar.Step(3600);
        Assert.Equal(0, jar.SpentFraction);
        Assert.Equal(0, jar.Delivered);
        Assert.Equal(0.5, jar.Voltage);   // still there to be read
        Assert.True(double.IsPositiveInfinity(jar.TimeLeft));
    }

    /// <summary>The trap the game teaches: a 5 kWh bank (18 MJ) from 75 µW jars.</summary>
    [Fact]
    public void FillingTheRoversBankFromOneJarTakes7600YearsAndTenThousandJarsOfVinegar()
    {
        var jar = new GalvanicJar("j", 1, 0.5, 0.15, 4.5e-5);
        double bank = 5 * 3.6e6;
        Assert.Equal(7605, bank / jar.Power / Year, precision: 0);
        Assert.Equal(9958, Math.Round(bank / (jar.Capacity * jar.Volts)));
    }

    [Fact]
    public void TheShippedMachineRunsTenJarsInSeriesAtMythBustersVoltage()
    {
        var def = MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "baghdad-battery.machine")));
        var runtime = new MachineRuntime(def, Materials);
        Assert.Equal(["one-jar", "ten-jars", "drops"], runtime.GalvanicJars.Keys);
        var ten = runtime.GalvanicJars["ten-jars"];
        Assert.Equal(4.33, ten.Voltage, precision: 9);                    // 10 × 0.433 V
        Assert.Equal(649.5, runtime.FieldGetters["ten-jars.power"](), precision: 6);   // µW: 4.33 V × 0.15 mA
        runtime.SetField("one-jar", "on", 0);
        for (int i = 0; i < 100; i++) runtime.Step(1);
        Assert.Equal(0, runtime.GalvanicJars["one-jar"].Delivered);
        Assert.Equal(4.33 * 0.15e-3 * 100, ten.Delivered, precision: 9);  // J over 100 s
    }

    [Fact]
    public void ALampLitByTheJarsGlowsByTheFourthRootOfTheirPower()
    {
        // #176: T = 1773.15 K (P / 649.5 µW)^¼: one jar 75 µW is 760.5 °C, ten jars 1,500 °C, a spent jar cold
        var one = new GalvanicJar("one", 1, 0.5, 0.15, 4.5e-5);
        var ten = new GalvanicJar("ten", 10, 0.433, 0.15, 4.5e-5);
        Assert.Equal(760.5, one.FilamentCelsius, precision: 1);
        Assert.Equal(1500.0, ten.FilamentCelsius, precision: 6);
        var drops = new GalvanicJar("drops", 1, 0.5, 0.15, 1e-7);
        Assert.Equal(760.5, drops.FilamentCelsius, precision: 1);
        for (int i = 0; i < 900; i++) drops.Step(60);     // past its 53,558 s
        Assert.True(drops.Spent);
        Assert.Equal(20, drops.FilamentCelsius);
        Assert.Equal(20, new GalvanicJar("off", 1, 0.5, 0.15, 4.5e-5) { On = false }.FilamentCelsius);
    }

    [Fact]
    public void TheEditorPlacesAJarAndExportsIt()
    {
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        s.Execute("(galvanic-jar jar #:at (0 0 0) #:cells 3 #:material clay)");
        Assert.Equal(3, s.Document.Parts["jar"].Number("cells"));
        Assert.StartsWith("ok:", s.Execute("(check)"));
        string rkt = RktExporter.Write(s.Document.ToMachineDef());
        Assert.Contains("(galvanic-jar jar #:at (0 0 0) #:cells 3 #:volts 0.5 #:milliamps 0.15", rkt);
        Assert.Contains("#:on #t", rkt);
        Assert.Contains("galvanic-jar", PartTemplates.PrimitiveKinds);
    }
}
