using Godot;
using HeroicInventions.Sim.Game;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Routes to the win (issues #227 to #233), the game's side of src/HeroicInventions.Sim/Game/Routes.cs and RouteReveal.cs. The routes are
/// the files of <c>res://routes</c> (all of them, for any world) and of <c>res://routes/&lt;world&gt;</c> (a world's own). Each frame, and
/// between the steps of a sleep, the scene is read and the first time of every step met and route started is recorded, written to the rover
/// log with the scene's clock (#233) and held in <see cref="_routes"/>'s history, which the world save keeps (<c>WorldSave.Routes</c>).
/// The goals panel asks <see cref="RoutesView"/> what to show; this file draws nothing.
/// Hooks: <c>SaveWorld</c> (<see cref="RoutesForSave"/>), <c>LoadSave</c> (<see cref="RoutesRestore"/>), <c>_PhysicsProcess</c> and the sleep's
/// <c>Stepped</c> (<see cref="RoutesTick"/>), <c>BeginWorldTuning</c> (restart), the links trace (<see cref="RouteFields"/>), and RoverLogTick (<see cref="RoutesFlushLog"/>).
/// </summary>
public partial class Main
{
    private const string RoutesDir = "res://routes";
    private readonly RouteTracker _routes = new();
    private string? _routesFor;                         // the world the routes were loaded for
    private readonly List<RouteEvent> _routeLogPending = [];

    /// <summary>Reads the route files for the running world, once per world. A malformed one is reported with its file and step, and no route is shown from it.</summary>
    private void RoutesEnsureLoaded()
    {
        string key = _world?.Name ?? "";
        if (_routes.Loaded && _routesFor == key) return;
        _routesFor = key;
        var files = new List<(string, string)>();
        void Add(string dir)
        {
            if (!DirAccess.DirExistsAbsolute(dir)) return;
            foreach (string f in DirAccess.GetFilesAt(dir).Where(f => f.EndsWith(".route")).Order())
                files.Add(($"{dir}/{f}", Godot.FileAccess.GetFileAsString($"{dir}/{f}")));
        }
        Add(RoutesDir);
        if (key.Length > 0) Add($"{RoutesDir}/{key}");
        try { _routes.Load(RouteLibrary.Parse(files)); }
        catch (MachineFormatException e)
        {
            GD.PushError($"[routes] {e.Message}");
            _routes.Load([]);
        }
    }

    /// <summary>What the goals panel shows: the final goal, and the routes the player has started.</summary>
    public RouteView RoutesView()
    {
        RoutesEnsureLoaded();
        var runtimes = GoalRuntimes();
        return _routes.View(runtimes.Count == 0 ? RouteReading.Empty : RouteReading.Read(runtimes));
    }

    /// <summary>Every frame, and every half second of a sleep: records what is newly met.</summary>
    private void RoutesTick()
    {
        var runtimes = GoalRuntimes();
        if (runtimes.Count == 0) return;
        RoutesEnsureLoaded();
        var events = _routes.Update(RouteReading.Read(runtimes), runtimes[0].Time, runtimes.Max(r => r.Sun.SolNumber));
        _routeLogPending.AddRange(events);
        RoutesFlushLog();
        RoutesPanelTick(events.Count > 0);
    }

    // ----------------------------------------------------------------- the Routes block of the goals panel (#230)

    private RoutesPanelLevel _routesLevel = RoutesPanelLevel.NextSteps;
    private bool _routesLevelChosen;                    // the player picked a level in this game: re-asserted in the settings file (see RoutesPanelTick)
    private string _routesShape = "", _routesText = ""; // the last block drawn: its shape (levels, routes, steps and statuses) and its text (reasons carry live numbers)
    private double _routesDumpAt = double.NegativeInfinity;
    private int _routesFrames;
    private readonly bool _routesDump = OS.GetEnvironment("HEROIC_ROUTES_PANEL") is { Length: > 0 };

    /// <summary>The panel's level: HEROIC_ROUTES_PANEL (1 or next, outline, 0 or off) when set, else a scripted run (as Hints treats one) is off, else what the settings file remembers (next steps by default).</summary>
    private RoutesPanelLevel RoutesLevelAtStart()
    {
        bool scripted = OS.GetEnvironment("HEROIC_INPUT") != "" || OS.GetEnvironment("HEROIC_EDITOR_INPUT") != "" || DisplayServer.GetName() == "headless";
        return OS.GetEnvironment("HEROIC_ROUTES_PANEL") switch
        {
            "1" or "next" => RoutesPanelLevel.NextSteps,
            "outline" => RoutesPanelLevel.Outline,
            "0" or "off" => RoutesPanelLevel.Off,
            _ when scripted => RoutesPanelLevel.Off,
            _ => RoutesPanelSetting.Read(RoutesSettingsPath()) ?? RoutesPanelLevel.NextSteps,
        };
    }

    private static string RoutesSettingsPath() => OS.GetEnvironment("HEROIC_SETTINGS") is { Length: > 0 } p ? p : "user://settings.cfg";

    /// <summary>Sets the level (the panel's three buttons), remembers it in the settings file's own section and redraws the block.</summary>
    public void SetRoutesLevel(RoutesPanelLevel level)
    {
        _routesLevel = level;
        _routesLevelChosen = true;
        RoutesPanelSetting.Write(RoutesSettingsPath(), level);
        RoutesPanelRefresh(true);
    }

    public RoutesPanelLevel RoutesLevel => _routesLevel;

    /// <summary>The block's lines at the current level, from the reveal rule's view of the scene. A route's progress is counted only for routes the view shows (started), so an unstarted route is never counted or named.</summary>
    public IReadOnlyList<PanelLine> RoutesPanelLines()
    {
        if (_routesLevel == RoutesPanelLevel.Off) return [];
        var view = RoutesView();
        var runtimes = GoalRuntimes();
        var now = runtimes.Count == 0 ? RouteReading.Empty : RouteReading.Read(runtimes);
        var progress = new Dictionary<string, (int Met, int Total)>();
        foreach (var shown in view.Routes)
            if (_routes.Routes.FirstOrDefault(r => r.Id == shown.Id) is { } route)
                progress[shown.Id] = (route.Steps.Count(st => st.IsMet(now) || _routes.History.HasMet(route.Id, st.Id)), route.Steps.Count);
        return RoutesPanelText.Lines(view, _routesLevel, progress);
    }

    /// <summary>Redraws the block when what it says changed; prints it (behind HEROIC_ROUTES_PANEL) when its shape changed or its numbers moved and ten seconds of the scene's clock have gone.</summary>
    public void RoutesPanelRefresh(bool force = false)
    {
        var lines = RoutesPanelLines();
        string shape = _routesLevel + "|" + string.Join("|", lines.Select(l => l.Kind == PanelLineKind.Reason ? "" : l.Text));
        string text = string.Join("\n", lines.Select(l => l.Text));
        bool shapeChanged = shape != _routesShape;
        if (force || shapeChanged || text != _routesText) _goalsPanel?.ShowRoutes(lines);
        double clock = GoalRuntimes() is { Count: > 0 } rt ? rt[0].Time : 0;
        if (_routesDump && GoalRuntimes().Count > 0 && (shapeChanged || (text != _routesText && clock >= _routesDumpAt + 10)))
        {
            _routesDumpAt = clock;
            GD.Print($"[routes-panel] begin level={_routesLevel} t={clock:0.0}");
            foreach (var l in lines) GD.Print($"[routes-panel] {l.Kind}: {l.Text}");
            GD.Print("[routes-panel] end");
        }
        if (_routesDump && GoalRuntimes().Count == 0) return;   // nothing to read yet: the first block with a scene is the one to print
        _routesShape = shape;
        _routesText = text;
    }

    /// <summary>Each frame (and each half second of a sleep) the block is looked at twice a second while the panel is up or the dump is on, and at once when a step or route was newly met.</summary>
    private void RoutesPanelTick(bool newlyMet)
    {
        if (++_routesFrames % 30 == 0 && _routesLevelChosen) RoutesPanelSetting.Reassert(RoutesSettingsPath(), _routesLevel);
        if (newlyMet || (_routesFrames % 30 == 1 && (_routesDump || _goalsPanel is { Visible: true }))) RoutesPanelRefresh();
    }

    /// <summary>Writes the pending first times to the rover log once it has begun for this world (RoverLogTick starts a new log, which would wipe an earlier line).</summary>
    private void RoutesFlushLog()
    {
        if (_routeLogPending.Count == 0 || _rover is null || _logWorld != _world) return;
        foreach (var e in _routeLogPending) AddLog("route", e.Text);
        _routeLogPending.Clear();
    }

    private SList? RoutesForSave() => _routes.History.Count > 0 ? _routes.ToForm() : null;

    private void RoutesRestore(WorldSave save)
    {
        RoutesEnsureLoaded();
        if (save.Routes is { } r) _routes.Restore(r);
    }

    /// <summary>Playtest fields for the links trace (<c>route.ROUTE.STEP</c>: the scene clock when first met, -1 before).</summary>
    private IReadOnlyDictionary<string, Func<double>> RouteFields
    {
        get { RoutesEnsureLoaded(); return _routes.TraceFields(); }
    }
}

public enum RoutesPanelLevel { Off, Outline, NextSteps }

public enum PanelLineKind { Header, Final, Route, Step, StepDim, Reason }

/// <summary>One line of the Routes block: its text and how it is drawn.</summary>
public sealed record PanelLine(PanelLineKind Kind, string Text);

/// <summary>
/// The Routes block's text, a pure function of the reveal rule's view (#229): the final goal always; per started route its name and how far
/// along, and at the next-steps level the steps the view gives with their reasons. It adds nothing about routes the view does not show: no
/// 'more routes' line and no count of them.
/// </summary>
public static class RoutesPanelText
{
    public static IReadOnlyList<PanelLine> Lines(RouteView view, RoutesPanelLevel level, IReadOnlyDictionary<string, (int Met, int Total)> progress)
    {
        var lines = new List<PanelLine>();
        if (level == RoutesPanelLevel.Off) return lines;
        lines.Add(new PanelLine(PanelLineKind.Header, "Routes to the call"));
        lines.Add(new PanelLine(PanelLineKind.Final, (view.Final.Met ? "[x]  " : "[ ]  ") + view.Final.Title));
        if (level == RoutesPanelLevel.NextSteps) lines.Add(new PanelLine(PanelLineKind.Reason, view.Final.Reason));
        foreach (var route in view.Routes)
        {
            string how = progress.TryGetValue(route.Id, out var p) ? $": {p.Met} of {p.Total} steps" : "";
            lines.Add(new PanelLine(PanelLineKind.Route, route.Name + how));
            if (level != RoutesPanelLevel.NextSteps) continue;
            foreach (var step in route.Steps)
            {
                lines.Add(new PanelLine(step.Status == StepStatus.After ? PanelLineKind.StepDim : PanelLineKind.Step, step.Status switch
                {
                    StepStatus.Met => "[x]  " + step.Title,
                    StepStatus.DoneEarlier => "[x]  " + step.Title + " (done earlier)",
                    StepStatus.Next => "[ ]  Next: " + step.Title,
                    _ => "[ ]  Then: " + step.Title,
                }));
                if (step.Status is StepStatus.Next or StepStatus.After) lines.Add(new PanelLine(PanelLineKind.Reason, step.Reason));
            }
        }
        return lines;
    }
}

/// <summary>The level in the settings file, in its own section ("routes-panel", key "level"), so Hints.cs's section is never edited by it.</summary>
public static class RoutesPanelSetting
{
    private const string Section = "routes-panel", Key = "level";

    public static string Name(RoutesPanelLevel l) => l switch { RoutesPanelLevel.Off => "off", RoutesPanelLevel.Outline => "outline", _ => "next" };

    public static RoutesPanelLevel? Read(string path)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(path) != Error.Ok) return null;
        return cfg.GetValue(Section, Key, "").AsString() switch
        {
            "off" => RoutesPanelLevel.Off, "outline" => RoutesPanelLevel.Outline, "next" => RoutesPanelLevel.NextSteps, _ => null,
        };
    }

    /// <summary>Loads the file again first, so whatever else it holds (the hints' section) is kept as it is now.</summary>
    public static void Write(string path, RoutesPanelLevel level)
    {
        var cfg = new ConfigFile();
        cfg.Load(path);
        cfg.SetValue(Section, Key, Name(level));
        cfg.Save(path);
    }

    /// <summary>Hints.cs saves the whole file from the copy it read at start, which drops this section; so a chosen level is written back if the file no longer has it.</summary>
    public static void Reassert(string path, RoutesPanelLevel level) { if (Read(path) != level) Write(path, level); }
}
