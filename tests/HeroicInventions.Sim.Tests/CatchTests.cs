using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #155: catches, pawls and tethers as fields. What they hold, and what a released trebuchet throws, is
/// checked in Jolt by heroic/tests/machine-behavior-test.rkt.
/// </summary>
public class CatchTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static MachineRuntime Load(string name) =>
        new(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", $"{name}.machine"))), Materials);

    [Fact]
    public void ACatchHoldsUntilItsTimeOrACommand()
    {
        var c = new Catch("arm", "catch", releaseAt: 3);
        Assert.True(c.HeldAt(2.99));
        Assert.False(c.HeldAt(3));                    // lets go by itself at #:release-after
        c.Command(1);                                 // a person who sets it decides from then on
        Assert.True(c.HeldAt(10));
        c.Command(0);
        Assert.False(c.HeldAt(0));
    }

    [Fact]
    public void TheBlueprintsOfferTheirCatchPawlAndTetherFields()
    {
        var trebuchet = Load("trebuchet");
        Assert.Equal(1, trebuchet.GetField("arm", "catch"));
        trebuchet.SetField("arm", "catch", 0);
        Assert.Equal(0, trebuchet.GetField("arm", "catch"));
        Assert.Equal(0, trebuchet.GetField("arm", "catch-load"));   // the view reckons it; nothing has held yet

        var windlass = Load("ratchet-windlass");
        Assert.Equal(1, windlass.GetField("hold-pawl", "pawl"));
        Assert.True(double.IsPositiveInfinity(windlass.Catches["lower-pawl"].ReleaseAt));   // no longer lifts itself: the blueprint's demo operator does (#157)
        Assert.Contains(windlass.Def.Operator, o => o.Target == "lower-pawl" && o.Field == "pawl" && o.Value == 0 && o.At == 2);

        var lantern = Load("kongming-lantern");
        Assert.Equal(1, lantern.GetField("mooring", "tether"));
        Assert.Equal(40, lantern.Catches["mooring"].ReleaseAt);
    }

    [Fact]
    public void ALiftedPawlLetsTheWheelRunBackAndDropsIntoTheValleyItHasReached()
    {
        var r = new Ratchet("r", 12, 0.15, reverse: false) { Lifted = true };
        const double dt = 1.0 / 120;
        Assert.Equal(0, r.Step(dt, -1.0, -2, 0.1));             // running back: no impulse while the pawl is clear
        r.Lifted = false;
        // -1 rad is between the valleys at -60 and -30 degrees: the pawl drops into the one below, -60
        Assert.Equal(-60, r.Locked * 180 / Math.PI, precision: 9);
        Assert.True(r.Step(dt, -1.1, -2, 0.1) > 0);              // and now holds there
    }
}
