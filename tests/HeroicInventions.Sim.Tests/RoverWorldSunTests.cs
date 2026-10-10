using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #250: the Lonely Rover worlds are lit by the sun and the scene's clock. The view draws the runtime's own default sun
/// (the found bank's: noon at the start, latitude 31.2, day 172, Mars) in every world that has a scenario, so the sun it draws must
/// be exactly the one the sim already uses, and no machine may have been given a sun clause (that would be sim-side). Worked by hand,
/// Mars's declination on day 172 of 669 is 25.19 sin(360 x 456 / 669) = -22.90 degrees, so at latitude 31.2 the noon sun stands
/// 90 - 31.2 - 22.9 = 35.9 degrees up, due south; it rises and sets where cos(w) = -tan(31.2) tan(-22.9) = 0.2560, w = 75.2 degrees =
/// 5.01 h either side of noon (07:00 and 17:01); at 03:00 it is 49.35 degrees below the horizon and at midnight 81.7.
/// </summary>
public class RoverWorldSunTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static MachineRuntime FoundBank() =>
        new(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "found-bank.machine"))), Materials);

    [Theory]
    [InlineData("lonely-rover-opening")]
    [InlineData("lonely-rover-easy")]
    [InlineData("lonely-rover-e2e")]
    public void TheRoverWorldsHaveAScenario_SoTheSceneDrawsTheirSun(string world) =>
        Assert.NotNull(WorldDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "worlds", world + ".world")), world).Scenario);

    [Fact]
    public void TheFoundBanksOwnSunIsTheDefaultOnMars_AndItHasNoSunClause()
    {
        var rt = FoundBank();
        Assert.False(rt.SunShown);                       // sim-side nothing changed: the bank has no (sun ...) and no mirror
        Assert.Null(rt.Def.Sun);
        Assert.Equal("Mars", rt.Planet.Name);
        Assert.Equal(31.2, rt.Sun.Latitude);
        Assert.Equal(172, rt.Sun.Day);
        Assert.Equal(12, rt.Sun.Time);                   // the opening starts at noon
        Assert.Equal(35.9, rt.Sun.Elevation, 1);
        Assert.Equal(180, rt.Sun.Azimuth, 1);            // due south: shadows fall to the north
    }

    [Theory]
    [InlineData(7.0, 0.14, 117.2)]
    [InlineData(9.0, 20.83, 135.8)]
    [InlineData(16.5, 5.74, 238.8)]
    [InlineData(17.2, -2.16, 244.4)]
    [InlineData(18.0, -11.63, 250.1)]
    [InlineData(3.0, -49.35, 89.6)]
    [InlineData(0.0, -81.7, 0.0)]
    public void TheSceneClockGivesTheWorkedElevationAndAzimuth(double hour, double elevation, double azimuth)
    {
        var rt = FoundBank();
        rt.Sun.Time = hour;
        Assert.Equal(elevation, rt.Sun.Elevation, 1);
        if (hour > 0) Assert.Equal(azimuth, rt.Sun.Azimuth, 1);
    }
}
