using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// MachineWriter's round trip: every .machine file the game ships, parsed,
/// written back out by the editor's serializer (not Racket's emit.rkt), and
/// reparsed, must describe the same machine. Records here hold
/// IReadOnlyList/IReadOnlyDictionary fields, so record equality is reference
/// equality on those and useless for this; instead each test compares the
/// fields that matter, and a canonical round trip is checked by idempotence
/// of the printed text (Write(Parse(text)) reparsed and rewritten prints
/// identically) rather than by string-diffing against the Racket original,
/// whose formatting this writer doesn't try to match byte for byte.
/// </summary>
public class MachineWriterTests
{
    public static IEnumerable<object[]> AllMachineFiles() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "machines"), "*.machine").Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(AllMachineFiles))]
    public void RoundTripsEveryShippedMachine(string path)
    {
        var original = MachineDef.Parse(File.ReadAllText(path));
        var savedOnce = MachineWriter.Write(original);
        var reparsedOnce = MachineDef.Parse(savedOnce);
        var savedTwice = MachineWriter.Write(reparsedOnce);
        var reparsedTwice = MachineDef.Parse(savedTwice);

        // Idempotent: writing what we just reparsed produces byte-identical
        // text, so nothing was lost or reordered on the way through.
        Assert.Equal(savedOnce, savedTwice);

        AssertEquivalent(original, reparsedOnce);
    }

    private static void AssertEquivalent(MachineDef a, MachineDef b)
    {
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.Source, b.Source);
        Assert.Equal(a.Parts.Select(PartKey), b.Parts.Select(PartKey));
        Assert.Equal(a.Pipes.Select(PipeKey), b.Pipes.Select(PipeKey));
        Assert.Equal(a.Connects.Select(c => (c.A, c.B)), b.Connects.Select(c => (c.A, c.B)));
        Assert.Equal(a.SealedAir.Select(s => (string.Join(",", s.Tanks), s.TubeVolume)),
                     b.SealedAir.Select(s => (string.Join(",", s.Tanks), s.TubeVolume)));
        Assert.Equal(a.Ropes.Count, b.Ropes.Count);
        Assert.Equal(a.Lifts.Count, b.Lifts.Count);
        Assert.Equal(a.Sources.Count, b.Sources.Count);
        Assert.Equal(a.Channels.Count, b.Channels.Count);
        Assert.Equal(a.Cylinders.Count, b.Cylinders.Count);
        Assert.Equal(a.Meshes.Count, b.Meshes.Count);
        Assert.Equal(a.Arbors.Count, b.Arbors.Count);
    }

    private static object PartKey(PartSpec p) =>
        (p.Id, p.Kind, p.Material, p.At,
         Props: string.Join(",", p.Props.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={Print(kv.Value)}")),
         Ports: string.Join(",", p.Ports.Select(pt => $"{pt.Name}/{pt.Kind}/{pt.Height}")));

    private static object PipeKey(PipeSpec p) => (p.Id, p.From, p.To, p.Conductance, p.Jet);

    private static string Print(SExpr e) => SExprWriter.Print(e);

    [Fact]
    public void RefusesToWriteANonFiniteNumber()
    {
        var m = new MachineDef
        {
            Name = "bad",
            Parts = [new PartSpec("t", "tank", "bronze", new Vec3(0, 0, 0),
                new Dictionary<string, SExpr> { ["area"] = new SNumber(double.NaN) }, [], null)],
            Pipes = [], Connects = [], SealedAir = [],
        };
        Assert.Throws<ArgumentException>(() => MachineWriter.Write(m));
    }
}

/// <summary>PortRules mirrors machine.rkt's check-machine! (L546-648): one accept and one reject per rule it enforces.</summary>
public class PortRulesTests
{
    private static PartSpec Tank(string id) => new(id, "tank", "bronze", new Vec3(0, 0, 0), new Dictionary<string, SExpr>(),
        [new PortSpec("inlet", "water", 0), new PortSpec("outlet", "water", 0)], null);
    private static PartSpec Boiler(string id) => new(id, "boiler", "bronze", new Vec3(0, 0, 0), new Dictionary<string, SExpr>(),
        [new PortSpec("steam", "steam", 0.3)], null);
    private static PartSpec Rotor(string id) => new(id, "rotor", "bronze", new Vec3(0, 0, 0), new Dictionary<string, SExpr>(),
        [new PortSpec("steam-in", "steam", 0)], null);
    private static PartSpec Wheel(string id) => new(id, "wheel", "bronze", new Vec3(0, 0, 0), new Dictionary<string, SExpr>(), [], null);
    private static PartSpec Screw(string id) => new(id, "screw", "bronze", new Vec3(0, 0, 0), new Dictionary<string, SExpr>(), [], null);

    [Fact]
    public void TwoWaterPortsMakeAPipe()
    {
        var (a, b) = (Tank("a"), Tank("b"));
        Assert.Equal(LinkKind.Pipe, PortRules.Compatibility(a, a.Ports[1], b, b.Ports[0]));
    }

    [Fact]
    public void ABoilerAndARotorMakeASteamConnect()
    {
        var (boiler, rotor) = (Boiler("kettle"), Rotor("wheel"));
        Assert.Equal(LinkKind.SteamConnect, PortRules.Compatibility(boiler, boiler.Ports[0], rotor, rotor.Ports[0]));
    }

    [Fact]
    public void AWaterPortWillNotJoinASteamPort()
    {
        var (tank, boiler) = (Tank("t"), Boiler("k"));
        Assert.Equal(LinkKind.None, PortRules.Compatibility(tank, tank.Ports[0], boiler, boiler.Ports[0]));
    }

    [Fact]
    public void APartCannotLinkToItself()
    {
        var t = Tank("t");
        Assert.Equal(LinkKind.None, PortRules.Compatibility(t, t.Ports[0], t, t.Ports[1]));
    }

    [Fact]
    public void ChannelsOnlyRunBetweenTanks()
    {
        Assert.True(PortRules.CanChannel(Tank("a"), Tank("b")));
        Assert.False(PortRules.CanChannel(Tank("a"), Boiler("b")));
    }

    [Fact]
    public void LiftsRunByAScrewWheelOrPistonBetweenTanks()
    {
        Assert.True(PortRules.CanLift(Screw("s"), Tank("a"), Tank("b")));
        Assert.False(PortRules.CanLift(Boiler("k"), Tank("a"), Tank("b")));
        Assert.False(PortRules.CanLift(Screw("s"), Tank("a"), Boiler("k")));
    }

    [Fact]
    public void OnlyWheelsMeshOrShareAnArbor()
    {
        Assert.True(PortRules.BothWheels(Wheel("a"), Wheel("b")));
        Assert.False(PortRules.BothWheels(Wheel("a"), Screw("b")));
    }
}

/// <summary>EditorDocument: placement, snapping and the load/edit/save/run round trip.</summary>
public class EditorDocumentTests
{
    [Fact]
    public void PlacingTwoTanksAndSnappingAPipeConnectsThem()
    {
        var doc = EditorDocument.New("bench");
        doc.AddPart(PartTemplates.Create("tank", "high", new Vec3(0, 1.0, 0), "bronze"));
        doc.AddPart(PartTemplates.Create("tank", "low", new Vec3(1, 0.0, 0), "bronze"));

        // "high"'s outlet (world (0, 1.0, 0)) is nowhere near "low"'s inlet
        // (world (1, 0.0, 0)) yet — too far apart in any of x/y/z to snap.
        Assert.Null(doc.NearestCompatiblePort("high", "outlet", radius: 0.05));

        // Drag "low" directly under "high" — within radius in all three axes.
        doc.Move("low", new Vec3(0.02, 1.0, 0));
        var hit = doc.NearestCompatiblePort("high", "outlet", radius: 0.05);
        Assert.NotNull(hit);
        Assert.Equal("low", hit!.Value.Part.Id);
        Assert.Equal(LinkKind.Pipe, hit.Value.Kind);

        doc.Connect(new PortHandle("high", "outlet"), new PortHandle("low", "inlet"));
        var pipe = Assert.Single(doc.Pipes.Values);
        Assert.Equal(new PortRef("high", "outlet"), pipe.From);
        Assert.Equal(new PortRef("low", "inlet"), pipe.To);
    }

    [Fact]
    public void RemovingAPartDropsItsPipesToo()
    {
        var doc = EditorDocument.New("bench");
        doc.AddPart(PartTemplates.Create("tank", "a", new Vec3(0, 0, 0), "bronze"));
        doc.AddPart(PartTemplates.Create("tank", "b", new Vec3(1, 0, 0), "bronze"));
        doc.Connect(new PortHandle("a", "outlet"), new PortHandle("b", "inlet"));
        Assert.Single(doc.Pipes);

        doc.RemovePart("b");
        Assert.Empty(doc.Pipes);
        Assert.False(doc.Parts.ContainsKey("b"));
    }

    [Fact]
    public void IncompatiblePortsRefuseToConnect()
    {
        var doc = EditorDocument.New("bench");
        doc.AddPart(PartTemplates.Create("tank", "t", new Vec3(0, 0, 0), "bronze"));
        doc.AddPart(PartTemplates.Create("boiler", "k", new Vec3(1, 0, 0), "bronze"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            doc.Connect(new PortHandle("t", "outlet"), new PortHandle("k", "steam")));
        Assert.Contains("water", ex.Message);
    }

    /// <summary>
    /// machine.rkt's check-machine! tracks steam supply per *boiler*, not per
    /// rotor (L562-572): a boiler already wired to one rotor by `connect`
    /// can't also be wired to a second — "one rotor per boiler for now".
    /// </summary>
    [Fact]
    public void ABoilerCannotFeedASecondRotor()
    {
        var doc = EditorDocument.New("bench");
        doc.AddPart(PartTemplates.Create("boiler", "k", new Vec3(0, 0, 0), "bronze"));
        doc.AddPart(new PartSpec("r1", "rotor", "bronze", new Vec3(1, 0, 0), new Dictionary<string, SExpr>(),
            [new PortSpec("steam-in", "steam", 0)], null));
        doc.AddPart(new PartSpec("r2", "rotor", "bronze", new Vec3(2, 0, 0), new Dictionary<string, SExpr>(),
            [new PortSpec("steam-in", "steam", 0)], null));
        doc.Connect(new PortHandle("k", "steam"), new PortHandle("r1", "steam-in"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            doc.Connect(new PortHandle("k", "steam"), new PortHandle("r2", "steam-in")));
        Assert.Contains("already feeds", ex.Message);
    }

    [Fact]
    public void LoadingAShippedMachineAndSavingItRightBackRoundTrips()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "machines", "herons-fountain.machine");
        var original = MachineDef.Parse(File.ReadAllText(path));
        var doc = EditorDocument.Load(original);
        var reparsed = MachineDef.Parse(MachineWriter.Write(doc.ToMachineDef()));
        Assert.Equal(original.Name, reparsed.Name);
        Assert.Equal(original.Parts.Count, reparsed.Parts.Count);
        Assert.Equal(original.Pipes.Count, reparsed.Pipes.Count);
        Assert.Equal(original.SealedAir.Count, reparsed.SealedAir.Count);
    }

    /// <summary>
    /// The verification the owner asked for: build a tank + pipe + tank by
    /// calling exactly the editor's API (no shortcuts through MachineDef's
    /// constructor), write it, reparse it, and run it through the same
    /// FluidNetwork every other machine uses. Water actually has to move,
    /// by the amount the flow law (FluidNetwork.Substep: q = conductance ×
    /// head difference) predicts for these numbers — not just "some" water.
    /// </summary>
    [Fact]
    public void ABuiltTankPipeTankMachineMovesWaterByThePredictedAmount()
    {
        var doc = EditorDocument.New("bench");
        // "high" starts full (0.3 m³ in a 1 m² tank = 0.3 m deep) and sits a
        // metre above "low", which starts empty — a substantial head to drive
        // a clearly measurable flow over a couple of seconds.
        var high = PartTemplates.Create("tank", "high", new Vec3(0, 1.0, 0), "bronze");
        high = high with { Props = new Dictionary<string, SExpr> { ["area"] = new SNumber(1.0), ["height"] = new SNumber(1.0), ["water"] = new SNumber(0.3) } };
        doc.AddPart(high);
        var low = PartTemplates.Create("tank", "low", new Vec3(3, 0.0, 0), "bronze");
        low = low with { Props = new Dictionary<string, SExpr> { ["area"] = new SNumber(1.0), ["height"] = new SNumber(1.0), ["water"] = new SNumber(0.0) } };
        doc.AddPart(low);
        doc.Connect(new PortHandle("high", "outlet"), new PortHandle("low", "inlet"), pipeConductance: 0.02);

        string text = MachineWriter.Write(doc.ToMachineDef());
        var reloaded = MachineDef.Parse(text);

        // The prediction: an independent FluidNetwork built from the same
        // numbers this machine describes (tank areas/heights/water, pipe
        // conductance, port elevations) — the physics MachineRuntime also
        // uses, exercised directly instead of through the .machine parser,
        // so this is a genuine prediction of the file's behaviour, not a
        // tautology against the same code path being tested.
        var predictedNet = new FluidNetwork();
        var predictedHigh = predictedNet.AddTank(new Tank("high", 1.0, 1.0, 1.0, 0.3));
        var predictedLow = predictedNet.AddTank(new Tank("low", 0.0, 1.0, 1.0, 0.0));
        predictedNet.AddPipe(new Pipe("pipe-1", predictedHigh, 1.0, predictedLow, 0.0, 0.02));
        for (int i = 0; i < 200; i++) predictedNet.Step(0.01); // 2 s simulated
        double predictedMoved = 0.3 - predictedHigh.WaterVolume;
        Assert.True(predictedMoved > 0.01, "the prediction itself should show a clear flow, or this test proves nothing");

        var runtime = new MachineRuntime(reloaded, MaterialLibrary.LoadDefault());
        for (int i = 0; i < 200; i++) runtime.Step(0.01);
        double actualMoved = 0.3 - runtime.Tanks["high"].WaterVolume;

        Assert.Equal(predictedMoved, actualMoved, precision: 9);
        Assert.True(runtime.Tanks["low"].WaterVolume > 0.01, "water should have arrived in the low tank");
    }
}

/// <summary>CatalogueReader parses catalogue.rktd (racket/build.rkt's output — game/meshes/catalogue/ is gitignored, rebuilt from Racket).</summary>
public class CatalogueReaderTests
{
    private const string Sample = """
        ;; Generated by racket/build.rkt from racket/heroic/geometry/catalogue.rkt. Do not edit.
        ;; (entry id description kind mesh-stem volume (inertia-x inertia-y inertia-z) ((prop value) ...))
        (
         (entry pulley-5cm "Pulley sheave, 5 cm radius" pulley "pulley-4482b6c318" 0.0001 (1e-8 1e-8 2e-8) ((radius 0.05) (width 0.025)))
         (entry vitruvian-screw-2m "Archimedes' screw by Vitruvius's rules, 2 m long" screw "screw-45c41012f0" 0.02 (0.0002 0.0002 0.0001) ((radius 0.125) (pitch 0.3) (starts 8))))
        """;

    [Fact]
    public void ParsesEveryEntryWithItsShapeAndInertia()
    {
        var entries = CatalogueReader.Parse(Sample);
        Assert.Equal(2, entries.Count);
        var pulley = entries[0];
        Assert.Equal("pulley-5cm", pulley.Id);
        Assert.Equal("pulley", pulley.ShapeKind);
        Assert.Equal("wheel", pulley.PartKind);
        Assert.Equal("pulley-4482b6c318", pulley.MeshStem);
        Assert.Equal(0.0001, pulley.Volume);
        Assert.Equal(new Vec3(1e-8, 1e-8, 2e-8), pulley.Inertia);
        Assert.Equal(0.05, Assert.IsType<SNumber>(pulley.ShapeProps["radius"]).Value);

        var screw = entries[1];
        Assert.Equal("screw", screw.PartKind);
        Assert.Equal(8.0, Assert.IsType<SNumber>(screw.ShapeProps["starts"]).Value);
    }

    [Fact]
    public void AnAbsentCatalogueFileIsAnEmptyPaletteNotAnError() =>
        Assert.Empty(CatalogueReader.ReadFile("/no/such/catalogue.rktd"));

    [Fact]
    public void APlacedCatalogueWheelCarriesTheInertiaMachineViewNeeds()
    {
        var entry = CatalogueReader.Parse(Sample)[0]; // pulley
        var part = PartTemplates.Create(entry, "p1", new Vec3(0, 1, 0), "bronze");
        Assert.Equal("wheel", part.Kind);
        Assert.Equal(1e-8, part.Number("inertia-x"));
        Assert.Equal(2e-8, part.Number("inertia-z"));
        Assert.Equal(0.0001, part.Number("volume"));
        Assert.Equal("pulley-4482b6c318", part.Text("mesh"));
    }

    /// <summary>
    /// If racket/build.rkt has been run in this checkout (it gitignores its
    /// own output, so this is best-effort), the real catalogue parses too —
    /// catching a drift between this reader and build.rkt's actual format
    /// that a hand-written fixture alone wouldn't.
    /// </summary>
    [Fact]
    public void TheRealBuiltCatalogueParsesIfPresent()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "meshes", "catalogue", "catalogue.rktd");
        if (!File.Exists(path)) return; // not built in this environment — see racket/build.rkt
        var entries = CatalogueReader.ReadFile(path);
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.True(e.Inertia.X > 0 && e.Inertia.Y > 0 && e.Inertia.Z > 0));
    }
}
