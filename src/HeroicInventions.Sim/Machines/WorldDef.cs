namespace HeroicInventions.Sim.Machines;

/// <summary>One machine placed in a world: a label unique in the world (the same machine can be placed many times), the machine's name, and where its origin goes.</summary>
public sealed record Placement(string Label, string Machine, Vec3 At, SourceLocation? Location)
{
    /// <summary>The machine's heading (issue #83): degrees turned about the vertical through its placement point, counter-clockwise seen from above.</summary>
    public double Heading { get; init; }
}

/// <summary>One end of a link between machines: a placement's label, a part of its machine, and (for a pipe) the part's port.</summary>
public sealed record LinkEnd(string Label, string Part, string? Port = null)
{
    public override string ToString() => Port is null ? $"{Label}.{Part}" : $"{Label}.{Part}.{Port}";
}

/// <summary>
/// A link between parts of two different machines in a world (issue #78):
/// a <c>pipe</c> between two tanks' ports, carrying water by the same law as a
/// pipe inside one machine (flow = conductance × head difference), or a
/// <c>shaft</c> between two things turning on axles, making the driven end
/// turn at <see cref="Ratio"/> times the driving end's speed.
/// </summary>
public sealed record LinkSpec(string Id, string Kind, LinkEnd From, LinkEnd To, SourceLocation? Location = null)
{
    /// <summary>A pipe's conductance, m³/s per metre of head.</summary>
    public double Conductance { get; init; } = 1e-3;
    /// <summary>A shaft's ratio: the To end turns at Ratio × the From end's speed (1, a plain shaft; other values, a gearbox).</summary>
    public double Ratio { get; init; } = 1;
}

/// <summary>Where the player's rover starts in a world (issue #94): a point of the ground and a heading, degrees about the vertical.</summary>
public sealed record RoverStart(double X, double Z, double Heading);

/// <summary>
/// The game's tuning numbers (issue #60): numbers a scenario feeds into the sim's unchanged formulas, never a change to a formula.
/// The labelled rate multipliers are 1 on real Mars (every one scales one input of one formula, so a tuned run states its numbers
/// and the formula still gives the traced result); the Advanced numbers are the physical constants themselves, null where the
/// machine's own (or the planet's) stands, and risky: they carry the game's lessons. A tuning with every number at its default is
/// <see cref="IsReal"/>, and a run with it is the untuned run exactly.
/// <code>
/// (scenario (title "…") (description "…")
///   (tuning (wear 0.25) (evaporation 5) (bank-capacity 0.2) (generator-cut-in 0.5) (call-window 6) (call-any-time #t) (sleep-speed 4))
///   (advanced (gravity 9.81) (generator-efficiency 0.9) (bank-min-charge-c -10) (bank-max-charge-c 55)))
/// </code>
/// </summary>
public sealed record ScenarioTuning
{
    // ---- labelled rate multipliers (1 = real) ----
    /// <summary>x the specific wear rate of every bearing (mm3 per N.m): 0.25, bearings last four times as long.</summary>
    public double Wear { get; init; } = 1;
    /// <summary>x the rain-house's evaporation coefficient (kg per s per m2 per Pa).</summary>
    public double Evaporation { get; init; } = 1;
    /// <summary>x the battery bank's capacity (Wh); a small bank fills sooner.</summary>
    public double BankCapacity { get; init; } = 1;
    /// <summary>x the generator's cut-in speed (rpm); its rated speed stays, and is lifted over the cut-in if the product passes it.</summary>
    public double GeneratorCutIn { get; init; } = 1;
    /// <summary>x the length of the relay pass the call goes out on, minutes.</summary>
    public double CallWindow { get; init; } = 1;
    /// <summary>The easy setting: the call may go at any hour once the bank is full and warm enough.</summary>
    public bool CallAnyTime { get; init; }
    /// <summary>x the computing time a sleep gets each frame (it runs ahead faster, and takes the same steps, so the result is the same).</summary>
    public double SleepSpeed { get; init; } = 1;

    // ---- Advanced: the constants (null = as the machine or planet has it) ----
    /// <summary>m/s2 for every machine in the world (Earth's 9.81 on Mars's ground: what Mars's slow falls and big cliffs teach goes with it).</summary>
    public double? Gravity { get; init; }
    /// <summary>The generator's eta, 0 to 1: the single number for all losses between shaft and bank.</summary>
    public double? GeneratorEfficiency { get; init; }
    /// <summary>Degrees C below which the bank takes no charge (lithium plating), default 0.</summary>
    public double? BankMinChargeC { get; init; }
    /// <summary>Degrees C above which the bank takes no charge, default 45.</summary>
    public double? BankMaxChargeC { get; init; }

    public static ScenarioTuning Real { get; } = new();

    /// <summary>Every number is its real one.</summary>
    public bool IsReal => this == Real;
    /// <summary>One of the Advanced (risky) numbers has been set.</summary>
    public bool HasAdvanced => Gravity is not null || GeneratorEfficiency is not null || BankMinChargeC is not null || BankMaxChargeC is not null;

    /// <summary>A label for each tuning number: key (as written in the file), name, unit, real value, and whether it is Advanced (risky).</summary>
    public sealed record Entry(string Key, string Label, string Unit, double Real, bool Advanced, string Note);

    public static IReadOnlyList<Entry> Entries { get; } =
    [
        new("wear", "Bearing wear", "x", 1, false, "bearings wear at this multiple of their real rate"),
        new("evaporation", "Evaporation", "x", 1, false, "rain-house water evaporates at this multiple of the real rate"),
        new("bank-capacity", "Battery bank capacity", "x", 1, false, "the bank holds this multiple of its capacity (a smaller bank fills sooner)"),
        new("generator-cut-in", "Generator cut-in speed", "x", 1, false, "the motor starts charging at this multiple of 1,500 rpm"),
        new("call-window", "Relay pass length", "x", 1, false, "the call window lasts this multiple of the pass's minutes"),
        new("call-any-time", "Call at any hour", "0/1", 0, false, "1: the call may go out whenever the bank is full and warm, not only on the pre-dawn pass"),
        new("sleep-speed", "Sleep speed", "x", 1, false, "sleep gets this multiple of the computing time per frame; the steps and the result are the same"),
        new("gravity", "Gravity", "m/s2", double.NaN, true, "risky: the planet's own g is what the game teaches; blank keeps it"),
        new("generator-efficiency", "Generator efficiency", "0 to 1", double.NaN, true, "risky: eta for all losses between shaft and bank; blank keeps the machine's own (0.8)"),
        new("bank-min-charge-c", "Bank lowest charging temperature", "C", 0, true, "risky: below this the bank takes no charge (real lithium cells plate)"),
        new("bank-max-charge-c", "Bank highest charging temperature", "C", 45, true, "risky: above this the bank takes no charge"),
    ];

    /// <summary>A number by its key; NaN for an Advanced number left unset.</summary>
    public double Get(string key) => key switch
    {
        "wear" => Wear, "evaporation" => Evaporation, "bank-capacity" => BankCapacity, "generator-cut-in" => GeneratorCutIn,
        "call-window" => CallWindow, "call-any-time" => CallAnyTime ? 1 : 0, "sleep-speed" => SleepSpeed,
        "gravity" => Gravity ?? double.NaN, "generator-efficiency" => GeneratorEfficiency ?? double.NaN,
        "bank-min-charge-c" => BankMinChargeC ?? 0, "bank-max-charge-c" => BankMaxChargeC ?? 45,
        _ => throw new ArgumentException($"no tuning number named {key}"),
    };

    /// <summary>The same tuning with one number set; NaN clears an Advanced number back to the machine's own.</summary>
    public ScenarioTuning With(string key, double v) => key switch
    {
        "wear" => this with { Wear = v }, "evaporation" => this with { Evaporation = v }, "bank-capacity" => this with { BankCapacity = v },
        "generator-cut-in" => this with { GeneratorCutIn = v }, "call-window" => this with { CallWindow = v },
        "call-any-time" => this with { CallAnyTime = v != 0 }, "sleep-speed" => this with { SleepSpeed = v },
        "gravity" => this with { Gravity = double.IsNaN(v) ? null : v }, "generator-efficiency" => this with { GeneratorEfficiency = double.IsNaN(v) ? null : v },
        "bank-min-charge-c" => this with { BankMinChargeC = v == 0 ? null : v }, "bank-max-charge-c" => this with { BankMaxChargeC = v == 45 ? null : v },   // the real limits are the unset ones
        _ => throw new ArgumentException($"no tuning number named {key}"),
    };

    /// <summary>Numbers that can still be changed once a game is running: the ones the sim reads each tick through a settable field.</summary>
    public static bool ChangeableInPlay(string key) => key is "call-any-time" or "call-window" or "sleep-speed";

    /// <summary>The scenario's own <c>(tuning …)</c> and <c>(advanced …)</c> forms; a number out of its range is refused.</summary>
    public static ScenarioTuning FromForms(SList? tuning, SList? advanced, string file)
    {
        var t = Real;
        void Read(SList? form, bool advancedForm)
        {
            if (form is null) return;
            foreach (var item in form.Items.Skip(1))
            {
                if (item is not SList { Items: [SSymbol key, var value] })
                    throw new MachineFormatException($"{file}: scenario {(advancedForm ? "advanced" : "tuning")} entries are (NAME NUMBER), got {SExprWriter.Print(item)}");
                var entry = Entries.FirstOrDefault(e => e.Key == key.Name && e.Advanced == advancedForm)
                    ?? throw new MachineFormatException($"{file}: scenario {(advancedForm ? "advanced" : "tuning")} has no number named {key.Name}; they are: {string.Join(", ", Entries.Where(e => e.Advanced == advancedForm).Select(e => e.Key))}");
                double v = value switch
                {
                    SNumber n => n.Value,
                    SBool b => b.Value ? 1 : 0,
                    _ => throw new MachineFormatException($"{file}: scenario {key.Name} needs a number"),
                };
                if (!double.IsFinite(v)) throw new MachineFormatException($"{file}: scenario {key.Name} must be a finite number");
                bool multiplier = !entry.Advanced && entry.Unit == "x";
                if (multiplier && !(v > 0)) throw new MachineFormatException($"{file}: scenario {key.Name} is a multiplier and must be above 0");
                if (key.Name == "generator-efficiency" && !(v > 0 && v <= 1)) throw new MachineFormatException($"{file}: scenario generator-efficiency must be in (0, 1]");
                if (key.Name == "gravity" && !(v > 0)) throw new MachineFormatException($"{file}: scenario gravity must be above 0");
                t = t.With(key.Name, v);
            }
        }
        Read(tuning, false);
        Read(advanced, true);
        if (t.BankMinChargeC is { } lo && t.BankMaxChargeC is { } hi && !(lo < hi))
            throw new MachineFormatException($"{file}: scenario bank-min-charge-c must be under bank-max-charge-c");
        return t;
    }

    /// <summary>The forms that write this tuning back (only what differs from real), or none.</summary>
    public IEnumerable<string> ToForms()
    {
        string N(double v) => SExprWriter.Number(v);
        var basic = Entries.Where(e => !e.Advanced && Get(e.Key) != e.Real).Select(e => e.Key == "call-any-time" ? "(call-any-time #t)" : $"({e.Key} {N(Get(e.Key))})").ToList();
        if (basic.Count > 0) yield return "(tuning " + string.Join(" ", basic) + ")";
        var adv = Entries.Where(e => e.Advanced && !double.IsNaN(Get(e.Key)) && (double.IsNaN(e.Real) || Get(e.Key) != e.Real)).Select(e => $"({e.Key} {N(Get(e.Key))})").ToList();
        if (adv.Count > 0) yield return "(advanced " + string.Join(" ", adv) + ")";
    }
}

/// <summary>
/// A scenario (issue #60): what a world is for. It names itself for the front end's list (<see cref="Title"/>, <see cref="Description"/>) and
/// carries the tuning numbers its machines are built with. <c>(scenario (title "…") (description "…") (tuning …) (advanced …))</c>; see
/// <see cref="ScenarioTuning"/>. The planet and the map stay the world's own, so any map can sit under any scenario.
/// </summary>
public sealed record ScenarioDef(string Title, string Description)
{
    public ScenarioTuning Tuning { get; init; } = ScenarioTuning.Real;

    public static ScenarioDef Parse(SList form, string file)
    {
        string Text(string field) => form.Field(field)?.Items.ElementAtOrDefault(1) is SString s ? s.Value : "";
        return new ScenarioDef(Text("title"), Text("description")) { Tuning = ScenarioTuning.FromForms(form.Field("tuning"), form.Field("advanced"), file) };
    }

    public string Write()
    {
        static string Q(string s) => SExprWriter.Print(new SString(s));
        var parts = new List<string> { $"(title {Q(Title)})", $"(description {Q(Description)})" };
        parts.AddRange(Tuning.ToForms());
        return "(scenario " + string.Join("\n    ", parts) + ")";
    }
}

/// <summary>
/// A world (issue #74): many machines standing in one scene, each an
/// ordinary machine moved into place with <see cref="MachineDef.Translated"/>.
/// Written in the same s-expression shape as a .machine file:
/// <code>
/// (world pendulums
///   (place p1 pendulum-demo (at 0 0 0))
///   (place p2 pendulum-demo (at 2 0 0)))
/// </code>
/// A placement may carry a heading, <c>(heading 30)</c>: degrees turned about
/// the vertical through its <c>at</c> point (issue #83), by
/// <see cref="MachineDef.Turned"/>. A machine with parts that can't be turned
/// (anything built along the axes) refuses it rather than ignore it.
/// </summary>
public sealed class WorldDef
{
    /// <summary>
    /// Where the player's rover stands when the world opens (issue #94): <c>(rover (at X Z) (heading D))</c>, on the ground
    /// at X, Z, turned D degrees about the vertical (0 faces -z, 270 faces +x). A world with one is the game: the player
    /// drives the rover there (game/scripts/Main.Rover.cs); a machine run has none, and keeps the free operator.
    /// </summary>
    public RoverStart? Rover { get; init; }

    /// <summary>
    /// What this world is for (issue #60): <c>(scenario (title "…") (description "…") …tuning…)</c>. Null for a world that says nothing
    /// (it runs on the real numbers). The front end lists scenarios by <see cref="ScenarioDef.Title"/> and <see cref="ScenarioDef.Description"/>.
    /// </summary>
    public ScenarioDef? Scenario { get; init; }

    public required string Name { get; init; }
    public required IReadOnlyList<Placement> Placements { get; init; }
    /// <summary>The ground the world stands on (issue #37): a map's name (game/maps/NAME.map), or null for the flat floor.</summary>
    public string? Map { get; init; }

    /// <summary>
    /// A placed machine's definition, moved into place: on a map its (at x y z)
    /// is taken as y metres above the ground there, so a machine stands on the
    /// hillside it is put on; with no map, as written. The machine is moved
    /// as a whole, not bent to the ground's shape.
    /// </summary>
    public static MachineDef Placed(MachineDef def, Placement p, Fluids.Terrain? ground) =>
        def.Turned(p.Heading).Translated(new Vec3(p.At.X, p.At.Y + (ground?.HeightAt(p.At.X, p.At.Z) ?? 0), p.At.Z));

    /// <summary>Pipes and shafts joining parts of different machines, resolved after every machine is built.</summary>
    public IReadOnlyList<LinkSpec> Links { get; init; } = [];

    public static readonly string[] LinkKinds = ["pipe", "shaft"];

    /// <summary>The same world with one more link (its id must be new).</summary>
    public WorldDef WithLink(LinkSpec link)
    {
        if (Links.Any(l => l.Id == link.Id)) throw new MachineFormatException($"world {Name} already has a link named {link.Id}");
        CheckLink(link, Placements, null);
        return new WorldDef { Name = Name, Map = Map, Rover = Rover, Scenario = Scenario, Placements = Placements, Links = [.. Links, link] };
    }

    /// <summary>The same world without the named link.</summary>
    public WorldDef WithoutLink(string id) =>
        new() { Name = Name, Map = Map, Rover = Rover, Scenario = Scenario, Placements = Placements, Links = Links.Where(l => l.Id != id).ToList() };

    /// <summary>A link id not yet used in this world: pipe-1, pipe-2, …</summary>
    public string NextLinkId(string stem)
    {
        for (int i = 1; ; i++)
            if (Links.All(l => l.Id != $"{stem}-{i}")) return $"{stem}-{i}";
    }

    /// <summary>The world written back in the shape <see cref="Parse"/> reads.</summary>
    public string Write()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"(world {Name}");
        if (Map is not null) sb.Append($"\n  (map {Map})");
        if (Scenario is { } sc) sb.Append("\n  " + sc.Write());
        if (Rover is { } r)
            sb.Append($"\n  (rover (at {SExprWriter.Number(r.X)} {SExprWriter.Number(r.Z)})" + (r.Heading != 0 ? $" (heading {SExprWriter.Number(r.Heading)})" : "") + ")");
        foreach (var p in Placements)
            sb.Append($"\n  (place {p.Label} {p.Machine} (at {SExprWriter.Number(p.At.X)} {SExprWriter.Number(p.At.Y)} {SExprWriter.Number(p.At.Z)})"
                      + (p.Heading != 0 ? $" (heading {SExprWriter.Number(p.Heading)})" : "") + ")");
        foreach (var l in Links)
        {
            static string End(LinkEnd e) => e.Port is null ? $"{e.Label} {e.Part}" : $"{e.Label} {e.Part} {e.Port}";
            sb.Append($"\n  (link {l.Id} {l.Kind} (from {End(l.From)}) (to {End(l.To)})");
            sb.Append(l.Kind == "pipe" ? $" (conductance {SExprWriter.Number(l.Conductance)}))" : $" (ratio {SExprWriter.Number(l.Ratio)}))");
        }
        sb.Append(")\n");
        return sb.ToString();
    }

    private static void CheckLink(LinkSpec l, IReadOnlyList<Placement> placements, string? file)
    {
        string at = file is null ? "" : $"{file}: ";
        if (!LinkKinds.Contains(l.Kind))
            throw new MachineFormatException($"{at}link {l.Id}: a link is a pipe or a shaft, not {l.Kind}", l.Location);
        foreach (var e in new[] { l.From, l.To })
            if (placements.All(p => p.Label != e.Label))
                throw new MachineFormatException($"{at}link {l.Id}: no machine is placed as {e.Label}", l.Location);
        if (l.From.Label == l.To.Label)
            throw new MachineFormatException($"{at}link {l.Id} joins {l.From.Label} to itself; parts of one machine are joined in the machine", l.Location);
        if (l.Kind == "pipe" && (l.From.Port is null || l.To.Port is null))
            throw new MachineFormatException($"{at}link {l.Id}: a pipe runs between two ports: (from LABEL TANK PORT) (to LABEL TANK PORT)", l.Location);
        if (l.Kind == "pipe" && !(l.Conductance > 0))
            throw new MachineFormatException($"{at}link {l.Id}: a pipe's conductance must be more than 0", l.Location);
        if (l.Kind == "shaft" && (l.Ratio == 0 || !double.IsFinite(l.Ratio)))
            throw new MachineFormatException($"{at}link {l.Id}: a shaft's ratio must be a number other than 0", l.Location);
    }

    public static WorldDef Parse(string text, string file = "<world>")
    {
        var root = SExprReader.ReadAll(text).OfType<SList>().FirstOrDefault(l => l.Head == "world")
            ?? throw new MachineFormatException($"{file}: expected (world NAME (place LABEL MACHINE (at x y z)) ...)");
        if (root.Items.Count < 2 || root.Items[1] is not SSymbol name)
            throw new MachineFormatException($"{file}: a world needs a name: (world NAME ...)");
        var placements = new List<Placement>();
        foreach (var p in root.Fields("place"))
        {
            var loc = new SourceLocation(file, 0, 0);
            if (p.Items.Count < 4 || p.Items[1] is not SSymbol label || p.Items[2] is not SSymbol machine)
                throw new MachineFormatException($"{file}: (place LABEL MACHINE (at x y z))", loc);
            double heading = p.Field("heading") is { } h
                ? h.Items.Count == 2 && h.Items[1] is SNumber hn ? hn.Value
                    : throw new MachineFormatException($"{file}: place {label.Name}: (heading DEGREES)", loc)
                : 0;
            var at = p.Field("at") is { Items.Count: 4 } a
                ? new Vec3(Num(a.Items[1]), Num(a.Items[2]), Num(a.Items[3]))
                : throw new MachineFormatException($"{file}: place {label.Name} needs (at x y z)", loc);
            if (placements.Any(q => q.Label == label.Name))
                throw new MachineFormatException($"{file}: two placements are labelled {label.Name}", loc);
            placements.Add(new Placement(label.Name, machine.Name, at, loc) { Heading = heading });
        }
        var links = new List<LinkSpec>();
        foreach (var l in root.Fields("link"))
        {
            var loc = new SourceLocation(file, 0, 0);
            if (l.Items.Count < 5 || l.Items[1] is not SSymbol id || l.Items[2] is not SSymbol kind)
                throw new MachineFormatException($"{file}: (link ID pipe|shaft (from LABEL PART [PORT]) (to LABEL PART [PORT]) ...)", loc);
            LinkEnd End(string which) =>
                l.Field(which) is { Items.Count: 3 or 4 } e && e.Items.Skip(1).All(x => x is SSymbol)
                    ? new LinkEnd(((SSymbol)e.Items[1]).Name, ((SSymbol)e.Items[2]).Name, e.Items.Count == 4 ? ((SSymbol)e.Items[3]).Name : null)
                    : throw new MachineFormatException($"{file}: link {id.Name} needs ({which} LABEL PART [PORT])", loc);
            var link = new LinkSpec(id.Name, kind.Name, End("from"), End("to"), loc)
            {
                Conductance = l.Field("conductance") is { Items.Count: 2 } c ? Num(c.Items[1]) : 1e-3,
                Ratio = l.Field("ratio") is { Items.Count: 2 } r ? Num(r.Items[1]) : 1,
            };
            if (links.Any(x => x.Id == link.Id))
                throw new MachineFormatException($"{file}: two links are named {link.Id}", loc);
            CheckLink(link, placements, file);
            links.Add(link);
        }
        var map = root.Field("map") is { Items.Count: 2 } m
            ? (m.Items[1] is SSymbol ms ? ms.Name : throw new MachineFormatException($"{file}: (map NAME)"))
            : null;
        RoverStart? rover = null;
        if (root.Field("rover") is { } rv)
        {
            if (rv.Field("at") is not { Items.Count: 3 } ra)
                throw new MachineFormatException($"{file}: (rover (at X Z) [(heading DEGREES)])");
            double heading = rv.Field("heading") is { Items: [_, SNumber hv] } ? hv.Value : 0;
            rover = new RoverStart(Num(ra.Items[1]), Num(ra.Items[2]), heading);
        }
        var scenario = root.Field("scenario") is { } sf ? ScenarioDef.Parse(sf, file) : null;
        return new WorldDef { Name = name.Name, Map = map, Rover = rover, Scenario = scenario, Placements = placements, Links = links };

        static double Num(SExpr e) => e is SNumber n ? n.Value : throw new MachineFormatException($"expected a number, got {e}");
    }

    /// <summary>
    /// Every given machine laid out in rows with room between them, sized
    /// from how far each one's parts spread (plus a margin for wheels, sails
    /// and throws that reach past their parts' positions): a gallery world.
    /// </summary>
    public static WorldDef Gallery(IEnumerable<MachineDef> machines, double rowWidth = 60, double gap = 4)
    {
        var placements = new List<Placement>();
        double x = 0, z = 0, rowDepth = 0;
        foreach (var m in machines)
        {
            var (min, max) = Extent(m);
            double w = max.X - min.X + gap, d = max.Z - min.Z + gap;
            if (x > 0 && x + w > rowWidth) { x = 0; z += rowDepth; rowDepth = 0; }
            // put the machine's footprint at (x, z), its origin offset accordingly
            placements.Add(new Placement(m.Name, m.Name, new Vec3(x - min.X, 0, z - min.Z), null));
            x += w;
            rowDepth = Math.Max(rowDepth, d);
        }
        return new WorldDef { Name = "gallery", Placements = placements };
    }

    /// <summary>A machine's footprint on the ground: its parts' positions, each widened by its own size (radius, length, area).</summary>
    public static (Vec3 Min, Vec3 Max) Extent(MachineDef m)
    {
        double minX = 0, maxX = 0, minZ = 0, maxZ = 0;
        bool first = true;
        foreach (var p in m.Parts)
        {
            double r = new[]
            {
                p.Number("radius", 0), p.Number("length", 0), Math.Sqrt(Math.Max(0, p.Number("area", 0))),
                p.Number("size", 0), p.Number("size-x", 0) / 2, p.Number("size-z", 0) / 2, 0.3,
            }.Max();
            if (first) { minX = maxX = p.At.X; minZ = maxZ = p.At.Z; first = false; }
            minX = Math.Min(minX, p.At.X - r); maxX = Math.Max(maxX, p.At.X + r);
            minZ = Math.Min(minZ, p.At.Z - r); maxZ = Math.Max(maxZ, p.At.Z + r);
        }
        return (new Vec3(minX, 0, minZ), new Vec3(maxX, 0, maxZ));
    }
}
