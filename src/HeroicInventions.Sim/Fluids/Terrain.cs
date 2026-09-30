using System.Globalization;
using System.Text;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// A soil of a map: its material, how fast it soaks water away (m/s), and
/// its strength by Mohr–Coulomb (issue #44): cohesion c (Pa) and friction
/// tan φ, the tangent of its angle of repose, with its density (kg/m³).
/// Shear strength at depth z is c + γ·z·tan φ, γ = ρ·g.
/// </summary>
public sealed record SoilSpec(string Material, double Infiltration, double Cohesion = 0, double Friction = 0.6, double Density = 1600)
{
    /// <summary>
    /// How tall a cut face of this soil stands unsupported: 4c/γ · tan(45° + φ/2)
    /// (Terzaghi's critical height of a vertical cut). Loose soil (c = 0) stands at none.
    /// </summary>
    public double CriticalHeight(double gravity) =>
        4 * Cohesion / (Density * gravity) * Math.Tan(Math.PI / 4 + Math.Atan(Friction) / 2);
}

/// <summary>A spring on open ground: water welling up at a point of the map, m³/s.</summary>
public sealed record MapSource(string Id, double X, double Z, double Flow);

/// <summary>
/// The ground of a world (issue #37): a heightfield on a regular grid of
/// square cells, one height (m, at the cell's centre) and one soil per
/// cell, authored in Racket (<c>define-map</c>) and read here from its
/// <c>.map</c> file. Every soil in a map carries how fast it soaks water
/// away (m/s). Cell (i, j) is centred at (X0 + (i + ½)·Cell, Z0 + (j + ½)·Cell);
/// arrays run x fastest: index i + j·Nx.
/// <code>
/// (map NAME (origin X0 Z0) (cell C) (size NX NZ) (edges open|closed) (roughness n)
///   (soils (sand 1e-5) (clay 0)) (heights h ...) (soil k ...) (source ID X Z FLOW) ...)
/// </code>
/// </summary>
public sealed partial class Terrain
{
    public required string Name { get; init; }
    public double X0 { get; init; }
    public double Z0 { get; init; }
    public required double Cell { get; init; }
    public required int Nx { get; init; }
    public required int Nz { get; init; }
    /// <summary>m, at each cell's centre (index i + j·Nx). Earthworks (#44) change them.</summary>
    public required double[] Heights { get; init; }
    /// <summary>Each cell's soil, an index into <see cref="Soils"/>.</summary>
    public required int[] Soil { get; init; }
    /// <summary>The map's soils: a material from the material table, how fast it soaks water away, and how strong it is.</summary>
    public required IReadOnlyList<SoilSpec> Soils { get; init; }
    /// <summary>Open edges let water run off the map (counted as leaked); closed ones are walls.</summary>
    public bool OpenEdges { get; init; } = true;
    /// <summary>Manning's n of the ground's surface (0.03: short grass, bare earth).</summary>
    public double Roughness { get; init; } = 0.03;
    public IReadOnlyList<MapSource> Sources { get; init; } = [];

    public int Count => Nx * Nz;
    public double Width => Nx * Cell;
    public double Depth => Nz * Cell;
    public double CellX(int i) => X0 + (i + 0.5) * Cell;
    public double CellZ(int j) => Z0 + (j + 0.5) * Cell;

    /// <summary>The cell a world point stands over, or null off the map.</summary>
    public int? CellAt(double x, double z)
    {
        int i = (int)Math.Floor((x - X0) / Cell), j = (int)Math.Floor((z - Z0) / Cell);
        return i < 0 || j < 0 || i >= Nx || j >= Nz ? null : i + j * Nx;
    }

    public bool Contains(double x, double z) => CellAt(x, z) is not null;

    /// <summary>The ground's height at a world point, interpolated between cell centres (held level beyond the outermost ones).</summary>
    public double HeightAt(double x, double z)
    {
        double fx = Math.Clamp((x - X0) / Cell - 0.5, 0, Nx - 1), fz = Math.Clamp((z - Z0) / Cell - 0.5, 0, Nz - 1);
        int i = Math.Min((int)fx, Math.Max(0, Nx - 2)), j = Math.Min((int)fz, Math.Max(0, Nz - 2));
        double tx = Nx > 1 ? fx - i : 0, tz = Nz > 1 ? fz - j : 0;
        double H(int a, int b) => Heights[Math.Min(a, Nx - 1) + Math.Min(b, Nz - 1) * Nx];
        return (H(i, j) * (1 - tx) + H(i + 1, j) * tx) * (1 - tz) + (H(i, j + 1) * (1 - tx) + H(i + 1, j + 1) * tx) * tz;
    }

    public static Terrain Parse(string text, string file = "<map>")
    {
        var root = SExprReader.ReadAll(text).OfType<SList>().FirstOrDefault(l => l.Head == "map")
            ?? throw new MachineFormatException($"{file}: expected (map NAME ...)");
        if (root.Items.Count < 2 || root.Items[1] is not SSymbol name)
            throw new MachineFormatException($"{file}: a map needs a name");
        double Num(SExpr e) => e is SNumber n ? n.Value : throw new MachineFormatException($"{file}: expected a number, got {e}");
        SList Need(string f) => root.Field(f) ?? throw new MachineFormatException($"{file}: map {name.Name} needs ({f} ...)");
        var size = Need("size");
        int nx = (int)Num(size.Items[1]), nz = (int)Num(size.Items[2]);
        var heights = Need("heights").Items.Skip(1).Select(Num).ToArray();
        if (heights.Length != nx * nz)
            throw new MachineFormatException($"{file}: map {name.Name} is {nx} x {nz} cells but has {heights.Length} heights");
        var soils = Need("soils").Items.Skip(1).OfType<SList>()
            .Select(s => new SoilSpec(s.Items[0] is SSymbol m ? m.Name : throw new MachineFormatException($"{file}: a soil is (MATERIAL RATE [COHESION FRICTION DENSITY])"),
                                      Num(s.Items[1]),
                                      s.Items.Count > 2 ? Num(s.Items[2]) : 0,
                                      s.Items.Count > 3 ? Num(s.Items[3]) : 0.6,
                                      s.Items.Count > 4 ? Num(s.Items[4]) : 1600)).ToList();
        var soilItems = root.Field("soil")?.Items.Skip(1).Select(x => (int)Num(x)).ToArray() ?? [];
        var soil = soilItems.Length == 1 ? Enumerable.Repeat(soilItems[0], nx * nz).ToArray()
                 : soilItems.Length == nx * nz ? soilItems
                 : soilItems.Length == 0 ? new int[nx * nz]
                 : throw new MachineFormatException($"{file}: map {name.Name} has {soilItems.Length} soil entries for {nx * nz} cells");
        if (soil.Any(k => k < 0 || k >= soils.Count))
            throw new MachineFormatException($"{file}: map {name.Name} names a soil it doesn't list");
        var origin = root.Field("origin");
        return new Terrain
        {
            Name = name.Name,
            X0 = origin is null ? 0 : Num(origin.Items[1]),
            Z0 = origin is null ? 0 : Num(origin.Items[2]),
            Cell = Num(Need("cell").Items[1]),
            Nx = nx, Nz = nz,
            Heights = heights,
            Soil = soil,
            Soils = soils,
            OpenEdges = root.Field("edges")?.Items.ElementAtOrDefault(1) is not SSymbol { Name: "closed" },
            Roughness = root.Field("roughness") is { } r ? Num(r.Items[1]) : 0.03,
            Sources = root.Fields("source").Select(s => new MapSource(((SSymbol)s.Items[1]).Name, Num(s.Items[2]), Num(s.Items[3]), Num(s.Items[4]))).ToList(),
        };
    }

    /// <summary>The map written back in the shape <see cref="Parse"/> reads (after earthworks, the ground as it now stands).</summary>
    public string Write()
    {
        static string N(double v) => SExprWriter.Number(v);
        var sb = new StringBuilder();
        sb.Append($"(map {Name}\n  (origin {N(X0)} {N(Z0)}) (cell {N(Cell)}) (size {Nx} {Nz}) (edges {(OpenEdges ? "open" : "closed")}) (roughness {N(Roughness)})\n");
        sb.Append("  (soils").Append(string.Concat(Soils.Select(s => $" ({s.Material} {N(s.Infiltration)} {N(s.Cohesion)} {N(s.Friction)} {N(s.Density)})"))).Append(")\n");
        foreach (var s in Sources) sb.Append($"  (source {s.Id} {N(s.X)} {N(s.Z)} {N(s.Flow)})\n");
        sb.Append("  (heights");
        for (int k = 0; k < Heights.Length; k++) sb.Append(k % Nx == 0 ? "\n   " : " ").Append(Heights[k].ToString("0.####", CultureInfo.InvariantCulture));
        sb.Append(")\n  (soil");
        if (Soil.All(k => k == Soil[0])) sb.Append(' ').Append(Soil[0]);
        else for (int k = 0; k < Soil.Length; k++) sb.Append(k % Nx == 0 ? "\n   " : " ").Append(Soil[k]);
        sb.Append("))\n");
        return sb.ToString();
    }
}
