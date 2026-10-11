using System.Globalization;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// "Add a stage" (building plan, phase 4): phase 2 and phase 3 in one edit. On a selected windmill or wheel, a wheel goes on its axle where
/// "Put a gear on this shaft" puts it by default (<see cref="ShaftSlots"/>: 1.3 m behind a bare windmill's hub, else one gear width,
/// <see cref="ShaftSlots.Spacing"/>, behind the hindmost wheel on the axle) and a pinion meshes it where "Add a meshing gear" puts it by
/// default (<see cref="MeshSlots"/>: on the pitch circle at angle 0, +x for the route's axles). The route's stage is a 72 and an 18 (4:1);
/// any two catalogue gears go. The stage is one (begin ...) of the same commands the two buttons emit, (wheel ...), (set ...),
/// (arbor ...), (wheel ...), (set ...), (mesh ...), so one undo takes the whole stage back and nothing in the physics changes. Two gears of
/// different modules still mesh (drawn orange, as any such mesh): not refused.
/// </summary>
public static class StageSlots
{
    /// <summary>The route's stage (solve.steps:93-115): a 72-tooth wheel driving an 18-tooth pinion, 4:1.</summary>
    public const string RouteWheel = "involute-gear-m5-72", RoutePinion = "involute-gear-m5-18";

    /// <summary>The kinds a stage can be added to: what a gear can be put on the shaft of.</summary>
    public static bool Takes(PartSpec part) => ShaftSlots.Takes(part);

    /// <summary>
    /// A stage on <paramref name="Anchor"/>'s axle: <paramref name="WheelId"/> at <paramref name="WheelAt"/> on <paramref name="Partner"/>'s
    /// arbor, <paramref name="PinionId"/> at <paramref name="PinionAt"/> meshing it, <paramref name="CentreDistance"/> apart; <paramref name="Command"/>
    /// the one (begin ...) that builds it.
    /// </summary>
    public sealed record Stage(string Anchor, string Partner, string WheelId, Vec3 WheelAt, string PinionId, Vec3 PinionAt, double CentreDistance, string Command);

    /// <summary>The stage that would go on <paramref name="anchor"/> now, or null when it is not a windmill or a wheel.</summary>
    public static Stage? Plan(EditorDocument doc, string anchor, CatalogueEntry wheel, CatalogueEntry pinion, string wheelId, string pinionId, string material)
    {
        if (ShaftSlots.Of(doc, anchor) is not { } shaft) return null;
        var wheelAt = shaft.At(shaft.DefaultT);
        var w = PartTemplates.Create(wheel, wheelId, wheelAt, material);
        var p = PartTemplates.Create(pinion, pinionId, wheelAt, material);
        double cd = LinkGestures.MeshCentreDistance(w, p);
        var ring = MeshSlots.Around(wheelId, wheelAt, shaft.Behind, shaft.WheelProps);
        var pinionAt = ring.At(0, cd);
        var c = new List<string> { $"(wheel {wheelId} #:at {Xyz(wheelAt)} #:catalogue {wheel.Id} #:material {material})" };
        c.AddRange(shaft.WheelProps.Select(kv => $"(set {wheelId} #:{kv.Key} {kv.Value})"));
        c.Add($"(arbor {shaft.Partner} {wheelId})");
        c.Add($"(wheel {pinionId} #:at {Xyz(pinionAt)} #:catalogue {pinion.Id} #:material {material})");
        c.AddRange(ring.WheelProps.Select(kv => $"(set {pinionId} #:{kv.Key} {kv.Value})"));
        c.Add($"(mesh {wheelId} {pinionId})");
        return new Stage(anchor, shaft.Partner, wheelId, wheelAt, pinionId, pinionAt, cd, $"(begin {string.Join(" ", c)})");
    }

    private static string N(double v) => Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);
    private static string Xyz(Vec3 v) => $"({N(v.X)} {N(v.Y)} {N(v.Z)})";
}
