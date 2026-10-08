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
///   (drain id #:at (x y z) #:into tank [#:perimeter P])   ; on a map, the water standing over it runs into the tank over a P m lip
///   (wheel|screw|fixture id #:catalogue entry-id #:at (x y z) [#:material M])
///   (pipe id from.port to.port #:conductance C)
///   (connect a.port b.port)
///   (rope id #:from (part x y z) #:to (part x y z) #:length L [#:over ((x y z) ...)] [#:wind-on drum] [#:turns pulley]
///         [#:release-deg d] [#:material M] [#:diameter D] [#:nocked #t] [#:bar M] [#:mu μ] [#:tether #t [#:release-after s]])   ; ends are points in each part's own frame; "world" is fixed; #:bar: the #:over points are fixed bars (capstan friction)
///   (mesh gear-a gear-b)                          ; two gears in mesh
///   (arbor wheel wheel ...)                       ; wheels fixed on one axle; the first carries the bearing and drive
///   (sealed-air (tank tank ...) #:tube V [#:heat-loss W/K] [#:heat-capacity J/K])   ; tanks sharing one sealed air space
///   (atmospheric-cylinder id #:piston p #:steam-from boiler [#:injection-temperature C])
///   (inflow id #:into tank #:flow m3/s) (channel id from.port to.port|off ...) (lift id #:by part #:from tank #:to tank)
///   (trigger id #:at (x y z) #:size (w h d) #:body part #:do ((target field value) ...))   ; fires once when the part's centre enters the box
///   (trigger id #:when (target field above|below value) #:do ((target field value) ...)) ; fires once when a field crosses the value
///   (follow id #:lever part|#:rope rope #:from a #:to b #:set (target field) [#:low v] [#:high v])   ; a field follows a lever's angle (deg) or a rope's tension (N)
///   (belt id drum drum #:tension N [#:material M])   ; an open belt between two drums; carries at most 2·T0·tanh(μθ/2) before it slips
///   (joint id #:kind pin|ball|universal|6dof #:a part #:b part|world #:at (x y z) [#:axis (x y z)] [#:free (x y z rx ry rz …)] [#:limit-deg d])
///   (wake id #:when ((target field above|below value) …) [#:join and|or] [#:limit seconds] [#:events ((target field above|below value) …)])   ; something to sleep until
///   (port part name kind height) (remove-port part name)   ; add or replace a port on a part
///   (set-rope id #:length L [#:diameter D] [#:material M] [#:release-deg d] [#:wind-on part] [#:turns part] [#:nocked #t] [#:bar M|#f] [#:mu μ|#f])   ; change a rope
///   (unmesh a b) (unarbor part) (remove-air tank)   ; take a link apart again
///   (source "text")                               ; where the machine comes from
///   (raw-part (part id kind ...))                 ; a part clause verbatim, for shaped parts no catalogue entry describes
///   (sun [#:latitude deg] [#:day n] [#:time hours])   ; the scene under the sun (default Alexandria, midsummer, noon)
///   (planet mars [#:gravity g] [#:pressure Pa] [#:temperature C] [#:air ((o2 x) ...)] ...)   ; the planet: a preset, some numbers changed; sets the ambient to its temperature
///   (weather [#:daily #t] [#:passes (3 15)] [#:pass-minutes 10])   ; sols: the air on the planet's daily curve, relay passes
///   (storm #:sol n [#:hour h] #:tau τ [#:sols d] [#:settle k])      ; a dust storm on the run's nth sol
///   (ambient °C)           ; the scene's air: boilers cool to it, water and air arrive at it, tanks freeze below 0
///   (move id (x y z))
///   (turn id degrees)       ; a block, ball, pendulum, lever, ramp, wheel, screw, fixture or post, turned about the vertical (its #:heading-deg)
///   (set id #:prop value)   ; a number, a symbol (#:axis y), a flag (#:round #t) or the name of another part (#:onto boiler)
///   (remove id)             ; a part, with every link on it; or a pipe, rope, inflow, channel, lift or cylinder by its own id
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
        "rope" => CreateRope(cmd),
        "mesh" => CreateMesh(cmd),
        "arbor" => CreateArbor(cmd),
        "sealed-air" => CreateSealedAir(cmd),
        "atmospheric-cylinder" or "steam-cylinder" => CreateCylinder(cmd),
        "trigger" => CreateTrigger(cmd),
        "follow" => CreateFollow(cmd),
        "belt" => CreateBelt(cmd),
        "wake" => CreateWake(cmd),
        "set-belt" => SetBelt(cmd),
        "joint" => CreateJoint(cmd),
        "port" => SetPort(cmd),
        "remove-port" => RemovePortCmd(cmd),
        "set-rope" => SetRope(cmd),
        "remove-air" => RemoveAir(cmd),
        "unmesh" => Unmesh(cmd),
        "unarbor" => Unarbor(cmd),
        "source" => SetSource(cmd),
        "raw-part" => RawPart(cmd),
        "move" => Move(cmd),
        "turn" => Turn(cmd),
        "ambient" => SetAmbient(cmd),
        "planet" => SetPlanet(cmd),
        "weather" => SetWeather(cmd),
        "storm" => AddStorm(cmd),
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
            else if (existing is SBool { Value: false } && key is "tail" or "race" or "into" or "gutter" or "store" && cmd.Items[i + 1] is SSymbol named)
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
        bool dynamic = Kw(cmd, "dynamic") is SBool { Value: true };
        int? cells = Kw(cmd, "cells") is { } cn ? (int)Num(cn, "channel") : null;
        if (cells is < 2 or > 2000) throw new FormatException("channel #:cells: from 2 to 2000");
        Document.AddChannel(id, from, to, end, width, length, via, onto, dynamic || cells is not null, cells);
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

    /// <summary>A (part x y z) rope end: a point in the part's own frame.</summary>
    private static RopeEnd RopeEndOf(SExpr e, string context)
    {
        if (e is not SList { Items.Count: 4 } l || l.Items[0] is not SSymbol part)
            throw new FormatException($"{context}: expected (part x y z)");
        return new RopeEnd(part.Name, new Vec3(Num(l.Items[1], context), Num(l.Items[2], context), Num(l.Items[3], context)));
    }

    private static string Name(SExpr e, string context) =>
        e is SSymbol s ? s.Name : throw new FormatException($"{context}: expected a name");

    private string CreateRope(SList cmd)
    {
        string id = Id(cmd, 1);
        var from = RopeEndOf(RequireKw(cmd, "from"), $"rope {id} #:from");
        var to = RopeEndOf(RequireKw(cmd, "to"), $"rope {id} #:to");
        double length = Num(RequireKw(cmd, "length"), $"rope {id}");
        var over = Kw(cmd, "over") is SList ol
            ? ol.Items.Select(p => VecOf(p, $"rope {id} #:over")).ToList()
            : new List<Vec3>();
        string? windOn = Kw(cmd, "wind-on") is SSymbol w ? w.Name : null;
        string? turns = Kw(cmd, "turns") is SSymbol t ? t.Name : null;
        double? release = Kw(cmd, "release-deg") is SNumber r ? r.Value : null;
        string material = Kw(cmd, "material") is SSymbol m ? m.Name : "hemp";
        double diameter = Kw(cmd, "diameter") is { } d ? Num(d, $"rope {id} #:diameter") : 0.02;
        bool nocked = Kw(cmd, "nocked") is SBool { Value: true };
        string? bar = Kw(cmd, "bar") is SSymbol b ? b.Name : null;
        double? mu = Kw(cmd, "mu") is SNumber u ? u.Value : null;
        int? links = Kw(cmd, "links") is SNumber ln ? (int)ln.Value : null;
        bool tether = Kw(cmd, "tether") is SBool { Value: true };                       // a tether (#155)
        double? releaseAfter = Kw(cmd, "release-after") is SNumber ra ? ra.Value : null;
        var spec = new RopeSpec(id, from, to, length, over, windOn, release, material, diameter, null) { Nocked = nocked, Turns = turns, Bar = bar, Mu = mu, Links = links, Tether = tether, ReleaseAfter = releaseAfter };
        CheckRopeFriction(spec);
        Snapshot();
        Document.AddRope(spec);
        return $"rope {id}: {from.Part} to {to.Part}, {length} m";
    }

    /// <summary>A rope runs over turning pulleys or fixed bars, not both; a bar is of a known material.</summary>
    private void CheckRopeFriction(RopeSpec r)
    {
        if (r.Bar is not null && r.Turns is not null)
            throw new FormatException($"rope {r.Id}: a rope runs over turning pulleys (#:turns) or fixed bars (#:bar), not both");
        if (r.Links is { } n && (n < 2 || n > 200 || r.WindOn is not null || r.Over.Count > 0 || r.Turns is not null || r.Bar is not null || r.ReleaseDeg is not null))
            throw new FormatException($"rope {r.Id}: a chain of #:links (2 to 200) hangs free between its ends: no #:wind-on, #:over, #:turns, #:bar or #:release-deg");
        if (r.Mu is not null && r.Bar is null)
            throw new FormatException($"rope {r.Id}: #:mu is the friction over fixed bars; give #:bar too");
        if (r.Mu is < 0)
            throw new FormatException($"rope {r.Id}: #:mu can't be negative");
        if (r.Bar is { } bar && !_materials.TryGet(bar, out _))
            throw new FormatException($"rope {r.Id}: unknown bar material {bar}");
    }

    /// <summary>(set-rope id #:key value …): changes the fields it names, keeps the rest.</summary>
    private string SetRope(SList cmd)
    {
        string id = Id(cmd, 1);
        var rope = Document.Ropes.FirstOrDefault(r => r.Id == id) ?? throw new InvalidOperationException($"no rope named {id}");
        var next = rope;
        if (Kw(cmd, "length") is { } l) next = next with { Length = Num(l, $"rope {id} #:length") };
        if (Kw(cmd, "diameter") is { } d) next = next with { Diameter = Num(d, $"rope {id} #:diameter") };
        if (Kw(cmd, "material") is SSymbol m) next = next with { Material = m.Name };
        if (Kw(cmd, "release-deg") is { } r) next = next with { ReleaseDeg = r is SBool { Value: false } ? null : Num(r, $"rope {id} #:release-deg") };
        if (Kw(cmd, "wind-on") is { } w) next = next with { WindOn = w is SSymbol ws ? ws.Name : null };
        if (Kw(cmd, "turns") is { } t) next = next with { Turns = t is SSymbol ts ? ts.Name : null };
        if (Kw(cmd, "nocked") is SBool nk) next = next with { Nocked = nk.Value };
        if (Kw(cmd, "bar") is { } bar) next = next with { Bar = bar is SSymbol bs ? bs.Name : null };
        if (Kw(cmd, "mu") is { } mu) next = next with { Mu = mu is SBool { Value: false } ? null : Num(mu, $"rope {id} #:mu") };
        if (next.Length <= 0) throw new FormatException($"rope {id}: #:length must be positive");
        CheckRopeFriction(next);
        Snapshot();
        Document.ReplaceRope(next);
        return $"rope {id}: {next.Length} m";
    }

    /// <summary>(remove-air tank): takes a tank out of the sealed-air group it is in.</summary>
    private string RemoveAir(SList cmd)
    {
        string tank = Id(cmd, 1);
        Snapshot();
        if (!Document.RemoveFromSealedAir(tank)) throw new InvalidOperationException($"{tank} shares no air");
        return $"{tank} no longer shares air";
    }

    private string CreateMesh(SList cmd)
    {
        string a = Id(cmd, 1), b = Id(cmd, 2);
        // #:efficiency: the share of the power through the mesh that arrives (#113); 1 when left out
        double efficiency = Kw(cmd, "efficiency") is { } e ? Num(e, "mesh #:efficiency") : 1;
        if (!(efficiency > 0 && efficiency <= 1)) throw new FormatException("a mesh's #:efficiency is more than 0 and at most 1");
        Snapshot();
        Document.AddMesh(a, b, efficiency);
        return efficiency == 1 ? $"meshed {a} with {b}" : $"meshed {a} with {b}, {efficiency:0.##} efficient";
    }

    private string Unmesh(SList cmd)
    {
        string a = Id(cmd, 1), b = Id(cmd, 2);
        Snapshot();
        if (!Document.RemoveMesh(a, b)) throw new InvalidOperationException($"{a} and {b} are not meshed");
        return $"unmeshed {a} from {b}";
    }

    private string CreateArbor(SList cmd)
    {
        var parts = cmd.Items.Skip(1).Select(e => Name(e, "arbor")).ToList();
        if (parts.Count < 2) throw new FormatException("(arbor a b …) needs at least two wheels");
        Snapshot();
        Document.AddArbor(parts);
        return $"arbor: {string.Join(", ", parts)}";
    }

    private string Unarbor(SList cmd)
    {
        string part = Id(cmd, 1);
        Snapshot();
        if (!Document.RemoveFromArbor(part)) throw new InvalidOperationException($"{part} is not on an arbor");
        return $"{part} taken off its arbor";
    }

    private string CreateSealedAir(SList cmd)
    {
        if (cmd.Items.ElementAtOrDefault(1) is not SList tl)
            throw new FormatException("(sealed-air (tank tank …) #:tube V)");
        var tanks = tl.Items.Select(e => Name(e, "sealed-air")).ToList();
        if (tanks.Count < 2) throw new FormatException("(sealed-air …) needs at least two tanks to share air");
        double tube = Kw(cmd, "tube") is { } tv ? Num(tv, "sealed-air #:tube") : 0;
        double loss = Kw(cmd, "heat-loss") is { } hl ? Num(hl, "sealed-air #:heat-loss") : 0;
        double capacity = Kw(cmd, "heat-capacity") is { } hc ? Num(hc, "sealed-air #:heat-capacity") : 0;
        Snapshot();
        Document.AddSealedAir(tanks, tube, loss, capacity);
        return $"sealed air over {string.Join(", ", tanks)}";
    }

    private string CreateCylinder(SList cmd)
    {
        string id = Id(cmd, 1);
        string head = ((SSymbol)cmd.Items[0]).Name;
        string piston = Name(RequireKw(cmd, "piston"), $"{head} {id} #:piston");
        string boiler = Name(RequireKw(cmd, "steam-from"), $"{head} {id} #:steam-from");
        double? injection = Kw(cmd, "injection-temperature") is { } t ? Num(t, $"{head} {id}") : null;
        Snapshot();
        string? crank = Kw(cmd, "crank") is SSymbol cs ? cs.Name : null;
        Document.AddCylinder(id, piston, boiler, injection, head == "steam-cylinder" ? "steam" : "atmospheric", crank);
        return $"cylinder {id}: {piston} fed by {boiler}";
    }

    /// <summary>(set-belt id #:tension N [#:material M]): tighten or slacken a belt.</summary>
    private string SetBelt(SList cmd)
    {
        string id = Id(cmd, 1);
        var belt = Document.Belts.FirstOrDefault(b => b.Id == id) ?? throw new InvalidOperationException($"no belt named {id}");
        var next = belt;
        if (Kw(cmd, "tension") is { } t) next = next with { Tension = Num(t, $"belt {id} #:tension") };
        if (Kw(cmd, "material") is SSymbol m) next = next with { Material = m.Name };
        if (next.Tension <= 0) throw new FormatException($"belt {id}: #:tension must be more than 0");
        Snapshot();
        Document.ReplaceBelt(next);
        return $"belt {id}: {next.Tension} N";
    }

    private string CreateWake(SList cmd)
    {
        string id = Id(cmd, 1);
        IReadOnlyList<WakeTerm> Terms(string key) => Kw(cmd, key) is SList l
            ? l.Items.Select(e => e is SList { Items.Count: 4 } t && t.Items[2] is SSymbol { Name: "above" or "below" } m
                ? new WakeTerm(Name(t.Items[0], $"wake {id} #:{key}"), Name(t.Items[1], $"wake {id} #:{key}"), m.Name == "above", Num(t.Items[3], $"wake {id} #:{key}"))
                : throw new FormatException($"wake {id} #:{key}: expected ((target field above|below value) …)")).ToList()
            : [];
        bool all = Kw(cmd, "join") is not SSymbol { Name: "or" };
        double limit = Kw(cmd, "limit") is { } lim ? Num(lim, $"wake {id} #:limit") : 3600;
        Snapshot();
        Document.AddWake(new WakeSpec(id, Terms("when"), all, limit, Terms("events")));
        return $"wake {id}: {Document.Wakes.Last().Describe()}";
    }

    private string CreateBelt(SList cmd)
    {
        string id = Id(cmd, 1), a = Id(cmd, 2), b = Id(cmd, 3);
        double tension = Num(RequireKw(cmd, "tension"), $"belt {id} #:tension");
        string material = Kw(cmd, "material") is SSymbol m ? m.Name : "hemp";
        Snapshot();
        Document.AddBelt(new BeltSpec(id, a, b, tension, material, null));
        return $"belt {id}: {a} and {b}, {tension} N";
    }

    /// <summary>Parts a joint can hold: the ones that move.</summary>
    private static readonly string[] JointableKinds = ["block", "pendulum", "lever", "wheel", "screw", "piston"];

    private string CreateJoint(SList cmd)
    {
        string id = Id(cmd, 1);
        string kind = RequireKw(cmd, "kind") is SSymbol k ? k.Name : throw new FormatException($"joint {id}: #:kind is pin, ball, universal or 6dof");
        if (!JointSpec.Kinds.Contains(kind)) throw new FormatException($"joint {id}: #:kind is pin, ball, universal or 6dof, not {kind}");
        string End(string key)
        {
            string part = RequireKw(cmd, key) is SSymbol s ? s.Name : throw new FormatException($"joint {id}: #:{key} names a part or world");
            if (part == "world") return part;
            var p = Document.Parts.GetValueOrDefault(part) ?? throw new InvalidOperationException($"joint {id}: no part named {part}");
            if (!JointableKinds.Contains(p.Kind))
                throw new InvalidOperationException($"joint {id}: {part} is a {p.Kind}, which doesn't move; a joint holds {string.Join(", ", JointableKinds)} or world");
            if (kind == "universal" && p.Kind is not ("wheel" or "screw"))
                throw new InvalidOperationException($"joint {id}: a universal joint joins two shafts (wheels or screws); {part} is a {p.Kind}");
            return part;
        }
        string a = End("a"), b = End("b");
        if (a == b) throw new FormatException($"joint {id}: a joint joins two different parts");
        if (kind == "universal" && (a == "world" || b == "world")) throw new FormatException($"joint {id}: a universal joint joins two shafts, not a shaft and the world");
        var at = VecOf(RequireKw(cmd, "at"), $"joint {id} #:at");
        Vec3? axis = Kw(cmd, "axis") is { } ax ? VecOf(ax, $"joint {id} #:axis") : null;
        if (kind == "pin" && axis is null) throw new FormatException($"joint {id}: a pin joint needs the #:axis (x y z) it turns about");
        var free = Kw(cmd, "free") is SList fl ? fl.Items.Select(e => Name(e, $"joint {id} #:free")).ToList() : [];
        if (free.Count > 0 && kind != "6dof") throw new FormatException($"joint {id}: only a 6dof joint takes #:free");
        if (free.FirstOrDefault(f => !JointSpec.Motions.Contains(f)) is { } bad) throw new FormatException($"joint {id}: #:free takes x y z rx ry rz, not {bad}");
        double? limit = Kw(cmd, "limit-deg") is { } l ? Num(l, $"joint {id} #:limit-deg") : null;
        if (limit is not null && kind != "ball") throw new FormatException($"joint {id}: only a ball joint takes #:limit-deg");
        Snapshot();
        Document.AddJoint(new JointSpec(id, kind, a, b, at, axis, free, limit, null));
        return $"{kind} joint {id}: {a} and {b}";
    }

    private string CreateFollow(SList cmd)
    {
        string id = Id(cmd, 1);
        string? lever = Kw(cmd, "lever") is SSymbol l ? l.Name : null, rope = Kw(cmd, "rope") is SSymbol r ? r.Name : null;
        double from = Num(RequireKw(cmd, "from"), $"follow {id} #:from"), to = Num(RequireKw(cmd, "to"), $"follow {id} #:to");
        if (RequireKw(cmd, "set") is not SList { Items.Count: 2 } set) throw new FormatException($"follow {id} #:set: expected (target field)");
        double low = Kw(cmd, "low") is { } lo ? Num(lo, $"follow {id} #:low") : 0, high = Kw(cmd, "high") is { } hi ? Num(hi, $"follow {id} #:high") : 1;
        Snapshot();
        Document.AddFollow(new FollowSpec(id, lever, rope, from, to, Name(set.Items[0], $"follow {id} #:set"), Name(set.Items[1], $"follow {id} #:set"), low, high, null));
        return $"follow {id}: {lever ?? rope} sets {set.Items[0]}.{set.Items[1]}";
    }

    private string CreateTrigger(SList cmd)
    {
        string id = Id(cmd, 1);
        var at = Kw(cmd, "at") is { } a ? VecOf(a, $"trigger {id} #:at") : (Vec3?)null;
        var size = Kw(cmd, "size") is { } z ? VecOf(z, $"trigger {id} #:size") : (Vec3?)null;
        string? body = Kw(cmd, "body") is SSymbol b ? b.Name : null;
        string? target = null, field = null;
        bool rising = true;
        double threshold = 0;
        if (Kw(cmd, "when") is { } w)
        {
            if (w is not SList { Items.Count: 4 } wl || wl.Items[2] is not SSymbol { Name: "above" or "below" } mode)
                throw new FormatException($"trigger {id} #:when: expected (target field above|below value)");
            target = Name(wl.Items[0], $"trigger {id} #:when");
            field = Name(wl.Items[1], $"trigger {id} #:when");
            rising = mode.Name == "above";
            threshold = Num(wl.Items[3], $"trigger {id} #:when");
        }
        var actions = new List<TriggerAction>();
        if (Kw(cmd, "do") is SList dl)
            foreach (var item in dl.Items)
            {
                if (item is not SList { Items.Count: 3 } al) throw new FormatException($"trigger {id} #:do: expected ((target field value) …)");
                actions.Add(new TriggerAction(Name(al.Items[0], $"trigger {id} #:do"), Name(al.Items[1], $"trigger {id} #:do"), Num(al.Items[2], $"trigger {id} #:do")));
            }
        Snapshot();
        Document.AddTrigger(new TriggerSpec(id, at, size, body, target, field, rising, threshold, actions, null));
        return $"trigger {id}: {(body is not null ? $"when {body} arrives" : $"when {target}.{field} goes {(rising ? "above" : "below")} {threshold}")}";
    }

    /// <summary>(port part name kind height): adds a port (or replaces the one with that name), height above the part's origin.</summary>
    private string SetPort(SList cmd)
    {
        string part = Id(cmd, 1), name = Id(cmd, 2), kind = Id(cmd, 3);
        double height = Num(cmd.Items.ElementAtOrDefault(4) ?? throw new FormatException("(port part name kind height)"), "port");
        Snapshot();
        Document.SetPort(part, new PortSpec(name, kind, height));
        return $"{part}.{name} ({kind}) at {height} m";
    }

    private string RemovePortCmd(SList cmd)
    {
        string part = Id(cmd, 1), name = Id(cmd, 2);
        Snapshot();
        Document.RemovePort(part, name);
        return $"removed {part}.{name}";
    }

    private string SetSource(SList cmd)
    {
        string text = cmd.Items.ElementAtOrDefault(1) is SString s ? s.Value : throw new FormatException("(source \"text\")");
        Snapshot();
        Document.Source = text;
        return "source set";
    }

    /// <summary>(raw-part (part id kind …)): a part clause exactly as a .machine file writes it. For a shaped part (a gear the Racket generator made) that no catalogue entry describes.</summary>
    private string RawPart(SList cmd)
    {
        if (cmd.Items.ElementAtOrDefault(1) is not SList { Head: "part" } clause)
            throw new FormatException("(raw-part (part id kind …))");
        var def = MachineDef.Parse($"(machine raw {SExprWriter.Print(clause)})");
        var part = def.Parts.Single();
        Snapshot();
        Document.AddPart(part);
        return $"placed {part.Id} ({part.Kind})";
    }

    /// <summary>(sun #:latitude deg #:day n #:time hours): where and when the scene stands; unspecified fields keep their current (or default) values.</summary>
    private string SetSun(SList cmd)
    {
        var now = Document.Sun ?? new SunSpec(31.2, 172, 12);
        double lat = Kw(cmd, "latitude") is { } l ? Num(l, "sun #:latitude") : now.Latitude;
        int day = Kw(cmd, "day") is { } d ? (int)Num(d, "sun #:day") : now.Day;
        double time = Kw(cmd, "time") is { } t ? Num(t, "sun #:time") : now.Time;
        if (lat is < -90 or > 90) throw new FormatException($"(sun #:latitude {lat}): must be in [-90, 90]");
        if (day < 1 || day > Document.Planet.Year) throw new FormatException($"(sun #:day {day}): must be 1 to {Document.Planet.Year}");
        if (time is < 0 or >= 24) throw new FormatException($"(sun #:time {time}): must be solar hours in [0, 24)");
        Snapshot();
        Document.Sun = new SunSpec(lat, day, time);
        return $"sun at {lat}° N, day {day}, {time} h";
    }

    /// <summary>
    /// (planet id #:key value …): stands the scene on a preset planet (earth,
    /// mars), with any of its numbers changed; #:air takes the whole mixture,
    /// ((o2 0.21) (n2 0.79)). The scene's air takes the planet's temperature,
    /// as define-machine's does when it gives no #:ambient.
    /// </summary>
    private string SetPlanet(SList cmd)
    {
        var planet = Planet.Named(Id(cmd, 1));
        foreach (var key in Planet.NumberKeys)
            if (Kw(cmd, key) is { } v) planet = planet.With(key, Num(v, $"planet #:{key}"));
        if (Kw(cmd, "air") is { } air)
        {
            if (air is not SList gases) throw new FormatException("planet #:air: expected ((gas fraction) ...)");
            var f = new Dictionary<string, double>();
            foreach (var g in gases.Items)
            {
                if (g is not SList { Items: [SSymbol name, var x] } || !GasMix.Names.Contains(name.Name))
                    throw new FormatException($"planet #:air: expected (gas fraction) with a gas among {string.Join(", ", GasMix.Names)}");
                f[name.Name] = Num(x, "planet #:air");
            }
            double G(string n) => f.GetValueOrDefault(n);
            planet = planet.WithAir(new GasMix(G("o2"), G("n2"), G("co2"), G("h2o"), G("ar")));
        }
        Snapshot();
        Document.Planet = planet;
        Document.Ambient = planet.Temperature;
        if (Document.Sun is { } sun && sun.Day > planet.Year) Document.Sun = sun with { Day = (int)planet.Year };
        return $"on {planet.Name}: g {planet.Gravity} m/s², {planet.Pressure} Pa, air {planet.Temperature} °C";
    }

    /// <summary>(weather #:daily b #:passes (h …) #:pass-minutes m): the scene's sols (issue #69); unsaid fields keep their values, and its storms stay.</summary>
    private string SetWeather(SList cmd)
    {
        var now = Document.Weather ?? new WeatherSpec(true, [3, 15], 10, []);
        bool daily = Kw(cmd, "daily") is SBool b ? b.Value : now.Daily;
        var passes = Kw(cmd, "passes") is SList pl ? pl.Items.Select(h => Num(h, "weather #:passes")).ToList() : now.Passes;
        if (passes.Any(h => h is < 0 or >= 24)) throw new FormatException("weather #:passes are local solar hours in [0, 24)");
        double minutes = Kw(cmd, "pass-minutes") is { } m ? Num(m, "weather #:pass-minutes") : now.PassMinutes;
        if (minutes <= 0) throw new FormatException("weather #:pass-minutes must be above 0");
        Snapshot();
        Document.Weather = new WeatherSpec(daily, passes, minutes, now.Storms);
        return $"weather: the air {(daily ? "follows the day" : "holds still")}, relay passes at {string.Join(", ", passes)} h";
    }

    /// <summary>(storm #:sol n #:hour h #:tau τ #:sols d #:settle k): adds a dust storm to the scene's weather.</summary>
    private string AddStorm(SList cmd)
    {
        var now = Document.Weather ?? new WeatherSpec(true, [3, 15], 10, []);
        int sol = (int)Num(RequireKw(cmd, "sol"), "storm #:sol");
        double hour = Kw(cmd, "hour") is { } h ? Num(h, "storm #:hour") : 0;
        double tau = Num(RequireKw(cmd, "tau"), "storm #:tau");
        double sols = Kw(cmd, "sols") is { } d ? Num(d, "storm #:sols") : 1;
        double settle = Kw(cmd, "settle") is { } k ? Num(k, "storm #:settle") : 0.5;
        if (sol < 1 || hour is < 0 or >= 24 || tau < 0 || sols <= 0 || settle < 0)
            throw new FormatException("(storm #:sol n≥1 #:hour [0,24) #:tau ≥0 #:sols >0 #:settle ≥0)");
        Snapshot();
        Document.Weather = now with { Storms = [.. now.Storms, new StormSpec(sol, hour, tau, sols, settle)] };
        return $"storm on sol {sol} from {hour} h, τ {tau}, {sols} sols";
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

    /// <summary>
    /// (turn id degrees): turns a part by that many degrees about the vertical, counter-clockwise seen from above,
    /// on top of whatever heading it has (issue #83); its pivot stays where it is. Only the kinds that can stand at a
    /// heading (<see cref="MachineDef.TurnableKinds"/>) turn: the rest are built along the axes.
    /// </summary>
    private string Turn(SList cmd)
    {
        string id = Id(cmd, 1);
        if (cmd.Items.Count != 3) throw new FormatException("(turn id degrees)");
        double degrees = Num(cmd.Items[2], "turn");
        if (!Document.Parts.TryGetValue(id, out var part)) throw new InvalidOperationException($"no part named {id}");
        if (!MachineDef.TurnableKinds.Contains(part.Kind))
            throw new InvalidOperationException($"{id} is a {part.Kind}, which is built along the axes and can't be turned (only {string.Join(", ", MachineDef.TurnableKinds)} can)");
        double heading = ((MachineDef.HeadingOf(part) + degrees) % 360 + 360) % 360;
        Snapshot();
        Document.SetProp(id, "heading-deg", heading);
        return $"turned {id} to heading {heading}";
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
        var value = cmd.Items[3];
        Document.Parts.TryGetValue(id, out var part);
        var current = part?.Props.GetValueOrDefault(key);
        switch (value)
        {
            case SBool or SString or SList:
                Snapshot();
                Document.SetValue(id, key, value);
                return $"set {id} #:{key} {SExprWriter.Print(value)}";
            case SSymbol name when !Units.LooksNumeric(name.Name):
                // A prop that points at another part (a mirror's #:onto, a pump's #:from) must point at one; any other symbol (#:axis y, #:fuel-kind coal) is taken as written.
                // world is a rope's or grip's fixed point; outside, the planet's open air, a zone a door or air pump can join
                if ((current is SSymbol { Name: "?" } || PartReferenceKeys.Contains(key)) && name.Name != "world" && !Document.HasName(name.Name)
                    && !(name.Name == "outside" && part?.Kind is "door" or "air-pump"))
                    throw new InvalidOperationException($"no part or link named {name.Name}");
                Snapshot();
                Document.SetName(id, key, name.Name);
                return $"set {id} #:{key} {name.Name}";
        }
        double number = Num(value, "set");
        Snapshot();
        Document.SetProp(id, key, number);
        return $"set {id} #:{key} {number}";
    }

    /// <summary>Props whose value names another part, checked against the document when set.</summary>
    private static readonly HashSet<string> PartReferenceKeys = ["on", "onto", "heats", "over", "from", "to", "vessel", "into", "tail", "race", "gutter", "water", "store"];

    private string Remove(SList cmd)
    {
        string id = Id(cmd, 1);
        Snapshot();
        if (!Document.Parts.ContainsKey(id) && Document.RemoveLink(id)) return $"removed {id}";
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
