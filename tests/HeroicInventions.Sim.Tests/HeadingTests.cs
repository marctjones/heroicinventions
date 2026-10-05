using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #83: parts and whole machines at a heading, in the data, the world file and the editor. How the engine behaves turned is checked in racket/heroic/tests (the headless Godot runner).</summary>
public class HeadingTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private const string Rig = """
        (machine rig
          (part slope ramp (material limestone) (at 1 0 2) (props (length 1.0) (width 0.5) (angle-deg 30)) (ports))
          (part rod pendulum (material iron) (at -2 1 0) (props (length 0.5) (start-angle-deg 10) (heading-deg 20)) (ports))
          (part bar lever (material oak) (at 0 1 3) (props (length 1.0) (axis x)) (ports))
          (rope tie (from rod 0.0 -0.5 0.0) (to world 4.0 0.0 0.0) (length 3.0) (over (1.0 2.0 3.0) (0.0 0.0 1.0)) (wind-on #f) (release-deg #f) (material hemp) (diameter 0.01) (nocked #f) (turns #f) (bar #f) (mu #f)))
        """;

    private static MachineDef Rigged() => MachineDef.Parse(Rig);

    [Fact]
    public void ATurnedMachineHasEveryPositionSwungAboutTheVerticalAndEveryPartHeadingAdded()
    {
        var def = Rigged();
        var turned = def.Turned(90);
        // Basis(Up, 90°) takes +X to -Z and +Z to +X: (x, z) -> (z, -x)
        var slope = turned.Part("slope")!;
        Assert.Equal(2, slope.At.X, 1e-9);
        Assert.Equal(-1, slope.At.Z, 1e-9);
        Assert.Equal(0, slope.At.Y);
        Assert.Equal(90, MachineDef.HeadingOf(slope));
        Assert.Equal(110, MachineDef.HeadingOf(turned.Part("rod")!));                  // 20 of its own, and the machine's 90
        Assert.Equal(90, MachineDef.HeadingOf(turned.Part("bar")!));
        // the rope's over-points swing with it; so does its world end; the end on a part stays in the part's own frame
        var rope = turned.Ropes.Single();
        Assert.Equal(3, rope.Over[0].X, 1e-9);
        Assert.Equal(-1, rope.Over[0].Z, 1e-9);
        Assert.Equal(0, rope.To.Local.X, 1e-9);
        Assert.Equal(-4, rope.To.Local.Z, 1e-9);
        Assert.Equal(new Vec3(0, -0.5, 0), rope.From.Local);
    }

    [Fact]
    public void ATurnAndItsReverseGiveBackTheMachine_AndNoTurnIsTheSameMachine()
    {
        var def = Rigged();
        Assert.Same(def, def.Turned(0));
        Assert.Same(def, def.Turned(360));
        var back = def.Turned(37).Turned(-37);
        foreach (var p in def.Parts)
        {
            var q = back.Part(p.Id)!;
            Assert.Equal(p.At.X, q.At.X, 1e-9);
            Assert.Equal(p.At.Z, q.At.Z, 1e-9);
            Assert.Equal(MachineDef.HeadingOf(p), MachineDef.HeadingOf(q), 1e-9);
        }
    }

    [Fact]
    public void AMachineWithAPartBuiltAlongTheAxesRefusesAHeadingRatherThanIgnoreIt()
    {
        var def = MachineDef.Parse("""
            (machine pool
              (part pond tank (material oak) (at 0 0 0) (props (area 1.0) (height 1.0) (water 0.5)) (ports)))
            """);
        var error = Assert.Throws<MachineFormatException>(() => def.Turned(30));
        Assert.Contains("tank pond", error.Message);
        Assert.Same(def, def.Turned(0)); // a heading of nothing needs nothing turned
    }

    [Fact]
    public void AWorldPlacementCarriesAHeadingThroughParseWriteAndPlacing()
    {
        var world = WorldDef.Parse("""
            (world turns
              (place a rig (at 0 0 0))
              (place b rig (at 10 0 5) (heading 90)))
            """);
        Assert.Equal(0, world.Placements[0].Heading);
        Assert.Equal(90, world.Placements[1].Heading);
        string written = world.Write();
        Assert.Contains("(place b rig (at 10.0 0.0 5.0) (heading 90.0))", written);
        Assert.DoesNotContain("(place a rig (at 0.0 0.0 0.0) (heading", written);
        Assert.Equal(90, WorldDef.Parse(written).Placements[1].Heading);

        // placed: turned about its own origin, then stood at the place's point
        var placed = WorldDef.Placed(Rigged(), world.Placements[1], null);
        var slope = placed.Part("slope")!;
        Assert.Equal(10 + 2, slope.At.X, 1e-9);
        Assert.Equal(5 - 1, slope.At.Z, 1e-9);
        Assert.Equal(90, MachineDef.HeadingOf(slope));
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse("(world bad (place a rig (at 0 0 0) (heading)))"));
    }

    [Fact]
    public void ABearingPendulumSwingsTheSameWhateverItsHeading()
    {
        // the sim swings it by its length and bearing alone: a heading changes nothing it can see
        const string text = """
            (machine swing
              (part bob pendulum (material iron) (at 0 1 0) (props (length 0.5) (start-angle-deg 10) (bearing-radius 0.01) (bearing-mu 0.0) (bearing-drag 0.05) (bearing-wear 0.0)) (ports)))
            """;
        var plain = new MachineRuntime(MachineDef.Parse(text), Materials);
        var turned = new MachineRuntime(MachineDef.Parse(text).Turned(53), Materials);
        for (int i = 0; i < 1000; i++) { plain.Step(0.005); turned.Step(0.005); }
        Assert.Equal(plain.Pendulums["bob"].Angle, turned.Pendulums["bob"].Angle, 1e-12);
        Assert.Equal(plain.Pendulums["bob"].Bearing.Heat, turned.Pendulums["bob"].Bearing.Heat, 1e-12);
    }

    [Fact]
    public void ThePartsOfTheEditorTurnAboutTheirPivot_SaveAndExportKeepTheHeading_AndAPartBuiltAlongTheAxesWillNot()
    {
        var session = new BuildSession(Materials, catalogue: [], machinesDir: Path.Combine(Path.GetTempPath(), "heading-" + Guid.NewGuid().ToString("N")), name: "turns");
        session.Execute("(ramp slope #:at (1 0 2) #:length 1 #:width 0.5 #:angle-deg 30 #:material limestone)");
        session.Execute("(block crate #:at (0 0.5 0) #:size 0.2 #:material oak #:heading-deg 10)");
        session.Execute("(pendulum bob #:at (0 1 0) #:length 0.5 #:material iron)");
        session.Execute("(tank pond #:at (4 0 0) #:area 1 #:height 1)");

        Assert.Equal("turned slope to heading 15", session.Execute("(turn slope 15)"));
        session.Execute("(turn slope 15)");
        Assert.Equal(30, session.Document.Parts["slope"].Number("heading-deg"));
        Assert.Equal(new Vec3(1, 0, 2), session.Document.Parts["slope"].At);          // about its own pivot: it does not move
        session.Execute("(turn crate -25)");
        Assert.Equal(345, session.Document.Parts["crate"].Number("heading-deg"));      // 10 - 25, kept in 0 to 360
        session.Execute("(undo)");
        Assert.Equal(10, session.Document.Parts["crate"].Number("heading-deg"));
        Assert.Throws<InvalidOperationException>(() => session.Execute("(turn pond 15)"));
        Assert.Throws<FormatException>(() => session.Execute("(turn slope)"));

        Assert.StartsWith("ok:", session.Execute("(check)"));
        string saved = Path.Combine(Path.GetTempPath(), "heading-" + Guid.NewGuid().ToString("N") + ".machine");
        session.SaveFile(saved);
        Assert.Equal(30, MachineDef.Parse(File.ReadAllText(saved)).Part("slope")!.Number("heading-deg"));
        string rkt = Path.ChangeExtension(saved, ".rkt");
        session.ExportRkt(rkt);
        string text = File.ReadAllText(rkt);
        Assert.Contains("#:angle-deg 30 #:material limestone #:heading-deg 30)", text);
        Assert.Contains("#:heading-deg 10", text);
        // an unturned part says nothing of headings
        Assert.DoesNotContain("(pendulum bob #:at (0 1 0) #:length 0.5 #:start-angle-deg 30 #:material iron #:heading-deg", text);
    }
}
