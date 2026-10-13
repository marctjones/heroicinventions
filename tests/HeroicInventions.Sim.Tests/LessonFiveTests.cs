using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Lesson 5, "Crank and gears" (docs/plans/tutorial-plan.md, slice A), played through the editor's own command path without Godot:
/// the new step fields (select, a button highlight), the place check's (teeth N), (catalogue ID) and (on-shaft ROLE), the "mesh" check
/// (with ROLE), the "turned" outcome and {ROLE.rpm}. The run that proves the gears' speeds is Jolt's, in racket/heroic/tests/lesson-test.rkt.
/// </summary>
public class LessonFiveTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private const string Catalogue = """
        (catalogue
         (entry involute-gear-m5-18 "18 teeth, module 5 mm" gear "g18" 0.0002 (1.6e-7 1.6e-7 2.5e-7) ((teeth 18.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.045)))
         (entry involute-gear-m5-24 "24 teeth, module 5 mm" gear "g24" 0.0004 (4.6e-7 4.6e-7 8e-7) ((teeth 24.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.06)))
         (entry involute-gear-m5-36 "36 teeth, module 5 mm" gear "g36" 0.001 (2.2e-6 2.2e-6 4e-6) ((teeth 36.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.09)))
         (entry involute-gear-m5-72 "72 teeth, module 5 mm" gear "g72" 0.004 (3.3e-5 3.3e-5 6.5e-5) ((teeth 72.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.18))))
        """;

    private static string LessonPath(string file) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "lessons", file);
    private static Lesson Gears() => Lesson.Parse(File.ReadAllText(LessonPath("05-gears.lesson")));

    private static BuildSession NewSession()
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-lesson5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new BuildSession(Materials, CatalogueReader.Parse(Catalogue), dir);
    }

    private static (bool Done, string? Reason) Do(BuildSession s, LessonRunner run, string command)
    {
        s.Execute(command);
        var (done, settle, reason) = run.CheckDesign(s.Document);
        foreach (var step in settle) s.Execute(step);
        if (done) run.Rebase(s.Document);
        return (done, reason);
    }

    private static TestFacts Turning(params (string Id, double Rpm)[] wheels) =>
        new(30, "limit", [], [.. wheels.Select(w => new WheelTurn(w.Id, w.Rpm / 2, Math.Abs(w.Rpm / 2), w.Rpm))], [], [], []);

    [Fact]
    public void TheLessonReadsAsFourBuildStepsAndATest()
    {
        var lesson = Gears();
        Assert.Equal("gears", lesson.Id);
        Assert.Equal(["place", "place", "mesh", "mesh", "turned"], lesson.Steps.Select(s => s.Check.Type));
        Assert.Equal([null, null, "crank", "second", null], lesson.Steps.Select(s => s.Select));
        Assert.Equal([null, null, "mesh_with", "mesh_with", null], lesson.Steps.Select(s => s.Button));
        Assert.Equal([null, "involute-gear-m5-72", "involute-gear-m5-18", "involute-gear-m5-36", null], lesson.Steps.Select(s => s.Variant));
        var crank = lesson.Steps[1].Check;
        Assert.Equal((72.0, "crank"), (crank.Teeth, crank.As));
        Assert.Equal(["(set {id} #:drive-rpm 30)", "(set {id} #:drive-torque 5)"], crank.Then);
        Assert.Equal(("crank", 18.0, "second"), (lesson.Steps[2].Check.With, lesson.Steps[2].Check.Teeth, lesson.Steps[2].Check.As));
        var test = lesson.Steps[4].Check;
        Assert.True(test.IsOutcome);
        Assert.Equal(("third", 60.0, 2.0), (test.Part, test.Rpm, test.Within));
    }

    [Fact]
    public void PlayedThroughTheGearsAreRememberedTheCrankIsFittedAndTheTrainIsTwoToOne()
    {
        var s = NewSession();
        var run = new LessonRunner(Gears(), Materials);
        Assert.True(Do(s, run, "(post post_1 #:at (0.1 0 0) #:material oak)").Done);
        Assert.Equal(new Vec3(0, 0, 0), s.Document.Parts["post_1"].At);

        // the wrong gear on top says so; the 72 counts, is set on 1.19 and fitted with its crank
        var (done, why) = Do(s, run, "(wheel g36 #:at (0 1.18 0) #:catalogue involute-gear-m5-36 #:material bronze)");
        Assert.False(done);
        Assert.Equal("That gear has 36 teeth, but this step needs the one with 72. Take it away, pick the right size in the box under the parts list and place it again.", why);
        s.Execute("(remove g36)");
        Assert.True(Do(s, run, "(wheel big #:at (0 1.18 0) #:catalogue involute-gear-m5-72 #:material bronze)").Done);
        Assert.Equal(new Vec3(0, 1.19, 0), s.Document.Parts["big"].At);
        Assert.Equal(30, s.Document.Parts["big"].Number("drive-rpm", 0));
        Assert.Equal(5, s.Document.Parts["big"].Number("drive-torque", 0));
        Assert.Equal("big", run.Roles["crank"]);

        // the mouse tool's commands one at a time: a gear placed but not yet meshed is not nudged about; the mesh completes the step
        Assert.Equal((false, (string?)null), Do(s, run, "(wheel small #:at (0.225 1.19 0) #:catalogue involute-gear-m5-18 #:material bronze)"));
        Assert.True(Do(s, run, "(mesh big small)").Done);
        Assert.Equal("small", run.Roles["second"]);
        Assert.Equal("big -> small: 4:1", Assert.Single(TrainRatio.Of(s.Document)).Text);

        // a 24 meshed with the small gear: the wrong size, said so; meshed with the crank instead of the small gear: said so
        Do(s, run, "(wheel w24 #:at (0.33 1.19 0) #:catalogue involute-gear-m5-24 #:material bronze)");
        (done, why) = Do(s, run, "(mesh small w24)");
        Assert.False(done);
        Assert.StartsWith("That gear has 24 teeth, but this step needs the one with 36.", why);
        s.Execute("(remove w24)");
        run.Rebase(s.Document);
        Do(s, run, "(wheel w36 #:at (0 1.46 0) #:catalogue involute-gear-m5-36 #:material bronze)");
        (done, why) = Do(s, run, "(mesh big w36)");
        Assert.False(done);
        Assert.Equal("That meshes the crank gear with a new gear, but this step's gear meshes with the second gear. Undo it (Ctrl+Z), select the second gear and press Add a meshing gear.", why);
        s.Execute("(remove w36)");
        Do(s, run, "(wheel last #:at (0.36 1.19 0) #:catalogue involute-gear-m5-36 #:material bronze)");
        Assert.True(Do(s, run, "(mesh small last)").Done);
        Assert.Equal("last", run.Roles["third"]);
        Assert.Equal("big -> last: 2:1", Assert.Single(TrainRatio.Of(s.Document)).Text);

        // the Test: the 36 at 60 rpm (within 2) passes; the done text quotes the traced speeds
        Assert.Equal("turned", run.Current!.Check.Type);
        Assert.False(run.OnTest(new Dictionary<string, List<double>>(), new Dictionary<string, double>(), Turning(("big", 30), ("small", -120), ("last", 57.5)), s.Document));
        Assert.StartsWith("The last gear was turning at 58 rpm at the end, not the 60 rpm it should.", run.Failure);
        Assert.True(run.OnTest(new Dictionary<string, List<double>>(), new Dictionary<string, double>(), Turning(("big", 30), ("small", -120), ("last", 60)), s.Document));
        Assert.True(run.Finished);
        Assert.StartsWith("The small gear turned at 120 rpm and the last gear at 60 rpm", run.Fill(run.Lesson.Done, s.Document));
    }

    [Fact]
    public void ANewPartIsNamedByTheRoleItWouldTakeWhenItWouldCount()
    {
        var s = NewSession();
        var run = new LessonRunner(Gears(), Materials);
        PartSpec Gear(int teeth) => PartTemplates.Create(CatalogueReader.Parse(Catalogue).First(e => e.Id == $"involute-gear-m5-{teeth}"), "probe", new Vec3(0, 0, 0), "bronze");
        Assert.Equal("post", run.RoleFor(PartTemplates.Create("post", "probe", new Vec3(0, 0, 0), "oak")));
        Assert.Null(run.RoleFor(PartTemplates.Create("block", "probe", new Vec3(0, 0, 0), "oak")));   // not what the step counts
        Do(s, run, "(post post #:at (0 0 0) #:material oak)");
        Assert.Null(run.RoleFor(Gear(36)));   // the step wants the 72
        Assert.Equal("crank", run.RoleFor(Gear(72)));
        Do(s, run, "(wheel crank #:at (0 1.18 0) #:catalogue involute-gear-m5-72 #:material bronze)");
        Assert.Equal("crank", run.Roles["crank"]);
        Assert.Equal("second", run.RoleFor(Gear(18)));   // a mesh step names its gear too
    }

    [Fact]
    public void ALostMeshTakesTheLessonBackToItsStep()
    {
        var s = NewSession();
        var run = new LessonRunner(Gears(), Materials);
        foreach (var c in new[] { "(post p #:at (0 0 0) #:material oak)", "(wheel big #:at (0 1.19 0) #:catalogue involute-gear-m5-72 #:material bronze)",
                                  "(wheel small #:at (0.225 1.19 0) #:catalogue involute-gear-m5-18 #:material bronze)", "(mesh big small)",
                                  "(wheel last #:at (0.36 1.19 0) #:catalogue involute-gear-m5-36 #:material bronze)", "(mesh small last)" })
            Do(s, run, c);
        Assert.Equal(4, run.Index);
        var roles = new Dictionary<string, string>(run.Roles);
        s.Execute("(unmesh small last)");
        run.Restore(4, roles);
        run.Revalidate(s.Document);
        Assert.Equal(3, run.Index);   // the last gear is there but not meshed: back to meshing it
        Assert.False(run.Roles.ContainsKey("third"));
        s.Execute("(remove small)");
        run.Restore(4, roles);
        run.Revalidate(s.Document);
        Assert.Equal(2, run.Index);   // the second gear is gone: back to adding it
    }

    [Fact]
    public void APlaceCheckCanAskForACataloguePartOnARememberedShaft()
    {
        var lesson = Lesson.Parse("""
            (lesson shaft (title "t")
              (step (text "a") (check place (kind wheel) (catalogue involute-gear-m5-72) (as drum)))
              (step (text "b") (highlight "button:preset:3 m") (check place (kind wheel) (teeth 18) (on-shaft drum) (as pinion))))
            """);
        Assert.Equal("preset:3 m", lesson.Steps[1].Button);
        var s = NewSession();
        var run = new LessonRunner(lesson, Materials);
        Assert.False(Do(s, run, "(wheel a #:at (0 1 0) #:catalogue involute-gear-m5-36 #:material bronze)").Done);
        Assert.True(Do(s, run, "(wheel b #:at (1 1 0) #:catalogue involute-gear-m5-72 #:material bronze)").Done);
        var (done, why) = Do(s, run, "(wheel c #:at (1 1 -0.1) #:catalogue involute-gear-m5-18 #:material bronze)");
        Assert.False(done);
        Assert.Equal("That gear is not on the drum's axle. Take it away, select the drum and press Put a gear on this shaft.", why);
        Assert.True(Do(s, run, "(arbor b c)").Done);
        Assert.Equal("c", run.Roles["pinion"]);
    }
}
