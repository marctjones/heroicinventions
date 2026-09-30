using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// What the build-mode "Join parts" tools do once the parts are picked: check
/// that the picks make sense and turn them into the one BuildSession command
/// that makes the link. It lives here, not in the Godot script, so the rules
/// (a gear meshes with a gear, a cylinder needs a piston and a boiler) are
/// tested without a window, and a refusal reads as plain English.
/// </summary>
public static class LinkGestures
{
    public enum Kind { Rope, Mesh, Arbor, SealedAir, Cylinder }

    /// <summary>How many parts a gesture takes: 2 for a rope, mesh or cylinder; any number from 2 for an arbor or shared air.</summary>
    public static (int Min, int? Max) Picks(Kind kind) => kind is Kind.Arbor or Kind.SealedAir ? (2, null) : (2, 2);

    public static string Prompt(Kind kind, int picked) => kind switch
    {
        Kind.Rope => picked == 0 ? "Rope: click the part it starts on" : "Rope: click the part it ends on",
        Kind.Mesh => picked == 0 ? "Gear mesh: click the first gear" : "Gear mesh: click the gear it meshes with",
        Kind.Cylinder => picked == 0 ? "Cylinder: click the piston (or the boiler)" : "Cylinder: click the boiler (or the piston)",
        Kind.Arbor => picked < 2 ? "Axle: click the wheels fixed on it (the first carries the bearing), then Enter" : $"Axle: {picked} wheels; click more, or press Enter",
        _ => picked < 2 ? "Shared air: click the tanks that share it, then Enter" : $"Shared air: {picked} tanks; click more, or press Enter",
    };

    /// <summary>The command for this gesture, or an <see cref="InvalidOperationException"/> saying why the picks won't do.</summary>
    public static string Command(EditorDocument doc, Kind kind, IReadOnlyList<string> picks)
    {
        var (min, max) = Picks(kind);
        if (picks.Count < min) throw new InvalidOperationException($"pick {min} parts first");
        if (max is { } m && picks.Count > m) throw new InvalidOperationException($"a {kind} joins {m} parts");
        if (picks.Distinct().Count() != picks.Count) throw new InvalidOperationException("pick each part once");
        var parts = picks.Select(id => doc.Parts.TryGetValue(id, out var p) ? p : throw new InvalidOperationException($"no part named {id}")).ToList();

        switch (kind)
        {
            case Kind.Rope:
                // ties to the middle of each part, cut to the distance between them; the inspector trims the length
                double length = Distance(parts[0].At, parts[1].At);
                if (length < 0.01) throw new InvalidOperationException("those parts are in the same place; move one first");
                return $"(rope {NextId(doc, "rope")} #:from ({parts[0].Id} 0 0 0) #:to ({parts[1].Id} 0 0 0) #:length {Num(length)})";
            case Kind.Mesh:
                Require(parts, "wheel", "a gear or wheel", "a gear meshes with another gear");
                if (doc.Meshes.Any(x => x.A == picks[0] && x.B == picks[1] || x.A == picks[1] && x.B == picks[0]))
                    throw new InvalidOperationException($"{picks[0]} and {picks[1]} are already meshed");
                return $"(mesh {picks[0]} {picks[1]})";
            case Kind.Arbor:
                Require(parts, "wheel", "a wheel", "an axle carries wheels");
                return $"(arbor {string.Join(' ', picks)})";
            case Kind.SealedAir:
                Require(parts, "tank", "a tank", "shared air joins tanks");
                return $"(sealed-air ({string.Join(' ', picks)}) #:tube 0.0005)";
            default:
                var piston = parts.FirstOrDefault(p => p.Kind == "piston");
                var boiler = parts.FirstOrDefault(p => p.Kind == "boiler");
                if (piston is null || boiler is null) throw new InvalidOperationException("a cylinder joins a piston to a boiler");
                return $"(atmospheric-cylinder {NextId(doc, "cylinder")} #:piston {piston.Id} #:steam-from {boiler.Id})";
        }
    }

    /// <summary>The links that name a part, with the command that takes each one away — what the inspector lists under the part.</summary>
    public static IReadOnlyList<(string Label, string Remove, string? RopeId)> LinksOn(EditorDocument doc, string id)
    {
        var links = new List<(string, string, string?)>();
        foreach (var r in doc.Ropes.Where(r => r.From.Part == id || r.To.Part == id || r.WindOn == id || r.Turns == id))
            links.Add(($"rope {r.Id}: {r.From.Part} to {r.To.Part}", $"(remove {r.Id})", r.Id));
        foreach (var g in doc.Meshes.Where(g => g.A == id || g.B == id))
            links.Add(($"meshed with {(g.A == id ? g.B : g.A)}", $"(unmesh {g.A} {g.B})", null));
        foreach (var a in doc.Arbors.Where(a => a.Parts.Contains(id)))
            links.Add(($"axle with {string.Join(", ", a.Parts.Where(p => p != id))}", $"(unarbor {id})", null));
        foreach (var a in doc.SealedAir.Where(a => a.Tanks.Contains(id)))
            links.Add(($"shares air with {string.Join(", ", a.Tanks.Where(t => t != id))}", $"(remove-air {id})", null));
        foreach (var c in doc.Cylinders.Where(c => c.Piston == id || c.Boiler == id))
            links.Add(($"cylinder {c.Id}: {c.Piston} and {c.Boiler}", $"(remove {c.Id})", null));
        foreach (var t in doc.Triggers.Where(t => t.Body == id || t.WatchTarget == id || t.Actions.Any(a => a.Target == id)))
            links.Add(($"trigger {t.Id}: " + (t.Body is { } b ? $"fires when {b} arrives" : $"fires when {t.WatchTarget}.{t.WatchField} goes {(t.Rising ? "above" : "below")} {t.Threshold:0.###}") +
                       $", sets {string.Join(", ", t.Actions.Select(a => $"{a.Target}.{a.Field} to {a.Value:0.###}"))}", $"(remove {t.Id})", null));
        return links;
    }

    private static void Require(IEnumerable<PartSpec> parts, string kind, string what, string why)
    {
        var wrong = parts.FirstOrDefault(p => p.Kind != kind);
        if (wrong is not null) throw new InvalidOperationException($"{wrong.Id} is a {wrong.Kind}, not {what}; {why}");
    }

    /// <summary>An id not yet used by a part or link: rope-1, rope-2, …</summary>
    public static string NextId(EditorDocument doc, string stem)
    {
        for (int i = 1; ; i++)
            if (!doc.HasName($"{stem}-{i}")) return $"{stem}-{i}";
    }

    private static double Distance(Vec3 a, Vec3 b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
    private static string Num(double v) => Math.Round(v, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
