using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Lesson 1, the see-saw (#166), played through the editor's own command
/// path without Godot: each step's check, the lesson setting parts exactly
/// on their targets, and the numbers its text quotes. The run that proves
/// the beam balances is Jolt's, in racket/heroic/tests/lesson-test.rkt.
/// </summary>
public class LessonTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static Lesson SeeSaw() =>
        Lesson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "lessons", "01-see-saw.lesson")));

    private static BuildSession NewSession() => new(Materials, catalogue: [], machinesDir: Path.GetTempPath());

    /// <summary>Runs a command, then lets the lesson look at the design, running its settle command as the editor does.</summary>
    private static bool Do(BuildSession s, LessonRunner run, string command)
    {
        s.Execute(command);
        var (done, settle, _) = run.CheckDesign(s.Document);
        foreach (var step in settle) s.Execute(step);
        if (done) run.Rebase(s.Document);   // as the editor does when it shows the next step
        return done;
    }

    [Fact]
    public void TheSeeSawLessonReadsAsSixStepsInPlainWords()
    {
        var lesson = SeeSaw();
        Assert.Equal("see-saw", lesson.Id);
        Assert.Equal(6, lesson.Steps.Count);
        Assert.Equal(["place", "place", "place", "test", "place", "balanced"], lesson.Steps.Select(s => s.Check.Type));
        foreach (var text in lesson.Steps.Select(s => s.Text).Append(lesson.Done))
            foreach (var jargon in new[] { "torque", "#:", "lever_", "Jolt", "rigid", "hinge", "prop" })
                Assert.DoesNotContain(jargon, text);
    }

    [Fact]
    public void PlayingTheDesignStepsSettlesEachPartOnItsTargetAndQuotesTheHandWorkedNumbers()
    {
        var s = NewSession();
        var run = new LessonRunner(SeeSaw(), Materials);

        // a part far from the target doesn't count; a near one does, and is set exactly on it
        Assert.False(Do(s, run, "(lever far #:at (3 0.4 3) #:material pine #:length 2)"));
        Assert.True(Do(s, run, "(lever beam1 #:at (0.25 0.4 -0.1) #:material pine #:length 2)"));
        Assert.Equal(new Vec3(0, 0.4, 0), s.Document.Parts["beam1"].At);
        s.Execute("(remove far)");

        // wood where the stone should go doesn't count: the step asks for stone
        Assert.False(Do(s, run, "(block w #:at (-0.2 0.47 0) #:material oak)"));
        s.Execute("(remove w)");
        Assert.True(Do(s, run, "(block stone #:at (-0.15 0.47 0.05) #:material granite)"));
        Assert.Equal(new Vec3(-0.2, 0.4625, 0), s.Document.Parts["stone"].At);
        Assert.True(Do(s, run, "(block wood #:at (0.2 0.47 0) #:material oak)"));
        Assert.Equal(new Vec3(0.2, 0.4625, 0), s.Document.Parts["wood"].At);

        Assert.Equal(2.7, run.MassOf(s.Document, "stone"), 9);   // 2700 kg/m³ × 0.001 m³
        Assert.Equal(0.72, run.MassOf(s.Document, "wood"), 9);   //  720 kg/m³ × 0.001 m³
        string why = run.Fill(run.Current!.Text, s.Document);
        Assert.Contains("stone 2.7 kg × 0.2 m = 0.54, wood 0.72 kg × 0.2 m = 0.144", why);

        // Test: the tipping run completes step 4 and its tilt is quoted next
        Assert.Equal("test", run.Current!.Check.Type);
        Assert.True(run.OnTest(new Dictionary<string, List<double>> { ["beam1"] = [0, 9, 18] }, new Dictionary<string, double> { ["stone"] = 2.7, ["wood"] = 0.72 }));
        Assert.Contains("tipped 18°", run.Fill(run.Current!.Text, s.Document));
        Assert.Contains("to the green spot, 0.75 m from the pivot", run.Fill(run.Current!.Text, s.Document));
        Assert.Equal(0.75, run.TargetAt(s.Document)!.Value.X, 9);

        // moved nearly out to the spot: it is set on 0.75 exactly
        Assert.False(Do(s, run, "(move wood (0.55 0.47 0))"));
        Assert.True(Do(s, run, "(move wood (0.7 0.47 0))"));
        Assert.Equal(new Vec3(0.75, 0.4625, 0), s.Document.Parts["wood"].At);
        Assert.Equal(0.54, run.MassOf(s.Document, "wood") * run.Along(s.Document, "beam1", "wood"), 9);
        Assert.Equal("balanced", run.Current!.Check.Type);
    }

    [Fact]
    public void TheBalanceCheckPassesOnlyWhenTheTracedBeamStaysWithinThreeDegrees()
    {
        var s = NewSession();
        var run = new LessonRunner(SeeSaw(), Materials);
        Do(s, run, "(lever b #:at (0 0.4 0) #:material pine #:length 2)");
        Do(s, run, "(block h #:at (-0.2 0.46 0) #:material granite)");
        Do(s, run, "(block l #:at (0.2 0.46 0) #:material oak)");
        run.OnTest(new Dictionary<string, List<double>> { ["b"] = [18] }, new Dictionary<string, double>());
        Do(s, run, "(move l (0.75 0.46 0))");

        Assert.False(run.OnTest(new Dictionary<string, List<double>> { ["b"] = [0, 2, 4.5] }, new Dictionary<string, double>()));
        Assert.Contains("leaned 4.5°", run.Failure);
        Assert.False(run.Finished);
        Assert.True(run.OnTest(new Dictionary<string, List<double>> { ["b"] = [0, -0.4, 0.2] }, new Dictionary<string, double>()));
        Assert.True(run.Finished);
        Assert.Contains("never leaned more than 0.4°", run.Fill(run.Lesson.Done, s.Document));
    }

    [Fact]
    public void AResumedLessonGoesBackToTheFirstStepWhosePartIsGone()
    {
        var s = NewSession();
        var run = new LessonRunner(SeeSaw(), Materials);
        Do(s, run, "(lever b #:at (0 0.4 0) #:material pine #:length 2)");
        Do(s, run, "(block h #:at (-0.2 0.46 0) #:material granite)");
        Do(s, run, "(block l #:at (0.2 0.46 0) #:material oak)");
        Assert.Equal(3, run.Index);

        var again = new LessonRunner(SeeSaw(), Materials);
        again.Restore(run.Index, run.Roles);
        s.Execute("(remove h)");
        again.Revalidate(s.Document);
        Assert.Equal(1, again.Index);
        Assert.False(again.Roles.ContainsKey("heavy"));
        Assert.False(again.Roles.ContainsKey("light"));
        Assert.True(again.Roles.ContainsKey("beam"));
    }

    // ------------------------------------------------ why a placement didn't count (#180)

    /// <summary>A session at step 2 (the stone block): the beam is placed and bound.</summary>
    private static (BuildSession S, LessonRunner Run) AtStoneStep()
    {
        var s = NewSession();
        var run = new LessonRunner(SeeSaw(), Materials);
        Assert.True(Do(s, run, "(lever beam1 #:at (0 0.4 0) #:material pine #:length 2)"));
        Assert.Null(run.Nudge);
        return (s, run);
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
    public void NothingPlacedMeansNothingToSay()
    {
        var (s, run) = AtStoneStep();
        Assert.Null(run.CheckDesign(s.Document).Reason);
    }

    [Fact]
    public void AWrongPartSaysWhatItIsAndWhatTheStepNeeds()
    {
        var (s, run) = AtStoneStep();
        Assert.Equal("That is a ball, but this step needs a block. Take the ball away and pick Block in the parts list.",
                     Why(s, run, "(ball b1 #:at (-0.2 0.47 0) #:material iron)"));
        s.Execute("(remove b1)");
        Assert.Null(run.CheckDesign(s.Document).Reason);   // taken away: nothing more to say
    }

    [Fact]
    public void AWrongMaterialSaysWhatWasUsedAndWhatWasAsked()
    {
        var (s, run) = AtStoneStep();
        Assert.Equal("That block is oak, a wood, but this step asks for a stone one, such as granite. Take it away and pick Block again: the lesson picks the material on its card.",
                     Why(s, run, "(block w #:at (-0.2 0.47 0) #:material oak)"));
    }

    [Fact]
    public void AnOffTargetPlacementSaysHowFarAndWhichWay()
    {
        var (s, run) = AtStoneStep();
        // 0.3 m too far left of the green block (and 0.0075 m too high, too little to mention)
        Assert.Equal("Not on the green block yet: that block is 0.3 m from it, and it has to be within 0.15 m. Move it 0.3 m to the right.",
                     Why(s, run, "(block off #:at (-0.5 0.47 0) #:material granite)"));
        // too far right and too high, and back: one move gives both
        s.Execute("(move off (0.2 0.9 0.1))");
        Assert.Equal("Not on the green block yet: that block is 0.6 m from it, and it has to be within 0.15 m. Move it 0.4 m to the left and 0.44 m down and 0.1 m farther from you.",
                     run.CheckDesign(s.Document).Reason);
        // near enough: it counts, and the nudge is gone
        s.Execute("(move off (-0.15 0.47 0.05))");
        var (done, _, reason) = run.CheckDesign(s.Document);
        Assert.True(done);
        Assert.Null(reason);
        Assert.Null(run.Nudge);
    }

    [Fact]
    public void TheWorstFaultIsNamedFirstWhenSeveralPartsAreWrong()
    {
        var (s, run) = AtStoneStep();
        s.Execute("(ball b1 #:at (-0.2 0.47 0) #:material iron)");
        s.Execute("(block w #:at (-0.2 0.47 0) #:material oak)");
        Assert.Contains("is oak", run.CheckDesign(s.Document).Reason);   // the wood block is nearer to counting than the ball
        s.Execute("(block off #:at (-0.5 0.47 0) #:material granite)");
        Assert.Contains("Not on the green block", run.CheckDesign(s.Document).Reason);
    }

    [Fact]
    public void AMovedPartThatWasAlreadyOutIsNotNaggedUntilItIsMovedAgain()
    {
        var s = NewSession();
        var run = new LessonRunner(SeeSaw(), Materials);
        Do(s, run, "(lever b #:at (0 0.4 0) #:material pine #:length 2)");
        Do(s, run, "(block h #:at (-0.2 0.46 0) #:material granite)");
        Do(s, run, "(block l #:at (0.2 0.46 0) #:material oak)");
        run.OnTest(new Dictionary<string, List<double>> { ["b"] = [18] }, new Dictionary<string, double>());
        Assert.Equal("place", run.Current!.Check.Type);
        Assert.Null(run.CheckDesign(s.Document).Reason);   // step 5 starts with the wood block 0.55 m short: that is the lesson, not a mistake
        Assert.Equal("Not on the green block yet: that block is 0.35 m from it, and it has to be within 0.15 m. Move it 0.35 m to the right.",
                     Why(s, run, "(move l (0.4 0.46 0))"));
    }

    [Fact]
    public void ALostPartOrBeamIsSaidSoToo()
    {
        var s = NewSession();
        var run = new LessonRunner(SeeSaw(), Materials);
        Do(s, run, "(lever b #:at (0 0.4 0) #:material pine #:length 2)");
        Do(s, run, "(block h #:at (-0.2 0.46 0) #:material granite)");
        Do(s, run, "(block l #:at (0.2 0.46 0) #:material oak)");
        run.OnTest(new Dictionary<string, List<double>> { ["b"] = [18] }, new Dictionary<string, double>());
        s.Execute("(remove l)");
        Assert.Equal("The light part is not on the bench any more. Undo your last change, or press Start over.", run.CheckDesign(s.Document).Reason);

        var (s2, run2) = AtStoneStep();
        s2.Execute("(remove beam1)");
        s2.Execute("(block h #:at (-0.2 0.47 0) #:material granite)");
        Assert.Equal("This step puts the block on the beam, but there is no beam on the bench any more. Press Start over to begin again.",
                     run2.CheckDesign(s2.Document).Reason);
    }
}
