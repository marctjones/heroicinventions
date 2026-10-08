using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Lessons 2 to 4 (#183, #184, #185), played through the editor's own command path without Godot: each step's check, the
/// lesson's settle commands, the new check kinds (join, lifted, rolled, level), and the worked numbers each lesson quotes.
/// The runs that prove them are Jolt's and the fluid model's, in racket/heroic/tests/lesson-test.rkt.
/// </summary>
public class LessonsTwoToFourTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static string LessonPath(string file) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "lessons", file);
    private static Lesson Load(string file) => Lesson.Parse(File.ReadAllText(LessonPath(file)));

    /// <summary>The catalogue's 10 cm and 5 cm pulley sheaves, as racket/build.rkt writes them to the (gitignored) catalogue.rktd.</summary>
    private static CatalogueEntry Pulley(string id, double radius) =>
        new(id, $"Pulley sheave, {radius * 100:0.0} cm radius", "pulley", "pulley-x", 0.00137206, new Vec3(3.51e-6, 3.51e-6, 6.395770106693461e-6),
            new Dictionary<string, SExpr> { ["radius"] = new SNumber(radius), ["width"] = new SNumber(radius / 2) });

    private static BuildSession NewSession() =>
        new(Materials, [Pulley("pulley-10cm", 0.1), Pulley("pulley-5cm", 0.05)], Path.GetTempPath());

    private static bool Do(BuildSession s, LessonRunner run, string command)
    {
        s.Execute(command);
        var (done, settle, _) = run.CheckDesign(s.Document);
        foreach (var step in settle) s.Execute(step);
        if (done) run.Rebase(s.Document);
        return done;
    }

    private static TestFacts Facts(PartRise[]? rises = null, TankChange[]? tanks = null) =>
        new(4, "settled", rises ?? [], [], tanks ?? [], [], []);

    // ------------------------------------------------------------ all four

    [Fact]
    public void AllFourLessonsAreShippedInOrderAndReadInPlainWords()
    {
        string[] files = Directory.GetFiles(LessonPath(""), "*.lesson").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToArray()!;
        Assert.Equal(["01-see-saw.lesson", "02-pulley.lesson", "03-ramp.lesson", "04-water.lesson"], files);
        var lessons = files.Select(Load).ToList();
        Assert.Equal(["see-saw", "pulley", "ramp", "water"], lessons.Select(l => l.Id));
        for (int i = 0; i < 4; i++) Assert.StartsWith($"Lesson {i + 1}: ", lessons[i].Title);
        foreach (var lesson in lessons)
            foreach (var text in lesson.Steps.SelectMany(s => new[] { s.Text, s.Fail ?? "" }).Append(lesson.Done))
                foreach (var jargon in new[] { "torque", "#:", "_1", "_2", "Jolt", "rigid", "hinge", "prop", "inertia", "conductance", "sim ", "engine" })
                    Assert.DoesNotContain(jargon, text);
    }

    // ------------------------------------------------------------ lesson 2: the pulley

    private static (BuildSession S, LessonRunner Run) PulleyRigged()
    {
        var s = NewSession();
        var run = new LessonRunner(Load("02-pulley.lesson"), Materials);
        Assert.True(Do(s, run, "(post post1 #:at (0.1 0 0.05) #:material oak)"));
        Assert.True(Do(s, run, "(wheel pul #:catalogue pulley-10cm #:at (0.05 1.05 0) #:material oak)"));
        Assert.True(Do(s, run, "(block wood #:at (0.15 0.05 0.05) #:material oak)"));
        Assert.True(Do(s, run, "(block stone #:at (-0.05 0.05 0) #:material granite)"));
        return (s, run);
    }

    [Fact]
    public void ThePulleyLessonSetsEachPartOnItsTargetAndMakesThePostSlim()
    {
        var (s, run) = PulleyRigged();
        Assert.Equal(new Vec3(0, 0, 0), s.Document.Parts["post1"].At);
        Assert.Equal(0.06, s.Document.Parts["post1"].Number("size-x"), 9);
        Assert.Equal(0.06, s.Document.Parts["post1"].Number("size-z"), 9);
        Assert.Equal(new Vec3(0, 1.1, 0), s.Document.Parts["pul"].At);
        Assert.Equal(new Vec3(0.1, 0.05, 0), s.Document.Parts["wood"].At);
        Assert.Equal(new Vec3(-0.1, 0.05, 0), s.Document.Parts["stone"].At);
        Assert.Equal(4, run.Index);
        Assert.Equal("join", run.Current!.Check.Type);
    }

    [Fact]
    public void APulleyOfTheWrongSizeIsSaidSoAndOneOfTheRightSizeCounts()
    {
        var s = NewSession();
        var run = new LessonRunner(Load("02-pulley.lesson"), Materials);
        Assert.True(Do(s, run, "(post post1 #:at (0 0 0) #:material oak)"));
        Assert.False(Do(s, run, "(wheel pul #:catalogue pulley-5cm #:at (0 1.1 0) #:material oak)"));
        Assert.Equal("That pulley is 5 cm in radius, but this lesson uses the 10 cm one. Take it away, pick the right size in the box under the parts list and place it again.",
                     run.Nudge);
        s.Execute("(remove pul)");
        Assert.True(Do(s, run, "(wheel pul #:catalogue pulley-10cm #:at (0 1.1 0) #:material oak)"));
    }

    [Fact]
    public void ARopeTiedBetweenTheBlocksIsThreadedOverThePulleyWithTheStoneHungUp()
    {
        var (s, run) = PulleyRigged();
        string tie = LinkGestures.Command(s.Document, LinkGestures.Kind.Rope, ["stone", "wood"]);
        s.Execute(tie);
        var (done, settle, reason) = run.CheckDesign(s.Document);
        Assert.True(done);
        Assert.Null(reason);
        foreach (var command in settle) s.Execute(command);

        var rope = Assert.Single(s.Document.Ropes);
        Assert.Equal("stone", rope.From.Part);
        Assert.Equal("wood", rope.To.Part);
        Assert.Equal("pul", rope.Turns);
        Assert.Equal(5, rope.Over.Count);
        // 0.45 m up the stone's side (its top is at 0.65, the rim at 1.1), 4 chords of 2 r sin 22.5° = 0.3061 m round the top, 1.0 m down to the wood's top at 0.1
        Assert.Equal(0.45 + 4 * 2 * 0.1 * Math.Sin(Math.PI / 8) + 1.0, rope.Length, 3);
        Assert.Equal(1.7561, rope.Length, 4);
        Assert.Equal(new Vec3(-0.1, 0.6, 0), s.Document.Parts["stone"].At);   // hung 0.55 m up
        Assert.Equal(new Vec3(0.1, 0.05, 0), s.Document.Parts["wood"].At);
        Assert.Equal("lifted", run.Current!.Check.Type);
    }

    [Fact]
    public void ARopeJoiningTheWrongPartsOrTheWrongKindOfLinkIsNudgedAbout()
    {
        var (s, run) = PulleyRigged();
        Assert.Equal("That rope joins the pulley to the granite block, but this step needs it between the granite block and the oak block. Undo it (Ctrl+Z) and join those two.",
                     Why(s, run, LinkGestures.Command(s.Document, LinkGestures.Kind.Rope, ["pul", "stone"])));
        Assert.Equal(4, run.Index);
        // no worry about which way round: wood first, then stone, still counts
        s.Execute("(remove rope-1)");
        Assert.Null(run.CheckDesign(s.Document).Reason);
        s.Execute(LinkGestures.Command(s.Document, LinkGestures.Kind.Rope, ["wood", "stone"]));
        var (done, settle, _) = run.CheckDesign(s.Document);
        Assert.True(done);
        Assert.Equal("(remove rope-1)", settle[0]);
        Assert.Contains("(rope rope-1 #:from (stone 0 0.05 0) #:to (wood 0 0.05 0) #:length 1.7561", settle[3]);
    }

    private static string? Why(BuildSession s, LessonRunner run, string command)
    {
        s.Execute(command);
        var (done, _, reason) = run.CheckDesign(s.Document);
        Assert.False(done);
        Assert.Equal(reason, run.Nudge);
        return reason;
    }

    [Fact]
    public void TheLiftedCheckPassesWhenTheWoodRoseAsFarAsTheStoneFellAndSaysWhatHappenedWhenItDidNot()
    {
        var (s, run) = PulleyRigged();
        s.Execute(LinkGestures.Command(s.Document, LinkGestures.Kind.Rope, ["stone", "wood"]));
        foreach (var c in run.CheckDesign(s.Document).Settle) s.Execute(c);
        run.Rebase(s.Document);

        // the text names the worked numbers: g (2.7 - 0.72) / (2.7 + 0.72) = 5.68 m/s², 0.55 m
        string words = run.Fill(run.Current!.Text, s.Document);
        Assert.Contains("2.7 kg", words);
        Assert.Contains("0.72 kg", words);
        Assert.Contains("5.68 m/s²", words);
        Assert.Contains("0.55 m", words);
        Assert.Equal(9.81 * (2.7 - 0.72) / (2.7 + 0.72), run.Acceleration(s.Document), 9);

        var none = new Dictionary<string, List<double>>();
        var masses = new Dictionary<string, double>();
        Assert.False(run.OnTest(none, masses, Facts([new PartRise("wood", 0.2, 0.2, 0)]), s.Document));
        Assert.Equal("The wood block rose 0.2 m, not the 0.55 m it should: the stone only falls as far as the rope lets it. Is the rope tied from the stone, over the pulley, to the wood? Is the stone really the heavier one? Press Test it again after you have changed it.",
                     run.Failure);
        Assert.False(run.Finished);
        // the sim's own run of this rig: the wood ended 0.5472 m up (0.82 m at its highest, on the stone's bounce), within 0.08 m of 0.55
        Assert.True(run.OnTest(none, masses, Facts([new PartRise("wood", 0.5472, 0.8189, 0), new PartRise("stone", -0.55, 0, -0.55)]), s.Document));
        Assert.True(run.Finished);
        Assert.Contains("The wood rose 0.55 m, just as far as the stone fell.", run.Fill(run.Lesson.Done, s.Document));
    }

    [Fact]
    public void ALesson2ResumedWithoutItsRopeGoesBackToTheJoin()
    {
        var (s, run) = PulleyRigged();
        s.Execute(LinkGestures.Command(s.Document, LinkGestures.Kind.Rope, ["stone", "wood"]));
        foreach (var c in run.CheckDesign(s.Document).Settle) s.Execute(c);
        Assert.Equal(5, run.Index);
        var again = new LessonRunner(Load("02-pulley.lesson"), Materials);
        again.Restore(run.Index, run.Roles);
        s.Execute("(remove rope-1)");
        again.Revalidate(s.Document);
        Assert.Equal(4, again.Index);
    }

    // ------------------------------------------------------------ lesson 3: the ramp

    private static (BuildSession S, LessonRunner Run) BallOnRamp()
    {
        var s = NewSession();
        var run = new LessonRunner(Load("03-ramp.lesson"), Materials);
        Assert.False(Do(s, run, "(ramp far #:at (3 0 3) #:material limestone)"));
        s.Execute("(remove far)");
        Assert.True(Do(s, run, "(ramp ramp1 #:at (0.1 0.02 0.05) #:material limestone)"));
        Assert.True(Do(s, run, "(ball ball1 #:at (-0.05 0.33 -0.85) #:material iron)"));
        return (s, run);
    }

    [Fact]
    public void TheBallIsSetOnTheSlopeWhereTheWorkedNumbersSayAndTheTextQuotesThem()
    {
        var (s, run) = BallOnRamp();
        Assert.Equal(new Vec3(0, 0, 0), s.Document.Parts["ramp1"].At);
        var ball = s.Document.Parts["ball1"].At;
        // 0.9 m up the 15° slope, standing 0.076 m off the slab's middle (half its 5 cm, the ball's 5 cm, a millimetre)
        Assert.Equal(0.9 * Math.Sin(Math.PI / 12) + 0.076 * Math.Cos(Math.PI / 12), ball.Y, 4);
        Assert.Equal(-0.9 * Math.Cos(Math.PI / 12) + 0.076 * Math.Sin(Math.PI / 12), ball.Z, 4);
        Assert.Equal(0, ball.X, 9);
        // it gives up h = 0.3063 - 0.05 = 0.2563 m: v = sqrt(10/7 g h) = 1.895 m/s, and a sliding block would get sqrt(2 g h) = 2.243
        double h = ball.Y - 0.05;
        Assert.Equal(Math.Sqrt(10.0 / 7 * 9.81 * h), LessonRunner.RollingSpeed(s.Document, "ball1"), 9);
        Assert.Equal(1.895, LessonRunner.RollingSpeed(s.Document, "ball1"), 3);
        string words = run.Fill(run.Current!.Text, s.Document);
        Assert.Contains("0.26 m above the ground", words);
        Assert.Contains("2.24 m/s", words);
        Assert.Contains("1.9 m/s", words);
        // the slope is low enough that a 5 cm ball stays under the engine's 47.1 rad/s cap
        Assert.True(LessonRunner.RollingSpeed(s.Document, "ball1") / 0.05 < 47.1);
        Assert.True(h < 0.40);
    }

    [Fact]
    public void TheRolledCheckWantsTheWorkedSpeedAndAMetreOfRolling()
    {
        var (s, run) = BallOnRamp();
        var none = new Dictionary<string, List<double>>();
        var masses = new Dictionary<string, double>();
        // dropped without a ramp's help (a block sliding would reach 2.24): not what rolling gives
        Assert.False(run.OnTest(none, masses, Facts([new PartRise("ball1", -0.2563, 0, -0.2563, 52, 2.24)]), s.Document));
        Assert.Equal("The ball's fastest was 2.24 m/s and it ended 52 m from where it began, but rolling down from 0.26 m up should give 1.9 m/s, and it should roll away at least a metre. Is the ball on the ramp, near the top? Move it back to the green ball and press Test it again.",
                     run.Failure);
        // it never got going
        Assert.False(run.OnTest(none, masses, Facts([new PartRise("ball1", -0.2563, 0, -0.2563, 0.4, 1.9)]), s.Document));
        // the sim's own run of this rig: 1.910 m/s, 51.8 m in 30 s
        Assert.True(run.OnTest(none, masses, Facts([new PartRise("ball1", -0.2563, 0, -0.2563, 51.818, 1.910)]), s.Document));
        Assert.True(run.Finished);
        Assert.Contains("The ball reached 1.91 m/s (worked out beforehand: 1.9 m/s) and rolled on 51.82 m", run.Fill(run.Lesson.Done, s.Document));
    }

    // ------------------------------------------------------------ lesson 4: the water

    private static (BuildSession S, LessonRunner Run) TanksJoined()
    {
        var s = NewSession();
        var run = new LessonRunner(Load("04-water.lesson"), Materials);
        Assert.True(Do(s, run, "(post post1 #:at (0 0 0) #:material oak)"));
        Assert.False(Do(s, run, "(tank wrong #:at (1.5 1 0) #:area 0.05 #:height 0.3 #:water 0.01)"));   // too far from the post's top
        s.Execute("(remove wrong)");
        Assert.True(Do(s, run, "(tank up #:at (0.1 1.01 0.05) #:area 0.05 #:height 0.3 #:water 0.01)"));
        Assert.True(Do(s, run, "(tank low #:at (0.85 0.01 0) #:area 0.05 #:height 0.3 #:water 0.01)"));
        return (s, run);
    }

    [Fact]
    public void TheLowerTankIsSetOnItsSpotAndPouredOut()
    {
        var (s, _) = TanksJoined();
        Assert.Equal(new Vec3(0, 1, 0), s.Document.Parts["up"].At);
        Assert.Equal(new Vec3(0.8, 0, 0), s.Document.Parts["low"].At);
        Assert.Equal(0.01, s.Document.Parts["up"].Number("water"), 9);
        Assert.Equal(0, s.Document.Parts["low"].Number("water"), 9);
    }

    [Fact]
    public void APipeBetweenTheTwoTanksCountsAndAnythingElseIsNudgedAbout()
    {
        var (s, run) = TanksJoined();
        Assert.Equal("That is a rope, but this step needs a pipe. Undo it (Ctrl+Z) and make a pipe between the water tank and the water tank.",
                     Why(s, run, LinkGestures.Command(s.Document, LinkGestures.Kind.Rope, ["up", "low"])));
        s.Execute("(remove rope-1)");
        Assert.Null(run.CheckDesign(s.Document).Reason);
        s.Execute("(snap up.outlet low.inlet)");
        Assert.True(run.CheckDesign(s.Document).Done);
        Assert.Equal("level", run.Current!.Check.Type);
    }

    [Fact]
    public void TheWorkedTimeAndLitresFollowFromThePipeModel()
    {
        var (s, run) = TanksJoined();
        s.Execute("(snap up.outlet low.inlet)");
        Assert.True(run.CheckDesign(s.Document).Done);
        // A da/dt = -k (D + 2a - (a0 + b0)): D = 1 m, a0 = 0.2 m, b0 = 0, k = 0.001, A = 0.05: a(t) = -0.4 + 0.6 e^(-0.04 t), dry at ln(1.5)/0.04
        Assert.Equal(Math.Log(1.5) / 0.04, run.TimeToEmpty(s.Document), 9);
        Assert.Equal(10.14, run.TimeToEmpty(s.Document), 2);
        string words = run.Fill(run.Current!.Text, s.Document);
        Assert.Contains("about 10 seconds", words);
        Assert.Contains("all 10 litres should end up in the lower one", words);
        Assert.Equal("10 litres", run.Fill("{upper.litres}", s.Document));
        Assert.Equal("20 cm", run.Fill("{upper.level}", s.Document));
    }

    [Fact]
    public void TheLevelCheckWantsAllTenLitresInTheLowerTank()
    {
        var (s, run) = TanksJoined();
        s.Execute("(snap up.outlet low.inlet)");
        run.CheckDesign(s.Document);
        var none = new Dictionary<string, List<double>>();
        var masses = new Dictionary<string, double>();
        Assert.False(run.OnTest(none, masses, Facts(tanks: [new TankChange("up", 10, 4), new TankChange("low", 0, 6)]), s.Document));
        Assert.Equal("The lower tank ended with less water than the 10 litres the upper one had: it should hold all of it. Is the pipe joined to a dot on each tank, and is the lower tank below the upper? Change it and press Test it again.",
                     run.Failure);
        // the sim's own run of this rig: 10.000 litres moved, over 11.15 s
        Assert.True(run.OnTest(none, masses, Facts(tanks: [new TankChange("up", 10, 0), new TankChange("low", 0, 10.0)]), s.Document));
        Assert.True(run.Finished);
        Assert.Contains("The lower tank ended with 10 litres more water, 20 cm deep", run.Fill(run.Lesson.Done, s.Document));
    }
}
