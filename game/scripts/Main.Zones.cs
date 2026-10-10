using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Enclosures that hold other machines' heat stores (issue #211, <see cref="WorldZones"/>): the found bank's cells pushed in
/// their crate into a vault of another machine are in that vault's air, and out again in their own. Worked out from where the
/// stores are, once a physics tick before the world steps or a sleep runs a slice (the bodies do not move while it sleeps), so
/// a crate pushed in or out, or a save loaded with it inside, is held or let go at the next tick. Changes go to the log.
/// </summary>
public partial class Main
{
    private readonly WorldZones _zones = new();

    private void UpdateZones()
    {
        if (_views.Count < 2) return;
        var machines = _views.Where(IsInstanceValid).Select(v => (Label: (string)v.Name, v.Runtime)).ToList();
        // a machine rebuilt (a part added in build mode) lets its zones go and takes them back a tick later: a "left" is held one
        // tick and dropped with the "joined" that undoes it, so the log keeps only real moves
        var lines = _zones.Update(machines, (label, id) => _byName[label].StorePoint(id)).ToList();
        var joins = lines.Where(l => l.Contains(" joined ")).Select(Pair).ToHashSet();
        foreach (var left in _zonesLeftPending.Where(l => !joins.Contains(Pair(l)))) GD.Print($"[zones] {left}");
        var undone = _zonesLeftPending.Select(Pair).ToHashSet();
        _zonesLeftPending = lines.Where(l => l.Contains(" left ")).ToList();
        foreach (var line in lines.Where(l => l.Contains(" joined ") ? !undone.Contains(Pair(l)) : !l.Contains(" left ")))
            GD.Print($"[zones] {line}");

        static string Pair(string line) => line[..(line.IndexOf(" at ") is var i and >= 0 ? i : line.Length)].Replace(" joined ", " | ").Replace(" left ", " | ");
    }

    private List<string> _zonesLeftPending = [];

    private void ClearZones() { _zones.Release(); _zonesLeftPending.Clear(); }
}
