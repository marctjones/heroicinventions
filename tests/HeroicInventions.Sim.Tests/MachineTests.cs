using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

public class SExprReaderTests
{
    [Fact]
    public void ReadsTheAtomsRacketWrites()
    {
        var forms = SExprReader.ReadAll("""
            ; a comment
            (part ball rotor (at 0.0 -1.5 2e-07) "Hero \"of\" Alexandria" #t #f [x])
            """);
        var list = Assert.IsType<SList>(Assert.Single(forms));
        Assert.Equal("part", list.Head);
        var at = list.Field("at")!;
        Assert.Equal([0.0, -1.5, 2e-07], at.Items.Skip(1).Cast<SNumber>().Select(n => n.Value));
        Assert.Equal("Hero \"of\" Alexandria", Assert.IsType<SString>(list.Items[4]).Value);
        Assert.Equal(new SBool(true), list.Items[5]);
        Assert.Equal(new SBool(false), list.Items[6]);
    }

    [Fact]
    public void ReportsTheLineOfAnUnclosedList()
    {
        var e = Assert.Throws<FormatException>(() => SExprReader.ReadAll("(machine a\n  (part b"));
        Assert.Contains("line 2", e.Message);
    }

    [Fact]
    public void RejectsBarQuotedSymbols() =>
        Assert.Throws<FormatException>(() => SExprReader.ReadAll("(machine |odd name|)"));
}

public class MachineFileTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static MachineDef Load(string name) =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", name + ".machine")));

    [Fact]
    public void AeolipileBlueprintKeepsItsRacketSourceLocations()
    {
        var def = Load("aeolipile");
        Assert.Equal("Hero of Alexandria, Pneumatica", def.Source);
        var ball = def.Part("ball")!;
        Assert.Equal("rotor", ball.Kind);
        Assert.Equal("racket/machines/aeolipile.rkt", ball.Location!.File);
        Assert.Equal(new PortRef("kettle", "steam"), Assert.Single(def.Connects).A);
    }

    [Fact]
    public void HeronsFountainBlueprintLiftsWaterAboveTheBasin()
    {
        var run = new MachineRuntime(Load("herons-fountain"), Materials);
        var nozzle = run.Pipes["nozzle"];
        double before = run.Fluids.TotalWater;
        double supplyStart = run.Tanks["supply"].WaterVolume;

        double maxJet = 0;
        for (int i = 0; i < 1200; i++)
        {
            run.Step(0.05);
            if (nozzle.Flow > 0) maxJet = Math.Max(maxJet, nozzle.JetHeight);
        }

        Assert.True(run.AirPockets[0].GaugePressure > 0, "air should be compressed");
        Assert.True(run.Tanks["supply"].WaterVolume < supplyStart, "supply vessel should empty through the nozzle");
        Assert.True(maxJet > 0, "jet should rise above the nozzle");
        Assert.Equal(before, run.Fluids.TotalWater, precision: 9);
    }

    [Fact]
    public void AeolipileBlueprintSpinsOnceTheWaterBoils()
    {
        var run = new MachineRuntime(Load("aeolipile"), Materials);
        for (int i = 0; i < 20_000; i++) run.Step(0.01); // 200 s

        Assert.True(run.Boilers["kettle"].Temperature >= 100);
        Assert.True(run.Rotors["ball"].Rpm > 100, $"expected a spinning rotor, got {run.Rotors["ball"].Rpm:F0} rpm");
    }

    [Fact]
    public void RotorInertiaComesFromItsMaterial()
    {
        // 6 cm bronze shell, 1 mm wall: m ≈ 0.40 kg, I = ⅔·m·r² ≈ 9.6e-4 kg·m².
        Assert.Equal(9.6e-4, MachineRuntime.ShellInertia(8800, 0.06, 0.001), precision: 5);
    }

    [Fact]
    public void BadReferencesPointBackToTheRacketLine()
    {
        const string text = """
            (machine broken
              (part vat tank (material bronze) (at 0.0 0.0 0.0) (props (area 0.1) (height 0.2) (water 0.0)) (ports (drain water 0.0)) (srcloc "racket/machines/broken.rkt" 3 2))
              (pipe p (from vat drain) (to nowhere inlet) (conductance 0.0001) (jet #f) (srcloc "racket/machines/broken.rkt" 4 2)))
            """;
        var e = Assert.Throws<MachineFormatException>(() => new MachineRuntime(MachineDef.Parse(text), Materials));
        Assert.Equal(new SourceLocation("racket/machines/broken.rkt", 4, 2), e.Location);
        Assert.Contains("nowhere is not a tank", e.Message);
    }

    [Fact]
    public void MaterialsJsonIsGeneratedFromTheRacketTable() =>
        Assert.Equal("Wrought iron", Materials["iron"].Name);
}
