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
///
/// A map that asks to settle (#:settle) lets what cannot stand collapse: all at
/// once on the first tick, or, with a rate (issue #61), played out at that many
/// relaxation passes a second so the collapse is watched (map.settling is 1
/// until the ground stands, map.settle-passes counts the passes). A map with a
/// wind field (#:wind) gives every windmill that takes its wind from the map
/// (#:wind-from-map) the wind at its own place and time of day.
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
        _getters["map.bed-moved"] = () => Water.BedMoved;      // m³ of sand the water has shifted (#53)
        _getters["map.bed-lost"] = () => Water.BedLost;        // m³ of it carried off the map
        _getters["map.settled"] = () => Settled;               // faces that failed as the map settled (#54)
        _getters["map.settling"] = () => ground.SettleOnLoad && !_settleDone ? 1 : 0;   // the ground is still coming down (#61)
        _getters["map.settle-passes"] = () => SettlePasses;    // relaxation passes it has taken so far
        _getters["map.spilled"] = () => Spilled;               // m³ tanks spilled or leaked onto it (#90)
        _getters["map.drained"] = () => Water.Drained;         // m³ drains took off it into tanks (#90)
        _getters["map.collapsed"] = () => Ground.Collapsed;       // m³ failed faces of rocky soil lost (#88)
        _getters["map.boulders"] = () => Ground.Boulders.Count;   // boulders the slides left
        _getters["map.boulder-volume"] = () => Ground.BoulderVolume;
        _getters["map.ground-volume"] = () => Ground.Heights.Sum() * Ground.Cell * Ground.Cell;   // m³ of ground above 0 (below, less)
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
        foreach (var part in machine.Def.Parts.Where(p => p.Kind == "windmill" && p.Props.GetValueOrDefault("wind-from-map") is SBool { Value: true }))
            if (Ground.Wind is not null && machine.Windmills.TryGetValue(part.Id, out var mill))
            {
                _fieldMills.Add((machine, mill, part.At));
                mill.Wind = WindAt(machine, part.At);
            }
        // tanks spill onto the ground under them, holes with nothing to catch them pour there, and drains take the
        // water standing over them into their tanks (#90): water crosses between the machine and the map, all of it counted
        foreach (var d in machine.Drains.Values) d.Attach(Water);
        foreach (var (id, tank) in machine.Tanks)
            if (machine.Def.Part(id) is { } part) tank.Spill = m3 => PourOnto(part.At.X, part.At.Z, m3);
        foreach (var (id, leak) in machine.Leaks)
            if (leak.Catch is null && machine.Def.Part(id) is { } hole)
            {
                var l = leak;
                // the jet leaves level along +x at √(2 g h) and falls to the ground under the hole: it lands 2 √(h · drop) out
                leak.Pour = m3 => PourOnto(hole.At.X + 2 * Math.Sqrt(l.Head * Math.Max(0, hole.At.Y - Ground.HeightAt(hole.At.X, hole.At.Z))), hole.At.Z, m3);
            }
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

    private readonly List<(MachineRuntime Machine, Mechanics.Windmill Mill, Vec3 At)> _fieldMills = [];

    /// <summary>The map's wind at a point, at the machine's solar hour and seconds into its run.</summary>
    private double WindAt(MachineRuntime machine, Vec3 at) =>
        Ground.Wind!.SpeedAt(at.X, at.Z, machine.Sun.Time, machine.Sun.Sols * machine.Sun.SolLength);

    /// <summary>Water a tank lets go of onto the ground at a world point; off the map, it runs away (#90).</summary>
    private void PourOnto(double x, double z, double m3)
    {
        Spilled += m3;
        if (!Water.AddWater(x, z, m3)) Water.Leak(m3);
    }
    /// <summary>m³ the machines' tanks have spilled or leaked onto the ground, all told (#90).</summary>
    public double Spilled { get; private set; }

    public void Step(double dt)
    {
        // a map that asks to settle lets its too-steep ground go, onto whatever stands below (#54): on the first tick,
        // or (#61) a few passes a tick at the map's rate, so the collapse is watched
        if (Ground.SettleOnLoad && !_settleDone)
        {
            if (Ground.SettleRate <= 0)
            {
                _settleDone = true;
                (int failures, SettlePasses) = Ground.Settle(Water.Gravity);
                Settled = failures;
            }
            else
            {
                _passDebt += Ground.SettleRate * dt;
                int passes = (int)_passDebt;
                _passDebt -= passes;
                if (passes > 0)
                {
                    var (failures, ran, stood) = Ground.SettleStep(passes, Water.Gravity);
                    Settled += failures;
                    SettlePasses += ran;
                    if (stood) _settleDone = true;
                }
            }
        }
        foreach (var (machine, mill, at) in _fieldMills) mill.Wind = WindAt(machine, at);
        Water.Step(dt);
        TraceBoulders();
    }

    /// <summary>
    /// Each boulder's readings, for the trace (#88): where it is (x y z, m), how fast it goes (speed, m/s), and how
    /// steep the ground is under it (slope, degrees). The game writes their poses back every tick.
    /// </summary>
    public void TraceBoulders()
    {
        for (; _boulderFields < Ground.Boulders.Count; _boulderFields++)
        {
            var b = Ground.Boulders[_boulderFields];
            _getters[$"{b.Id}.x"] = () => b.X;
            _getters[$"{b.Id}.y"] = () => b.Y;
            _getters[$"{b.Id}.z"] = () => b.Z;
            _getters[$"{b.Id}.speed"] = () => b.Speed;
            _getters[$"{b.Id}.size"] = () => b.Size;
            _getters[$"{b.Id}.slope"] = () => Ground.SlopeAt(b.X, b.Z);
        }
    }
    [NonSerialized] private int _boulderFields;

    /// <summary>After a save's boulders replace the ground's: their readings start again.</summary>
    public void RetraceBoulders()
    {
        foreach (var k in _getters.Keys.Where(k => k.StartsWith("boulder-")).ToList()) _getters.Remove(k);
        _boulderFields = 0;
        TraceBoulders();
    }
    private bool _settleDone;
    private double _passDebt;
    /// <summary>Relaxation passes settling has taken.</summary>
    public int SettlePasses { get; private set; }
    /// <summary>How many faces failed when the map settled (0 if it didn't ask to, or nothing was too steep).</summary>
    public int Settled { get; private set; }
}
