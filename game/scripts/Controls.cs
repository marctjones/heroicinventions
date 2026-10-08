using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The one table of what a click does (#152): part kind → controls. Each control sets one existing runtime field, so the
/// physics' own tests already cover what it does; this only says which field a person reaches by clicking which part, and
/// how it reads. The first control of a part whose field exists (and whose <see cref="Control.Needs"/> holds) is its
/// click; the next is Shift+click; the rest are in the right-click list, which also offers every other settable field.
/// A "machine run" lets a person operate anything; the Lonely Rover game will narrow that through <see cref="Allowed"/> (#163).
/// </summary>
public static class Controls
{
    public enum Style
    {
        /// <summary>A switch on a quantity: nonzero goes to 0, zero goes to the part's own working value (the one it was first seen with, else <see cref="Control.On"/>).</summary>
        Toggle,
        /// <summary>Adds <see cref="Control.On"/> to the field (pour 10 L, stoke 1 kg).</summary>
        Add,
        /// <summary>Sets the field to <see cref="Control.On"/> (empty a tank, clean a mirror, turn a sand timer).</summary>
        Set,
        /// <summary>Negates the field (a driven wheel the other way).</summary>
        Reverse,
    }

    /// <param name="Kinds">Part kinds it applies to (a rope or source counts as "rope" or "source").</param>
    /// <param name="Whenever">Verb when the field is nonzero (the click turns it off); for Add, Set and Reverse the only verb.</param>
    /// <param name="Otherwise">Verb when the field is zero (the click turns it on).</param>
    /// <param name="On">The working value when nothing was seen; for Add and Set the amount.</param>
    /// <param name="Max">With <see cref="Max"/> above <see cref="Min"/>, the field is a range and gets a slider.</param>
    public sealed record Control(string[] Kinds, string Field, Style Style, string Whenever, string Otherwise, double On,
        double Min = 0, double Max = 0, string Unit = "", Func<double, bool>? Needs = null)
    {
        public bool IsRange => Max > Min;
    }

    private static Control Switch(string kinds, string field, string whenever, string otherwise, double on, double min = 0, double max = 0, string unit = "") =>
        new(kinds.Split(' '), field, Style.Toggle, whenever, otherwise, on, min, max, unit);

    public static readonly IReadOnlyList<Control> Table =
    [
        // fire and heat
        Switch("boiler", "fire", "Douse the fire", "Light the fire", 3000, 0, 6000, "W"),
        Switch("hearth", "power", "Douse the hearth", "Light the hearth", 1000, 0, 5000, "W"),
        new(["hearth"], "fuel", Style.Add, "Stoke +1 kg fuel", "", 1),
        Switch("envelope", "burner-power", "Burner off", "Burner on", 20000, 0, 60000, "W"),
        Switch("melter", "power", "Switch off", "Switch on", 1000, 0, 5000, "W"),
        Switch("electrolyser", "power", "Switch off", "Switch on", 100, 0, 500, "W"),
        Switch("galvanic-jar", "on", "Disconnect", "Connect", 1),
        // water
        new(["tank"], "water", Style.Add, "Pour 10 L", "", 10),
        new(["tank"], "water", Style.Set, "Empty the tank", "", 0, Needs: v => v > 0),
        Switch("tank", "tap", "Shut the tap", "Open the tap", 1, 0, 5, "L/s"),
        Switch("source", "inflow", "Shut the sluice at the head", "Open the sluice at the head", 1, 0, 20, "L/s"),
        // openings
        Switch("door", "open", "Shut", "Open", 1, 0, 1),
        Switch("sluice", "opening", "Shut the gate", "Open the gate", 1, 0, 1),
        new(["hopper"], "turn", Style.Set, "Turn the timer over", "", 1),
        Switch("hopper", "orifice", "Shut the gate", "Open the gate", 10, 0, 50, "mm"),
        Switch("leak", "area", "Plug the leak", "Unplug the leak", 1, 0, 20, "cm2"),
        // holds and catches
        Switch("grip", "closed", "Open the jaws", "Close the jaws", 1),
        Switch("capstan", "hold", "Let go of the rope", "Hold the rope", 1000, 0, 5000, "N"),
        Switch("ratchet", "pawl", "Lift the pawl", "Drop the pawl", 1),
        Switch("lever", "catch", "Release the catch", "Cock the catch", 1),
        Switch("rope", "tether", "Let the tether go", "Tie the tether", 1),
        // mirrors
        new(["mirror", "burning-mirror"], "dust", Style.Set, "Clean the mirror", "", 0, Needs: v => v > 0.005),
        Switch("mirror burning-mirror", "area", "Cover the mirror", "Uncover the mirror", 1, 0, 20, "m2"),
        // machines that turn or work
        Switch("pump", "rpm", "Stop the pump", "Start the pump", 30, 0, 120, "rpm"),
        Switch("air-pump", "speed", "Stop the pump", "Start the pump", 50, 0, 100, "L/s"),
        Switch("lift", "rpm", "Stop", "Start", 30, 0, 120, "rpm"),
        Switch("windmill waterwheel", "load", "Free the stones", "Engage the stones", 1000, 0, 5000, "load"),
        Switch("wheel screw", "drive-rpm", "Stop the wheel", "Start the wheel", 10, -60, 60, "rpm"),
        new(["wheel", "screw"], "drive-rpm", Style.Reverse, "Reverse the wheel", "", 0, Needs: v => v != 0),
        Switch("wheel screw", "drive-torque", "Let go of the drive", "Take hold of the drive", 1000, 0, 0, "N.m"),
        Switch("wheel screw", "grind-torque", "Free the stones", "Engage the stones", 5, 0, 50, "N.m"),
        Switch("bellows", "airflow", "Stop the bellows", "Work the bellows", 1, 0, 5, "m3/s"),
        Switch("jetwheel smokejack", "load", "Free the shaft", "Load the shaft", 1, 0, 50, "N.m"),
        Switch("enclosure", "leak", "Patch the hole", "Open a hole", 1, 0, 20, "cm2"),
        Switch("digger", "power", "Stop digging", "Start digging", 1000, 0, 5000, "W"),
        new(["plants"], "harvest", Style.Set, "Coppice the trees onto the stove", "", 1000),   // cuts all that stands (a value over it is what is standing)
    ];

    /// <summary>The capability hook: whether a person may work this control on this part. A machine run says yes to everything; the game narrows it to what the rover could do (#163).</summary>
    public static Func<MachineView, string, string, bool> Allowed { get; set; } = (_, _, _) => true;

    /// <summary>A part's kind as the table names it: its part kind, or "rope" and "source" for the machine's ropes and water sources.</summary>
    public static string? KindOf(MachineView view, string id)
    {
        var def = view.Runtime.Def;
        if (def.Part(id) is { } part) return part.Kind;
        if (def.Ropes.Any(r => r.Id == id)) return "rope";
        if (def.Sources.Any(s => s.Id == id)) return "source";
        if (def.Lifts.Any(l => l.Id == id)) return "lift";
        return null;
    }

    /// <summary>One action a click, Shift+click or menu offers: what it says, and the field and value it sets.</summary>
    public sealed record Action(Control Control, string Label, string Field, double Value);

    /// <summary>The controls of a part that work now (the field is settable, a person may, and the control's condition holds), in table order.</summary>
    public static List<Action> For(MachineView view, string id)
    {
        var list = new List<Action>();
        if (KindOf(view, id) is not { } kind) return list;
        Remember(view, id);
        var rt = view.Runtime;
        foreach (var c in Table)
        {
            if (!c.Kinds.Contains(kind) || !rt.FieldSetters.ContainsKey($"{id}.{c.Field}") || !Allowed(view, id, c.Field)) continue;
            double now = Read(view, id, c.Field);
            if (c.Needs is { } needs && !needs(now)) continue;
            list.Add(Resolve(view, id, c, now));
        }
        return list;
    }

    private static Action Resolve(MachineView view, string id, Control c, double now)
    {
        switch (c.Style)
        {
            case Style.Add: return new Action(c, c.Whenever, c.Field, now + c.On);
            case Style.Set: return new Action(c, c.Whenever, c.Field, c.On);
            case Style.Reverse: return new Action(c, c.Whenever, c.Field, -now);
            default:
                bool on = Math.Abs(now) > 1e-12;
                return new Action(c, on ? c.Whenever : c.Otherwise, c.Field, on ? 0 : Working(view, id, c));
        }
    }

    // What each toggle goes back to: the value the part had when first looked at (a hearth's rated watts, a pump's rpm),
    // since 0 is "off" and says nothing about "on". Falls back to the table's default for a part that began off.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MachineView, Dictionary<string, double>> Seen = new();

    private static double Working(MachineView view, string id, Control c)
    {
        var seen = Seen.GetOrCreateValue(view);
        return seen.TryGetValue($"{id}.{c.Field}", out double v) && Math.Abs(v) > 1e-12 ? v : c.On;
    }

    /// <summary>Notes the nonzero values of the part's toggles; called when a part is first hovered, before anyone has switched anything.</summary>
    public static void Remember(MachineView view, string id)
    {
        if (KindOf(view, id) is not { } kind) return;
        var seen = Seen.GetOrCreateValue(view);
        foreach (var c in Table)
            if (c.Style == Style.Toggle && c.Kinds.Contains(kind) && !seen.ContainsKey($"{id}.{c.Field}")
                && view.Runtime.FieldGetters.ContainsKey($"{id}.{c.Field}") && Read(view, id, c.Field) is var v && Math.Abs(v) > 1e-12)
                seen[$"{id}.{c.Field}"] = v;
    }

    /// <summary>The field's value now, 0 when it has no getter (the Set-only ones, like a sand timer's turn).</summary>
    public static double Read(MachineView view, string id, string field) =>
        view.Runtime.FieldGetters.TryGetValue($"{id}.{field}", out var get) ? get() : 0;

    /// <summary>The range controls of a part, for sliders: the table's, with the top stretched to twice a part's working value so a strong one isn't pinned.</summary>
    public static List<(Control Control, double Max)> Ranges(MachineView view, string id)
    {
        var list = new List<(Control, double)>();
        if (KindOf(view, id) is not { } kind) return list;
        foreach (var c in Table)
            if (c.IsRange && c.Style == Style.Toggle && c.Kinds.Contains(kind) && view.Runtime.FieldSetters.ContainsKey($"{id}.{c.Field}") && Allowed(view, id, c.Field))
                list.Add((c, Math.Max(c.Max, 2 * Math.Abs(Working(view, id, c)))));
        return list;
    }

    /// <summary>Every settable field of a part, for the right-click list.</summary>
    public static List<string> Fields(MachineView view, string id) =>
        view.Runtime.FieldSetters.Keys
            .Where(k => k.StartsWith(id + ".", StringComparison.Ordinal) && k.IndexOf('.', id.Length + 1) < 0 && Allowed(view, id, k[(id.Length + 1)..]))
            .Select(k => k[(id.Length + 1)..]).Order().ToList();
}
