using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Building plan, phase 2 ("Put a gear on this shaft"): the axle line of a windmill or a wheel, and the default spot for the next gear on
/// it. The route's train (solve.steps): sails at (0 11 0), wheel-a 1.3 m behind on the windshaft, each stage 0.1 m further back.
/// </summary>
public class ShaftSlotsTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private const string Catalogue = """
        (catalogue
         (entry g72 "Involute spur gear, 72 teeth, module 5 mm" gear "gear-ff4eb30017" 0.004 (3.3e-5 3.3e-5 6.5e-5) ((teeth 72.0) (module 0.005) (profile involute) (width 0.04) (pitch-radius 0.18))))
        """;

    private static BuildSession Session(params string[] commands)
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-shaft-slots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var s = new BuildSession(Materials, CatalogueReader.Parse(Catalogue), dir);
        foreach (var c in commands) s.Execute(c);
        return s;
    }

    private static void Near(double x, double y, double z, HeroicInventions.Sim.Machines.Vec3 v)
    {
        Assert.Equal(x, v.X, 6);
        Assert.Equal(y, v.Y, 6);
        Assert.Equal(z, v.Z, 6);
    }

    [Fact]
    public void ABareWindmillTakesItsFirstGear1Point3MetresBehindTheHubDownTheWindshaft()
    {
        var s = Session("(windmill sails #:at (0 11 0))");
        var shaft = ShaftSlots.Of(s.Document, "sails")!;
        Near(0, 0, -1, shaft.Behind);   // facing +z (90), the windshaft runs back along -z
        Assert.Equal(1.3, shaft.DefaultT, 9);
        Near(0, 11, -1.3, shaft.At(shaft.DefaultT));   // the route's wheel-a
        Assert.Empty(shaft.WheelProps.Where(p => p.Key == "heading-deg"));
    }

    [Fact]
    public void TheNextGearGoesATenthOfAMetreBehindTheHindmostOnTheAxleFromTheMillOrFromAWheelOnIt()
    {
        var s = Session("(windmill sails #:at (0 11 0))", "(wheel wheel-a #:catalogue g72 #:at (0 11 -1.3))", "(arbor sails wheel-a)");
        Near(0, 11, -1.4, ShaftSlots.Of(s.Document, "sails")!.At(ShaftSlots.Of(s.Document, "sails")!.DefaultT));
        var onWheel = ShaftSlots.Of(s.Document, "wheel-a")!;
        Near(0, 0, -1, onWheel.Behind);
        Assert.Equal(0.1, onWheel.DefaultT, 9);
        Near(0, 11, -1.4, onWheel.At(onWheel.DefaultT));
        // a second arbor through wheel-a is the same axle: the next goes behind wheel-b, from whichever part is chosen
        s.Execute("(wheel wheel-b #:catalogue g72 #:at (0 11 -1.4))");
        s.Execute("(arbor wheel-a wheel-b)");
        Assert.Equal(["wheel-a", "sails", "wheel-b"], ShaftSlots.AxleMates(s.Document, "wheel-a"));
        Near(0, 11, -1.5, ShaftSlots.Of(s.Document, "sails")!.At(ShaftSlots.Of(s.Document, "sails")!.DefaultT));
        var fromA = ShaftSlots.Of(s.Document, "wheel-a")!;
        Near(0, 11, -1.5, fromA.At(fromA.DefaultT));
    }

    [Fact]
    public void AMillFacingAnotherWayTurnsItsShaftAndItsWheels()
    {
        var s = Session("(windmill sails #:at (0 11 0) #:facing-deg 0)");   // facing +x
        var shaft = ShaftSlots.Of(s.Document, "sails")!;
        Near(-1, 0, 0, shaft.Behind);
        Near(-1.3, 11, 0, shaft.At(shaft.DefaultT));
        Assert.Contains(("heading-deg", "90"), shaft.WheelProps);   // +z turned 90° is +x: the wheel's axle along the windshaft
        s.Execute("(wheel w #:catalogue g72 #:at (-1.3 11 0))");
        s.Execute("(set w #:heading-deg 90)");   // a catalogue wheel takes only #:at and #:material: the rest is set after
        s.Execute("(arbor sails w)");
        var onWheel = ShaftSlots.Of(s.Document, "w")!;
        Near(-1, 0, 0, onWheel.Behind);
        Near(-1.4, 11, 0, onWheel.At(onWheel.DefaultT));
    }

    [Fact]
    public void ALoneWheelTakesTheNextGearATenthBehindItAlongMinusItsAxle()
    {
        var s = Session("(wheel w #:catalogue g72 #:at (1 2 3))", "(set w #:axis x)", "(block b #:at (0 0 0))");
        var shaft = ShaftSlots.Of(s.Document, "w")!;
        Near(-1, 0, 0, shaft.Behind);
        Near(0.9, 2, 3, shaft.At(shaft.DefaultT));
        Assert.Contains(("axis", "x"), shaft.WheelProps);
        Assert.Null(ShaftSlots.Of(s.Document, "b"));
    }
}
