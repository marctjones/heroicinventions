using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Game;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// The found rule of the rover's buried cargo (owner decision on #240, Game/CargoFind.cs) and its place in the world save. Worked
/// beforehand from the opening (the bank's crate 0.5 m a side, 1.04 m under the rubble, held: Burial.Held is cover > 0.25 x size = 0.125):
/// not found as it lies; found when a corner of its top is 0.05 m or less under the ground though its middle is still 0.5 m under; found
/// when freed (the solve digs to under 0.12 m of cover, past the 0.125 m that holds it); found by teeth inside its box grown by 0.05 m,
/// not by teeth 0.1 m off its side.
/// </summary>
public class CargoFindTests
{
    private const double Size = 0.5;

    [Fact]
    public void The_bank_as_it_lies_under_the_rubble_is_not_found()
    {
        Assert.False(CargoFind.Found(1.04, freed: false, struck: false, touched: false));
        Assert.Null(CargoFind.Why(1.04, false, false, false));
        Assert.False(CargoFind.Found(0.06, false, false, false), "6 cm over the least-covered part still hides it");
    }

    [Fact]
    public void Any_part_of_its_top_within_5_cm_of_the_air_is_a_find()
    {
        Assert.True(CargoFind.Found(0.05, false, false, false));
        Assert.True(CargoFind.Found(-0.3, false, false, false));
        // a hole whose bottom reaches one corner of the lid: the ground over the top is 0.5 m but for one sample at 0.04 m
        double top = -44.0;
        var ground = CargoFind.TopSamples(Size).Select(s => s.U > 0.2 && s.V > 0.2 ? top + 0.04 : top + 0.5).ToList();
        Assert.Equal(25, ground.Count);
        Assert.Equal(0.04, CargoFind.TopCover(top, ground), 9);
        Assert.True(CargoFind.Found(CargoFind.TopCover(top, ground), false, false, false));
    }

    [Fact]
    public void The_top_samples_reach_the_edges_of_the_lid()
    {
        var s = CargoFind.TopSamples(Size).ToList();
        Assert.Equal(-0.25, s.Min(p => p.U), 12);
        Assert.Equal(0.25, s.Max(p => p.U), 12);
        Assert.Equal(-0.25, s.Min(p => p.V), 12);
        Assert.Equal(0.25, s.Max(p => p.V), 12);
    }

    [Fact]
    public void Freed_struck_or_touched_each_find_it_whatever_the_cover()
    {
        Assert.False(Burial.Held(0.11, Size));   // the solve digs until the cover is under 0.12: the ground lets it go
        Assert.True(CargoFind.Found(1.04, freed: true, struck: false, touched: false));
        Assert.True(CargoFind.Found(1.04, false, struck: true, touched: false));
        Assert.True(CargoFind.Found(1.04, false, false, touched: true));
    }

    [Fact]
    public void The_teeth_strike_it_inside_its_box_grown_by_5_cm_only()
    {
        Assert.True(CargoFind.InBox(0, 0.25, 0, Size, CargoFind.StrikeReach), "on the lid");
        Assert.True(CargoFind.InBox(0.29, 0.1, 0, Size, CargoFind.StrikeReach), "4 cm off its side, below its top");
        Assert.False(CargoFind.InBox(0.35, 0.1, 0, Size, CargoFind.StrikeReach), "10 cm off its side");
        Assert.False(CargoFind.InBox(0, 0.36, 0, Size, CargoFind.StrikeReach), "11 cm over the lid: soil still between");
    }

    [Fact]
    public void Found_crates_go_through_the_world_save_and_an_older_save_has_none()
    {
        var found = new FoundCargo();
        Assert.Null(found.ToForm());
        Assert.True(found.Add("battery-bank", "crate"));
        Assert.False(found.Add("battery-bank", "crate"));
        var save = new WorldSave { Kind = "world", Name = "lonely-rover-opening", Machines = [], FoundCargo = found.ToForm() };
        string text = save.ToText();
        Assert.Contains("\n  (found-cargo 1.0 (crate battery-bank crate))", text);   // its own line, as the routes' and goals' are
        var back = FoundCargo.Parse(WorldSave.Parse(text).FoundCargo);
        Assert.True(back.Contains("battery-bank", "crate"));
        Assert.Single(back.All);

        var older = WorldSave.Parse(new WorldSave { Kind = "world", Name = "lonely-rover-opening", Machines = [] }.ToText());
        Assert.Null(older.FoundCargo);
        Assert.Empty(FoundCargo.Parse(older.FoundCargo).All);
    }
}
