using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// "Inside a selected room" (building phase 5, #254): where a part goes when the player is building in an enclosure, and what its
/// blank references can be filled with there. Pure, no Godot, like <see cref="ShaftSlots"/> and <see cref="MeshSlots"/>.
/// <list type="bullet">
/// <item>The room: the selected enclosure, or else the innermost enclosure whose box holds the selected part's #:at (after the rock is
/// placed the rock is selected, and the bin must still go into the room). An enclosure's #:at is the middle of its floor and its box rises
/// size-y above it (<see cref="WorldZones.Inside"/>), so its floor is the plane y = At.Y over x, z within half its sizes of At.</item>
/// <item>The references a part is placed with as `?` (a heat bin's holds, a bimetal's senses and drives, a mirror's onto) and which parts
/// in the room the runtime takes for them (MachineRuntime: holds a heat store not already in a bin; drives a heat bin with no strip yet;
/// senses a heat store or the room itself; onto whatever HeatedNamed takes). Exactly one candidate, and the reference is filled in as
/// part of the same edit; none or several, it stays `?` for the player. Nothing is refused: the inspector's list still offers every part.</item>
/// </list>
/// </summary>
public static class RoomSlots
{
    /// <summary>The floor of a room: its height and the box of x, z it covers.</summary>
    public readonly record struct Floor(string Room, double Y, double CentreX, double CentreZ, double HalfX, double HalfZ)
    {
        public bool Holds(double x, double z) => Math.Abs(x - CentreX) <= HalfX && Math.Abs(z - CentreZ) <= HalfZ;
    }

    /// <summary>The kinds a mirror's #:onto can be (what MachineRuntime.HeatedNamed takes, less the room itself).</summary>
    public static readonly IReadOnlyList<string> HeatedKinds = ["boiler", "tank", "enclosure", "crucible", "pond", "stirling", "envelope", "heat-store", "melter"];

    /// <summary>The references that are filled in by default, as the part keeps them: key.</summary>
    public static readonly IReadOnlyList<string> Keys = ["holds", "drives", "senses", "onto"];

    /// <summary>The room the player is building in: the selected enclosure, else the innermost enclosure holding the selected part; null if none.</summary>
    public static string? RoomOf(EditorDocument doc, string? selected)
    {
        if (selected is null || !doc.Parts.TryGetValue(selected, out var part)) return null;
        if (part.Kind == "enclosure") return part.Id;
        return doc.Parts.Values.Where(p => p.Kind == "enclosure" && WorldZones.Inside(part.At, p))
                              .OrderBy(p => p.Number("size-x") * p.Number("size-y") * p.Number("size-z")).FirstOrDefault()?.Id;
    }

    /// <summary>The floor of room <paramref name="room"/>, or null when it is not an enclosure of the document.</summary>
    public static Floor? FloorOf(EditorDocument doc, string room) =>
        doc.Parts.TryGetValue(room, out var p) && p.Kind == "enclosure"
            ? new Floor(room, p.At.Y, p.At.X, p.At.Z, p.Number("size-x", 2) / 2, p.Number("size-z", 2) / 2)
            : null;

    /// <summary>The parts of the document whose #:at is inside the room's box (the room itself not among them), in id order.</summary>
    public static IReadOnlyList<PartSpec> PartsIn(EditorDocument doc, string room) =>
        doc.Parts.TryGetValue(room, out var box) && box.Kind == "enclosure"
            ? doc.Parts.Values.Where(p => p.Id != room && WorldZones.Inside(p.At, box)).OrderBy(p => p.Id, StringComparer.Ordinal).ToList()
            : [];

    /// <summary>
    /// What the reference <paramref name="key"/> of a part about to be placed in the room could name: in the order the inspector lists them.
    /// holds: heat stores not held by another bin; drives: heat bins with no strip on their lid; senses: the room itself, then its heat
    /// stores; onto: the heated kinds in the room. Any other key: none.
    /// </summary>
    public static IReadOnlyList<string> Candidates(EditorDocument doc, string room, string key)
    {
        var inside = PartsIn(doc, room);
        switch (key)
        {
            case "holds":
                return inside.Where(p => p.Kind == "heat-store" && !doc.Parts.Values.Any(b => b.Kind == "heat-bin" && b.Symbol("holds", "") == p.Id))
                             .Select(p => p.Id).ToList();
            case "drives":
                return inside.Where(p => p.Kind == "heat-bin" && !doc.Parts.Values.Any(s => s.Kind == "bimetal" && s.Symbol("drives", "") == p.Id))
                             .Select(p => p.Id).ToList();
            case "senses":
                return [room, .. inside.Where(p => p.Kind == "heat-store").Select(p => p.Id)];
            case "onto":
                return inside.Where(p => HeatedKinds.Contains(p.Kind)).Select(p => p.Id).ToList();
            default:
                return [];
        }
    }

    /// <summary>
    /// The `?` references of a part about to be placed (<paramref name="probe"/>, a fresh template of its kind) and what each could be.
    /// <paramref name="Chosen"/> are those with exactly one candidate (for senses the room counts as the one even when a store is also
    /// there: a strip in a room reads the room), <paramref name="Open"/> those left as `?`, with the candidates a person could pick from.
    /// </summary>
    public sealed record Plan(IReadOnlyList<(string Key, string Target)> Chosen, IReadOnlyList<(string Key, IReadOnlyList<string> Candidates)> Open);

    public static Plan Defaults(EditorDocument doc, string room, PartSpec probe)
    {
        var chosen = new List<(string, string)>();
        var open = new List<(string, IReadOnlyList<string>)>();
        foreach (var key in Keys)
        {
            if (probe.Props.GetValueOrDefault(key) is not SSymbol { Name: "?" }) continue;
            var c = Candidates(doc, room, key);
            if (key == "senses") chosen.Add((key, room));   // the room is the one: it comes first
            else if (c.Count == 1) chosen.Add((key, c[0]));
            else open.Add((key, c));
        }
        return new Plan(chosen, open);
    }

    /// <summary>
    /// The one edit that places a part and fills in its chosen references: <paramref name="placeCommand"/> alone when there are none,
    /// else (begin PLACE (set ID #:KEY TARGET) ...), so one undo takes the part and its defaults back together.
    /// A room itself is never wrapped: WithPlaceDefaults only sees a bare (enclosure ...) command.
    /// </summary>
    public static string Command(string placeCommand, string id, Plan plan) =>
        plan.Chosen.Count == 0 ? placeCommand
        : $"(begin {placeCommand} {string.Join(" ", plan.Chosen.Select(c => $"(set {id} #:{c.Key} {c.Target})"))})";
}
