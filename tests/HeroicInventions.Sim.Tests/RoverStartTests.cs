using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #94: a world places the player's rover with (rover (at X Z) (heading D)); a world without one is a machine run.</summary>
public class RoverStartTests
{
    [Fact]
    public void TheOpeningPlacesTheRoverOnTheFloorFacingTheCargo()
    {
        var world = WorldDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "worlds", "lonely-rover-opening.world")), "lonely-rover-opening");
        Assert.NotNull(world.Rover);
        Assert.Equal(190, world.Rover!.X);
        Assert.Equal(140, world.Rover.Z);
        Assert.Equal(270, world.Rover.Heading);   // faces +x, towards the crates at x 233 to 264
    }

    [Fact]
    public void AMachineWorldHasNoRoverAndKeepsTheFreeOperator()
    {
        var world = WorldDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "worlds", "trench.world")), "trench.world");
        Assert.Null(world.Rover);
    }

    [Fact]
    public void TheRoverSurvivesWritingTheWorldBackAndLinkEdits()
    {
        var world = WorldDef.Parse("(world w (map victoria) (rover (at 10 -20) (heading 90)))", "w");
        var again = WorldDef.Parse(world.Write(), "w");
        Assert.Equal(new RoverStart(10, -20, 90), again.Rover);
        Assert.Equal(new RoverStart(10, -20, 90), world.WithoutLink("none").Rover);
    }

    [Fact]
    public void ARoverWithoutAPlaceIsRefused()
    {
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse("(world w (rover (heading 90)))", "w"));
    }
}
