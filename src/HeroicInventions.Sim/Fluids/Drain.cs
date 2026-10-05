namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// A drain (issue #90): a grate flush with the ground at (X, Z) over a pipe into a tank, usually a cistern sunk below
/// it. Water standing on the ground over it pours in over the grate's lip as over a weir, the same broad-crested weir
/// as a channel's head (<see cref="Channel.WeirFlow"/>): Q = 1.705·√(g/9.81)·P·h^1.5, P the grate's
/// <see cref="Perimeter"/> and h the depth of the water standing on the ground's cell over it. It takes no more than
/// the cell holds, nor more than the tank has room for: a full cistern backs the drain up and the water stays on the
/// ground. Nothing on a map to drain (a machine standing on the plain floor), it does nothing.
/// </summary>
public sealed class Drain(string name, Tank into, double x, double z, double perimeter)
{
    public string Name { get; } = name;
    public Tank Into { get; } = into;
    public double X { get; } = x;
    public double Z { get; } = z;
    /// <summary>m of lip the water pours over: a square grate's four sides.</summary>
    public double Perimeter { get; } = perimeter;

    public double Flow { get; private set; }       // m³/s it took last step
    public double Drained { get; private set; }    // m³ all told
    public double Depth { get; private set; }      // m of water standing over it, last step

    [NonSerialized] private ShallowWater2D? _water;   // the world's, not this machine's to save

    /// <summary>Puts it on a map's ground water (a world's <see cref="Machines.WorldGround"/> does this).</summary>
    public void Attach(ShallowWater2D water) => _water = water;
    public bool Attached => _water is not null;

    public void Step(double dt)
    {
        Flow = 0;
        if (_water is null || dt <= 0) { Depth = 0; return; }
        Depth = _water.DepthAt(X, Z);
        double want = Channel.WeirFlow(Perimeter, Depth, _water.Gravity) * dt;
        double room = Math.Max(0, Into.Capacity - Into.WaterVolume);
        double taken = _water.TakeWater(X, Z, Math.Min(want, room));
        Into.WaterVolume += taken;
        Drained += taken;
        Flow = taken / dt;
    }
}
