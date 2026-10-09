using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The scenario's tuning numbers in the game (issue #60): a panel (F3, or <see cref="ToggleTuningPanel"/> from a menu button) that lists
/// every number the scenario feeds into the sim's unchanged formulas, with its label, unit, real value and a note; the Advanced
/// (risky) constants sit in their own section under a warning; a "Real Mars" button returns every number to its real value.
///
/// Before a game starts every number can be changed (the edits wait and are laid over the scenario's own numbers when a world loads,
/// so they hold for whichever scenario is started next). During a game the numbers the sim reads through a settable field each tick
/// (<see cref="ScenarioTuning.ChangeableInPlay"/>: call at any hour, the relay pass length, sleep speed) can still be changed; the
/// rest are built into the parts (a bank's capacity, a motor's cut-in, a bearing's wear, gravity) and are locked until the world is
/// restarted, where the edits made here are used. Hooks in Main.cs: <c>_Ready</c> adds the panel; <c>LoadWorld</c> and
/// <c>ReplaceView</c> build runtimes with <see cref="_activeTuning"/>; sleeping gets <see cref="SleepBudgetMs"/> per frame.
/// </summary>
public partial class Main
{
    /// <summary>The tuning the running world was built with (null: no world, or a machine run).</summary>
    private ScenarioTuning? _activeTuning;
    /// <summary>What the player has changed in the panel, by key, laid over the scenario's own numbers each time a world loads.</summary>
    private readonly Dictionary<string, double> _tuningEdits = [];
    private TuningPanel? _tuningPanel;

    /// <summary>Per-frame computing budget for a sleep, ms: 10 at real sleep speed.</summary>
    private double SleepBudgetMs => 10 * (_activeTuning?.SleepSpeed ?? 1);

    /// <summary>The scenario's numbers with the player's edits laid over them.</summary>
    private ScenarioTuning EffectiveTuning(WorldDef? world)
    {
        var t = world?.Scenario?.Tuning ?? ScenarioTuning.Real;
        foreach (var (key, v) in _tuningEdits) t = t.With(key, v);
        return t;
    }

    /// <summary>Called as a world loads: fixes the numbers its machines are built with.</summary>
    private void BeginWorldTuning(WorldDef world)
    {
        _activeTuning = EffectiveTuning(world);
        _tuningPanel?.Refresh();
        _goals?.RestartRun();   // a world (re)started: the path starts over, the achievements stay (a save loaded next puts its own back)
        _goalsKey = world.Name;
        _routes.RestartRun();   // and the route steps met: a new run (a save loaded next puts its own back)
    }

    private void BuildTuningPanel()
    {
        _tuningPanel = new TuningPanel(this) { Name = "TuningPanel", Visible = OS.GetEnvironment("HEROIC_TUNING_PANEL") == "1" };
        AddChild(_tuningPanel);
        BuildGoalsPanel();   // the goals panel and the achievement toast (Main.Goals.cs, #68)
        _sleep.Stepped += RoutesTick;   // routes are seen as a sleep runs through them (Main.Routes.cs, #233)
    }

    /// <summary>Shows or hides the tuning panel.</summary>
    public void ToggleTuningPanel() { if (_tuningPanel is { } p) { p.Visible = !p.Visible; if (p.Visible) p.Refresh(); } }

    /// <summary>The numbers the panel shows: the running world's, or what the next one would be built with.</summary>
    private ScenarioTuning TuningShown() => _world is not null && _activeTuning is { } active ? active : EffectiveTuning(null);
    private bool GameRunning => _world is not null;

    /// <summary>The player sets a number. In a game only a live one takes effect now; every edit is kept for the next world loaded.</summary>
    private void SetTuning(string key, double v)
    {
        _tuningEdits[key] = v;
        if (!GameRunning || _activeTuning is not { } active || !ScenarioTuning.ChangeableInPlay(key)) return;
        double before = active.Get(key);
        _activeTuning = active.With(key, v);
        foreach (var view in _views)
            foreach (var bank in view.Runtime.Banks.Values)
            {
                if (key == "call-any-time") bank.CallAnyTime = v != 0;
                else if (key == "call-window" && before > 0) bank.CallMinutes = Math.Max(1e-6, bank.CallMinutes * v / before);
            }
    }

    /// <summary>"Real Mars": every number back to its real value (the ones locked in a running game wait for a restart).</summary>
    private void RealMarsPreset()
    {
        foreach (var e in ScenarioTuning.Entries)
            SetTuning(e.Key, e.Real);
    }

    /// <summary>The panel's note on a value the scenario has tuned, for HUD numbers it changes (see Main.Win.cs).</summary>
    private string TunedNote(string key)
    {
        if (!GameRunning || _activeTuning is not { } t) return "";
        var e = ScenarioTuning.Entries.First(x => x.Key == key);
        double v = t.Get(key);
        return double.IsNaN(v) || v == e.Real ? "" : $" [tuned: {e.Label} {(e.Unit == "x" ? $"×{v:0.####}" : $"{v:0.##} {e.Unit}")}]";
    }
}

/// <summary>The tuning panel: a list of labelled numbers, the Advanced ones apart under a warning, and the Real Mars preset.</summary>
public partial class TuningPanel : CanvasLayer
{
    private readonly Main _main;
    private VBoxContainer _rows = null!;
    private Label _status = null!;
    private readonly Dictionary<string, (SpinBox Box, CheckBox? Check, Label Lock)> _controls = [];
    private bool _building;

    public TuningPanel(Main main) { _main = main; Layer = 70; }

    public override void _Ready()
    {
        var root = new PanelContainer { Position = new Vector2(540, 60), CustomMinimumSize = new Vector2(600, 0) };
        root.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.17f, 0.15f, 0.14f, 0.96f), ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6 });
        AddChild(root);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        root.AddChild(col);
        var title = new Label { Text = "Game tuning (F3)" };
        title.AddThemeFontSizeOverride("font_size", 20);
        col.AddChild(title);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(570, 0) };
        _status.AddThemeColorOverride("font_color", HudTheme.CreamDim);
        col.AddChild(_status);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(570, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        col.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_rows);
        var buttons = new HBoxContainer();
        var real = new Button { Text = "Real Mars", TooltipText = "Every number, basic and Advanced, back to its real value" };
        real.Pressed += () => { _main.CallRealMars(); Refresh(); };
        var close = new Button { Text = "Close" };
        close.Pressed += () => Visible = false;
        buttons.AddChild(real); buttons.AddChild(close);
        col.AddChild(buttons);
        Refresh();
    }

    private (bool Game, ScenarioTuning Tuning)? _shown;

    /// <summary>The numbers in force change as a world loads: the rows follow them.</summary>
    public override void _Process(double delta)
    {
        if (!Visible) return;
        var now = (_main.CallGameRunning(), _main.CallTuningShown());
        if (_shown is { } s && s.Game == now.Item1 && s.Tuning == now.Item2) return;
        _shown = (now.Item1, now.Item2);
        Refresh();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 }) { _main.ToggleTuningPanel(); GetViewport().SetInputAsHandled(); }
    }

    /// <summary>Rebuilds the rows from the numbers now in force.</summary>
    public void Refresh()
    {
        if (_rows is null) return;
        _building = true;
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }
        _controls.Clear();
        var t = _main.CallTuningShown();
        bool game = _main.CallGameRunning();
        _status.Text = game
            ? "A game is running. Numbers marked locked are built into the parts; change them here and restart the world to use them."
            : "No game yet: change any number; they are used by the next scenario you start.";
        _rows.AddChild(Heading("Rate multipliers (unrealistic; real = 1)"));
        foreach (var e in ScenarioTuning.Entries.Where(e => !e.Advanced)) AddRow(e, t, game);
        var warn = Heading("Advanced: physical constants (risky)");
        warn.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.35f));
        _rows.AddChild(new HSeparator());
        _rows.AddChild(warn);
        var caution = new Label { Text = "These carry the lessons (rain is a poor engine, flat mirrors can't melt basalt). Changing them changes what the game teaches; they are for experiments and difficulty.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0) };
        caution.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.55f));
        _rows.AddChild(caution);
        foreach (var e in ScenarioTuning.Entries.Where(e => e.Advanced)) AddRow(e, t, game);
        _building = false;
    }

    private static Label Heading(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 16);
        return l;
    }

    private void AddRow(ScenarioTuning.Entry e, ScenarioTuning t, bool game)
    {
        var box = new VBoxContainer();
        var row = new HBoxContainer();
        var name = new Label { Text = e.Label + (e.Advanced ? "  [risky]" : ""), CustomMinimumSize = new Vector2(250, 0) };
        row.AddChild(name);
        double v = t.Get(e.Key);
        bool locked = game && !ScenarioTuning.ChangeableInPlay(e.Key);
        CheckBox? check = null;
        var spin = new SpinBox { MinValue = e.Advanced && e.Key == "gravity" ? 0.01 : e.Unit == "x" ? 0.01 : e.Key.StartsWith("bank-m") ? -100 : 0, MaxValue = e.Unit == "x" ? 10000 : e.Key == "call-any-time" ? 1 : e.Key == "generator-efficiency" ? 1 : 1000, Step = e.Key == "call-any-time" ? 1 : 0.01, CustomMinimumSize = new Vector2(110, 0), Editable = !locked };
        bool unset = double.IsNaN(v);
        spin.Value = unset ? (e.Key == "gravity" ? 3.71 : 0.8) : v;
        if (double.IsNaN(e.Real))
        {
            check = new CheckBox { Text = "set", ButtonPressed = !unset, Disabled = locked };
            check.Toggled += on => { if (_building) return; _main.CallSetTuning(e.Key, on ? spin.Value : double.NaN); };
            row.AddChild(check);
        }
        spin.ValueChanged += x => { if (_building) return; if (check is { ButtonPressed: false }) return; _main.CallSetTuning(e.Key, x); };
        row.AddChild(spin);
        row.AddChild(new Label { Text = e.Unit });
        var lockLabel = new Label { Text = locked ? "locked during a game" : "", Modulate = new Color(1, 1, 1, 0.6f) };
        row.AddChild(lockLabel);
        box.AddChild(row);
        var note = new Label { Text = $"{e.Note}" + (double.IsNaN(e.Real) ? "" : $" (real: {e.Real:0.##})"), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(500, 0) };
        note.AddThemeFontSizeOverride("font_size", 12);
        note.AddThemeColorOverride("font_color", HudTheme.CreamDim);
        box.AddChild(note);
        _rows.AddChild(box);
        _controls[e.Key] = (spin, check, lockLabel);
    }
}

public partial class Main
{
    // thin public doors for the panel node (private members of Main stay private)
    public void CallRealMars() => RealMarsPreset();
    public ScenarioTuning CallTuningShown() => TuningShown();
    public bool CallGameRunning() => GameRunning;
    public void CallSetTuning(string key, double v) => SetTuning(key, v);
}
