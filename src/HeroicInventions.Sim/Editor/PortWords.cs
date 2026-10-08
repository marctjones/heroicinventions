using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// Connection points in plain words, so build mode can write "water in" next to a
/// dot instead of leaving a bare coloured ball (issue #178). The table is keyed by
/// part kind and port name; it lives in its own file, not in PartTemplates, so a new
/// part adds its words here without touching the templates. A port the table doesn't
/// know (one made by hand with a console command) falls back to words for its kind of
/// connection, so a label is never blank. Ropes and axles aren't ports (a rope ties to
/// the middle of a part, an axle is a join tool), so they have no entries here.
/// </summary>
public static class PortWords
{
    private static readonly Dictionary<(string Kind, string Port), string> Table = new()
    {
        [("tank", "inlet")] = "water in",
        [("tank", "outlet")] = "water out",
        [("boiler", "steam")] = "steam out",
        [("rotor", "steam-in")] = "steam in",
        [("jetwheel", "steam-in")] = "steam in",
    };

    /// <summary>Whether the table itself (not the fallback) has words for this port of this part kind.</summary>
    public static bool HasWords(string partKind, string portName) => Table.ContainsKey((partKind, portName));

    /// <summary>The short plain words for a port: "water in", "steam out", ...</summary>
    public static string Words(string partKind, PortSpec port) =>
        Table.TryGetValue((partKind, port.Name), out var words) ? words
        : port.Kind switch { "water" => "water", "steam" => "steam", _ => port.Name.Replace('-', ' ') };

    public static string Words(PartSpec part, PortSpec port) => Words(part.Kind, port);

    /// <summary>
    /// One label for several ports drawn at the same spot (a tank's inlet and outlet are
    /// both at its foot): ports with the same first word share it, "water in / out".
    /// </summary>
    public static string Combine(IEnumerable<string> words)
    {
        var all = words.Distinct().ToList();
        if (all.Count <= 1) return all.FirstOrDefault() ?? "";
        var heads = all.Select(w => w.Split(' ', 2)[0]).Distinct().ToList();
        if (heads.Count == 1 && all.All(w => w.Contains(' ')))
            return heads[0] + " " + string.Join(" / ", all.Select(w => w.Split(' ', 2)[1]));
        return string.Join(" / ", all);
    }
}
