using HeroicInventions.Sim.Editor;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// What "Test it" says and how long it runs (#182), fed synthetic ticks so
/// each number is worked out by hand first. The real runs, through Jolt,
/// are in racket/heroic/tests/test-report-test.rkt.
/// </summary>
public class TestReportTests
{
    private const double Dt = 1.0 / 120;
    private static readonly Dictionary<string, RopeSample> NoRopes = [];
    private static readonly Dictionary<string, double> NoTanks = [];

    private static TestTick Tick(IReadOnlyDictionary<string, BodySample>? bodies = null, IReadOnlyDictionary<string, double>? tanks = null,
                                 IReadOnlyDictionary<string, RopeSample>? ropes = null) =>
        new(Dt, bodies ?? new Dictionary<string, BodySample>(), tanks ?? NoTanks, ropes ?? NoRopes);

    private static BodySample Still(double y) => new(y, 0, 0, 0);

    /// <summary>A design where nothing moves still runs the 4 s the first lesson's sentences were made for, and not a tick more.</summary>
    [Fact]
    public void AnIdleDesignRunsTheMinimumAndStops()
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double> { ["a"] = 0.5 }, NoTanks);
        int ticks = 0;
        while (!rec.Done) { rec.Tick(Tick(new Dictionary<string, BodySample> { ["a"] = Still(0.5) })); ticks++; }
        Assert.InRange(rec.Time, 4.0, 4.0 + Dt);
        Assert.Equal("settled", rec.EndedBy);
        Assert.InRange(ticks, 480, 481);
    }

    /// <summary>Something moving for 6 s then still: the run goes on until a second after it stops (7 s), not to 4 s and not to 30 s.</summary>
    [Fact]
    public void ARunGoesOnUntilEverythingHasBeenQuietForASecond()
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double> { ["a"] = 0 }, NoTanks);
        while (!rec.Done)
        {
            bool moving = rec.Time < 6;
            rec.Tick(Tick(new Dictionary<string, BodySample> { ["a"] = new(0, moving ? 0.5 : 0, 0, 0) }));
        }
        Assert.InRange(rec.Time, 7.0, 7.05);
        Assert.Equal("settled", rec.EndedBy);
    }

    /// <summary>A pendulum that never stops: the run stops at the limit, and says so.</summary>
    [Fact]
    public void ARunThatNeverSettlesStopsAtTheLimit()
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double> { ["a"] = 0 }, NoTanks);
        while (!rec.Done) rec.Tick(Tick(new Dictionary<string, BodySample> { ["a"] = new(0, 1, 0, 0) }));
        Assert.InRange(rec.Time, 30.0, 30.0 + Dt);
        Assert.Equal("limit", rec.EndedBy);
        Assert.Equal(10.0, new TestRecorder(10).MaxSeconds);
    }

    /// <summary>A block lifted by a rope over a pulley: the granite falls 0.55 m, so the oak rises 0.55 m; the sentence says it, the data holds it.</summary>
    [Fact]
    public void ALiftedPartIsReportedWithHowFarItRose()
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double> { ["oak"] = 0.05, ["granite"] = 0.6 }, NoTanks);
        for (int i = 0; i < 480; i++)
        {
            double f = Math.Min(1, i / 240.0);   // 2 s up, then still
            rec.Tick(Tick(new Dictionary<string, BodySample>
            {
                ["oak"] = new(0.05 + 0.55 * f, i < 240 ? 0.1 : 0, 0, 0),
                ["granite"] = new(0.6 - 0.55 * f, i < 240 ? 0.1 : 0, 0, 0),
            }));
        }
        var facts = rec.Facts();
        Assert.Equal(0.55, facts.RiseOf("oak")!.Metres, 6);
        Assert.Equal(-0.55, facts.RiseOf("granite")!.Metres, 6);
        Assert.Equal("The oak block rose 0.55 m.", TestReport.RiseSentence("oak block", facts.RiseOf("oak")!));
        Assert.Null(TestReport.RiseSentence("granite block", facts.RiseOf("granite")!));
    }

    [Fact]
    public void APartThatRoseAndCameBackSaysHowHighItGot()
    {
        var s = TestReport.RiseSentence("oak block", new PartRise("oak", 0.55, 0.82, 0));
        Assert.Equal("The oak block rose 0.55 m (it got up to 0.82 m before settling).", s);
        Assert.Null(TestReport.RiseSentence("oak block", new PartRise("oak", 0.04, 0.05, 0)));
    }

    /// <summary>A pulley whose rim keeps pace with a rope hauled 0.55 m, r = 0.1 m: 0.55 / (2 pi 0.1) = 0.8754 turns, reported as 0.88 of a turn.</summary>
    [Fact]
    public void AWheelReportsItsTurns()
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double> { ["pul"] = 2 }, NoTanks);
        double total = 0.55 / 0.1;   // rad
        for (int i = 0; i < 480; i++)
            rec.Tick(Tick(new Dictionary<string, BodySample> { ["pul"] = new(2, 0, i < 240 ? 1 : 0, i < 240 ? total / 240 : 0) }));
        var t = rec.Facts().TurnOf("pul")!;
        Assert.Equal(0.55 / (2 * Math.PI * 0.1), t.Turns, 9);
        Assert.Equal("The bronze pulley turned 0.88 of a turn.", TestReport.TurnSentence("bronze pulley", t));
    }

    /// <summary>Turns are counted past a full circle (the tick's turn is unwrapped, not the total), either way round.</summary>
    [Theory]
    [InlineData(2.5, "turned 2.5 times")]
    [InlineData(-2.5, "turned 2.5 times")]
    [InlineData(1.0, "turned once")]
    [InlineData(12.4, "turned 12 times")]
    [InlineData(0.5, "turned 0.5 of a turn")]
    public void TurnsPastAFullCircleAreCounted(double turns, string said)
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double> { ["w"] = 1 }, NoTanks);
        for (int i = 0; i < 480; i++) rec.Tick(Tick(new Dictionary<string, BodySample> { ["w"] = new(1, 0, 0, turns * 2 * Math.PI / 480) }));
        var t = rec.Facts().TurnOf("w")!;
        Assert.Equal(turns, t.Turns, 9);
        Assert.StartsWith("The oak wheel " + said, TestReport.TurnSentence("oak wheel", t));
    }

    [Fact]
    public void AWheelStillTurningAtTheEndSaysSoAndRunsToTheLimit()
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double> { ["w"] = 1 }, NoTanks);
        double rate = 2 * Math.PI;   // 1 turn a second = 60 rpm
        while (!rec.Done) rec.Tick(Tick(new Dictionary<string, BodySample> { ["w"] = new(1, 0, rate, rate * Dt) }));
        var t = rec.Facts().TurnOf("w")!;
        Assert.Equal("limit", rec.EndedBy);
        Assert.InRange(t.Turns, 30, 30.01);
        Assert.Equal(60, t.EndRpm, 6);
        Assert.Equal("The oak wheel turned 30 times and was still turning at 60 rpm.", TestReport.TurnSentence("oak wheel", t));
    }

    [Fact]
    public void AWheelThatSwungToAndFroSaysSo()
    {
        var s = TestReport.TurnSentence("oak wheel", new WheelTurn("w", 0.01, 0.25, 0));
        Assert.Equal("The oak wheel swung to and fro, up to 0.25 of a turn either way.", s);
        Assert.Null(TestReport.TurnSentence("oak wheel", new WheelTurn("w", 0.01, 0.02, 0)));
    }

    /// <summary>
    /// A 0.05 m² tank with 0.3 m of water draining through a 10 cm² hole at its floor, Cd 0.6, into a lower tank:
    /// Torricelli, h falls linearly in sqrt(h) at (Cd a / A) sqrt(g/2) = 0.026576 per s, so it is empty at
    /// T = sqrt(0.3) / 0.026576 = 20.61 s, and 15 litres have moved. The run ends a second after the last of it stops.
    /// </summary>
    [Fact]
    public void ATankDrainingRunsUntilItIsEmptyAndReportsTheLitres()
    {
        const double area = 0.05, cd = 0.6, hole = 0.001, g = 9.81, h0 = 0.3;
        double k = cd * hole / area * Math.Sqrt(g / 2), t0 = Math.Sqrt(h0) / k;
        Assert.InRange(t0, 20.6, 20.62);
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double>(), new Dictionary<string, double> { ["up"] = area * h0, ["low"] = 0 });
        while (!rec.Done)
        {
            double root = Math.Max(0, Math.Sqrt(h0) - k * rec.Time);
            double up = area * root * root;
            rec.Tick(Tick(tanks: new Dictionary<string, double> { ["up"] = up, ["low"] = area * h0 - up }));
        }
        Assert.InRange(rec.Time, t0 + 0.3, t0 + 1.3);
        var facts = rec.Facts();
        Assert.Equal(-15, facts.TankOf("up")!.Litres, 1);
        Assert.Equal(15, facts.LitresMoved("up", "low"), 1);
        Assert.Equal("15.0 litres ran from up into low.", Assert.Single(TestReport.WaterSentences(facts)));
    }

    /// <summary>Stopped early (Esc) at 10 s: the litres moved so far are Torricelli's at 10 s, 15 - 0.05 (sqrt(0.3) - 0.26576)^2 * 1000 = 11.02.</summary>
    [Fact]
    public void ATestStoppedPartWayReportsWhatMovedSoFar()
    {
        const double area = 0.05;
        double rate = 0.6 * 0.001 / 0.05 * Math.Sqrt(9.81 / 2);
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double>(), new Dictionary<string, double> { ["up"] = area * 0.3, ["low"] = 0 });
        for (int i = 0; i < 1200; i++)
        {
            double root = Math.Sqrt(0.3) - rate * rec.Time;
            double up = area * root * root;
            rec.Tick(Tick(tanks: new Dictionary<string, double> { ["up"] = up, ["low"] = area * 0.3 - up }));
        }
        Assert.Equal("stopped", rec.EndedBy);
        Assert.Equal(11.02, rec.Facts().LitresMoved("up", "low"), 1);
    }

    [Fact]
    public void WaterThatLeavesOrArrivesFromNowhereIsSaidTankByTank()
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double>(), new Dictionary<string, double> { ["a"] = 0.010, ["b"] = 0, ["c"] = 0 });
        rec.Tick(Tick(tanks: new Dictionary<string, double> { ["a"] = 0.004, ["b"] = 0.003, ["c"] = 0.003 }));
        var facts = rec.Facts();
        Assert.Empty(facts.Moves);   // two gained: no single pair to name
        Assert.Equal(["a lost 6.0 litres.", "b gained 3.0 litres.", "c gained 3.0 litres."], TestReport.WaterSentences(facts).ToArray());
        Assert.Equal(6, facts.LitresLost, 6);
        Assert.Equal(6, facts.LitresGained, 6);
    }

    [Fact]
    public void ABrokenRopeIsReportedWithWhatItHeldAndWhatPulledIt()
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double>(), NoTanks);
        for (int i = 0; i < 480; i++)
            rec.Tick(Tick(ropes: new Dictionary<string, RopeSample> { ["r"] = new(i < 3 ? 11.15 : 0, 7.54, 0, i >= 3, false) }));
        var facts = rec.Facts();
        var r = facts.RopeOf("r")!;
        Assert.True(r.Broke);
        Assert.Equal(4 * Dt, r.BrokeAt!.Value, 6);   // the fourth tick
        Assert.Equal(11.15, r.MaxTension, 6);
        Assert.True(facts.AnyRopeBroke);
        Assert.Equal("The rope r broke after 0.03 s: it holds 7.5 N and the pull reached 11.2 N.", Assert.Single(TestReport.RopeSentences(facts)));
    }

    [Fact]
    public void ARopeThatWasTautAndEndedLooseWentSlackOneThatNeverPulledDidNot()
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double>(), NoTanks);
        for (int i = 0; i < 480; i++)
            rec.Tick(Tick(ropes: new Dictionary<string, RopeSample>
            {
                ["pulled"] = i < 100 ? new(20, 100, 0.001, false, false) : new(0, 100, -0.3, false, false),
                ["idle"] = new(0, 100, -0.3, false, false),
                ["hangs"] = new(26.5, 47, 0.0005, false, false),
            }));
        var facts = rec.Facts();
        Assert.True(facts.RopeOf("pulled")!.WentSlack);
        Assert.False(facts.RopeOf("idle")!.WentSlack);
        Assert.False(facts.RopeOf("hangs")!.WentSlack);
        Assert.Equal("The rope pulled went slack: nothing is pulling on it any more.", Assert.Single(TestReport.RopeSentences(facts)));
    }

    [Fact]
    public void ADataLineHasTheNumbersAScriptReads()
    {
        var rec = new TestRecorder();
        rec.Begin(new Dictionary<string, double> { ["b"] = 0.05 }, new Dictionary<string, double> { ["t"] = 0.002 });
        rec.Tick(Tick(new Dictionary<string, BodySample> { ["b"] = new(0.6, 0, 0, 0.5) }, new Dictionary<string, double> { ["t"] = 0 }));
        string line = TestReport.DataLine(rec.Facts());
        Assert.Contains("rise b=0.5500 peak=0.5500", line);
        Assert.Contains("turns b=0.0796", line);
        Assert.Contains("water t=-2.000 L", line);
    }
}
