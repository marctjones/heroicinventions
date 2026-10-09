using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Game;

// The reveal rule (issue #229) and the history it needs, with the owner's decisions of 2026-10-09 (#234, #240):
//  1. The final goal (call Earth with a full, warm bank) is always shown.
//  2. A route is STARTED when any of its steps has ever been met (the final goal is not one of its steps), except a SHARED step, one any
//     route needs whatever its power source (a warm, full bank): that never starts or names a route, but shows and counts as met once started.
//  3. For a started route: its next unmet step and the one after it, and the last step met.
//  4. A route not started is hidden AND unnamed: it is not in the output at all.
//  5. A step once met stays met even if the state undoes it (status DoneEarlier).
//  6. A step with alternatives is met by any one.
// "Next", "after" and "last" follow the route's order in its file: next = the first step never met, after = the second, last = the
// highest-numbered step ever met. (A player who wires the generator before building the windmill still sees the windmill step as next.)

public enum StepStatus
{
    /// <summary>Not yet met, the first such in the route: what the route is waiting for.</summary>
    Next,
    /// <summary>Not yet met, the second such.</summary>
    After,
    /// <summary>The last step met, and its condition holds now.</summary>
    Met,
    /// <summary>The last step met, though its condition no longer holds ("done earlier").</summary>
    DoneEarlier,
}

public sealed record ShownStep(string RouteId, string StepId, string Title, string Reason, StepStatus Status);
public sealed record ShownRoute(string Id, string Name, string Description, IReadOnlyList<ShownStep> Steps);
public sealed record ShownFinal(string Id, string Title, string Reason, bool Met);

/// <summary>What the goals panel shows: the final goal, and the routes the player has started with their steps in the route's order.</summary>
public sealed record RouteView(ShownFinal Final, IReadOnlyList<ShownRoute> Routes);

/// <summary>When each step was first met and each route first started, on the scene's clock: the 'ever met' history and the playtest record (#233).</summary>
public sealed class RouteHistory
{
    /// <summary>The route id under which the final goal's first time is kept.</summary>
    public const string FinalRoute = "final";

    private readonly Dictionary<(string Route, string Step), (double Time, int Sol)> _met = [];
    private readonly Dictionary<string, (double Time, int Sol)> _started = [];

    public bool HasMet(string route, string step) => _met.ContainsKey((route, step));
    public bool IsStarted(string route) => _started.ContainsKey(route);
    public (double Time, int Sol)? MetAt(string route, string step) => _met.TryGetValue((route, step), out var t) ? t : null;
    public (double Time, int Sol)? StartedAt(string route) => _started.TryGetValue(route, out var t) ? t : null;
    public void RecordMet(string route, string step, double time, int sol) => _met.TryAdd((route, step), (time, sol));
    public void RecordStarted(string route, double time, int sol) => _started.TryAdd(route, (time, sol));
    public void Clear() { _met.Clear(); _started.Clear(); }
    public int Count => _met.Count + _started.Count;

    /// <summary><c>(routes 1 (started ROUTE time sol) … (met ROUTE STEP time sol) …)</c>.</summary>
    public SList ToForm()
    {
        var items = new List<SExpr> { new SSymbol("routes"), new SNumber(1) };
        foreach (var (r, t) in _started.OrderBy(kv => kv.Value.Time)) items.Add(new SList([new SSymbol("started"), new SSymbol(r), new SNumber(t.Time), new SNumber(t.Sol)]));
        foreach (var (k, t) in _met.OrderBy(kv => kv.Value.Time)) items.Add(new SList([new SSymbol("met"), new SSymbol(k.Route), new SSymbol(k.Step), new SNumber(t.Time), new SNumber(t.Sol)]));
        return new SList(items);
    }

    /// <summary>Takes up a save's history (added to what is here: a step met is never taken back). Entries it cannot read are skipped.</summary>
    public void Restore(SList form)
    {
        foreach (var s in form.Fields("started"))
            if (s.Items is [_, SSymbol r, SNumber t, SNumber sol]) RecordStarted(r.Name, t.Value, (int)sol.Value);
        foreach (var m in form.Fields("met"))
            if (m.Items is [_, SSymbol r, SSymbol st, SNumber t, SNumber sol]) RecordMet(r.Name, st.Name, t.Value, (int)sol.Value);
    }
}

public static class RouteReveal
{
    public const string FinalId = "the-call";
    public const string FinalTitle = "Call Earth with a full, warm bank";
    public const string FinalReason = "The relay pass takes the call only from a bank that is full and between 0 and 45 °C; the bank holds {bank-charge-wh|?} of {bank-wh|?} Wh at {bank-temp|?} °C.";

    /// <summary>A pure function of the routes, the history so far and the present reading; it changes nothing.</summary>
    public static RouteView Reveal(IReadOnlyList<Route> routes, RouteHistory history, RouteReading now)
    {
        var shown = new List<ShownRoute>();
        foreach (var route in routes)
        {
            var ever = route.Steps.Select(s => s.IsMet(now) || history.HasMet(route.Id, s.Id)).ToArray();
            if (!Enumerable.Range(0, ever.Length).Any(i => ever[i] && !route.Steps[i].Shared) && !history.IsStarted(route.Id)) continue;   // not started: hidden and unnamed
            int last = Array.FindLastIndex(ever, e => e);
            var unmet = Enumerable.Range(0, ever.Length).Where(i => !ever[i]).Take(2).ToArray();
            var steps = new List<ShownStep>();
            for (int i = 0; i < route.Steps.Count; i++)
            {
                StepStatus? status = i == last ? (route.Steps[i].IsMet(now) ? StepStatus.Met : StepStatus.DoneEarlier)
                    : unmet.Length > 0 && i == unmet[0] ? StepStatus.Next
                    : unmet.Length > 1 && i == unmet[1] ? StepStatus.After : null;
                if (status is { } st) steps.Add(new ShownStep(route.Id, route.Steps[i].Id, route.Steps[i].Title, route.Steps[i].ReasonFor(now), st));
            }
            shown.Add(new ShownRoute(route.Id, route.Name, route.Description, steps));
        }
        return new RouteView(new ShownFinal(FinalId, FinalTitle, RouteText.Fill(FinalReason, now), now.Called || history.HasMet(RouteHistory.FinalRoute, FinalId)), shown);
    }
}

/// <summary>A first time (#233): a route started, or a step met. The rover log carries one line for each.</summary>
public sealed record RouteEvent(string RouteId, string RouteName, string? StepId, string Text, double Time, int Sol);

/// <summary>
/// The routes of a game and what the player has done on them: updated each frame (and between the steps of a sleep) from the scene, it
/// records the first time of every step met and every route started, and answers <see cref="View"/>. The history persists in the world
/// save (<see cref="ToForm"/>, <see cref="Restore"/>; WorldSave.Routes) and starts over with a restarted run, as the path's goals do.
/// </summary>
public sealed class RouteTracker
{
    public IReadOnlyList<Route> Routes { get; private set; } = [];
    public bool Loaded { get; private set; }
    public RouteHistory History { get; } = new();

    public void Load(IReadOnlyList<Route> routes) { Routes = routes; Loaded = true; }

    public void RestartRun() => History.Clear();

    /// <summary>Records what is newly met at <paramref name="time"/> (the scene's clock) and returns it, a route's start before its step.</summary>
    public IReadOnlyList<RouteEvent> Update(RouteReading now, double time, int sol)
    {
        var events = new List<RouteEvent>();
        foreach (var route in Routes)
            foreach (var step in route.Steps)
            {
                if (History.HasMet(route.Id, step.Id) || !step.IsMet(now)) continue;
                if (step.Shared && !History.IsStarted(route.Id)) { History.RecordMet(route.Id, step.Id, time, sol); continue; }   // counts as met, starts and names nothing
                if (!History.IsStarted(route.Id))
                {
                    History.RecordStarted(route.Id, time, sol);
                    events.Add(new RouteEvent(route.Id, route.Name, null, $"Route started: {route.Name} (first step met: {step.Title}).", time, sol));
                }
                History.RecordMet(route.Id, step.Id, time, sol);
                events.Add(new RouteEvent(route.Id, route.Name, step.Id, $"Route step met: {route.Name}, {step.Title}.", time, sol));
            }
        if (now.Called && !History.HasMet(RouteHistory.FinalRoute, RouteReveal.FinalId))
        {
            History.RecordMet(RouteHistory.FinalRoute, RouteReveal.FinalId, time, sol);
            events.Add(new RouteEvent(RouteHistory.FinalRoute, "", RouteReveal.FinalId, $"Route step met: {RouteReveal.FinalTitle}.", time, sol));
        }
        return events;
    }

    public RouteView View(RouteReading now) => RouteReveal.Reveal(Routes, History, now);

    /// <summary>
    /// Playtest fields for a trace: <c>route.ROUTE.started</c> and <c>route.ROUTE.STEP</c>, the scene clock (s) at the first time, -1 before;
    /// <c>route.final.the-call</c> for the call. The trace may name every route; only the panel hides the unstarted ones.
    /// </summary>
    public IReadOnlyDictionary<string, Func<double>> TraceFields()
    {
        var fields = new Dictionary<string, Func<double>>();
        foreach (var route in Routes)
        {
            fields[$"route.{route.Id}.started"] = () => History.StartedAt(route.Id)?.Time ?? -1;
            foreach (var step in route.Steps) fields[$"route.{route.Id}.{step.Id}"] = () => History.MetAt(route.Id, step.Id)?.Time ?? -1;
        }
        fields[$"route.{RouteHistory.FinalRoute}.{RouteReveal.FinalId}"] = () => History.MetAt(RouteHistory.FinalRoute, RouteReveal.FinalId)?.Time ?? -1;
        return fields;
    }

    public SList ToForm() => History.ToForm();
    public void Restore(SList form) => History.Restore(form);
}
