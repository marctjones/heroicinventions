using HeroicInventions.Sim.Game;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// The pure rules of the cargo markers, the bearing readout and the map's frame (#236, #239, #237).
/// Predicted before the code ran, by an independent FNV-1a in Python (offset = R/3 (0.5 + 0.5 hi/65536), angle = 2 pi lo/65536, R = 12),
/// from the crates' settled places in the opening (trace of lonely-rover-opening, t = 120 s):
///   battery-bank  (254.487, 137.899) -> offset 2.963 at 156.2 deg -> centre (251.776, 139.095)
///   solar-panels  (265.750, 143.500) -> offset 3.900 at 180.9 deg -> centre (261.850, 143.438)
///   gas-cylinders (249.384, 144.000) -> offset 2.496 at  79.8 deg -> centre (249.827, 146.457)
///   hand-tools    (241.301, 139.739) -> offset 2.739 at  38.6 deg -> centre (243.441, 141.448)
///   motors        (233.714, 134.986) -> offset 2.950 at 289.1 deg -> centre (234.678, 132.198)
/// each true place inside its 12 m disc. Compass: north is -z, east +x.
/// </summary>
public class MarkerTests
{
    private static readonly (string Id, double X, double Z, double Cx, double Cz)[] Crates =
    [
        ("battery-bank", 254.4873, 137.8986, 251.776, 139.095),
        ("solar-panels", 265.75, 143.5, 261.850, 143.438),
        ("gas-cylinders", 249.3844, 144.0004, 249.827, 146.457),
        ("hand-tools", 241.3012, 139.7389, 243.441, 141.448),
        ("motors", 233.7137, 134.9862, 234.678, 132.198),
    ];

    [Fact]
    public void Each_cargo_crate_gets_the_predicted_centre_and_lies_inside_its_disc()
    {
        foreach (var (id, x, z, cx, cz) in Crates)
        {
            var disc = RoughArea.For(id, x, z);
            Assert.Equal(cx, disc.X, 0.002);
            Assert.Equal(cz, disc.Z, 0.002);
            Assert.Equal(12, disc.Radius);
            double off = Math.Sqrt((disc.X - x) * (disc.X - x) + (disc.Z - z) * (disc.Z - z));
            Assert.InRange(off, 12 / 6.0 - 1e-9, 12 / 3.0 + 1e-9);   // never the exact spot, never beyond a third of the radius
            Assert.True(off < disc.Radius, $"{id}: the crate lies outside its own disc");
        }
    }

    [Fact]
    public void The_hash_is_fixed_not_per_process()
    {
        Assert.Equal(0x7b506f13u, RoughArea.Hash("battery-bank"));   // FNV-1a; string.GetHashCode() would change on every launch
        Assert.Equal(RoughArea.Offset("motors"), RoughArea.Offset("motors"));
    }

    [Fact]
    public void The_disc_follows_a_creeping_crate_and_does_not_shrink_as_the_rover_nears()
    {
        var a = RoughArea.For("hand-tools", 242.49, 140.0);
        var b = RoughArea.For("hand-tools", 241.30, 139.74);   // after the 1.2 m of creep
        Assert.Equal(241.30 - 242.49, b.X - a.X, 9);
        Assert.Equal(139.74 - 140.0, b.Z - a.Z, 9);
        Assert.Equal(a.Radius, b.Radius);   // the rule takes no rover place: nothing in it can shrink
    }

    [Fact]
    public void Names_are_the_cargo_and_nothing_else()
    {
        Assert.Equal("battery bank", RoughArea.Name("battery-bank"));
        Assert.Equal("gas cylinders", RoughArea.Name("gas-cylinders"));
    }

    // #239: a marker at a known offset from the rover. Rover at (200, 140) facing north (-z).
    [Theory]
    [InlineData(0, -30, 30.0, 0.0, 0.0)]       // due north
    [InlineData(40, 0, 40.0, 90.0, 90.0)]      // due east: 090, to the right
    [InlineData(0, 25, 25.0, 180.0, 180.0)]    // due south, behind
    [InlineData(-10, 0, 10.0, 270.0, -90.0)]   // due west: 270, to the left
    [InlineData(30, -40, 50.0, 36.8699, 36.8699)]   // 3-4-5: atan(30/40) east of north
    [InlineData(-30, 40, 50.0, 216.8699, -143.1301)]
    public void Distance_and_bearing_to_a_marker_at_a_known_offset(double dx, double dz, double distance, double bearing, double relative)
    {
        var (d, b) = Compass.To(200, 140, 200 + dx, 140 + dz);
        Assert.Equal(distance, d, 0.1);
        Assert.Equal(bearing, b, 1.0);
        Assert.Equal(relative, Compass.Relative(b, 0), 1.0);
    }

    [Fact]
    public void The_arrow_is_relative_to_the_rovers_heading()
    {
        // facing east (heading 90), a marker due north is 90 degrees to the left; due south, to the right; dead ahead is 0
        Assert.Equal(-90, Compass.Relative(0, 90), 1e-9);
        Assert.Equal(90, Compass.Relative(180, 90), 1e-9);
        Assert.Equal(0, Compass.Relative(90, 90), 1e-9);
        Assert.Equal(180, Compass.Relative(270, 90), 1e-9);   // dead behind reads +180, not -180
        Assert.Equal(20, Compass.Relative(5, 345), 1e-9);     // across north
        Assert.Equal("NE", Compass.Point(40));
        Assert.Equal("N", Compass.Point(358));
    }

    [Fact]
    public void A_crates_map_position_is_its_world_position_through_the_frame_and_back()
    {
        var frame = new MapFrame(245, 140, 0.25, 640, 400);   // 0.25 m a pixel, the world's (245, 140) at the screen's (640, 400)
        foreach (var (id, x, z, _, _) in Crates)
        {
            var disc = RoughArea.For(id, x, z);
            var (sx, sy) = frame.ToScreen(disc.X, disc.Z);
            var (wx, wz) = frame.ToWorld(sx, sy);
            Assert.Equal(disc.X, wx, 1e-9);
            Assert.Equal(disc.Z, wz, 1e-9);
        }
        // north up: a point 10 m north (-z) of the centre is 40 px above it, 10 m east 40 px to the right
        Assert.Equal((640.0, 360.0), frame.ToScreen(245, 130));
        Assert.Equal((680.0, 400.0), frame.ToScreen(255, 140));
        Assert.Equal(10, frame.BarMetres(60), 1e-9);   // 60 px is 15 m: the bar is 10 m
        Assert.Equal(50, new MapFrame(0, 0, 1, 0, 0).BarMetres(60), 1e-9);
        Assert.Equal(20, new MapFrame(0, 0, 0.5, 0, 0).BarMetres(60), 1e-9);   // 30 m: 20
    }
}
