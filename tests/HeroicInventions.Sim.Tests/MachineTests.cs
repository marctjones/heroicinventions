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
    public void WorldFilesPlaceMachinesByLabelAndRefuseHeadings()
    {
        var world = WorldDef.Parse("(world bench (place a pendulum-demo (at 0 0 0)) (place b pendulum-demo (at 2 0 0.5)))");
        Assert.Equal("bench", world.Name);
        Assert.Equal(["a", "b"], world.Placements.Select(p => p.Label));
        Assert.Equal(new Vec3(2, 0, 0.5), world.Placements[1].At);
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse("(world w (place a x (at 0 0 0)) (place a y (at 1 0 0)))"));
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse("(world w (place a x (at 0 0 0) (heading 90)))"));
    }

    /// <summary>A gallery of every shipped machine lays their footprints out without overlaps.</summary>
    [Fact]
    public void GalleryOfEveryMachineDoesNotOverlap()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "machines");
        var machines = Directory.GetFiles(dir, "*.machine").Select(f => MachineDef.Parse(File.ReadAllText(f))).ToList();
        var gallery = WorldDef.Gallery(machines);
        Assert.Equal(machines.Count, gallery.Placements.Count);
        var boxes = gallery.Placements.Select(p =>
        {
            var (min, max) = WorldDef.Extent(machines.First(m => m.Name == p.Machine));
            return (MinX: min.X + p.At.X, MaxX: max.X + p.At.X, MinZ: min.Z + p.At.Z, MaxZ: max.Z + p.At.Z, p.Label);
        }).ToList();
        foreach (var a in boxes)
            foreach (var b in boxes.Where(b => string.CompareOrdinal(b.Label, a.Label) > 0))
                Assert.False(a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinZ < b.MaxZ && b.MinZ < a.MaxZ, $"{a.Label} overlaps {b.Label}");
    }

    /// <summary>
    /// A machine moved somewhere else in a shared world (issue #74) behaves
    /// exactly as it did where it was written: every traced number matches
    /// the untranslated run, step for step. Covers channels with bends
    /// (the mill race), sealed air and siphons (temple doors), mirrors aimed
    /// at a pot (the solar steam wheel), leaks, and a hearth under a boiler.
    /// </summary>
    [Theory]
    [InlineData("water-mill-race")]
    [InlineData("heron-temple-doors")]
    [InlineData("solar-steam-wheel")]
    [InlineData("tank-leaks")]
    [InlineData("branca-steam-wheel")]
    [InlineData("sluice-demo")]
    public void TranslatedMachineBehavesIdentically(string name)
    {
        var def = Load(name);
        var moved = def.Translated(new Vec3(37, 2, -11));
        Assert.All(moved.Parts.Zip(def.Parts), p => Assert.Equal(p.Second.At.X + 37, p.First.At.X, 9));
        var here = new MachineRuntime(def, Materials);
        var there = new MachineRuntime(moved, Materials);
        for (int i = 0; i < 3000; i++) { here.Step(0.02); there.Step(0.02); }   // a minute
        foreach (var (field, get) in here.FieldGetters)
            Assert.True(Math.Abs(get() - there.FieldGetters[field]()) <= 1e-9 * Math.Max(1, Math.Abs(get())),
                        $"{name} {field}: {get()} here, {there.FieldGetters[field]()} moved");
    }

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

    // HeronsFountainBlueprintLiftsWaterAboveTheBasin and
    // AeolipileBlueprintSpinsOnceTheWaterBoils used to live here. They
    // asserted simulated *behaviour* (jet height, rpm), not this file's
    // format/reference concerns, so they moved to
    // racket/heroic/tests/machine-behavior-test.rkt, run through the
    // headless HeroicInventions.SimHost instead of MachineRuntime
    // directly — see docs/design.html §III "Machines as tests".

    [Fact]
    public void ConservesWaterAcrossASimulatedRun()
    {
        var run = new MachineRuntime(Load("herons-fountain"), Materials);
        double before = run.Fluids.TotalWater;
        for (int i = 0; i < 1200; i++) run.Step(0.05);
        Assert.Equal(before, run.Fluids.TotalWater, precision: 9);
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
