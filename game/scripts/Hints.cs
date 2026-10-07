using Godot;

namespace HeroicInventions;

/// <summary>
/// First-run hints (#98): a few short pointers that appear from what the
/// player has and hasn't done, never as a scripted tour. Each waits for its
/// condition to hold for a while, shows once, and is retired for good as soon
/// as the player does the thing it points at, or dismisses it. "No more
/// hints" (or View ▸ Show Hints) turns them all off. Remembered in
/// user://settings.cfg (HEROIC_SETTINGS points elsewhere, for tests).
///
/// Off in scripted runs (HEROIC_INPUT, HEROIC_EDITOR_INPUT, headless) unless
/// HEROIC_HINTS=1, so recorded checks and screenshots stay clean.
/// </summary>
public partial class Hints : PanelContainer
{
    /// <summary>What the game is doing, read twice a second.</summary>
    public readonly record struct State(bool MachineShown, bool Running, double Speed, bool InBuildMode, bool CameraMoved,
                                        int MachinesWatched, int PartsInDesign);

    private sealed record Hint(string Id, string Text, double After, Func<State, bool> When, Func<State, bool> Done);

    private static readonly Hint[] All =
    [
        new("pick", "Pick a machine to watch from the list on the left, or press 1–9.", 8,
            s => !s.MachineShown && !s.InBuildMode, s => s.MachineShown),
        new("run", "It's paused. Space (or Run) starts it.", 6,
            s => s.MachineShown && !s.Running && !s.InBuildMode, s => s.Running),
        new("camera", "Look around: drag to orbit, scroll or pinch to zoom, arrow keys to move, Home to reset the view.", 15,
            s => s.Running && !s.CameraMoved && !s.InBuildMode, s => s.CameraMoved),
        new("speed", "Slow going? The speed row runs time faster: 5×, 20× (or ] and [). Heat machines need it.", 40,
            s => s.Running && Math.Abs(s.Speed - 1) < 1e-9 && !s.InBuildMode, s => Math.Abs(s.Speed - 1) > 1e-9),
        new("build", "Build your own: the Build Mode button, or Machine ▸ Build Mode.", 20,
            s => !s.InBuildMode && s.MachinesWatched >= 2, s => s.InBuildMode),
        new("place", "Drag a part from the list into the scene. Hold R and drag a part to turn it; Ctrl+Z undoes.", 5,
            s => s.InBuildMode && s.PartsInDesign == 0, s => s.PartsInDesign > 0),
    ];

    private readonly Func<State> _read;
    private readonly string _path;
    private readonly ConfigFile _config = new();
    private readonly HashSet<string> _retired = [];
    private readonly Dictionary<string, double> _held = [];
    private Hint? _showing;
    private double _tick;
    private Label _text = null!;

    public Hints(Func<State> read)
    {
        _read = read;
        _path = OS.GetEnvironment("HEROIC_SETTINGS") is { Length: > 0 } p ? p : "user://settings.cfg";
        _config.Load(_path);   // a missing file just leaves the defaults
        foreach (var id in _config.GetValue("hints", "retired", Array.Empty<string>()).AsStringArray()) _retired.Add(id);
        bool scripted = OS.GetEnvironment("HEROIC_INPUT") != "" || OS.GetEnvironment("HEROIC_EDITOR_INPUT") != ""
                        || DisplayServer.GetName() == "headless";
        Enabled = OS.GetEnvironment("HEROIC_HINTS") switch
        {
            "1" => true,
            "0" => false,
            _ => !scripted && _config.GetValue("hints", "enabled", true).AsBool(),
        };
    }

    /// <summary>Whether hints show at all (View ▸ Show Hints).</summary>
    public bool Enabled { get; private set; }

    public void SetEnabled(bool on)
    {
        Enabled = on;
        if (!on) Hide(retire: false);
        Save();
    }

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.CenterBottom);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Begin;
        OffsetTop = -150; OffsetBottom = -70;
        OffsetLeft = -230; OffsetRight = 230;
        var row = new VBoxContainer();
        AddChild(row);
        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0) };
        row.AddChild(_text);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var ok = new Button { Text = "Got it", FocusMode = FocusModeEnum.None };
        ok.Pressed += () => Hide(retire: true);
        var off = new Button { Text = "No more hints", FocusMode = FocusModeEnum.None };
        off.Pressed += () => SetEnabled(false);
        buttons.AddChild(off);
        buttons.AddChild(ok);
        row.AddChild(buttons);
    }

    public override void _Process(double delta)
    {
        if (!Enabled || (_tick += delta) < 0.5) return;
        double dt = _tick;
        _tick = 0;
        var state = _read();

        // anything the player has now done is retired, shown or not
        foreach (var h in All.Where(h => !_retired.Contains(h.Id) && h.Done(state)))
        {
            _retired.Add(h.Id);
            if (_showing == h) Hide(retire: false);
            Save();
        }
        if (_showing is not null) return;
        foreach (var h in All.Where(h => !_retired.Contains(h.Id)))
        {
            _held[h.Id] = h.When(state) ? _held.GetValueOrDefault(h.Id) + dt : 0;
            if (_held[h.Id] >= h.After) { Show(h); break; }
        }
    }

    private void Show(Hint hint)
    {
        _showing = hint;
        _text.Text = hint.Text;
        Visible = true;
        GD.Print($"[hints] showing {hint.Id}");
    }

    private void Hide(bool retire)
    {
        if (_showing is { } h && retire) { _retired.Add(h.Id); Save(); }
        _showing = null;
        Visible = false;
    }

    private void Save()
    {
        _config.SetValue("hints", "enabled", Enabled);
        _config.SetValue("hints", "retired", _retired.ToArray());
        _config.Save(_path);
    }
}
