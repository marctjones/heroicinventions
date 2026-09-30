using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// The build-mode editor's command layer: every edit — placing a part,
/// moving it, changing a prop, snapping two ports, undoing, checking,
/// running, saving — is one s-expression command, read with the same
/// <see cref="SExprReader"/> the .machine format itself uses. This is the
/// one place editing happens; the palette/drag-and-drop UI
/// (game/scripts/BuildMode.cs) and a text console both work by building a
/// command string and calling <see cref="Execute"/> — there is no second,
/// UI-only way to mutate the document, so anything the UI can do, a test
/// (or a saved script) can reproduce exactly.
///
/// A design *is* its command log (<see cref="CommandLog"/>): undo/redo
/// falls out of snapshotting the document before every mutating command,
/// rather than tracking a separate inverse for each command kind.
///
/// Commands (id is a bare symbol; a.b is a part.port reference):
///   (tank id #:at (x y z) #:area A #:height H [#:water W] [#:material M] ...)
///   (boiler|block|pendulum|lever|ramp|piston|post id #:at (x y z) ... similarly)
///   (sluice id #:at (x y z) #:on channel #:height H [#:opening o] [#:width w])
///   (float-valve id #:at (x y z) #:on feed #:shut S #:travel T)   ; feed: an inflow, pipe or channel into a tank
///   (safety-valve id #:at (x y z) #:on boiler #:lift Pa #:bore D [#:coefficient Cd] [#:accumulation a])   ; a boiler's #:burst Pa rates it
///   (mirror id #:at (x y z) #:area m2 #:onto boiler-or-sealed-tank [#:reflectivity r])   ; a heliostat: DNI·area·r·cos(θ/2)
///   (capstan id #:at (x y z) #:turns n #:load kg [#:hold N] [#:mu μ] [#:drop m] [#:radius r] [#:rope hemp])   ; holds e^(μ·2πn) × the pull
///   (windmill id #:at (x y z) #:radius R #:mass M #:wind v [#:load N·m] [#:cp Cp] [#:tip-speed-ratio λ])   ; Cp at most 16/27 (Betz)
///   (bellows id #:at (x y z) #:on hearth #:airflow m3/s [#:material M])   ; forces the hearth's draught past what it draws unforced
///   (pump id #:at (x y z) #:from tank #:to tank #:bore D #:stroke S [#:rpm n] [#:efficiency e] [#:force N] [#:temperature C])   ; #:at is the barrel's foot
///   (leak id #:at (x y z) #:on tank #:height H #:area A [#:coefficient Cd] [#:into catch-tank] [#:evaporation m3/s])
///   (wheel|screw|fixture id #:catalogue entry-id #:at (x y z) [#:material M])
///   (pipe id from.port to.port #:conductance C)
///   (connect a.port b.port)
///   (sun [#:latitude deg] [#:day n] [#:time hours])   ; the scene under the sun (default Alexandria, midsummer, noon)
///   (ambient °C)           ; the scene's air: boilers cool to it, water and air arrive at it, tanks freeze below 0
///   (move id (x y z))
///   (set id #:prop value)
///   (remove id)
///   (snap a.port b.port)         ; picks pipe vs connect by port kind (PortRules)
///   (undo) (redo)
///   (check)                      ; builds MachineDef + MachineRuntime; reports MachineFormatException, with source location
///   (run seconds)
///   (save name) (load name)      ; by name, under the session's machines directory
///   (palette)
///
/// Every number may carry one of a fixed unit whitelist — see
/// <see cref="Units"/> — instead of an arbitrary expression: this is a
/// small fixed grammar, not a general evaluator.
/// </summary>
public sealed class BuildSession
{
    private static readonly string[] PrimitiveHeads = [.. PartTemplates.PrimitiveKinds];
    private static readonly string[] CatalogueHeads = ["wheel", "screw", "fixture"];

    private readonly MaterialLibrary _materials;
    private readonly IReadOnlyList<CatalogueEntry> _catalogue;
    private readonly string _machinesDir;
    private readonly List<string> _log = [];
    private readonly Stack<MachineDef> _undo = new();
    private readonly Stack<MachineDef> _redo = new();

    public EditorDocument Document { get; private set; }
    public IReadOnlyList<string> CommandLog => _log;
    public MachineRuntime? LastRun { get; private set; }

    public BuildSession(MaterialLibrary materials, IReadOnlyList<CatalogueEntry> catalogue, string machinesDir, string name = "untitled")
    {
        _materials = materials;
        _catalogue = catalogue;
        _machinesDir = machinesDir;
        Document = EditorDocument.New(name);
    }

    /// <summary>Runs one command; returns a human-readable result for a console to print. Throws FormatException/InvalidOperationException/MachineFormatException on a bad command.</summary>
    public string Execute(string line)
    {
        var forms = SExprReader.ReadAll(line);
        if (forms.Count != 1 || forms[0] is not SList cmd || cmd.Items.Count == 0 || cmd.Items[0] is not SSymbol head)
            throw new FormatException($"not a command: {line}");
        string result = Dispatch(head.Name, cmd);
        _log.Add(line);
        return result;
    }

    private string Dispatch(string head, SList cmd) => head switch
    {
        _ when PrimitiveHeads.Contains(head) => CreatePrimitive(head, cmd),
        _ when CatalogueHeads.Contains(head) => CreateFromCatalogue(head, cmd),
        "pipe" => CreatePipe(cmd),
        "connect" => CreateConnect(cmd),
        "inflow" => CreateInflow(cmd),
        "channel" => CreateChannel(cmd),
        "lift" => CreateLift(cmd),
        "move" => Move(cmd),
        "ambient" => SetAmbient(cmd),
        "sun" => SetSun(cmd),
        "set" => Set(cmd),
        "remove" => Remove(cmd),
        "snap" => Snap(cmd),
        "undo" => Undo(),
        "redo" => Redo(),
        "check" => Check(),
        "run" => Run(cmd),
        "save" => SaveNamed(Id(cmd, 1)),
        "load" => LoadNamed(Id(cmd, 1)),
        "palette" => Palette(),
        _ => throw new FormatException($"unknown command '{head}'"),
    };

    // ------------------------------------------------------------ parsing

    private static string Id(SList cmd, int i) =>
        cmd.Items.ElementAtOrDefault(i) is SSymbol s ? s.Name : throw new FormatException($"({cmd.Head} …): expected a name at position {i}");

    /// <summary>The value after a #:key keyword anywhere in the command, or null if absent.</summary>
    private static SExpr? Kw(SList cmd, string key)
    {
        for (int i = 1; i < cmd.Items.Count - 1; i++)
            if (cmd.Items[i] is SSymbol s && s.Name == "#:" + key) return cmd.Items[i + 1];
        return null;
    }

    private static SExpr RequireKw(SList cmd, string key) =>
        Kw(cmd, key) ?? throw new FormatException($"({cmd.Head} …) needs #:{key}");

    /// <summary>A number, in the DSL's own unit whitelist (see <see cref="Units"/>) or bare (already SI).</summary>
    private static double Num(SExpr e, string context) => e switch
    {
        SNumber n => n.Value,
        SSymbol s => Units.Parse(s.Name, context),
        _ => throw new FormatException($"{context}: expected a number"),
    };

    private static Vec3 VecOf(SExpr e, string context)
    {
        if (e is not SList { Items.Count: 3 } l) throw new FormatException($"{context}: expected (x y z)");
        return new Vec3(Num(l.Items[0], context), Num(l.Items[1], context), Num(l.Items[2], context));
    }

    private static PortRef PortRefOf(SExpr e, string context)
    {
        string s = e is SSymbol sym ? sym.Name : throw new FormatException($"{context}: expected part.port");
        string[] pieces = s.Split('.');
        if (pieces.Length != 2) throw new FormatException($"{context}: expected part.port, got '{s}'");
        return new PortRef(pieces[0], pieces[1]);
    }

    // ---------------------------------------------------------- mutating

    private void Snapshot()
    {
        _undo.Push(Document.ToMachineDef());
        _redo.Clear();
    }

    private string CreatePrimitive(string kind, SList cmd)
    {
        string id = Id(cmd, 1);
        var at = VecOf(RequireKw(cmd, "at"), kind);
        string material = Kw(cmd, "material") is SSymbol m ? m.Name : "bronze";
        var part = ApplyPropOverrides(PartTemplates.Create(kind, id, at, material), cmd, kind);
        Snapshot();
        Document.AddPart(part);
        return $"placed {id} ({kind})";
    }

    /// <summary>Any #:key value in the command whose key already names one of the template's own numeric props overrides it — e.g. #:area on a tank.</summary>
    private static PartSpec ApplyPropOverrides(PartSpec part, SList cmd, string context)
    {
        var props = new Dictionary<string, SExpr>(part.Props);
        for (int i = 1; i < cmd.Items.Count - 1; i++)
        {
            if (cmd.Items[i] is not SSymbol s || !s.Name.StartsWith("#:")) continue;
            string key = s.Name[2..];
            if (key is "at" or "material" or "catalogue") continue;
            if (props.TryGetValue(key, out var existing) && existing is SNumber)
                props[key] = new SNumber(Num(cmd.Items[i + 1], context));
            else if (existing is SSymbol && cmd.Items[i + 1] is SSymbol sym)
                props[key] = sym;                          // a name, such as a sluice's #:on channel
            else if (existing is SBool { Value: false } && key is "tail" or "race" or "into" && cmd.Items[i + 1] is SSymbol named)
                props[key] = named;                        // an optional name, such as a water wheel's #:race
            else if (existing is SBool { Value: false } && cmd.Items[i + 1] is SNumber or SSymbol)
                props[key] = new SNumber(Num(cmd.Items[i + 1], context)); // an optional number, such as a sluice's #:width
        }
        return part with { Props = props };
    }

    private string CreateFromCatalogue(string kind, SList cmd)
    {
        string id = Id(cmd, 1);
        string catalogueId = RequireKw(cmd, "catalogue") is SSymbol s ? s.Name : throw new FormatException($"{kind} {id}: #:catalogue needs an entry name");
        var entry = _catalogue.FirstOrDefault(e => e.Id == catalogueId)
            ?? throw new InvalidOperationException($"no catalogue entry named {catalogueId} (try (palette))");
        if (entry.PartKind != kind)
            throw new InvalidOperationException($"{catalogueId} is a {entry.PartKind}, not a {kind}");
        var at = VecOf(RequireKw(cmd, "at"), kind);
        string material = Kw(cmd, "material") is SSymbol m ? m.Name : "bronze";
        Snapshot();
        Document.AddPart(PartTemplates.Create(entry, id, at, material));
        return $"placed {id} ({catalogueId})";
    }

    private string CreatePipe(SList cmd)
    {
        string id = Id(cmd, 1);
        var from = PortRefOf(cmd.Items[2], "pipe");
        var to = PortRefOf(cmd.Items[3], "pipe");
        double conductance = Kw(cmd, "conductance") is { } c ? Num(c, "pipe") : 0.001;
        bool jet = Kw(cmd, "jet") is SBool { Value: true };
        Snapshot();
        Document.AddPipe(id, from, to, conductance, jet);
        return $"piped {id}: {from} -> {to}";
    }

    private string CreateConnect(SList cmd)
    {
        var a = PortRefOf(cmd.Items[1], "connect");
        var b = PortRefOf(cmd.Items[2], "connect");
        Snapshot();
        Document.AddConnect(a, b);
        return $"connected {a} to {b}";
    }

    private string CreateInflow(SList cmd)
    {
        string id = Id(cmd, 1);
        string into = RequireKw(cmd, "into") is SSymbol s ? s.Name : throw new FormatException($"inflow {id}: #:into needs a tank name");
        double flow = Num(RequireKw(cmd, "flow"), "inflow");
        Snapshot();
        Document.AddInflow(id, into, flow);
        return $"inflow {id} -> {into} at {flow} m3/s";
    }

    private string CreateChannel(SList cmd)
    {
        string id = Id(cmd, 1);
        var from = PortRefOf(cmd.Items[2], "channel");
        PortRef? to = cmd.Items.ElementAtOrDefault(3) is SSymbol { Name: "off" } ? null : PortRefOf(cmd.Items[3], "channel");
        Vec3? end = Kw(cmd, "end") is { } e ? VecOf(e, "channel") : null;
        double width = Num(RequireKw(cmd, "width"), "channel");
        double? length = Kw(cmd, "length") is { } l ? Num(l, "channel") : null;
        Snapshot();
        var via = Kw(cmd, "via") is SList vl
            ? vl.Items.Select(p => p is SList { Items.Count: 2 } xz
                ? (Num(xz.Items[0], "channel"), Num(xz.Items[1], "channel"))
                : throw new FormatException("channel #:via: expected ((x z) ...)")).ToList()
            : null;
        string? onto = Kw(cmd, "onto") is { } o
            ? o is SSymbol os ? os.Name : throw new FormatException("channel #:onto needs a hearth or boiler name")
            : null;
        Document.AddChannel(id, from, to, end, width, length, via, onto);
        return $"channel {id}: {from} -> {(to is { } t ? t.ToString() : "off")}";
    }

    private string CreateLift(SList cmd)
    {
        string id = Id(cmd, 1);
        string by = RequireKw(cmd, "by") is SSymbol s ? s.Name : throw new FormatException($"lift {id}: #:by needs a part name");
        string from = RequireKw(cmd, "from") is SSymbol f ? f.Name : throw new FormatException($"lift {id}: #:from needs a tank name");
        string to = RequireKw(cmd, "to") is SSymbol t ? t.Name : throw new FormatException($"lift {id}: #:to needs a tank name");
        double? current = Kw(cmd, "current") is { } c ? Num(c, "lift") : null;
        string? currentFrom = Kw(cmd, "current-from") is SSymbol cf ? cf.Name : null;
        Snapshot();
        Document.AddLift(id, by, from, to, current, currentFrom);
        return $"lift {id}: {by} raises {from} -> {to}";
    }

    /// <summary>(sun #:latitude deg #:day n #:time hours): where and when the scene stands; unspecified fields keep their current (or default) values.</summary>
    private string SetSun(SList cmd)
    {
        var now = Document.Sun ?? new SunSpec(31.2, 172, 12);
        double lat = Kw(cmd, "latitude") is { } l ? Num(l, "sun #:latitude") : now.Latitude;
        int day = Kw(cmd, "day") is { } d ? (int)Num(d, "sun #:day") : now.Day;
        double time = Kw(cmd, "time") is { } t ? Num(t, "sun #:time") : now.Time;
        if (lat is < -90 or > 90) throw new FormatException($"(sun #:latitude {lat}): must be in [-90, 90]");
        if (day is < 1 or > 365) throw new FormatException($"(sun #:day {day}): must be 1 to 365");
        if (time is < 0 or >= 24) throw new FormatException($"(sun #:time {time}): must be solar hours in [0, 24)");
        Snapshot();
        Document.Sun = new SunSpec(lat, day, time);
        return $"sun at {lat}° N, day {day}, {time} h";
    }

    /// <summary>(ambient °C): the scene's air temperature.</summary>
    private string SetAmbient(SList cmd)
    {
        double c = Num(cmd.Items.ElementAtOrDefault(1) ?? throw new FormatException("(ambient °C) needs a temperature"), "ambient");
        if (c <= -273.15) throw new FormatException($"(ambient {c}): below absolute zero");
        Snapshot();
        Document.Ambient = c;
        return $"ambient {c} °C";
    }

    private string Move(SList cmd)
    {
        string id = Id(cmd, 1);
        var at = VecOf(cmd.Items[2], "move");
        Snapshot();
        Document.Move(id, at);
        return $"moved {id} to ({at.X} {at.Y} {at.Z})";
    }

    /// <summary>(set id #:prop value): #:material takes a material symbol; every other prop takes a number (its own unit, if any).</summary>
    private string Set(SList cmd)
    {
        string id = Id(cmd, 1);
        if (cmd.Items[2] is not SSymbol kw || !kw.Name.StartsWith("#:"))
            throw new FormatException("(set id #:prop value)");
        string key = kw.Name[2..];
        if (key == "material")
        {
            string material = cmd.Items[3] is SSymbol m ? m.Name : throw new FormatException("(set id #:material name)");
            Snapshot();
            Document.SetMaterial(id, material);
            return $"set {id} #:material {material}";
        }
        // A name, where the prop names another part: (set m1 #:onto boiler)
        if (cmd.Items[3] is SSymbol name && !double.TryParse(name.Name, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
            && Document.Parts.TryGetValue(id, out var named) && named.Props.GetValueOrDefault(key) is SSymbol or SBool)
        {
            if (!Document.Parts.ContainsKey(name.Name))
                throw new InvalidOperationException($"no part named {name.Name}");
            Snapshot();
            Document.SetName(id, key, name.Name);
            return $"set {id} #:{key} {name.Name}";
        }
        double value = Num(cmd.Items[3], "set");
        Snapshot();
        Document.SetProp(id, key, value);
        return $"set {id} #:{key} {value}";
    }

    private string Remove(SList cmd)
    {
        string id = Id(cmd, 1);
        Snapshot();
        Document.RemovePart(id);
        return $"removed {id}";
    }

    private string Snap(SList cmd)
    {
        var a = PortRefOf(cmd.Items[1], "snap");
        var b = PortRefOf(cmd.Items[2], "snap");
        Snapshot();
        Document.Connect(new PortHandle(a.Part, a.Port), new PortHandle(b.Part, b.Port));
        return $"snapped {a} to {b}";
    }

    private string Undo()
    {
        if (_undo.Count == 0) throw new InvalidOperationException("nothing to undo");
        _redo.Push(Document.ToMachineDef());
        Document = EditorDocument.Load(_undo.Pop());
        return "undone";
    }

    private string Redo()
    {
        if (_redo.Count == 0) throw new InvalidOperationException("nothing to redo");
        _undo.Push(Document.ToMachineDef());
        Document = EditorDocument.Load(_redo.Pop());
        return "redone";
    }

    // ------------------------------------------------------- non-mutating

    /// <summary>
    /// Builds the current design into a real MachineDef and MachineRuntime —
    /// the same two steps the game itself does — so every reference, port
    /// kind and steam-feed rule MachineDef.Parse/MachineRuntime enforce gets
    /// checked here too, in one place, instead of each editor command
    /// re-implementing a piece of it. Round-trips through MachineWriter
    /// first so a failure's MachineFormatException names a real "<editor>"
    /// source location instead of reporting none.
    /// </summary>
    private string Check()
    {
        try
        {
            var def = MachineDef.Parse(MachineWriter.Write(Document.ToMachineDef()));
            _ = new MachineRuntime(def, _materials);
            return $"ok: {def.Parts.Count} parts, {def.Pipes.Count} pipes, {def.Connects.Count} connects";
        }
        catch (MachineFormatException e)
        {
            return $"error: {e.Message}";
        }
        catch (FormatException e)
        {
            return $"error: {e.Message}";
        }
    }

    private string Run(SList cmd)
    {
        double seconds = Num(cmd.Items[1], "run");
        var def = MachineDef.Parse(MachineWriter.Write(Document.ToMachineDef()));
        var runtime = new MachineRuntime(def, _materials);
        const double dt = 0.01;
        int steps = (int)Math.Ceiling(seconds / dt);
        for (int i = 0; i < steps; i++) runtime.Step(dt);
        LastRun = runtime;
        string tanks = string.Join(", ", runtime.Tanks.Select(t => $"{t.Key}={t.Value.WaterVolume * 1000:F1}L"));
        return $"ran {seconds}s: {tanks}";
    }

    private string SaveNamed(string name)
    {
        string path = Path.Combine(_machinesDir, name + ".machine");
        SaveFile(path);
        return $"saved {path}";
    }

    private string LoadNamed(string name) => LoadFile(Path.Combine(_machinesDir, name + ".machine"));

    /// <summary>Save to an exact path — used by name-based (save) and directly by the palette/console UI's file picker.</summary>
    public void SaveFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _machinesDir);
        MachineWriter.WriteFile(Document.ToMachineDef(), path);
    }

    /// <summary>Load from an exact path.</summary>
    /// <summary>Opens a machine that's already running in a world, to edit it live (issue #75). Not undoable: it's where editing starts.</summary>
    public void Open(MachineDef def) => Document = EditorDocument.Load(def);

    public string LoadFile(string path)
    {
        var def = MachineDef.Parse(File.ReadAllText(path));
        Snapshot();
        Document = EditorDocument.Load(def);
        return $"loaded {path}";
    }

    /// <summary>The Racket source export — see <see cref="RktExporter"/> for what it can and can't reproduce.</summary>
    public string ExportRkt(string path)
    {
        File.WriteAllText(path, RktExporter.Write(Document.ToMachineDef()));
        return $"exported {path}";
    }

    private string Palette() =>
        string.Join("\n", PartTemplates.PrimitiveKinds.Concat(_catalogue.Select(e => $"{e.Id} — {e.Description}")));
}
