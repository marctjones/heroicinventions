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
        foreach (var line in _zones.Update(machines, (label, id) => _byName[label].StorePoint(id)))
            GD.Print($"[zones] {line}");
    }

    private void ClearZones() => _zones.Release();
}
