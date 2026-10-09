using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #211: an enclosure holds another machine's heat store when the store's point lies in its box (<see cref="WorldZones"/>).
/// The machines are vault-night (night-heat's tight vault with its rock, alone) and found-bank (the crate, its cells and bank), each
/// placed as a world places them. The Jolt side (the crate pushed in and out, a save, the night) is racket/heroic/tests/vault-zone-test.rkt.
/// </summary>
public class WorldZonesTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private static string Text(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", name + ".machine"));
    private static MachineRuntime Placed(string text, string label, Vec3 at) =>
        new(WorldDef.Placed(MachineDef.Parse(text), new Placement(label, label, at, null), null), Materials);

    /// <summary>Total heat of an isolated vault and what it holds: the room's node (m c T of its gas and walls) and each store's.</summary>
    private static double Energy(Enclosure room) => room.HeatCapacity * room.Temperature + room.Stores.Sum(s => s.Heat);

    [Fact]
    public void AnIsolatedVaultAndAStoreOfAnotherMachineConserveEnergyTo1e9WhicheverMachineStepsFirst()
    {
        foreach (bool vaultFirst in new[] { true, false })
        {
            // the vault with no wall and no insulation (nothing leaves it), a node of 500 J/K; the rock at 200 C radiates through its open
            // lid; the found bank's cells (16 kJ/K at -63 C) are at its #:at, (0, 0.25, 0), in the middle of the 0.5 m box
            var vault = Placed(Text("vault-night").Replace("(wall regolith)", "(wall #f)").Replace("(heat-capacity 0.0)", "(heat-capacity 500.0)"),
                               "vault", new Vec3(0, 0, 0));
            var bank = Placed(Text("found-bank"), "bank", new Vec3(0, 0, 0));
            var room = vault.Enclosures["vault"];
            var cells = bank.HeatStores["cells"];
            var zones = new WorldZones();
            var machines = new List<(string, MachineRuntime)> { ("vault", vault), ("bank", bank) };
            var log = zones.Update(machines, (label, id) => (label == "vault" ? vault : bank).Def.Part(id)!.At);
            Assert.Equal(["bank.cells joined vault.vault at -63.00 C (the room at -55.00 C)"], log);
            Assert.Same(room, cells.Zone);
            Assert.Contains(cells, room.Stores);
            Assert.Equal(0, room.Insulation);
            Assert.Null(room.Wall);

            double e0 = Energy(room);
            for (int i = 0; i < 3600; i++)
            {
                zones.Update(machines, (label, id) => (label == "vault" ? vault : bank).Def.Part(id)!.At);
                if (vaultFirst) { vault.Step(1); bank.Step(1); } else { bank.Step(1); vault.Step(1); }
            }
            double e1 = Energy(room);
            Assert.True(Math.Abs(e1 - e0) <= 1e-9 * Math.Abs(e0), $"energy {e0} J then {e1} J ({(e1 - e0) / e0:e2} relative)");
            // and heat did flow: the rock gave, the cells took (radiation only, through 610 Pa), towards the mixed temperature
            // (33.6 kJ/K x 200 + 16 kJ/K x -63 + 0.5 kJ/K x -55) / 50.1 kJ/K = 113.5 C, which with no wall to lose to they would reach
            double rock = vault.HeatStores["rock"].Temperature;
            Assert.True(cells.Temperature > -40 && rock < 190 && cells.Temperature < rock, $"cells {cells.Temperature} C, rock {rock} C");
            Assert.Equal(cells.Given, -(cells.Heat - (-63 * 16 * 1000)), 6);   // what the cells took is what the room gave them
        }
    }

    [Fact]
    public void ContainmentFollowsThePointInAndOut_AndAStoreInItsOwnRoomStaysThere()
    {
        var vault = Placed(Text("vault-night"), "vault", new Vec3(0, 0, 0));
        var bank = Placed(Text("found-bank"), "bank", new Vec3(1.5, 0, 0));   // beside it: x 1.5, outside the 0.25 half-width
        var heat = Placed(Text("night-heat"), "heat", new Vec3(0, 0, 10));     // far off: its own rooms hold its own stores
        var machines = new List<(string, MachineRuntime)> { ("vault", vault), ("bank", bank), ("heat", heat) };
        var cellsAt = bank.Def.Part("cells")!.At;
        Vec3 Point(string label, string id) => label == "bank" && id == "cells" ? cellsAt : machines.First(m => m.Item1 == label).Item2.Def.Part(id)!.At;
        var zones = new WorldZones();
        var cells = bank.HeatStores["cells"];
        var room = vault.Enclosures["vault"];

        Assert.Empty(zones.Update(machines, Point));
        Assert.Same(bank.Outside, cells.Zone);

        // the crate pushed in: its centre 0.10 m from the vault's middle, 0.395 m up (the rest the crate came to in the game)
        cellsAt = new Vec3(0.10, 0.395, 0);
        Assert.Single(zones.Update(machines, Point));
        Assert.Same(room, cells.Zone);
        Assert.Equal(new WorldZones.Hold("vault", "vault", "bank", "cells"), Assert.Single(zones.Holds));
        // stepping the found bank alone no longer cools the cells in the open: the room holds them
        double before = cells.Temperature;
        bank.Step(1);
        Assert.Equal(before, cells.Temperature);

        // a change of planet resets the found bank's zones; the next update puts the cells back in the room
        bank.ChangePlanet(bank.Outside.Planet);
        Assert.Same(bank.Outside, cells.Zone);
        Assert.Empty(zones.Update(machines, Point));
        Assert.Same(room, cells.Zone);

        // straddling the wall: the point decides (0.25 m from the middle is still in, 0.26 out)
        cellsAt = new Vec3(0.25, 0.395, 0);
        Assert.Empty(zones.Update(machines, Point));
        cellsAt = new Vec3(0.26, 0.395, 0);
        Assert.Equal(["bank.cells left vault.vault at -63.00 C"], zones.Update(machines, Point));
        Assert.Same(bank.Outside, cells.Zone);
        Assert.DoesNotContain(cells, room.Stores);
        Assert.Single(room.Stores);   // its own rock

        // night-heat's stores are in its own rooms, and stay there even with the vault and the cells moved over its tight vault
        cellsAt = new Vec3(-2.0, 0.25, 10);
        zones.Update(machines, Point);
        Assert.Same(heat.Enclosures["tight"], cells.Zone);   // the cells, the foreign store, join tight
        Assert.Same(heat.Enclosures["tight"], heat.HeatStores["tight-bank"].Zone);
        Assert.Equal(3, heat.Enclosures["tight"].Stores.Count);
        var vaultOver = Placed(Text("vault-night"), "vault2", new Vec3(-2, 0, 10));   // a second vault exactly over tight
        var withOver = machines.Append(("vault2", vaultOver)).ToList();
        Vec3 Point2(string label, string id) => label == "vault2" ? vaultOver.Def.Part(id)!.At : Point(label, id);
        zones.Update(withOver, Point2);
        Assert.Same(heat.Enclosures["tight"], heat.HeatStores["tight-bank"].Zone);   // own room wins
        Assert.Same(vaultOver.Enclosures["vault"], vaultOver.HeatStores["rock"].Zone);

        zones.Release();
        Assert.Same(bank.Outside, cells.Zone);
        Assert.Equal(2, heat.Enclosures["tight"].Stores.Count);
    }

    [Fact]
    public void ASaveOfEitherMachineLeavesTheOtherOut_AndALoadHoldsTheStoreAgainFromWhereItIs()
    {
        MachineRuntime Vault() => Placed(Text("vault-night"), "vault", new Vec3(0, 0, 0));
        MachineRuntime Bank() => Placed(Text("found-bank"), "bank", new Vec3(0, 0, 0));   // the cells at (0, 0.25, 0): inside
        var (vault, bank) = (Vault(), Bank());
        var zones = new WorldZones();
        List<(string, MachineRuntime)> Pair(MachineRuntime v, MachineRuntime b) => [("vault", v), ("bank", b)];
        Vec3 At(MachineRuntime v, MachineRuntime b, string label, string id) => (label == "vault" ? v : b).Def.Part(id)!.At;
        zones.Update(Pair(vault, bank), (l, id) => At(vault, bank, l, id));
        for (int i = 0; i < 600; i++) { vault.Step(1); bank.Step(1); }

        static IEnumerable<string> Paths(SList state) => state.Items.Skip(1).OfType<SList>().Select(l => ((SSymbol)l.Items[0]).Name);
        var vs = RuntimeState.Capture(vault);
        var bs = RuntimeState.Capture(bank);
        Assert.DoesNotContain(Paths(vs), p => p.Contains("/_stores/1/"));    // the vault's save has its own rock and not the cells (held stores are not its state)
        Assert.Contains(Paths(bs), p => p == "runtime/_heatStores/cells/_enthalpy");   // the cells are saved by their own machine

        // loaded into a fresh pair: everything finds its place, and the cells are held again once the zones are worked out. The one
        // thing left over: the bank's save walks from its cells into the room they were in (runtime/_heatStores/cells/Zone/...), a copy
        // of the vault's state that a load ignores, as the cells are in their own air until the zones are worked out again
        var (vault2, bank2) = (Vault(), Bank());
        Assert.Empty(RuntimeState.Restore(vault2, vs));
        Assert.All(RuntimeState.Restore(bank2, bs), p => Assert.StartsWith("runtime/_heatStores/cells/Zone/", p));
        Assert.Equal(-63, bank2.Outside.Temperature);   // the vault's air was not laid on the bank's own
        var zones2 = new WorldZones();
        Assert.Single(zones2.Update(Pair(vault2, bank2), (l, id) => At(vault2, bank2, l, id)));
        Assert.Same(vault2.Enclosures["vault"], bank2.HeatStores["cells"].Zone);
        // and goes on as the original does
        for (int i = 0; i < 600; i++) { vault.Step(1); bank.Step(1); vault2.Step(1); bank2.Step(1); }
        Assert.Equal(bank.HeatStores["cells"].Temperature, bank2.HeatStores["cells"].Temperature, 6);
        Assert.Equal(vault.HeatStores["rock"].Temperature, vault2.HeatStores["rock"].Temperature, 6);
    }
}
