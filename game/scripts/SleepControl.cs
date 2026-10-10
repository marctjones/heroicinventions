using System.Diagnostics;
using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// "Sleep until…" (issue #59): the player names a condition (or picks one the machine file offers), sees the
/// wake time estimated from the machine's present rates, and the simulation runs ahead as fast as the CPU allows,
/// with no rendering of the steps in between, until it is met, then goes back to real time with the scene showing
/// the result. It takes the machine's own fixed step, never a larger one, so it leaves what watching would have shown.
/// A sleep takes one of two forms (issue #207). When nothing in the scene is a rigid body coupled to the simulation
/// (water filling, fires burning down, grain running out) the Jolt side is paused and the simulation alone runs ahead, as fast
/// as the CPU allows. When a machine is Jolt-driven (a geared windmill whose sails and train are bodies), pausing the engine
/// would stop it charging and dump its flywheel on waking, so the sleep instead lets the game's own loop run at speed: every
/// tick is the tick watching would take (the same step, 1/120 s of sim time), only the redrawing is left out, so the result is
/// the same as watching, at the engine's pace (measured in the crater world, about 600 steps a second, 5 sim-seconds per second: 600 s took 120 s,
/// so a night's 36,000 s would take two hours).
/// The sleep takes this form by default only while its estimate is within <see cref="LiveLimit"/>; a longer sleep can be told to
/// keep the machines turning, and a shorter one to pause them (the "Keep machines turning" box, HEROIC_SLEEP_PHYSICS).
/// It stops at a safety limit, and wakes early for any event the player marked.
/// A paused sleep charges (owner's ruling, 2026-10-09): a generator on a rigid body, which the paused engine leaves still, is held at its
/// last settled power (Generator.SettledPower: its mean over its last ten steady seconds of charging), or, with none, at an estimate worked
/// from the prime mover and the train (Generator.EstimatePower, marked "estimated"), and charges its bank at that rate,
/// by the bank's own rules, through the sleep. That is an approximation, said in the panel and the log: the wind's changes over the sleep
/// are not followed. A generator the sim turns itself keeps its real power; a live sleep and watching are unchanged.
/// </summary>
public partial class SleepControl : VBoxContainer
{
    private readonly Func<IReadOnlyList<MachineView>> _views;
    private readonly Func<MachineView?> _focus;
    private readonly Action<bool> _setRunning;
    private readonly Action<string> _say;
    private OptionButton _preset = null!, _op = null!;
    private LineEdit _field = null!, _value = null!, _limit = null!, _event = null!;
    private CheckBox _any = null!;
    private Label _prediction = null!, _progressText = null!, _heldNote = null!;
    private ProgressBar _bar = null!;
    private Button _go = null!, _cancel = null!;
    private VBoxContainer _body = null!;
    private List<WakeSpec> _presets = [];
    private SleepSession? _session;
    private MachineView? _sleeper;
    private double _predicted = double.NaN;
    private CheckBox _physics = null!;
    private bool _live, _physicsChosen;
    private int _liveSteps;
    private double _speedBefore = 1;

    /// <summary>The longest sleep, in sim seconds, that lets the physics engine run by default: about 25 minutes of CPU at the engine's pace in the crater world.</summary>
    public const double LiveLimit = 7200;
    /// <summary>The speed a live sleep asks of the engine: ticks are taken as fast as the CPU gives them.</summary>
    public const double LiveSpeed = 200;

    /// <summary>Set by the game: the engine's speed multiplier, read and set, which a live sleep raises and puts back.</summary>
    public Func<double> GetSpeed { get; set; } = () => 1;
    public Action<double> SetSpeed { get; set; } = _ => { };
    /// <summary>True while the sleep is letting the physics engine run (the game steps the scene as ever and calls <see cref="Observe"/> after each step).</summary>
    public bool Live => _live && Active;
    /// <summary>The sleep now ending keeps the engine running (read in <see cref="Woke"/>, before it is cleared).</summary>
    public bool EndingLive => _live;

    public SleepControl(Func<IReadOnlyList<MachineView>> views, Func<MachineView?> focus, Action<bool> setRunning, Action<string> say)
    {
        _views = views; _focus = focus; _setRunning = setRunning; _say = say;
    }

    /// <summary>The sleep in progress, as a save records it (issue #67), or null when none is; <paramref name="label"/> names the machine it is on.</summary>
    public SavedSleep? Saved(string label) =>
        _session is { Done: false } s ? new SavedSleep(label, s.Plan, s.StartedAt, s.Predicted) : null;

    /// <summary>Takes up a sleep a save recorded, on the machine it was on: it knows how long it has slept and the estimate it set out with.</summary>
    public void Resume(MachineView view, SavedSleep saved)
    {
        _sleeper = view;
        _predicted = saved.Predicted ?? double.NaN;
        _session = new SleepSession(view.Runtime, saved.Plan, 1.0 / 120, saved.Predicted, saved.StartedAt);
        _setRunning(false);
        HoldGenerators();                        // a resumed sleep is a paused one: the generators charge at their steady rate again
        _go.Disabled = true; _cancel.Disabled = false; _bar.Visible = true;
        _body.Visible = true;
        if (_session.Done) Finish();
    }

    /// <summary>Raised every 60 steps (half a second of the machine's time) while a sleep runs ahead, so the goals (issue #68) see a night or a sol as it passes, not a single sample of its end.</summary>
    public event Action? Stepped;

    /// <summary>Called when a sleep ends, so the game can save on waking (issue #67).</summary>
    public event Action? Woke;

    /// <summary>Called when a sleep ends for any reason, woken or stopped (#207): the traces' clocks catch up with the machines'.</summary>
    public event Action? Ended;

    /// <summary>True while a sleep is in progress: the game should not step the simulation itself.</summary>
    public bool Active => _session is { Done: false };

    public override void _Ready()
    {
        var toggle = new Button { Text = "Sleep until…", TooltipText = "Run ahead, as fast as the computer can, until something happens" };
        toggle.Pressed += () => { _body.Visible = !_body.Visible; if (_body.Visible) Refresh(); };
        AddChild(toggle);
        _body = new VBoxContainer { Visible = false };
        AddChild(_body);

        _body.AddChild(new Label { Text = "Wake when" });
        _preset = new OptionButton { TooltipText = "Conditions this machine's file defines" };
        _preset.ItemSelected += index => PickPreset((int)index);
        _body.AddChild(_preset);
        var row = new HBoxContainer();
        _field = new LineEdit { PlaceholderText = "target.field", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "any readable field, e.g. cistern.water" };
        _op = new OptionButton();
        _op.AddItem("≥"); _op.AddItem("≤");
        _value = new LineEdit { PlaceholderText = "value", CustomMinimumSize = new Vector2(60, 0) };
        row.AddChild(_field); row.AddChild(_op); row.AddChild(_value);
        _body.AddChild(row);
        var limitRow = new HBoxContainer();
        limitRow.AddChild(new Label { Text = "safety limit (s)" });
        _limit = new LineEdit { Text = "3600", CustomMinimumSize = new Vector2(70, 0) };
        limitRow.AddChild(_limit);
        _any = new CheckBox { Text = "any (or)", TooltipText = "for a preset with several terms: wake at the first rather than when all are met" };
        limitRow.AddChild(_any);
        _body.AddChild(limitRow);
        _physics = new CheckBox { Text = "Keep machines turning", TooltipText = "Let the physics engine run through the sleep, so a windmill or geared train keeps working; slower, a tick at a time. On by default for sleeps under two hours" };
        _physics.Toggled += _ => _physicsChosen = true;
        _body.AddChild(_physics);
        _event = new LineEdit { PlaceholderText = "wake early if  target.field ≥ value" };
        _body.AddChild(_event);
        var estimate = new Button { Text = "Estimate wake time" };
        estimate.Pressed += Estimate;
        _body.AddChild(estimate);
        _prediction = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) };
        _body.AddChild(_prediction);
        var buttons = new HBoxContainer();
        _go = new Button { Text = "Sleep" };
        _go.Pressed += Start;
        _cancel = new Button { Text = "Stop", Disabled = true };
        _cancel.Pressed += Cancel;
        buttons.AddChild(_go); buttons.AddChild(_cancel);
        _body.AddChild(buttons);
        _bar = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8), Visible = false };
        _body.AddChild(_bar);
        _progressText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) };
        _body.AddChild(_progressText);
        _heldNote = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0), Visible = false,
                                Modulate = new Color(1f, 0.85f, 0.55f) };
        _body.AddChild(_heldNote);
    }

    /// <summary>
    /// A sleep that pauses the engine begins: every machine's generators on rigid bodies are held at their last settled power (see the class
    /// notes), and the panel and the log say so, generator by generator.
    /// </summary>
    private void HoldGenerators()
    {
        var lines = new List<string>();
        foreach (var v in _views())
            foreach (var (id, gen, watts) in v.Runtime.HoldGenerators())
                lines.Add(!gen.HeldEstimated
                    ? $"{id}: {watts:0.0} W into {gen.Bank?.Name} (steady {gen.SettledAgo:0} s before the sleep)"
                    : watts > 0
                    ? $"{id}: {watts:0.0} W into {gen.Bank?.Name} (estimated: no steady rate yet, worked from the {gen.DrivenBy} and the train)"
                    : $"{id}: no steady rate yet and none estimated (nothing turns it past its cut-in): charges nothing");
        if (lines.Count == 0) { _heldNote.Visible = false; return; }
        const string Head = "Machines paused: generators charge at their last steady rate (approximate: the wind's changes overnight are not followed)";
        _heldNote.Text = Head + "\n" + string.Join("\n", lines);
        _heldNote.Visible = true;
        GD.Print($"[sleep] {Head}: {string.Join("; ", lines)}");
    }

    private void ReleaseGenerators()
    {
        foreach (var v in _views()) v.Runtime.ReleaseGenerators();
        _heldNote.Visible = false;
    }

    /// <summary>Re-reads the focused machine's wake presets: call when the machine changes.</summary>
    public void Refresh()
    {
        if (_preset is null) return;
        _preset.Clear();
        _physicsChosen = false;
        _presets = _focus()?.Runtime.Wakes.Values.ToList() ?? [];
        _preset.AddItem("(a condition of your own)");
        foreach (var w in _presets) _preset.AddItem($"{w.Id}: {w.Describe()}");
        _prediction.Text = "";
        _progressText.Text = "";
    }

    private void PickPreset(int index)
    {
        if (index <= 0 || index > _presets.Count) return;
        var w = _presets[index - 1];
        if (w.Terms.Count > 0)
        {
            _field.Text = w.Terms[0].Path; _op.Select(w.Terms[0].Above ? 0 : 1); _value.Text = w.Terms[0].Value.ToString("0.###");
        }
        _limit.Text = w.Limit.ToString("0.###");
        _any.ButtonPressed = !w.All;
        _event.Text = w.Events.Count > 0 ? $"{w.Events[0].Path} {(w.Events[0].Above ? "≥" : "≤")} {w.Events[0].Value:0.###}" : "";
        Estimate();
    }

    private static bool TryTerm(string path, bool above, string value, out WakeTerm term)
    {
        term = null!;
        var parts = path.Trim().Split('.', 2);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) return false;
        if (!double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) return false;
        term = new WakeTerm(parts[0], parts[1], above, v);
        return true;
    }

    /// <summary>The condition on screen: the chosen preset's terms, or the one typed here.</summary>
    private WakeSpec? Plan(out string problem)
    {
        problem = "";
        double limit = double.TryParse(_limit.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double l) && l > 0 ? l : 3600;
        var events = new List<WakeTerm>();
        var e = _event.Text.Replace("≥", " above ").Replace("≤", " below ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (e.Length == 3 && TryTerm(e[0], e[1] == "above", e[2], out var ev)) events.Add(ev);
        int p = _preset.Selected;
        if (p > 0 && p <= _presets.Count && _field.Text == _presets[p - 1].Terms.FirstOrDefault()?.Path)
        {
            var w = _presets[p - 1];
            return w with { All = !_any.ButtonPressed, Limit = limit, Events = events.Count > 0 ? events : w.Events };
        }
        if (!TryTerm(_field.Text, _op.Selected == 0, _value.Text, out var term))
        {
            problem = "Name a field as target.field and a number to wait for.";
            return null;
        }
        return new WakeSpec("sleep", [term], true, limit, events);
    }

    private void Estimate()
    {
        var view = _focus();
        if (view is null) { _prediction.Text = "Pick a machine first."; return; }
        var plan = Plan(out string problem);
        if (plan is null) { _prediction.Text = problem; return; }
        try
        {
            var p = SleepPlanner.Predict(view.Runtime, plan);
            _predicted = p.Seconds ?? double.NaN;
            _prediction.Text = $"Wake time: {p.Note}" + (p.Seconds is { } s ? $" ({Clock(s)})" : "");
            bool driven = _views().Any(v => v.JoltDriven);
            _physics.Disabled = !driven;
            if (!_physicsChosen) _physics.SetPressedNoSignal(driven && (p.Seconds ?? plan.Limit) <= LiveLimit);
        }
        catch (MachineFormatException ex) { _prediction.Text = ex.Message; _predicted = double.NaN; }
    }

    private void Start()
    {
        var view = _focus();
        if (view is null) { _prediction.Text = "Pick a machine first."; return; }
        var plan = Plan(out string problem);
        if (plan is null) { _prediction.Text = problem; return; }
        if (!_physicsChosen) Estimate();      // the box shows what the sleep's length asks for, until the player has chosen
        Begin(view, plan, _physics.ButtonPressed);
    }

    private void Begin(MachineView view, WakeSpec plan, bool? physics)
    {
        try
        {
            var p = SleepPlanner.Predict(view.Runtime, plan);
            _predicted = p.Seconds ?? double.NaN;
            _prediction.Text = $"Wake time: {p.Note}" + (p.Seconds is { } secs ? $" ({Clock(secs)})" : "");
            _session = new SleepSession(view.Runtime, plan, 1.0 / 120, p.Seconds);
        }
        catch (MachineFormatException ex) { _prediction.Text = ex.Message; return; }
        _sleeper = view;
        _go.Disabled = true; _cancel.Disabled = false; _bar.Visible = true;
        _live = !_session.Done && physics == true && _views().Any(v => v.JoltDriven);
        _liveSteps = 0;
        if (_live)
        {
            // the game's own stepping goes on, at speed, with the scene left undrawn until it wakes
            _setRunning(true);                   // a paused game runs for a live sleep: nothing else would step it
            _speedBefore = GetSpeed();
            SetSpeed(LiveSpeed);
            MachineView.Hurrying = true;
            GD.Print($"[sleep] keeping the machines turning (the physics engine at {LiveSpeed:0}x) until {plan.Describe()}");
        }
        else
        {
            _setRunning(false);                  // the game's own stepping stops; Advance takes over
            if (!_session.Done) HoldGenerators(); // and the generators the engine turned charge at their last steady rate
        }
        if (_session.Done) Finish();
    }

    /// <summary>
    /// Starts a named wake headlessly (HEROIC_SLEEP=wake-id, or the script's "sleep wake-id"): for scripted checks of the game's own path.
    /// The wake is looked for on the focused machine, then on every other in the scene. <paramref name="physics"/>: "live" or "paused"
    /// to choose the form of the sleep, else it is chosen as the panel would (HEROIC_SLEEP_PHYSICS=1 or 0 does the same from the environment).
    /// Returns false, having said why, when no machine has the wake.
    /// </summary>
    public bool StartNamed(string id, string? physics = null)
    {
        physics ??= OS.GetEnvironment("HEROIC_SLEEP_PHYSICS") switch { "1" => "live", "0" => "paused", _ => null };
        var view = new[] { _focus() }.Concat(_views()).FirstOrDefault(v => v is not null && v.Runtime.Wakes.ContainsKey(id));
        if (view is null) { GD.PrintErr($"[sleep] no machine has a wake called {id}"); return false; }
        return StartPlan(view, view.Runtime.Wakes[id], physics);
    }

    /// <summary>
    /// Starts a sleep on a condition of the script's own ("sleep until target.field above 5"): on the first machine, the focused one before the
    /// rest, that has the target. Returns false, having said why, when none does.
    /// </summary>
    public bool StartCondition(string path, bool above, double value, double limit, string? physics = null)
    {
        physics ??= OS.GetEnvironment("HEROIC_SLEEP_PHYSICS") switch { "1" => "live", "0" => "paused", _ => null };
        var parts = path.Split('.', 2);
        if (parts.Length != 2) { GD.PrintErr($"[sleep] name the field as target.field, not {path}"); return false; }
        var view = new[] { _focus() }.Concat(_views()).FirstOrDefault(v =>
        {
            if (v is null) return false;
            try { v.Runtime.GetField(parts[0], parts[1]); return true; } catch (Exception) { return false; }
        });
        if (view is null) { GD.PrintErr($"[sleep] no machine has a field called {path}"); return false; }
        return StartPlan(view, new WakeSpec("sleep", [new WakeTerm(parts[0], parts[1], above, value)], true, limit, []), physics);
    }

    private bool StartPlan(MachineView view, WakeSpec plan, string? physics)
    {
        double estimate = double.NaN;
        try { estimate = SleepPlanner.Predict(view.Runtime, plan).Seconds ?? double.NaN; } catch (MachineFormatException) { }
        bool live = physics switch { "live" => true, "paused" => false, _ => _views().Any(v => v.JoltDriven) && (double.IsNaN(estimate) ? plan.Limit : estimate) <= LiveLimit };
        Refresh();
        Begin(view, plan, live);
        return true;
    }

    /// <summary>After each step of a live sleep: counts it, and wakes if the condition is met. The game calls it once per physics tick while <see cref="Live"/>.</summary>
    public void Observe()
    {
        if (_session is null || !_live) return;
        _session.Observe();
        if (++_liveSteps % 60 == 0)
        {
            Stepped?.Invoke();
            _bar.Value = _session.Progress;
            _progressText.Text = $"Sleeping… {Clock(_session.Elapsed)} slept" + (double.IsNaN(_predicted) ? "" : $" of about {Clock(_predicted)}");
        }
        if (_session.Done) Finish();
    }

    /// <summary>Stops a sleep in progress where it is (the Stop button, and Pause while the engine runs through it).</summary>
    public void Cancel()
    {
        if (_session is null) return;
        _progressText.Text = $"Stopped after {Clock(_session.Elapsed)}.";
        _session = null;
        End();
    }

    /// <summary>Runs the sleep on for a slice of real time, stepping every machine in the scene, then shows the result. Call every frame while <see cref="Active"/>.</summary>
    public void Advance(double budgetMs = 10)
    {
        if (_session is null || _sleeper is null) return;
        var sw = Stopwatch.StartNew();
        var others = _views().Where(v => v != _sleeper).ToList();
        while (!_session.Done && sw.Elapsed.TotalMilliseconds < budgetMs)
        {
            for (int i = 0; i < 60 && !_session.Done; i++)
            {
                foreach (var v in others) v.Runtime.Step(_session.Dt);     // the rest of a world sleeps along
                _session.Run(1);
            }
            Stepped?.Invoke();
        }
        _sleeper.ShowState();
        foreach (var v in others) v.ShowState();
        _bar.Value = _session.Progress;
        _progressText.Text = $"Sleeping… {Clock(_session.Elapsed)} slept" + (double.IsNaN(_predicted) ? "" : $" of about {Clock(_predicted)}");
        if (_session.Done) Finish();
    }

    private void Finish()
    {
        var result = _session!.Result!;
        _progressText.Text = result.ToString();
        _say($"Sleep over: {result}");
        Woke?.Invoke();
        GD.Print($"[sleep] {result}");
        _session = null;
        End();
    }

    private void End()
    {
        _go.Disabled = false; _cancel.Disabled = true; _bar.Visible = false;
        if (_live) { SetSpeed(_speedBefore); MachineView.Hurrying = false; _live = false; }
        else ReleaseGenerators();
        foreach (var v in _views()) v.ShowState();
        _sleeper = null;
        Ended?.Invoke();
        _setRunning(true);                       // back to real time, showing what the sleep left
    }

    private static string Clock(double seconds) =>
        seconds < 90 ? $"{seconds:0.#} s" : seconds < 5400 ? $"{seconds / 60:0.#} min" : seconds < 172800 ? $"{seconds / 3600:0.#} h" : $"{seconds / 86400:0.#} days";
}
