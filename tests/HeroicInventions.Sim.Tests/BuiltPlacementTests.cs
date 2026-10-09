using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #204: a machine the player builds in a world is a placement with its design on it (<see cref="Placement.Built"/>, world
/// coordinates), written whole into the world file's <c>(build …)</c> form and into a world save, and built as far as it can be
/// (<see cref="BuildSession.Buildable"/>): a generator with no bank to charge is set aside and the rest runs.
/// </summary>
public class BuiltPlacementTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    // the two gears of the windmill's train, as build.rkt writes them into the catalogue (module 5 mm, 72 and 18 teeth: 4:1 a stage)
    private const string Catalogue = """
        (catalogue
         (entry involute-gear-m5-18 "Involute spur gear, 18 teeth, module 5 mm" gear "gear-3310e2068b" 0.00024138769777083563 (1.5781044876578355e-7 1.5781044876578345e-7 2.512508447926773e-7) ((teeth 18.0) (module 0.005) (profile involute) (width 0.05) (pitch-radius 0.045)))
         (entry involute-gear-m5-72 "Involute spur gear, 72 teeth, module 5 mm" gear "gear-ff4eb30017" 0.003962724349873982 (3.319971575925939e-5 3.319971575925936e-5 6.5342705025219e-5) ((teeth 72.0) (module 0.005) (profile involute) (width 0.05) (pitch-radius 0.18))))
        """;

    private const string MarsScene = """
        (machine scene (ambient -63.0)
          (planet mars (name "Mars") (gravity 3.71) (pressure 610.0) (temperature -63.0) (air (o2 0.0017) (n2 0.0259) (co2 0.9527) (h2o 0.0003) (ar 0.0194)) (molar-mass #f) (solar-constant 586.2) (sky-transmittance 0.741) (air-mass-exponent 1.0) (sol 88775.0) (year 669.0) (obliquity 25.19) (daily-temperature -80.0 -20.0 15.0) (sky-color 0.78 0.6 0.45) (ground-color 0.6 0.36 0.22)))
        """;

    /// <summary>The windmill, a three-stage 64:1 train and a generator, built where they stand at (200, h, 130) as the game's scripted build does.</summary>
    private static MachineDef BuiltWindmill(double groundY = -59.22)
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-built-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var session = new BuildSession(Materials, CatalogueReader.Parse(Catalogue), dir, "built-1");
        var scene = MachineDef.Parse(MarsScene);
        session.Open(new MachineDef { Name = "built-1", Ambient = scene.Ambient, Planet = scene.Planet, Parts = [], Pipes = [], Connects = [], SealedAir = [] });
        string At(double x, double y, double z) => $"#:at ({200 + x} {groundY + y} {130 + z})";
        foreach (string c in new[]
        {
            $"(post tower {At(0.45, 0, -1.45)} #:size-y 10.6)",
            $"(windmill sails {At(0, 11, 0)})",
            $"(wheel wheel-a #:catalogue involute-gear-m5-72 {At(0, 11, -1.3)})", "(arbor sails wheel-a)",
            $"(wheel pinion-b #:catalogue involute-gear-m5-18 {At(0.225, 11, -1.3)})", "(mesh wheel-a pinion-b)",
            $"(wheel wheel-b #:catalogue involute-gear-m5-72 {At(0.225, 11, -1.4)})", "(arbor pinion-b wheel-b)",
            $"(wheel pinion-c #:catalogue involute-gear-m5-18 {At(0.45, 11, -1.4)})", "(mesh wheel-b pinion-c)",
            $"(wheel wheel-c #:catalogue involute-gear-m5-72 {At(0.45, 11, -1.5)})", "(arbor pinion-c wheel-c)",
            $"(wheel pinion-d #:catalogue involute-gear-m5-18 {At(0.675, 11, -1.5)})", "(mesh wheel-c pinion-d)",
            $"(generator motor {At(0.675, 11, -1.75)})", "(set motor #:on pinion-d)",
        })
            session.Execute(c);
        return session.Document.ToMachineDef();
    }

    private static WorldDef Opening() => WorldDef.Parse("""
        (world lonely-rover-opening (map victoria) (rover (at 190.0 140.0) (heading 270))
          (place battery-bank cargo-crate (at 264.0 0 143.0)))
        """);

    [Fact]
    public void AGeneratorWithNoBankIsSetAsideAndTheRestRuns()
    {
        var design = BuiltWindmill();
        Assert.Equal(9, design.Parts.Count);
        var unfinished = new Dictionary<string, string>();
        MachineRuntime? runtime = null;
        var built = BuildSession.Buildable(design, d => runtime = new MachineRuntime(d, Materials), unfinished);
        Assert.NotNull(built);
        Assert.Equal(["motor"], unfinished.Keys);
        Assert.Contains("charges ?, which is not a battery-bank", unfinished["motor"]);
        Assert.Equal(8, built!.Parts.Count);
        // the sails on Mars's air: tau(omega) = 1/2 rho A v^2 R (Cp/lambda*) (2 - omega/omega*), omega* = lambda* v / R = 1.5 rad/s,
        // I = m R^2 / 3 = 50,000 kg m2, so omega(t) = 3 (1 - e^(-t/T)), T = 1.5 I / (1/2 rho A v^2 R Cp/lambda*)
        double rho = runtime!.FieldGetters["scene.air-density"]();
        double half = 0.5 * rho * Math.PI * 100 * 36 * 10 * 0.3 / 2.5;
        Assert.Equal(102.9, half, 0);   // N m: the worked number (0.01518 kg/m3 at 610 Pa and -63 C, CO2)
        double T = 1.5 * 50_000 / half;
        for (int i = 0; i < 120 * 60; i++) runtime.Step(1.0 / 120);
        double omega = runtime.Windmills["sails"].AngularVelocity;
        Assert.Equal(3 * (1 - Math.Exp(-60 / T)), omega, 3);   // 0.237 rad/s at 60 s (the sim's sails alone; the train is Jolt's, 0.3% more inertia)
    }

    [Fact]
    public void ABuiltPlacementIsWrittenWholeIntoTheWorldAndReadBack()
    {
        var design = BuiltWindmill();
        var world = Opening();
        string label = world.NextPlacementLabel("built");
        Assert.Equal("built-1", label);
        world = world.WithPlacement(new Placement(label, label, new Vec3(200, 0, 130), null) { Built = design });
        Assert.Equal("built-2", world.NextPlacementLabel("built"));
        Assert.Throws<MachineFormatException>(() => world.WithPlacement(new Placement(label, label, new Vec3(0, 0, 0), null)));
        string text = world.Write();
        Assert.Contains("(place battery-bank cargo-crate", text);
        Assert.Contains("(build built-1 (at 200.0 0.0 130.0) (machine built-1", text);
        var again = WorldDef.Parse(text);
        var b = Assert.Single(again.Placements, p => p.Built is not null);
        Assert.Equal(MachineWriter.Write(design), MachineWriter.Write(b.Built!));
        Assert.Equal(3.71, b.Built!.Planet.Gravity, 9);   // it stands on Mars, as the world's machines do
        Assert.Equal(text, again.Write());
        // a live edit of it replaces its design; links reach it as any placement; emptied, it goes, with its links
        var edited = again.WithBuilt("built-1", MachineDef.Parse(MachineWriter.Write(design).Replace("(radius 10.0)", "(radius 8.0)")));
        Assert.Equal(8, edited.Placements.Single(p => p.Label == "built-1").Built!.Part("sails")!.Number("radius"));
        Assert.Throws<MachineFormatException>(() => edited.WithBuilt("battery-bank", design));   // the found crate is not a build
        var linked = edited.WithLink(new LinkSpec("shaft-1", "shaft", new LinkEnd("built-1", "pinion-d"), new LinkEnd("battery-bank", "crate")));
        Assert.Empty(linked.WithoutPlacement("built-1").Links);
    }

    [Fact]
    public void AWorldSaveCarriesTheBuiltMachinesAndAnOlderSaveHasNone()
    {
        var design = BuiltWindmill();
        var placement = new Placement("built-1", "built-1", new Vec3(200, 0, 130), null) { Built = design };
        var runtime = new MachineRuntime(BuildSession.Buildable(design, d => _ = new MachineRuntime(d, Materials), new Dictionary<string, string>())!, Materials);
        var save = new WorldSave
        {
            Kind = "world", Name = "lonely-rover-opening", Built = [placement],
            Machines = [new SavedMachine("built-1", "built-1", 12.5, RuntimeState.Capture(runtime))],
            Rover = new SList([new SSymbol("rover"), new SNumber(1)]),
        };
        string text = save.ToText();
        var lines = text.Split('\n');
        // one line for the build, its machine whole on it; the save's own (machine …) line is the state; the rover stays last
        Assert.Single(lines, l => l.StartsWith("  (build built-1 (at 200.0 0.0 130.0) (machine built-1 "));
        Assert.Single(lines, l => l.StartsWith("  (machine "));
        Assert.StartsWith("  (rover ", lines.Last(l => l.Length > 0));
        var back = WorldSave.Parse(text);
        var b = Assert.Single(back.Built);
        Assert.Equal(MachineWriter.Write(design), MachineWriter.Write(b.Built!));
        Assert.Equal(new Vec3(200, 0, 130), b.At);
        Assert.Empty(WorldSave.Parse(text.Replace(lines.Single(l => l.StartsWith("  (build ")), "")).Built);
    }
}
