using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// "Add a meshing gear" (building plan, phase 3): the circle a new gear's centre rides around a selected wheel, in the wheel's plane
/// (across its axle), at the centre distance of the two (<see cref="LinkGestures.MeshCentreDistance"/>, which depends on the new gear's
/// size, so it is asked for each size). Angle 0 is world +x laid into the plane (the route's pinion-b stands +0.225 m in x from wheel-a);
/// angles grow toward <see cref="Ring.V"/> (up, for a wheel whose axle runs along z). Nothing here changes the physics: the new gear is
/// joined by an ordinary (mesh selected new).
/// </summary>
public static class MeshSlots
{
    /// <summary>
    /// A wheel's plane: through <paramref name="Centre"/> (its #:at) across <paramref name="Normal"/> (its axle, unit), with
    /// <paramref name="U"/> (angle 0) and <paramref name="V"/> (angle 90 degrees) in it. <paramref name="WheelProps"/> turn a new wheel
    /// (built along +z) parallel to it: the same as <see cref="ShaftSlots.Shaft.WheelProps"/>.
    /// </summary>
    public sealed record Ring(string Anchor, Vec3 Centre, Vec3 Normal, Vec3 U, Vec3 V, IReadOnlyList<(string Key, string Value)> WheelProps)
    {
        /// <summary>The point <paramref name="radius"/> from the centre at <paramref name="angle"/> radians.</summary>
        public Vec3 At(double angle, double radius) => new(
            Centre.X + radius * (Math.Cos(angle) * U.X + Math.Sin(angle) * V.X),
            Centre.Y + radius * (Math.Cos(angle) * U.Y + Math.Sin(angle) * V.Y),
            Centre.Z + radius * (Math.Cos(angle) * U.Z + Math.Sin(angle) * V.Z));

        /// <summary>The angle (radians, -pi..pi) of a point's shadow on the plane.</summary>
        public double AngleOf(Vec3 p)
        {
            var d = new Vec3(p.X - Centre.X, p.Y - Centre.Y, p.Z - Centre.Z);
            return Math.Atan2(Dot(d, V), Dot(d, U));
        }
    }

    /// <summary>The kinds a meshing gear can be added to: a wheel (gear or plain).</summary>
    public static bool Takes(PartSpec part) => part.Kind == "wheel";

    /// <summary>The ring around wheel <paramref name="id"/>, or null when it is not a wheel.</summary>
    public static Ring? Of(EditorDocument doc, string id)
    {
        if (!doc.Parts.TryGetValue(id, out var part) || !Takes(part) || ShaftSlots.Of(doc, id) is not { } shaft) return null;
        var n = Unit(shaft.Behind);
        // world +x laid into the plane; for an axle along x, world -z (what +x is for a wheel turned that way)
        var u = Unit(Reject(new Vec3(1, 0, 0), n));
        if (Length(Reject(new Vec3(1, 0, 0), n)) < 0.1) u = Unit(Reject(new Vec3(0, 0, -1), n));
        var v = Cross(u, n);   // up for an axle along -z
        return new Ring(id, part.At, n, u, v, shaft.WheelProps);
    }

    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static double Length(Vec3 a) => Math.Sqrt(Dot(a, a));
    private static Vec3 Unit(Vec3 a) { double l = Length(a); return new(a.X / l, a.Y / l, a.Z / l); }
    private static Vec3 Reject(Vec3 a, Vec3 n) { double k = Dot(a, n); return new(a.X - k * n.X, a.Y - k * n.Y, a.Z - k * n.Z); }
    private static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
