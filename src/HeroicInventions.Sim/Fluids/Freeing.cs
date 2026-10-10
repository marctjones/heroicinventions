namespace HeroicInventions.Sim.Fluids;

/// <summary>What freeing a buried block in place did (<see cref="Terrain.FreeBlock"/>).</summary>
/// <param name="Soil">m³ of soil that stood over and beside the block, crumbled off and tipped round it.</param>
/// <param name="Block">m³ of the block itself that the ground's surface had stood for.</param>
/// <param name="Spoil">m³ of that soil that landed round it: <paramref name="Soil"/>, to the bit, unless there was no open ground near.</param>
/// <param name="Nodes">Nodes laid at the block's base.</param>
/// <param name="Given">m³ the ground's surface went down by over those nodes: <paramref name="Soil"/> + <paramref name="Block"/>.</param>
public readonly record struct FreedBlock(double Soil, double Block, double Spoil, int Nodes, double Given);

public sealed partial class Terrain
{
    /// <summary>Whether a block may be freed on the map's own cells (<see cref="FreeBlockOnMap"/>): they are as fine as two of a worked
    /// patch's, and no soil of the map leaves boulders (a slide's boulders are not put back if a try is undone).</summary>
    public bool FreesOnMap => Cell <= 2 * WorkedGround.FineCell + 1e-9 && !Soils.Any(s => s.Boulders is { Fraction: > 0 });

    /// <summary>
    /// <see cref="FreeBlock"/> on the map's own cells, where no patch has been worked (a world of fine cells dug by a gang, #54): rock is
    /// never cut (a cell of rock, at its top; rubble a slide left on rock, down to the rock's top), and no spoil is tipped on rock.
    /// </summary>
    public FreedBlock? FreeBlockOnMap(double x, double z, double halfX, double halfZ, double bottom, double top, double gravity, Func<WorkedGround.Raised, bool> inTheWay) =>
        FreeBlock(x, z, halfX, halfZ, bottom, top, gravity,
                  k => IsRock(k) ? Heights[k] : Covering(k) is { } c ? c.RockTop : double.NegativeInfinity, k => !IsRock(k), inTheWay, edge: 0);

    /// <summary>
    /// A buried block (#54) let go where it lies, on this ground's own lattice (its nodes at the cell centres, as its height map is
    /// drawn and made solid). A height map is one surface: it cannot hold soil over a block and soil under it too, so a block left
    /// under it has nothing under it, and one whose box it crosses is shoved out by Jolt (the jump this replaces). So the thin crust
    /// over the block, and the soil beside it within a cell, crumble off it: every node of every cell its footprint
    /// (<paramref name="x"/> ± <paramref name="halfX"/>, <paramref name="z"/> ± <paramref name="halfZ"/>) touches is laid at its base,
    /// <paramref name="bottom"/> (never below <paramref name="floor"/>: rock is not cut), so the triangles over the footprint are flat
    /// there and the block stands on them, the pit's walls rising outside it. What those nodes gave is the block's own volume (where
    /// a node's square overlaps the footprint, from its base up to <paramref name="top"/>) and soil. The soil is tipped as loose spoil
    /// on the ring of nodes just beyond the pit's lip (shared evenly over those <paramref name="canTake"/> allows that are not the
    /// top of a face; a ring further out if none is), and the ground settles by its repose rules with the pit and its lip closed (<see cref="Blocked"/>), so nothing
    /// slides back under or against the block, then or at a later settling: the lip is a cut face, which holds to its soil's
    /// critical height, with no loose soil on it. A node the spoil raised that <paramref name="inTheWay"/> says would reach a body is closed
    /// too and the whole is done again from the ground as it was (as <see cref="WorkedGround.Around"/> does for the backhoe, #72).
    /// Soil is kept exactly (<see cref="FreedBlock.Spoil"/> = <see cref="FreedBlock.Soil"/>). Null if it could not be done in
    /// <paramref name="rounds"/> tries: the ground is then as it was. Nodes within <paramref name="edge"/> of the map's edge are left alone.
    /// </summary>
    public FreedBlock? FreeBlock(double x, double z, double halfX, double halfZ, double bottom, double top, double gravity,
                                 Func<int, double> floor, Func<int, bool> canTake, Func<WorkedGround.Raised, bool> inTheWay, int edge, int rounds = 24)
    {
        int Lo(double v, double o) => (int)Math.Floor((v - o) / Cell - 0.5);
        int Hi(double v, double o) => (int)Math.Ceiling((v - o) / Cell - 0.5);
        int i0 = Math.Max(edge, Lo(x - halfX, X0)), i1 = Math.Min(Nx - 1 - edge, Hi(x + halfX, X0));
        int j0 = Math.Max(edge, Lo(z - halfZ, Z0)), j1 = Math.Min(Nz - 1 - edge, Hi(z + halfZ, Z0));
        if (i1 < i0 || j1 < j0) return null;
        static double Overlap(double a0, double a1, double b0, double b1) => Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));
        double half = Cell / 2, area = Cell * Cell;
        int soilKind = Soil[CellAt(x, z) ?? i0 + j0 * Nx];
        var closed = new bool[Count];
        for (int r = 0; r < rounds; r++)
        {
            var heights = (double[])Heights.Clone();
            var loose = (bool[])Loose.Clone();
            var soils = (int[])Soil.Clone();
            int version = Version;
            double soil = 0, block = 0, given = 0;
            int count = 0;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int k = i + j * Nx;
                    closed[k] = true;   // the block's base: no spoil lands on it, and nothing slides onto it
                    double target = Math.Max(bottom, floor(k)), h = Heights[k];
                    if (h <= target) continue;
                    double inBlock = Overlap(CellX(i) - half, CellX(i) + half, x - halfX, x + halfX)
                                   * Overlap(CellZ(j) - half, CellZ(j) + half, z - halfZ, z + halfZ)
                                   * Math.Max(0, Math.Min(h, top) - Math.Max(target, bottom));
                    given += (h - target) * area;
                    block += inBlock;
                    soil += (h - target) * area - inBlock;
                    Heights[k] = target;
                    count++;
                }
            // the pit's lip, the ring of nodes round it, takes no spoil and nothing slides onto it: it stands as the cut face it is (a
            // face holds to its soil's critical height), so loose spoil heaped on it would only slide into the pit at the ground's next
            // settling (a gang's spit, #54) and bury the block again
            for (int j = j0 - 1; j <= j1 + 1; j++)
                for (int i = i0 - 1; i <= i1 + 1; i++)
                    if (i >= 0 && j >= 0 && i < Nx && j < Nz) closed[i + j * Nx] = true;
            // the spoil, evenly over the open nodes of the first ring beyond the lip that has any: not on rock the map keeps as rock, and
            // not on the top of a face (a trench's end, say): this ground has one loose flag a column, so spoil there would make the
            // whole face loose, and it would slump into the trench and the pit at the next settling
            double spoil = 0;
            for (int ring = 2; soil > 0 && spoil == 0 && ring <= 12; ring++)
            {
                var open = new List<int>();
                for (int j = j0 - ring; j <= j1 + ring; j++)
                    for (int i = i0 - ring; i <= i1 + ring; i++)
                    {
                        if (i > i0 - ring && i < i1 + ring && j > j0 - ring && j < j1 + ring) continue;   // inside the ring
                        if (i < edge || j < edge || i > Nx - 1 - edge || j > Nz - 1 - edge) continue;
                        int k = i + j * Nx;
                        if (!closed[k] && canTake(k) && !FaceTop(i, j, Soils[soilKind].Friction)) open.Add(k);
                    }
                if (open.Count == 0) continue;
                double rise = soil / (open.Count * area);
                foreach (int k in open)
                {
                    Heights[k] += rise;
                    Loose[k] = true;
                    if (IsRock(k)) Soil[k] = soilKind;   // spoil on bedrock is soil (a worked patch keeps the rock's level as its floor)
                }
                spoil = soil;
            }
            Version++;
            if (soil > 0)
            {
                Blocked = closed;
                try { Relax(edge, edge, Nx - 1 - edge, Nz - 1 - edge, gravity, 20000); } finally { Blocked = null; }
            }
            bool clear = true;
            for (int k = 0; k < heights.Length; k++)
                if (Heights[k] > heights[k] + 1e-6 && inTheWay(new WorkedGround.Raised(k, CellX(k % Nx), CellZ(k / Nx), heights[k], Heights[k])))
                {
                    closed[k] = true;
                    clear = false;
                }
            if (clear) return new FreedBlock(soil, block, spoil, count, given);
            Array.Copy(heights, Heights, heights.Length);
            Array.Copy(loose, Loose, loose.Length);
            Array.Copy(soils, Soil, soils.Length);
            Version = version;
        }
        return null;
    }

    /// <summary>Whether cell (i, j) is the top of a face: a neighbour lies lower than the slope of <paramref name="friction"/> (a loose soil's repose) reaches.</summary>
    private bool FaceTop(int i, int j, double friction)
    {
        double h = Heights[i + j * Nx];
        for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                int ni = i + di, nj = j + dj;
                if ((di | dj) == 0 || ni < 0 || nj < 0 || ni >= Nx || nj >= Nz) continue;
                double run = (di != 0 && dj != 0 ? Math.Sqrt(2) : 1) * Cell;
                if (h - Heights[ni + nj * Nx] > friction * run + 1e-9) return true;
            }
        return false;
    }
}
