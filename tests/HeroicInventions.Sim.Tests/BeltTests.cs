using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #51: belts. The Jolt-side drive (a tight belt grips, a loose one slips at its limit) is in heroic/tests/machine-behavior-test.rkt.</summary>
public class BeltTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static MachineDef BeltDrive() =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "belt-drive.machine")));

    /// <summary>Drums of 10 and 20 cm, 60 cm apart, hemp (mu 0.5): wrap pi - 2 asin(0.1 / 0.6) = 2.8067 rad.</summary>
    [Fact]
    public void ABeltWrapsTheSmallerDrumAndCarriesTwoT0TanhMuThetaOverTwo()
    {
        var belt = new Belt("b", 0.1, 0.2, 0.6, 0.5, 10);
        double theta = Math.PI - 2 * Math.Asin(0.1 / 0.6);
        Assert.Equal(2.8067, belt.Wrap, precision: 4);
        Assert.Equal(theta, belt.Wrap, precision: 12);
        Assert.Equal(2 * 10 * Math.Tanh(0.5 * theta / 2), belt.MaxForce, precision: 12);
        Assert.Equal(12.109, belt.MaxForce, precision: 3);
        // tight: T1 = 2 T0 e^(mu theta) / (1 + e^(mu theta)) and T2 = 2 T0 / (1 + e^(mu theta)): they sum to 2 T0 and their ratio is the capstan limit
        double e = Math.Exp(0.5 * theta), t1 = 2 * 10 * e / (1 + e), t2 = 2 * 10 / (1 + e);
        Assert.Equal(20, t1 + t2, precision: 12);
        Assert.Equal(e, t1 / t2, precision: 12);
        Assert.Equal(belt.MaxForce, t1 - t2, precision: 12);
        // twice the tension carries twice the force
        belt.Tension = 20;
        Assert.Equal(2 * 12.1086, belt.MaxForce, precision: 3);
    }

    [Fact]
    public void ABeltsLengthIsTwoRunsAndTheArcs()
    {
        var belt = new Belt("b", 0.1, 0.2, 0.6, 0.5, 10);
        double phi = Math.Asin(0.1 / 0.6);
        double runs = 2 * Math.Sqrt(0.6 * 0.6 - 0.1 * 0.1);                     // the tangent's length: sqrt(C^2 - (r2 - r1)^2), twice
        double arcs = 0.1 * (Math.PI - 2 * phi) + 0.2 * (Math.PI + 2 * phi);
        Assert.Equal(runs + arcs, belt.Length, precision: 10);
    }

    [Fact]
    public void ABeltEqualisesRimSpeedsWithinItsLimitAndSlipsBeyondIt()
    {
        var belt = new Belt("b", 0.1, 0.2, 0.6, 0.5, 10);
        const double dt = 1.0 / 120, ia = 0.004605, ib = 0.14736;

        // a small slip: one impulse removes all of it, and the drums' angular momentum is conserved
        var (ja, jb, force, left) = belt.Grip(0.05, ia, ib, dt);
        Assert.Equal(0, left, precision: 12);
        Assert.True(force < belt.MaxForce);
        Assert.Equal(-ja / 0.1, jb / 0.2, precision: 12);                        // the same belt impulse on both rims
        double rimA = 0.05 + ja / ia * 0.1, rimB = 0 + jb / ib * 0.2;            // rim speeds after: driver 0.05 fast, driven 0
        Assert.Equal(rimA, rimB, precision: 12);

        // a big slip: the impulse is the most it can carry, and the rest of the slip stays
        (_, _, force, left) = belt.Grip(5.0, ia, ib, dt);
        Assert.Equal(belt.MaxForce, force, precision: 10);
        double softness = 0.1 * 0.1 / ia + 0.2 * 0.2 / ib;
        Assert.Equal(5.0 - belt.MaxForce * dt * softness, left, precision: 10);
        Assert.True(left > 4);
        // the same slip the other way round is carried the other way
        (_, _, force, _) = belt.Grip(-5.0, ia, ib, dt);
        Assert.Equal(-belt.MaxForce, force, precision: 10);
    }

    [Fact]
    public void TheRuntimeReportsCapacityWrapAndTakesATighterBelt()
    {
        var runtime = new MachineRuntime(BeltDrive(), Materials);
        Assert.Equal(160.8, runtime.GetField("tight-belt", "wrap"), precision: 1);
        Assert.Equal(12.109, runtime.GetField("tight-belt", "capacity"), precision: 3);
        Assert.Equal(6.054, runtime.GetField("loose-belt", "capacity"), precision: 3);
        Assert.Equal(2.0, runtime.GetField("tight-belt", "capacity") / runtime.GetField("loose-belt", "capacity"), precision: 9);
        OperatorRun.Of(runtime, "(at 0 (loose-belt tension 10))");                // a person tightens the loose one to match
        Assert.Equal(12.109, runtime.GetField("loose-belt", "capacity"), precision: 3);
    }

    private static MachineDef Variant(MachineDef d, IReadOnlyList<PartSpec>? parts = null, IReadOnlyList<BeltSpec>? belts = null) =>
        new() { Name = d.Name, Parts = parts ?? d.Parts, Pipes = d.Pipes, Connects = d.Connects, SealedAir = d.SealedAir, Belts = belts ?? d.Belts };

    [Fact]
    public void ABadBeltIsReportedAtTheClause()
    {
        var def = BeltDrive();
        string Message(MachineDef d) => Assert.Throws<MachineFormatException>(() => new MachineRuntime(d, Materials)).Message;
        Assert.Contains("two different drums", Message(Variant(def, belts: [new BeltSpec("b", "loose-driver", "loose-driver", 5, "hemp", null)])));
        Assert.Contains("no tension grips nothing", Message(Variant(def, belts: [new BeltSpec("b", "loose-driver", "loose-driven", 0, "hemp", null)])));
        Assert.Contains("not a drum", Message(Variant(def, belts: [new BeltSpec("b", "loose-driver", "nowhere", 5, "hemp", null)])));
        var crowded = Variant(def, parts: def.Parts.Select(p => p.Id == "loose-driven" ? p with { At = new Vec3(0.05, 1, 0) } : p).ToList());
        Assert.Contains("too close for a belt", Message(crowded));
        var turned = Variant(def, parts: def.Parts.Select(p => p.Id == "loose-driven" ? p with { Props = new Dictionary<string, SExpr>(p.Props) { ["axis"] = new SSymbol("y") } } : p).ToList());
        Assert.Contains("different axes", Message(turned));
    }

    [Fact]
    public void TheEditorMakesBeltsByGestureAndCommandAndListsThem()
    {
        var s = new BuildSession(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        foreach (string line in CommandScript.For(BeltDrive())) s.Execute(line);
        s.Execute("(remove loose-belt)");
        Assert.Single(s.Document.Belts);

        string command = LinkGestures.Command(s.Document, LinkGestures.Kind.Belt, ["loose-driver", "loose-driven"]);
        Assert.Equal("(belt belt-1 loose-driver loose-driven #:tension 100)", command);
        s.Execute(command);
        s.Execute("(set-belt belt-1 #:tension 5)");
        Assert.Equal(5, s.Document.Belts.Single(b => b.Id == "belt-1").Tension);
        Assert.StartsWith("ok:", s.Execute("(check)"));
        Assert.Contains(LinkGestures.LinksOn(s.Document, "loose-driven"), l => l.Label.Contains("belt belt-1") && l.Remove == "(remove belt-1)");

        s.Execute("(remove loose-driver)");                                       // a drum removed takes its belt
        Assert.DoesNotContain(s.Document.Belts, b => b.Id == "belt-1");
        s.Execute("(post pillar #:at (5 0 0))");
        Assert.Contains("a belt runs on drums", Assert.Throws<InvalidOperationException>(() =>
            LinkGestures.Command(s.Document, LinkGestures.Kind.Belt, ["loose-driven", "pillar"])).Message);
    }
}
