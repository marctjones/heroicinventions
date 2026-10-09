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
/// HEROIC_HINTS=1, so recorded checks and screenshots stay clean. HEROIC_HINTS_RATE=n runs the hints' waiting clocks n times
/// faster (a test aid: the rover hints wait minutes).
///
/// In the rover game (a world with a rover the player drives) the sandbox pointers about picking a machine, the camera keys and
/// the speed row are replaced by four that point at the player's tools and never at a route: driving and the backhoe, the speed and
/// sleep controls, the rover log, build mode. They wait on real time (not the sim's speed), only count while no page is up, show
/// for 40 s at most and then retire like any other hint, so a player who ignores one still meets the next.
/// </summary>
public partial class Hints : PanelContainer
{
    /// <summary>What the game is doing, read twice a second.</summary>
    public readonly record struct State(bool MachineShown, bool Running, double Speed, bool InBuildMode, bool CameraMoved,
                                        int MachinesWatched, int PartsInDesign, bool LessonStarted = false,
                                        bool InRover = false, bool OnPage = false, bool OnLog = false, Vector3 RoverPos = default,
                                        bool ArmBusy = false, bool Sleeping = false,
                                        bool RoverDriven = false, bool LogOpened = false, bool SpeedOrSleepUsed = false);

    private sealed record Hint(string Id, string Text, double After, Func<State, bool> When, Func<State, bool> Done, double Lifetime = 0);

    /// <summary>The rover game's pointers: the player is in the rover world, on no page, and hasn't yet done the thing.</summary>
    private static bool Playing(State s) => s.InRover && !s.OnPage;

    private static readonly Hint[] All =
    [
        new("pick", "Pick a machine to watch from the list on the left, or press 1–9.", 8,
            s => !s.MachineShown && !s.InBuildMode && !s.InRover, s => s.MachineShown),
        new("run", "It's paused. Space (or Run) starts it.", 6,
            s => s.MachineShown && !s.Running && !s.InBuildMode, s => s.Running),
        new("camera", "Look around: drag to orbit, scroll or pinch to zoom, arrow keys to move, Home to reset the view.", 15,
            s => s.Running && !s.CameraMoved && !s.InBuildMode && !s.InRover, s => s.CameraMoved),
        new("speed", "Slow going? The speed row runs time faster: 5×, 20× (or ] and [). Heat machines need it.", 40,
            s => s.Running && Math.Abs(s.Speed - 1) < 1e-9 && !s.InBuildMode && !s.InRover, s => Math.Abs(s.Speed - 1) > 1e-9),
        new("build", "Build your own: the Build Mode button, or Machine ▸ Build Mode.", 20,
            s => !s.InBuildMode && s.MachinesWatched >= 2 && !s.InRover, s => s.InBuildMode),
        new("place", "New here? Press Lessons (top left) to build a see-saw step by step. Or drag a part from the list into the scene; Ctrl+Z undoes.", 5,
            s => s.InBuildMode && s.PartsInDesign == 0 && !s.LessonStarted, s => s.PartsInDesign > 0 || s.LessonStarted),

        // the rover game (#98): tools, never the route
        new("rover-drive", "Arrows or W A S D drive the rover, and B works its backhoe. Drag to look around.", 20,
            s => Playing(s) && s.Running && !s.RoverDriven && !s.InBuildMode, s => s.RoverDriven, Lifetime: 40),
        new("rover-speed", "Things here take sols. The speed row runs time faster, and Sleep until… skips ahead to a time or event you name.", 150,
            s => Playing(s) && !s.SpeedOrSleepUsed && !s.InBuildMode, s => s.SpeedOrSleepUsed, Lifetime: 40),
        new("rover-log", "The rover keeps a log of what it notices, refusals included. Press I to read it.", 270,
            s => Playing(s) && !s.LogOpened && !s.InBuildMode, s => s.LogOpened, Lifetime: 40),
        new("rover-build", "\"Build a new machine\" (left panel) opens build mode: set parts down and watch what they do.", 390,
            s => Playing(s) && !s.InBuildMode, s => s.InBuildMode, Lifetime: 40),
    ];

    private readonly Func<State> _read;
    private readonly string _path;
    private readonly ConfigFile _config = new();
    private readonly HashSet<string> _retired = [];
    private readonly Dictionary<string, double> _held = [];
    private Hint? _showing;
    private double _tick, _shownFor;
    private readonly double _rate = double.TryParse(OS.GetEnvironment("HEROIC_HINTS_RATE"), System.Globalization.CultureInfo.InvariantCulture, out double r) && r > 0 ? r : 1;
    // what the player has done in this rover world, kept every frame so a quick press isn't missed between two reads
    private Vector3? _roverStart;
    private bool _driven, _logOpened, _speedUsed;
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
        // waits are in real seconds: Godot hands _Process the delta already scaled by the sim's speed
        double real = delta / Math.Max(Engine.TimeScale, 1e-9) * _rate;
        var state = Latch(_read());
        if (!Enabled || (_tick += real) < 0.5) return;
        double dt = _tick;
        _tick = 0;
        state = state with { RoverDriven = _driven, LogOpened = _logOpened, SpeedOrSleepUsed = _speedUsed };

        // anything the player has now done is retired, shown or not
        foreach (var h in All.Where(h => !_retired.Contains(h.Id) && h.Done(state)))
        {
            _retired.Add(h.Id);
            if (_showing == h) Hide(retire: false);
            Save();
        }
        if (_showing is { Lifetime: > 0 } shown && (_shownFor += dt) >= shown.Lifetime)
        {
            _retired.Add(shown.Id);   // shown once: it had its time
            Save();
            Hide(retire: false);
            GD.Print($"[hints] expired {shown.Id}");
        }
        if (_showing is not null) return;
        foreach (var h in All.Where(h => !_retired.Contains(h.Id)))
        {
            _held[h.Id] = h.When(state) ? _held.GetValueOrDefault(h.Id) + dt : 0;
            if (_held[h.Id] >= h.After) { Show(h); break; }
        }
    }

    /// <summary>Notes, every frame, what the player has done in the rover world; reset when there is no rover.</summary>
    private State Latch(State s)
    {
        if (!s.InRover) { _roverStart = null; _driven = _logOpened = _speedUsed = false; return s; }
        if (s.OnLog) _logOpened = true;
        if (s.OnPage) return s;
        _roverStart ??= s.RoverPos;
        if (s.ArmBusy || (s.RoverPos - _roverStart.Value).Length() > 0.3f) _driven = true;
        if (Math.Abs(s.Speed - 1) > 1e-9 || s.Sleeping) _speedUsed = true;
        return s;
    }

    private void Show(Hint hint)
    {
        _showing = hint;
        _shownFor = 0;
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
