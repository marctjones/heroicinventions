using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// "Put a gear on this shaft" (building plan, phase 2): the line a windmill's windshaft or a wheel's axle runs along, and where the next
/// gear on it goes by default. Build mode slides its ghost along <see cref="Shaft.Behind"/> from <see cref="Shaft.Point"/>; nothing here
/// changes the physics (a gear put on the line is joined by an ordinary (arbor ...)).
/// </summary>
public static class ShaftSlots
{
    /// <summary>
    /// How far behind a windmill's hub its first gear goes: where the route's first wheel is (solve.steps, wheel-a at (0 11 -1.3) behind
    /// sails at (0 11 0)). It clears the nave (0.45 m) and the stocks' band behind the sails' plane, and phase 1's post stands under the
    /// middle of a four-stage train that starts here (BuildMode.MillTrainSetback = 1.3 + 3 x 0.1 / 2).
    /// </summary>
    public const double FirstGearBehindHub = 1.3;

    /// <summary>How far behind the last gear on an axle the next one goes: the route's step between stages (wheel-a -1.3, wheel-b -1.4).</summary>
    public const double Spacing = 0.1;

    /// <summary>
    /// An axle line: through <paramref name="Point"/> (the selected part's #:at: a windmill's hub, a wheel's middle), the way
    /// <paramref name="Behind"/> (a unit vector) runs away from the sails; <paramref name="DefaultT"/> metres along it is where the next gear
    /// goes. <paramref name="Mates"/> are the parts already sharing the axle (arbors joined through common parts), the anchor among them.
    /// <paramref name="WheelProps"/> turn a new wheel (built along +z) onto the line: #:axis, #:tilt-deg, #:heading-deg, only those that
    /// differ from a wheel's own (axis z, no tilt or heading). A catalogue wheel's command takes only #:at and #:material, so build mode
    /// sets these after it, one (set ...) each.
    /// </summary>
    public sealed record Shaft(string Anchor, Vec3 Point, Vec3 Behind, double DefaultT, IReadOnlyList<string> Mates,
                               IReadOnlyList<(string Key, string Value)> WheelProps)
    {
        public Vec3 At(double t) => new(Point.X + Behind.X * t, Point.Y + Behind.Y * t, Point.Z + Behind.Z * t);
    }

    /// <summary>The kinds a gear can be put on the shaft of.</summary>
    public static bool Takes(PartSpec part) => part.Kind is "windmill" or "wheel";

    /// <summary>The parts on one axle with <paramref name="id"/>: every arbor holding it, and every arbor holding one of those, and so on.</summary>
    public static IReadOnlyList<string> AxleMates(EditorDocument doc, string id)
    {
        var seen = new List<string> { id };
        for (int i = 0; i < seen.Count; i++)
            foreach (var a in doc.Arbors.Where(a => a.Parts.Contains(seen[i])))
                foreach (var p in a.Parts.Where(p => !seen.Contains(p))) seen.Add(p);
        return seen;
    }

    /// <summary>The way a windmill's windshaft runs back from its hub: opposite its #:facing-deg (azimuth from +x toward +z, 90 = +z).</summary>
    public static Vec3 BehindMill(PartSpec mill)
    {
        double f = mill.Number("facing-deg", 90) * Math.PI / 180;
        return new Vec3(-Math.Cos(f), 0, -Math.Sin(f));
    }

    /// <summary>
    /// A wheel's axle in the world, negated: its #:axis raised #:tilt-deg (<see cref="GearPhase.AxleOf"/>) turned by #:heading-deg about
    /// the vertical (as the view's YawOf: a turn of h takes +z to (sin h, 0, cos h)). Negated so the route's wheels (#:axis z) step back
    /// along -z, as the windmill's do.
    /// </summary>
    public static Vec3 BehindWheel(PartSpec wheel)
    {
        var a = GearPhase.AxleOf(wheel);
        double h = MachineDef.HeadingOf(wheel) * Math.PI / 180;
        var world = new Vec3(Math.Cos(h) * a.X + Math.Sin(h) * a.Z, a.Y, -Math.Sin(h) * a.X + Math.Cos(h) * a.Z);
        return new Vec3(-world.X, -world.Y, -world.Z);
    }

    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static Vec3 Minus(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>
    /// The shaft of part <paramref name="id"/>, or null when it is not a windmill or a wheel. The next gear's default: <see cref="Spacing"/>
    /// behind the hindmost wheel already on the axle; on a windmill with none yet, <see cref="FirstGearBehindHub"/> behind the hub; on a
    /// lone wheel, <see cref="Spacing"/> behind it. A wheel on a windmill's axle steps back the way the windmill's windshaft runs.
    /// </summary>
    public static Shaft? Of(EditorDocument doc, string id)
    {
        if (!doc.Parts.TryGetValue(id, out var part) || !Takes(part)) return null;
        var mates = AxleMates(doc, id);
        var mill = part.Kind == "windmill" ? part : mates.Select(m => doc.Parts.GetValueOrDefault(m)).FirstOrDefault(p => p?.Kind == "windmill");
        Vec3 behind;
        if (part.Kind == "windmill") behind = BehindMill(part);
        else
        {
            behind = BehindWheel(part);
            // the same line, stepped away from the sails
            if (mill is not null && Dot(behind, BehindMill(mill)) < 0) behind = new Vec3(-behind.X, -behind.Y, -behind.Z);
        }
        var wheels = mates.Select(m => doc.Parts.GetValueOrDefault(m)).Where(p => p is { Kind: "wheel" }).Select(p => p!).ToList();
        double t = wheels.Count > 0 ? wheels.Max(w => Dot(Minus(w.At, part.At), behind)) + Spacing
                 : part.Kind == "windmill" ? FirstGearBehindHub : Spacing;
        IReadOnlyList<(string, string)> props;
        if (part.Kind == "windmill")
            // a wheel is built along +z: heading h takes +z to (sin h, 0, cos h), the way the sails face when h = 90 - facing
            props = [("axis", "z"), ("heading-deg", Num(90 - part.Number("facing-deg", 90)))];
        else
            props = [("axis", part.Symbol("axis", "z")), ("tilt-deg", Num(part.Number("tilt-deg", 0))), ("heading-deg", Num(MachineDef.HeadingOf(part)))];
        return new Shaft(id, part.At, behind, t, mates, props.Where(p => !(p.Item1 is "tilt-deg" or "heading-deg" && p.Item2 == "0" || p.Item1 == "axis" && p.Item2 == "z")).ToList());
    }

    private static string Num(double v) => Math.Round(v, 6).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
}
