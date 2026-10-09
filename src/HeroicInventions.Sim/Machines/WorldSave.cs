using System.Globalization;

namespace HeroicInventions.Sim.Machines;

/// <summary>One machine's part of a save: which placement (label) and machine it is, its clock, and its running state.</summary>
public sealed record SavedMachine(string Label, string Machine, double Clock, SList State, SList? View = null);

/// <summary>A sleep that was in progress when the world was saved: on which machine, what it waited for, when it started and what it had estimated.</summary>
public sealed record SavedSleep(string Label, WakeSpec Plan, double StartedAt, double? Predicted);

/// <summary>
/// A whole world on disk (issue #67): a file in the same s-expression shape as a .machine file that names what it
/// was made of (a machine, or a world of placed machines) and holds the running state beside the names: each machine's
/// clock and <see cref="RuntimeState"/>, what the game's rigid bodies were doing (as an opaque list the game reads back),
/// and a sleep in progress. It does not copy the machine definitions: loading rebuilds them from their own files and lays the
/// state on, so a machine edited since is loaded as edited with whatever of the old state still fits.
/// It is written atomically: to a temporary file beside it, then renamed over it, so a crash mid-save leaves the
/// last good save and never half of a new one.
/// <code>
/// (world-save 1 (kind machine) (name wake-clock)
///   (machine wake-clock wake-clock (clock 12.5) (state (path value) …) (view …))
///   (sleep wake-clock (started 0) (predicted 25) (join and) (limit 600) (when (cistern water above 50)) (events)))
/// </code>
/// </summary>
public sealed class WorldSave
{
    public const int Version = 1;

    public required string Kind { get; init; }          // "machine" or "world"
    public required string Name { get; init; }
    public required IReadOnlyList<SavedMachine> Machines { get; init; }
    public SavedSleep? Sleep { get; init; }
    /// <summary>The ground the world stands on (its map's heights and soils, the water on it, the bed it has moved), or null with no map.</summary>
    public SList? Ground { get; init; }
    /// <summary>The boulders slides have left on the ground (issue #88), where they lie and how they move: <c>(boulders (boulder …) …)</c>, or null.</summary>
    public SList? Boulders { get; init; }
    /// <summary>The fine patches of ground the rover has dug and heaped (issue #199): <c>(worked 1 (patch …) …)</c>, or null. An older save has none, and loads as before.</summary>
    public SList? Worked { get; init; }

    /// <summary>The goals and achievements earned (issue #68): <c>(goals 1 (found X Z) (earned id time sol) …)</c>, or null. An older save has none, and loads with nothing earned.</summary>
    public SList? Goals { get; init; }

    /// <summary>Every link between machines the world has when saved (issue #82): pipes, shafts and wires, the ones its file declares and the ones made during play. Null for a save that has none to say (a single machine, or an older save): the world file's own links stand.</summary>
    public IReadOnlyList<LinkSpec>? Links { get; init; }

    /// <summary>The rover itself (issue #201): <c>(rover 1 (chassis …) (wheel K …) (drive …) (bucket …) (arm …))</c>, which the game reads back; or null. An older save has none, and the world file's rover stands where it puts it.</summary>
    public SList? Rover { get; init; }

    /// <summary>Everything the person (or a demo operator, or a replay) has done to the run so far (issue #153), in order; empty when nothing was done.</summary>
    public IReadOnlyList<OperatorAction> Operated { get; init; } = [];
    /// <summary>True once the person has taken a control: a blueprint's demo operator stays stopped after loading.</summary>
    public bool OperatorTaken { get; init; }

    public string ToText()
    {
        var items = new List<SExpr>
        {
            new SSymbol("world-save"), new SNumber(Version),
            new SList([new SSymbol("kind"), new SSymbol(Kind)]),
            new SList([new SSymbol("name"), new SSymbol(Name)]),
        };
        foreach (var m in Machines)
        {
            var parts = new List<SExpr> { new SSymbol("machine"), new SSymbol(m.Label), new SSymbol(m.Machine),
                                          new SList([new SSymbol("clock"), new SNumber(m.Clock)]), m.State };
            if (m.View is { } v) parts.Add(v);
            items.Add(new SList(parts));
        }
        if (Operated.Count > 0 || OperatorTaken)
            items.Add(new SList([new SSymbol("operator-log"), .. (OperatorTaken ? [new SList([new SSymbol("taken"), new SBool(true)])] : Array.Empty<SExpr>()),
                                 .. Operated.Select(a => (SExpr)OperatorLog.ToForm(a))]));
        if (Goals is { } gl) items.Add(gl);   // (before the ground: the rover stays the last form of a save, as worked-save-test builds an older save by blanking its line)
        if (Links is { } links) items.Add(new SList([new SSymbol("links"), new SNumber(1), .. links.Select(l => (SExpr)LinkForms.ToForm(l))]));
        if (Ground is { } g) items.Add(new SList([new SSymbol("ground"), g]));
        if (Boulders is { } bs) items.Add(bs);
        if (Worked is { } wk) items.Add(wk);
        if (Rover is { } rv) items.Add(rv);
        if (Sleep is { } s)
        {
            static SExpr Term(WakeTerm t) => new SList([new SSymbol(t.Target), new SSymbol(t.Field), new SSymbol(t.Above ? "above" : "below"), new SNumber(t.Value)]);
            items.Add(new SList([new SSymbol("sleep"), new SSymbol(s.Label),
                new SList([new SSymbol("started"), new SNumber(s.StartedAt)]),
                new SList([new SSymbol("predicted"), s.Predicted is { } p ? new SNumber(p) : new SBool(false)]),
                new SList([new SSymbol("join"), new SSymbol(s.Plan.All ? "and" : "or")]),
                new SList([new SSymbol("limit"), new SNumber(s.Plan.Limit)]),
                new SList([new SSymbol("when"), .. s.Plan.Terms.Select(Term)]),
                new SList([new SSymbol("events"), .. s.Plan.Events.Select(Term)])]));
        }
        return ";; A saved world. Loading rebuilds the machines from their own files and lays this running state on them.\n"
               + SExprWriter.Print(new SList(items)).Replace(" (machine ", "\n  (machine ").Replace(" (sleep ", "\n  (sleep ").Replace(" (operator-log ", "\n  (operator-log ").Replace(" (links ", "\n  (links ").Replace(" (ground ", "\n  (ground ").Replace(" (boulders ", "\n  (boulders ").Replace(" (worked ", "\n  (worked ").Replace(" (rover ", "\n  (rover ").Replace(" (goals ", "\n  (goals ") + "\n";
    }

    public static WorldSave Parse(string text)
    {
        var forms = SExprReader.ReadAll(text);
        if (forms is not [SList { Head: "world-save" } root] || root.Items.ElementAtOrDefault(1) is not SNumber v)
            throw new FormatException("not a saved world: expected one (world-save …) form");
        if (v.Value != Version) throw new FormatException($"this save is version {v.Value}; this game reads version {Version}");
        string Sym(string field) => root.Field(field)?.Items.ElementAtOrDefault(1) is SSymbol s ? s.Name : throw new FormatException($"the save has no ({field} …)");
        var machines = root.Fields("machine").Select(m =>
        {
            if (m.Items is not [_, SSymbol label, SSymbol machine, SList clock, SList state, ..])
                throw new FormatException("a (machine label name (clock t) (state …)) is expected");
            return new SavedMachine(label.Name, machine.Name, clock.Items[1] is SNumber c ? c.Value : 0, state, m.Field("view"));
        }).ToList();
        SavedSleep? sleep = null;
        if (root.Field("sleep") is { } sl && sl.Items.ElementAtOrDefault(1) is SSymbol slLabel)
        {
            static IReadOnlyList<WakeTerm> Terms(SList? l) => (l?.Items.Skip(1) ?? []).OfType<SList>()
                .Select(t => t.Items is [SSymbol tg, SSymbol fl, SSymbol md, SNumber vl] ? new WakeTerm(tg.Name, fl.Name, md.Name == "above", vl.Value) : throw new FormatException("a term is (target field above|below value)")).ToList();
            double Num(string f, double fallback) => sl.Field(f)?.Items.ElementAtOrDefault(1) is SNumber n ? n.Value : fallback;
            sleep = new SavedSleep(slLabel.Name,
                new WakeSpec("sleep", Terms(sl.Field("when")), sl.Field("join")?.Items.ElementAtOrDefault(1) is not SSymbol { Name: "or" }, Num("limit", 3600), Terms(sl.Field("events"))),
                Num("started", 0), sl.Field("predicted")?.Items.ElementAtOrDefault(1) is SNumber pr ? pr.Value : null);
        }
        return new WorldSave { Kind = Sym("kind"), Name = Sym("name"), Machines = machines, Sleep = sleep, Ground = root.Field("ground")?.Items.ElementAtOrDefault(1) as SList, Boulders = root.Field("boulders"), Worked = root.Field("worked"), Rover = root.Field("rover"), Goals = root.Field("goals"),
            Links = root.Field("links") is { } lk ? lk.Items.Skip(2).OfType<SList>().Where(x => x.Head == "link").Select(LinkForms.FromForm).ToList() : null,
            Operated = (root.Field("operator-log")?.Items.Skip(1) ?? []).Select(OperatorLog.FromForm).OfType<OperatorAction>().ToList(),
            OperatorTaken = root.Field("operator-log")?.Field("taken")?.Items.ElementAtOrDefault(1) is SBool { Value: true } };
    }

    /// <summary>Writes the save to <paramref name="path"/> atomically: a temporary file beside it, flushed to disk, then renamed over it.</summary>
    public void WriteAtomic(string path)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Environment.ProcessId}.tmp");
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
            {
                w.Write(ToText());
                w.Flush();
                fs.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static WorldSave Read(string path) => Parse(File.ReadAllText(path));

    /// <summary>The save of one machine on its own.</summary>
    public static WorldSave OfMachine(string name, MachineRuntime runtime, SavedSleep? sleep = null, SList? view = null) => new()
    {
        Kind = "machine", Name = name, Sleep = sleep,
        Machines = [new SavedMachine(name, name, runtime.Time, RuntimeState.Capture(runtime), view)],
    };
}
