using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Lessons 8 and 9, "The mill" and "Mill to generator" (docs/plans/tutorial-plan.md, slice C), played through the editor's own command
/// path without Godot: the "stage" check (two names from one Add a stage edit, and the names Add a stage gives its gears in a lesson), a
/// step's several outcome checks, the "ratio" and "charging" outcomes and {ratio} / {ROLE.watts}, a step's own view, and the Test's
/// generator facts. The runs that prove the speeds and the watts are Jolt's and the sim's, in racket/heroic/tests/lesson-test.rkt.
/// </summary>
public class LessonEightNineTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private const string Catalogue = """
        (catalogue
         (entry involute-gear-m5-18 "18 teeth, module 5 mm" gear "g18" 0.00024138769777083563 (1.5781044876578355e-7 1.5781044876578345e-7 2.512508447926773e-7) ((teeth 18.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.045)))
         (entry involute-gear-m5-48 "48 teeth, module 5 mm" gear "g48" 0.0017 (1.4e-5 1.4e-5 2.6e-5) ((teeth 48.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.12)))
         (entry involute-gear-m5-72 "72 teeth, module 5 mm" gear "g72" 0.003962724349873982 (3.319971575925939e-5 3.319971575925936e-5 6.5342705025219e-5) ((teeth 72.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.18))))
        """;

    private static string LessonPath(string file) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "lessons", file);
    private static Lesson Mill() => Lesson.Parse(File.ReadAllText(LessonPath("08-mill.lesson")));
    private static Lesson Generator() => Lesson.Parse(File.ReadAllText(LessonPath("09-generator.lesson")));
    private static IReadOnlyList<CatalogueEntry> Entries => CatalogueReader.Parse(Catalogue);

    private static BuildSession NewSession()
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-lesson89-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new BuildSession(Materials, Entries, dir);
    }

    private static (bool Done, IReadOnlyList<string> Settle, string? Reason) Do(BuildSession s, LessonRunner run, string command)
    {
        s.Execute(command);
        var (done, settle, reason) = run.CheckDesign(s.Document);
        foreach (var step in settle) s.Execute(step);
        if (done) run.Rebase(s.Document);
        return (done, settle, reason);
    }

    /// <summary>The (begin ...) the inspector's "Add a stage" makes on <paramref name="anchor"/>, with the ids the lesson would give it.</summary>
    private static string Stage(BuildSession s, LessonRunner run, string anchor, int wheelTeeth = 72, int pinionTeeth = 18, string? w = null, string? p = null)
    {
        var wheel = Entries.First(e => e.Id == $"involute-gear-m5-{wheelTeeth}");
        var pinion = Entries.First(e => e.Id == $"involute-gear-m5-{pinionTeeth}");
        var roles = run.StageRolesFor(s.Document, anchor, PartTemplates.Create(wheel, "probe", new Vec3(0, 0, 0), "bronze"), PartTemplates.Create(pinion, "probe", new Vec3(0, 0, 0), "bronze"));
        return StageSlots.Plan(s.Document, anchor, wheel, pinion, w ?? roles?.Wheel ?? "w_x", p ?? roles?.Pinion ?? "p_x", "bronze")!.Command;
    }

    private static readonly Dictionary<string, List<double>> NoTilts = [];
    private static readonly Dictionary<string, double> NoMasses = [];

    [Fact]
    public void TheMillLessonReadsAsSixBuildStepsAndATestWithTwoChecks()
    {
        var lesson = Mill();
        Assert.Equal(["place", "place", "stage", "stage", "stage", "stage", "turned"], lesson.Steps.Select(s => s.Check.Type));
        Assert.Equal(["10.6 m", null, null, null, null, null, null], lesson.Steps.Select(s => s.Preset));
        Assert.Equal(["(set {id} #:load 11440)"], lesson.Steps[1].Check.Then);
        var stage = lesson.Steps[3].Check;
        Assert.Equal(("pinion-b", "wheel-b", "pinion-c", 72.0, 18.0), (stage.On, stage.As, stage.As2, stage.Teeth, stage.Teeth2));
        Assert.Equal([null, null, "add_stage", "add_stage", "add_stage", "add_stage", null], lesson.Steps.Select(s => s.Button));
        var test = lesson.Steps[6];
        Assert.Equal(["turned", "ratio"], test.Checks.Select(c => c.Type));
        Assert.Equal(("wheel-a", "pinion-e", 256.0, 2.0), (test.Also[0].Part, test.Also[0].To, test.Also[0].Is, test.Also[0].Within));
        Assert.Throws<FormatException>(() => Lesson.Parse("(lesson x (step (text \"a\") (check place (kind post)) (check turned (part p) (rpm 1)))) "));
    }

    [Fact]
    public void FourStagesAreRememberedByTheirRolesAndTheTestNeedsBothTheSpeedAndTheRatio()
    {
        var s = NewSession();
        var run = new LessonRunner(Mill(), Materials);
        Assert.True(Do(s, run, "(post post #:at (0.45 0 -1.45) #:material oak #:size-x 0.6 #:size-y 10.6 #:size-z 0.6)").Done);
        var (done, settle, _) = Do(s, run, "(windmill sails #:at (0 11.05 0) #:material oak)");
        Assert.True(done);
        Assert.Equal(["(set sails #:load 11440)"], settle);

        // a 48 driving the 18 on the sails: the wrong sizes, said so; the stage's names are not offered for it
        Assert.Null(run.StageRolesFor(s.Document, "sails", PartTemplates.Create(Entries[1], "probe", new Vec3(0, 0, 0), "bronze"), PartTemplates.Create(Entries[0], "probe", new Vec3(0, 0, 0), "bronze")));
        var (_, _, why) = Do(s, run, Stage(s, run, "sails", 48, 18, "w48", "p18"));
        Assert.Equal("That stage is 48 drives 18, but this step's is 72 drives 18. Undo it (Ctrl+Z), pick 72 and 18 under Add a stage and press it again.", why);
        s.Execute("(undo)");
        run.Rebase(s.Document);

        // Add a stage's one edit: both gears remembered at once, by the names the lesson gave them as they were built
        foreach (var (anchor, w, p) in new[] { ("sails", "wheel-a", "pinion-b"), ("pinion-b", "wheel-b", "pinion-c"), ("pinion-c", "wheel-c", "pinion-d"), ("pinion-d", "wheel-d", "pinion-e") })
        {
            string command = Stage(s, run, anchor);
            Assert.Contains($"(wheel {w} ", command);
            Assert.Contains($"(wheel {p} ", command);
            Assert.True(Do(s, run, command).Done, anchor);
            Assert.Equal((w, p), (run.Roles[w], run.Roles[p]));
        }
        Assert.Equal("sails -> pinion-e: 256:1", Assert.Single(TrainRatio.Of(s.Document)).Text);

        // the Test: the speed alone isn't enough (a train that slipped would turn 2,100 at the end and not 256 to one); both, done
        TestFacts Facts(double pinionTurns, double rpm) => new(30, "limit", [], [new WheelTurn("wheel-a", 2.9756, 2.9756, 8.2), new WheelTurn("pinion-e", pinionTurns, pinionTurns, rpm)], [], [], []);
        Assert.False(run.OnTest(NoTilts, NoMasses, Facts(700, 2101.3), s.Document));
        Assert.StartsWith("The last pinion was turning at 2101 rpm at the end, not the 2,100 rpm it should, or it didn't turn 256 times for each turn of the sails (235).", run.Failure);
        Assert.True(run.OnTest(NoTilts, NoMasses, Facts(761.9428, 2101.3), s.Document));
        Assert.True(run.Finished);
        Assert.StartsWith("The last pinion ended at 2101 rpm, and over the Test it turned 256 times for each turn of the sails", run.Fill(run.Lesson.Done, s.Document));

        // a resumed lesson with a stage taken apart goes back to it, and forgets both its names
        s.Execute("(unmesh wheel-c pinion-d)");
        run.Revalidate(s.Document);
        Assert.Equal(4, run.Index);
        Assert.False(run.Roles.ContainsKey("pinion-d"));
    }

    [Fact]
    public void TheGeneratorLessonHasItsOwnViewsAndChecksTheChargeAndTheSpeed()
    {
        var lesson = Generator();
        Assert.Equal(["place", "place", "stage", "stage", "stage", "place", "place", "place", "charging"], lesson.Steps.Select(s => s.Check.Type));
        Assert.Equal(("gear_on", "generator", "pinion-d"), (lesson.Steps[5].Button, lesson.Steps[5].Variant, lesson.Steps[5].Select));
        Assert.Equal(["(set {id} #:in {cells})", "(set {generator} #:charges {id})"], lesson.Steps[7].Check.Then);
        Assert.NotNull(lesson.Steps[6].View);
        Assert.Null(lesson.Steps[7].View);
        Assert.Equal(3, lesson.Steps[6].View!.Pivot.X);
        var test = lesson.Steps[8];
        Assert.Equal((440.0, 120.0), (test.Check.Watts, test.Check.Within));
        Assert.Equal(("turned", "pinion-d", 1750.0), (test.Also[0].Type, test.Also[0].Part, test.Also[0].Rpm));

        var s = NewSession();
        var run = new LessonRunner(lesson, Materials);
        Do(s, run, "(post post #:at (0.45 0 -1.45) #:material oak #:size-x 0.6 #:size-y 10.6 #:size-z 0.6)");
        Do(s, run, "(windmill sails #:at (0 11.05 0) #:material oak)");
        foreach (var anchor in new[] { "sails", "pinion-b", "pinion-c" }) Assert.True(Do(s, run, Stage(s, run, anchor)).Done);
        // "Put a generator on this shaft": one edit, the can on the pinion's shaft 0.24 m behind it, turned by it
        var (done, settle, _) = Do(s, run, "(begin (generator generator #:at (0.675 11.05 -1.74) #:material iron) (set generator #:on pinion-d))");
        Assert.True(done);
        Assert.Equal(["(set generator #:on pinion-d)"], settle);
        Assert.True(Do(s, run, "(heat-store cells #:at (2.5 0 -3) #:material granite)").Done);
        (done, settle, _) = Do(s, run, "(battery-bank bank #:at (3.5 0 -3))");
        Assert.True(done);
        Assert.Equal(["(set bank #:in cells)", "(set generator #:charges bank)"], settle);
        Assert.Equal("bank", ((SSymbol)s.Document.Parts["generator"].Props["charges"]).Name);

        TestFacts Facts(double watts, double rpm) => new(30, "limit", [], [new WheelTurn("pinion-d", -645.2, 645.2, -rpm)], [], [], [])
        { Generators = [new GeneratorFact("generator", rpm, watts, watts, watts, watts * 7, watts > 0 ? 15.69 : null)] };
        // under the cut-in nothing charges: the fail text says so, with the numbers
        Assert.False(run.OnTest(NoTilts, NoMasses, Facts(0, 1420), s.Document));
        Assert.StartsWith("The generator was charging the bank at 0 W at the end, its shaft at 1420 rpm, not the 440 W and 1,750 rpm it should. Under 1,500 turns a minute", run.Failure);
        Assert.True(run.OnTest(NoTilts, NoMasses, Facts(440.3, 1750.2), s.Document));
        Assert.StartsWith("The generator ended at 1750 rpm, charging the battery at 440 W.", run.Fill(run.Lesson.Done, s.Document));
    }

    [Fact]
    public void TheTestRecordsEachGeneratorsSpeedChargeAndWhenTheBankFirstTookIt()
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double>(), new Dictionary<string, double>());
        var none = new Dictionary<string, BodySample>();
        TestTick Tick(double rpm, double watts, double delivered) => new(0.5, none, new Dictionary<string, double>(), new Dictionary<string, RopeSample>())
        { Generators = new Dictionary<string, GeneratorSample> { ["gen"] = new(rpm, watts, delivered, watts > 0 ? 3 : 0) } };
        rec.Tick(Tick(1200, 0, 0));
        rec.Tick(Tick(1600, 200, 200));
        rec.Tick(Tick(1750, 440, 440));
        var g = Assert.Single(rec.Facts().Generators);
        Assert.Equal(("gen", 1750.0, 440.0, 440.0, 440.0, 1.0), (g.Id, g.EndRpm, g.EndWatts, g.EndDelivered, g.MostDelivered, g.Since!.Value));
        Assert.Equal(320.0, g.Joules, 6);   // (200 + 440) W x 0.5 s
        Assert.Contains("gen gen rpm=1750.0 watts=440.0 delivered=440.0 most=440.0 wh=0.089 since=1.00", TestReport.DataLine(rec.Facts()));
        Assert.Equal(["The gen charged the bank at 440 W at the end, its rotor at 1,750 rpm (from 1.0 s)."], TestReport.GeneratorSentences(rec.Facts(), _ => 1500));

        var cold = new TestFacts(30, "limit", [], [], [], [], []) { Generators = [new GeneratorFact("gen", 1750, 440, 0, 0, 0, null)] };
        Assert.EndsWith("but the bank took none of it (too cold, too hot or full).", Assert.Single(TestReport.GeneratorSentences(cold, _ => 1500)));
        var slow = new TestFacts(30, "limit", [], [], [], [], []) { Generators = [new GeneratorFact("gen", 1420, 0, 0, 0, 0, null)] };
        Assert.Equal("The gen charged nothing: its rotor at 1,420 rpm at the end, under the 1,500 it needs.", Assert.Single(TestReport.GeneratorSentences(slow, _ => 1500)));
    }
}
