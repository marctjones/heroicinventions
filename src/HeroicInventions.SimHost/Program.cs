using System.Globalization;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

// One command per stdin line, one reply per stdout line, both plain
// S-expressions — the same vocabulary LiveLinkServer.cs speaks to a
// running game, but here there is no game: this process only ever
// touches HeroicInventions.Sim, so it can run in `raco test` or CI
// without Godot installed.
//
// Command:  (simulate "<path-to>.machine" <seconds> <step> <sample-dt> [(set (<target> <field> <value> [<at-seconds>]) ...)]
//                     [(save "<path>" <at-seconds>)] [(resume "<path>")] [(actions "<path>")])
//           (actions ...) replays an operator log (issue #153): a file of (at <seconds> (<target> <field> <value>)) forms, applied as timed settings.
//           (save ...) writes the running state to a save file (issue #67) when the clock reaches <at-seconds>; (resume ...) lays a saved
//           state on the machine before the first step, so the run carries on from where the save left off.
// Reply:    (run (<time> (<target.field> <value>) ...) (<time> ...) ...)
//        or (error "<message>")
//
// Command:  (sleep "<path-to>.machine" <step> <wake-id | (terms (<target> <field> above|below <value>) ...)>
//                  [(join and|or)] [(limit <seconds>)] [(events (<target> <field> above|below <value>) ...)] [(set (<target> <field> <value>) ...)])
//           Sleeps the machine until the condition (issue #59): steps it ahead at <step> as fast as it will go, stopping at the first of an
//           event, the condition, or the limit.
// Reply:    (slept <condition|event|limit> <elapsed-seconds> "<detail>" <steps> <predicted-seconds-or-#f> "<prediction note>" (<time> (<target.field> <value>) ...))
var materials = MaterialLibrary.LoadDefault();

string? line;
while ((line = Console.In.ReadLine()) is not null)
{
    if (line.Trim().Length == 0) continue;
    string reply;
    try
    {
        reply = Handle(line, materials);
    }
    catch (Exception e)
    {
        reply = $"(error {Quote(e.Message)})";
    }
    Console.Out.Write(reply);
    Console.Out.Write('\n');
    Console.Out.Flush();
}

static string Handle(string line, MaterialLibrary materials)
{
    var forms = SExprReader.ReadAll(line);
    if (forms is not [SList form]) throw new FormatException("expected exactly one command form per line");
    if (form.Head == "sleep") return Sleep(form, materials);
    if (form.Head != "simulate")
        throw new FormatException($"unknown command '{form.Head}'; (simulate ...) and (sleep ...) are supported");

    var items = form.Items;
    if (items.Count < 5 || items[1] is not SString path || items[2] is not SNumber seconds
        || items[3] is not SNumber step || items[4] is not SNumber sampleDt)
        throw new FormatException("usage: (simulate \"<path>.machine\" <seconds> <step> <sample-dt> [(set (<target> <field> <value> [<at-seconds>]) ...)] [(save \"<path>\" <at-seconds>)] [(resume \"<path>\")])");
    var options = items.Skip(5).OfType<SList>().ToList();
    var resume = options.FirstOrDefault(o => o.Head == "resume")?.Items.ElementAtOrDefault(1) as SString;
    var saveOption = options.FirstOrDefault(o => o.Head == "save");
    string? savePath = (saveOption?.Items.ElementAtOrDefault(1) as SString)?.Value;
    double saveAt = (saveOption?.Items.ElementAtOrDefault(2) as SNumber)?.Value ?? double.PositiveInfinity;
    bool saved = false;
    if (step.Value <= 0) throw new FormatException("step must be positive");
    if (sampleDt.Value <= 0) throw new FormatException("sample-dt must be positive");

    var def = MachineDef.Parse(File.ReadAllText(path.Value));
    var run = new MachineRuntime(def, materials);
    // Settings — what the game's engine side or a player would otherwise
    // supply (a screw's turning speed, a fire) — applied before the first
    // step, or once the clock reaches <at-seconds> (a player's hand on a tap).
    var pending = new List<(string Target, string Field, double Value, double At)>();
    if (resume is not null)
    {
        var save = WorldSave.Read(resume.Value);
        var machine = save.Machines.FirstOrDefault(m => m.Machine == def.Name) ?? throw new FormatException($"the save holds no machine called {def.Name}");
        RuntimeState.Restore(run, machine.State);
    }
    if (options.FirstOrDefault(o => o.Head == "set") is { } setList)
        foreach (var setting in setList.Items.Skip(1).OfType<SList>())
            pending.Add(setting.Items switch
            {
                [SSymbol target, SSymbol field, SNumber value] => (target.Name, field.Name, value.Value, 0),
                [SSymbol target, SSymbol field, SNumber value, SNumber at] => (target.Name, field.Name, value.Value, at.Value),
                _ => throw new FormatException("each setting is (target field value [at-seconds])"),
            });
    // an operator log to replay: the same timed settings, in the form the game records them (issue #153)
    if (options.FirstOrDefault(o => o.Head == "actions")?.Items.ElementAtOrDefault(1) is SString actionsPath)
        pending.AddRange(OperatorLog.Parse(File.ReadAllText(actionsPath.Value)).Select(a => (a.Target, a.Field, a.Value, a.At)));
    void SaveDue()
    {
        if (savePath is null || saved || run.Time + 1e-9 < saveAt) return;
        WorldSave.OfMachine(def.Name, run).WriteAtomic(savePath);
        saved = true;
    }
    void ApplyDue()
    {
        SaveDue();
        foreach (var due in pending.Where(p => p.At <= run.Time + 1e-9).ToList())
        {
            run.SetField(due.Target, due.Field, due.Value);
            pending.Remove(due);
        }
    }
    ApplyDue();

    int totalSteps = (int)Math.Round(seconds.Value / step.Value);
    var frames = new List<string>(capacity: (int)(seconds.Value / sampleDt.Value) + 2);
    double nextSampleAt = 0;
    for (int i = 0; i <= totalSteps; i++)
    {
        // by step count, not the summed clock: 36 000 steps of 0.1 s add up to a
        // hair under 3600, and a frame due at 3600 must not be skipped for it
        if (i * step.Value + step.Value / 2 >= nextSampleAt)
        {
            frames.Add(FrameOf(run));
            nextSampleAt += sampleDt.Value;
        }
        if (i < totalSteps) run.Step(step.Value);
        ApplyDue();
    }
    return $"(run {string.Join(' ', frames)})";
}

static string Sleep(SList form, MaterialLibrary materials)
{
    var items = form.Items;
    if (items.Count < 4 || items[1] is not SString path || items[2] is not SNumber step)
        throw new FormatException("usage: (sleep \"<path>.machine\" <step> <wake-id | (terms (<target> <field> above|below <value>) ...)> [(join and|or)] [(limit <seconds>)] [(events ...)] [(set ...)])");
    var def = MachineDef.Parse(File.ReadAllText(path.Value));
    var run = new MachineRuntime(def, materials);
    foreach (var setting in items.Skip(3).OfType<SList>().Where(l => l.Head == "set").SelectMany(l => l.Items.Skip(1).OfType<SList>()))
        if (setting.Items is [SSymbol target, SSymbol field, SNumber value]) run.SetField(target.Name, field.Name, value.Value);
    static IReadOnlyList<WakeTerm> Terms(IEnumerable<SExpr> list) => list.OfType<SList>()
        .Select(t => t.Items is [SSymbol target, SSymbol field, SSymbol { Name: "above" or "below" } mode, SNumber value]
            ? new WakeTerm(target.Name, field.Name, mode.Name == "above", value.Value)
            : throw new FormatException("each term is (target field above|below value)")).ToList();

    WakeSpec plan;
    if (items[3] is SSymbol id)
        plan = def.Wakes.FirstOrDefault(w => w.Id == id.Name) ?? throw new FormatException($"the machine has no wake called {id.Name}");
    else if (items[3] is SList { Head: "terms" } terms)
    {
        var options = items.Skip(4).OfType<SList>().ToList();
        plan = new WakeSpec("sleep", Terms(terms.Items.Skip(1)),
            options.FirstOrDefault(o => o.Head == "join")?.Items.ElementAtOrDefault(1) is not SSymbol { Name: "or" },
            options.FirstOrDefault(o => o.Head == "limit")?.Items.ElementAtOrDefault(1) is SNumber l ? l.Value : 3600,
            Terms(options.FirstOrDefault(o => o.Head == "events")?.Items.Skip(1) ?? []));
    }
    else throw new FormatException("the wake condition is a wake id or (terms ...)");

    var prediction = SleepPlanner.Predict(run, plan);
    var result = SleepSession.FastForward(run, plan, step.Value);
    string reason = result.Reason switch { WakeReason.Condition => "condition", WakeReason.Event => "event", _ => "limit" };
    return $"(slept {reason} {Num(result.Elapsed)} {Quote(result.Detail)} {result.Steps} {(prediction.Seconds is { } p ? Num(p) : "#f")} {Quote(prediction.Note)} {FrameOf(run)})";
}

static string FrameOf(MachineRuntime run)
{
    var entries = run.FieldGetters
        .Select(kv => (kv.Key, Value: SafeRead(kv.Value)))
        .Where(e => e.Value is not null)
        .Select(e => $"({e.Key} {Num(e.Value!.Value)})");
    return $"({Num(run.Time)} {string.Join(' ', entries)})";
}

// A getter can throw or return NaN/Infinity in a transient state (e.g. a
// pipe with no flow yet); skip it for this frame rather than emit a token
// Racket's reader can't parse back.
static double? SafeRead(Func<double> get)
{
    try
    {
        double v = get();
        return double.IsFinite(v) ? v : null;
    }
    catch
    {
        return null;
    }
}

// "R"/"G17" round-trip exactly but may use an uppercase E exponent marker,
// which Racket's reader rejects — lowercase it.
static string Num(double v) => v.ToString("G17", CultureInfo.InvariantCulture).Replace("E", "e");
static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
