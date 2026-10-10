namespace HeroicInventions.Sim.Fluids;

/// <summary>What freeing a buried block in place did (<see cref="Terrain.FreeBlock"/>).</summary>
/// <param name="Soil">m³ of soil that stood over and beside the block, crumbled off and tipped round it.</param>
/// <param name="Block">m³ of the block itself that the ground's surface had stood for.</param>
/// <param name="Spoil">m³ of that soil that landed round it: <paramref name="Soil"/>, to the bit, unless there was no open ground near.</param>
/// <param name="Nodes">Nodes laid at the block's base.</param>
/// <param name="Given">m³ the ground's surface went down by over those nodes: <paramref name="Soil"/> + <paramref name="Block"/>.</param>
/// <param name="Crust">m³ of <paramref name="Soil"/> that lay over the block's lid (over its footprint, above its top); the rest stood beside it.</param>
/// <param name="Skin">m³ of <paramref name="Soil"/> put back as an even skin, not loose: the soil beside the block (and the crust, if it found nowhere to fall clear of a body).</param>
/// <param name="SkinRise">m the skin raised the nodes it was spread over.</param>
public readonly record struct FreedBlock(double Soil, double Block, double Spoil, int Nodes, double Given, double Crust, double Skin, double SkinRise);

public sealed partial class Terrain
{
    /// <summary>How many rings of nodes beyond a freed block's pit share its crumbled crust (<see cref="FreeBlock"/>).</summary>
    public const int SpoilRings = 3;
    /// <summary>m round a node where a freed block's crust ran into a body that the crust is then kept from being tipped (<see cref="FreeBlock"/>).</summary>
    public const double SpoilShun = 1.5;
    /// <summary>m: the most the soil beside a freed block that only the grid took raises the ground it is spread over, and the rings it may be spread over (<see cref="FreeBlock"/>).</summary>
    public const double SkinDepth = 0.03;
    public const int SkinRings = 48;

    /// <summary>Whether a block may be freed on the map's own cells (<see cref="FreeBlockOnMap"/>): they are as fine as two of a worked
    /// patch's, and no soil of the map leaves boulders (a slide's boulders are not put back if a try is undone).</summary>
    public bool FreesOnMap => Cell <= 2 * WorkedGround.FineCell + 1e-9 && !Soils.Any(s => s.Boulders is { Fraction: > 0 });

    /// <summary>
    /// <see cref="FreeBlock"/> on the map's own cells, where no patch has been worked (a world of fine cells dug by a gang, #54): rock is
    /// never cut (a cell of rock, at its top; rubble a slide left on rock, down to the rock's top), and no spoil is tipped on rock.
    /// </summary>
    public FreedBlock? FreeBlockOnMap(double x, double z, double halfX, double halfZ, double bottom, double top, double gravity, Func<WorkedGround.Raised, bool> inTheWay,
                                      Func<double, double, bool>? spoilAt = null) =>
        FreeBlock(x, z, halfX, halfZ, bottom, top, gravity,
                  k => IsRock(k) ? Heights[k] : Covering(k) is { } c ? c.RockTop : double.NegativeInfinity,
                  k => !IsRock(k) && (spoilAt?.Invoke(CellX(k % Nx), CellZ(k / Nx)) ?? true), inTheWay, edge: 0);

    /// <summary>
    /// A buried block (#54) let go where it lies, on this ground's own lattice (its nodes at the cell centres, as its height map is
    /// drawn and made solid). A height map is one surface: it cannot hold soil over a block and soil under it too, so a block left
    /// under it has nothing under it, and one whose box it crosses is shoved out by Jolt (the jump this replaces). So every node of
    /// every cell the block's footprint (<paramref name="x"/> ± <paramref name="halfX"/>, <paramref name="z"/> ± <paramref name="halfZ"/>)
    /// touches is laid at its base, <paramref name="bottom"/> (never below <paramref name="floor"/>: rock is not cut): the triangles
    /// over the footprint are flat there and the block stands on them, the pit's walls rising outside it. What those nodes gave is
    /// three things:
    /// <list type="bullet">
    /// <item>the block's own volume (where a node's square overlaps the footprint, from its base up to <paramref name="top"/>), which
    /// the surface stood for;</item>
    /// <item>the crust (over the footprint, above its top): it crumbles off and is tipped as loose spoil over <see cref="SpoilRings"/>
    /// rings of nodes beyond the pit's lip (those <paramref name="canTake"/> allows that are not the top of a face), and the ground
    /// settles by its repose rules with the pit and its lip closed (<see cref="Blocked"/>);</item>
    /// <item>the soil beside the block within a cell, which only the grid takes (a real pit's wall stands at the block's side; the
    /// grid's stands a cell out, for its triangles to clear the box). It is no crumbled soil, and on rubble at its repose loose
    /// soil runs on down to whatever stands below, so it is put back as a skin, not loosened: an even rise of at most
    /// <see cref="SkinDepth"/> over the nearest nodes above the block's top that <paramref name="canTake"/> allows and that can rise
    /// that much with every slope down from them still standing (<see cref="Slack"/>): it leaves every slope inside it as it was,
    /// and no later settling moves it.</item>
    /// </list>
    /// If the ground then rose at a node <paramref name="inTheWay"/> says reaches a body (a rover standing by: soil the backhoe left
    /// held against its wheels, #72, is let go by a settling that does not know them), that node is closed too, the crust is tipped
    /// nowhere within <see cref="SpoilShun"/> of there, and all is done again from the ground as it was; after half the
    /// <paramref name="rounds"/>, the crust goes into the skin. Soil is kept exactly (<see cref="FreedBlock.Spoil"/> =
    /// <see cref="FreedBlock.Soil"/>). Null if it could not be done: the ground is then as it was. Nodes within
    /// <paramref name="edge"/> of the map's edge are left alone.
    /// </summary>
    public FreedBlock? FreeBlock(double x, double z, double halfX, double halfZ, double bottom, double top, double gravity,
                                 Func<int, double> floor, Func<int, bool> canTake, Func<WorkedGround.Raised, bool> inTheWay, int edge, int rounds = 16)
    {
        int Lo(double v, double o) => (int)Math.Floor((v - o) / Cell - 0.5);
        int Hi(double v, double o) => (int)Math.Ceiling((v - o) / Cell - 0.5);
        int i0 = Math.Max(edge, Lo(x - halfX, X0)), i1 = Math.Min(Nx - 1 - edge, Hi(x + halfX, X0));
        int j0 = Math.Max(edge, Lo(z - halfZ, Z0)), j1 = Math.Min(Nz - 1 - edge, Hi(z + halfZ, Z0));
        if (i1 < i0 || j1 < j0) return null;
        static double Overlap(double a0, double a1, double b0, double b1) => Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));
        double half = Cell / 2, area = Cell * Cell, friction = Soils[Soil[CellAt(x, z) ?? i0 + j0 * Nx]].Friction;
        int soilKind = Soil[CellAt(x, z) ?? i0 + j0 * Nx];
        // the pit and its lip (the ring of nodes round it, the cut face): nothing lands or slides onto them while the crust is tipped
        var pit = new bool[Count];
        for (int j = Math.Max(0, j0 - 1); j <= Math.Min(Nz - 1, j1 + 1); j++)
            for (int i = Math.Max(0, i0 - 1); i <= Math.Min(Nx - 1, i1 + 1); i++) pit[i + j * Nx] = true;
        // the nodes of ring n round the pit (n = 1 is the lip), in raster order
        IEnumerable<(int I, int J)> Ring(int n)
        {
            for (int j = j0 - n; j <= j1 + n; j++)
                for (int i = i0 - n; i <= i1 + n; i++)
                {
                    if (i > i0 - n && i < i1 + n && j > j0 - n && j < j1 + n) continue;
                    if (i < edge || j < edge || i > Nx - 1 - edge || j > Nz - 1 - edge) continue;
                    yield return (i, j);
                }
        }
        var shunned = new bool[Count];   // nodes the crust may not be tipped on: it ran from them to a body
        int reach = (int)Math.Ceiling(SpoilShun / Cell);
        for (int r = 0; r < rounds; r++)
        {
            var heights = (double[])Heights.Clone();
            var loose = (bool[])Loose.Clone();
            var soils = (int[])Soil.Clone();
            int version = Version;
            double soil = 0, block = 0, given = 0, crust = 0;
            int count = 0;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int k = i + j * Nx;
                    double target = Math.Max(bottom, floor(k)), h = Heights[k];
                    if (h <= target) continue;
                    double over = Overlap(CellX(i) - half, CellX(i) + half, x - halfX, x + halfX) * Overlap(CellZ(j) - half, CellZ(j) + half, z - halfZ, z + halfZ);
                    double inBlock = over * Math.Max(0, Math.Min(h, top) - Math.Max(target, bottom));
                    crust += over * Math.Max(0, h - Math.Max(top, target));
                    given += (h - target) * area;
                    block += inBlock;
                    soil += (h - target) * area - inBlock;
                    Heights[k] = target;
                    count++;
                }
            bool crustLoose = r < rounds / 2;
            double skin = crustLoose ? soil - crust : soil;
            // the crust, loose, evenly over the open nodes of SpoilRings rings from the first beyond the lip that has any: not on rock
            // the map keeps as rock, not where it once ran to a body, and not on the top of a face (a trench's end, say): this ground
            // has one loose flag a column, so spoil there would make the whole face loose, and it would slump into the trench and the pit
            double spoil = 0;
            if (crustLoose && crust > 0)
            {
                var open = new List<int>();
                for (int ring = 2, last = 12; ring <= last; ring++)
                {
                    foreach (var (i, j) in Ring(ring))
                        if (!shunned[i + j * Nx] && !pit[i + j * Nx] && canTake(i + j * Nx) && !FaceTop(i, j, friction)) open.Add(i + j * Nx);
                    if (open.Count > 0 && last == 12) last = ring + SpoilRings - 1;
                }
                if (open.Count == 0) { crustLoose = false; skin = soil; }
                else
                {
                    double rise = crust / (open.Count * area);
                    foreach (int k in open)
                    {
                        Heights[k] += rise;
                        Loose[k] = true;
                        if (IsRock(k)) Soil[k] = soilKind;   // spoil on bedrock is soil (a worked patch keeps the rock's level as its floor)
                    }
                    spoil += crust;
                }
            }
            // the grid's share, as an even skin over the nearest nodes above the block's top, SkinDepth at most
            double skinRise = 0;
            if (skin > 0)
            {
                var under = new List<int>();
                for (int ring = 2; ring <= SkinRings && under.Count * area * SkinDepth < skin; ring++)
                    foreach (var (i, j) in Ring(ring))
                    {
                        int k = i + j * Nx;
                        if (!pit[k] && Heights[k] > top && canTake(k) && Slack(i, j) >= SkinDepth) under.Add(k);
                    }
                if (under.Count * area * SkinDepth < skin - 1e-12) { Restore(); return null; }
                if (under.Count == 0) { Restore(); return null; }
                skinRise = skin / (under.Count * area);
                foreach (int k in under) Heights[k] += skinRise;
                spoil += skin;
            }
            Version++;
            Blocked = pit;
            try { Relax(edge, edge, Nx - 1 - edge, Nz - 1 - edge, gravity, 20000); } finally { Blocked = null; }
            bool clear = true;
            for (int k = 0; k < heights.Length; k++)
            {
                if (Heights[k] <= heights[k] + 1e-6) continue;
                int i = k % Nx, j = k / Nx;
                if (!inTheWay(new WorkedGround.Raised(k, CellX(i), CellZ(j), heights[k], Heights[k]))) continue;
                clear = false;
                pit[k] = true;   // soil already held against a body by the backhoe's own rule (#72) stays held there
                for (int dj = -reach; dj <= reach; dj++)
                    for (int di = -reach; di <= reach; di++)
                        if (i + di >= 0 && j + dj >= 0 && i + di < Nx && j + dj < Nz) shunned[i + di + (j + dj) * Nx] = true;
            }
            if (clear) return new FreedBlock(soil, block, spoil, count, given, crust, skin, skinRise);
            Restore();

            void Restore()
            {
                Array.Copy(heights, Heights, heights.Length);
                Array.Copy(loose, Loose, loose.Length);
                Array.Copy(soils, Soil, soils.Length);
                Version = version;
            }
        }
        return null;
    }

    /// <summary>
    /// m cell (i, j) may rise by and every slope down from it still stand: the least, over its lower neighbours, of how far the drop
    /// to it is under its soil's repose (a loose slope steeper fails, and so does a face of soil with no cohesion); far more than
    /// any skin (1 m) where it has no lower neighbour.
    /// </summary>
    private double Slack(int i, int j)
    {
        int k = i + j * Nx;
        double h = Heights[k], slack = 1, friction = SoilOf(k).Friction;
        for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                int ni = i + di, nj = j + dj;
                if ((di | dj) == 0 || ni < 0 || nj < 0 || ni >= Nx || nj >= Nz) continue;
                double run = (di != 0 && dj != 0 ? Math.Sqrt(2) : 1) * Cell, drop = h - Heights[ni + nj * Nx];
                if (drop > 0) slack = Math.Min(slack, friction * run - drop);
            }
        return slack;
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
