using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// #220: a Stirling engine is one end of a world shaft link, as a windmill is, so the player's engine turns the found motor (the
/// `motors` crate's rotor, found-motor.rkt). Before, the join gesture refused it ("a stirling and a wheel") and the link builder had no
/// Stirling end. The engine is mars-stirling's `matched` one (1 m2 receiver, K 9.36 W/K, six 1 m2 heliostats at latitude -2), started at
/// its settled hot end, 40.2 C, where it makes 111.3 W indicated (the machine's header, worked by bisection; pinned at 0.35 of Carnot)
/// and has the default drag (#269); its own load is taken off (#:load 0),
/// so the motor is its only load. In the game the rotor is a Jolt body on a hinge; here a stand-in axle of the rotor's inertia, loaded by
/// the generator each tick as the view loads the body (<see cref="Generator.Step"/>).
/// </summary>
public class StirlingShaftLinkTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static MachineDef Load(string name) =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", name + ".machine")));

    /// <summary>mars-stirling with its matched engine warm (40.2 C) and unloaded.</summary>
    private static MachineDef WarmEngine() => MachineDef.Parse(string.Join("\n",
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "machines", "mars-stirling.machine"))
            .Select(l => l.Contains("(part matched stirling") ? l.Replace("(load 3.0)", "(load 0.0)").Replace("(temperature #f)", "(temperature 40.2)") : l)));

    private static readonly WorldDef World = WorldDef.Parse("(world w (place eng mars-stirling (at 0 0 0)) (place motors found-motor (at 30 0 0)) (place works found-electrics (at 0 0 40)))", "w");
    private static MachineDef? MachineOf(string label) => World.Placements.FirstOrDefault(p => p.Label == label) is { } p ? Load(p.Machine) : null;

    /// <summary>The found motor's rotor as the view gives it to a link: a body on a hinge (a 10 cm iron disc, 8 cm wide: I = m r^2 / 2).</summary>
    private sealed class Rotor : IShaft
    {
        public double AngularVelocity { get; set; }
        public double ShaftInertia { get; } = 0.5 * 7874 * Math.PI * 0.1 * 0.1 * 0.08 * 0.1 * 0.1;   // 0.0990 kg.m2
        public void AddAngularImpulse(double impulse) => AngularVelocity += impulse / ShaftInertia;
    }

    [Fact]
    public void AStirlingAndTheFoundMotorMakeAShaftByTheJoinGesture()
    {
        var a = WorldLinkGestures.Link(World, MachineOf, new LinkEnd("eng", "matched"), new LinkEnd("motors", "rotor"));
        Assert.Equal(("shaft-1", "shaft", 1.0, new LinkEnd("eng", "matched"), new LinkEnd("motors", "rotor")), (a.Id, a.Kind, a.Ratio, a.From, a.To));
        // a click on the motor's can is a click on the rotor it sits on (#247)
        var b = WorldLinkGestures.Link(World, MachineOf, new LinkEnd("motors", "motor"), new LinkEnd("eng", "matched"));
        Assert.Equal(("shaft", new LinkEnd("motors", "rotor"), new LinkEnd("eng", "matched")), (b.Kind, b.From, b.To));
    }

    private sealed class Linked
    {
        public readonly MachineRuntime Engine = new(WarmEngine(), Materials);
        public readonly MachineRuntime Motors = new(Load("found-motor").Translated(new Vec3(30, 0, 0)), Materials);
        public readonly MachineRuntime Works = new(Load("found-electrics").Translated(new Vec3(0, 0, 40)), Materials);
        public readonly Rotor Rotor = new();
        public readonly WorldLinks Links;
        public StirlingEngine Se => Engine.Stirlings["matched"];
        public Generator Motor => Motors.Generators["motor"];

        public Linked(bool wired)
        {
            var specs = new List<LinkSpec> { new("shaft-1", "shaft", new LinkEnd("eng", "matched"), new LinkEnd("motors", "rotor")) { Ratio = 1 } };
            if (wired) specs.Add(new("wire-1", "wire", new LinkEnd("motors", "motor"), new LinkEnd("works", "bank")));
            Links = WorldLinks.Build(specs, l => l switch { "eng" => Engine, "motors" => Motors, "works" => Works, _ => null },
                end => end is { Label: "motors", Part: "rotor" } ? Rotor : null);
            // the works' bank, at 20 C in its vault, made big enough not to fill in the run
            Works.SetField("bank", "capacity", 1000);
            Works.SetField("bank", "charge", 0);
        }

        /// <summary>One tick as the game steps a world: every machine, then the shafts, then the view loads the rotor with the generator.</summary>
        public void Step(double dt)
        {
            Engine.Step(dt);
            Motors.Step(dt);
            Works.Step(dt);
            Links.StepShafts(dt);
            double torque = Motor.Step(Rotor.AngularVelocity, dt);
            Rotor.AddAngularImpulse(-torque * dt);
        }
    }

    [Fact]
    public void TheLinkedMotorFilesItsChargeUnderTheStirling()
    {
        var w = new Linked(wired: true);
        Assert.All(w.Links.All, l => Assert.Null(l.Unfinished));
        Assert.Null(w.Motor.Prime);                                          // the crate's machine has no prime mover
        Assert.Equal("stirling", w.Motor.DrivenBy);                          // across the link: the player's engine
        var m = Assert.IsType<PrimeMover>(w.Motor.Mover);
        Assert.Equal(("matched", 1.0), (m.Id, m.Ratio));
    }

    /// <summary>
    /// What sets the shaft's speed: the engine's indicated power has no speed in it (P = f (1 - Tc/Th) K (Th - Tc)), so with no load the
    /// engine and the rotor gain its work as speed, less only the engine's own drag, k ω² (#269: k 1.38e-5 N·m·s²), which grows until it
    /// takes all of P at the free speed (P / k)^⅓ = 200.4 rad/s at 111.3 W. Time-stepped before running (I = 0.5 + 0.099): 84.9 rad/s
    /// after 20 s (with no drag it was sqrt(2 P t / I) = 86.2), both ends turning as one (ratio 1), and the work the gas cycle made,
    /// 2,226 J, is the two flywheels' 2,159 J plus the drag's 69 J of heat.
    /// </summary>
    [Fact]
    public void UnloadedTheEngineAndRotorGainItsWorkAsSpeedLessItsDrag()
    {
        var w = new Linked(wired: false);
        double dt = 1.0 / 120, work = 0;
        for (int i = 0; i < 20 * 120; i++)
        {
            w.Step(dt);
            work += w.Se.IndicatedPower * dt;
        }
        double kinetic = 0.5 * w.Se.ShaftInertia * w.Se.AngularVelocity * w.Se.AngularVelocity + 0.5 * w.Rotor.ShaftInertia * w.Rotor.AngularVelocity * w.Rotor.AngularVelocity;
        Assert.Equal(111.3, w.Se.IndicatedPower, 0.5);                       // the warm engine's settled power
        Assert.Equal(w.Se.AngularVelocity, w.Rotor.AngularVelocity, 0.02);   // one shaft
        Assert.Equal(84.9, w.Rotor.AngularVelocity, 0.5);                    // rad/s, still rising toward 200.4
        Assert.Equal(200.4, w.Se.FreeSpeed, 0.5);
        Assert.Equal(work, kinetic + w.Se.DragHeat, 0.01 * work);            // the work is in the two flywheels and the drag's heat
        Assert.Equal(0, w.Motor.Generated);                                  // no bank: an open circuit charges nothing
    }

    /// <summary>
    /// Wired to a warm bank, the motor is the load: the rotor settles where the engine's power less its drag is the motor's,
    /// 111.3 = 1.38e-5 X³ + 0.114592 (X - 157.08) X: X = 160.06 rad/s (1,528 rpm), τ 0.341 N.m, 54.6 W to the motor and 43.7 W of charge
    /// (worked before running; with no drag it was 163.04 rad/s and 89.0 W). The engine turns at the rotor's speed.
    /// </summary>
    [Fact]
    public void WiredToAWarmBankTheMotorLoadsTheEngineAndChargesAtItsPower()
    {
        var w = new Linked(wired: true);
        double dt = 1.0 / 120;
        for (int i = 0; i < 300 * 120; i++) w.Step(dt);
        Assert.Equal(160.06, w.Rotor.AngularVelocity, 0.5);
        Assert.Equal(w.Se.AngularVelocity, w.Rotor.AngularVelocity, 0.1);   // the rotor is read after the motor's tick: tau dt / I = 0.03 rad/s behind
        Assert.Equal(0.341, w.Motor.Torque, 0.02);
        Assert.Equal(0.8 * w.Se.ShaftPower, w.Motor.Power, 0.02 * w.Motor.Power);   // what the drag leaves at the shaft goes to the motor
        Assert.Equal(43.7, w.Motor.Power, 1.0);
        Assert.True(w.Works.Banks["bank"].Sources.GetValueOrDefault("stirling") > 0, "the works' bank files the motor's charge under the Stirling");
    }
}
