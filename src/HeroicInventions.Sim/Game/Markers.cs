using System.Text;

namespace HeroicInventions.Sim.Game;

// Where things are (issues #235-#239): the pure rules of the cargo markers, the bearing readout and the map's frame, so that tests can pin them.
// Owner decision on #240: the rover knows ROUGHLY where each crate landed, never its depth. Nothing that is drawn, listed or read out
// may be anchored at a crate's true place: the discs, their labels, the bearing and distance and the map all use the disc's centre.

/// <summary>
/// The rough area of a crate (#236, #240): a disc of <see cref="Radius"/> m whose centre is the crate's place moved by a fixed offset
/// of between a sixth and a third of the radius, in a direction and by an amount taken from a hash of the crate's label. So the
/// centre does not give the spot away, the same crate always gets the same offset (in any run, any process), and the true place always
/// lies inside the disc (the offset is at most R/3). The disc is the crate's place plus the offset, so it follows the crate as it creeps
/// and does not shrink towards a point as the rover nears: nothing in the game scans or surveys, so the area stays.
/// <para>
/// Radius 6 m (owner's choice): the cargo crates of the opening settle within 8 s of the rim coming down and the largest creep measured
/// was 1.22 m (hand-tools; trace of lonely-rover-opening, 120 s), so 6 m is five times that and a crate pushed a few metres by the
/// rover still lies inside. The crates lie 5 to 11 m apart, so the discs (12 m across) mostly stand apart.
/// </para>
/// </summary>
public static class RoughArea
{
    public const double Radius = 6;

    /// <summary>The largest offset of the centre from the true place, as a share of the radius ("about a third").</summary>
    public const double MaxOffsetShare = 1.0 / 3;

    /// <summary>FNV-1a over the label's UTF-8 bytes. Not string.GetHashCode(), which is different in every process.</summary>
    public static uint Hash(string id)
    {
        uint h = 0x811C9DC5;
        foreach (byte b in Encoding.UTF8.GetBytes(id)) { h ^= b; h *= 0x01000193; }
        return h;
    }

    /// <summary>The offset (dx, dz) from a crate's place to its disc's centre: direction from the hash's low 16 bits, length from its high 16 bits, R/6 to R/3.</summary>
    public static (double Dx, double Dz) Offset(string id, double radius = Radius)
    {
        uint h = Hash(id);
        double angle = 2 * Math.PI * (h & 0xFFFF) / 65536.0;
        double length = radius * MaxOffsetShare * (0.5 + 0.5 * ((h >> 16) & 0xFFFF) / 65536.0);
        return (length * Math.Cos(angle), length * Math.Sin(angle));
    }

    /// <summary>The disc of a crate that lies at (x, z): its centre and radius.</summary>
    public static (double X, double Z, double Radius) For(string id, double x, double z, double radius = Radius)
    {
        var (dx, dz) = Offset(id, radius);
        return (x + dx, z + dz, radius);
    }

    /// <summary>The cargo name a label carries: the world's label with its dashes as spaces ("battery-bank" is "battery bank"). Nothing about depth or cover.</summary>
    public static string Name(string id) => id.Replace('-', ' ');
}

/// <summary>
/// Compass bearings in the game's own frame (docs: victoria.rkt, "north, -z"; azimuth runs from +x toward +z): north is -z, east +x, south +z,
/// west -x. A bearing is degrees clockwise from north seen from above, 0 to 360: due north 0, due east 90.
/// </summary>
public static class Compass
{
    /// <summary>The bearing of the vector (dx, dz), degrees in [0, 360).</summary>
    public static double Bearing(double dx, double dz)
    {
        double deg = Math.Atan2(dx, -dz) * 180 / Math.PI;
        return deg < 0 ? deg + 360 : deg >= 360 ? deg - 360 : deg;
    }

    /// <summary>Metres and bearing from one point to another.</summary>
    public static (double Distance, double Bearing) To(double fromX, double fromZ, double toX, double toZ) =>
        (Math.Sqrt((toX - fromX) * (toX - fromX) + (toZ - fromZ) * (toZ - fromZ)), Bearing(toX - fromX, toZ - fromZ));

    /// <summary>The angle from where the rover faces to a bearing, degrees in (-180, 180]: positive is to its right (clockwise), 0 dead ahead, 180 behind.</summary>
    public static double Relative(double bearing, double heading)
    {
        double d = (bearing - heading) % 360;
        if (d > 180) d -= 360; else if (d <= -180) d += 360;
        return d;
    }

    /// <summary>The eight-point name of a bearing ("N", "NE" ...).</summary>
    public static string Point(double bearing) => new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[(int)Math.Round(bearing / 45) % 8];
}

/// <summary>
/// The navigation map's frame (#237): north up, so screen x runs with world x and screen y with world z (north, -z, is up the screen), one
/// scale for both: a pixel is <see cref="MetresPerPixel"/> m. <see cref="CentreX"/>, <see cref="CentreZ"/> is the world point at the
/// screen point <see cref="ScreenX"/>, <see cref="ScreenY"/>. Everything the map draws goes through <see cref="ToScreen"/>, so a crate's
/// map position is its world position by construction (and the test is the round trip).
/// </summary>
public readonly record struct MapFrame(double CentreX, double CentreZ, double MetresPerPixel, double ScreenX, double ScreenY)
{
    public (double X, double Y) ToScreen(double worldX, double worldZ) =>
        (ScreenX + (worldX - CentreX) / MetresPerPixel, ScreenY + (worldZ - CentreZ) / MetresPerPixel);

    public (double X, double Z) ToWorld(double screenX, double screenY) =>
        (CentreX + (screenX - ScreenX) * MetresPerPixel, CentreZ + (screenY - ScreenY) * MetresPerPixel);

    /// <summary>The scale bar's length: the largest of 1, 2, 5 x 10^n metres that is at most <paramref name="maxPixels"/> wide.</summary>
    public double BarMetres(double maxPixels)
    {
        double limit = maxPixels * MetresPerPixel, mag = Math.Pow(10, Math.Floor(Math.Log10(limit)));
        foreach (double m in new[] { 5.0, 2.0, 1.0 }) if (m * mag <= limit) return m * mag;
        return mag;
    }
}
