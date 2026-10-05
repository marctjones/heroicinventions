using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// A world's ground (issue #37): when a world names a map, the map is read
/// from res://maps, its machines are stood on it, their off-scene channels
/// pour onto it, its water is stepped after them, and it is drawn and made
/// solid (TerrainView). The flat stone floor sinks out of the way beneath it.
/// </summary>
public partial class Main
{
    private const string MapsDir = "res://maps";
    private WorldGround? _groundSim;
    private TerrainView? _terrainView;

    /// <summary>Reads and shows the world's map, if it names one; before its machines are placed on it.</summary>
    private void LoadGround(WorldDef world)
    {
        ClearGround();
        if (world.Map is not { } name) return;
        string path = $"{MapsDir}/{name}.map";
        if (!Godot.FileAccess.FileExists(path)) { GD.PushError($"world {world.Name}: no map file {path}"); return; }
        var terrain = Terrain.Parse(Godot.FileAccess.GetFileAsString(path), path);
        _groundSim = new WorldGround(terrain);
        _terrainView = new TerrainView { Name = "Terrain" };
        AddChild(_terrainView);
        _terrainView.Show(terrain, _groundSim.Water, _materials);   // the materials: for the boulders slides leave (#88)
        // the floor goes down under the lowest ground, out of sight and out of the way
        if (_floor is not null) _floor.Position = new Vector3(0, (float)terrain.Heights.Min() - 3f, 0);
    }

    private void ClearGround()
    {
        _groundSim = null;
        _terrainView?.QueueFree();
        _terrainView = null;
        if (_floor is not null) _floor.Position = new Vector3(0, -1f, 0);
    }

    /// <summary>The ground's readings, for the world's trace.</summary>
    private IReadOnlyDictionary<string, Func<double>> GroundFields =>
        _groundSim?.FieldGetters ?? new Dictionary<string, Func<double>>();
}
