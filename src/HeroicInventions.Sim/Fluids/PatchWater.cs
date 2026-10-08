namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// The water on a patch of worked ground (issue #200): a shallow-water grid of the patch's own fine cells, nested in the map's
/// coarse one, so a trench the rover digs holds water and a channel it cuts carries it.
///
/// The grid covers the map cells wholly inside the patch, <see cref="I0"/>..I0+Ci−1 by <see cref="J0"/>..J0+Cj−1, each cut into
/// K × K cells (0.25 m on the crater's 5 m map): its edges lie on the map's own cell faces, so what crosses a face between a
/// coarse cell and the fine ones is one number on both sides and the water is counted once. The patch's outer half-cells
/// (the ring the rover never works, <see cref="WorkedGround.Margin"/>) stay with the coarse grid. Each fine cell's bed is the
/// worked ground at its centre (the mean of its four nodes), its soil the node's there: it soaks water away at that soil's
/// rate, as a coarse cell does.
///
/// The coarse cells under the grid hold no water while it is there (<see cref="ShallowWater2D"/> moves what they held into it);
/// the water lives on the patch, so it is merged with patches and saved with them.
/// </summary>
public sealed class PatchWater
{
    public WorkedGround Patch { get; }
    /// <summary>The first map cell the grid covers, and how many it covers each way.</summary>
    public int I0 { get; }
    public int J0 { get; }
    public int Ci { get; }
    public int Cj { get; }
    /// <summary>Fine cells to a map cell's side.</summary>
    public int K { get; }
    /// <summary>The fine cells' beds and soils, as a terrain the nested solver runs on.</summary>
    public Terrain Bed { get; }
    public ShallowWater2D Water { get; }
    private int _bedVersion;

    public PatchWater(WorkedGround patch)
    {
        Patch = patch;
        var map = patch.Base;
        K = patch.K;
        (I0, J0) = (patch.Bi0 + 1, patch.Bj0 + 1);
        (Ci, Cj) = (Math.Max(0, patch.Bi1 - patch.Bi0 - 1), Math.Max(0, patch.Bj1 - patch.Bj0 - 1));
        int nx = Math.Max(1, Ci * K), nz = Math.Max(1, Cj * K);
        Bed = new Terrain
        {
            Name = patch.Fine.Name + "-water", X0 = map.X0 + I0 * map.Cell, Z0 = map.Z0 + J0 * map.Cell, Cell = map.Cell / K,
            Nx = nx, Nz = nz, Heights = new double[nx * nz], Soil = new int[nx * nz], Soils = patch.Fine.Soils,
            OpenEdges = false, Roughness = map.Roughness,
        };
        Resample();
        Water = new ShallowWater2D(Bed, nested: true);
    }

    /// <summary>Whether the patch's ground has changed since the beds were sampled.</summary>
    public bool Stale => _bedVersion != Patch.Version;

    /// <summary>
    /// Each fine cell's bed and soil from the worked ground as it now stands. A cell dug or heaped under water keeps its depth
    /// of water: the volume is kept, and the surface moves with the bed until the water runs level again.
    /// </summary>
    public void Resample()
    {
        var fine = Patch.Fine;
        for (int j = 0; j < Bed.Nz; j++)
            for (int i = 0; i < Bed.Nx; i++)
            {
                double x = Bed.CellX(i), z = Bed.CellZ(j);
                int c = i + j * Bed.Nx;
                Bed.Heights[c] = fine.CoarseHeightAt(x, z);
                Bed.Soil[c] = fine.Soil[fine.CellAt(x, z) ?? 0];
            }
        _bedVersion = Patch.Version;
        Bed.Touch();
    }

    /// <summary>The fine cell (i, j) of map cell (ci, cj) of the grid (both counted from the grid's corner).</summary>
    internal int FineCell(int ci, int cj, int i, int j) => ci * K + i + (cj * K + j) * Bed.Nx;
}
