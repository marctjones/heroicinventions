using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #158: a test that stands in for a person says so in the operator-log form, <c>(at t (part field value))</c>, and
/// this runs it the way SimHost's ApplyDue and the game's timed queue do: settings due at the start land before the first
/// step, then after every step each setting whose time has come lands, in the order written. So "step, set a field, step
/// again" is a list of timed actions, not a hand-written gap in a loop. Scenario settings (ambient, gravity, clock-rate)
/// are not operator actions: they are set once, before the run, and a test names them as such.
/// Time is the run's own clock, counted by steps (start + n * dt) so a thousand steps do not drift; a test that is about the
/// stepping itself (step sizes, accumulation, the clock) keeps its own loop.
/// </summary>
internal sealed class OperatorRun
{
    private readonly List<OperatorAction> _pending;
    private readonly Action<double> _step;
    private readonly Action<OperatorAction> _apply;

    /// <summary>Sim-seconds run so far.</summary>
    public double Time { get; private set; }

    /// <summary>Every action applied so far, in the order it landed.</summary>
    public List<OperatorAction> Applied { get; } = [];

    /// <summary>A run of anything with a step: <paramref name="apply"/> says what each action does to it.</summary>
    public OperatorRun(IEnumerable<OperatorAction> actions, Action<double> step, Action<OperatorAction> apply)
    {
        _pending = actions.OrderBy(a => a.At).ToList();     // stable: equal times keep the order written
        _step = step;
        _apply = apply;
        ApplyDue();
    }

    /// <summary>A run of a machine runtime, with the actions as log text or a list.</summary>
    public static OperatorRun Of(MachineRuntime runtime, IEnumerable<OperatorAction> actions) =>
        new(actions, runtime.Step, a => runtime.SetField(a.Target, a.Field, a.Value));

    public static OperatorRun Of(MachineRuntime runtime, string log) => Of(runtime, OperatorLog.Parse(log));

    /// <summary>Steps of <paramref name="dt"/> for <paramref name="seconds"/> (a whole number of steps, rounded as tests round them); <paramref name="each"/> sees the time after every step and after its actions.</summary>
    public void Run(double seconds, double dt, Action<double>? each = null)
    {
        double start = Time;
        int n = (int)Math.Round(seconds / dt);
        for (int i = 1; i <= n; i++)
        {
            _step(dt);
            Time = start + i * dt;
            ApplyDue();
            each?.Invoke(Time);
        }
    }

    /// <summary>Actions still waiting for their time.</summary>
    public int Pending => _pending.Count;

    private void ApplyDue()
    {
        while (_pending.Count > 0 && _pending[0].At <= Time + 1e-9)
        {
            var a = _pending[0];
            _pending.RemoveAt(0);
            _apply(a);
            Applied.Add(a);
        }
    }
}
