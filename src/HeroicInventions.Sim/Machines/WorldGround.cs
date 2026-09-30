using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Machines;

/// <summary>
/// A world's ground and the water loose on it (issue #37), joined to the
/// machines standing on it: every channel a machine runs off its own scene
/// (#:to off, pouring onto nothing of its own) pours onto the ground cell
/// under its end, so a sluice drawn on a hillside pond floods the plain
/// below. Attach each machine as it is built, and again when a live edit
/// rebuilds it. Step it after the machines, so what they poured this tick
/// is already on the ledger.
///
/// Readings, for the HUD and the world's trace: map.volume (m³ standing),
/// map.wet-area (m²), map.max-depth (cm), map.poured, map.infiltrated and
/// map.leaked (m³, all told), and map.depth-at (cm) / map.height-at (m) at
/// a probe point (map.probe-x, map.probe-z, settable).
/// </summary>
public sealed class WorldGround
{
    public Terrain Ground { get; }
    public ShallowWater2D Water { get; }
    private readonly Dictionary<string, Func<double>> _getters = [];
    private readonly Dictionary<string, Action<double>> _setters = [];
    private double _probeX, _probeZ;

    public IReadOnlyDictionary<string, Func<double>> FieldGetters => _getters;
    public IReadOnlyDictionary<string, Action<double>> FieldSetters => _setters;

    /// <summary>m³ poured onto the ground by each machine's channels, by channel (label.channel), all told.</summary>
    public IReadOnlyDictionary<string, double> PouredBy => _pouredBy;
    private readonly Dictionary<string, double> _pouredBy = [];

    public WorldGround(Terrain ground)
    {
        Ground = ground;
        Water = new ShallowWater2D(ground);
        (_probeX, _probeZ) = (ground.X0 + ground.Width / 2, ground.Z0 + ground.Depth / 2);
        _getters["map.volume"] = () => Water.Volume;
        _getters["map.wet-area"] = () => Water.WetArea;
        _getters["map.max-depth"] = () => Water.MaxDepth * 100;
        _getters["map.poured"] = () => Water.Poured;
        _getters["map.infiltrated"] = () => Water.Infiltrated;
        _getters["map.leaked"] = () => Water.Leaked;
        _getters["map.probe-x"] = () => _probeX;
        _getters["map.probe-z"] = () => _probeZ;
        _setters["map.probe-x"] = x => _probeX = x;
        _setters["map.probe-z"] = z => _probeZ = z;
        _getters["map.depth-at"] = () => Water.DepthAt(_probeX, _probeZ) * 100;
        _getters["map.height-at"] = () => Ground.HeightAt(_probeX, _probeZ);
    }

    /// <summary>
    /// Joins a machine to the ground: its off-scene channels pour onto the
    /// cell under their ends. Water poured off the map's edge runs away
    /// (counted as leaked). The world's water falls at the first machine's
    /// planet's gravity.
    /// </summary>
    public void Attach(string label, MachineRuntime machine)
    {
        if (_attached++ == 0) Water.Gravity = machine.Outside.Gravity;
        foreach (var d in machine.Diggers.Values) d.Attach(Ground, Water.Gravity);   // digging gangs dig this ground (#44)
        foreach (var spec in machine.Def.Channels)
        {
            if (spec.To is not null || spec.Onto is not null || spec.End is not { } end) continue;
            string key = $"{label}.{spec.Id}";
            _pouredBy.TryAdd(key, 0);
            machine.Channels[spec.Id].Pour = m3 =>
            {
                _pouredBy[key] += m3;
                if (!Water.AddWater(end.X, end.Z, m3)) Water.Leak(m3);
            };
        }
    }
    private int _attached;

    public void Step(double dt) => Water.Step(dt);
}
