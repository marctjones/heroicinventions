using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #78: pipes and shafts joining parts of different machines in one
/// world, stepped with the machines, surviving a rebuild of either machine.
/// </summary>
public class WorldLinkTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static MachineDef Load(string name) =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", name + ".machine")));

    private static WorldDef World(string name) =>
        WorldDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "worlds", name + ".world")), name);

    /// <summary>A world stepped as the game steps it: the pipes, every machine, then the shafts.</summary>
    private sealed class Running
    {
        public readonly Dictionary<string, MachineRuntime> Machines = [];
        public WorldDef Def;
        public WorldLinks Links;

        public Running(WorldDef world)
        {
            Def = world;
            foreach (var p in world.Placements) Machines[p.Label] = new MachineRuntime(Load(p.Machine).Translated(p.At), Materials);
            Links = WorldLinks.Build(world.Links, l => Machines.GetValueOrDefault(l));
        }

        public void Step(double dt, int n)
        {
            for (int i = 0; i < n; i++)
            {
                Links.StepPipes(dt);
                foreach (var m in Machines.Values) m.Step(dt);
                Links.StepShafts(dt);
            }
        }

        /// <summary>A live edit: one machine rebuilt from a changed definition, taking over its state, and the links resolved again.</summary>
        public void Rebuild(string label, MachineDef def)
        {
            var next = new MachineRuntime(def, Materials);
            next.TakeStateFrom(Machines[label]);
            Machines[label] = next;
            Links = WorldLinks.Build(Def.Links, l => Machines.GetValueOrDefault(l));
        }
    }

    private static MachineDef WithExtraTank(MachineDef def)
    {
        var extra = new PartSpec("new-tank", "tank", "oak", new Vec3(50, 0, 50),
            new Dictionary<string, SExpr> { ["area"] = new SNumber(1), ["height"] = new SNumber(1), ["water"] = new SNumber(0.2) },
            [new PortSpec("inlet", "water", 0)], null);
        return new MachineDef
        {
            Name = def.Name, Source = def.Source, Ambient = def.Ambient, Sun = def.Sun,
            Parts = [.. def.Parts, extra], Pipes = def.Pipes, Connects = def.Connects, SealedAir = def.SealedAir,
            Ropes = def.Ropes, Arbors = def.Arbors, Meshes = def.Meshes, Lifts = def.Lifts,
            Sources = def.Sources, Channels = def.Channels, Cylinders = def.Cylinders,
        };
    }

    [Fact]
    public void LinksAreReadWrittenAndChecked()
    {
        var world = World("linked-pipe");
        var feed = Assert.Single(world.Links);
        Assert.Equal(("feed", "pipe", 0.002), (feed.Id, feed.Kind, feed.Conductance));
        Assert.Equal(new LinkEnd("tank-a", "cistern", "outlet"), feed.From);
        Assert.Equal(new LinkEnd("tank-b", "trough", "inlet"), feed.To);

        var again = WorldDef.Parse(world.Write());
        Assert.Equal(world.Write(), again.Write());
        Assert.Equal(world.Links.Select(l => l with { Location = null }), again.Links.Select(l => l with { Location = null }));
        var shaft = Assert.Single(World("linked-shaft").Links);
        Assert.Equal(("shaft", 1.0, (string?)null), (shaft.Kind, shaft.Ratio, shaft.From.Port));

        const string two = "(world w (place a cistern (at 0 0 0)) (place b trough (at 4 0 0))";
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse(two + " (link x pipe (from a cistern outlet) (to c trough inlet)))"));   // no c
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse(two + " (link x pipe (from a cistern) (to b trough inlet)))"));        // no port
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse(two + " (link x belt (from a cistern) (to b trough)))"));              // no such kind
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse(two + " (link x shaft (from a cistern) (to a cistern)))"));            // one machine
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse(two + " (link x shaft (from a p) (to b q) (ratio 0)))"));
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse(two + " (link x shaft (from a p) (to b q)) (link x shaft (from a p) (to b q)))"));
    }

    /// <summary>
    /// The cross-machine pipe moves water exactly as the same pipe inside one
    /// machine does, and both match the hand calculation in
    /// cistern-and-trough.rkt: 229.4 L left at 60 s, 122.9 L at 120 s.
    /// </summary>
    [Fact]
    public void APipeBetweenMachinesFlowsExactlyAsOneInsideAMachine()
    {
        var world = new Running(World("linked-pipe"));
        var one = world.Machines["reference"];
        var (cistern, trough) = (world.Machines["tank-a"].Tanks["cistern"], world.Machines["tank-b"].Tanks["trough"]);
        for (int s = 1; s <= 300; s++)
        {
            world.Step(0.01, 100);
            Assert.Equal(one.Tanks["cistern"].WaterVolume, cistern.WaterVolume, 12);
            Assert.Equal(one.Tanks["trough"].WaterVolume, trough.WaterVolume, 12);
            Assert.Equal(one.Pipes["feed"].Flow, world.Links.All[0].Pipe!.Flow, 12);
            if (s == 60) Assert.Equal(229.4, cistern.WaterVolume * 1000, 0);
            if (s == 120) Assert.Equal(122.9, cistern.WaterVolume * 1000, 0);
        }
        Assert.True(cistern.WaterVolume < 1e-6, "dry by 275 s");
        Assert.Equal(400, trough.WaterVolume * 1000, 3);
        Assert.Equal(0, world.Links.FieldGetters["feed.unfinished"]());
    }

    /// <summary>
    /// The free sails drive the dry mill through a shaft: both settle at the
    /// sails' best tip-speed ratio, 14.32 rpm, the shaft carrying the
    /// stones' 8171 N·m and 12.26 kW (dry-mill.rkt).
    /// </summary>
    [Fact]
    public void AShaftBetweenMachinesCarriesTheTorqueTheDrivenMachineNeeds()
    {
        var world = new Running(World("linked-shaft"));
        var sails = world.Machines["sails"].Windmills["sails"];
        var wheel = world.Machines["mill"].WaterWheels["wheel"];
        world.Step(0.01, 12000);   // 120 s, a dozen time constants
        var f = world.Links.FieldGetters;
        Assert.Equal(14.32, sails.Rpm, 2);
        Assert.Equal(14.32, wheel.Rpm, 2);
        Assert.Equal(14.32, f["drive.driven-rpm"](), 2);
        Assert.Equal(8171, f["drive.torque"](), 0);
        Assert.Equal(12.26, f["drive.power"]() / 1000, 2);
        Assert.Equal(12.26, wheel.Power / 1000, 2);   // what the stones take is what the shaft gave
        Assert.Equal(0.3, sails.PowerCoefficient, 3);

        // unjoined, the sails race to tip-speed ratio 5 and the stones stand still
        var alone = new Running(World("linked-shaft").WithoutLink("drive"));
        alone.Step(0.01, 12000);
        Assert.Equal(28.6, alone.Machines["sails"].Windmills["sails"].Rpm, 1);
        Assert.Equal(0, alone.Machines["mill"].WaterWheels["wheel"].Rpm);
    }

    /// <summary>A shaft through a gearbox: the driven end turns Ratio times as fast, and power, not torque, is what passes.</summary>
    [Fact]
    public void AShaftWithARatioTradesSpeedForTorque()
    {
        var world = new Running(World("linked-shaft").WithoutLink("drive").WithLink(
            new LinkSpec("geared", "shaft", new LinkEnd("sails", "sails"), new LinkEnd("mill", "wheel")) { Ratio = 0.5 }));
        world.Step(0.01, 12000);
        var (sails, wheel) = (world.Machines["sails"].Windmills["sails"], world.Machines["mill"].WaterWheels["wheel"]);
        // geared down 2:1 the stones' 8171 N·m reaches the sails as half that, τ0/2 = τ0 (2 − λ/2.5):
        // λ = 3.75, ω = 3.75 × 6 / 10 = 2.25 rad/s (21.49 rpm); the stones at half, 10.74 rpm, taking 8171 × 1.125 = 9.19 kW
        Assert.Equal(21.49, sails.Rpm, 2);
        Assert.Equal(10.74, wheel.Rpm, 2);
        Assert.Equal(8171, world.Links.FieldGetters["geared.torque"](), 0);
        Assert.Equal(9.19, world.Links.FieldGetters["geared.power"]() / 1000, 2);
    }

    /// <summary>Rebuilding either machine mid-run (a live edit) leaves the flow and the turning exactly as if it had not been touched.</summary>
    [Theory]
    [InlineData("linked-pipe", "tank-b")]
    [InlineData("linked-pipe", "tank-a")]
    [InlineData("linked-shaft", "mill")]
    [InlineData("linked-shaft", "sails")]
    public void LinksCarryOnAcrossARebuildOfEitherMachine(string worldName, string rebuilt)
    {
        var untouched = new Running(World(worldName));
        var edited = new Running(World(worldName));
        untouched.Step(0.01, 3000);
        edited.Step(0.01, 3000);   // 30 s
        var place = edited.Def.Placements.First(p => p.Label == rebuilt);
        edited.Rebuild(rebuilt, WithExtraTank(Load(place.Machine)).Translated(place.At));
        untouched.Step(0.01, 3000);
        edited.Step(0.01, 3000);   // 30 s more
        foreach (var (label, m) in untouched.Machines)
            foreach (var (field, get) in m.FieldGetters)
                Assert.True(Math.Abs(get() - edited.Machines[label].FieldGetters[field]()) <= 1e-9 * Math.Max(1, Math.Abs(get())),
                            $"{label} {field}: {get()} untouched, {edited.Machines[label].FieldGetters[field]()} after rebuilding {rebuilt}");
        foreach (var (field, get) in untouched.Links.FieldGetters)
            Assert.Equal(get(), edited.Links.FieldGetters[field](), 9);
    }

    /// <summary>A link whose port has gone is kept but unfinished, says why, and moves nothing.</summary>
    [Fact]
    public void ALinkWhosePortIsGoneIsUnfinished()
    {
        var world = new Running(World("linked-pipe").WithoutLink("feed").WithLink(
            new LinkSpec("feed", "pipe", new LinkEnd("tank-a", "cistern", "outlet"), new LinkEnd("tank-b", "trough", "spout"))));
        var link = Assert.Single(world.Links.All);
        Assert.Contains("no port spout", link.Unfinished);
        world.Step(0.01, 1000);
        Assert.Equal(400, world.Machines["tank-a"].Tanks["cistern"].WaterVolume * 1000, 9);
        Assert.Equal(1, world.Links.FieldGetters["feed.unfinished"]());

        var shaft = new Running(World("linked-shaft").WithoutLink("drive").WithLink(
            new LinkSpec("drive", "shaft", new LinkEnd("sails", "trestle"), new LinkEnd("mill", "wheel"))));
        Assert.Contains("post", shaft.Links.All[0].Unfinished);
    }
}

/// <summary>The world's "Join machines" tool: what two clicks make, and what it refuses.</summary>
public class WorldLinkGestureTests
{
    private static MachineDef Load(string name) =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", name + ".machine")));

    private static readonly WorldDef World = WorldDef.Parse(
        "(world w (place a cistern (at 0 0 0)) (place b trough (at 4 0 0)) (place s free-sails (at 0 0 9)) (place m dry-mill (at 8 0 9)))");
    private static MachineDef? MachineOf(string label) => World.Placements.FirstOrDefault(p => p.Label == label) is { } p ? Load(p.Machine) : null;

    [Fact]
    public void TwoTankPortsMakeAPipeAndTwoTurningPartsAShaft()
    {
        var pipe = WorldLinkGestures.Link(World, MachineOf, new LinkEnd("a", "cistern", "outlet"), new LinkEnd("b", "trough", "inlet"));
        Assert.Equal(("pipe-1", "pipe"), (pipe.Id, pipe.Kind));
        var shaft = WorldLinkGestures.Link(World, MachineOf, new LinkEnd("s", "sails"), new LinkEnd("m", "wheel"));
        Assert.Equal(("shaft-1", "shaft", 1.0), (shaft.Id, shaft.Kind, shaft.Ratio));

        // and the world with them reads back the same
        var joined = World.WithLink(pipe).WithLink(shaft);
        Assert.Equal(joined.Write(), WorldDef.Parse(joined.Write()).Write());
        Assert.Equal("pipe-2", joined.NextLinkId("pipe"));
        Assert.Throws<InvalidOperationException>(() => WorldLinkGestures.Link(joined, MachineOf, new LinkEnd("b", "trough", "inlet"), new LinkEnd("a", "cistern", "outlet")));
    }

    [Theory]
    [InlineData("a", "cistern", "outlet", "a", "cistern", "outlet", "both parts of a")]
    [InlineData("a", "cistern", null, "b", "trough", "inlet", "port to port")]
    [InlineData("a", "cistern", "outlet", "m", "wheel", null, "a pipe joins two tanks' ports")]
    [InlineData("s", "trestle", null, "m", "wheel", null, "is a post")]
    [InlineData("a", "cistern", "spout", "b", "trough", "inlet", "no port spout")]
    public void RefusesPicksThatDoNotMakeALink(string la, string pa, string? qa, string lb, string pb, string? qb, string why)
    {
        var e = Assert.Throws<InvalidOperationException>(() => WorldLinkGestures.Link(World, MachineOf, new LinkEnd(la, pa, qa), new LinkEnd(lb, pb, qb)));
        Assert.Contains(why, e.Message);
    }

    [Fact]
    public void RefusesJoiningTheSamePairTwice()
    {
        var joined = World.WithLink(WorldLinkGestures.Link(World, MachineOf, new LinkEnd("s", "sails"), new LinkEnd("m", "wheel")));
        Assert.Throws<InvalidOperationException>(() => WorldLinkGestures.Link(joined, MachineOf, new LinkEnd("m", "wheel"), new LinkEnd("s", "sails")));
    }
}
