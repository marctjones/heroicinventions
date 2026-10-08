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
        var (done, settle) = run.CheckDesign(s.Document);
        if (settle is not null) s.Execute(settle);
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
}
