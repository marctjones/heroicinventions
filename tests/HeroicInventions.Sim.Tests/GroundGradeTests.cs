using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// The rover panel's "ahead" grade (the ground-reading cue): the steepest metre of ground along the heading over the next 3 m.
/// Predicted by hand: on a plane tilted t the grade is t everywhere (up one way, down the other); on dig-bank's 1 m bank, whose
/// 45 degree face runs from x = 29.5 to 30.5 (cell centres 0 and 1 m apart, interpolated between), a rover at x0 facing +x has
/// its runs start at x0 + 0, 0.5 ... 2, each a metre long: x0 = 24 sees only level ground; x0 = 26.75, whose last run
/// (28.75 to 29.75) climbs 0.25 m, sees atan 0.25 = 14.04; x0 = 27, last run (29 to 30) 0.5 m, atan 0.5 = 26.57; x0 = 27.5, a run
/// exactly on the face, 45 (not the 18.4 a mean over 3 m would give).
/// </summary>
public class GroundGradeTests
{
    private static Terrain Bank()
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "game", "maps", "dig-bank.map"))) dir = Path.GetDirectoryName(dir)!;
        return Terrain.Parse(File.ReadAllText(Path.Combine(dir, "game", "maps", "dig-bank.map")), "dig-bank.map");
    }

    [Theory]
    [InlineData(10)] [InlineData(20)] [InlineData(30)] [InlineData(45)]
    public void A_plane_reads_its_own_tilt_up_and_down(double tilt)
    {
        double rise = Math.Tan(tilt * Math.PI / 180);
        Assert.Equal(tilt, GroundGrade.Ahead((x, z) => rise * x, 5, 7, 1, 0), 9);
        Assert.Equal(-tilt, GroundGrade.Ahead((x, z) => rise * x, 5, 7, -1, 0), 9);
        double c = Math.Sqrt(0.5);   // a heading along a diagonal still measures the run along it
        Assert.Equal(tilt, GroundGrade.Ahead((x, z) => rise * (x + z) / Math.Sqrt(2), 5, 7, c, c), 9);
    }

    [Theory]
    [InlineData(24.0, 0.0)]
    [InlineData(26.75, 14.036)]
    [InlineData(27.0, 26.5651)]
    [InlineData(27.5, 45.0)]
    [InlineData(28.0, 45.0)]
    public void The_dig_bank_reads_as_predicted(double x0, double expected)
    {
        var bank = Bank();
        Assert.Equal(expected, GroundGrade.Ahead(bank.HeightAt, x0, 30, 1, 0), 2);
    }

    [Fact]
    public void Facing_away_from_the_bank_reads_level_and_the_steep_run_is_not_averaged_away()
    {
        var bank = Bank();
        Assert.Equal(0, GroundGrade.Ahead(bank.HeightAt, 24, 30, -1, 0), 6);
        Assert.True(GroundGrade.Ahead(bank.HeightAt, 27.5, 30, 1, 0) > 40);   // a mean over the 3 m would be 18.4
    }
}
