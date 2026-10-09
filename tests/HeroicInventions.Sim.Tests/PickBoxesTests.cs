using HeroicInventions.Sim.Editor;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #225: which part a join click picks when the ray crosses nested boxes. Predicted by hand, each case.</summary>
public class PickBoxesTests
{
    private static PickHit Hit(string name, double cx, double side, double t) => new(name, PickBox.Around(cx, 0, 0, side), t);

    [Fact]
    public void Gear_inside_the_sails_box_beats_the_sails_though_the_sails_are_nearer()
    {
        // the route: the sails' box (12 m) holds pinion-e (0.2 m), wheel-d (0.5 m) and pinion-d (0.2 m); the ray reaches the sails first
        var hits = new[] { Hit("built-1.sails", 0, 12, 19.8), Hit("built-1.pinion-e", 1, 0.2, 29.2), Hit("built-1.wheel-d", 0.5, 0.5, 29.2), Hit("built-1.pinion-d", 1.2, 0.2, 29.4) };
        Assert.Equal("built-1.pinion-e", PickBoxes.Choose(hits)!.Value.Name);   // smallest, and of the two equal pinions the nearer
    }

    [Fact]
    public void Open_area_of_the_sails_still_picks_the_sails()
    {
        Assert.Equal("built-1.sails", PickBoxes.Choose([Hit("built-1.sails", 0, 12, 19.8)])!.Value.Name);
    }

    [Fact]
    public void Bank_inside_its_crate_is_picked_and_the_crate_when_only_it_is_crossed()
    {
        var crate = Hit("battery-bank.crate", 0, 0.55, 30);
        Assert.Equal("battery-bank.bank", PickBoxes.Choose([crate, Hit("battery-bank.bank", 0, 0.3, 30.1)])!.Value.Name);
        Assert.Equal("battery-bank.crate", PickBoxes.Choose([crate])!.Value.Name);
    }

    [Fact]
    public void Boxes_that_only_overlap_keep_nearest_first()
    {
        var a = Hit("a", 0, 1, 10);
        var b = Hit("b", 0.8, 1, 9);   // overlaps a, neither contains the other
        Assert.Equal("b", PickBoxes.Choose([a, b])!.Value.Name);
        Assert.Equal(["b", "a"], PickBoxes.List([a, b]).Select(h => h.Name).ToArray());
    }

    [Fact]
    public void Descends_through_two_levels_and_none_crossed_is_null()
    {
        var hits = new[] { Hit("big", 0, 10, 5), Hit("mid", 0, 2, 6), Hit("small", 0, 0.5, 7) };
        Assert.Equal("small", PickBoxes.Choose(hits)!.Value.Name);
        Assert.Null(PickBoxes.Choose([]));
    }

    [Fact]
    public void Generator_and_bank_get_boxes_and_other_kinds_do_not()
    {
        Assert.NotNull(PickBoxes.SimPartSide("generator"));
        Assert.NotNull(PickBoxes.SimPartSide("battery-bank"));
        Assert.Null(PickBoxes.SimPartSide("heat-store"));
    }
}
