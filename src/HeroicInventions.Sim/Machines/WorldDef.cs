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
        return new WorldDef { Name = Name, Map = Map, Placements = Placements, Links = [.. Links, link] };
    }

    /// <summary>The same world without the named link.</summary>
    public WorldDef WithoutLink(string id) =>
        new() { Name = Name, Map = Map, Placements = Placements, Links = Links.Where(l => l.Id != id).ToList() };

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
        return new WorldDef { Name = name.Name, Map = map, Placements = placements, Links = links };

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
