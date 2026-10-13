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
    public void Motor_can_hides_the_rotor_so_a_plain_click_anywhere_on_the_can_picks_the_motor()
    {
        // #247: the can (0.44 m cube, SimPartSide) is a solid body drawn round the rotor's wheel box; the ray reaches the can first either way
        double can = PickBoxes.SimPartSide("generator")!.Value;
        Assert.True(PickBoxes.HidesWhatIsInside("generator"));
        Assert.False(PickBoxes.HidesWhatIsInside("battery-bank"));
        var motor = new PickHit("motors.motor", PickBox.Around(0, 0.62, -0.12, can), 12.2, Opaque: true);
        var rotor = new PickHit("motors.rotor", PickBox.Around(0, 0.62, 0, 0.2), 12.3);
        Assert.True(motor.Box.Encloses(rotor.Box));
        Assert.Equal("motors.motor", PickBoxes.Choose([motor, rotor])!.Value.Name);   // the dial's centre: both crossed, and the can is what is drawn
        Assert.Equal("motors.motor", PickBoxes.Choose([motor])!.Value.Name);          // the can's flank: only the can's box
        Assert.Equal("motors.motor", PickBoxes.Choose([rotor with { T = 12.0 }, motor])!.Value.Name);   // from behind, the rotor's box met first: still the can
        Assert.Equal(["motors.motor", "motors.rotor"], PickBoxes.List([motor, rotor]).Select(h => h.Name).ToArray());   // the rotor stays in the pick list
        Assert.Equal("motors.rotor", PickBoxes.Choose([rotor])!.Value.Name);          // a rotor that lies outside any can is picked plainly
    }

    [Fact]
    public void A_ray_through_the_cans_back_cap_is_one_the_rotor_may_take()
    {
        var can = PickBox.Around(0, 0.62, -0.12, 0.54);   // the generator's box (0.44 m grown 5 cm a side), the axle along +z, the dial on the -z end
        Assert.True(PickBoxes.EntersByBack(can, 0.05, 0.6, 6, 0, 0, -1, 0, 0, 1));          // from +z, straight at the back cap
        Assert.True(PickBoxes.EntersByBack(can, 0.05, 1.6, 6, 0, -0.15, -1, 0, 0, 1));      // from above and behind, still through the cap
        Assert.False(PickBoxes.EntersByBack(can, 0.05, 0.6, -6, 0, 0, 1, 0, 0, 1));         // from the dial's side
        Assert.False(PickBoxes.EntersByBack(can, 6, 0.6, -0.1, -1, 0, 0, 0, 0, 1));         // from the flank
        Assert.False(PickBoxes.EntersByBack(can, 0.05, 0.6, 6, 0, 0, -1, 1, 1, 1));         // an axle not along an axis: no back cap
        Assert.False(PickBoxes.EntersByBack(can, 4, 0.6, 6, 0, 0, -1, 0, 0, 1));            // misses the box
        Assert.True(PickBoxes.EntersByBack(can, 6, 0.6, -0.1, -1, 0, 0, 1, 0, 0));          // an axle along +x: the +x end is the back
    }

    [Fact]
    public void A_crate_does_not_hide_the_bank_in_it()
    {
        var crate = new PickHit("bank.crate", PickBox.Around(0, 0, 0, 0.5), 5);
        var bank = new PickHit("bank.bank", PickBox.Around(0, 0, 0, 0.3), 5.1);
        Assert.Equal("bank.bank", PickBoxes.Choose([crate, bank])!.Value.Name);
    }

    [Fact]
    public void Generator_and_bank_get_boxes_and_other_kinds_do_not()
    {
        Assert.NotNull(PickBoxes.SimPartSide("generator"));
        Assert.NotNull(PickBoxes.SimPartSide("battery-bank"));
        Assert.Null(PickBoxes.SimPartSide("heat-store"));
    }
}
