using System.Diagnostics;
using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// "Sleep until…" (issue #59): the player names a condition (or picks one the machine file offers), sees the
/// wake time estimated from the machine's present rates, and the simulation runs ahead as fast as the CPU allows,
/// with no rendering of the steps in between, until it is met, then goes back to real time with the scene showing
/// the result. It takes the machine's own fixed step, never a larger one, so it leaves what watching would have shown.
/// The Jolt side is paused while it sleeps (rigid bodies stay where they were): this is for the slow loops the
/// simulation core runs, water filling, fires burning down, grain running out. It stops at a safety limit, and
/// wakes early for any event the player marked.
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
    private Label _prediction = null!, _progressText = null!;
    private ProgressBar _bar = null!;
    private Button _go = null!, _cancel = null!;
    private VBoxContainer _body = null!;
    private List<WakeSpec> _presets = [];
    private SleepSession? _session;
    private MachineView? _sleeper;
    private double _predicted = double.NaN;

    public SleepControl(Func<IReadOnlyList<MachineView>> views, Func<MachineView?> focus, Action<bool> setRunning, Action<string> say)
    {
        _views = views; _focus = focus; _setRunning = setRunning; _say = say;
    }

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
    }

    /// <summary>Re-reads the focused machine's wake presets: call when the machine changes.</summary>
    public void Refresh()
    {
        if (_preset is null) return;
        _preset.Clear();
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
        }
        catch (MachineFormatException ex) { _prediction.Text = ex.Message; _predicted = double.NaN; }
    }

    private void Start()
    {
        var view = _focus();
        if (view is null) { _prediction.Text = "Pick a machine first."; return; }
        var plan = Plan(out string problem);
        if (plan is null) { _prediction.Text = problem; return; }
        try
        {
            Estimate();
            _session = new SleepSession(view.Runtime, plan, 1.0 / 120, double.IsNaN(_predicted) ? null : _predicted);
        }
        catch (MachineFormatException ex) { _prediction.Text = ex.Message; return; }
        _sleeper = view;
        _setRunning(false);                      // the game's own stepping stops; Advance takes over
        _go.Disabled = true; _cancel.Disabled = false; _bar.Visible = true;
        if (_session.Done) Finish();
    }

    /// <summary>Starts a named wake headlessly (HEROIC_SLEEP=wake-id): for scripted checks of the game's own path.</summary>
    public void StartNamed(string id)
    {
        Refresh();
        int i = _presets.FindIndex(w => w.Id == id);
        if (i < 0) { GD.PrintErr($"[sleep] the machine has no wake called {id}"); return; }
        _preset.Select(i + 1);
        PickPreset(i + 1);
        Start();
    }

    private void Cancel()
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
        GD.Print($"[sleep] {result}");
        _session = null;
        End();
    }

    private void End()
    {
        _go.Disabled = false; _cancel.Disabled = true; _bar.Visible = false;
        _sleeper?.ShowState();
        _sleeper = null;
        _setRunning(true);                       // back to real time, showing what the sleep left
    }

    private static string Clock(double seconds) =>
        seconds < 90 ? $"{seconds:0.#} s" : seconds < 5400 ? $"{seconds / 60:0.#} min" : seconds < 172800 ? $"{seconds / 3600:0.#} h" : $"{seconds / 86400:0.#} days";
}
