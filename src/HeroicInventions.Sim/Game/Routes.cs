using System.Globalization;
using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Game;

// Routes to the win (issues #227, #228): a route is DATA (game/routes/*.route), a chain of steps whose conditions are read from state.
// A new route is a new file; a new KIND of condition is one line in RouteCondition (and a reading below), so the file never names C#.

/// <summary>One generator as a route step reads it: the sim's own fields (<c>&lt;g&gt;.rpm</c>, <c>&lt;g&gt;.cut-in-rpm</c>), whether it has a bank (ChargeStatus's "unwired"), and the train between its prime mover and its rotor.</summary>
/// <param name="Ratio">rotor over prime mover from the teeth (<c>Generator.Mover.Ratio</c>: 1 for sails on the rotor's own shaft, 256 for four 4:1 stages, null with no prime mover). The teeth, not the speeds, so a stopped sail (a paused sleep) does not hide it.</param>
/// <param name="EstimatedWatts">what it would charge at once loaded, from the present state (<c>&lt;g&gt;.estimated-power</c>); lazy, read only if a reason shows it.</param>
public sealed record GeneratorReading(string Id, double Rpm, double CutInRpm, bool Wired, double? Ratio, Func<double>? EstimatedWatts = null);

/// <summary>One battery bank: <c>&lt;b&gt;.temperature</c>, <c>&lt;b&gt;.full</c>, <c>&lt;b&gt;.capacity</c>, <c>&lt;b&gt;.charge</c>.</summary>
public sealed record BankReading(string Id, double Temperature, bool Full, double CapacityWh, double ChargeWh);

/// <summary>
/// Everything a route's conditions and reasons look at, in the sim's own field terms. Built from the runtimes by <see cref="Read"/>; a test
/// builds one directly from the same field names, so the reveal rule can be tested over a sequence of states without a game.
/// </summary>
/// <param name="BuiltTrains">the gearing of each prime mover's built train, with or without a generator on it (#251): rotor-side speed over the prime mover's, at the fastest part the train reaches (<see cref="BuiltTrainRatio"/>). Used only while no generator has a ratio of its own.</param>
public sealed record RouteReading(int Windmills, IReadOnlyList<GeneratorReading> Generators, IReadOnlyList<BankReading> Banks, bool Called, IReadOnlyList<double>? BuiltTrains = null)
{
    public static readonly RouteReading Empty = new(0, [], [], false);

    /// <summary>
    /// The train ratios the route looks at: each generator's own (the whole train to its rotor, across a shaft link) while any generator has one;
    /// otherwise the built trains', so a windmill and a 256:1 train that drive nothing yet already count (#251). Once a generator is joined its
    /// ratio is the truth, whatever the link adds or takes away.
    /// </summary>
    public IEnumerable<double> TrainRatios => Generators.Any(g => g.Ratio is not null)
        ? Generators.Where(g => g.Ratio is not null).Select(g => g.Ratio!.Value)
        : BuiltTrains ?? [];

    /// <summary>
    /// The gearing of the train that starts at part <paramref name="start"/> of a machine (a prime mover): the greatest ω_part / ω_start over the parts
    /// its arbors, meshes and belts reach, from the teeth (<see cref="MachineRuntime.TrainOf"/>, the same walk a generator's ratio is made with). 1 for
    /// a mover with no train. The speeds do not enter, so a stopped sail does not hide it.
    /// </summary>
    public static double BuiltTrainRatio(MachineRuntime rt, string start) => rt.TrainOf(start).Values.Where(v => v.Ratio > 0).Select(v => v.Ratio).DefaultIfEmpty(1).Max();

    /// <summary>The names a reason template may use, each a number read from this state (null when nothing yet gives it).</summary>
    public static IReadOnlyList<string> NumberNames { get; } = ["cut-in-rpm", "rotor-rpm", "ratio", "wind-watts", "bank-wh", "bank-charge-wh", "bank-temp"];

    public double? Number(string name) => name switch
    {
        "cut-in-rpm" => Generators.Count > 0 ? Generators[0].CutInRpm : null,
        "rotor-rpm" => Generators.Count > 0 ? Generators.Max(g => g.Rpm) : null,
        "ratio" => TrainRatios.Select(x => (double?)x).DefaultIfEmpty(null).Max(),
        "wind-watts" => Generators.Select(g => g.EstimatedWatts?.Invoke() ?? 0).DefaultIfEmpty(0).Max() is var w && w > 0 ? w : null,
        "bank-wh" => Banks.Count > 0 ? Banks[0].CapacityWh : null,
        "bank-charge-wh" => Banks.Count > 0 ? Banks[0].ChargeWh : null,
        "bank-temp" => Banks.Count > 0 ? Banks[0].Temperature : null,
        _ => throw new ArgumentException($"no route number named {name}"),
    };

    /// <summary>Reads the scene (every machine of a world) as it stands.</summary>
    public static RouteReading Read(IReadOnlyList<MachineRuntime> runtimes) => new(
        runtimes.Sum(rt => rt.Windmills.Count),
        runtimes.SelectMany(rt => rt.Generators.Select(kv => (kv.Key, G: kv.Value)))
            .Select(x => new GeneratorReading(x.Key, x.G.Rpm, x.G.CutInRpm, x.G.Bank is not null, x.G.Mover?.Ratio, () => x.G.EstimatePower())).ToList(),
        runtimes.SelectMany(rt => rt.Banks.Select(kv => new BankReading(kv.Key, kv.Value.Temperature, kv.Value.Full, kv.Value.CapacityWh, kv.Value.ChargeWh))).ToList(),
        runtimes.Any(rt => rt.Won),
        runtimes.SelectMany(rt => rt.Windmills.Keys.Concat(rt.WaterWheels.Keys).Concat(rt.JetWheels.Keys).Concat(rt.Stirlings.Keys).Select(id => BuiltTrainRatio(rt, id))).ToList());
}

/// <summary>A condition over a <see cref="RouteReading"/>: a closed set of kinds, named in the route file with their numbers.</summary>
public sealed record RouteCondition(string Kind, IReadOnlyList<double> Args)
{
    /// <summary>Each kind: how many numbers it takes, and the reading it consumes (the doc string is the one place that says so).</summary>
    public static IReadOnlyDictionary<string, (int Args, string Reads)> Kinds { get; } = new Dictionary<string, (int, string)>
    {
        ["windmill-exists"] = (0, "a windmill part exists in some machine of the scene (MachineRuntime.Windmills)"),
        ["train-ratio-at-least"] = (1, "a prime mover is geared up at N:1 or more: a generator's own train (Generator.Mover.Ratio), or, while no generator has one, the built train's fastest part (MachineRuntime.TrainOf; the teeth, not the present speeds)"),
        ["rotor-over-cut-in"] = (0, "a generator's rotor is above its cut-in (<generator>.rpm > <generator>.cut-in-rpm)"),
        ["generator-wired"] = (0, "a generator has a bank, by a wire link or built beside it (Generator.Bank; ChargeStatus's 'unwired')"),
        ["bank-temperature-between"] = (2, "a bank is between LO and HI degrees C (<bank>.temperature)"),
        ["bank-full"] = (0, "a bank is full (<bank>.full)"),
        ["called"] = (0, "the call went out (scene.won)"),
    };

    public bool Holds(RouteReading r) => Kind switch
    {
        "windmill-exists" => r.Windmills > 0,
        "train-ratio-at-least" => r.TrainRatios.Any(x => x >= Args[0]),
        "rotor-over-cut-in" => r.Generators.Any(g => g.Rpm > g.CutInRpm),
        "generator-wired" => r.Generators.Any(g => g.Wired),
        "bank-temperature-between" => r.Banks.Any(b => b.Temperature >= Args[0] && b.Temperature <= Args[1]),
        "bank-full" => r.Banks.Any(b => b.Full),
        "called" => r.Called,
        _ => throw new InvalidOperationException($"unknown condition kind {Kind}"),
    };
}

/// <summary>A step: an id, a title, a reason template, and conditions of which any one meets it. The template may carry <c>{name}</c> or <c>{name|fallback}</c>, a number read from state (<see cref="RouteReading.NumberNames"/>).</summary>
public sealed record RouteStep(string Id, string Title, string Reason, IReadOnlyList<RouteCondition> Alternatives, bool Shared = false)
{
    public bool IsMet(RouteReading r) => Alternatives.Any(c => c.Holds(r));
    public string ReasonFor(RouteReading r) => RouteText.Fill(Reason, r);
}

/// <summary>A route: its name, one line, the test that proves it wins (#231), and its steps in order. The final goal (the call) is not a step; it is always shown.</summary>
public sealed record Route(string Id, string Name, string Description, string ProvenBy, IReadOnlyList<RouteStep> Steps)
{
    /// <summary>Reads one route file. A malformed one fails with the file and the step named.</summary>
    public static Route Parse(string text, string file)
    {
        IReadOnlyList<SExpr> forms;
        try { forms = SExprReader.ReadAll(text); }
        catch (FormatException e) { throw new MachineFormatException($"{file}: {e.Message}"); }
        if (forms is not [SList { Head: "route" } root] || root.Items.ElementAtOrDefault(1) is not SSymbol id)
            throw new MachineFormatException($"{file}: expected one (route ID ...) form");
        string Str(SList from, string field, string where)
            => from.Field(field)?.Items is [_, SString s] && s.Value.Trim().Length > 0 ? s.Value : throw new MachineFormatException($"{file}: {where} needs ({field} \"text\")");
        string rw = $"route {id.Name}";
        var steps = new List<RouteStep>();
        foreach (var item in root.Items.Skip(2))
        {
            if (item is not SList { Head: var head } form) throw new MachineFormatException($"{file}: {rw}: unexpected {SExprWriter.Print(item)}");
            if (head is "name" or "description" or "proven-by") continue;
            if (head != "step") throw new MachineFormatException($"{file}: {rw}: unknown form ({head} ...); a route has name, description, proven-by and steps");
            if (form.Items.ElementAtOrDefault(1) is not SSymbol sid) throw new MachineFormatException($"{file}: {rw}: a step is (step ID (title ...) (reason ...) (met ...))");
            string where = $"{rw} step {sid.Name}";
            if (steps.Any(s => s.Id == sid.Name)) throw new MachineFormatException($"{file}: {where}: the id is used twice");
            if (sid.Name == "the-call") throw new MachineFormatException($"{file}: {where}: the call is the final goal and always shown; it is not a step");
            string reason = Str(form, "reason", where);
            RouteText.Validate(reason, file, where);
            if (form.Field("met") is not { Items.Count: > 1 } met) throw new MachineFormatException($"{file}: {where} needs (met CONDITION ...), any one of which meets it");
            var alternatives = met.Items.Skip(1).Select(c => ParseCondition(c, file, where)).ToList();
            bool shared = form.Field("shared") switch { null => false, { Items: [_, SBool b] } => b.Value, _ => throw new MachineFormatException($"{file}: {where}: shared is (shared #t) or (shared #f)") };
            steps.Add(new RouteStep(sid.Name, Str(form, "title", where), reason, alternatives, shared));
        }
        if (steps.Count > 0 && steps.All(s => s.Shared)) throw new MachineFormatException($"{file}: {rw} has only shared steps, so nothing could ever start it");
        if (steps.Count == 0) throw new MachineFormatException($"{file}: {rw} has no steps");
        return new Route(id.Name, Str(root, "name", rw), Str(root, "description", rw), Str(root, "proven-by", rw), steps);
    }

    private static RouteCondition ParseCondition(SExpr c, string file, string where)
    {
        if (c is not SList { Head: { } kind } list) throw new MachineFormatException($"{file}: {where}: a condition is (KIND numbers...), got {SExprWriter.Print(c)}");
        if (!RouteCondition.Kinds.TryGetValue(kind, out var spec))
            throw new MachineFormatException($"{file}: {where}: no condition named {kind}; they are: {string.Join(", ", RouteCondition.Kinds.Keys)}");
        var args = list.Items.Skip(1).ToList();
        if (args.Count != spec.Args || args.Any(a => a is not SNumber))
            throw new MachineFormatException($"{file}: {where}: {kind} takes {spec.Args} number(s), got {SExprWriter.Print(list)}");
        var nums = args.Select(a => ((SNumber)a).Value).ToList();
        if (kind == "train-ratio-at-least" && !(nums[0] > 0)) throw new MachineFormatException($"{file}: {where}: a ratio must be above 0");
        if (kind == "bank-temperature-between" && !(nums[0] < nums[1])) throw new MachineFormatException($"{file}: {where}: bank-temperature-between needs LO under HI");
        return new RouteCondition(kind, nums);
    }
}

/// <summary>Reason templates: <c>{name}</c> or <c>{name|fallback}</c>, filled from a <see cref="RouteReading"/>.</summary>
public static class RouteText
{
    private static IEnumerable<(int Start, int End, string Name, string? Fallback)> Holes(string template)
    {
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] != '{') continue;
            int end = template.IndexOf('}', i);
            if (end < 0) throw new FormatException("a { is never closed");
            string inner = template[(i + 1)..end];
            int bar = inner.IndexOf('|');
            yield return (i, end, bar < 0 ? inner : inner[..bar], bar < 0 ? null : inner[(bar + 1)..]);
            i = end;
        }
    }

    public static void Validate(string template, string file, string where)
    {
        try
        {
            foreach (var h in Holes(template))
                if (!RouteReading.NumberNames.Contains(h.Name))
                    throw new MachineFormatException($"{file}: {where}: the reason uses {{{h.Name}}}; the numbers it may use are: {string.Join(", ", RouteReading.NumberNames)}");
        }
        catch (FormatException e) { throw new MachineFormatException($"{file}: {where}: reason: {e.Message}"); }
    }

    public static string Fill(string template, RouteReading r)
    {
        var sb = new System.Text.StringBuilder();
        int at = 0;
        foreach (var h in Holes(template))
        {
            sb.Append(template, at, h.Start - at);
            sb.Append(r.Number(h.Name) is { } v ? Format(v) + Unit(h.Name) : h.Fallback ?? "?");
            at = h.End + 1;
        }
        return sb.Append(template, at, template.Length - at).ToString();
    }

    /// <summary>What a number carries after it, inside its own hole so a fallback has none: a ratio is written N:1 (#251), so "{ratio|not there yet}" reads "256:1" or "not there yet", never "not there yet:1".</summary>
    private static string Unit(string name) => name == "ratio" ? ":1" : "";

    public static string Format(double v) => Math.Abs(v) >= 100 ? v.ToString("#,0", CultureInfo.InvariantCulture) : v.ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>The routes a game ships: the files of <c>game/routes</c> (and a world's own folder), parsed together.</summary>
public static class RouteLibrary
{
    public static IReadOnlyList<Route> Parse(IEnumerable<(string File, string Text)> files)
    {
        var routes = new List<Route>();
        foreach (var (file, text) in files)
        {
            var route = Route.Parse(text, file);
            if (routes.Any(r => r.Id == route.Id)) throw new MachineFormatException($"{file}: route {route.Id} is already defined by another file");
            routes.Add(route);
        }
        return routes;
    }
}
