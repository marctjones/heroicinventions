using Godot;
using HeroicInventions.Sim.Game;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Which of the rover's buried crates it has found (owner decision on #240: "Show the rough area."). Until found, the scene's burial tag
/// and build mode's buried marker give no place or depth, only the rough area; after, the exact ones (MachineView.Found.cs, the rule in
/// Sim/Game/CargoFind.cs). Asked each frame from <see cref="MarkersProcess"/>, only once the ground has settled after the slide (the goals'
/// own test, map.settling 0), so a crate that lies on the surface before the rim comes down on it is not found by that.
/// Hooks: <c>SaveWorld</c> (<see cref="FoundForSave"/>), <c>LoadSave</c> (<see cref="FoundRestore"/>); the scripted step "marker found LABEL [BLOCK]"
/// (Main.Markers.cs) marks one found by hand, for the checks.
/// </summary>
public partial class Main
{
    private Vector3? _digSeen;

    private void FoundTick()
    {
        if (_world is null) return;
        bool rover = RoverIsPlayer;
        Vector3? dig = null;
        if (rover)
        {
            var at = _rover!.LastDigAt;
            if (_digSeen is { } seen && at != seen) dig = at;   // a dig new since the last frame (the first frame only remembers where)
            _digSeen = at;
        }
        bool settled = _groundSim is null || !_groundSim.FieldGetters.TryGetValue("map.settling", out var settling) || settling() == 0;
        foreach (var view in CargoViews())
        {
            view.RoughUntilFound = rover;
            if (rover && settled) view.CheckFound(dig, o => o is Node n && IsInstanceValid(_rover) && (n == _rover || _rover!.IsAncestorOf(n)));
        }
    }

    /// <summary>The views of the world's cargo (the placed crates, not the player's machines).</summary>
    private IEnumerable<MachineView> CargoViews()
    {
        if (_world is null) yield break;
        foreach (var p in _world.Placements)
            if (p.Built is null && CargoMachines.Contains(p.Machine) && _byName.TryGetValue(p.Label, out var view) && IsInstanceValid(view)) yield return view;
    }

    private SList? FoundForSave()
    {
        var found = new FoundCargo();
        foreach (var view in CargoViews())
            foreach (var block in view.FoundBlocks) found.Add(view.Name.ToString(), block);
        return found.ToForm();
    }

    private void FoundRestore(WorldSave save)
    {
        foreach (var (label, block) in FoundCargo.Parse(save.FoundCargo).All)
            if (_byName.TryGetValue(label, out var view) && IsInstanceValid(view)) view.MarkFound(block, "found before the save");
    }

    /// <summary>"marker found LABEL [BLOCK]": marks a crate found by hand (the block "crate" unless named), for the checks.</summary>
    private void MarkFoundByHand(string label, string block)
    {
        if (_byName.TryGetValue(label, out var view) && IsInstanceValid(view)) view.MarkFound(block, "marked by a scripted step");
        else GD.Print($"[found] no machine {label}");
    }
}
