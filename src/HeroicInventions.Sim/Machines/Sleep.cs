namespace HeroicInventions.Sim.Machines;

/// <summary>One comparison on a readable field: <c>target.field</c> at or above (or at or below) a value.</summary>
public sealed record WakeTerm(string Target, string Field, bool Above, double Value)
{
    public string Path => $"{Target}.{Field}";
    public bool Met(double x) => Above ? x >= Value : x <= Value;
    public override string ToString() => $"{Path} {(Above ? "≥" : "≤")} {Value:0.###}";
}

/// <summary>
/// When to wake (issue #59): terms on any readable field, joined by and (<see cref="All"/>) or or, a safety
/// <see cref="Limit"/> in simulated seconds so a condition that can never be met does not sleep for ever,
/// and <see cref="Events"/>: things that wake it early whatever the condition (a pane cracking, a fire going out).
/// </summary>
public sealed record WakeSpec(string Id, IReadOnlyList<WakeTerm> Terms, bool All, double Limit, IReadOnlyList<WakeTerm> Events, SourceLocation? Location = null)
{
    public string Describe() =>
        string.Join(All ? " and " : " or ", Terms.Select(t => t.ToString())) + (Events.Count > 0 ? $" (or early if {string.Join(" or ", Events.Select(e => e.ToString()))})" : "");
}

public enum WakeReason { Condition, Event, Limit }

/// <summary>What ended a sleep: which reason, how many simulated seconds it slept, and what it was.</summary>
public sealed record SleepResult(WakeReason Reason, double Elapsed, string Detail, long Steps)
{
    public override string ToString() => Reason switch
    {
        WakeReason.Condition => $"woke after {Elapsed:0.##} s: {Detail}",
        WakeReason.Event => $"woken early after {Elapsed:0.##} s by {Detail}",
        _ => $"reached the safety limit after {Elapsed:0.##} s without {Detail}",
    };
}

/// <summary>
/// A sleep in progress: steps the simulation ahead at its own fixed step, as fast as the caller lets it (a
/// slice at a time, so a game can keep drawing), and stops at the first of an event, the condition, or the
/// limit. It takes no larger step than the machine normally takes, so what it leaves behind is what
/// watching would have shown. A condition already true at the start wakes at once.
/// </summary>
public sealed class SleepSession
{
    private readonly MachineRuntime _runtime;
    private readonly double _start;

    /// <param name="startedAt">When the sleep began, in the machine's own time, when this is a sleep resumed from a save; null for one starting now.</param>
    public SleepSession(MachineRuntime runtime, WakeSpec plan, double dt = 1.0 / 120, double? predicted = null, double? startedAt = null)
    {
        _runtime = runtime; Plan = plan; Dt = dt; Predicted = predicted;
        _start = startedAt ?? runtime.Time;
        Check(0);      // already true?
    }

    public WakeSpec Plan { get; }
    public double Dt { get; }
    /// <summary>The estimated time to wake, s, if there is one (see <see cref="SleepPlanner.Predict"/>).</summary>
    public double? Predicted { get; }
    /// <summary>When this sleep began, on the machine's own clock: saved with the world so a sleep can be resumed.</summary>
    public double StartedAt => _start;
    public long Steps { get; private set; }
    public double Elapsed => _runtime.Time - _start;
    public bool Done => Result is not null;
    public SleepResult? Result { get; private set; }

    /// <summary>How far through it is, 0 to 1: against the predicted wake time when there is one, else the limit.</summary>
    public double Progress => Done ? 1 : Math.Clamp(Elapsed / (Predicted is { } p && p > 0 ? Math.Min(p, Plan.Limit) : Plan.Limit), 0, 1);

    /// <summary>Steps up to <paramref name="maxSteps"/> more, stopping at once when it wakes. Returns how many it took.</summary>
    public int Run(int maxSteps)
    {
        int taken = 0;
        while (!Done && taken < maxSteps)
        {
            _runtime.Step(Dt);
            Steps++; taken++;
            Check(Steps);
        }
        return taken;
    }

    /// <summary>
    /// Checks the wake after the caller has stepped the machine itself (issue #207): a sleep that lets the physics engine run
    /// takes its steps from the game's own loop, one tick at a time, so the sim is stepped exactly as watching steps it and the
    /// session only counts the step and asks whether it is time to wake.
    /// </summary>
    public void Observe()
    {
        if (Done) return;
        Steps++;
        Check(Steps);
    }

    private void Check(long steps)
    {
        foreach (var e in Plan.Events)
            if (e.Met(_runtime.GetField(e.Target, e.Field))) { Result = new SleepResult(WakeReason.Event, Elapsed, e.ToString(), steps); return; }
        bool met = Plan.All ? Plan.Terms.All(Reached) : Plan.Terms.Any(Reached);
        if (met && Plan.Terms.Count > 0) { Result = new SleepResult(WakeReason.Condition, Elapsed, Plan.Describe(), steps); return; }
        if (Elapsed >= Plan.Limit) Result = new SleepResult(WakeReason.Limit, Elapsed, Plan.Describe(), steps);
    }

    private bool Reached(WakeTerm t) => t.Met(_runtime.GetField(t.Target, t.Field));

    /// <summary>Sleeps a whole plan through without pausing: for tests, the headless host and the live link.</summary>
    public static SleepResult FastForward(MachineRuntime runtime, WakeSpec plan, double dt = 1.0 / 120)
    {
        var s = new SleepSession(runtime, plan, dt);
        while (!s.Done) s.Run(100_000);
        return s.Result!;
    }
}

/// <summary>An estimate of when a sleep will end, made before it starts.</summary>
public sealed record WakePrediction(double? Seconds, string Note);

public static class SleepPlanner
{
    /// <summary>
    /// Estimates, without disturbing the machine, when the condition will be met: a scratch copy is forked from
    /// its state, run <paramref name="probe"/> seconds, and each term's rate of change taken from that; a term
    /// moving towards its value is met in (value − now)/rate. and takes the latest term, or the earliest.
    /// A term moving away, or not at all, is never met at the current rate, and if every one that has to be is
    /// like that the answer is <c>null</c>: "never, at the current rate".
    /// </summary>
    public static WakePrediction Predict(MachineRuntime current, WakeSpec plan, double probe = 2.0)
    {
        if (plan.Terms.Count == 0) return new WakePrediction(null, "nothing to wait for");
        var scratch = current.Fork();
        var before = plan.Terms.Select(t => scratch.GetField(t.Target, t.Field)).ToList();
        double t0 = scratch.Time;
        const double dt = 1.0 / 60;
        int steps = (int)Math.Round(probe / dt);
        for (int i = 0; i < steps; i++) scratch.Step(dt);
        double span = scratch.Time - t0;
        var times = new List<double?>();
        for (int i = 0; i < plan.Terms.Count; i++)
        {
            var t = plan.Terms[i];
            double now = before[i], rate = (scratch.GetField(t.Target, t.Field) - now) / span;
            if (t.Met(now)) times.Add(0);
            else if (t.Above ? rate > 1e-12 : rate < -1e-12) times.Add((t.Value - now) / rate);
            else times.Add(null);
        }
        double? seconds = plan.All
            ? (times.Any(x => x is null) ? null : times.Max())
            : times.Where(x => x is not null).DefaultIfEmpty(null).Min();
        if (seconds is null) return new WakePrediction(null, "never, at the current rate");
        if (seconds > plan.Limit) return new WakePrediction(seconds, $"{seconds:0.#} s at the current rate: beyond the {plan.Limit:0.#} s limit");
        return new WakePrediction(seconds, $"about {seconds:0.#} s at the current rate");
    }
}
