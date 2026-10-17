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
    /// <paramref name="Partner"/> is the part the new gear is joined to, (arbor Partner new): see <see cref="PartnerOf"/>.
    public sealed record Shaft(string Anchor, Vec3 Point, Vec3 Behind, double DefaultT, IReadOnlyList<string> Mates,
                               IReadOnlyList<(string Key, string Value)> WheelProps, string Partner)
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

    /// <summary>
    /// What a new gear on <paramref name="id"/>'s axle is joined to: the part itself, unless it is a wheel already on an arbor, when it is
    /// that arbor's lead (its first part the sim doesn't turn, the one the view gives the bearing and the gear train counts the arbor by,
    /// MachineView.ArborLead). Joined to a rider instead, (arbor wheel_2 wheel_3) beside (arbor wheel_1 wheel_2), wheel_2 would lead one
    /// arbor and ride another, and the view's gear train (BuildGearTrains, which takes each arbor's lead in turn) would count wheel_3's
    /// meshes apart from wheel_1's, the sails' wheel: all on one lead, the axle is one shaft wherever the gear is put on it.
    /// </summary>
    public static string PartnerOf(EditorDocument doc, string id)
    {
        if (!doc.Parts.TryGetValue(id, out var part) || part.Kind != "wheel") return id;
        var arbor = doc.Arbors.FirstOrDefault(a => a.Parts.Contains(id));
        return arbor?.Parts.FirstOrDefault(p => doc.Parts.GetValueOrDefault(p)?.Kind is not ("windmill" or "waterwheel" or "jetwheel")) ?? id;
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
        return new Shaft(id, part.At, behind, t, mates, props.Where(p => !(p.Item1 is "tilt-deg" or "heading-deg" && p.Item2 == "0" || p.Item1 == "axis" && p.Item2 == "z")).ToList(), PartnerOf(doc, id));
    }

    private static string Num(double v) => Math.Round(v, 6).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// A wheel's shape (or its parts list entry) in the word "Put a … on this shaft" uses: a gear, a drum, a pulley, a wheel (the spoked
    /// cart wheel), a solid wheel, a treadwheel.
    /// </summary>
    public static string Noun(string shape) => shape switch
    {
        "cart-wheel" => "wheel",
        "disc-wheel" => "solid wheel",
        "antikythera-gear" => "gear",
        _ => shape.Replace('-', ' '),
    };

    /// <summary>
    /// How far a generator's motor can reaches along the shaft it is put on either side of its middle: the view draws it 0.34 m long
    /// (MachineView.Electrics), so "Put a generator on this shaft" starts it clear of the gear it turns with.
    /// </summary>
    public const double GeneratorHalfLength = 0.17;

    /// <summary>The gap left between the faces of two wide wheels put on one axle (a cart wheel behind a windlass's drum), metres.</summary>
    public const double Clearance = 0.05;

    /// <summary>How far a wheel reaches along its own axle either side of its #:at: half a drum's #:length, half any other wheel's #:width (0 when it has neither).</summary>
    public static double HalfLength(PartSpec part) =>
        (part.Props.GetValueOrDefault("shape") is SSymbol { Name: "drum" } ? part.Number("length", 0) : part.Number("width", 0)) / 2;

    /// <summary>
    /// Where along <paramref name="s"/> a wheel reaching <paramref name="newHalf"/> either side of its middle goes by default (tutorial plan,
    /// gap 7: "Put a wheel on this shaft"): <see cref="Shaft.DefaultT"/>, unless that puts it inside a wide wheel already on the axle (a
    /// 0.6 m drum, a 1.2 m treadwheel), when it goes just clear of the hindmost one, <see cref="Clearance"/> between their faces. Module-5
    /// gears (4 cm wide) never need it: two of them span 0.04 + 0.05 &lt; <see cref="Spacing"/>, so the route's train stands where it did.
    /// </summary>
    public static double ClearT(EditorDocument doc, Shaft s, double newHalf)
    {
        var faces = s.Mates.Select(m => doc.Parts.GetValueOrDefault(m)).Where(p => p is { Kind: "wheel" })
                           .Select(p => Dot(Minus(p!.At, s.Point), s.Behind) + HalfLength(p)).ToList();
        return faces.Count == 0 ? s.DefaultT : Math.Max(s.DefaultT, faces.Max() + newHalf + Clearance);
    }
}
