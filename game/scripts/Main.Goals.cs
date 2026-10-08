using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Game;

namespace HeroicInventions;

/// <summary>
/// Goals and achievements in the game (issue #68): the path to the call as a checklist, Heron's concept achievements and the route
/// achievements, in a panel (F2, <c>HEROIC_GOALS_PANEL=1</c> opens it at the start), and a toast when one is earned. The goals are
/// <see cref="GoalTracker"/>'s (src/HeroicInventions.Sim/Game/Goals.cs), read from the sim each frame and between the steps of a sleep, and
/// persist in the world save (<c>WorldSave.Goals</c>). Hooks in Main.cs: <c>BuildTuningPanel</c> (called from <c>_Ready</c>) builds the panel
/// and toast; <c>_PhysicsProcess</c> calls <see cref="GoalsTick"/>; <c>SaveWorld</c> writes <c>Goals = GoalsForSave()</c>;
/// <c>LoadSave</c> calls <c>GoalsRestore</c>.
/// </summary>
public partial class Main
{
    private GoalTracker? _goals;
    private string? _goalsKey;                  // the world or machine the run is of: a different one starts a new run
    private GoalsPanel? _goalsPanel;
    private Label? _toast;
    private double _toastUntil;
    private readonly bool _goalsReport = OS.GetEnvironment("HEROIC_GOALS_REPORT") == "1";
    private double _goalsReportAt;
    private readonly Queue<string> _toastQueue = new();

    private void BuildGoalsPanel()
    {
        _goalsPanel = new GoalsPanel(this) { Name = "GoalsPanel", Visible = OS.GetEnvironment("HEROIC_GOALS_PANEL") == "1" };
        AddChild(_goalsPanel);
        var layer = new CanvasLayer { Layer = 80, Name = "GoalToast" };
        AddChild(layer);
        _toast = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(400, 8), Size = new Vector2(800, 80) };
        _toast.AddThemeFontSizeOverride("font_size", 22);
        _toast.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.55f));
        _toast.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _toast.AddThemeConstantOverride("outline_size", 8);
        layer.AddChild(_toast);
        _sleep.Stepped += GoalsTick;            // see a night or a sol as a sleep runs through it
    }

    /// <summary>Shows or hides the goals panel.</summary>
    public void ToggleGoalsPanel() { if (_goalsPanel is { } p) { p.Visible = !p.Visible; if (p.Visible) p.Refresh(); } }

    private List<Sim.Machines.MachineRuntime> GoalRuntimes() =>
        (_views.Count > 0 ? _views.Select(v => v.Runtime) : _current is null ? [] : [_current.Runtime]).ToList();

    /// <summary>The bank's crate in a world that has one placed as "battery-bank": its cover and place, read from the rigid body and the ground.</summary>
    private CrateReading? BankCrateReading()
    {
        if (_groundSim?.Ground is not { } ground || !_byName.TryGetValue("battery-bank", out var view)) return null;
        if (view.Runtime.Banks.Count > 0) return null;                       // a bank built into the scene is read from the sim itself
        if (view.BodyNamed("crate") is not { } body || !IsInstanceValid(body)) return null;
        var at = PhysicsServer3D.BodyGetDirectState(body.GetRid()).Transform.Origin;
        double size = view.Runtime.Def.Part("crate")?.Number("size", 0.5) ?? 0.5;
        return new CrateReading(ground.HeightAt(at.X, at.Z) - (at.Y + size / 2), size, at.X, at.Z);
    }

    /// <summary>A tracker reading this game's crate and ground (a save loaded first thing makes one before any frame has run).</summary>
    private GoalTracker NewTracker() => new()
    {
        BankCrate = BankCrateReading,
        GroundSettled = () => _groundSim is null || !_groundSim.FieldGetters.TryGetValue("map.settling", out var settling) || settling() == 0,
    };

    /// <summary>Looks at the scene and toasts what was newly earned. Every frame, and every half second of a sleep.</summary>
    private void GoalsTick()
    {
        var runtimes = GoalRuntimes();
        if (runtimes.Count == 0) return;
        string key = _world?.Name ?? _currentName ?? "";
        _goals ??= NewTracker();
        if (_goalsReport && runtimes[0].Time >= _goalsReportAt)
        {
            _goalsReportAt = runtimes[0].Time + 2;
            var c = BankCrateReading();
            GD.Print($"[goals] t={runtimes[0].Time:0.0} crate " + (c is null ? "none" : $"cover {c.Cover:0.00} m at ({c.X:0.0}, {c.Z:0.0})") + $" settled {_goals.GroundSettled()} earned {_goals.EarnedGoals.Count}");
        }
        if (_goalsKey != key) { if (_goalsKey is not null) _goals.RestartRun(); _goalsKey = key; }
        foreach (var goal in _goals.Update(runtimes, runtimes[0].Time))
        {
            GD.Print($"[goal] {goal.Title}: {goal.Story}");
            _toastQueue.Enqueue($"{(goal.Kind == GoalKind.Path ? "Goal" : "Achievement")}: {goal.Title}\n{goal.Story}");
            _goalsPanel?.Refresh();
        }
    }

    private void ToastTick(double delta)
    {
        if (_toast is null) return;
        _toastUntil -= delta;
        if (_toastUntil > 0) return;
        _toast.Visible = false;
        if (_toastQueue.Count > 0) { _toast.Text = _toastQueue.Dequeue(); _toast.Visible = true; _toastUntil = 4; }
    }

    /// <summary>The goals as the world save holds them.</summary>
    private Sim.Machines.SList? GoalsForSave() => _goals?.ToForm();

    /// <summary>Takes up the goals a save holds (an older save has none).</summary>
    private void GoalsRestore(Sim.Machines.WorldSave save)
    {
        _goals ??= NewTracker();
        _goalsKey = _world?.Name ?? _currentName ?? "";
        if (save.Goals is { } g) _goals.Restore(g);
        _goalsPanel?.Refresh();
    }

    // thin doors for the panel node
    public GoalTracker? CallGoals() => _goals;
}

/// <summary>The goals panel: the path to the call as a checklist, then Heron's catalogue and the routes, each with what it reads.</summary>
public partial class GoalsPanel : CanvasLayer
{
    private readonly Main _main;
    private VBoxContainer _rows = null!;

    public GoalsPanel(Main main) { _main = main; Layer = 70; }

    public override void _Ready()
    {
        var root = new PanelContainer { Position = new Vector2(310, 60), CustomMinimumSize = new Vector2(560, 0) };
        root.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.17f, 0.15f, 0.14f, 0.96f), ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6 });
        AddChild(root);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        root.AddChild(col);
        var title = new Label { Text = "Goals (F2)" };
        title.AddThemeFontSizeOverride("font_size", 20);
        col.AddChild(title);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(530, 640) };
        col.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_rows);
        Refresh();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F2 }) { _main.ToggleGoalsPanel(); GetViewport().SetInputAsHandled(); }
    }

    public void Refresh()
    {
        if (_rows is null) return;
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }
        var goals = _main.CallGoals();
        void Section(string text, GoalKind kind)
        {
            var l = new Label { Text = text };
            l.AddThemeFontSizeOverride("font_size", 16);
            _rows.AddChild(new HSeparator());
            _rows.AddChild(l);
            foreach (var g in GoalTracker.Catalogue.Where(g => g.Kind == kind))
            {
                var earned = goals?.Get(g.Id);
                var row = new Label
                {
                    Text = (earned is null ? "[ ]  " : "[x]  ") + g.Title + (earned is { } e ? $"   (sol {e.Sol})" : ""),
                    TooltipText = "Earned when: " + g.Trigger,
                };
                if (earned is null) row.AddThemeColorOverride("font_color", HudTheme.CreamDim);
                _rows.AddChild(row);
                if (earned is { }) { var s = new Label { Text = "      " + g.Story, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(500, 0) }; s.AddThemeFontSizeOverride("font_size", 12); s.AddThemeColorOverride("font_color", HudTheme.CreamDim); _rows.AddChild(s); }
                else
                {
                    var s = new Label { Text = "      " + g.Trigger, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(500, 0) };
                    s.AddThemeFontSizeOverride("font_size", 11);
                    s.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.45f));
                    _rows.AddChild(s);
                }
            }
        }
        Section("The path to the call", GoalKind.Path);
        Section("Heron's catalogue", GoalKind.Concept);
        Section("Routes", GoalKind.Route);
    }
}
