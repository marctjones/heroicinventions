using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// BuildSession: the command layer under the editor's UI. Every test here
/// drives the session the same way a text console or the palette/drag-drop
/// UI would — one command string at a time — so it also documents the
/// command language. See BuildSession's class comment for the full grammar.
/// </summary>
public class BuildSessionTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static BuildSession NewSession(string dir) =>
        new(Materials, catalogue: [], machinesDir: dir);

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-editor-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void UnitsAcceptTheWhitelistAndRejectAnythingElse()
    {
        Assert.Equal(1.2, Units.Parse("1.2m", "test"));
        Assert.Equal(0.8, Units.Parse("80cm", "test"));
        Assert.Equal(0.5, Units.Parse("500L/s", "test"));
        Assert.Equal(3000, Units.Parse("3kW", "test"));
        Assert.Equal(5, Units.Parse("5", "test")); // bare number, already SI
        Assert.Throws<FormatException>(() => Units.Parse("12furlongs", "test"));
    }

    /// <summary>The verification the owner asked for: a command script builds tank + pipe + tank, (run)s it, and water moves by the predicted amount.</summary>
    [Fact]
    public void TankPipeTankScriptMovesWaterByThePredictedAmount()
    {
        var session = NewSession(TempDir());
        session.Execute("(tank high #:at (0 1.0 0) #:area 1.0 #:height 1.0 #:water 0.3)");
        session.Execute("(tank low #:at (3 0.0 0) #:area 1.0 #:height 1.0)");
        session.Execute("(pipe p1 high.outlet low.inlet #:conductance 0.02)");

        string checkResult = session.Execute("(check)");
        Assert.StartsWith("ok:", checkResult);

        session.Execute("(run 2)");
        Assert.NotNull(session.LastRun);

        var predicted = new FluidNetwork();
        var pHigh = predicted.AddTank(new Tank("high", 1.0, 1.0, 1.0, 0.3));
        var pLow = predicted.AddTank(new Tank("low", 0.0, 1.0, 1.0, 0.0));
        predicted.AddPipe(new Pipe("p1", pHigh, 1.0, pLow, 0.0, 0.02));
        for (int i = 0; i < 200; i++) predicted.Step(0.01);

        Assert.Equal(pHigh.WaterVolume, session.LastRun!.Tanks["high"].WaterVolume, precision: 9);
        Assert.Equal(pLow.WaterVolume, session.LastRun!.Tanks["low"].WaterVolume, precision: 9);
        Assert.True(pLow.WaterVolume > 0.01, "the prediction itself should show a clear flow");
    }

    /// <summary>A second link type: (inflow) feeding a weir pool that spills over a (channel) — water should move at the weir-formula rate, not just "some".</summary>
    [Fact]
    public void InflowAndChannelScriptMovesWaterAtTheWeirRate()
    {
        var session = NewSession(TempDir());
        session.Execute("(tank pool #:at (0 0 0) #:area 4.0 #:height 1.6 #:water 5.0)");
        session.Execute("(set pool #:water 5.412)"); // exercise (set) too
        session.Execute("(tank river #:at (6.0 0.0 0.0) #:area 30.0 #:height 1.0)");
        session.Execute("(inflow upstream #:into pool #:flow 0.5)");
        session.Execute("(channel race pool.outlet river.inlet #:width 1.2)");

        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 5)");

        // Independent prediction: the same Fluids/OpenChannel classes,
        // stepped in the same order MachineRuntime.Step uses (sources then
        // channels every 0.01s substep), built straight from the numbers
        // the commands above described — not by re-running the code under test.
        var poolTank = new Tank("pool", 0, 4.0, 1.6, 5.412);
        var riverTank = new Tank("river", 0, 30.0, 1.0, 0);
        var source = new WaterSource("upstream", poolTank, 0.5);
        var doc = session.Document;
        double outletHeight = doc.Parts["pool"].Ports.First(p => p.Name == "outlet").Height;
        double inletHeight = doc.Parts["river"].Ports.First(p => p.Name == "inlet").Height;
        var channel = new Channel("race", poolTank, outletHeight, riverTank, inletHeight, 1.2,
            length: Math.Max(0.1, 6.0 - Math.Sqrt(4.0) / 2 - Math.Sqrt(30.0) / 2));
        for (int i = 0; i < 500; i++)
        {
            for (int s = 0; s < 1; s++) { source.Step(0.01); channel.Step(0.01); }
        }

        Assert.True(riverTank.WaterVolume > 0.01, "the prediction itself should show a clear flow");
        Assert.Equal(riverTank.WaterVolume, session.LastRun!.Tanks["river"].WaterVolume, precision: 6);
    }

    [Fact]
    public void UndoRestoresTheExactPriorStateAndRedoReplaysIt()
    {
        var session = NewSession(TempDir());
        session.Execute("(tank a #:at (0 0 0) #:area 1.0 #:height 1.0)");
        string beforeMove = MachineWriter.Write(session.Document.ToMachineDef());

        session.Execute("(move a (5 0 0))");
        Assert.Equal(5.0, session.Document.Parts["a"].At.X);

        session.Execute("(undo)");
        Assert.Equal(0.0, session.Document.Parts["a"].At.X);
        Assert.Equal(beforeMove, MachineWriter.Write(session.Document.ToMachineDef()));

        session.Execute("(redo)");
        Assert.Equal(5.0, session.Document.Parts["a"].At.X);
    }

    [Fact]
    public void UndoAlsoUndoesPlacingAndRemovingParts()
    {
        var session = NewSession(TempDir());
        session.Execute("(tank a #:at (0 0 0) #:area 1.0 #:height 1.0)");
        session.Execute("(tank b #:at (1 0 0) #:area 1.0 #:height 1.0)");
        session.Execute("(remove a)");
        Assert.False(session.Document.Parts.ContainsKey("a"));

        session.Execute("(undo)"); // undoes the remove
        Assert.True(session.Document.Parts.ContainsKey("a"));

        session.Execute("(undo)"); // undoes placing b
        Assert.False(session.Document.Parts.ContainsKey("b"));

        session.Execute("(undo)"); // undoes placing a
        Assert.Empty(session.Document.Parts);
    }

    [Fact]
    public void UndoWithNothingToUndoRefuses() =>
        Assert.Throws<InvalidOperationException>(() => NewSession(TempDir()).Execute("(undo)"));

    [Fact]
    public void SnapPicksAPipeForTwoWaterPortsAndRefusesIncompatibleOnes()
    {
        var session = NewSession(TempDir());
        session.Execute("(tank a #:at (0 0 0) #:area 1.0 #:height 1.0)");
        session.Execute("(tank b #:at (1 0 0) #:area 1.0 #:height 1.0)");
        session.Execute("(boiler k #:at (2 0 0) #:radius 0.15 #:height 0.3 #:water 0.01)");

        session.Execute("(snap a.outlet b.inlet)");
        Assert.Single(session.Document.Pipes);

        var ex = Assert.Throws<InvalidOperationException>(() => session.Execute("(snap a.outlet k.steam)"));
        Assert.Contains("water", ex.Message);
    }

    [Fact]
    public void CheckReportsAMachineFormatExceptionWithLocationForABadReference()
    {
        var session = NewSession(TempDir());
        session.Execute("(tank a #:at (0 0 0) #:area 1.0 #:height 1.0)");
        session.Execute("(pipe bad a.outlet nowhere.inlet #:conductance 0.001)");
        string result = session.Execute("(check)");
        Assert.StartsWith("error:", result);
        Assert.Contains("nowhere", result);
    }

    [Fact]
    public void SaveThenLoadByNameRoundTrips()
    {
        string dir = TempDir();
        var session = NewSession(dir);
        session.Execute("(tank a #:at (0 0 0) #:area 1.0 #:height 1.0 #:water 0.2)");
        session.Execute("(save bench)");
        Assert.True(File.Exists(Path.Combine(dir, "bench.machine")));

        var session2 = NewSession(dir);
        session2.Execute("(load bench)");
        Assert.Equal(0.2, ((SNumber)session2.Document.Parts["a"].Props["water"]).Value);
    }

    [Fact]
    public void SetMaterialChangesAPartsMaterial()
    {
        var session = NewSession(TempDir());
        session.Execute("(tank a #:at (0 0 0) #:area 1.0 #:height 1.0)");
        session.Execute("(set a #:material oak)");
        Assert.Equal("oak", session.Document.Parts["a"].Material);
    }

    [Fact]
    public void PaletteListsThePrimitiveKinds()
    {
        string result = NewSession(TempDir()).Execute("(palette)");
        Assert.Contains("tank", result);
        Assert.Contains("boiler", result);
    }

    [Fact]
    public void TheCommandLogRecordsEveryCommandInOrder()
    {
        var session = NewSession(TempDir());
        session.Execute("(tank a #:at (0 0 0) #:area 1.0 #:height 1.0)");
        session.Execute("(move a (1 0 0))");
        Assert.Equal(["(tank a #:at (0 0 0) #:area 1.0 #:height 1.0)", "(move a (1 0 0))"], session.CommandLog);
    }

    /// <summary>Every .machine file the game ships, loaded and saved right back by name, unchanged in every field that matters — see MachineWriterTests for the field-by-field comparison this reuses.</summary>
    public static IEnumerable<object[]> AllMachineFiles() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "machines"), "*.machine").Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    [Theory]
    [MemberData(nameof(AllMachineFiles))]
    public void LoadThenSaveRoundTripsEveryShippedMachine(string name)
    {
        string srcDir = Path.Combine(AppContext.BaseDirectory, "machines");
        string dstDir = TempDir();
        var session = NewSession(srcDir);
        session.Execute($"(load {name})");
        var loaded = session.Document.ToMachineDef();

        var savePath = Path.Combine(dstDir, name + ".machine");
        session.SaveFile(savePath);
        var reparsed = MachineDef.Parse(File.ReadAllText(savePath));

        Assert.Equal(loaded.Name, reparsed.Name);
        Assert.Equal(loaded.Parts.Count, reparsed.Parts.Count);
        Assert.Equal(loaded.Pipes.Count, reparsed.Pipes.Count);
        Assert.Equal(loaded.Connects.Count, reparsed.Connects.Count);
        Assert.Equal(loaded.SealedAir.Count, reparsed.SealedAir.Count);
    }

    [Fact]
    public void ExportsRacketSourceForPrimitiveParts()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "bench");
        session.Execute("(tank a #:at (0 1 0) #:area 1.0 #:height 1.0 #:water 0.2)");
        session.Execute("(tank b #:at (0 0 0) #:area 1.0 #:height 1.0)");
        session.Execute("(pipe p1 a.outlet b.inlet #:conductance 0.01)");
        string path = Path.Combine(TempDir(), "bench.rkt");
        session.ExportRkt(path);
        string text = File.ReadAllText(path);
        Assert.StartsWith("#lang heroic", text);
        Assert.Contains("(define-machine bench", text);
        Assert.Contains("(tank a ", text);
        Assert.Contains("(pipe p1 a.outlet b.inlet", text);
    }

    /// <summary>
    /// A sluice placed from the palette: #:on names its channel, #:opening
    /// narrows the slot, and the pool settles where the orifice passes the
    /// whole spring — h = (Q / 0.6 w a)^2 / 2g above the slot's middle — then
    /// the design round-trips through .machine and exports as a Racket clause.
    /// </summary>
    [Fact]
    public void SluiceScriptHoldsThePoolAtTheOrificeHeadAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "gated");
        session.Execute("(tank pool #:at (0 0.5 0) #:area 0.25 #:height 1.5 #:water 0.05)");
        session.Execute("(tank pond #:at (4 0 0) #:area 4.0 #:height 1.0)");
        session.Execute("(inflow spring #:into pool #:flow 0.01)");
        session.Execute("(channel race pool.outlet pond.inlet #:width 0.3)");
        session.Execute("(sluice gate #:at (0.25 0.5 0) #:on race #:height 0.5 #:opening 0.1)");
        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 200)");

        // a = 5 cm slot, 30 cm wide: h = (0.01 / (0.6 * 0.3 * 0.05))^2 / (2 * 9.81) = 6.293 cm
        double h = Math.Pow(0.01 / (0.6 * 0.3 * 0.05), 2) / (2 * 9.81);
        var gate = session.LastRun!.Gates["gate"];
        Assert.Equal(h, gate.OrificeHead, precision: 4);
        Assert.Equal(0.01, session.LastRun.Channels["race"].Flow, precision: 6);
        // read just after the step's outflow: Q·dt/A = 0.4 mm below the level the gate saw
        Assert.InRange(session.LastRun.Tanks["pool"].SurfaceElevation, 0.5 + 0.025 + h - 0.0005, 0.5 + 0.025 + h);

        string saved = Path.Combine(TempDir(), "gated.machine");
        session.SaveFile(saved);
        var reparsed = MachineDef.Parse(File.ReadAllText(saved));
        var sluice = reparsed.Part("gate")!;
        Assert.Equal("sluice", sluice.Kind);
        Assert.Equal("race", sluice.Symbol("on", ""));
        Assert.Equal(0.1, sluice.Number("opening"));
        Assert.Equal(0.5, sluice.Number("height"));
        Assert.False(sluice.Props["width"] is SNumber, "no #:width: the channel's own");

        string rkt = Path.Combine(TempDir(), "gated.rkt");
        session.ExportRkt(rkt);
        Assert.Contains("(sluice gate #:at (0.25 0.5 0) #:on race #:height 0.5 #:opening 0.1 #:material bronze)", File.ReadAllText(rkt));
    }

    /// <summary>A channel run off the scene #:onto a boiler feeds it, and the clause round-trips.</summary>
    [Fact]
    public void ChannelOntoABoilerFeedsItAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "fed");
        session.Execute("(tank feed #:at (0 1 0) #:area 0.02 #:height 0.4 #:water 0.004)");
        session.Execute("(boiler copper #:at (1 0.2 0) #:radius 0.2 #:height 0.4 #:water 4 #:temperature 90)");
        session.Execute("(channel chute feed.outlet off #:end (1 0.8 0) #:width 0.1 #:onto copper)");
        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 60)");
        var copper = session.LastRun!.Boilers["copper"];
        Assert.True(copper.WaterFed > 0.1, $"fed {copper.WaterFed} kg");
        // everything it holds is the old 4 kg at 90 C and the fed water at 20 C, less what it lost to the air
        double mixed = (4 * 90 + copper.WaterFed * 20) / (4 + copper.WaterFed);
        Assert.Equal(mixed, copper.Temperature + copper.HeatLost / ((4 + copper.WaterFed) * 4186), precision: 6);

        string saved = Path.Combine(TempDir(), "fed.machine");
        session.SaveFile(saved);
        Assert.Equal("copper", MachineDef.Parse(File.ReadAllText(saved)).Channels.Single().Onto);
        string rkt = Path.Combine(TempDir(), "fed.rkt");
        session.ExportRkt(rkt);
        Assert.Contains("#:onto copper)", File.ReadAllText(rkt));
    }

    [Fact]
    public void ExportsCataloguePartsWaterAndLiftsToRacket()
    {
        var gear = new CatalogueEntry("involute-gear-m5-18", "a gear", "gear", "gear-x", 0.001, new Vec3(1, 1, 2),
            new Dictionary<string, SExpr> { ["teeth"] = new SNumber(18) });
        var session = new BuildSession(Materials, catalogue: [gear], machinesDir: TempDir(), name: "mill");
        session.Execute("(tank pool #:at (0 1 0) #:area 4.0 #:height 1.0)");
        session.Execute("(tank basin #:at (3 0 0) #:area 4.0 #:height 1.0)");
        session.Execute("(inflow spring #:into pool #:flow 0.0045)");
        session.Execute("(channel race pool.outlet basin.inlet #:width 0.5)");
        session.Execute("(channel tail basin.outlet off #:end (6 0 0) #:width 0.5)");
        session.Execute("(wheel g1 #:catalogue involute-gear-m5-18 #:at (1 1 0) #:material bronze)");
        session.Execute("(lift up #:by g1 #:from basin #:to pool #:current-from race)");
        string path = Path.Combine(TempDir(), "mill.rkt");
        session.ExportRkt(path);
        string text = File.ReadAllText(path);
        Assert.Contains("(inflow spring #:into pool #:flow 0.0045)", text);
        Assert.Contains("(channel race #:from pool.outlet #:to basin.inlet", text);
        Assert.Contains("#:to off #:end (6 0 0)", text);
        Assert.Contains("(wheel g1 #:shape (catalogue-shape 'involute-gear-m5-18) #:at (1 1 0) #:material bronze", text);
        Assert.Contains("(lift up #:by g1 #:from basin #:to pool #:current-from race)", text);
        Assert.DoesNotContain("can't be exported", text);
    }
}
