using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// A digging gang cutting a trench into the ground (issue #44): from
/// (X0, Z0), <see cref="Length"/> m along +x, <see cref="Width"/> m across,
/// down to <see cref="TargetDepth"/>, a spit at a time across the whole
/// trench, the spoil thrown into a ridge <see cref="SpoilOffset"/> m to the
/// +z side.
///
/// Each cubic metre costs the work to shear it free and lift it out: the
/// soil's shear strength where it is cut, c + γ·z·tan φ (Mohr–Coulomb; a
/// stress in Pa is an energy per volume, J/m³), plus γ·z to raise it the z
/// metres to the surface, z the depth of the middle of the slab cut. Loose
/// soil (spoil, or a fallen wall) has no cohesion. The gang works at
/// <see cref="Power"/> W, so it digs at Power / that. After each spit the
/// ground settles (<see cref="Terrain.Relax"/>); if a wall has failed, the
/// gang stops and climbs out, and the depth it had reached is the depth at
/// which the unshored wall gave way.
/// </summary>
public sealed class Digger(string name, double x0, double z0, double length, double width, double depth, double power, double spit, double spoilOffset)
{
    public string Name { get; } = name;
    public double X0 { get; private set; } = x0;
    public double Z0 { get; private set; } = z0;
    public double StartX { get; } = x0;
    public double StartZ { get; } = z0;
    public double Length { get; } = length;
    public double Width { get; } = width;
    public double TargetDepth { get; } = depth;
    public double Power { get; set; } = power;
    public double Spit { get; } = spit;
    public double SpoilOffset { get; } = spoilOffset;

    public double Dug { get; private set; }            // m³ taken out
    public double Work { get; private set; }           // J done
    public double Depth { get; private set; }          // m the whole trench is down to
    public bool Collapsed { get; private set; }        // a wall fell in
    public double CollapseDepth { get; private set; } = -1;   // m the trench was when it did
    public bool Done { get; private set; }
    public double SpecificWork => Dug > 0 ? Work / Dug : 0;   // J/m³

    private Terrain? _ground;
    private double _gravity = Physics.Gravity;
    private int[] _cells = [], _spoil = [];
    private double[] _surface = [];   // m, the ground's height over each trench cell before digging began
    private int _spitIndex, _next;
    private double _energy;           // J done towards the slab now being cut

    /// <summary>The trench's cells: those whose centres fall inside it (a trench narrower than a cell has none: the ground can't show it).</summary>
    public IReadOnlyList<int> Cells => _cells;

    public void Attach(Terrain ground, double gravity)
    {
        _ground = ground;
        _gravity = gravity;
        var cells = new List<int>();
        var spoil = new List<int>();
        for (int j = 0; j < ground.Nz; j++)
            for (int i = 0; i < ground.Nx; i++)
            {
                double x = ground.CellX(i), z = ground.CellZ(j);
                if (x < X0 || x > X0 + Length || Math.Abs(z - Z0) > Width / 2) continue;
                cells.Add(i + j * ground.Nx);
                spoil.Add(ground.CellAt(x, Z0 + SpoilOffset) ?? -1);
            }
        _cells = [.. cells];
        _spoil = [.. spoil];
        // a gang carried over a live edit resumes where it was: the surface it started from is the spits it has dug above the ground now
        _surface = _cells.Select((c, k) => ground.Heights[c]
            + (Dug > 0 ? Math.Min((_spitIndex + (k < _next ? 1 : 0)) * Spit, TargetDepth) : 0)).ToArray();
    }

    /// <summary>
    /// A person picks where the gang works (#162): it climbs out of what it had cut and starts afresh at (x, z), the
    /// trench's near end and centre line. What it had dug stays dug (the ground keeps it, and <see cref="Dug"/>
    /// and <see cref="Work"/> are totals); the new trench starts from the ground as it is there.
    /// </summary>
    public void MoveTo(double x, double z)
    {
        X0 = x; Z0 = z;
        SiteHeight = _ground?.HeightAt(x, z);
        _spitIndex = 0; _next = 0; _energy = 0;
        Depth = 0; Done = false; Collapsed = false; CollapseDepth = -1;
        if (_ground is { } g) Attach(g, _gravity);
    }

    /// <summary>The ground's height under the trench's near end when the gang was sent there; null while it works where it was built.</summary>
    public double? SiteHeight { get; private set; }

    public void Step(double dt)
    {
        if (_ground is not { } g || Done || _cells.Length == 0) { if (_cells.Length == 0 && _ground is not null) Done = true; return; }
        _energy += Power * dt;
        double area = g.Cell * g.Cell;
        while (!Done)
        {
            int c = _cells[_next];
            double target = _surface[_next] - Math.Min((_spitIndex + 1) * Spit, TargetDepth);
            double cut = g.Heights[c] - target;
            if (cut > 1e-9)
            {
                var soil = g.SoilOf(c);
                double gamma = soil.Density * _gravity;
                double z = _surface[_next] - (g.Heights[c] - cut / 2);          // the slab's middle, below the ground as it was
                double cohesion = g.Loose[c] ? 0 : soil.Cohesion;
                double cost = cut * area * (cohesion + gamma * z * soil.Friction + gamma * z);
                if (_energy < cost) return;                                    // still at it
                _energy -= cost;
                Work += cost;
                double v = g.Dig(c, cut);
                Dug += v;
                if (_spoil[_next] >= 0) g.Heap(_spoil[_next], v);
            }
            if (++_next < _cells.Length) continue;
            // the spit is done across the trench: let the ground settle
            _next = 0;
            _spitIndex++;
            Depth = Math.Min(_spitIndex * Spit, TargetDepth);
            var (failures, _) = Relax(g);
            if (failures > 0) { Collapsed = true; CollapseDepth = Depth; Done = true; }
            else if (Depth >= TargetDepth - 1e-9) Done = true;
        }
    }

    private (int, int) Relax(Terrain g)
    {
        int margin = (int)Math.Ceiling((SpoilOffset + Width) / g.Cell) + 2;
        int i0 = (int)Math.Floor((X0 - g.X0) / g.Cell) - margin, i1 = (int)Math.Ceiling((X0 + Length - g.X0) / g.Cell) + margin;
        int j0 = (int)Math.Floor((Z0 - g.Z0) / g.Cell) - margin, j1 = (int)Math.Ceiling((Z0 - g.Z0) / g.Cell) + margin;
        return g.Relax(i0, j0, i1, j1, _gravity);
    }
}
