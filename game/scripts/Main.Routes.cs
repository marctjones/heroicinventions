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
        _routeLogPending.AddRange(_routes.Update(RouteReading.Read(runtimes), runtimes[0].Time, runtimes.Max(r => r.Sun.SolNumber)));
        RoutesFlushLog();
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
