using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #158: the helper that runs a list of timed actions lands each one the way SimHost's ApplyDue does.</summary>
public class OperatorRunTests
{
    [Fact]
    public void ActionsLandAtTheStartAndAfterTheStepThatReachesTheirTimeInTheOrderWritten()
    {
        var seen = new List<(double Clock, string Action)>();
        double clock = 0;
        var run = new OperatorRun(OperatorLog.Parse("(at 0.03 (b f 2)) (at 0 (a f 1)) (at 0.03 (c f 3)) (at 9 (z f 0))"),
            dt => clock += dt, a => seen.Add((Math.Round(clock, 9), a.Target)));
        Assert.Equal([(0.0, "a")], seen);                                // due at the start: before the first step
        run.Run(0.05, 0.01);                                             // five steps of 0.01 s
        Assert.Equal([(0.0, "a"), (0.03, "b"), (0.03, "c")], seen);      // after the third step, b before c as written
        Assert.Equal(1, run.Pending);                                    // the one at 9 s is still waiting
        Assert.Equal(0.05, run.Time, precision: 12);
    }

    [Fact]
    public void ARunContinuesFromWhereTheLastLeftOffEvenAtADifferentStepSize()
    {
        int steps = 0;
        var run = new OperatorRun(OperatorLog.Parse("(at 0.011 (a f 1))"), _ => steps++, _ => { });
        run.Run(0.01, 0.01);
        Assert.Equal(1, run.Pending);
        run.Run(0.001, 0.001);
        Assert.Equal(0, run.Pending);
        Assert.Equal(2, steps);
    }
}
