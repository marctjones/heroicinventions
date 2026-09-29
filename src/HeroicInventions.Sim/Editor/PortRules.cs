using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>What kind of link, if any, joins two compatible ports.</summary>
public enum LinkKind
{
    /// <summary>No link: the ports don't belong together.</summary>
    None,
    /// <summary>A <c>pipe</c> clause: water flowing between two tank ports.</summary>
    Pipe,
    /// <summary>A <c>connect</c> clause: a rigid steam joint from a boiler to a rotor.</summary>
    SteamConnect,
}

/// <summary>
/// The same compatibility rules racket/heroic/machine.rkt's <c>check-machine!</c>
/// enforces at compile time (machine.rkt lines ~546-648), reimplemented here so
/// the in-game editor can refuse — or snap — a connection the moment the player
/// tries to make it, instead of only failing later when Racket rebuilds the
/// file. Kept as one place so both directions (editor UI, tests) agree on what
/// "compatible" means.
/// </summary>
public static class PortRules
{
    /// <summary>
    /// What linking <paramref name="a"/>'s port to <paramref name="b"/>'s port would mean,
    /// or <see cref="LinkKind.None"/> if they can't be joined at all.
    /// A pipe needs two water ports; a steam connect needs a boiler's steam port
    /// paired with a rotor's steam-in port (machine.rkt: "a steam connection must
    /// join a boiler to a rotor", one rotor per boiler).
    /// </summary>
    public static LinkKind Compatibility(PartSpec a, PortSpec aPort, PartSpec b, PortSpec bPort)
    {
        if (a.Id == b.Id) return LinkKind.None;
        if (aPort.Kind != bPort.Kind) return LinkKind.None;
        return aPort.Kind switch
        {
            "water" => LinkKind.Pipe,
            "steam" when IsBoilerRotorPair(a, b) => LinkKind.SteamConnect,
            _ => LinkKind.None,
        };
    }

    private static bool IsBoilerRotorPair(PartSpec a, PartSpec b) =>
        (a.Kind == "boiler" && b.Kind == "rotor") || (a.Kind == "rotor" && b.Kind == "boiler");

    /// <summary>A boiler already feeding a rotor by <c>connect</c> can't feed a second one (machine.rkt L570-572).</summary>
    public static bool BoilerAlreadyFeedsARotor(MachineDef m, string boilerId) =>
        m.Connects.Count(c => IsSteamLinkOf(m, c, boilerId)) > 0;

    private static bool IsSteamLinkOf(MachineDef m, ConnectSpec c, string boilerId)
    {
        var partA = m.Part(c.A.Part);
        var partB = m.Part(c.B.Part);
        return (partA?.Id == boilerId && partB?.Kind == "rotor") || (partB?.Id == boilerId && partA?.Kind == "rotor");
    }

    /// <summary>Two tanks' ports can be joined by an open channel (machine.rkt L634-638).</summary>
    public static bool CanChannel(PartSpec a, PartSpec b) => a.Kind == "tank" && b.Kind == "tank";

    /// <summary>A screw, wheel or piston can lift water between two tanks (machine.rkt L612-620).</summary>
    public static bool CanLift(PartSpec by, PartSpec from, PartSpec to) =>
        by.Kind is "screw" or "wheel" or "piston" && from.Kind == "tank" && to.Kind == "tank";

    /// <summary>Only wheels can share an arbor or mesh their teeth (machine.rkt L594-610).</summary>
    public static bool BothWheels(PartSpec a, PartSpec b) => a.Kind == "wheel" && b.Kind == "wheel";
}
