using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// The world's "Join machines" tool (issue #78): the player clicks a part
/// (a tank's port, or something turning on an axle) on one machine, then one
/// on another, and this says what link that makes, or in plain English why
/// it can't. Kept out of the Godot script so the rules are tested without a
/// window: two tanks' water ports make a pipe; two turning parts make a
/// shaft; anything else is refused.
/// </summary>
public static class WorldLinkGestures
{
    /// <summary>Parts a shaft can take: things that turn on an axle, in the sim (windmills, water and jet wheels) or the physics engine (wheels, screws).</summary>
    public static readonly string[] Turning = ["windmill", "waterwheel", "jetwheel", "smokejack", "wheel", "screw"];

    public static string Prompt(LinkEnd? first) =>
        first is null ? "Join machines: click a tank's port or a turning part on one machine"
                      : $"Join machines: {first} picked; click a port or turning part on another machine (Esc to stop)";

    /// <summary>The link two picks make in <paramref name="world"/>, or an <see cref="InvalidOperationException"/> saying why they won't do.</summary>
    public static LinkSpec Link(WorldDef world, Func<string, MachineDef?> machineOf, LinkEnd a, LinkEnd b)
    {
        if (a.Label == b.Label)
            throw new InvalidOperationException($"{a.Part} and {b.Part} are both parts of {a.Label}; join parts of one machine in its own editor");
        var pa = PartOf(machineOf, a);
        var pb = PartOf(machineOf, b);
        if (pa.Kind == "tank" && pb.Kind == "tank")
        {
            if (a.Port is null || b.Port is null) throw new InvalidOperationException("a pipe runs from port to port: click on a tank's port");
            var (porta, portb) = (Port(pa, a), Port(pb, b));
            if (porta.Kind != "water" || portb.Kind != "water")
                throw new InvalidOperationException($"{a} is a {porta.Kind} port and {b} a {portb.Kind} port; a pipe joins two water ports");
            if (world.Links.Any(l => l.Kind == "pipe" && (l.From == a && l.To == b || l.From == b && l.To == a)))
                throw new InvalidOperationException($"{a} and {b} are already joined by a pipe");
            return new LinkSpec(world.NextLinkId("pipe"), "pipe", a, b) { Conductance = 1e-3 };
        }
        if (Turning.Contains(pa.Kind) && Turning.Contains(pb.Kind))
        {
            var (sa, sb) = (a with { Port = null }, b with { Port = null });
            if (world.Links.Any(l => l.Kind == "shaft" && (l.From == sa || l.To == sa) && (l.From == sb || l.To == sb)))
                throw new InvalidOperationException($"{sa} and {sb} are already on one shaft");
            return new LinkSpec(world.NextLinkId("shaft"), "shaft", sa, sb) { Ratio = 1 };
        }
        throw new InvalidOperationException(
            $"{a.Label}'s {pa.Id} is a {pa.Kind} and {b.Label}'s {pb.Id} a {pb.Kind}: a pipe joins two tanks' ports, a shaft two things turning on axles");
    }

    private static PartSpec PartOf(Func<string, MachineDef?> machineOf, LinkEnd e) =>
        (machineOf(e.Label) ?? throw new InvalidOperationException($"no machine is placed as {e.Label}")).Part(e.Part)
        ?? throw new InvalidOperationException($"{e.Label} has no part {e.Part}");

    private static PortSpec Port(PartSpec p, LinkEnd e) =>
        p.Ports.FirstOrDefault(x => x.Name == e.Port) ?? throw new InvalidOperationException($"{e.Label}'s {p.Id} has no port {e.Port}");
}
