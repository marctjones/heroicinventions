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
    private WindView? _windView;   // the wind's streaks over a map with a wind field, and the rover's readout (WindView.cs)

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
        if (terrain.Wind is not null)
        {
            _windView = new WindView
            {
                Name = "Wind", Ground = terrain, Running = () => _running,
                Focus = () => RoverIsPlayer ? _rover!.Chassis.GlobalPosition : _orbit.Pivot,
                Clock = () => _views.Count > 0 ? (_views[0].Runtime.Sun.Time, _views[0].Runtime.Sun.Sols * _views[0].Runtime.Sun.SolLength) : (12, 0),
            };
            AddChild(_windView);
        }
        // the floor goes down under the lowest ground, out of sight and out of the way
        if (_floor is not null) _floor.Position = new Vector3(0, (float)terrain.Heights.Min() - 3f, 0);
    }

    private void ClearGround()
    {
        _groundSim = null;
        _terrainView?.QueueFree();
        _terrainView = null;
        _windView?.GetParent()?.RemoveChild(_windView);   // detach first: a world loaded this frame must not be auto-named (the QueueFree trap)
        _windView?.QueueFree();
        _windView = null;
        if (_floor is not null) _floor.Position = new Vector3(0, -1f, 0);
    }

    /// <summary>The ground's readings, for the world's trace.</summary>
    private IReadOnlyDictionary<string, Func<double>> GroundFields =>
        _groundSim?.FieldGetters ?? new Dictionary<string, Func<double>>();
}
