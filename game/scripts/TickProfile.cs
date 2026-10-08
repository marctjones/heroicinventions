using System.Diagnostics;
using Godot;

namespace HeroicInventions;

/// <summary>
/// HEROIC_TICK_PROFILE=1 (issue #188): times the ground's per-tick work in pieces, and prints each piece's
/// distribution (median, p95, max, in ms, over every tick it ran) when the scene tree exits. The budget is one
/// physics tick, 1/120 s = 8.3 ms. "period" is the wall time between two ticks' ground refreshes, so a tick
/// that ran long shows there whatever made it long; "physics" is Godot's own reading of the last physics frame.
/// </summary>
public static partial class TickProfile
{
    public static readonly bool On = OS.GetEnvironment("HEROIC_TICK_PROFILE") is "1" or "check";
    /// <summary>HEROIC_TICK_PROFILE=check also compares the ground mesh, after every reshape, with a whole rebuild (slow: the timings are then not the game's).</summary>
    public static readonly bool Check = OS.GetEnvironment("HEROIC_TICK_PROFILE") == "check";
    private static readonly Dictionary<string, List<double>> _samples = [];

    static TickProfile() { if (On) HeroicInventions.Sim.StepProfile.Sink = Add; }

    public static long Start() => On ? Stopwatch.GetTimestamp() : 0;

    public static void Stop(string name, long start)
    {
        if (On) Add(name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    public static void Add(string name, double ms)
    {
        if (!On) return;
        if (name == "scripts") _scriptsMs = ms; else if (name == "engine(jolt+sync)") Add("tick-work", ms + _scriptsMs);
        if (!_samples.TryGetValue(name, out var list)) _samples[name] = list = [];
        list.Add(ms);
    }

    private static long _first, _last, _frames, _framesAtLast, _ticks;
    private static double _scriptsMs;
    public static long ReshapeTick = -1;
    public static long Ticks => _ticks;

    /// <summary>
    /// A node that runs first (or last) of every physics tick's callbacks: the first stamps the start of the
    /// tick's scripts and closes the engine's part of the last tick (Jolt's step, the scene's physics sync,
    /// everything between the last callback and this one); the last closes the scripts' part.
    /// </summary>
    public partial class Edge : Node
    {
        private readonly bool _start;
        public Edge(bool start) { _start = start; ProcessPhysicsPriority = start ? -100000 : 100000; }
        public override void _PhysicsProcess(double delta)
        {
            long now = Stopwatch.GetTimestamp();
            if (_start)
            {
                if (_last != 0)
                {
                    double ms = Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
                    // between two ticks of one frame there is nothing but the engine's step; across frames the frame's draw and idle are in it too
                    Add(_frames == _framesAtLast ? "engine(jolt+sync)" : "engine+frame", ms);
                    if (ms > 8.333) GD.Print($"[tick] slow engine step {ms:F1} ms after tick {_ticks - 1}; last reshape at tick {ReshapeTick}");
                }
                _ticks++;
                _first = now;
            }
            else if (_first != 0)
            {
                double scripts = Stopwatch.GetElapsedTime(_first, now).TotalMilliseconds;
                Add("scripts", scripts);
                _last = now;
                _framesAtLast = _frames;
                if (scripts > 8.333) GD.Print($"[tick] slow scripts {scripts:F1} ms at tick {_ticks}");
            }
        }
        public override void _Process(double delta) { if (_start) _frames++; }
    }

    public static void Report()
    {
        if (!On) return;
        foreach (var (name, list) in _samples.OrderBy(p => p.Key))
        {
            var s = list.OrderBy(x => x).ToArray();
            double At(double q) => s[Math.Min(s.Length - 1, (int)(q * s.Length))];
            GD.Print($"[tick] {name,-18} n {s.Length,6}  median {At(0.5):F3}  p95 {At(0.95):F3}  p99 {At(0.99):F3}  max {s[^1]:F3}  over 8.33 ms: {s.Count(x => x > 8.333)}");
        }
        _samples.Clear();
    }
}
