using HeroicInventions.Sim.Electrics;

namespace HeroicInventions.Sim;

/// <summary>
/// Why a generator is, or is not, charging its bank right now (issue #212): the label on the generator in the scene. It reads state
/// only, adds no rule, and names every reason that holds, in the order a player would fix them:
/// asleep with the machines paused, not wired to a bank, the bank full, the bank outside its 0-45 °C, the rotor under the cut-in.
/// </summary>
public static class ChargeStatus
{
    // The names of the fields the bank-sleep work adds to a generator (read if present; a missing field means "not held"). All in this one place.
    public const string SettledPowerField = "settled-power", HeldField = "held", HeldEstimatedField = "held-estimated";

    /// <summary>A paused sleep holding the generator at its last steady rate: <see cref="Watts"/> (null when none is held) and whether that rate is only estimated.</summary>
    public readonly record struct Hold(double? Watts, bool Estimated)
    {
        public static readonly Hold None = new(null, false);
        /// <summary>Reads the hold from a runtime's field getters; a field the sim does not have (yet) reads as not held.</summary>
        public static Hold Read(IReadOnlyDictionary<string, Func<double>> getters, string generatorId)
        {
            double Get(string f) => getters.TryGetValue($"{generatorId}.{f}", out var g) ? g() : 0;
            return Get(HeldField) > 0 ? new Hold(Get(SettledPowerField), Get(HeldEstimatedField) > 0) : None;
        }
    }

    /// <summary>The reasons the generator is not charging; empty when it is (or is turning and would, were the bank not full of nothing: see <see cref="Label"/>).</summary>
    /// <param name="rotorAsleep">A sleep has paused the physics engine that turns this generator's rotor: its speed stands still, so no other reason can be read.</param>
    public static List<string> Reasons(Generator g, bool rotorAsleep = false, Hold hold = default)
    {
        var reasons = new List<string>();
        if (rotorAsleep && hold.Watts is not null) return reasons;   // held: it charges, at the last steady rate (see Label)
        if (rotorAsleep) { reasons.Add("sleeping, machines paused"); return reasons; }
        if (g.Bank is not { } bank) { reasons.Add("unwired"); return reasons; }   // no bank to say anything of
        if (bank.Full) reasons.Add("bank full");
        if (!bank.InRange) reasons.Add($"bank {bank.Temperature:0} °C outside {bank.MinChargeC:0}-{bank.MaxChargeC:0} °C");
        if (g.Rpm < g.CutInRpm) reasons.Add($"rotor {g.Rpm:#,0} rpm under {g.CutInRpm:#,0}");
        return reasons;
    }

    /// <summary>The prime mover's name as a player says it, from what the generator's charge is filed under.</summary>
    public static string PrimeName(Generator g) => g.DrivenBy switch
    {
        "wind" => "windmill", "water-wheel" => "water wheel", "steam-jet" => "jet wheel", "stirling" => "Stirling", _ => "prime mover",
    };

    /// <summary>
    /// The prime mover's rpm: from the sim where it turns it, else (a generator in another machine from its prime mover, GAP 9) said to be unknown.
    /// Null where there is no prime mover to speak of (a falling weight, a hand).
    /// </summary>
    /// <param name="linkedRpm">The speed of the shaft a world's shaft link brings to this generator's rotor from another machine, rpm, if there is one (read until the sim names the other machine's prime mover, GAP 9).</param>
    public static string? PrimeLine(Generator g, double? linkedRpm = null)
    {
        if (g.PrimeOmega is { } p) return $"{PrimeName(g)} {Math.Abs(p()) * 60 / (2 * Math.PI):#,0} rpm";
        if (linkedRpm is { } r) return $"shaft in {r:#,0} rpm";
        return g.DrivenBy == "shaft" ? "prime mover rpm unknown" : null;
    }

    /// <summary>The whole label: the generator and its prime mover's rpm, then why it is not charging (or how much it is, with the rotor's rpm).</summary>
    public static string Label(Generator g, bool rotorAsleep = false, double? linkedRpm = null, Hold hold = default)
    {
        string head = g.Name + (PrimeLine(g, linkedRpm) is { } prime ? $" · {prime}" : "");
        if (rotorAsleep && hold.Watts is { } w)
            return $"{head}\nsleeping, machines paused: charging at its last {(hold.Estimated ? "estimated" : "steady")} rate, {w:0} W (approximate)";
        var reasons = Reasons(g, rotorAsleep, hold);
        if (reasons.Count == 0)
            return $"{head}\ncharging {g.Delivered:0} W · {g.Delivered / g.Bank!.Volts:0.0} A · rotor {g.Rpm:#,0} rpm";
        return $"{head}\nnot charging: {string.Join("\n and ", reasons)}";
    }
}
