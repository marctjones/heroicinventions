namespace HeroicInventions.Sim.Machines;

public readonly record struct Vec3(double X, double Y, double Z);

/// <summary>Where a clause came from in the Racket source, carried through so runtime errors can point back to it.</summary>
public sealed record SourceLocation(string File, int Line, int Column)
{
    public override string ToString() => $"{File}:{Line}:{Column}";
}

/// <summary>A problem in a .machine file, reported against the Racket line that produced it.</summary>
public sealed class MachineFormatException(string message, SourceLocation? location = null)
    : Exception(location is null ? message : $"{location}: {message}")
{
    public SourceLocation? Location { get; } = location;
}

public sealed record PortSpec(string Name, string Kind, double Height);

public sealed record PortRef(string Part, string Port)
{
    public override string ToString() => $"{Part}.{Port}";
}

public sealed record PartSpec(
    string Id,
    string Kind,
    string Material,
    Vec3 At,
    IReadOnlyDictionary<string, SExpr> Props,
    IReadOnlyList<PortSpec> Ports,
    SourceLocation? Location)
{
    public double Number(string key) =>
        Props.TryGetValue(key, out var v) && v is SNumber n
            ? n.Value
            : throw new MachineFormatException($"{Kind} {Id} needs a numeric {key}", Location);

    public double Number(string key, double fallback) =>
        Props.TryGetValue(key, out var v) && v is SNumber n ? n.Value : fallback;

    /// <summary>A string prop, such as a generated shape's mesh file stem.</summary>
    public string Text(string key) =>
        Props.TryGetValue(key, out var v) && v is SString s
            ? s.Value
            : throw new MachineFormatException($"{Kind} {Id} needs a string {key}", Location);

    /// <summary>A symbol prop, such as a wheel's axis (x, y or z).</summary>
    public string Symbol(string key, string fallback) =>
        Props.TryGetValue(key, out var v) && v is SSymbol s ? s.Name : fallback;

    public PortSpec Port(string name, SourceLocation? usedAt) =>
        Ports.FirstOrDefault(p => p.Name == name)
        ?? throw new MachineFormatException($"{Kind} {Id} has no port named {name}", usedAt);
}

public sealed record PipeSpec(string Id, PortRef From, PortRef To, double Conductance, bool Jet, SourceLocation? Location);
public sealed record ConnectSpec(PortRef A, PortRef B, SourceLocation? Location);
public sealed record SealedAirSpec(IReadOnlyList<string> Tanks, double TubeVolume, SourceLocation? Location);

/// <summary>Water lifted by a screw or noria (By) from one tank to another; Current is a river's speed for a noria.</summary>
public sealed record LiftSpec(string Id, string By, string From, string To, double? Current, SourceLocation? Location);

/// <summary>A Newcomen atmospheric cylinder driving Piston, with steam from Boiler.</summary>
public sealed record CylinderSpec(string Id, string Piston, string Boiler, double InjectionTemperature, SourceLocation? Location);

/// <summary>Two gears in mesh.</summary>
public sealed record MeshSpec(string A, string B, SourceLocation? Location);

/// <summary>Wheels fixed on one axle; the first carries the bearing and any drive.</summary>
public sealed record ArborSpec(IReadOnlyList<string> Parts, SourceLocation? Location);

/// <summary>A point on a part, in the part's own frame; Part is "world" for a fixed point in world coordinates.</summary>
public sealed record RopeEnd(string Part, Vec3 Local);

/// <summary>
/// A rope or chain: pulls its two ends together once taut, never pushes.
/// Runs over fixed <see cref="Over"/> points in order. WindOn names a wheel
/// the From end winds onto; ReleaseDeg, when set, lets go of the To end
/// once the rope points within that many degrees of straight out along the
/// From part (a trebuchet sling slipping off its pin).
/// </summary>
public sealed record RopeSpec(
    string Id, RopeEnd From, RopeEnd To, double Length, IReadOnlyList<Vec3> Over,
    string? WindOn, double? ReleaseDeg, string Material, double Diameter, SourceLocation? Location)
{
    /// <summary>The To end is nocked, not tied: the rope can drive it but lets go rather than pull it back.</summary>
    public bool Nocked { get; init; }
    /// <summary>A pulley wheel the rope runs over (at its first Over point), turned to keep pace with the rope.</summary>
    public string? Turns { get; init; }
}

/// <summary>
/// A machine as written by #lang heroic: parts with positions, materials
/// and ports, plus the pipes, steam connections and sealed-air groups
/// between them. Pure data; <see cref="MachineRuntime"/> makes it run.
/// </summary>
public sealed class MachineDef
{
    public required string Name { get; init; }
    public string? Source { get; init; }
    public required IReadOnlyList<PartSpec> Parts { get; init; }
    public required IReadOnlyList<PipeSpec> Pipes { get; init; }
    public required IReadOnlyList<ConnectSpec> Connects { get; init; }
    public required IReadOnlyList<SealedAirSpec> SealedAir { get; init; }
    public IReadOnlyList<RopeSpec> Ropes { get; init; } = [];
    public IReadOnlyList<ArborSpec> Arbors { get; init; } = [];
    public IReadOnlyList<MeshSpec> Meshes { get; init; } = [];
    public IReadOnlyList<LiftSpec> Lifts { get; init; } = [];
    public IReadOnlyList<CylinderSpec> Cylinders { get; init; } = [];

    public PartSpec? Part(string id) => Parts.FirstOrDefault(p => p.Id == id);

    public static MachineDef Parse(string text)
    {
        var forms = SExprReader.ReadAll(text);
        if (forms.Count != 1 || forms[0] is not SList { Head: "machine" } m || m.Items.Count < 2 || m.Items[1] is not SSymbol name)
            throw new MachineFormatException("a .machine file must contain exactly one (machine name …) form");

        var clauses = m.Items.Skip(2).OfType<SList>().ToList();
        return new MachineDef
        {
            Name = name.Name,
            Source = clauses.FirstOrDefault(c => c.Head == "source")?.Items.ElementAtOrDefault(1) is SString s ? s.Value : null,
            Parts = clauses.Where(c => c.Head == "part").Select(ParsePart).ToList(),
            Pipes = clauses.Where(c => c.Head == "pipe").Select(ParsePipe).ToList(),
            Connects = clauses.Where(c => c.Head == "connect").Select(ParseConnect).ToList(),
            SealedAir = clauses.Where(c => c.Head == "sealed-air").Select(ParseSealedAir).ToList(),
            Ropes = clauses.Where(c => c.Head == "rope").Select(ParseRope).ToList(),
            Lifts = clauses.Where(c => c.Head == "lift").Select(c =>
            {
                var loc = ParseLoc(c);
                string Field(string f) => c.Field(f) is { } l ? Sym(l, 1, loc) : throw new MachineFormatException($"lift has no {f}", loc);
                return new LiftSpec(Sym(c, 1, loc), Field("by"), Field("from"), Field("to"),
                                    c.Field("current")?.Items.ElementAtOrDefault(1) is SNumber n ? n.Value : null, loc);
            }).ToList(),
            Cylinders = clauses.Where(c => c.Head == "atmospheric-cylinder").Select(c =>
            {
                var loc = ParseLoc(c);
                string Field(string f) => c.Field(f) is { } l ? Sym(l, 1, loc) : throw new MachineFormatException($"atmospheric-cylinder has no {f}", loc);
                return new CylinderSpec(Sym(c, 1, loc), Field("piston"), Field("steam-from"),
                                        c.Field("injection-temperature") is { } t ? Num(t, 1, loc) : 60, loc);
            }).ToList(),
            Meshes = clauses.Where(c => c.Head == "mesh").Select(c =>
            {
                var loc = ParseLoc(c);
                return new MeshSpec(Sym(c, 1, loc), Sym(c, 2, loc), loc);
            }).ToList(),
            Arbors = clauses.Where(c => c.Head == "arbor").Select(c =>
            {
                var loc = ParseLoc(c);
                var parts = c.Field("parts") ?? throw new MachineFormatException("arbor has no parts", loc);
                return new ArborSpec(parts.Items.Skip(1).Select((_, i) => Sym(parts, i + 1, loc)).ToList(), loc);
            }).ToList(),
        };
    }

    // (part id kind (material m) (at x y z) (props (k v) …) (ports (name kind height) …) (srcloc file line col))
    private static PartSpec ParsePart(SList c)
    {
        var loc = ParseLoc(c);
        string id = Sym(c, 1, loc);
        var at = c.Field("at") ?? throw new MachineFormatException($"part {id} has no position", loc);
        return new PartSpec(
            id,
            Sym(c, 2, loc),
            c.Field("material") is { } mat ? Sym(mat, 1, loc) : throw new MachineFormatException($"part {id} has no material", loc),
            new Vec3(Num(at, 1, loc), Num(at, 2, loc), Num(at, 3, loc)),
            (c.Field("props")?.Items.Skip(1) ?? [])
                .OfType<SList>()
                .ToDictionary(kv => Sym(kv, 0, loc), kv => kv.Items.ElementAtOrDefault(1) ?? new SBool(false)),
            (c.Field("ports")?.Items.Skip(1) ?? [])
                .OfType<SList>()
                .Select(p => new PortSpec(Sym(p, 0, loc), Sym(p, 1, loc), Num(p, 2, loc)))
                .ToList(),
            loc);
    }

    // (pipe id (from part port) (to part port) (conductance c) (jet bool) (srcloc …))
    private static PipeSpec ParsePipe(SList c)
    {
        var loc = ParseLoc(c);
        string id = Sym(c, 1, loc);
        return new PipeSpec(
            id,
            Ref(c.Field("from"), 1, loc),
            Ref(c.Field("to"), 1, loc),
            c.Field("conductance") is { } k ? Num(k, 1, loc) : throw new MachineFormatException($"pipe {id} has no conductance", loc),
            c.Field("jet")?.Items.ElementAtOrDefault(1) is SBool { Value: true },
            loc);
    }

    // (connect (part port) (part port) (srcloc …))
    private static ConnectSpec ParseConnect(SList c)
    {
        var loc = ParseLoc(c);
        return new ConnectSpec(Ref(c.Items.ElementAtOrDefault(1) as SList, 0, loc), Ref(c.Items.ElementAtOrDefault(2) as SList, 0, loc), loc);
    }

    // (rope id (from part x y z) (to part x y z) (length l) (over (x y z) …)
    //       (wind-on part|#f) (release-deg d|#f) (material m) (diameter d) (srcloc …))
    private static RopeSpec ParseRope(SList c)
    {
        var loc = ParseLoc(c);
        string id = Sym(c, 1, loc);
        RopeEnd End(string field)
        {
            var e = c.Field(field) ?? throw new MachineFormatException($"rope {id} has no {field} end", loc);
            return new RopeEnd(Sym(e, 1, loc), new Vec3(Num(e, 2, loc), Num(e, 3, loc), Num(e, 4, loc)));
        }
        return new RopeSpec(
            id, End("from"), End("to"),
            c.Field("length") is { } l ? Num(l, 1, loc) : throw new MachineFormatException($"rope {id} has no length", loc),
            (c.Field("over")?.Items.Skip(1) ?? []).OfType<SList>()
                .Select(p => new Vec3(Num(p, 0, loc), Num(p, 1, loc), Num(p, 2, loc))).ToList(),
            c.Field("wind-on")?.Items.ElementAtOrDefault(1) is SSymbol w ? w.Name : null,
            c.Field("release-deg")?.Items.ElementAtOrDefault(1) is SNumber r ? r.Value : null,
            c.Field("material") is { } m ? Sym(m, 1, loc) : "hemp",
            c.Field("diameter") is { } d ? Num(d, 1, loc) : 0.02,
            loc)
        {
            Nocked = c.Field("nocked")?.Items.ElementAtOrDefault(1) is SBool { Value: true },
            Turns = c.Field("turns")?.Items.ElementAtOrDefault(1) is SSymbol t ? t.Name : null,
        };
    }

    // (sealed-air (tanks a b …) (tube-volume v) (srcloc …))
    private static SealedAirSpec ParseSealedAir(SList c)
    {
        var loc = ParseLoc(c);
        var tanks = c.Field("tanks") ?? throw new MachineFormatException("sealed-air has no tanks", loc);
        return new SealedAirSpec(
            tanks.Items.Skip(1).Select((_, i) => Sym(tanks, i + 1, loc)).ToList(),
            c.Field("tube-volume") is { } v ? Num(v, 1, loc) : 0,
            loc);
    }

    private static SourceLocation? ParseLoc(SList c) =>
        c.Field("srcloc") is { Items: [_, SString file, SNumber line, SNumber col] }
            ? new SourceLocation(file.Value, (int)line.Value, (int)col.Value)
            : null;

    private static PortRef Ref(SList? l, int start, SourceLocation? loc) =>
        l is null
            ? throw new MachineFormatException("expected a (part port) reference", loc)
            : new PortRef(Sym(l, start, loc), Sym(l, start + 1, loc));

    private static string Sym(SList l, int i, SourceLocation? loc) =>
        l.Items.ElementAtOrDefault(i) is SSymbol s
            ? s.Name
            : throw new MachineFormatException($"expected a name at position {i} of ({l.Head} …)", loc);

    private static double Num(SList l, int i, SourceLocation? loc) =>
        l.Items.ElementAtOrDefault(i) is SNumber n
            ? n.Value
            : throw new MachineFormatException($"expected a number at position {i} of ({l.Head} …)", loc);
}
