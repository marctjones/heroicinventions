using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Building plan, phase 4 ("Add a stage"): a wheel on the selected part's axle and a pinion meshing it, as one (begin ...) that one undo
/// takes back. Four stages on the route's sails (solve.steps:89-115, sails at (0 11 0)) are the route's train: wheel-a (72) 1.3 m behind the
/// hub, pinion-b (18) +0.225 m in x, each stage 0.1 m further back on the pinion's axle, 256:1.
/// </summary>
public class StageSlotsTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private const string Catalogue = """
        (catalogue
         (entry involute-gear-m5-18 "18 teeth, module 5 mm" gear "g18" 0.0002 (1.6e-7 1.6e-7 2.5e-7) ((teeth 18.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.045)))
         (entry involute-gear-m5-36 "36 teeth, module 5 mm" gear "g36" 0.001 (2.2e-6 2.2e-6 4e-6) ((teeth 36.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.09)))
         (entry involute-gear-m5-72 "72 teeth, module 5 mm" gear "g72" 0.004 (3.3e-5 3.3e-5 6.5e-5) ((teeth 72.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.18))))
        """;

    private static readonly IReadOnlyList<CatalogueEntry> Entries = CatalogueReader.Parse(Catalogue);
    private static CatalogueEntry E(string id) => Entries.Single(e => e.Id == id);

    private static BuildSession Session(params string[] commands)
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-stage-slots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var s = new BuildSession(Materials, Entries, dir);
        foreach (var c in commands) s.Execute(c);
        return s;
    }

    /// <summary>A stage on <paramref name="anchor"/>, wheel_N and wheel_N+1 as build mode names them; returns the pinion's id.</summary>
    private static string AddStage(BuildSession s, string anchor, ref int serial, string wheel = StageSlots.RouteWheel, string pinion = StageSlots.RoutePinion)
    {
        string w = $"wheel_{serial++}", p = $"wheel_{serial++}";
        var stage = StageSlots.Plan(s.Document, anchor, E(wheel), E(pinion), w, p, "bronze")!;
        s.Execute(stage.Command);
        return p;
    }

    private static void Near(Vec3 got, double x, double y, double z)
    {
        Assert.Equal(x, got.X, 6);
        Assert.Equal(y, got.Y, 6);
        Assert.Equal(z, got.Z, 6);
    }

    [Fact]
    public void FourStagesOnTheSailsAreTheRoutesTrainAt256To1()
    {
        var s = Session("(windmill sails #:at (0 11 0))");
        int serial = 1;
        string anchor = "sails";
        for (int i = 0; i < 4; i++) anchor = AddStage(s, anchor, ref serial);
        var parts = s.Document.Parts;
        double[][] want = [[0, 11, -1.3], [0.225, 11, -1.3], [0.225, 11, -1.4], [0.45, 11, -1.4], [0.45, 11, -1.5], [0.675, 11, -1.5], [0.675, 11, -1.6], [0.9, 11, -1.6]];
        for (int i = 0; i < 8; i++) Near(parts[$"wheel_{i + 1}"].At, want[i][0], want[i][1], want[i][2]);
        Assert.Equal(["sails+wheel_1", "wheel_2+wheel_3", "wheel_4+wheel_5", "wheel_6+wheel_7"], s.Document.Arbors.Select(a => string.Join("+", a.Parts)));
        Assert.Equal(["wheel_1+wheel_2", "wheel_3+wheel_4", "wheel_5+wheel_6", "wheel_7+wheel_8"], s.Document.Meshes.Select(m => $"{m.A}+{m.B}"));
        var t = Assert.Single(TrainRatio.Of(s.Document));
        Assert.Equal("sails -> wheel_8: 256:1", t.Text);
        Assert.All(s.Document.Meshes, m => Assert.True(LinkGestures.FitOf(parts[m.A], parts[m.B]).True));
    }

    [Fact]
    public void OneUndoTakesAWholeStageBackAndRedoPutsItBack()
    {
        var s = Session("(windmill sails #:at (0 11 0))");
        int serial = 1;
        string p = AddStage(s, "sails", ref serial);
        string before = MachineWriter.Write(s.Document.ToMachineDef());
        AddStage(s, p, ref serial);
        Assert.Equal(5, s.Document.Parts.Count);
        Assert.Equal("16:1", Assert.Single(TrainRatio.Of(s.Document)).RatioText);
        s.Execute("(undo)");
        Assert.Equal(before, MachineWriter.Write(s.Document.ToMachineDef()));
        Assert.Equal("4:1", Assert.Single(TrainRatio.Of(s.Document)).RatioText);
        s.Execute("(redo)");
        Assert.Equal(5, s.Document.Parts.Count);
        s.Execute("(undo)");
        s.Execute("(undo)");
        Assert.Equal(["sails"], s.Document.Parts.Keys);
    }

    [Fact]
    public void TheCardsSizesAreTakenAndAStageOnAStageStepsBackOneGearWidth()
    {
        var s = Session("(windmill sails #:at (0 11 0))");
        int serial = 1;
        string p = AddStage(s, "sails", ref serial, "involute-gear-m5-36", "involute-gear-m5-18");
        Near(s.Document.Parts["wheel_2"].At, 0.135, 11, -1.3);   // 0.005 x (36 + 18) / 2
        Assert.Contains("involute-gear-m5-36", MachineWriter.Write(s.Document.ToMachineDef()));
        AddStage(s, p, ref serial);
        Near(s.Document.Parts["wheel_3"].At, 0.135, 11, -1.4);
        Assert.Equal("8:1", Assert.Single(TrainRatio.Of(s.Document)).RatioText);
    }

    [Fact]
    public void AGroupThatFailsPartWayLeavesTheDocumentAndUndoAsTheyWere()
    {
        var s = Session("(windmill sails #:at (0 11 0))", "(tank a #:at (5 0 0) #:area 1.0 #:height 1.0)", "(move a (6 0 0))", "(undo)");
        string before = MachineWriter.Write(s.Document.ToMachineDef());
        Assert.ThrowsAny<Exception>(() => s.Execute("(begin (wheel w #:at (0 11 -1.3) #:catalogue involute-gear-m5-72) (arbor sails w) (frob w))"));
        Assert.Equal(before, MachineWriter.Write(s.Document.ToMachineDef()));
        s.Execute("(redo)");   // the redo the group would have cleared is still there
        Assert.Equal(6.0, s.Document.Parts["a"].At.X);
        s.Execute("(undo)");
        s.Execute("(undo)");   // placing the tank
        Assert.False(s.Document.Parts.ContainsKey("a"));
        Assert.Throws<FormatException>(() => s.Execute("(begin (undo))"));
    }
}
