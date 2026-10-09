using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #208: a wire link joins a generator in one machine to a battery bank in another, charging it as the same generator
/// charges its own machine's bank. Issue #82: every link kind, whether the world file declared it or it was made during play,
/// is saved with the world and comes back working.
/// </summary>
public class WireLinkTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static string MachineText(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", name + ".machine"));
    private static MachineDef Load(string name) => MachineDef.Parse(MachineText(name));

    // found-electrics' hand-worked numbers (racket/machines/found-electrics.rkt): the rotor settles at 125 w = 174.479 rad/s,
    // 17.40 rad/s over the cut-in, tau_g = 0.114592 x 17.40 = 1.9938 N.m, and the bank is charged with 0.8 x tau_g x w = 278.30 W
    private const double RotorOmega = 174.479;
    private const double ChargeWatts = 278.30;

    private sealed class Running
    {
        public readonly Dictionary<string, MachineRuntime> Machines = [];
        public readonly WorldDef Def;
        public WorldLinks Links;

        public Running(WorldDef world, Func<string, MachineDef>? load = null)
        {
            Def = world;
            load ??= Load;
            foreach (var p in world.Placements) Machines[p.Label] = new MachineRuntime(load(p.Machine).Translated(p.At), Materials);
            Links = WorldLinks.Build(world.Links, l => Machines.GetValueOrDefault(l));
        }

        /// <summary>One tick as the game runs it, the geared rotor held at <paramref name="rotor"/> rad/s (the view's job: only the engine layer can turn a train).</summary>
        public void Step(double dt, int n, double rotor = RotorOmega)
        {
            for (int i = 0; i < n; i++)
            {
                Links.StepPipes(dt);
                foreach (var m in Machines.Values) m.Step(dt);
                foreach (var m in Machines.Values) foreach (var g in m.Generators.Values) g.Step(rotor, dt);
                Links.StepShafts(dt);
            }
        }
    }

    private static WorldDef Wired(string name = "wire-test") => WorldDef.Parse($@"(world {name}
        (place works found-electrics (at 0 0 0))
        (place vault remote-vault (at 6 0 0))
        (link wire-1 wire (from works motor) (to vault bank)))", name);

    [Fact]
    public void AWireIsReadWrittenAndChecked()
    {
        var world = Wired();
        var wire = Assert.Single(world.Links);
        Assert.Equal(("wire-1", "wire", new LinkEnd("works", "motor"), new LinkEnd("vault", "bank")), (wire.Id, wire.Kind, wire.From, wire.To));
        Assert.Equal(world.Write(), WorldDef.Parse(world.Write()).Write());
        Assert.DoesNotContain("ratio", world.Write());
        Assert.Throws<MachineFormatException>(() => WorldDef.Parse("(world w (place a found-electrics (at 0 0 0)) (link x wire (from a motor) (to b bank)))"));   // no b
        // the shipped world
        var shipped = WorldDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "worlds", "wired-electrics.world")), "wired-electrics");
        Assert.Equal("wire", Assert.Single(shipped.Links).Kind);
    }

    [Fact]
    public void TheJoinGestureMakesAWireFromAGeneratorAndABankInEitherOrderAndRefusesTheRest()
    {
        var world = new WorldDef { Name = "w", Placements = Wired().Placements };
        MachineDef? MachineOf(string label) => Load(world.Placements.First(p => p.Label == label).Machine);
        var gen = new LinkEnd("works", "motor");
        var bank = new LinkEnd("vault", "bank");
        var a = WorldLinkGestures.Link(world, MachineOf, gen, bank);
        var b = WorldLinkGestures.Link(world, MachineOf, bank, gen);
        Assert.Equal(("wire-1", "wire", gen, bank), (a.Id, a.Kind, a.From, a.To));
        Assert.Equal((a.Kind, a.From, a.To), (b.Kind, b.From, b.To));
        var joined = world.WithLink(a);
        Assert.Contains("already wired", Assert.Throws<InvalidOperationException>(() => WorldLinkGestures.Link(joined, MachineOf, gen, bank)).Message);
        // a generator and a generator, or a bank and a vault, make no wire
        Assert.Throws<InvalidOperationException>(() => WorldLinkGestures.Link(world, MachineOf, bank, new LinkEnd("vault", "cells")));
        Assert.Throws<InvalidOperationException>(() => WorldLinkGestures.Link(world, MachineOf, gen, new LinkEnd("works", "bank")));   // both in one machine
    }

    /// <summary>
    /// The wire carries what found-electrics' own wire carries: with the rotor at the train's settled speed the bank in the other machine
    /// is charged at 278.30 W, the same joules as the same generator charging its own bank, the record says wind, and the machine's own
    /// bank (which the motor would charge unwired) takes nothing.
    /// </summary>
    [Fact]
    public void AGeneratorInOneMachineChargesABankInAnotherAsItChargesItsOwn()
    {
        var world = new Running(Wired());
        var alone = new MachineRuntime(Load("found-electrics"), Materials);
        var gen = world.Machines["works"].Generators["motor"];
        var theirs = world.Machines["vault"].Banks["bank"];
        Assert.Same(theirs, gen.Bank);
        Assert.Equal(0, world.Links.FieldGetters["wire-1.unfinished"]());

        for (int i = 0; i < 1000; i++)
        {
            world.Step(0.01, 1);
            alone.Step(0.01);
            alone.Generators["motor"].Step(RotorOmega, 0.01);
        }
        Assert.Equal(ChargeWatts, gen.Delivered, 1);
        Assert.Equal(ChargeWatts, world.Links.FieldGetters["wire-1.power"](), 1);
        Assert.Equal(9.94, world.Links.FieldGetters["wire-1.current"](), 2);                 // at the pack's 28 V
        Assert.Equal(ChargeWatts * 10 / 3600, theirs.ChargeWh, 2);                          // 10 s of it, in Wh
        Assert.Equal(alone.Banks["bank"].Charge, theirs.Charge, 6);                         // joule for joule what the same generator gives its own bank
        Assert.Equal(0, world.Machines["works"].Banks["bank"].Charge);                      // the wired generator no longer charges its own
        Assert.Equal(theirs.Charge, theirs.Sources["wind"], 9);                             // the record names the prime mover
        Assert.Single(theirs.Sources);
        Assert.Equal(theirs.ChargeWh, world.Links.FieldGetters["wire-1.from-wind"](), 9);
        Assert.Equal(gen.Torque, alone.Generators["motor"].Torque, 12);                     // the same load on the shaft: 1.9938 N.m
        Assert.Equal(1.9938, gen.Torque, 3);
    }

    /// <summary>Full, the bank is an open circuit down the wire: no charge, no torque. Out of its 0 to 45 C it is too, and takes charge again when it comes back.</summary>
    [Fact]
    public void TheWireIsOpenCircuitWhenTheBankIsFullOrOutOfRange()
    {
        var world = new Running(Wired());
        var gen = world.Machines["works"].Generators["motor"];
        var bank = world.Machines["vault"].Banks["bank"];

        bank.Sensed = () => 50;    // too hot to charge
        world.Step(0.01, 100);
        Assert.Equal((0, 0, 0), (bank.Charge, gen.Torque, gen.Delivered));
        Assert.Equal(1, world.Links.FieldGetters["wire-1.open-circuit"]());
        Assert.Equal(0, gen.Power);   // no torque, so nothing is made either

        bank.Sensed = null;        // back to 20 C
        world.Step(0.01, 100);
        Assert.True(bank.Charge > 0 && gen.Torque > 1.9);

        bank.Charge = bank.Capacity - 100;   // 100 J short of full: it takes 100 J and no more
        world.Step(0.01, 100);
        Assert.True(bank.Full);
        Assert.Equal(bank.Capacity, bank.Charge, 6);
        world.Step(0.01, 1);
        Assert.Equal((0.0, 0.0), (gen.Torque, gen.Delivered));
        Assert.Equal(1, world.Links.FieldGetters["wire-1.open-circuit"]());
    }

    /// <summary>A generator that names no bank (#:charges #f) charges nothing until wired; the wire is its only bank.</summary>
    [Fact]
    public void AGeneratorWithNoBankOfItsOwnIsOpenCircuitUntilItIsWired()
    {
        string text = string.Join('\n', MachineText("found-electrics").Split('\n').Where(l => !l.Contains(" battery-bank "))).Replace("(charges bank)", "(charges #f)");
        MachineDef Bankless(string name) => name == "found-electrics" ? MachineDef.Parse(text) : Load(name);

        var unwired = new Running(new WorldDef { Name = "w", Placements = Wired().Placements }, Bankless);
        var g0 = unwired.Machines["works"].Generators["motor"];
        Assert.Null(g0.Bank);
        unwired.Step(0.01, 100);
        Assert.Equal((0.0, 0.0), (g0.Torque, g0.Delivered));

        var world = new Running(Wired(), Bankless);
        var gen = world.Machines["works"].Generators["motor"];
        world.Step(0.01, 500);
        Assert.Equal(ChargeWatts, gen.Delivered, 1);
        Assert.Equal("wind", Assert.Single(world.Machines["vault"].Banks["bank"].Sources).Key);

        // taking the wires off (a world rebuilt without them) leaves it open circuit again
        world.Links.Release();
        Assert.Null(gen.Bank);
    }

    [Fact]
    public void AWireWithAnEndGoneIsKeptUnfinishedAndDoesNothing()
    {
        var world = new Running(Wired());
        var broken = WorldLinks.Build([new LinkSpec("w", "wire", new LinkEnd("works", "sails"), new LinkEnd("vault", "bank"))], l => world.Machines.GetValueOrDefault(l));
        Assert.Contains("starts at a generator", Assert.Single(broken.All).Unfinished);
        var noBank = WorldLinks.Build([new LinkSpec("w", "wire", new LinkEnd("works", "motor"), new LinkEnd("vault", "cells"))], l => world.Machines.GetValueOrDefault(l));
        Assert.Contains("ends at a battery bank", Assert.Single(noBank.All).Unfinished);
        world.Links.Release();
        var twice = WorldLinks.Build([new LinkSpec("a", "wire", new LinkEnd("works", "motor"), new LinkEnd("vault", "bank")),
                                      new LinkSpec("b", "wire", new LinkEnd("works", "motor"), new LinkEnd("vault", "bank"))], l => world.Machines.GetValueOrDefault(l));
        Assert.Null(twice.All[0].Unfinished);
        Assert.Contains("already wired", twice.All[1].Unfinished);
    }

    // ---- #82: links in a save ----

    /// <summary>
    /// A world of every kind: a pipe the file declares, then (during play) a shaft and a wire joined by the gesture. It is run, saved mid-run
    /// to text, and a world built fresh from the FILE (the pipe only) has the save's links and states laid on it. Run on beside the one that
    /// never stopped, every link's reading and every machine's state is the same.
    /// </summary>
    [Fact]
    public void EveryKindOfLinkIsSavedWithTheWorldAndComesBackWorking()
    {
        var file = WorldDef.Parse(@"(world all-links
            (place tank-a cistern (at 0 0 0))
            (place tank-b trough (at 4 0 0))
            (place sails free-sails (at 20 0 0))
            (place mill dry-mill (at 36 0 6))
            (place works found-electrics (at 0 0 40))
            (place vault remote-vault (at 6 0 40))
            (link feed pipe (from tank-a cistern outlet) (to tank-b trough inlet) (conductance 0.002)))", "all-links");
        MachineDef? MachineOf(WorldDef w, string label) => Load(w.Placements.First(p => p.Label == label).Machine);

        var live = new Running(file);
        live.Step(0.01, 500);
        // joined during play, by the gestures
        var def = file;
        def = def.WithLink(WorldLinkGestures.Link(def, l => MachineOf(def, l), new LinkEnd("sails", "sails"), new LinkEnd("mill", "wheel")));
        def = def.WithLink(WorldLinkGestures.Link(def, l => MachineOf(def, l), new LinkEnd("vault", "bank"), new LinkEnd("works", "motor")));
        Assert.Equal(["feed", "shaft-1", "wire-1"], def.Links.Select(l => l.Id));
        var madeLive = new Running(def);                        // (the same machines, rebuilt state-less: carry the live state over)
        foreach (var (label, rt) in live.Machines) RuntimeState.Restore(madeLive.Machines[label], RuntimeState.Capture(rt));
        madeLive.Step(0.01, 800);

        // save: the machines' states and every link
        var save = new WorldSave
        {
            Kind = "world", Name = def.Name, Links = def.Links,
            Machines = madeLive.Machines.Select(kv => new SavedMachine(kv.Key, def.Placements.First(p => p.Label == kv.Key).Machine, kv.Value.Time, RuntimeState.Capture(kv.Value))).ToList(),
        };
        string text = save.ToText();
        Assert.Contains("(link wire-1 wire", text);
        Assert.Contains("(link shaft-1 shaft", text);
        Assert.Contains("(link feed pipe", text);

        // load: the world from its file has only the pipe; the save puts back what play made
        var loaded = WorldSave.Parse(text);
        var left = new List<string>();
        var restoredDef = LinkForms.Restore(file, loaded.Links!, left);
        Assert.Empty(left);
        Assert.Equal(def.Links.Select(l => l with { Location = null }), restoredDef.Links.Select(l => l with { Location = null }));
        var back = new Running(restoredDef);
        foreach (var m in loaded.Machines) Assert.Empty(RuntimeState.Restore(back.Machines[m.Label], m.State));

        // the run that kept going and the run that was loaded, side by side
        for (int s = 0; s < 6; s++)
        {
            madeLive.Step(0.01, 150);
            back.Step(0.01, 150);
            foreach (var (key, get) in madeLive.Links.FieldGetters)
                Assert.True(get() == back.Links.FieldGetters[key]() || Math.Abs(get() - back.Links.FieldGetters[key]()) <= 1e-9 * Math.Max(1, Math.Abs(get())), $"{key} at step {s}: {get()} vs {back.Links.FieldGetters[key]()}");
            foreach (var (label, rt) in madeLive.Machines)
                foreach (var (key, get) in rt.FieldGetters)
                {
                    double a = get(), b = back.Machines[label].FieldGetters[key]();
                    Assert.True(a == b || Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Abs(a)), $"{label}.{key} at step {s}: {a} vs {b}");
                }
        }
        var bank = back.Machines["vault"].Banks["bank"];
        Assert.True(bank.Charge > 0 && madeLive.Machines["vault"].Banks["bank"].Charge == bank.Charge);
        Assert.True(back.Links.FieldGetters["shaft-1.power"]() > 0, "the shaft carries power again");
        Assert.True(Math.Abs(back.Links.FieldGetters["feed.flow"]()) > 0 || back.Machines["tank-a"].Tanks["cistern"].WaterVolume < 1e-3, "the pipe flows (or has run dry)");
        Assert.Equal(bank.Charge, bank.Sources["wind"], 9);

        // a save with no link list (an older one) leaves the file's links standing
        Assert.Null(WorldSave.Parse(new WorldSave { Kind = "world", Name = "w", Machines = [] }.ToText()).Links);
    }
}
