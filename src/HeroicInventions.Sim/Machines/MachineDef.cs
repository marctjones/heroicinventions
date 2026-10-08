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

/// <summary>Where and when the scene stands under the sun: latitude (degrees north), day of the year, solar time (hours).</summary>
public sealed record SunSpec(double Latitude, int Day, double Time);

/// <summary>A dust storm (issue #69): from Hour of the run's Sol (1 first), for Sols sols, the air's dust optical depth is Tau; mirrors lose Settle of what they reflect a sol.</summary>
public sealed record StormSpec(int Sol, double Hour, double Tau, double Sols, double Settle);

/// <summary>Time and weather (issue #69): the air on the planet's daily curve (Daily), relay passes at local hours, dust storms.</summary>
public sealed record WeatherSpec(bool Daily, IReadOnlyList<double> Passes, double PassMinutes, IReadOnlyList<StormSpec> Storms)
{
    public bool Equals(WeatherSpec? o) => o is not null && Daily == o.Daily && PassMinutes == o.PassMinutes
        && Passes.SequenceEqual(o.Passes) && Storms.SequenceEqual(o.Storms);
    public override int GetHashCode() => HashCode.Combine(Daily, PassMinutes, Passes.Count, Storms.Count);
}

public sealed record PipeSpec(string Id, PortRef From, PortRef To, double Conductance, bool Jet, SourceLocation? Location);
public sealed record ConnectSpec(PortRef A, PortRef B, SourceLocation? Location);
public sealed record SealedAirSpec(IReadOnlyList<string> Tanks, double TubeVolume, SourceLocation? Location,
                                   double HeatLoss = 0, double HeatCapacity = 0);

/// <summary>
/// Water lifted by a screw, noria or pump (By) from one tank to another.
/// A noria's current is either a fixed speed (Current) or the speed of the
/// water in a channel (CurrentFrom) running at its paddles.
/// </summary>
public sealed record LiftSpec(string Id, string By, string From, string To, double? Current, SourceLocation? Location)
{
    public string? CurrentFrom { get; init; }
}

/// <summary>Water arriving from outside the scene at a steady Flow (m³/s) into a tank.</summary>
public sealed record SourceSpec(string Id, string Into, double Flow, SourceLocation? Location);

/// <summary>An open channel from a tank's port to another's (To), or out of the scene to End.</summary>
public sealed record ChannelSpec(string Id, PortRef From, PortRef? To, Vec3? End, double Width, double? Length, SourceLocation? Location,
                          IReadOnlyList<(double X, double Z)>? Via = null, string? Onto = null)
{
    /// <summary>The channel holds water along its reach, solved as a 1-D shallow-water wave (issue #36); false, the steady channel.</summary>
    public bool Dynamic { get; init; }
    /// <summary>How many cells a dynamic reach is cut into; null, one per half metre (10 to 400).</summary>
    public int? Cells { get; init; }
}

/// <summary>A Newcomen atmospheric cylinder driving Piston, with steam from Boiler.</summary>
/// <summary>InjectionTemperature null: the jet water warms 40 K over the ambient it is drawn at.</summary>
public sealed record CylinderSpec(string Id, string Piston, string Boiler, double? InjectionTemperature, SourceLocation? Location)
{
    /// <summary>"atmospheric" (Newcomen: the air pushes, condensed steam pulls) or "steam" (high-pressure: the boiler's steam pushes).</summary>
    public string Kind { get; init; } = "atmospheric";
    /// <summary>A steam cylinder's crank: the wheel whose turn works its valve (an eccentric), or null.</summary>
    public string? Crank { get; init; }
}

/// <summary>Two gears in mesh.</summary>
public sealed record MeshSpec(string A, string B, SourceLocation? Location)
{
    /// <summary>The share of the power passing through that arrives (issue #113): 1 for a perfect mesh.</summary>
    public double Efficiency { get; init; } = 1;
}

/// <summary>Wheels fixed on one axle; the first carries the bearing and any drive. One part may be a water wheel, windmill or jet wheel, which the sim turns: its axle drives the wheels and they load it (issue #113).</summary>
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
    /// <summary>
    /// The material of fixed bars at the Over points, or null for turning
    /// pulleys: the rope drags over bars, and its tight side can carry up to
    /// e^(μθ) times its slack side (the capstan equation), θ the angle it
    /// turns through over all of them.
    /// </summary>
    public string? Bar { get; init; }
    /// <summary>Friction over the bars, overriding √(μ_rope·μ_bar); null to use the materials'.</summary>
    public double? Mu { get; init; }
    /// <summary>A chain (issue #31): the rope built as this many rigid links pinned end to end, sagging and swinging as a real chain does; null, the lumped rope.</summary>
    public int? Links { get; init; }
    /// <summary>A tether (issue #155): the rope holds until its field <c>tether</c> is set to 0, then lets go of both ends.</summary>
    public bool Tether { get; init; }
    /// <summary>A tether lets go by itself this many seconds into the run (until demo operators, #153); null: only on command.</summary>
    public double? ReleaseAfter { get; init; }
}

/// <summary>One thing a trigger does when it fires: sets a runtime field, as <c>(target field value)</c>.</summary>
public sealed record TriggerAction(string Target, string Field, double Value);

/// <summary>
/// A sensor that acts when something arrives (issue #32). It watches either a
/// body — fires once when the part <see cref="Body"/>'s centre enters the box
/// of extent <see cref="Size"/> centred on <see cref="At"/> — or a runtime field,
/// firing once when <see cref="WatchTarget"/>.<see cref="WatchField"/> rises above
/// (or, with <see cref="Rising"/> false, falls below) <see cref="Threshold"/>.
/// Firing applies each of <see cref="Actions"/> through the field setters.
/// </summary>
public sealed record TriggerSpec(string Id, Vec3? At, Vec3? Size, string? Body, string? WatchTarget, string? WatchField,
                                 bool Rising, double Threshold, IReadOnlyList<TriggerAction> Actions, SourceLocation? Location);

/// <summary>
/// A field that follows a mechanism (issue #46): the value of Target.Field is
/// set every tick from where a lever has turned (its angle in degrees from its
/// start, when <see cref="Lever"/> is named) or how hard a rope pulls (its
/// tension in N, when <see cref="Rope"/> is). The input runs from <see cref="From"/>
/// to <see cref="To"/>, and the field from <see cref="Low"/> to <see cref="High"/>, linearly and
/// held at the ends: a plug lifted part-way lets part of the flow through.
/// </summary>
/// <summary>
/// A joint between two moving parts, or a part and the world (issue #30).
/// Kind: <c>pin</c> (a hinge about <see cref="Axis"/> through <see cref="At"/>),
/// <c>ball</c> (turns every way about the point, within <see cref="LimitDeg"/>
/// of where it started if given), <c>universal</c> (a Cardan cross between two
/// shafts, its arms set by their axles), <c>6dof</c> (locked but for the
/// motions in <see cref="Free"/>: x y z sliding, rx ry rz turning).
/// A and B are part ids or <c>world</c>.
/// </summary>
public sealed record JointSpec(string Id, string Kind, string A, string B, Vec3 At, Vec3? Axis,
                               IReadOnlyList<string> Free, double? LimitDeg, SourceLocation? Location)
{
    public static readonly IReadOnlyList<string> Kinds = ["pin", "ball", "universal", "6dof"];
    public static readonly IReadOnlyList<string> Motions = ["x", "y", "z", "rx", "ry", "rz"];
}

public sealed record FollowSpec(string Id, string? Lever, string? Rope, double From, double To,
                                string Target, string Field, double Low, double High, SourceLocation? Location);

/// <summary>An open belt between two drums, pretensioned to Tension N, of a Material whose friction grips them (issue #51).</summary>
public sealed record BeltSpec(string Id, string A, string B, double Tension, string Material, SourceLocation? Location);

/// <summary>
/// A machine as written by #lang heroic: parts with positions, materials
/// and ports, plus the pipes, steam connections and sealed-air groups
/// between them. Pure data; <see cref="MachineRuntime"/> makes it run.
/// </summary>
public sealed class MachineDef
{
    public required string Name { get; init; }
    public string? Source { get; init; }
    /// <summary>The air round the machine, °C: what boilers cool towards, what water and air arrive at, whether tanks freeze.</summary>
    public double Ambient { get; init; } = 20;
    /// <summary>Null: the default sun, Alexandria at noon on midsummer's day, and the scene keeps its fixed studio light.</summary>
    public SunSpec? Sun { get; init; }
    /// <summary>The planet it stands on (issue #38): gravity, the air's pressure and mix, sunlight, the day's length. Earth unless the file says otherwise.</summary>
    public Planet Planet { get; init; } = Planet.Earth;
    /// <summary>Sols, the daily air, relay passes and dust storms (issue #69); null: none.</summary>
    public WeatherSpec? Weather { get; init; }
    public required IReadOnlyList<PartSpec> Parts { get; init; }
    public required IReadOnlyList<PipeSpec> Pipes { get; init; }
    public required IReadOnlyList<ConnectSpec> Connects { get; init; }
    public required IReadOnlyList<SealedAirSpec> SealedAir { get; init; }
    public IReadOnlyList<RopeSpec> Ropes { get; init; } = [];
    public IReadOnlyList<ArborSpec> Arbors { get; init; } = [];
    public IReadOnlyList<MeshSpec> Meshes { get; init; } = [];
    public IReadOnlyList<LiftSpec> Lifts { get; init; } = [];
    public IReadOnlyList<SourceSpec> Sources { get; init; } = [];
    public IReadOnlyList<ChannelSpec> Channels { get; init; } = [];
    public IReadOnlyList<CylinderSpec> Cylinders { get; init; } = [];
    public IReadOnlyList<TriggerSpec> Triggers { get; init; } = [];
    public IReadOnlyList<FollowSpec> Follows { get; init; } = [];
    public IReadOnlyList<BeltSpec> Belts { get; init; } = [];
    /// <summary>Named things to sleep until (issue #59), offered as presets by the game's sleep control.</summary>
    public IReadOnlyList<WakeSpec> Wakes { get; init; } = [];
    public IReadOnlyList<JointSpec> Joints { get; init; } = [];
    /// <summary>A demo operator's actions (issue #153), in time order: what a person's hand does when nobody else acts.</summary>
    public IReadOnlyList<OperatorAction> Operator { get; init; } = [];

    public PartSpec? Part(string id) => Parts.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// The same machine moved by <paramref name="offset"/>: every position it
    /// holds in world coordinates (its parts, its channels' ends and bends, its
    /// ropes' over-points) shifted together, so it stands somewhere else in a
    /// shared world and behaves exactly as it did. Rope ends are local to their
    /// parts and move with them. Only translation: a heading would also have to
    /// turn every part's axis, which is written as a symbol (x, z) and only
    /// quarter-turns map cleanly.
    /// </summary>
    public MachineDef Translated(Vec3 offset, string? name = null)
    {
        Vec3 Move(Vec3 v) => new(v.X + offset.X, v.Y + offset.Y, v.Z + offset.Z);
        return new MachineDef
        {
            Name = name ?? Name,
            Source = Source,
            Ambient = Ambient,
            Sun = Sun,
            Planet = Planet,
            Weather = Weather,
            Parts = Parts.Select(p => p with { At = Move(p.At) }).ToList(),
            Pipes = Pipes,
            Connects = Connects,
            SealedAir = SealedAir,
            Ropes = Ropes.Select(r => r with { Over = r.Over.Select(Move).ToList() }).ToList(),
            Arbors = Arbors,
            Meshes = Meshes,
            Lifts = Lifts,
            Sources = Sources,
            Channels = Channels.Select(c => c with
            {
                End = c.End is { } e ? Move(e) : null,
                Via = c.Via?.Select(v => (v.X + offset.X, v.Z + offset.Z)).ToList(),
            }).ToList(),
            Cylinders = Cylinders,
            Triggers = Triggers.Select(t => t with { At = t.At is { } a ? Move(a) : null }).ToList(),
            Follows = Follows,
            Belts = Belts,
            Wakes = Wakes,
            Joints = Joints.Select(j => j with { At = Move(j.At) }).ToList(),
            Operator = Operator,
        };
    }

    /// <summary>The kinds of part that know how to stand at a heading (issue #83): the bodies the engine turns, which the view builds in a yawed frame.</summary>
    public static readonly IReadOnlyList<string> TurnableKinds = ["block", "ball", "pendulum", "lever", "ramp", "wheel", "screw", "fixture", "post"];

    /// <summary>A part's heading: its yaw in degrees about the vertical, counter-clockwise seen from above (the sense Godot turns a body about +Y); 0 if it has none.</summary>
    public static double HeadingOf(PartSpec part) => part.Number("heading-deg", 0);

    /// <summary>
    /// The same machine turned <paramref name="degrees"/> about the vertical through
    /// its origin (issue #83): every position it holds (parts, rope over-points
    /// and world ends, channel ends and bends, triggers, joints and their axes)
    /// swung round together, and each part given that much more heading, so the
    /// view builds its body, its axle and its slope in the turned frame. It
    /// behaves exactly as the unturned machine does, its traces the same with
    /// positions and velocities turned. Only the kinds in <see cref="TurnableKinds"/>
    /// can be turned, and no trigger boxes: the others build their geometry along
    /// the axes, and a heading silently ignored would put them wrong; asking
    /// for one raises a <see cref="MachineFormatException"/> naming the part.
    /// </summary>
    public MachineDef Turned(double degrees)
    {
        if (degrees % 360 == 0) return this;
        foreach (var p in Parts.Where(p => !TurnableKinds.Contains(p.Kind)))
            throw new MachineFormatException($"machine {Name} can't be turned: {p.Kind} {p.Id} is built along the axes (only {string.Join(", ", TurnableKinds)} can stand at a heading)", p.Location);
        if (Triggers.FirstOrDefault(t => t.Size is not null) is { } box)
            throw new MachineFormatException($"machine {Name} can't be turned: trigger {box.Id} watches a box along the axes", box.Location);
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        // Basis(Up, a) maps +X to (cos a, 0, -sin a) and +Z to (sin a, 0, cos a)
        Vec3 Swing(Vec3 v) => new(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        (double X, double Z) SwingXz((double X, double Z) v) => (v.X * c + v.Z * s, -v.X * s + v.Z * c);
        RopeEnd End(RopeEnd e) => e.Part == "world" ? e with { Local = Swing(e.Local) } : e;   // a part's own end turns with the part
        PartSpec Turn(PartSpec p) => p with
        {
            At = Swing(p.At),
            Props = new Dictionary<string, SExpr>(p.Props) { ["heading-deg"] = new SNumber(HeadingOf(p) + degrees) },
        };
        return new MachineDef
        {
            Name = Name,
            Source = Source,
            Ambient = Ambient,
            Sun = Sun,
            Planet = Planet,
            Weather = Weather,
            Parts = Parts.Select(Turn).ToList(),
            Pipes = Pipes,
            Connects = Connects,
            SealedAir = SealedAir,
            Ropes = Ropes.Select(r => r with { From = End(r.From), To = End(r.To), Over = r.Over.Select(Swing).ToList() }).ToList(),
            Arbors = Arbors,
            Meshes = Meshes,
            Lifts = Lifts,
            Sources = Sources,
            Channels = Channels.Select(ch => ch with
            {
                End = ch.End is { } e ? Swing(e) : null,
                Via = ch.Via?.Select(SwingXz).ToList(),
            }).ToList(),
            Cylinders = Cylinders,
            Triggers = Triggers.Select(t => t with { At = t.At is { } at ? Swing(at) : null }).ToList(),
            Follows = Follows,
            Belts = Belts,
            Wakes = Wakes,
            Joints = Joints.Select(j => j with { At = Swing(j.At), Axis = j.Axis is { } ax ? Swing(ax) : null }).ToList(),
            Operator = Operator,
        };
    }

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
            Ambient = clauses.FirstOrDefault(c => c.Head == "ambient")?.Items.ElementAtOrDefault(1) is SNumber amb ? amb.Value : 20,
            Sun = clauses.FirstOrDefault(c => c.Head == "sun") is { } sun
                ? new SunSpec(sun.Field("latitude") is { } la ? Num(la, 1, null) : 31.2,
                              sun.Field("day") is { } dy ? (int)Num(dy, 1, null) : 172,
                              sun.Field("time") is { } tm ? Num(tm, 1, null) : 12)
                : null,
            Planet = clauses.FirstOrDefault(c => c.Head == "planet") is { } planet ? Planet.Parse(planet, 1) : Planet.Earth,
            Weather = clauses.FirstOrDefault(c => c.Head == "weather") is { } wx ? ParseWeather(wx) : null,
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
                                    c.Field("current")?.Items.ElementAtOrDefault(1) is SNumber n ? n.Value : null, loc)
                { CurrentFrom = c.Field("current-from")?.Items.ElementAtOrDefault(1) is SSymbol cf ? cf.Name : null };
            }).ToList(),
            Sources = clauses.Where(c => c.Head == "inflow").Select(c =>
            {
                var loc = ParseLoc(c);
                return new SourceSpec(Sym(c, 1, loc),
                    c.Field("into") is { } i ? Sym(i, 1, loc) : throw new MachineFormatException("inflow has no tank to flow into", loc),
                    c.Field("flow") is { } f ? Num(f, 1, loc) : throw new MachineFormatException("inflow has no flow", loc), loc);
            }).ToList(),
            Channels = clauses.Where(c => c.Head == "channel").Select(c =>
            {
                var loc = ParseLoc(c);
                var to = c.Field("to");
                return new ChannelSpec(Sym(c, 1, loc),
                    Ref(c.Field("from"), 1, loc),
                    to is { Items.Count: 3 } ? Ref(to, 1, loc) : null,
                    c.Field("end") is { Items.Count: 4 } e ? new Vec3(Num(e, 1, loc), Num(e, 2, loc), Num(e, 3, loc)) : null,
                    c.Field("width") is { } w ? Num(w, 1, loc) : throw new MachineFormatException("channel has no width", loc),
                    c.Field("length")?.Items.ElementAtOrDefault(1) is SNumber l ? l.Value : null,
                    loc,
                    c.Field("via") is { } via
                        ? via.Items.Skip(1).OfType<SList>().Select(p => (Num(p, 0, loc), Num(p, 1, loc))).ToList()
                        : [],
                    c.Field("onto")?.Items.ElementAtOrDefault(1) is SSymbol onto ? onto.Name : null)
                {
                    Dynamic = c.Field("dynamic")?.Items.ElementAtOrDefault(1) is SBool { Value: true },
                    Cells = c.Field("cells")?.Items.ElementAtOrDefault(1) is SNumber cells ? (int)cells.Value : null,
                };
            }).ToList(),
            Cylinders = clauses.Where(c => c.Head is "atmospheric-cylinder" or "steam-cylinder").Select(c =>
            {
                var loc = ParseLoc(c);
                string Field(string f) => c.Field(f) is { } l ? Sym(l, 1, loc) : throw new MachineFormatException($"{c.Head} has no {f}", loc);
                return new CylinderSpec(Sym(c, 1, loc), Field("piston"), Field("steam-from"),
                                        c.Field("injection-temperature") is { } t && t.Items.ElementAtOrDefault(1) is SNumber ? Num(t, 1, loc) : null, loc)
                {
                    Kind = c.Head == "steam-cylinder" ? "steam" : "atmospheric",
                    Crank = c.Field("crank")?.Items.ElementAtOrDefault(1) is SSymbol cr ? cr.Name : null,
                };
            }).ToList(),
            Triggers = clauses.Where(c => c.Head == "trigger").Select(ParseTrigger).ToList(),
            Follows = clauses.Where(c => c.Head == "follow").Select(ParseFollow).ToList(),
            Wakes = clauses.Where(c => c.Head == "wake").Select(ParseWake).ToList(),
            Belts = clauses.Where(c => c.Head == "belt").Select(c =>
            {
                var loc = ParseLoc(c);
                return new BeltSpec(Sym(c, 1, loc), Sym(c, 2, loc), Sym(c, 3, loc),
                    c.Field("tension") is { } t ? Num(t, 1, loc) : throw new MachineFormatException("belt has no tension", loc),
                    c.Field("material") is { } m ? Sym(m, 1, loc) : "hemp", loc);
            }).ToList(),
            Joints = clauses.Where(c => c.Head == "joint").Select(ParseJoint).ToList(),
            // (operator (at t (part field value)) … (srcloc …)): the timed settings of a demo operator, played in time order
            Operator = clauses.Where(c => c.Head == "operator").SelectMany(c => c.Items.Skip(1).OfType<SList>().Where(a => a.Head == "at"))
                .Select(a => OperatorLog.FromForm(a) ?? throw new MachineFormatException("an operator action is (at seconds (part field value))", ParseLoc(a)))
                .OrderBy(a => a.At).ToList(),
            Meshes = clauses.Where(c => c.Head == "mesh").Select(c =>
            {
                var loc = ParseLoc(c);
                double eff = c.Field("efficiency") is { } e ? Num(e, 1, loc) : 1;
                if (!(eff > 0 && eff <= 1)) throw new MachineFormatException($"a mesh's efficiency is more than 0 and at most 1, not {eff}", loc);
                return new MeshSpec(Sym(c, 1, loc), Sym(c, 2, loc), loc) { Efficiency = eff };
            }).ToList(),
            Arbors = clauses.Where(c => c.Head == "arbor").Select(c =>
            {
                var loc = ParseLoc(c);
                var parts = c.Field("parts") ?? throw new MachineFormatException("arbor has no parts", loc);
                return new ArborSpec(parts.Items.Skip(1).Select((_, i) => Sym(parts, i + 1, loc)).ToList(), loc);
            }).ToList(),
        };
    }

    // (weather (daily #t) (passes 3.0 15.0) (pass-minutes 10.0) (storm (sol 2) (hour 0.0) (tau 10.8) (sols 1.0) (settle 0.5)) …)
    private static WeatherSpec ParseWeather(SList w)
    {
        double N(SList l, string f, double d) => l.Field(f)?.Items.ElementAtOrDefault(1) is SNumber n ? n.Value : d;
        return new WeatherSpec(
            w.Field("daily")?.Items.ElementAtOrDefault(1) is not SBool { Value: false },
            (w.Field("passes")?.Items.Skip(1) ?? []).OfType<SNumber>().Select(n => n.Value).ToList(),
            N(w, "pass-minutes", 10),
            w.Fields("storm").Select(s => new StormSpec((int)N(s, "sol", 1), N(s, "hour", 0), N(s, "tau", 0), N(s, "sols", 1), N(s, "settle", 0.5))).ToList());
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
            Bar = c.Field("bar")?.Items.ElementAtOrDefault(1) is SSymbol b ? b.Name : null,
            Mu = c.Field("mu")?.Items.ElementAtOrDefault(1) is SNumber mu ? mu.Value : null,
            Links = c.Field("links")?.Items.ElementAtOrDefault(1) is SNumber links ? (int)links.Value : null,
            Tether = c.Field("tether")?.Items.ElementAtOrDefault(1) is SBool { Value: true },
            ReleaseAfter = c.Field("release-after")?.Items.ElementAtOrDefault(1) is SNumber after ? after.Value : null,
        };
    }

    // (joint id (kind k) (a part) (b part) (at x y z) (axis x y z | nothing) (free m …) (limit-deg d|#f) (srcloc …))
    private static JointSpec ParseJoint(SList c)
    {
        var loc = ParseLoc(c);
        string id = Sym(c, 1, loc);
        string Name(string f) => c.Field(f) is { } l ? Sym(l, 1, loc) : throw new MachineFormatException($"joint {id} has no ({f} …)", loc);
        Vec3 Point(SList l) => new(Num(l, 1, loc), Num(l, 2, loc), Num(l, 3, loc));
        var at = c.Field("at") ?? throw new MachineFormatException($"joint {id} has no (at x y z)", loc);
        string kind = Name("kind");
        if (!JointSpec.Kinds.Contains(kind)) throw new MachineFormatException($"joint {id}: kind {kind} is not one of {string.Join(", ", JointSpec.Kinds)}", loc);
        var free = (c.Field("free")?.Items.Skip(1) ?? []).Select(e => e is SSymbol s ? s.Name : throw new MachineFormatException($"joint {id}: (free …) takes motion names", loc)).ToList();
        return new JointSpec(id, kind, Name("a"), Name("b"), Point(at),
            c.Field("axis") is { Items.Count: 4 } ax ? Point(ax) : null, free,
            c.Field("limit-deg")?.Items.ElementAtOrDefault(1) is SNumber d ? d.Value : null, loc);
    }

    // (wake id (when (target field above|below value) …) (join and|or) (limit seconds) (events (target field above|below value) …) (srcloc …))
    private static WakeSpec ParseWake(SList c)
    {
        var loc = ParseLoc(c);
        WakeTerm Term(SList t) => new(Sym(t, 0, loc), Sym(t, 1, loc), Sym(t, 2, loc) == "above", Num(t, 3, loc));
        IReadOnlyList<WakeTerm> Terms(string field) => (c.Field(field)?.Items.Skip(1) ?? []).OfType<SList>().Select(Term).ToList();
        return new WakeSpec(Sym(c, 1, loc), Terms("when"), c.Field("join") is { } j ? Sym(j, 1, loc) != "or" : true,
            c.Field("limit") is { } l ? Num(l, 1, loc) : 3600, Terms("events"), loc);
    }

    // (follow id (lever part|#f) (rope id|#f) (from a) (to b) (set target field) (low v) (high v) (srcloc …))
    private static FollowSpec ParseFollow(SList c)
    {
        var loc = ParseLoc(c);
        string? Name(string f) => c.Field(f)?.Items.ElementAtOrDefault(1) is SSymbol s ? s.Name : null;
        var set = c.Field("set") ?? throw new MachineFormatException("follow has no (set target field)", loc);
        double N(string f) => c.Field(f) is { } l ? Num(l, 1, loc) : throw new MachineFormatException($"follow has no ({f} …)", loc);
        return new FollowSpec(Sym(c, 1, loc), Name("lever"), Name("rope"), N("from"), N("to"), Sym(set, 1, loc), Sym(set, 2, loc), N("low"), N("high"), loc);
    }

    // (trigger id (at x y z) (size w h d) (body part|#f) (when target field above|below value) (do (target field value) …) (srcloc …))
    private static TriggerSpec ParseTrigger(SList c)
    {
        var loc = ParseLoc(c);
        Vec3? Vec(string field) => c.Field(field) is { Items.Count: 4 } v ? new Vec3(Num(v, 1, loc), Num(v, 2, loc), Num(v, 3, loc)) : null;
        var when = c.Field("when") is { Items.Count: 5 } w ? w : null;
        return new TriggerSpec(Sym(c, 1, loc), Vec("at"), Vec("size"),
            c.Field("body")?.Items.ElementAtOrDefault(1) is SSymbol b ? b.Name : null,
            when is null ? null : Sym(when, 1, loc), when is null ? null : Sym(when, 2, loc),
            when is null || Sym(when, 3, loc) == "above", when is null ? 0 : Num(when, 4, loc),
            (c.Field("do")?.Items.Skip(1) ?? []).OfType<SList>()
                .Select(a => new TriggerAction(Sym(a, 0, loc), Sym(a, 1, loc), Num(a, 2, loc))).ToList(),
            loc);
    }

    // (sealed-air (tanks a b …) (tube-volume v) (srcloc …))
    private static SealedAirSpec ParseSealedAir(SList c)
    {
        var loc = ParseLoc(c);
        var tanks = c.Field("tanks") ?? throw new MachineFormatException("sealed-air has no tanks", loc);
        return new SealedAirSpec(
            tanks.Items.Skip(1).Select((_, i) => Sym(tanks, i + 1, loc)).ToList(),
            c.Field("tube-volume") is { } v ? Num(v, 1, loc) : 0,
            loc,
            c.Field("heat-loss") is { } hl ? Num(hl, 1, loc) : 0,
            c.Field("heat-capacity") is { } hc ? Num(hc, 1, loc) : 0);
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
