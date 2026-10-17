using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Lessons 6 and 7, "Windlass" and "Treadwheel crane" (docs/plans/tutorial-plan.md, slice B), played through the editor's own command
/// path without Godot: "Put a wheel on this shaft"'s default spot clear of a wide wheel (ShaftSlots.ClearT), the then-lines' {ROLE},
/// the rope join's wind-on settle (on a drum alone, and over a pulley), joins of kind arbor and mesh, (preset "3 m"), and a finished
/// lesson keeping the numbers of the Test that finished it. The runs that prove the lifts are Jolt's, in racket/heroic/tests/lesson-test.rkt.
/// </summary>
public class LessonSixSevenTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    // the real catalogue's lines for these parts (game/meshes/catalogue/catalogue.rktd)
    private const string Catalogue = """
        (catalogue
         (entry involute-gear-m5-18 "18 teeth, module 5 mm" gear "g18" 0.0002 (1.6e-7 1.6e-7 2.5e-7) ((teeth 18.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.045)))
         (entry involute-gear-m5-72 "72 teeth, module 5 mm" gear "g72" 0.004 (3.3e-5 3.3e-5 6.5e-5) ((teeth 72.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.18)))
         (entry pulley-20cm "Pulley sheave, 20.0 cm radius" pulley "pulley-f037a5f4e5" 0.010976480050648371 (0.00011234378340059177 0.00011234378340059187 0.00020466464341419076) ((radius 0.2) (width 0.1) (groove-depth 0.036) (bore 0.03)))
         (entry drum-15cm "Windlass drum, 15.0 cm radius" drum "drum-53b4fb2a96" 0.046143149579340674 (0.0020296185857400424 0.0020296185857400424 0.0006882519544519751) ((radius 0.15) (length 0.6) (flange-radius 0.21) (bore 0.0375)))
         (entry drum-30cm "Windlass drum, 30.0 cm radius" drum "drum-b37c210798" 0.3691451966347254 (0.06494779474368136 0.06494779474368136 0.022024062542463203) ((radius 0.3) (length 1.2) (flange-radius 0.42) (bore 0.075)))
         (entry cart-wheel-120cm "Spoked cart wheel, 120 cm across" cart-wheel "cart-wheel-051a109f5f" 0.024263894176278205 (0.002874653690421196 0.002874653690421196 0.005728065600954447) ((radius 0.6) (width 0.072) (spokes 8.0)))
         (entry disc-wheel-30cm "Solid disc wheel, 30 cm across" disc-wheel "disc-wheel-x" 0.0035 (0.00003 0.00003 0.00004) ((radius 0.15) (width 0.05)))
         (entry treadwheel-4.0m "Roman crane treadwheel, 4.0 m across" treadwheel "treadwheel-ff7bc90969" 0.5891112437526249 (0.9234167677135602 0.9234167677135615 1.5914487003534372) ((radius 2.0) (width 1.2) (spokes 8.0) (treads 32.0))))
        """;

    private static string LessonPath(string file) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "lessons", file);
    private static Lesson Windlass() => Lesson.Parse(File.ReadAllText(LessonPath("06-windlass.lesson")));
    private static Lesson Crane() => Lesson.Parse(File.ReadAllText(LessonPath("07-crane.lesson")));

    private static BuildSession NewSession()
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-lesson67-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new BuildSession(Materials, CatalogueReader.Parse(Catalogue), dir);
    }

    /// <summary>A command as the editor runs it: the lesson looks, and runs its settle commands; the settle lines come back for the test to read.</summary>
    private static (bool Done, IReadOnlyList<string> Settle, string? Reason) Do(BuildSession s, LessonRunner run, string command)
    {
        s.Execute(command);
        var (done, settle, reason) = run.CheckDesign(s.Document);
        foreach (var step in settle) s.Execute(step);
        if (done) run.Rebase(s.Document);
        return (done, settle, reason);
    }

    private static TestFacts Rose(string id, double metres) => new(30, "limit", [new PartRise(id, metres, Math.Max(0, metres), Math.Min(0, metres))], [], [], [], []);
    private static readonly Dictionary<string, List<double>> NoTilts = [];
    private static readonly Dictionary<string, double> NoMasses = [];

    [Fact]
    public void ACatalogueCartWheelOrDiscWheelIsAWheelThatTurnsNotAFixture()
    {
        var entries = CatalogueReader.Parse(Catalogue);
        Assert.Equal("wheel", entries.First(e => e.Id == "cart-wheel-120cm").PartKind);
        Assert.Equal("wheel", entries.First(e => e.Id == "disc-wheel-30cm").PartKind);
        var s = NewSession();
        s.Execute("(wheel w #:at (0 1 0) #:catalogue cart-wheel-120cm #:material oak)");
        Assert.Equal("wheel", s.Document.Parts["w"].Kind);
    }

    [Fact]
    public void AWheelPutOnAShaftStartsClearOfAWideWheelAlreadyOnItAndGearsKeepTheirTenCentimetreStep()
    {
        var s = NewSession();
        s.Execute("(wheel drum #:at (0 3.22 0) #:catalogue drum-15cm #:material oak)");
        var shaft = ShaftSlots.Of(s.Document, "drum")!;
        Assert.Equal(ShaftSlots.Spacing, shaft.DefaultT, 9);
        // a 0.6 m drum, then a 7.2 cm cart wheel: 0.3 + 0.036 + 0.05
        Assert.Equal(0.386, ShaftSlots.ClearT(s.Document, shaft, 0.036), 9);

        s.Execute("(wheel tw #:at (0 2.4 0) #:catalogue treadwheel-4.0m #:material oak)");
        Assert.Equal(1.25, ShaftSlots.ClearT(s.Document, ShaftSlots.Of(s.Document, "tw")!, 0.6), 9);   // a 1.2 m treadwheel, then a 1.2 m drum

        // module-5 gears (4 cm wide): 0.02 + 0.02 + 0.05 is under the 0.1 m step, so the route's train stands where it did
        s.Execute("(wheel g #:at (0 1 0) #:catalogue involute-gear-m5-72 #:material bronze)");
        Assert.Equal(0.1, ShaftSlots.ClearT(s.Document, ShaftSlots.Of(s.Document, "g")!, 0.02), 9);
        s.Execute("(windmill sails #:at (0 11 0))");
        Assert.Equal(ShaftSlots.FirstGearBehindHub, ShaftSlots.ClearT(s.Document, ShaftSlots.Of(s.Document, "sails")!, 0.02), 9);

        Assert.Equal(["gear", "drum", "pulley", "wheel", "solid wheel", "treadwheel"],
                     new[] { "gear", "drum", "pulley", "cart-wheel", "disc-wheel", "treadwheel" }.Select(ShaftSlots.Noun));
    }

    [Fact]
    public void TheWindlassLessonReadsAsFiveBuildStepsAndATest()
    {
        var lesson = Windlass();
        Assert.Equal("windlass", lesson.Id);
        Assert.Equal(["place", "place", "place", "place", "join", "lifted"], lesson.Steps.Select(s => s.Check.Type));
        Assert.Equal(["3 m", null, null, null, null, null], lesson.Steps.Select(s => s.Preset));
        Assert.Equal([null, null, "gear_on", null, null, null], lesson.Steps.Select(s => s.Button));
        Assert.Equal([null, "drum-15cm", "cart-wheel-120cm", null, null, null], lesson.Steps.Select(s => s.Variant));
        var wheel = lesson.Steps[2].Check;
        Assert.Equal(("drum", "handle", "cart-wheel-120cm"), (wheel.OnShaft, wheel.As, wheel.Catalogue));
        Assert.Equal(["(set {drum} #:drive-rpm 3)", "(set {drum} #:drive-torque 40)"], wheel.Then);
        var rope = lesson.Steps[4].Check;
        Assert.Equal(("drum", 0.3, null, ("drum", "load")), (rope.WindOn, rope.Hoist, rope.Over, rope.Between!.Value));
        Assert.Equal(("load", 1.41, 0.15), (lesson.Steps[5].Check.Part, lesson.Steps[5].Check.About, lesson.Steps[5].Check.Within));
    }

    [Fact]
    public void PlayedThroughTheHandTurnsTheDrumTheRopeIsWoundOnItAndTheBlockHangsPlumbUnderItsWindingSide()
    {
        var s = NewSession();
        var run = new LessonRunner(Windlass(), Materials);
        var (done, settle, _) = Do(s, run, "(post post #:at (0.1 0 0) #:material oak #:size-x 0.3 #:size-y 3 #:size-z 0.3)");
        Assert.True(done);
        Assert.Equal(["(move post (0 0 0))", "(set post #:size-x 0.15)", "(set post #:size-z 0.15)"], settle);
        Assert.True(Do(s, run, "(wheel drum #:at (0.05 3.21 0.1) #:catalogue drum-15cm #:material oak)").Done);
        Assert.Equal(new Vec3(0, 3.22, 0), s.Document.Parts["drum"].At);

        // the button's two commands: the wheel alone isn't on the drum's axle yet (build mode looks only after both); the axle completes it
        var (_, _, why) = Do(s, run, "(wheel handle #:at (0 3.22 -0.386) #:catalogue cart-wheel-120cm #:material oak)");
        Assert.Equal("That cart wheel is not on the drum's axle. Take it away, select the drum and press Put a wheel on this shaft.", why);
        (done, settle, _) = Do(s, run, "(arbor drum handle)");
        Assert.True(done);
        // the hand's drive goes on the drum, the axle's first wheel: the engine drives an axle by its first ({drum}, not {id})
        Assert.Equal(["(move handle (0 3.22 -0.4))", "(set drum #:drive-rpm 3)", "(set drum #:drive-torque 40)"], settle);
        Assert.Equal(3, s.Document.Parts["drum"].Number("drive-rpm", 0));
        Assert.Equal(0, s.Document.Parts["handle"].Number("drive-rpm", 0));

        Assert.True(Do(s, run, "(block load #:at (0.15 0.05 0) #:material granite)").Done);
        // the Rope tool's rope, straight between the two; the lesson ties it again wound on the drum, the block hung 0.3 m up under
        // the rim's +x side (turning forward about +z carries it up): 3.22 - (0.35 + 0.05) = 2.82 m of rope
        (done, settle, _) = Do(s, run, "(rope rope-1 #:from (drum 0 0 0) #:to (load 0 0 0) #:length 3.174)");
        Assert.True(done);
        Assert.Equal(["(remove rope-1)", "(move load (0.15 0.35 0))",
                      "(rope rope-1 #:from (drum 0 0 0) #:to (load 0 0.05 0) #:length 2.82 #:wind-on drum #:material hemp #:diameter 0.02)"], settle);
        var rope = Assert.Single(s.Document.Ropes);
        Assert.Equal(("drum", 2.82), (rope.WindOn, rope.Length));

        // the Test: 1.41 within 0.15
        Assert.Equal("lifted", run.Current!.Check.Type);
        Assert.False(run.OnTest(NoTilts, NoMasses, Rose("load", 1.2), s.Document));
        Assert.StartsWith("The block rose 1.2 m, not the 1.41 m it should.", run.Failure);
        Assert.True(run.OnTest(NoTilts, NoMasses, Rose("load", 1.4107), s.Document));
        Assert.True(run.Finished);
        Assert.StartsWith("The block rose 1.41 m in the Test's 30 seconds", run.Fill(run.Lesson.Done, s.Document));
    }

    [Fact]
    public void TheCraneRopeIsWoundOnTheDrumOverThePulleysTopAndDownItsFarSideToTheStone()
    {
        var s = NewSession();
        var run = new LessonRunner(Crane(), Materials);
        var (done, settle, _) = Do(s, run, "(wheel treadwheel #:at (0 2 0) #:catalogue treadwheel-4.0m #:material oak)");
        Assert.True(done);
        Assert.Equal(["(move treadwheel (0 2.4 0))", "(set treadwheel #:drive-rpm 1)", "(set treadwheel #:drive-torque 1373)"], settle);
        Do(s, run, "(wheel drum #:at (0 2.4 -1.25) #:catalogue drum-30cm #:material oak)");
        Assert.True(Do(s, run, "(arbor treadwheel drum)").Done);
        Assert.True(Do(s, run, "(post jib #:at (-4 0 -1.25) #:material oak #:size-x 0.3 #:size-y 3 #:size-z 0.3)").Done);
        Assert.True(Do(s, run, "(wheel pulley #:at (-4.1 3.2 -1.25) #:catalogue pulley-20cm #:material bronze)").Done);
        (done, settle, _) = Do(s, run, "(block stone #:at (-4.45 0.05 -1.25) #:material granite)");
        Assert.True(done);
        Assert.Equal(["(set stone #:size 0.5)", "(move stone (-4.45 0.25 -1.25))"], settle);
        Assert.Equal(337.5, run.MassOf(s.Document, "stone"), 6);

        // the rope: off the drum to the pulley's near side (4.1193), round its top (4 chords, 0.6123) and down 2.41 to the stone's top
        (done, settle, _) = Do(s, run, "(rope rope-1 #:from (drum 0 0 0) #:to (stone 0 0 0) #:length 3.65)");
        Assert.True(done);
        Assert.Equal(["(remove rope-1)", "(move stone (-4.45 0.55 -1.25))",
                      "(rope rope-1 #:from (drum 0 0 0) #:to (stone 0 0.25 0) #:length 7.1416 #:wind-on drum #:over ((-4.05 3.21 -1.25) (-4.1086 3.3514 -1.25) (-4.25 3.41 -1.25) (-4.3914 3.3514 -1.25) (-4.45 3.21 -1.25)) #:turns pulley #:material hemp #:diameter 0.02)"],
                     settle);

        // the Test: 0.94 within 0.12 passes; the lesson then keeps that run's numbers through the player's one-walker try
        Assert.True(run.OnTest(NoTilts, NoMasses, Rose("stone", 0.935), s.Document));
        Assert.True(run.Finished);
        Assert.False(run.OnTest(NoTilts, NoMasses, Rose("stone", -0.3), s.Document));
        Assert.StartsWith("The stone rose 0.94 m in the Test's 30 seconds.", run.Fill(run.Lesson.Done, s.Document));
    }

    [Fact]
    public void JoinsOfKindArborAndMeshCountAndTheirThenLinesNameTheSecondPartAndAnyRole()
    {
        var lesson = Lesson.Parse("""
            (lesson joins
              (step (text "a") (check place (kind wheel) (catalogue involute-gear-m5-72) (as big)))
              (step (text "b") (check place (kind wheel) (catalogue involute-gear-m5-18) (as small)))
              (step (text "c") (check join (kind mesh) (between big small) (then "(set {id} #:start-rpm 1)")))
              (step (text "d") (check place (kind wheel) (catalogue drum-15cm) (as drum)))
              (step (text "e") (check join (kind arbor) (between drum big) (then "(set {big} #:drive-rpm 2)") (then "(set {id} #:angle-deg 5)")))
              (step (text "f") (check test)))
            """);
        var s = NewSession();
        var run = new LessonRunner(lesson, Materials);
        Assert.True(Do(s, run, "(wheel g72 #:at (0 1 0) #:catalogue involute-gear-m5-72 #:material bronze)").Done);
        Assert.True(Do(s, run, "(wheel g18 #:at (0.225 1 0) #:catalogue involute-gear-m5-18 #:material bronze)").Done);
        var (done, settle, _) = Do(s, run, "(mesh g18 g72)");   // either way round
        Assert.True(done);
        Assert.Equal(["(set g18 #:start-rpm 1)"], settle);
        Assert.True(Do(s, run, "(wheel d #:at (0 1 -0.4) #:catalogue drum-15cm #:material oak)").Done);
        (done, settle, _) = Do(s, run, "(arbor g72 d)");
        Assert.True(done);
        Assert.Equal(["(set g72 #:drive-rpm 2)", "(set g72 #:angle-deg 5)"], settle);   // {id}: the join's second part, big

        // the axle taken apart: a resumed lesson goes back to that step
        s.Execute("(unarbor d)");
        run.Revalidate(s.Document);
        Assert.Equal(4, run.Index);
    }
}
