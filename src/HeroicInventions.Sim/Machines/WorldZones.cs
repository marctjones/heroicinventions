using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Machines;

/// <summary>
/// Enclosures that hold other machines' heat stores (issue #211). Inside one machine an enclosure holds the parts whose
/// #:at lies in its box (MachineRuntime.BuildEnclosures). In a world the same rule spans machines: a heat store of one
/// machine whose point lies inside an enclosure of another is held by that enclosure, in its air and its node network,
/// exactly as one of its own: the room's network solves it (radiation to the inner surface, through a bin's lid if it has
/// one) and its own machine no longer steps it in the open air. So the found bank's cells, pushed in their crate into a
/// vault the player built, are warmed by that vault's rock, and pushed out again they cool in the open air as before.
///
/// The rules:
/// <list type="bullet">
/// <item>A store's point is its #:at, carried with its body when it rides one: the found bank's cells ride the crate
/// that their battery bank is #:on (<see cref="CarrierOf"/>). That one point decides, so a store straddling a wall is in
/// the room if its point is (the crate's centre, for the found bank).</item>
/// <item>Only a store in the open air of its own machine can be held: one already in an enclosure of its own machine
/// stays there (so a machine with self-contained rooms is exactly as it was).</item>
/// <item>Of several enclosures round the point, the smallest holds it, as inside one machine.</item>
/// </list>
/// <see cref="Update"/> is called once a tick before the machines step (and before a sleep's slice: bodies do not move
/// while it runs). It is stateless against the world as it now stands: it works out who holds what from the positions,
/// releases what it held before and no longer should (a store carried out, or a machine rebuilt by a live edit), and
/// re-asserts each held store's zone, so nothing a machine does to its own zones (a change of planet) can leave a store
/// stepped twice. Energy: a held store exchanges heat only with its room's node, by the room's backward-Euler solve,
/// which gives the node exactly what it takes from the store; so the two machines together conserve it.
/// </summary>
public sealed class WorldZones
{
    /// <summary>An enclosure of one machine holding a store of another, by their placement labels and part ids.</summary>
    public sealed record Hold(string RoomLabel, string Room, string StoreLabel, string Store)
    {
        public override string ToString() => $"{StoreLabel}.{Store} in {RoomLabel}.{Room}";
    }

    private sealed record Held(Hold Hold, Enclosure Room, HeatStore Store, MachineRuntime Home);
    private List<Held> _held = [];

    /// <summary>What is held now.</summary>
    public IReadOnlyList<Hold> Holds => _held.Select(h => h.Hold).ToList();

    /// <summary>
    /// The body a heat store rides, or null: a battery bank #:in the store and #:on a body (#209: the found bank's cells
    /// are in the crate the bank is built into).
    /// </summary>
    public static string? CarrierOf(MachineDef def, string store) =>
        def.Parts.FirstOrDefault(p => p.Kind == "battery-bank" && p.Symbol("in", "") == store
                                      && p.Symbol("on", "") is { Length: > 0 } on && def.Part(on) is not null)?.Symbol("on", "");

    /// <summary>Is the point inside the enclosure's box: its #:at the middle of its floor, sizes along the axes (as BuildEnclosures).</summary>
    public static bool Inside(Vec3 p, PartSpec box) =>
        Math.Abs(p.X - box.At.X) <= box.Number("size-x") / 2 && Math.Abs(p.Z - box.At.Z) <= box.Number("size-z") / 2
        && p.Y >= box.At.Y - 1e-9 && p.Y <= box.At.Y + box.Number("size-y");

    /// <summary>
    /// Works out who holds what now and makes it so. <paramref name="pointOf"/> gives a store's point in the world (label,
    /// store id); a placed machine's parts are already in world coordinates, so a store on no body is at its #:at.
    /// Returns what changed, as lines for the log ("joined" / "left").
    /// </summary>
    public IReadOnlyList<string> Update(IReadOnlyList<(string Label, MachineRuntime Runtime)> machines, Func<string, string, Vec3> pointOf)
    {
        var want = new List<Held>();
        foreach (var (label, rt) in machines)
            foreach (var (id, store) in rt.HeatStores)
            {
                if (rt.ZoneOf(id) is Enclosure) continue;   // held by its own machine's room: that stands
                Vec3? p = null;
                Held? best = null;
                foreach (var (otherLabel, other) in machines)
                {
                    if (ReferenceEquals(other, rt)) continue;
                    foreach (var (roomId, room) in other.Enclosures)
                    {
                        if (other.Def.Part(roomId) is not { } box) continue;
                        p ??= pointOf(label, id);
                        if (Inside(p.Value, box) && (best is null || room.Volume < best.Room.Volume))
                            best = new Held(new Hold(otherLabel, roomId, label, id), room, store, rt);
                    }
                }
                if (best is not null) want.Add(best);
            }

        var log = new List<string>();
        foreach (var h in _held)
            if (!want.Any(w => ReferenceEquals(w.Room, h.Room) && ReferenceEquals(w.Store, h.Store)))
            {
                h.Room.RemoveStore(h.Store);
                if (ReferenceEquals(h.Store.Zone, h.Room)) h.Store.Zone = h.Home.ZoneOf(h.Hold.Store);   // back in its own machine's air
                log.Add($"{h.Hold.StoreLabel}.{h.Hold.Store} left {h.Hold.RoomLabel}.{h.Hold.Room} at {h.Store.Temperature:0.00} C");
            }
        foreach (var w in want)
        {
            if (!_held.Any(h => ReferenceEquals(h.Room, w.Room) && ReferenceEquals(h.Store, w.Store)))
                log.Add($"{w.Hold.StoreLabel}.{w.Hold.Store} joined {w.Hold.RoomLabel}.{w.Hold.Room} at {w.Store.Temperature:0.00} C (the room at {w.Room.Temperature:0.00} C)");
            w.Room.AddStore(w.Store);   // no-op when it is already there
            w.Store.Zone = w.Room;      // re-asserted every tick: the store's own machine no longer steps it in the open
        }
        _held = want;
        return log;
    }

    /// <summary>Lets every held store go back to its own machine's air (the world is being cleared).</summary>
    public void Release()
    {
        foreach (var h in _held)
        {
            h.Room.RemoveStore(h.Store);
            if (ReferenceEquals(h.Store.Zone, h.Room)) h.Store.Zone = h.Home.ZoneOf(h.Hold.Store);
        }
        _held = [];
    }
}
