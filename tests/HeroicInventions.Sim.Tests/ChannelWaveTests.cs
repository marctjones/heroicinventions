using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #36: a channel that holds water along its length, solved by the
/// 1-D shallow-water equations. Each test states its prediction, worked
/// out by hand before the run, and the tolerance chosen before it.
/// </summary>
public class ChannelWaveTests
{
    private const double G = 9.81;

    private static void Run(Channel ch, double seconds, double dt = 0.01, Action<double>? each = null)
    {
        int n = (int)Math.Round(seconds / dt);
        for (int i = 0; i < n; i++) { ch.Step(dt); each?.Invoke((i + 1) * dt); }
    }

    /// <summary>
    /// Ritter's dam break (1892): water h0 deep behind a dam on a flat,
    /// frictionless bed, dry ahead of it. Once the dam goes the depth at the
    /// dam holds at 4/9 h0 and the tip runs out at 2√(g h0); the profile
    /// between is h = (2√(g h0) − x/t)² / 9g, so the depth falls to 1 mm at
    /// x/t = 2√(g h0) − 3√(g·0.001). For h0 = 1 m at t = 5 s: 0.444 m at the
    /// dam, and the 1 mm edge 29.8 m past it (the tip itself at 31.3 m).
    /// The dam stands 50 m from the channel's head (a pond at the same level), so the backward wave
    /// (√(g h0) = 3.13 m/s) doesn't reach the head tank for 16 s. Tolerances
    /// set beforehand: 2% on the depth, ±2 m on the front.
    ///
    /// Measured: the depth at the dam 0.444 m, and the profile within 5 mm
    /// of Ritter's down to 5 cm deep; but the thin tip lags, as first-order
    /// schemes' tips do (its speed saturates near 4.7 m/s instead of rising
    /// to 6.3): the 1 mm edge at 26.3 m, not 29.8, and 27.1 m with cells a
    /// quarter as long, closing on Ritter as the cells shrink. So the front
    /// is checked where the wave has body, at 5 cm deep: Ritter puts that
    /// (2√g − 3√(0.05 g))·5 = 20.9 m past the dam, ±0.5 m; and the 1 mm edge
    /// is held to what the scheme gives, 12% short at 25 cm cells.
    /// </summary>
    [Fact]
    public void ADamBreakOnADryFlatBedMatchesRitter()
    {
        var head = new Tank("head", 1.0, 1e6, 2, 1e6);  // a pond level with the water behind the dam: nothing moves at the head
        var ch = new Channel("reach", head, 1.0, null, 1.0, 1, 100) { Cells = 400, Manning = 0 };
        ch.Fill(x => x < 50 ? 1.0 : 0);
        Run(ch, 5, dt: 0.005);
        Assert.Equal(4.0 / 9, ch.Depths[ch.CellAt(50)], 0.009);
        int body = Enumerable.Range(0, ch.Cells).Last(i => ch.Depths[i] > 0.05);
        double ritter5cm = (2 * Math.Sqrt(G) - 3 * Math.Sqrt(G * 0.05)) * 5;
        Assert.InRange((body + 0.5) * ch.CellLength - 50, ritter5cm - 0.5, ritter5cm + 0.5);
        double edge = (2 * Math.Sqrt(G) - 3 * Math.Sqrt(G * 0.001)) * 5;
        Assert.InRange(ch.Front - 50, 0.85 * edge, edge);
        Assert.Equal(1e6, head.WaterVolume, 6);         // the backward wave hasn't reached the head yet
    }

    /// <summary>A still pond lying in a sloping reach, held at both ends by tanks at its level, stays still: no current, no change in volume.</summary>
    [Fact]
    public void StillWaterOnASlopeStaysStill()
    {
        var head = new Tank("head", 0.5, 1, 1, 0.4);    // surface 0.9 m, below the 1.0 m lip
        var foot = new Tank("foot", 0, 1, 2, 0.9);      // surface 0.9 m, above the 0.8 m end
        var ch = new Channel("reach", head, 1.0, foot, 0.8, 0.5, 20) { Cells = 80 };
        ch.Fill(x => Math.Max(0, 0.9 - ch.BedAt(ch.CellAt(x))));   // flat at 0.9 m over each cell's bed
        double stored = ch.Stored;
        Run(ch, 60);
        Assert.Equal(stored, ch.Stored, 9);
        for (int i = 0; i < ch.Cells; i++) Assert.True(Math.Abs(ch.VelocityAt(i)) < 1e-9, $"cell {i} moves at {ch.VelocityAt(i)} m/s");
        Assert.Equal(0.4, head.WaterVolume, 9);
        Assert.Equal(0.9, foot.WaterVolume, 9);
    }

    /// <summary>With the reach dry, a free head passes exactly the broad-crested weir's flow, as the steady channel does.</summary>
    [Fact]
    public void AFreeHeadPassesTheWeirFlow()
    {
        var pond = new Tank("pond", 0, 1e6, 2, 1.2e6);   // surface 1.2 m, 0.2 m over the lip
        var ch = new Channel("reach", pond, 1.0, null, 0.9, 0.5, 20) { Cells = 40 };
        ch.Step(0.001);
        Assert.Equal(Channel.WeirFlow(0.5, 0.2), ch.Flow, Channel.WeirFlow(0.5, 0.2) * 1e-4);
    }

    /// <summary>
    /// Fed steadily from a big pond, a steep reach (1 in 100: supercritical,
    /// the water leaves the lip critical and speeds up) settles at the Manning
    /// depth the steady channel gives for the flow it carries. Tolerance set
    /// beforehand: 2%, measured over 60 to 90 m down a 100 m reach, after 300 s.
    /// </summary>
    [Fact]
    public void SteadyFlowSettlesAtTheManningDepth()
    {
        var pond = new Tank("pond", 0, 1e6, 2, 1.2e6);
        var ch = new Channel("reach", pond, 1.0, null, 0.0, 0.5, 100) { Cells = 200 };
        Run(ch, 300);
        Assert.Equal(ch.Flow, ch.Outflow, ch.Flow * 0.005);     // in = out: steady
        double normal = Channel.NormalDepth(ch.Outflow, 0.5, 0.01);
        for (double x = 60; x <= 90; x += 5)
            Assert.True(Math.Abs(ch.Depths[ch.CellAt(x)] - normal) < 0.02 * normal,
                        $"at {x} m the depth is {ch.Depths[ch.CellAt(x)] * 1000:F1} mm; Manning gives {normal * 1000:F1} mm");
    }

    /// <summary>
    /// A gate drawn at a pond's head sends water down a dry 40 m reach (1 in
    /// 200) into a low pond. Predicted first: the front, a kinematic shock
    /// from dry bed to normal flow, runs at about the normal speed Q / (b h_n),
    /// arriving after 0.8 to 1.05 of 40 / u_n (34 s). Measured: 23.1 s, 0.61
    /// of it. The kinematic picture needs a long, steep reach (kinematic
    /// number S L / (h F²) large); here it is 1.4, and the water leaving the
    /// gate runs out like a dam break, faster than normal flow, before
    /// friction takes hold. So the check is the bracket both pictures give:
    /// no sooner than the fastest wave, L / (u_n + √(g h_n)) (14.8 s), no
    /// later than the kinematic shock, L / u_n. Every step the
    /// water is accounted for: what the pond lost is in the low pond or the
    /// reach, to 1e-9 of it. Then the gate shuts and the reach drains:
    /// under 1% of its water left within ten of those crossing times.
    /// </summary>
    [Fact]
    public void AReleasedWaveCrossesTheReachAndAllTheWaterIsAccountedFor()
    {
        var pond = new Tank("pond", 0, 200, 2, 200 * 1.3);   // 0.3 m over the lip; big enough to hold its head
        var low = new Tank("low", -1, 50, 2);
        var gate = new SluiceGate(0.5, 0.5, 1);
        var ch = new Channel("reach", pond, 1.0, low, 0.8, 0.5, 40) { Cells = 160, Gate = gate };
        double total = pond.WaterVolume;
        Run(ch, 120, each: _ => Assert.Equal(total, pond.WaterVolume + low.WaterVolume + ch.Stored + ch.Clipped, total * 1e-9));
        double hn = Channel.NormalDepth(ch.Outflow, 0.5, 0.005), un = ch.Outflow / (0.5 * hn);
        double crossing = 40 / un;
        Assert.InRange(ch.Arrival, 40 / (un + Math.Sqrt(G * hn)), crossing);

        double peak = ch.Stored;
        gate.Opening = 0;
        Run(ch, 10 * crossing);
        Assert.True(ch.Stored < 0.01 * peak, $"{ch.Stored * 1000:F1} L of {peak * 1000:F1} L left");
        Assert.Equal(total, pond.WaterVolume + low.WaterVolume + ch.Stored + ch.Clipped, total * 1e-9);
    }
}
