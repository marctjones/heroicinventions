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
        // a machine rebuilt (a part added in build mode) lets its zones go and takes them back a little later: a "left" is held
        // up to ZonesHoldTicks and dropped with the "joined" that undoes it, so the log keeps only real moves
        var lines = _zones.Update(machines, (label, id) => _byName[label].StorePoint(id)).ToList();
        foreach (var line in lines)
        {
            if (line.Contains(" left ")) { _zonesLeftPending.Add((line, 0)); continue; }
            int undone = line.Contains(" joined ") ? _zonesLeftPending.FindIndex(p => Pair(p.Line) == Pair(line)) : -1;
            if (undone >= 0) { _zonesLeftPending.RemoveAt(undone); continue; }   // a rebuild's left and rejoin: no move
            GD.Print($"[zones] {line}");
        }
        for (int i = _zonesLeftPending.Count - 1; i >= 0; i--)   // a left not undone within ZonesHoldTicks was a real move
        {
            var (line, age) = _zonesLeftPending[i];
            if (age + 1 < ZonesHoldTicks) { _zonesLeftPending[i] = (line, age + 1); continue; }
            GD.Print($"[zones] {line}");
            _zonesLeftPending.RemoveAt(i);
        }

        static string Pair(string line) => line[..(line.IndexOf(" at ") is var i and >= 0 ? i : line.Length)].Replace(" joined ", " | ").Replace(" left ", " | ");
    }

    private readonly List<(string Line, int Age)> _zonesLeftPending = [];
    private const int ZonesHoldTicks = 240;   // 2 s at 120 Hz: longer than build mode's rebuild of a machine

    private void ClearZones() { _zones.Release(); _zonesLeftPending.Clear(); }
}
