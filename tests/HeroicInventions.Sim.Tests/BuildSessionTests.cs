using HeroicInventions.Sim;
using HeroicInventions.Sim.Mechanics;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Thermo;

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
    /// Opening a running machine in the editor to edit it live (issue #75)
    /// must not change it: the machine the editor hands back, before any
    /// edit, is the machine that was running.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShippedMachines))]
    public void OpeningAMachineInTheEditorChangesNothing(string file)
    {
        var def = MachineDef.Parse(File.ReadAllText(file));
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "live");
        session.Open(def);
        var reopened = session.Document.ToMachineDef();
        var a = MachineWriter.Write(def).Split('\n').Where(l => !l.TrimStart().StartsWith(";;")).ToList();
        var b = MachineWriter.Write(reopened).Split('\n').Where(l => !l.TrimStart().StartsWith(";;")).ToList();
        var firstDifference = a.Zip(b).FirstOrDefault(p => p.First != p.Second);
        Assert.True(a.SequenceEqual(b), $"{Path.GetFileName(file)}: {firstDifference.First}\n  became {firstDifference.Second}");
    }

    public static IEnumerable<object[]> ShippedMachines() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "machines"), "*.machine").Select(f => new object[] { f });

    /// <summary>
    /// The first thing a person tried to build: a fire, a pot of water on it,
    /// and a paddle wheel the pot's steam turns. These are the commands the
    /// editor issues for those clicks (place three parts from the palette,
    /// pick the pot in the fire's inspector, click the pot's steam point then
    /// the wheel's). The pot boils, and its steam turns the wheel.
    /// </summary>
    [Fact]
    public void FirePotAndPaddleWheelBuiltFromThePaletteTurns()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "first");
        session.Execute("(hearth fire #:at (0 0 0))");
        session.Execute("(boiler pot #:at (0 0.1 0))");
        session.Execute("(set fire #:heats pot)");
        session.Execute("(jetwheel wheel #:at (0.4 0.15 0))");
        session.Execute("(snap pot.steam wheel.steam-in)");
        var runtime = new MachineRuntime(session.Document.ToMachineDef(), Materials);
        for (int i = 0; i < 60000; i++) runtime.Step(0.01); // ten minutes
        Assert.True(runtime.Boilers["pot"].Temperature >= 100, $"the pot is at {runtime.Boilers["pot"].Temperature:F1} C");
        Assert.True(runtime.JetWheels["wheel"].Rpm > 100, $"the wheel turns at {runtime.JetWheels["wheel"].Rpm:F0} rpm");
    }

    /// <summary>
    /// A mirror placed from the palette starts aimed at nothing (#:onto ?),
    /// which the runtime rejects; (set m #:onto boiler) aims it, the way the
    /// editor's inspector does, and then the machine builds. Naming a part
    /// that isn't there is refused.
    /// </summary>
    [Fact]
    public void SetNamesAPartThatAMirrorHeats()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "sunlit");
        session.Execute("(boiler pot #:at (0 0 0))");
        session.Execute("(mirror m #:at (2 0 0))");
        Assert.ThrowsAny<Exception>(() => new MachineRuntime(session.Document.ToMachineDef(), Materials));
        Assert.Throws<InvalidOperationException>(() => session.Execute("(set m #:onto nowhere)"));
        session.Execute("(set m #:onto pot)");
        Assert.Equal("pot", ((SSymbol)session.Document.Parts["m"].Props["onto"]).Name);
        _ = new MachineRuntime(session.Document.ToMachineDef(), Materials);
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

    /// <summary>
    /// A float valve placed from the palette on an inflow: the cistern settles
    /// where the valve's feed, Q·(shut − h)/travel, equals the tap's orifice
    /// draw 0.6·w·a·√(2g(h − a/2)), inside the valve's band; then the design
    /// round-trips through .machine and exports as a Racket clause.
    /// </summary>
    [Fact]
    public void FloatValveScriptHoldsTheCisternWhereFeedMeetsDrawAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "head");
        session.Execute("(tank cistern #:at (0 0.8 0) #:area 0.25 #:height 0.6 #:water 0.1)");
        session.Execute("(tank receiver #:at (2 0 0) #:area 0.5 #:height 0.75)");
        session.Execute("(inflow aqueduct #:into cistern #:flow 0.002)");
        session.Execute("(channel outlet cistern.outlet receiver.inlet #:width 0.05)");
        session.Execute("(sluice tap #:at (0.25 0.8 0) #:on outlet #:height 0.5 #:opening 0.02)");
        session.Execute("(float-valve ball #:at (0 1.2 0) #:on aqueduct #:shut 0.4 #:travel 0.02)");
        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 60)");

        double Draw(double level) => 0.6 * 0.05 * 0.01 * Math.Sqrt(2 * 9.81 * (level - 0.005));
        double h = 0.4;
        for (int i = 0; i < 200; i++) h = 0.4 - 0.02 * Draw(h) / 0.002;   // 39.17 cm
        var run = session.LastRun!;
        Assert.Equal(h, run.Tanks["cistern"].Level, precision: 4);
        Assert.Equal(Draw(h), run.Sources["aqueduct"].Flow, precision: 6);
        Assert.Equal((0.4 - h) / 0.02, run.FloatValves["ball"].Valve.Opening, precision: 3);

        string saved = Path.Combine(TempDir(), "head.machine");
        session.SaveFile(saved);
        var valve = MachineDef.Parse(File.ReadAllText(saved)).Part("ball")!;
        Assert.Equal("float-valve", valve.Kind);
        Assert.Equal("aqueduct", valve.Symbol("on", ""));
        Assert.Equal(0.4, valve.Number("shut"));
        Assert.Equal(0.02, valve.Number("travel"));

        string rkt = Path.Combine(TempDir(), "head.rkt");
        session.ExportRkt(rkt);
        Assert.Contains("(float-valve ball #:at (0 1.2 0) #:on aqueduct #:shut 0.4 #:travel 0.02 #:material bronze)", File.ReadAllText(rkt));
    }

    /// <summary>
    /// A leak placed from the palette in a barrel: the level follows Torricelli's
    /// draw-down, √(h − hole) falling at Cd·a·√(2g)/(2A), then the design
    /// round-trips through .machine and exports as a Racket clause.
    /// </summary>
    [Fact]
    public void LeakScriptDrawsTheBarrelDownByTorricelliAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "leaky");
        session.Execute("(tank barrel #:at (0 0.3 0) #:area 0.25 #:height 1 #:water 0.2)");
        session.Execute("(tank catch #:at (0.8 0 0) #:area 0.5 #:height 0.3)");
        session.Execute("(leak hole #:at (0.25 0.4 0) #:on barrel #:height 0.1 #:area 0.0005 #:into catch)");
        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 100)");

        double rate = 0.6 * 0.0005 * Math.Sqrt(2 * 9.81) / (2 * 0.25);
        double level = 0.1 + Math.Pow(Math.Sqrt(0.7) - rate * 100, 2);        // 42.59 cm
        var run = session.LastRun!;
        Assert.Equal(level, run.Tanks["barrel"].Level, precision: 3);
        Assert.Equal(0.25 * (0.8 - level) / 0.5, run.Tanks["catch"].Level, precision: 3);
        Assert.Equal(0.6 * 0.0005 * Math.Sqrt(2 * 9.81 * (level - 0.1)), run.Leaks["hole"].Flow, precision: 6);

        string saved = Path.Combine(TempDir(), "leaky.machine");
        session.SaveFile(saved);
        var hole = MachineDef.Parse(File.ReadAllText(saved)).Part("hole")!;
        Assert.Equal("leak", hole.Kind);
        Assert.Equal("barrel", hole.Symbol("on", ""));
        Assert.Equal("catch", hole.Symbol("into", ""));
        Assert.Equal(0.0005, hole.Number("area"));

        string rkt = Path.Combine(TempDir(), "leaky.rkt");
        session.ExportRkt(rkt);
        Assert.Contains("(leak hole #:at (0.25 0.4 0) #:on barrel #:height 0.1 #:area 0.0005 #:coefficient 0.6 #:into catch #:material", File.ReadAllText(rkt));
    }

    /// <summary>
    /// A safety valve placed from the palette on a boiler rated to burst: the
    /// sealed boiler warms to the valve's lift, M·c·dT/dt = Q − h(T − 20), and
    /// then the valve vents (Q − h(T − 20))/L so it never reaches the rating;
    /// the design round-trips through .machine and exports as Racket clauses.
    /// </summary>
    [Fact]
    public void SafetyValveScriptHoldsTheBoilerBelowItsRatingAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "papin");
        session.Execute("(boiler k #:at (0 0.25 0) #:water 10 #:fire 10000 #:burst 200000)");
        session.Execute("(safety-valve guard #:at (0.075 0.55 0) #:on k #:lift 100000 #:bore 0.008)");
        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 600)");

        var run = session.LastRun!;
        double tHold = run.Boilers["k"].Temperature;
        Assert.InRange(run.Boilers["k"].GaugePressure, 100e3, 110e3);
        Assert.False(run.Boilers["k"].Burst);
        Assert.Equal((10000 - 2 * (tHold - 20)) / 2.257e6, run.SafetyValves["guard"].Valve.Flow, precision: 7);
        Assert.True(run.Boilers["k"].Vented > 0.5);

        string saved = Path.Combine(TempDir(), "papin.machine");
        session.SaveFile(saved);
        var def = MachineDef.Parse(File.ReadAllText(saved));
        Assert.Equal(200000, def.Part("k")!.Number("burst"));
        var guard = def.Part("guard")!;
        Assert.Equal("safety-valve", guard.Kind);
        Assert.Equal("k", guard.Symbol("on", ""));
        Assert.Equal(100000, guard.Number("lift"));

        string rkt = Path.Combine(TempDir(), "papin.rkt");
        session.ExportRkt(rkt);
        string text = File.ReadAllText(rkt);
        Assert.Contains("#:burst 200000 #:material bronze)", text);
        Assert.Contains("(safety-valve guard #:at (0.075 0.55 0) #:on k #:lift 100000 #:bore 0.008 #:coefficient 0.8 #:accumulation 0.1 #:material bronze)", text);
    }

    /// <summary>
    /// A bellows placed from the palette on a hearth: scales the hearth's
    /// burn rate by the ratio its forced air adds over the hearth's own
    /// natural draught; round-trips through .machine and exports as Racket.
    /// </summary>
    [Fact]
    public void AmbientIsASceneSettingThatUndoesAndExports()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "frost");
        session.Execute("(tank pond #:at (0 0 0) #:area 1 #:height 1 #:water 0.5)");
        session.Execute("(ambient -10)");
        Assert.Equal(-10, session.Document.Ambient);
        session.Execute("(run 3600)");
        Assert.True(session.LastRun!.Tanks["pond"].Ice > 0.02);

        string rkt = Path.Combine(TempDir(), "frost.rkt");
        session.ExportRkt(rkt);
        Assert.Contains("#:ambient -10\n", File.ReadAllText(rkt));

        session.Execute("(undo)");
        Assert.Equal(20, session.Document.Ambient);
    }

    /// <summary>
    /// Issue #38. (planet mars) stands the scene on Mars: its air takes Mars's
    /// −63 °C, a lift pump over a well 1 m down holds nothing (the limit is
    /// (610 − 605.6 Pa)/(1000 × 3.71) = 1.19 mm, water's vapour pressure at 0 °C
    /// by the sim's Antoine equation being 605.6 Pa), and a number changed on
    /// the preset (#:gravity 9.81) survives export, the command script and undo.
    /// </summary>
    [Fact]
    public void PlanetIsASceneSettingThatChangesTheNumbersAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "red");
        session.Execute("(tank well #:at (0 0 0) #:area 0.5 #:height 1 #:water 0.4)");
        session.Execute("(tank trough #:at (1 0 0) #:area 0.25 #:height 0.4)");
        session.Execute("(pump p #:at (0.5 1.8 0) #:from well #:to trough #:bore 0.15 #:stroke 0.5 #:rpm 20)");
        session.Execute("(planet mars)");
        Assert.Equal(-63, session.Document.Ambient);
        session.Execute("(run 10)");
        var run = session.LastRun!;
        Assert.Equal(3.71, run.Outside.Gravity);
        Assert.Equal(0.0011917, run.Pumps["p"].Limit, 6);
        Assert.Equal(0, run.Pumps["p"].Delivered);
        Assert.Equal(0.0151833, run.Outside.AirDensity, 6);     // 610 × 0.043489 / (8.314 × 210.15)
        Assert.Equal(0.0995, run.Outside.BoilingPoint, 4);

        session.Execute("(planet mars #:gravity 9.81)");
        session.Execute("(ambient 5)");
        var def = session.Document.ToMachineDef();
        Assert.Equal(["(planet mars #:gravity 9.81)", "(ambient 5.0)"], CommandScript.For(def).Take(2));
        var reparsed = MachineDef.Parse(MachineWriter.Write(def));
        Assert.Equal(def.Planet, reparsed.Planet);
        Assert.Equal(5, reparsed.Ambient);
        string rkt = Path.Combine(TempDir(), "red.rkt");
        session.ExportRkt(rkt);
        Assert.Contains("#:planet (planet mars #:gravity 9.81)\n  #:ambient 5\n", File.ReadAllText(rkt));

        session.Execute("(undo)");
        session.Execute("(undo)");
        Assert.Equal(Planet.Mars, session.Document.Planet);
        session.Execute("(undo)");
        Assert.True(session.Document.Planet.IsEarth);
        Assert.Equal(20, session.Document.Ambient);
    }

    /// <summary>
    /// Issue #39. An enclosure placed and filled by commands: a
    /// room (8 m³: the template's 2 m cube) of pure oxygen at 200 kPa in a 20 °C Earth scene, its walls
    /// losing 10 W/K and a 100 W heater in it, settles at 20 + 100/10 = 30 °C;
    /// its pressure follows, 200 × 303.15/293.15 = 206.82 kPa. A boiler inside
    /// boils where water's vapour pressure reaches 206.82 kPa, 121.1 °C. It
    /// round-trips through the command script and the Racket export.
    /// </summary>
    [Fact]
    public void EnclosureScriptHoldsItsOwnAirAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "room");
        session.Execute("(enclosure hab #:at (0 0 0) #:pressure 200kPa #:temperature 20 #:insulation 10 #:heater 100)");
        session.Execute("(set hab #:size-y 2)");
        session.Execute("(set hab #:o2 1)");
        session.Execute("(boiler pot #:at (0.2 0.5 0.2))");
        session.Execute("(run 20000)");
        var run = session.LastRun!;
        var hab = run.Enclosures["hab"];
        Assert.Equal(8, hab.Volume);                                    // the template is 2 m a side: 2 × 2 × 2
        Assert.True(Math.Abs(hab.Temperature - 30) < 0.01, $"{hab.Temperature} °C: the pot, still warming towards the room, takes a few hundredths of a watt");
        Assert.Equal(200 * 303.15 / 293.15, hab.Pressure / 1000, 2);
        Assert.Same(hab, run.ZoneOf("pot"));
        Assert.Equal(Boiler.SaturationTemperature(hab.Pressure), hab.BoilingPoint, 9);
        Assert.Equal(100, hab.OxygenFraction * 100, 9);

        var def = session.Document.ToMachineDef();
        var rebuilt = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "room");
        foreach (var line in CommandScript.For(def)) rebuilt.Execute(line);
        Assert.Equal(MachineWriter.Write(def), MachineWriter.Write(rebuilt.Document.ToMachineDef()));
        string rkt = Path.Combine(TempDir(), "room.rkt");
        session.ExportRkt(rkt);
        Assert.Contains("(enclosure hab #:at (0 0 0) #:size (2 2 2) #:pressure 200000 #:temperature 20 #:air '((o2 1)) #:insulation 10", File.ReadAllText(rkt));
    }

    /// <summary>
    /// A closed box blown down through a small hole into near-vacuum: while
    /// choked the pressure falls as e^(−t/τ), τ = V/(Cd·A·√(γRT)·(2/(γ+1))^((γ+1)/(2(γ−1)))).
    /// For 1 m³ of air (21/79) at 20 °C and a 1 cm² hole, Cd 0.6: τ = 83.77 s.
    /// </summary>
    [Fact]
    public void AChokedLeakEmptiesAnEnclosureExponentially()
    {
        var outside = new Zone(Planet.Mars, 20);
        var room = new Enclosure("box", 1, outside, 100_000, 20, new GasMix(0.21, 0.79, 0, 0, 0)) { LeakArea = 1e-4, Insulation = 0 };
        double gamma = Enclosure.Gamma(room.Moles), rs = Physics.GasConstant / room.MolarMass;
        double tau = 1 / (0.6 * 1e-4 * Math.Sqrt(gamma * rs * 293.15) * Math.Pow(2 / (gamma + 1), (gamma + 1) / (2 * (gamma - 1))));
        Assert.Equal(83.77, tau, 2);
        for (int i = 0; i < 10_000; i++) room.Step(0.01);
        Assert.True(room.Choked);
        Assert.Equal(100 * Math.Exp(-100 / tau), room.Pressure / 1000, 2);
    }

    /// <summary>
    /// Issue #40. A wood fire in a sealed 1 m³ room burns until the oxygen is
    /// down to 15%. Wood, as cellulose, takes 6 O₂ and gives 6 CO₂ and 5 H₂O
    /// per C₆H₁₀O₅: 37.005 mol of O₂ and 30.837 mol of water a kilogram, so the
    /// room gains moles as it burns and the fire stops at m kg where
    /// (n_O2 − 37.005 m)/(n + 30.837 m) = 0.15. The same stove outdoors on Mars,
    /// in 0.17% oxygen, never lights.
    /// </summary>
    [Fact]
    public void AFireBurnsItsRoomsOxygenDownToTheLimitAndWontLightOnMars()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "cell");
        session.Execute("(enclosure cell #:at (0 0 0) #:insulation 1000)");
        session.Execute("(set cell #:size-x 1)");
        session.Execute("(set cell #:size-y 1)");
        session.Execute("(set cell #:size-z 1)");
        session.Execute("(hearth fire #:at (0 0 0) #:power 1000 #:fuel 1)");
        session.Execute("(set fire #:heats cell)");
        session.Execute("(run 2000)");
        var run = session.LastRun!;
        var fire = run.Hearths["fire"];
        double n = 101_325 * 1 / (8.314 * 293.15), o2 = 0.2095 * n;
        double perKgO2 = 6 / 0.16214, perKgWater = 5 / 0.16214;          // mol a kilogram
        double m = (o2 - 0.15 * n) / (perKgO2 + 0.15 * perKgWater);
        Assert.Equal(m, fire.FuelBurned, 5);
        Assert.False(fire.Lit);
        Assert.Equal(0.15, run.Enclosures["cell"].OxygenFraction, 5);
        Assert.Equal(m * Hearth.OxygenPerKg("wood"), fire.OxygenUsed, 5);

        var mars = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "camp");
        mars.Execute("(planet mars)");
        mars.Execute("(boiler pot #:at (0 0.3 0))");
        mars.Execute("(hearth fire #:at (0 0 0) #:power 1000 #:fuel 1)");
        mars.Execute("(set fire #:heats pot)");
        mars.Execute("(run 10)");
        Assert.False(mars.LastRun!.Hearths["fire"].Lit);
        Assert.Equal(0, mars.LastRun!.Hearths["fire"].FuelBurned);
    }

    /// <summary>
    /// Issue #41. Two 8 m³ rooms, one at 200 kPa and one at 100 kPa, joined by a
    /// door opened wide: they come level at 150 kPa (equal volumes, one
    /// temperature, the moles shared) and nothing is lost. An air pump from the
    /// low room to outside, joined by command and left stopped, moves nothing.
    /// Doors and air pumps round-trip through the command script.
    /// </summary>
    [Fact]
    public void ADoorBringsTwoRoomsLevelAndAValveLetsOneOut()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "rooms");
        session.Execute("(enclosure a #:at (0 0 0) #:pressure 200kPa #:insulation 0)");
        session.Execute("(enclosure b #:at (3 0 0) #:pressure 100kPa #:insulation 0)");
        session.Execute("(door d #:at (1.5 0 0) #:open 1)");
        session.Execute("(set d #:from a)");
        session.Execute("(set d #:to b)");
        session.Execute("(air-pump p #:at (3 0 1) #:speed 0)");
        session.Execute("(set p #:from b)");
        session.Execute("(set p #:to outside)");
        session.Execute("(run 5)");
        var run = session.LastRun!;
        double before = (200_000 + 100_000) * 8 / (8.314 * 293.15);
        Assert.Equal(150, run.Enclosures["a"].Pressure / 1000, 4);
        Assert.Equal(150, run.Enclosures["b"].Pressure / 1000, 4);
        Assert.Equal(before, run.Enclosures["a"].TotalMoles + run.Enclosures["b"].TotalMoles, 6);
        Assert.True(run.Doors["d"].Passed > 0);

        var def = session.Document.ToMachineDef();
        var rebuilt = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "rooms");
        foreach (var line in CommandScript.For(def)) rebuilt.Execute(line);
        Assert.Equal(MachineWriter.Write(def), MachineWriter.Write(rebuilt.Document.ToMachineDef()));
    }

    /// <summary>
    /// Issue #69. Weather by command: on Mars, under the sun from 03:00, the
    /// air is on the daily curve's coldest, −80 °C, and a relay pass is on;
    /// a sol later it is back to 03:00 on sol 2, where a storm of τ 5 has the
    /// sun's beam at e^(−(5 − 0.3)·AM) of a clear sky's. Round-trips.
    /// </summary>
    [Fact]
    public void WeatherCommandsRunSolsAndStormsAndRoundTrip()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "sols");
        session.Execute("(planet mars)");
        session.Execute("(sun #:latitude -2 #:day 100 #:time 3)");
        session.Execute("(weather #:passes (3 15))");
        session.Execute("(storm #:sol 2 #:hour 0 #:tau 5 #:sols 1)");
        session.Execute("(tank t #:at (0 0 0))");
        session.Execute("(run 1)");
        var run = session.LastRun!;
        Assert.Equal(-80, run.Ambient, 3);
        Assert.True(run.Weather!.Relay);
        session.Execute("(run 88775)");
        run = session.LastRun!;
        Assert.Equal(2, run.Sun.SolNumber);
        Assert.Equal(3, run.Sun.Time, 6);
        Assert.NotNull(run.Weather!.Storm);
        Assert.Equal(5 - 0.3, run.Sun.ExtraDust, 3);

        var def = session.Document.ToMachineDef();
        Assert.Equal(def.Weather, MachineDef.Parse(MachineWriter.Write(def)).Weather);
        var rebuilt = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "sols");
        foreach (var line in CommandScript.For(def)) rebuilt.Execute(line);
        Assert.Equal(MachineWriter.Write(def), MachineWriter.Write(rebuilt.Document.ToMachineDef()));
    }

    /// <summary>
    /// Issue #56. A crucible under a steady flux settles at its stagnation
    /// temperature, σ(T⁴ − T_air⁴) = C·I, whatever its emissivity: 100 kW/m²
    /// in a 20 °C room gives (1e5/5.670374e-8 + 293.15⁴)^¼ = 1153.6 K,
    /// 880.4 °C, below basalt's 1,200 °C, so nothing melts. And a charge
    /// melts only once its latent heat is in: 1 kg of silica held at
    /// 1,700 °C by a flux far past it melts as its surplus runs to 0.14 MJ.
    /// </summary>
    [Fact]
    public void ACrucibleStopsWhereItReradiatesWhatItReceivesAndMeltsPastIt()
    {
        var pot = new Crucible("pot", SandKind.Named("basalt"), 1, 0.01, 20) { Emissivity = 0.6, HeatInput = 1000 };
        for (int i = 0; i < 20_000; i++) pot.Step(1);
        Assert.Equal(880.44, pot.Temperature, 2);
        Assert.Equal(pot.Stagnation, pot.Temperature, 3);
        Assert.Equal(0, pot.Melted);

        var silica = new Crucible("s", SandKind.Named("silica"), 1, 0.001, 1700) { Emissivity = 1, HeatInput = 2000 };
        double lossAtMelt = Crucible.StefanBoltzmann * 0.001 * (Math.Pow(1973.15, 4) - Math.Pow(293.15, 4));   // 859.1 W
        double seconds = 140_000 / (2000 - lossAtMelt);
        silica.Step(seconds * 0.5);
        Assert.Equal(0.5, silica.Melted, 3);
        Assert.Equal(1700, silica.Temperature, 9);
        Assert.Equal(0.9, silica.Sand.Transmittance);
    }

    /// <summary>
    /// Issue #57. A 1 m pane at 13.5 kPa must be a·√(0.29·q/σ) = 2.365 cm thick
    /// to hold; one of 2 cm cracks, and all the panes of its size with it,
    /// opening their area in the room's wall. Panes round-trip by command.
    /// </summary>
    [Fact]
    public void APaneCracksPastItsPressureAndTheRoomLeaks()
    {
        Assert.Equal(0.02365, Math.Sqrt(0.29 * 13_500 / 7e6), 5);
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "panes");
        session.Execute("(enclosure hut #:at (0 0 0) #:pressure 13.5kPa #:insulation 0)");
        session.Execute("(pane big #:at (0 2 0) #:side 1 #:thickness 0.02 #:count 2)");
        session.Execute("(set big #:on hut)");
        session.Execute("(pane stout #:at (0 2 0.5) #:side 1 #:thickness 0.025)");
        session.Execute("(set stout #:on hut)");
        session.Execute("(planet mars)");
        session.Execute("(run 0.02)");
        var run = session.LastRun!;
        Assert.True(run.Panes["big"].Cracked);
        Assert.False(run.Panes["stout"].Cracked);
        Assert.Equal(2, run.Enclosures["hut"].LeakArea, 9);
        Assert.Equal(7e6 * Math.Pow(0.025, 2) / 0.29, run.Panes["stout"].CrackPressure, 6);

        var def = session.Document.ToMachineDef();
        var rebuilt = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "panes");
        foreach (var line in CommandScript.For(def)) rebuilt.Execute(line);
        Assert.Equal(MachineWriter.Write(def), MachineWriter.Write(rebuilt.Document.ToMachineDef()));
    }

    /// <summary>
    /// Issue #58. A room holding 1 kPa of water vapour (its dew point 7.07 °C by
    /// the sim's Antoine equation) under a roof of 50 W/K to a −20 °C outside
    /// condenses U·(T_dew − T_out)/L on it; a pond of 30 °C water in it
    /// evaporates k·A·(p_sat(30 °C) − p_v).
    /// </summary>
    [Fact]
    public void ARoofCondensesItsHeatsWorthAndAPondEvaporatesByTheVapourGap()
    {
        var outside = new Zone(Planet.Earth, -20);
        var room = new Enclosure("room", 10, outside, 100_000, 20, new GasMix(0.21, 0.78, 0, 0.01, 0)) { Insulation = 0 };
        double dew = Boiler.SaturationTemperature(room.PartialPressure(3));
        Assert.Equal(1000, room.PartialPressure(3), 6);
        var roof = new Roof("lid", room, 50);
        roof.Step(0.01);
        Assert.Equal(50 * (dew + 20) / Physics.LatentHeatVaporization, roof.Rain, 12);
        Assert.Equal(7.07, dew, 2);

        var tank = new Tank("basin", 0, 2, 0.5, 0.5) { Zone = room };
        var pond = new Pond("warm", tank, 30);
        double pv = room.PartialPressure(3);
        pond.Step(0.001);
        Assert.Equal(Pond.DefaultCoefficient * 2 * (Boiler.SaturationPressure(30) - pv), pond.Evaporation, 12);
    }

    /// <summary>
    /// Issue #42. Wood grown is wood burned backwards: plants given 1 kW of light
    /// in a room grow 0.005 × 1000/18e6 kg/s less respiration, and the room gains
    /// 1.1841 kg of O₂ and loses 1.6286 of CO₂ a kg; a hearth burning that wood
    /// in the same room puts both back. An ice melter takes 466.3 kJ a kg from
    /// −63 °C, an electrolyser 17.875 MJ a kg of O₂ over its efficiency.
    /// </summary>
    [Fact]
    public void TreesGiveTheOxygenTheirWoodTakesBackAndTheMelterAndElectrolyserKeepTheirBooks()
    {
        var outside = new Zone(Planet.Mars, -63);
        var room = new Enclosure("room", 30, outside, 13_500, 20, new GasMix(0.21, 0.49, 0.3, 0, 0)) { Insulation = 0 };
        double o2 = room.Moles[0], co2 = room.Moles[2];
        var water = new Tank("water", 0, 1, 1, 0.5) { Zone = room };
        var trees = new Plants("trees", 10, water, () => 1000) { Zone = room };
        for (int i = 0; i < 1000; i++) trees.Step(10);
        double expect = (0.005 * 1000 / 18e6 - Plants.DefaultRespiration * 10) * 10_000;
        Assert.Equal(expect, trees.Wood, 12);
        Assert.Equal(trees.Wood * Wood.OxygenPerKg / GasMix.O2MolarMass, room.Moles[0] - o2, 9);
        Assert.Equal(trees.Wood * Wood.CarbonDioxidePerKg / GasMix.CO2MolarMass, co2 - room.Moles[2], 9);
        var stove = new Hearth(room, 1000, trees.Wood, "wood") { Zone = room };
        trees.Harvest();
        for (int i = 0; i < 100; i++) stove.Step(10);
        Assert.Equal(0, stove.Fuel, 12);
        Assert.Equal(o2, room.Moles[0], 9);
        Assert.Equal(co2, room.Moles[2], 9);

        var tank = new Tank("melt", 0, 1, 1);
        var melter = new Melter("m", tank, -63) { Power = 1000 };
        melter.Step(1);
        Assert.Equal(1000 / 466_300.0, melter.Melted, 12);
        var feed = new Tank("feed", 0, 1, 1, 0.1);
        var cell = new Electrolyser("e", feed, 1000, 0.5);
        cell.Step(1);
        Assert.Equal(500 / (286e3 * 2 / 0.031998), cell.Oxygen, 12);
    }

    /// <summary>Every existing scene is unchanged: Earth's air is the game's 287.05 J/(kg·K) dry air, 1.204118 kg/m³ at 20 °C.</summary>
    [Fact]
    public void EarthIsTheDefaultPlanetAndItsNumbersAreTheOldConstants()
    {
        var zone = new Zone();
        Assert.True(zone.Planet.IsEarth);
        Assert.Equal(Physics.Gravity, zone.Gravity);
        Assert.Equal(Physics.AtmosphericPressure, zone.Pressure);
        Assert.Equal(Physics.AirDensity, zone.AirDensity, 12);
        Assert.Equal(43.49, Planet.Mars.MolarMass * 1000, 2);    // NASA's Mars fact sheet: mean molecular weight 43.49
        Assert.True(MachineDef.Parse("(machine m)").Planet.IsEarth);
    }

    /// <summary>
    /// A bollard placed from the palette with nobody holding the rope lets the
    /// load fall; given one turn and a pull inside the capstan band it holds.
    /// </summary>
    [Fact]
    public void CapstanScriptHoldsInsideTheBandAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "bollard");
        session.Execute("(capstan post #:at (0 2 0) #:turns 1 #:load 200 #:hold 150 #:material oak)");
        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 2)");
        var c = session.LastRun!.Capstans["post"];
        Assert.Equal(Math.Sqrt(0.5 * 0.45), c.Mu, precision: 12);
        Assert.True(c.Held);
        Assert.Equal(1, c.Height, precision: 12);

        session.Execute("(set post #:hold 0)");
        session.Execute("(run 2)");
        Assert.True(session.LastRun!.Capstans["post"].Grounded);

        string rkt = Path.Combine(TempDir(), "bollard.rkt");
        session.ExportRkt(rkt);
        Assert.Contains("(capstan post #:at (0 2 0) #:turns 1 #:load 200 #:hold 0 #:drop 1 #:radius 0.15 #:rope hemp #:material oak)", File.ReadAllText(rkt));
    }

    /// <summary>
    /// A windmill placed from the palette, its stones set to τ₀ from the
    /// console: it settles at the tip-speed ratio where the sails take Cp*
    /// of the wind, and round-trips through .machine and Racket export.
    /// </summary>
    [Fact]
    public void WindmillScriptSettlesAtItsBestTipSpeedAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "mill");
        double tau0 = 0.5 * Physics.AirDensity * Math.PI * 100 * 36 * 10 * 0.3 / 2.5;
        session.Execute("(windmill sails #:at (0 10 0) #:radius 10 #:mass 1500 #:wind 6)");
        session.Execute($"(set sails #:load {tau0.ToString("R", System.Globalization.CultureInfo.InvariantCulture)})");
        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 300)");
        var mill = session.LastRun!.Windmills["sails"];
        Assert.Equal(2.5, mill.TipSpeedRatioNow, precision: 5);
        Assert.Equal(0.3, mill.PowerCoefficient, precision: 5);

        string saved = Path.Combine(TempDir(), "mill.machine");
        session.SaveFile(saved);
        var part = MachineDef.Parse(File.ReadAllText(saved)).Part("sails")!;
        Assert.Equal("windmill", part.Kind);
        Assert.Equal(6, part.Number("wind"));

        string rkt = Path.Combine(TempDir(), "mill.rkt");
        session.ExportRkt(rkt);
        Assert.Contains("(windmill sails #:at (0 10 0) #:radius 10 #:mass 1500 #:wind 6 #:load ", File.ReadAllText(rkt));
        Assert.Contains("#:cp 0.3 #:tip-speed-ratio 2.5 #:material bronze)", File.ReadAllText(rkt));
    }

    /// <summary>
    /// A hearth isn't itself placeable from the palette (it needs a boiler
    /// or sealed vessel to heat), so this starts from the shipped
    /// bellows-forge scene and adds a second bellows, onto the hearth that
    /// had none — it isn't a catalogue part either, so this is also the
    /// coverage for #:on referencing a hearth through the palette/BuildSession
    /// path rather than the Racket compiler.
    /// </summary>
    [Fact]
    public void BellowsScriptSpeedsUpTheHearthsBurnAndRoundTrips()
    {
        var session = NewSession(Path.Combine(AppContext.BaseDirectory, "machines"));
        session.Execute("(load bellows-forge)");
        session.Execute("(bellows extra #:at (-0.3 0.1 0.2) #:on bare #:airflow 0.005)");
        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 1)");

        var run = session.LastRun!;
        double density = Hearth.EnergyDensity("wood"), natural = 5000 / density * Hearth.AirFuelRatio("wood");
        double expectedDraught = (natural + Physics.AirDensity * 0.005) / natural;
        Assert.Equal(expectedDraught, run.Hearths["bare"].Draught, precision: 9);

        string saved = Path.Combine(TempDir(), "forge.machine");
        session.SaveFile(saved);
        var def = MachineDef.Parse(File.ReadAllText(saved));
        var extra = def.Part("extra")!;
        Assert.Equal("bellows", extra.Kind);
        Assert.Equal("bare", extra.Symbol("on", ""));
        Assert.Equal(0.005, extra.Number("airflow"));

        string rkt = Path.Combine(TempDir(), "forge.rkt");
        session.ExportRkt(rkt);
        Assert.Contains("(bellows extra #:at (-0.3 0.1 0.2) #:on bare #:airflow 0.005 #:material bronze)", File.ReadAllText(rkt));
    }

    /// <summary>
    /// Two pumps placed from the palette over the same kind of well: one
    /// with its bucket 6 m over the water delivers A·S·η a stroke, the other
    /// 11 m over, past (P_atm − P_v)/ρg = 10.09 m, delivers nothing however
    /// strong its drive; the design round-trips through .machine and exports
    /// as Racket clauses.
    /// </summary>
    [Fact]
    public void PumpScriptLiftsBelowTheSuctionLimitOnlyAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "wells");
        session.Execute("(tank well #:at (0 0 0) #:area 100 #:height 1 #:water 80)");
        session.Execute("(tank low-cistern #:at (1 6 0) #:area 1 #:height 1)");
        session.Execute("(tank high-cistern #:at (2 11 0) #:area 1 #:height 1)");
        session.Execute("(pump low #:at (0 6.8 0) #:from well #:to low-cistern #:bore 15cm #:stroke 50cm #:rpm 20)");
        session.Execute("(pump high #:at (0.5 11.8 0) #:from well #:to high-cistern #:bore 15cm #:stroke 50cm #:rpm 20 #:force 10000)");
        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 30)");

        var run = session.LastRun!;
        double perStroke = Math.PI * 0.15 * 0.15 / 4 * 0.5 * 0.8;
        Assert.Equal(10, run.Pumps["low"].Strokes);
        Assert.Equal(10 * perStroke, run.Pumps["low"].Delivered, precision: 9);
        Assert.Equal(0, run.Pumps["high"].Delivered);
        Assert.Equal(LiftPump.SuctionLimit(20), run.Pumps["high"].Column, precision: 12);

        string saved = Path.Combine(TempDir(), "wells.machine");
        session.SaveFile(saved);
        var def = MachineDef.Parse(File.ReadAllText(saved));
        var high = def.Part("high")!;
        Assert.Equal("pump", high.Kind);
        Assert.Equal("well", high.Symbol("from", ""));
        Assert.Equal(10000, high.Number("force"));
        Assert.Equal(double.PositiveInfinity, def.Part("low")!.Number("force", double.PositiveInfinity));

        string rkt = Path.Combine(TempDir(), "wells.rkt");
        session.ExportRkt(rkt);
        string text = File.ReadAllText(rkt);
        Assert.Contains("(pump low #:at (0 6.8 0) #:from well #:to low-cistern #:bore 0.15 #:stroke 0.5 #:rpm 20 #:efficiency 0.8 #:material bronze)", text);
        Assert.Contains("#:rpm 20 #:efficiency 0.8 #:force 10000 #:material", text);
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

    /// <summary>An overshot wheel fed #:onto from a tank settles at ρ·g·Q·r·(1 − cos θ) / load, and round-trips.</summary>
    [Fact]
    public void OvershotWheelScriptTurnsAtThePredictedSpeedAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "mill");
        session.Execute("(tank header #:at (0 3 0) #:area 0.5 #:height 0.4 #:water 0.05)");
        session.Execute("(inflow spring #:into header #:flow 0.02)");
        session.Execute("(waterwheel wheel #:at (1 1.5 0) #:radius 1.2 #:load 250 #:buckets 24 #:bucket-volume 0.01)");
        session.Execute("(channel race header.outlet off #:end (0.8 2.8 0) #:width 0.3 #:onto wheel)");
        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 300)");
        double omega = 1000 * 9.81 * 0.02 * 1.2 * (1 - Math.Cos(2 * Math.PI / 3)) / 250;
        Assert.Equal(omega, session.LastRun!.WaterWheels["wheel"].AngularVelocity, precision: 4);

        string saved = Path.Combine(TempDir(), "mill.machine");
        session.SaveFile(saved);
        var wheel = MachineDef.Parse(File.ReadAllText(saved)).Part("wheel")!;
        Assert.Equal(250, wheel.Number("load"));
        string rkt = Path.Combine(TempDir(), "mill.rkt");
        session.ExportRkt(rkt);
        Assert.Contains("(waterwheel wheel #:at (1 1.5 0) #:radius 1.2 #:width 0.3 #:mass 100 #:load 250 #:buckets 24 #:bucket-volume 0.01 #:spill-deg 120", File.ReadAllText(rkt));
    }

    /// <summary>A pendulum scripted onto a greased bearing dies away inside A₀·exp(−c·t / 2I), I from its rod and ball; round-trips; one without a bearing stays with Jolt.</summary>
    [Fact]
    public void GreasedPendulumScriptRunsDownOnTheViscousEnvelopeAndRoundTrips()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: TempDir(), name: "swing");
        session.Execute("(pendulum bob #:at (0 1 0) #:length 0.5 #:start-angle-deg 10 #:material iron #:bearing-radius 0.01 #:bearing-drag 0.05)");
        session.Execute("(pendulum loose #:at (1 1 0) #:length 0.5 #:material iron)");
        Assert.StartsWith("ok:", session.Execute("(check)"));
        session.Execute("(run 30)");
        Assert.False(session.LastRun!.Pendulums.ContainsKey("loose"));
        var p = session.LastRun.Pendulums["bob"];

        double rho = Materials["iron"].Density, L = 0.5, rb = 0.04;
        double rod = rho * Math.PI * 0.01 * 0.01 * L, ball = rho * 4.0 / 3 * Math.PI * rb * rb * rb;
        double inertia = rod * L * L / 3 + ball * (L * L + 0.4 * rb * rb);
        double predicted = 10 * Math.Exp(-0.05 * p.PeakTime / (2 * inertia));
        Assert.InRange(p.Amplitude * 180 / Math.PI, predicted * 0.99, predicted * 1.01);
        Assert.True(p.Swings > 20);

        string saved = Path.Combine(TempDir(), "swing.machine");
        session.SaveFile(saved);
        var def = MachineDef.Parse(File.ReadAllText(saved));
        Assert.Equal(0.05, def.Part("bob")!.Number("bearing-drag"));
        Assert.IsType<SBool>(def.Part("loose")!.Props["bearing-radius"]);
        string rkt = Path.Combine(TempDir(), "swing.rkt");
        session.ExportRkt(rkt);
        string text = File.ReadAllText(rkt);
        Assert.Contains("(pendulum bob #:at (0 1 0) #:length 0.5 #:start-angle-deg 10 #:material iron #:bearing-radius 0.01 #:bearing-mu 0 #:bearing-drag 0.05 #:bearing-wear 0)", text);
        Assert.Contains("(pendulum loose #:at (1 1 0) #:length 0.5 #:start-angle-deg 30 #:material iron)", text);
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
