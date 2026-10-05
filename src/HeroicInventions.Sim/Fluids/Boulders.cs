using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// A boulder a slide has left (issue #88): a cube of rock lying loose on the ground, a rigid body in the game (it
/// rolls, slides, can be pushed and built with) whose pose the game writes back here every tick, so the world's trace
/// and its save can read it. Made by <see cref="Terrain.Relax"/> when a face of a soil with rock in it fails.
/// </summary>
public sealed class Boulder
{
    public required string Id { get; init; }
    public required string Material { get; init; }
    /// <summary>m, a side of the cube.</summary>
    public required double Size { get; init; }
    public double Volume => Size * Size * Size;
    /// <summary>Its centre (m) and its turn (a unit quaternion), as the game last saw them.</summary>
    public double X, Y, Z, Qx, Qy, Qz, Qw = 1;
    /// <summary>Its velocity (m/s) and spin (rad/s).</summary>
    public double Vx, Vy, Vz, Wx, Wy, Wz;
    public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy + Vz * Vz);
}

/// <summary>
/// Boulders from terrain collapse (issue #88). A soil may carry rock (<see cref="SoilSpec.Boulders"/>): when a cut
/// face of it fails, that fraction of the volume the failed cells lose comes down as cubes of the rock instead of as
/// loose soil. The count is the whole number of cubes the fraction holds, floor(f·V / s³); their volume comes out of
/// the debris, every cell that gained giving up the same share of what it gained, so the ground keeps exactly
/// V − n·s³ of what came down and the boulders hold the rest. Each boulder is laid on the debris where it lies
/// thickest, resting on the slope there, no nearer another than 1.5 sides; where there is no room left, on top.
/// </summary>
public sealed partial class Terrain
{
    [NonSerialized] private readonly List<Boulder> _boulders = [];
    /// <summary>The boulders slides have left, in the order they came down.</summary>
    public IReadOnlyList<Boulder> Boulders => _boulders;

    /// <summary>m³ the failed faces of rocky soils have lost, all told (the V of a collapse).</summary>
    public double Collapsed { get; private set; }
    /// <summary>m³ of it that came down as boulders.</summary>
    public double BoulderVolume { get; private set; }
    private int _boulderCount;

    /// <summary>
    /// Takes the boulders' share out of the debris of a collapse so far (the ground as it stood <paramref name="before"/>
    /// it, the cells that have failed) and lays them on it: as many more as the volume come down owes beyond those
    /// <paramref name="made"/> already, by soil. True if any were made.
    /// </summary>
    private bool TakeBoulders(double[] before, HashSet<int> failed, Dictionary<int, int> made, List<BoulderSpec> toLay)
    {
        double area = Cell * Cell;
        var owed = new List<(BoulderSpec Spec, int Count)>();
        foreach (var group in failed.GroupBy(c => Soil[c]))
        {
            if (Soils[group.Key].Boulders is not { Fraction: > 0 } spec) continue;
            double v = group.Sum(c => Math.Max(0, before[c] - Heights[c])) * area;
            int n = (int)Math.Floor(spec.Fraction * v / (spec.Size * spec.Size * spec.Size) + 1e-9) - made.GetValueOrDefault(group.Key);
            if (n <= 0) continue;
            owed.Add((spec, n));
            made[group.Key] = made.GetValueOrDefault(group.Key) + n;
        }
        if (owed.Count == 0) return false;
        // the debris: every cell that gained, by how much
        var gains = new List<(int Cell, double Gain)>();
        for (int c = 0; c < Count; c++)
            if (Heights[c] - before[c] > 1e-9) gains.Add((c, Heights[c] - before[c]));
        double debris = gains.Sum(g => g.Gain) * area;
        double rock = owed.Sum(m => m.Count * m.Spec.Size * m.Spec.Size * m.Spec.Size);
        if (rock > debris) return false;   // cannot happen with a fraction of at most 1: the debris is at least what the failed cells lost
        foreach (var (c, gain) in gains) Heights[c] -= gain / debris * rock;
        BoulderVolume += rock;
        Version++;
        foreach (var (spec, n) in owed)
            for (int k = 0; k < n; k++) toLay.Add(spec);
        return true;
    }

    /// <summary>Lays the boulders a collapse made where its debris (what the ground gained since <paramref name="before"/>) lies thickest, once the ground has settled.</summary>
    private void LayBoulders(double[] before, List<BoulderSpec> toLay)
    {
        var spots = Enumerable.Range(0, Count).Where(c => Heights[c] - before[c] > 1e-9)
            .OrderByDescending(c => Heights[c] - before[c]).ThenBy(c => c).ToList();
        if (spots.Count == 0) spots = Enumerable.Range(0, Count).Where(c => Math.Abs(Heights[c] - before[c]) > 1e-9).ToList();
        if (spots.Count == 0) return;
        foreach (var spec in toLay) Lay(spec, spots);
    }

    private void Lay(BoulderSpec spec, List<int> spots)
    {
        double s = spec.Size;
        // two sides and a cell in from the outermost cell centres, where the ground's collision ends (and a closed edge
        // shapes the debris), and 1.5 sides from any other boulder
        double margin = 2 * s + Cell;
        bool Free(double x, double z) => x >= CellX(0) + margin && x <= CellX(Nx - 1) - margin && z >= CellZ(0) + margin && z <= CellZ(Nz - 1) - margin
            && _boulders.All(b => (b.X - x) * (b.X - x) + (b.Z - z) * (b.Z - z) >= 2.25 * Math.Max(s, b.Size) * Math.Max(s, b.Size));
        int spot = spots.FirstOrDefault(c => Free(CellX(c % Nx), CellZ(c / Nx)), -1);
        double x, z, lift = 0;
        if (spot >= 0) (x, z) = (CellX(spot % Nx), CellZ(spot / Nx));
        else
        {
            // no room on the debris: on top of the pile at its thickest
            (x, z) = (CellX(spots[0] % Nx), CellZ(spots[0] / Nx));
            lift = _boulders.Where(b => Math.Abs(b.X - x) < s && Math.Abs(b.Z - z) < s).Select(b => b.Y + b.Size / 2).DefaultIfEmpty(0).Max();
        }
        var (nx, ny, nz) = NormalAt(x, z);
        double y = HeightAt(x, z);
        var b = new Boulder { Id = $"boulder-{++_boulderCount}", Material = spec.Material, Size = s };
        if (lift > 0)
        {
            (b.X, b.Y, b.Z) = (x, Math.Max(lift, y) + s / 2 + 0.01, z);
        }
        else
        {
            // resting on the slope: its bottom face on the ground, its centre half a side out along the ground's normal
            (b.X, b.Y, b.Z) = (x + nx * (s / 2 + 0.01), y + ny * (s / 2 + 0.01), z + nz * (s / 2 + 0.01));
            // the turn that takes up (0 1 0) to the normal: about up × n by acos(n·up)
            double ax = nz, az = -nx, sinA = Math.Sqrt(ax * ax + az * az);
            if (sinA > 1e-12)
            {
                double half = Math.Acos(Math.Clamp(ny, -1, 1)) / 2;
                (b.Qx, b.Qy, b.Qz, b.Qw) = (ax / sinA * Math.Sin(half), 0, az / sinA * Math.Sin(half), Math.Cos(half));
            }
        }
        _boulders.Add(b);
    }

    /// <summary>The ground's upward unit normal at a point, from its slope there.</summary>
    public (double X, double Y, double Z) NormalAt(double x, double z)
    {
        double h = Cell / 2;
        double gx = (HeightAt(x + h, z) - HeightAt(x - h, z)) / (2 * h);
        double gz = (HeightAt(x, z + h) - HeightAt(x, z - h)) / (2 * h);
        double len = Math.Sqrt(gx * gx + gz * gz + 1);
        return (-gx / len, 1 / len, -gz / len);
    }

    /// <summary>The ground's slope at a point, in degrees from level.</summary>
    public double SlopeAt(double x, double z) => Math.Acos(NormalAt(x, z).Y) * 180 / Math.PI;

    /// <summary>The boulders as a save holds them: <c>(boulders (boulder ID MATERIAL SIZE (at x y z) (turn x y z w) (v x y z) (spin x y z)) …)</c>.</summary>
    public SList SaveBoulders()
    {
        static SList L(string head, params double[] v) => new([new SSymbol(head), .. v.Select(x => (SExpr)new SNumber(x))]);
        return new SList([new SSymbol("boulders"), .. _boulders.Select(b => (SExpr)new SList([
            new SSymbol("boulder"), new SSymbol(b.Id), new SSymbol(b.Material), new SNumber(b.Size),
            L("at", b.X, b.Y, b.Z), L("turn", b.Qx, b.Qy, b.Qz, b.Qw), L("v", b.Vx, b.Vy, b.Vz), L("spin", b.Wx, b.Wy, b.Wz)]))]);
    }

    /// <summary>Puts back the boulders a save holds, in place of any there are now.</summary>
    public void LoadBoulders(SList saved)
    {
        _boulders.Clear();
        foreach (var e in saved.Items.Skip(1).OfType<SList>().Where(e => e.Head == "boulder"))
        {
            if (e.Items is not [_, SSymbol id, SSymbol material, SNumber size, ..])
                throw new FormatException("a boulder is (boulder ID MATERIAL SIZE (at x y z) (turn x y z w) (v x y z) (spin x y z))");
            double[] F(string f, int n) => e.Field(f)?.Items.Skip(1).OfType<SNumber>().Select(x => x.Value).ToArray() is { } a && a.Length == n
                ? a : throw new FormatException($"boulder {id.Name}: ({f} …) needs {n} numbers");
            var b = new Boulder { Id = id.Name, Material = material.Name, Size = size.Value };
            var (at, turn, v, spin) = (F("at", 3), F("turn", 4), F("v", 3), F("spin", 3));
            (b.X, b.Y, b.Z) = (at[0], at[1], at[2]);
            (b.Qx, b.Qy, b.Qz, b.Qw) = (turn[0], turn[1], turn[2], turn[3]);
            (b.Vx, b.Vy, b.Vz) = (v[0], v[1], v[2]);
            (b.Wx, b.Wy, b.Wz) = (spin[0], spin[1], spin[2]);
            _boulders.Add(b);
            if (int.TryParse(id.Name.AsSpan(id.Name.LastIndexOf('-') + 1), out int k)) _boulderCount = Math.Max(_boulderCount, k);
        }
        BouldersReplaced++;
    }

    /// <summary>Goes up each time a save's boulders replace the ones there were, so a view knows to make them again.</summary>
    public int BouldersReplaced { get; private set; }
}
