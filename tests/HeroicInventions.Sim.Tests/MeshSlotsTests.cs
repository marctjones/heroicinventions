using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Building plan, phase 3 ("Add a meshing gear" and the live ratio): the centre distance of two gears, the pitch circle a new gear rides
/// round a selected wheel, how far a mesh is from true, and the ratio from a prime mover to its train's last gear. The route's train
/// (solve.steps): sails at (0 11 0); wheel-a (72) 1.3 m behind; pinion-b (18) +0.225 m in x; four such stages, 256:1.
/// </summary>
public class MeshSlotsTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private const string Catalogue = """
        (catalogue
         (entry involute-gear-m5-18 "18 teeth, module 5 mm" gear "g18" 0.0002 (1.6e-7 1.6e-7 2.5e-7) ((teeth 18.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.045)))
         (entry involute-gear-m5-24 "24 teeth, module 5 mm" gear "g24" 0.0004 (4.6e-7 4.6e-7 8e-7) ((teeth 24.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.06)))
         (entry involute-gear-m5-36 "36 teeth, module 5 mm" gear "g36" 0.001 (2.2e-6 2.2e-6 4e-6) ((teeth 36.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.09)))
         (entry involute-gear-m5-72 "72 teeth, module 5 mm" gear "g72" 0.004 (3.3e-5 3.3e-5 6.5e-5) ((teeth 72.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.18)))
         (entry involute-gear-m10-18 "18 teeth, module 10 mm" gear "g18b" 0.002 (5e-6 5e-6 8e-6) ((teeth 18.0) (module 0.01) (profile involute) (width 0.08) (pitch-radius 0.09))))
        """;

    private static BuildSession Session(params string[] commands)
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-mesh-slots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var s = new BuildSession(Materials, CatalogueReader.Parse(Catalogue), dir);
        foreach (var c in commands) s.Execute(c);
        return s;
    }

    /// <summary>The route's mill and train, solve.steps:89-115 (stages a..e, <paramref name="stages"/> of them).</summary>
    private static string[] Route(int stages)
    {
        var c = new List<string> { "(windmill sails #:at (0 11 0))" };
        string[] w = ["wheel-a", "wheel-b", "wheel-c", "wheel-d"], p = ["pinion-b", "pinion-c", "pinion-d", "pinion-e"];
        for (int i = 0; i < stages; i++)
        {
            string x = (0.225 * i).ToString(System.Globalization.CultureInfo.InvariantCulture), x1 = (0.225 * (i + 1)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string z = (-1.3 - 0.1 * i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            c.Add($"(wheel {w[i]} #:catalogue involute-gear-m5-72 #:at ({x} 11 {z}))");
            c.Add(i == 0 ? "(arbor sails wheel-a)" : $"(arbor {p[i - 1]} {w[i]})");
            c.Add($"(wheel {p[i]} #:catalogue involute-gear-m5-18 #:at ({x1} 11 {z}))");
            c.Add($"(mesh {w[i]} {p[i]})");
        }
        return [.. c];
    }

    [Fact]
    public void TheCentreDistanceIsTheModuleTimesHalfTheTeethTogether()
    {
        var s = Session("(wheel a #:catalogue involute-gear-m5-72 #:at (0 0 0))", "(wheel b #:catalogue involute-gear-m5-18 #:at (1 0 0))",
                        "(wheel c #:catalogue involute-gear-m5-36 #:at (2 0 0))", "(wheel d #:catalogue involute-gear-m5-36 #:at (3 0 0))",
                        "(wheel e #:catalogue involute-gear-m10-18 #:at (4 0 0))");
        var p = s.Document.Parts;
        Assert.Equal(0.225, LinkGestures.MeshCentreDistance(p["a"], p["b"]), 9);   // 0.005 x (72 + 18) / 2
        Assert.Equal(0.18, LinkGestures.MeshCentreDistance(p["c"], p["d"]), 9);    // 0.005 x (36 + 36) / 2
        Assert.True(LinkGestures.SameModule(p["a"], p["b"]));
        // two modules: no true mesh; the pitch circles still touch at the sum of the radii (0.045 + 0.09), and the fit says the modules differ
        Assert.Equal(0.135, LinkGestures.MeshCentreDistance(p["b"], p["e"]), 9);
        Assert.False(LinkGestures.SameModule(p["b"], p["e"]));
    }

    [Fact]
    public void TheRingAroundTheRoutesFirstWheelPutsItsPinionWhereTheRouteDoesAtAngleZero()
    {
        var s = Session(Route(1).Take(3).ToArray());
        var ring = MeshSlots.Of(s.Document, "wheel-a")!;
        var pinion = PartTemplates.Create(CatalogueReader.Parse(Catalogue).First(e => e.Id == "involute-gear-m5-18"), "ghost", new(0, 0, 0), "bronze");
        double d = LinkGestures.MeshCentreDistance(s.Document.Parts["wheel-a"], pinion);
        var at = ring.At(0, d);
        Assert.Equal(0.225, at.X, 9); Assert.Equal(11, at.Y, 9); Assert.Equal(-1.3, at.Z, 9);
        var up = ring.At(Math.PI / 2, d);
        Assert.Equal(0, up.X, 9); Assert.Equal(11.225, up.Y, 9); Assert.Equal(-1.3, up.Z, 9);   // angles grow upward
        Assert.Equal(0, ring.AngleOf(at), 9);
        Assert.Empty(ring.WheelProps);   // the new gear's axle is z, as the wheel's
        Assert.Null(MeshSlots.Of(s.Document, "sails"));   // a windmill takes a gear on its shaft, not one meshed with it
    }

    [Fact]
    public void ARingAroundAWheelTurnedOnItsSideLiesAcrossItsAxle()
    {
        var s = Session("(wheel a #:catalogue involute-gear-m5-72 #:at (0 1 0))", "(set a #:axis x)");
        var ring = MeshSlots.Of(s.Document, "a")!;
        var at = ring.At(0, 0.225);
        Assert.Equal(0, at.X, 9);   // in the wheel's plane, across x
        Assert.Equal(0.225, Math.Sqrt((at.Y - 1) * (at.Y - 1) + at.Z * at.Z), 9);
        Assert.Contains(("axis", "x"), ring.WheelProps);
    }

    [Fact]
    public void TheRoutesMeshesAreTrueAndAMeshThreeMetresApartOrAcrossModulesIsNot()
    {
        var s = Session(Route(4).Append("(wheel far #:catalogue involute-gear-m5-18 #:at (3 11 -1.3))").Append("(mesh wheel-a far)")
            .Append("(wheel odd #:catalogue involute-gear-m10-18 #:at (0.27 11 -1.3))").Append("(mesh wheel-a odd)").ToArray());
        var p = s.Document.Parts;
        foreach (var (a, b) in new[] { ("wheel-a", "pinion-b"), ("wheel-b", "pinion-c"), ("wheel-c", "pinion-d"), ("wheel-d", "pinion-e") })
            Assert.True(LinkGestures.FitOf(p[a], p[b]).True, LinkGestures.FitOf(p[a], p[b]).Why);
        var far = LinkGestures.FitOf(p["wheel-a"], p["far"]);
        Assert.False(far.True);
        Assert.Equal(2.775, far.Off, 9);
        Assert.Contains("too far", far.Why);
        var odd = LinkGestures.FitOf(p["wheel-a"], p["odd"]);   // at the sum of the pitch radii (0.18 + 0.09), but teeth of two sizes
        Assert.Equal(0, odd.Off, 9);
        Assert.False(odd.True);
        Assert.Contains("modules differ", odd.Why);
    }

    [Fact]
    public void TheRoutesFourStagesAre256To1FromTheSailsToPinionE()
    {
        var t = Assert.Single(TrainRatio.Of(Session(Route(4)).Document));
        Assert.Equal("sails -> pinion-e: 256:1", t.Text);
        Assert.Equal(4, t.Meshes);
        Assert.Equal(256, t.Ratio, 9);
    }

    [Fact]
    public void OneStageIs4To1()
    {
        var t = Assert.Single(TrainRatio.Of(Session(Route(1)).Document));
        Assert.Equal("sails -> pinion-b: 4:1", t.Text);
    }

    [Fact]
    public void A36On36Is1To1ToTheMeshedGearAndAGearDownReadsTheOtherWayRound()
    {
        var even = Assert.Single(TrainRatio.Of(Session("(windmill sails #:at (0 11 0))", "(wheel w #:catalogue involute-gear-m5-36 #:at (0 11 -1.3))",
            "(arbor sails w)", "(wheel v #:catalogue involute-gear-m5-36 #:at (0.18 11 -1.3))", "(mesh w v)").Document));
        Assert.Equal("sails -> v: 1:1", even.Text);
        var down = Assert.Single(TrainRatio.Of(Session("(windmill sails #:at (0 11 0))", "(wheel w #:catalogue involute-gear-m5-24 #:at (0 11 -1.3))",
            "(arbor sails w)", "(wheel v #:catalogue involute-gear-m5-36 #:at (0.15 11 -1.3))", "(mesh w v)").Document));
        Assert.Equal("sails -> v: 2:3", down.Text);
    }

    [Fact]
    public void NoTrainWithoutAMeshOrAPrimeMover()
    {
        Assert.Empty(TrainRatio.Of(Session(Route(1).Take(3).ToArray()).Document));   // the sails and a gear on their shaft: nothing meshed yet
        Assert.Empty(TrainRatio.Of(Session("(wheel a #:catalogue involute-gear-m5-72 #:at (0 0 0))", "(wheel b #:catalogue involute-gear-m5-18 #:at (0.225 0 0))", "(mesh a b)").Document));
    }
}
