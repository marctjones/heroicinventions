using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The operator log (issue #153): everything done to the running machine, as <c>(at t (part field value))</c> in the
/// timed-setting form of Main.Timed.cs, so one record is a console history, a save, a replay, a test and a demo.
/// <list type="bullet">
/// <item><see cref="Operate"/> is the one entry point a person's action goes through (the F key now; clicks and drags later):
/// it sets the field and logs it, and a blueprint's demo operator stops for good.</item>
/// <item>HEROIC_ACTIONS=&lt;file&gt; replays a log through the timed queue, as HEROIC_SET does for a test's settings. A replayed
/// action is logged as it is applied, so a replay's log is the recording's.</item>
/// <item>A blueprint's <c>(operator (at 400 (bleed open 1)) …)</c> plays the same way while nobody else is acting: not with
/// HEROIC_SET or HEROIC_ACTIONS set, and not once the person has operated anything.</item>
/// <item>HEROIC_ACTIONS_OUT=&lt;file&gt; and HEROIC_TEST_OUT=&lt;file&gt; write the log and its Racket test when a scripted run ends.</item>
/// </list>
/// </summary>
public partial class Main
{
    private readonly List<OperatorAction> _operatorLog = new();
    private bool _operatorTaken;
    private VBoxContainer _operatorSection = null!;
    private RichTextLabel _operatorConsole = null!;
    private bool _operatorShown;

    /// <summary>
    /// A person's action on <paramref name="view"/>: sets <c>target.field</c> to <paramref name="value"/> now and appends
    /// <c>(at t (target field value))</c> to the run's log. Anything that operates a machine by hand (clicks, drags, keys) calls this.
    /// A bad target or field throws before anything is logged.
    /// </summary>
    public void Operate(MachineView view, string target, string field, double value)
    {
        // a person's hand on a rope or a view-side drive is the view's to do (MachineView.Hooks.cs), the rest the runtime's
        if (!view.TrySetViewField(target, field, value)) view.Runtime.SetField(target, field, value);
        TakeControl();
        if (view == _current) LogOperatorAction(new OperatorAction(view.Runtime.Time, target, field, value));
    }

    /// <summary>The person has a control: any demo operator hands over to them.</summary>
    private void TakeControl()
    {
        _operatorTaken = true;
        _timedSettings.RemoveAll(s => s.Source == DemoSource);
    }

    /// <summary>
    /// The hand (#159, #160) noted in the operator log. A hook or unhook is a field (`ROPE hook 1/0`) and is logged
    /// like any control. A drag is a path through time, not one value, so it isn't a log entry: it takes the controls
    /// from any demo operator and prints its own replay line (HEROIC_DRAG), which reproduces it.
    /// </summary>
    private sealed class LoggedHandActions(Main main) : IHandActions
    {
        public void RecordDrag(MachineView view, DragRecord drag)
        {
            main.TakeControl();
            GD.Print($"[hand] HEROIC_DRAG=\"{drag.ToReplay()}\"");
        }

        public void RecordHook(MachineView view, string rope, string action, string? load, Vector3 point)
        {
            main.TakeControl();
            // a stone laid in a sling's pouch (#161) is its own field, SLING load 1
            if (view == main._current) main.LogOperatorAction(new OperatorAction(view.Runtime.Time, rope, action == "load" ? "load" : "hook", action == "unhook" ? 0 : 1));
        }
    }

    /// <summary>The F key and the menu's Fire: one logged action for each boiler it changes.</summary>
    private void OperateFire()
    {
        if (_current is null) return;
        foreach (var (id, watts) in _current.FireToggles()) Operate(_current, id, "fire", watts);
    }

    private const string DemoSource = "demo", ReplaySource = "replay";

    private void LogOperatorAction(OperatorAction a)
    {
        if (_operatorLog.Count == 0) _operatorShown = true;   // the first action opens the log
        _operatorLog.Add(a);
        GD.Print($"[operate] {OperatorLog.Line(a)}");
        _operatorConsole.AddText(OperatorLog.Line(a) + "\n");
        RefreshOperatorSection();
    }

    /// <summary>
    /// Starts a fresh log for a machine just selected (or restarted), and queues what will act on it without a person:
    /// a replay when HEROIC_ACTIONS names a file, else the blueprint's demo operator unless a test is setting things.
    /// </summary>
    private void StartOperatorRun(MachineView view)
    {
        _operatorLog.Clear();
        _operatorTaken = false;
        _operatorConsole.Clear();
        _timedSettings.RemoveAll(s => s.Source is not null);
        if (OS.GetEnvironment("HEROIC_ACTIONS") is { Length: > 0 } file)
        {
            try
            {
                foreach (var a in OperatorLog.Parse(System.IO.File.ReadAllText(file)))
                    _timedSettings.Add((a.Target, a.Field, a.Value, a.At, ReplaySource));
            }
            catch (Exception e) when (e is IOException or FormatException)
            {
                SettingFailed($"{file}: {e.Message}", "HEROIC_ACTIONS");
            }
        }
        else if (view.Runtime.Def.Operator.Count > 0 && string.IsNullOrEmpty(OS.GetEnvironment("HEROIC_SET")))
        {
            foreach (var a in view.Runtime.Def.Operator) _timedSettings.Add((a.Target, a.Field, a.Value, a.At, DemoSource));
            // the cycle takes minutes of machine time: show it at a pace worth watching unless the machine asks for another
            if (!DefaultSpeeds.ContainsKey(_currentName ?? "")) SetSpeed(Math.Max(_timeScale, 20));
        }
        RefreshOperatorSection();
    }

    /// <summary>A save's log goes back as it was; what a demo or replay would still do after the saved clock goes on, what it already did does not repeat.</summary>
    private void RestoreOperator(WorldSave save, MachineView view)
    {
        _operatorLog.Clear();
        _operatorConsole.Clear();
        foreach (var a in save.Operated) { _operatorLog.Add(a); _operatorConsole.AddText(OperatorLog.Line(a) + "\n"); }
        _operatorTaken = save.OperatorTaken;
        double clock = view.Runtime.Time;
        _timedSettings.RemoveAll(s => s.Source is not null && (s.At <= clock + 1e-9 || (_operatorTaken && s.Source == DemoSource)));
        RefreshOperatorSection();
    }

    private void RefreshOperatorSection()
    {
        _operatorSection.Visible = _operatorShown;
    }

    private void ToggleOperatorLog()
    {
        _operatorShown = !_operatorShown;
        RefreshOperatorSection();
    }

    private void BuildOperatorSection(VBoxContainer col)
    {
        _operatorSection = new VBoxContainer { Visible = false };
        _operatorSection.AddThemeConstantOverride("separation", 6);
        _operatorSection.AddChild(new HSeparator());
        _operatorSection.AddChild(SectionLabel("Operator log (every action is a command)", 15));
        _operatorConsole = new RichTextLabel { CustomMinimumSize = new Vector2(0, 110), ScrollFollowing = true, SelectionEnabled = true, BbcodeEnabled = false };
        _operatorSection.AddChild(_operatorConsole);
        var row = new HBoxContainer();
        var copy = new Button { Text = "Copy as test", TooltipText = "The log as a Racket simulate form, on the clipboard" };
        copy.Pressed += () => { DisplayServer.ClipboardSet(OperatorTestText()); _hudNote.Text = "Copied the log as a test."; _hudNote.Visible = true; };
        var save = new Button { Text = "Save log", TooltipText = "Write the log to a file HEROIC_ACTIONS (or simulate's #:actions) can replay" };
        save.Pressed += () =>
        {
            string path = ProjectSettings.GlobalizePath($"{SavesDir}/{SaveName}.actions");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, OperatorLog.ToText(_operatorLog));
            _hudNote.Text = $"Saved the log to {path}."; _hudNote.Visible = true;
        };
        row.AddChild(copy);
        row.AddChild(save);
        _operatorSection.AddChild(row);
        col.AddChild(_operatorSection);
    }

    private string OperatorTestText() =>
        OperatorLog.ToSimulateForm(_currentName ?? "machine", _current?.Runtime.Time ?? 0, _operatorLog);

    // a scripted run's end: the log and its test, for a check to read
    private void WriteOperatorOutputs()
    {
        if (OS.GetEnvironment("HEROIC_ACTIONS_OUT") is { Length: > 0 } actions) System.IO.File.WriteAllText(actions, OperatorLog.ToText(_operatorLog));
        if (OS.GetEnvironment("HEROIC_TEST_OUT") is { Length: > 0 } test) System.IO.File.WriteAllText(test, OperatorTestText() + "\n");
    }

    /// <summary>Scripted-input steps: "operate TARGET FIELD VALUE" (a person's action) and "waitsim SECONDS" (until the run's clock gets there).</summary>
    private ScriptedInput.Step? OperatorStep(string[] w)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        switch (w[0])
        {
            case "operate" when _current is not null && w.Length == 4 && double.TryParse(w[3], inv, out double v):
                Operate(_current, w[1], w[2], v);
                return ScriptedInput.Step.Continue;
            case "waitsim" when _current is not null && w.Length == 2 && double.TryParse(w[1], inv, out double t):
                return _current.Runtime.Time >= t ? ScriptedInput.Step.Continue : ScriptedInput.Step.Again;
        }
        return null;
    }
}
