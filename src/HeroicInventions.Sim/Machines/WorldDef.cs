namespace HeroicInventions.Sim.Machines;

/// <summary>One machine placed in a world: a label unique in the world (the same machine can be placed many times), the machine's name, and where its origin goes.</summary>
public sealed record Placement(string Label, string Machine, Vec3 At, SourceLocation? Location);

/// <summary>
/// A world (issue #74): many machines standing in one scene, each an
/// ordinary machine moved into place with <see cref="MachineDef.Translated"/>.
/// Written in the same s-expression shape as a .machine file:
/// <code>
/// (world pendulums
///   (place p1 pendulum-demo (at 0 0 0))
///   (place p2 pendulum-demo (at 2 0 0)))
/// </code>
/// Placement is by position only. There is no heading yet: turning a machine
/// would also have to turn axes written as symbols (a lever's #:axis z), and
/// a heading silently ignored would put levers and pendulums on wrong axes,
/// so the parser refuses one.
/// </summary>
public sealed class WorldDef
{
    public required string Name { get; init; }
    public required IReadOnlyList<Placement> Placements { get; init; }

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
            if (p.Field("heading") is not null)
                throw new MachineFormatException($"{file}: place {label.Name}: no heading yet; machines can only be moved, not turned", loc);
            var at = p.Field("at") is { Items.Count: 4 } a
                ? new Vec3(Num(a.Items[1]), Num(a.Items[2]), Num(a.Items[3]))
                : throw new MachineFormatException($"{file}: place {label.Name} needs (at x y z)", loc);
            if (placements.Any(q => q.Label == label.Name))
                throw new MachineFormatException($"{file}: two placements are labelled {label.Name}", loc);
            placements.Add(new Placement(label.Name, machine.Name, at, loc));
        }
        return new WorldDef { Name = name.Name, Placements = placements };

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
