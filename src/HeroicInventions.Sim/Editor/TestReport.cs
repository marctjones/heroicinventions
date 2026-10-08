using System.Globalization;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// What a body did in one physics tick of a "Test it" run (#165, #182):
/// where it is, how fast it moves and spins, and, for a wheel on an axle,
/// how far it turned this tick (rad, signed, about its axle, not wrapped).
/// </summary>
public readonly record struct BodySample(double Y, double LinearSpeed, double AngularSpeed, double TurnedRad, double X = 0, double Z = 0);

/// <summary>A rope in one tick: the pull it carried, how much it can carry, how far its path ran past its length (negative: slack), and whether it has gone.</summary>
public readonly record struct RopeSample(double Tension, double Strength, double Stretch, bool Broken, bool Released);

/// <summary>Everything the recorder reads off a running design in one tick; ids are the editor's part, tank and rope ids.</summary>
public sealed record TestTick(double Dt,
                              IReadOnlyDictionary<string, BodySample> Bodies,
                              IReadOnlyDictionary<string, double> TankVolumes,
                              IReadOnlyDictionary<string, RopeSample> Ropes);

/// <summary>
/// A body that ended higher (positive) or lower than it began, and the highest and lowest it got, metres; how far it ended from
/// where it began across the ground (<see cref="Across"/>, m) and the fastest it ever went (<see cref="TopSpeed"/>, m/s).
/// </summary>
public sealed record PartRise(string Id, double Metres, double Peak, double Low, double Across = 0, double TopSpeed = 0);

/// <summary>A wheel's turning over the run: the net turns from where it began (signed, + the way the axle's own direction turns it), the most it was ever away from there, and its speed at the end.</summary>
public sealed record WheelTurn(string Id, double Turns, double PeakTurns, double EndRpm);

/// <summary>A tank's water at the start and the end, litres.</summary>
public sealed record TankChange(string Id, double StartLitres, double EndLitres)
{
    public double Litres => EndLitres - StartLitres;
}

/// <summary>Water that ran from one tank to another: only said when exactly one tank lost water and exactly one gained it.</summary>
public sealed record WaterMove(string From, string To, double Litres);

/// <summary>A rope over the run: the most it carried, what it could carry, and whether it broke, let go, or ended slack after having been taut.</summary>
public sealed record RopeFact(string Id, double MaxTension, double Strength, bool Broke, double? BrokeAt, bool Released, bool WentSlack);

/// <summary>
/// The data a test run leaves (#182): what the lessons check, and what the
/// sentences are made from. The sentences are in <see cref="TestReport"/>.
/// </summary>
public sealed record TestFacts(double Seconds, string EndedBy,
                               IReadOnlyList<PartRise> Rises, IReadOnlyList<WheelTurn> Turns,
                               IReadOnlyList<TankChange> Tanks, IReadOnlyList<WaterMove> Moves,
                               IReadOnlyList<RopeFact> Ropes)
{
    public static readonly TestFacts Empty = new(0, "stopped", [], [], [], [], []);

    public PartRise? RiseOf(string part) => Rises.FirstOrDefault(r => r.Id == part);
    public WheelTurn? TurnOf(string wheel) => Turns.FirstOrDefault(t => t.Id == wheel);
    public TankChange? TankOf(string tank) => Tanks.FirstOrDefault(t => t.Id == tank);
    public RopeFact? RopeOf(string rope) => Ropes.FirstOrDefault(r => r.Id == rope);

    /// <summary>Litres that ran from one tank into another (what the first lost, which is what the second gained when there are only the two); 0 when they did not.</summary>
    public double LitresMoved(string from, string to) => Moves.FirstOrDefault(m => m.From == from && m.To == to)?.Litres ?? 0;

    /// <summary>All the water the tanks gained, and all they lost, litres.</summary>
    public double LitresGained => Tanks.Where(t => t.Litres > 0).Sum(t => t.Litres);
    public double LitresLost => -Tanks.Where(t => t.Litres < 0).Sum(t => t.Litres);

    public bool AnyRopeBroke => Ropes.Any(r => r.Broke);
}

/// <summary>
/// Watches a "Test it" run tick by tick (the game feeds it <see cref="TestTick"/>s)
/// and decides when it has seen enough: the run lasts at least
/// <see cref="MinSeconds"/> (the 4 s the first lessons' sentences were made
/// for), then ends once everything has been quiet for
/// <see cref="QuietSeconds"/> in a row, or at <see cref="MaxSeconds"/> whatever
/// is still going on. Quiet means: every body slower than
/// <see cref="LinearEps"/> and <see cref="AngularEps"/>, every tank's water
/// changing by less than <see cref="WaterEps"/> m³/s, no rope breaking or
/// letting go. A tank draining through a hole is so quiet when its last
/// 20 mL go (Torricelli's flow falls as the square root of what is left),
/// a rolling or swinging thing is not until it stops.
/// </summary>
public sealed class TestRecorder
{
    public const double MinSeconds = 4;
    public const double DefaultMaxSeconds = 30;
    public const double QuietSeconds = 1.0;
    public const double LinearEps = 0.02;     // m/s
    public const double AngularEps = 0.05;    // rad/s, about 3 degrees a second
    public const double WaterEps = 2e-5;      // m³/s, 0.02 L/s

    private sealed class Track
    {
        public double StartY, EndY, MaxY, MinY;
        public double StartX, StartZ, EndX, EndZ, TopSpeed;
        public bool Seen;
        public double Turned, PeakTurned, EndRate;   // rad
    }

    private readonly Dictionary<string, Track> _bodies = [];
    private readonly Dictionary<string, double> _tankStart = [];
    private readonly Dictionary<string, double> _tankPrev = [];
    private readonly Dictionary<string, double> _tankEnd = [];
    private sealed class RopeTrack
    {
        public double Max, Strength, Stretch;
        public bool Broke, Released, EverTaut;
        public double? BrokeAt;
    }
    private readonly Dictionary<string, RopeTrack> _ropes = [];

    private double _quiet;

    public TestRecorder(double maxSeconds = DefaultMaxSeconds) { MaxSeconds = maxSeconds; }

    public double MaxSeconds { get; }
    public double Time { get; private set; }
    public bool Quiet => _quiet >= QuietSeconds;

    /// <summary>The run has seen enough: quiet long enough after the minimum, or out of time.</summary>
    public bool Done => Time >= MaxSeconds || (Time >= MinSeconds && Quiet);

    /// <summary>"settled" when the run ended because everything stopped, "limit" when it ran out of time, "stopped" when someone ended it.</summary>
    public string EndedBy => Quiet && Time >= MinSeconds ? "settled" : Time >= MaxSeconds ? "limit" : "stopped";

    /// <summary>Takes the starting state; call before the first tick.</summary>
    public void Begin(IReadOnlyDictionary<string, double> bodyY, IReadOnlyDictionary<string, double> tankVolumes)
    {
        foreach (var (id, y) in bodyY) _bodies[id] = new Track { StartY = y, EndY = y, MaxY = y, MinY = y };
        foreach (var (id, v) in tankVolumes) { _tankStart[id] = v; _tankPrev[id] = v; _tankEnd[id] = v; }
    }

    public void Tick(TestTick tick)
    {
        Time += tick.Dt;
        bool quiet = true;
        foreach (var (id, s) in tick.Bodies)
        {
            if (!_bodies.TryGetValue(id, out var t)) _bodies[id] = t = new Track { StartY = s.Y, EndY = s.Y, MaxY = s.Y, MinY = s.Y };
            if (!t.Seen) { t.Seen = true; t.StartX = t.EndX = s.X; t.StartZ = t.EndZ = s.Z; }
            t.EndX = s.X;
            t.EndZ = s.Z;
            t.TopSpeed = Math.Max(t.TopSpeed, s.LinearSpeed);
            t.EndY = s.Y;
            t.MaxY = Math.Max(t.MaxY, s.Y);
            t.MinY = Math.Min(t.MinY, s.Y);
            t.Turned += s.TurnedRad;
            t.PeakTurned = Math.Max(t.PeakTurned, Math.Abs(t.Turned));
            t.EndRate = tick.Dt > 0 ? s.TurnedRad / tick.Dt : 0;
            if (s.LinearSpeed > LinearEps || s.AngularSpeed > AngularEps || Math.Abs(t.EndRate) > AngularEps) quiet = false;
        }
        foreach (var (id, v) in tick.TankVolumes)
        {
            if (!_tankStart.ContainsKey(id)) _tankStart[id] = _tankPrev[id] = v;
            if (tick.Dt > 0 && Math.Abs(v - _tankPrev[id]) / tick.Dt > WaterEps) quiet = false;
            _tankPrev[id] = v;
            _tankEnd[id] = v;
        }
        foreach (var (id, r) in tick.Ropes)
        {
            if (!_ropes.TryGetValue(id, out var t)) _ropes[id] = t = new RopeTrack();
            t.Max = Math.Max(t.Max, r.Tension);
            t.Strength = r.Strength;
            t.Stretch = r.Stretch;
            if (r.Tension > 1) t.EverTaut = true;
            if (r.Broken && !t.Broke) { t.Broke = true; t.BrokeAt = Time; quiet = false; }
            if (r.Released && !t.Released) { t.Released = true; quiet = false; }
        }
        _quiet = quiet ? _quiet + tick.Dt : 0;
    }

    /// <summary>What the run saw, as data.</summary>
    public TestFacts Facts()
    {
        const double litre = 1000;
        var rises = _bodies.Select(kv => new PartRise(kv.Key, kv.Value.EndY - kv.Value.StartY, kv.Value.MaxY - kv.Value.StartY, kv.Value.MinY - kv.Value.StartY,
                                                    Math.Sqrt((kv.Value.EndX - kv.Value.StartX) * (kv.Value.EndX - kv.Value.StartX) + (kv.Value.EndZ - kv.Value.StartZ) * (kv.Value.EndZ - kv.Value.StartZ)),
                                                    kv.Value.TopSpeed)).ToList();
        var turns = _bodies.Where(kv => kv.Value.PeakTurned > 0)
                           .Select(kv => new WheelTurn(kv.Key, kv.Value.Turned / (2 * Math.PI), kv.Value.PeakTurned / (2 * Math.PI), kv.Value.EndRate * 60 / (2 * Math.PI))).ToList();
        var tanks = _tankStart.Select(kv => new TankChange(kv.Key, kv.Value * litre, _tankEnd[kv.Key] * litre)).ToList();
        var ropes = _ropes.Select(kv => new RopeFact(kv.Key, kv.Value.Max, kv.Value.Strength, kv.Value.Broke, kv.Value.BrokeAt, kv.Value.Released,
                                                     !kv.Value.Broke && !kv.Value.Released && kv.Value.EverTaut && kv.Value.Stretch < TestReport.SlackBy)).ToList();
        var losers = tanks.Where(t => t.Litres <= -TestReport.WaterWorthSaying).ToList();
        var gainers = tanks.Where(t => t.Litres >= TestReport.WaterWorthSaying).ToList();
        var moves = losers.Count == 1 && gainers.Count == 1
            ? new List<WaterMove> { new(losers[0].Id, gainers[0].Id, Math.Min(-losers[0].Litres, gainers[0].Litres)) }
            : [];
        return new TestFacts(Time, EndedBy, rises, turns, tanks, moves, ropes);
    }
}

/// <summary>The sentences a test run's facts make (#182), in the plain words the first lessons use.</summary>
public static class TestReport
{
    /// <summary>A part that ended this much higher counts as having risen, metres.</summary>
    public const double RiseWorthSaying = 0.1;
    /// <summary>A wheel that turned this many turns (net or at the most) counts as having turned.</summary>
    public const double TurnWorthSaying = 0.05;
    /// <summary>A tank that gained or lost this much water, litres, counts.</summary>
    public const double WaterWorthSaying = 0.05;
    /// <summary>A rope whose path ended this much shorter than its length is slack, metres.</summary>
    public const double SlackBy = -0.01;

    private static string N(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>"The oak block rose 0.6 m." (and where it got to, if it came back down some).</summary>
    public static string? RiseSentence(string name, PartRise r)
    {
        if (r.Metres < RiseWorthSaying) return null;
        string s = $"The {name} rose {N(r.Metres, "0.0#")} m";
        if (r.Peak - r.Metres >= RiseWorthSaying) s += $" (it got up to {N(r.Peak, "0.0#")} m before settling)";
        return s + ".";
    }

    /// <summary>"The bronze pulley turned 0.88 of a turn." / "turned 3.5 times" / "went on turning at 12 rpm".</summary>
    public static string? TurnSentence(string name, WheelTurn t)
    {
        double turns = Math.Abs(t.Turns);
        if (Math.Max(turns, t.PeakTurns) < TurnWorthSaying) return null;
        string how;
        if (turns < TurnWorthSaying) how = $"swung to and fro, up to {N(t.PeakTurns, "0.##")} of a turn either way";
        else if (turns < 0.95) how = $"turned {N(turns, "0.##")} of a turn";
        else if (turns < 1.05) how = "turned once";
        else how = $"turned {N(turns, turns < 10 ? "0.0" : "0")} times";
        if (Math.Abs(t.EndRpm) >= 1) how += $" and was still turning at {N(Math.Abs(t.EndRpm), Math.Abs(t.EndRpm) < 10 ? "0.#" : "0")} rpm";
        return $"The {name} {how}.";
    }

    /// <summary>"15.0 litres ran from tank_1 into tank_2." or, when it is not just two tanks, what each tank gained or lost.</summary>
    public static IEnumerable<string> WaterSentences(TestFacts facts)
    {
        if (facts.Moves.Count > 0)
        {
            foreach (var m in facts.Moves)
                yield return $"{Litres(m.Litres)} ran from {m.From} into {m.To}.";
            yield break;
        }
        foreach (var t in facts.Tanks)
        {
            if (t.Litres >= WaterWorthSaying) yield return $"{t.Id} gained {Litres(t.Litres)}.";
            else if (t.Litres <= -WaterWorthSaying) yield return $"{t.Id} lost {Litres(-t.Litres)}.";
        }
    }

    private static string Litres(double l) => l < 0.95 ? $"{N(l * 1000, "0")} mL" : l < 10 ? $"{N(l, "0.0#")} litres" : $"{N(l, "0.0")} litres";

    /// <summary>"The rope r broke: it holds 7.5 N and the pull reached 11.2 N." / "went slack" / "let go".</summary>
    public static IEnumerable<string> RopeSentences(TestFacts facts)
    {
        foreach (var r in facts.Ropes)
        {
            if (r.Broke) yield return $"The rope {r.Id} broke after {N(r.BrokeAt ?? 0, "0.0#")} s: it holds {N(r.Strength, "0.#")} N and the pull reached {N(r.MaxTension, "0.#")} N.";
            else if (r.Released) yield return $"The rope {r.Id} let go.";
            else if (r.WentSlack) yield return $"The rope {r.Id} went slack: nothing is pulling on it any more.";
        }
    }

    /// <summary>The numbers in one line a script can read: "t=12.3 by=settled | rise b=0.55 | turns pul=0.875 | water tank_1=-14.98 | rope r max=11.2 broke=no".</summary>
    public static string DataLine(TestFacts f)
    {
        var parts = new List<string> { $"t={N(f.Seconds, "0.00")} by={f.EndedBy}" };
        parts.AddRange(f.Rises.Where(r => Math.Abs(r.Metres) >= 0.005 || r.Peak >= 0.005 || r.Across >= 0.005)
                                .Select(r => $"rise {r.Id}={N(r.Metres, "0.0000")} peak={N(r.Peak, "0.0000")} low={N(r.Low, "0.0000")} across={N(r.Across, "0.000")} top={N(r.TopSpeed, "0.000")}"));
        parts.AddRange(f.Turns.Select(t => $"turns {t.Id}={N(t.Turns, "0.0000")} peak={N(t.PeakTurns, "0.0000")} rpm={N(t.EndRpm, "0.0")}"));
        parts.AddRange(f.Tanks.Where(t => Math.Abs(t.Litres) >= 0.0005).Select(t => $"water {t.Id}={N(t.Litres, "0.000")} L"));
        parts.AddRange(f.Ropes.Select(r => $"rope {r.Id} max={N(r.MaxTension, "0.00")} strength={N(r.Strength, "0.00")} broke={(r.Broke ? "yes" : "no")} slack={(r.WentSlack ? "yes" : "no")}"));
        return string.Join(" | ", parts);
    }
}
