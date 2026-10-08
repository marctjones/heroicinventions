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
public sealed record SoilSpec(string Material, double Infiltration, double Cohesion = 0, double Friction = 0.6, double Density = 1600,
                              double GrainSize = 0, double GrainDensity = 2650)
{
    /// <summary>Whether flowing water can lift its grains at all (issue #53): it has a grain size.</summary>
    public bool Erodible => GrainSize > 0;

    /// <summary>
    /// How tall a cut face of this soil stands unsupported: 4c/γ · tan(45° + φ/2)
    /// (Terzaghi's critical height of a vertical cut). Loose soil (c = 0) stands at none.
    /// </summary>
    public double CriticalHeight(double gravity) =>
        4 * Cohesion / (Density * gravity) * Math.Tan(Math.PI / 4 + Math.Atan(Friction) / 2);

    /// <summary>What part of this soil comes down as boulders when a face of it fails (issue #88), or null: it all comes down as loose soil.</summary>
    public BoulderSpec? Boulders { get; init; }
}

/// <summary>
/// The wind over a map's floor (issue #61): a regular but variable wind that a notch in the rim funnels across
/// the floor, strongest in a corridor below the notch:
/// <code>v(x, z, t) = Speed · corridor(x, z) · daily(hour) · gusts(t)</code>
/// <b>corridor</b> is Base + (1 − Base)·exp(−(d / Width)²), d the distance of the point across the line that
/// runs from the notch (at azimuth <see cref="NotchDeg"/>, measured from +x toward +z) through the point
/// (<see cref="ThroughX"/>, <see cref="ThroughZ"/>); <b>daily</b> is 1 + Daily·cos(2π(hour − PeakHour)/24), the
/// crater's walls draining cold air down them at night and drawing warm air up by day (measured in Gale crater:
/// stronger and gustier at night); <b>gusts</b> is 1 + Gust·(sin(2πt/37 s) + sin(2πt/91 s + 1.3))/2, the same
/// gusts every run. A windmill that takes its wind from the map (#:wind-from-map) sees this at its own place.
/// </summary>
public sealed record WindField(double ThroughX, double ThroughZ, double NotchDeg, double Speed, double Width,
                               double Base, double Daily, double PeakHour, double Gust)
{
    /// <summary>The corridor's share of the full speed at a point: 1 along its line, Base far from it.</summary>
    public double Corridor(double x, double z)
    {
        double a = NotchDeg * Math.PI / 180;
        double across = -(x - ThroughX) * Math.Sin(a) + (z - ThroughZ) * Math.Cos(a);
        return Base + (1 - Base) * Math.Exp(-(across / Width) * (across / Width));
    }

    public double DailyFactor(double hour) => 1 + Daily * Math.Cos(2 * Math.PI * (hour - PeakHour) / 24);

    public double Gusts(double seconds) =>
        1 + Gust * (Math.Sin(2 * Math.PI * seconds / 37) + Math.Sin(2 * Math.PI * seconds / 91 + 1.3)) / 2;

    /// <summary>The wind's speed (m/s) at a point, at a solar hour of the day and a number of seconds into the run.</summary>
    public double SpeedAt(double x, double z, double hour, double seconds) =>
        Speed * Corridor(x, z) * DailyFactor(hour) * Gusts(seconds);
}

/// <summary>
/// The rock in a soil (issue #88): when a face fails, <see cref="Fraction"/> of the volume that comes down
/// comes down as cubes <see cref="Size"/> m on a side, of <see cref="Material"/> (a material in the table).
/// </summary>
public sealed record BoulderSpec(double Fraction, double Size, string Material);

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
    /// <summary>The wind over the floor (issue #61), or null for a map with none: windmills there take the wind they are given.</summary>
    public WindField? Wind { get; init; }

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

    /// <summary>
    /// The ground's height at a world point: where the rover has worked it (<see cref="Worked"/>), that fine ground, else the
    /// map's cells interpolated between their centres (held level beyond the outermost ones).
    /// </summary>
    public double HeightAt(double x, double z)
    {
        for (int n = 0; n < Worked.Count; n++)
            if (Worked[n].Inside(x, z)) return Worked[n].HeightAt(x, z);
        return CoarseHeightAt(x, z);
    }

    /// <summary>The map's own cells, interpolated between their centres, whatever has been worked over them.</summary>
    public double CoarseHeightAt(double x, double z)
    {
        double fx = Math.Clamp((x - X0) / Cell - 0.5, 0, Nx - 1), fz = Math.Clamp((z - Z0) / Cell - 0.5, 0, Nz - 1);
        int i = Math.Min((int)fx, Math.Max(0, Nx - 2)), j = Math.Min((int)fz, Math.Max(0, Nz - 2));
        double tx = Nx > 1 ? fx - i : 0, tz = Nz > 1 ? fz - j : 0;
        double H(int a, int b) => Heights[Math.Min(a, Nx - 1) + Math.Min(b, Nz - 1) * Nx];
        return (H(i, j) * (1 - tx) + H(i + 1, j) * tx) * (1 - tz) + (H(i, j + 1) * (1 - tx) + H(i + 1, j + 1) * tx) * tz;
    }

    /// <summary>
    /// The height of the map's drawn surface at a point: the ground mesh's triangles (each square between four cell centres is
    /// cut along the diagonal from (i+1, j) to (i, j+1), as the view cuts it), so a patch of finer cells that starts from these
    /// heights is the same ground bit for bit, not bilinear's slightly different one.
    /// </summary>
    public double CoarseSurfaceAt(double x, double z)
    {
        double fx = Math.Clamp((x - X0) / Cell - 0.5, 0, Nx - 1), fz = Math.Clamp((z - Z0) / Cell - 0.5, 0, Nz - 1);
        int i = Math.Min((int)fx, Math.Max(0, Nx - 2)), j = Math.Min((int)fz, Math.Max(0, Nz - 2));
        double u = Nx > 1 ? fx - i : 0, v = Nz > 1 ? fz - j : 0;
        double H(int a, int b) => Heights[Math.Min(a, Nx - 1) + Math.Min(b, Nz - 1) * Nx];
        double a0 = H(i, j), b0 = H(i + 1, j), c0 = H(i, j + 1), d0 = H(i + 1, j + 1);
        return u + v <= 1 ? a0 + u * (b0 - a0) + v * (c0 - a0) : d0 + (1 - u) * (c0 - d0) + (1 - v) * (b0 - d0);
    }

    /// <summary>The patches of fine ground the rover has made by digging (#63): at most a few, none overlapping, each over whole map cells.</summary>
    [NonSerialized] private readonly List<WorkedGround> _worked = [];   // saved its own way (SaveWorked), not field by field
    public List<WorkedGround> Worked => _worked;

    /// <summary>Goes up when patches are made or merged, so a view knows to rebuild what it shows of them.</summary>
    [NonSerialized] private int _workedPatches;
    public int WorkedPatches { get => _workedPatches; private set => _workedPatches = value; }

    /// <summary>
    /// The patch of fine ground to dig or dump at a point, made if there is none: <see cref="WorkedGround.Side"/> m square
    /// round the point, in whole map cells, kept inside the map. A point nearer than <see cref="WorkedGround.Margin"/> to the
    /// edge of a patch that is there is given a bigger patch (this one and the new square together, up to three times the side),
    /// its fine ground carried over. Null if that would be bigger, or the point is off the map.
    /// </summary>
    public WorkedGround? WorkAt(double x, double z)
    {
        if (!Contains(x, z)) return null;
        foreach (var w in Worked) if (w.Inside(x, z, WorkedGround.Margin)) return w;
        int n = Math.Max(2, (int)Math.Ceiling(WorkedGround.Side / Cell)), cap = 3 * n;
        double fx = (x - X0) / Cell - 0.5, fz = (z - Z0) / Cell - 0.5;
        int i0 = Math.Clamp((int)Math.Round(fx) - n / 2, 0, Math.Max(0, Nx - 1 - n)), j0 = Math.Clamp((int)Math.Round(fz) - n / 2, 0, Math.Max(0, Nz - 1 - n));
        int i1 = Math.Min(Nx - 1, i0 + n), j1 = Math.Min(Nz - 1, j0 + n);
        var parts = new List<WorkedGround>();
        bool grew;
        do
        {
            grew = false;
            foreach (var w in Worked)
                if (!parts.Contains(w) && w.Bi0 <= i1 && i0 <= w.Bi1 && w.Bj0 <= j1 && j0 <= w.Bj1)
                {
                    parts.Add(w);
                    (i0, j0, i1, j1) = (Math.Min(i0, w.Bi0), Math.Min(j0, w.Bj0), Math.Max(i1, w.Bi1), Math.Max(j1, w.Bj1));
                    grew = true;
                }
        } while (grew);
        if (i1 - i0 > cap || j1 - j0 > cap) return null;
        var made = parts.Count == 0 ? new WorkedGround(this, i0, j0, i1, j1) : WorkedGround.Merged(this, i0, j0, i1, j1, parts);
        foreach (var p in parts) Worked.Remove(p);
        Worked.Add(made);
        WorkedPatches++;
        return made.Inside(x, z, WorkedGround.Margin) ? made : null;
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
                                      s.Items.Count > 4 ? Num(s.Items[4]) : 1600,
                                      s.Items.Count > 5 ? Num(s.Items[5]) : 0,
                                      s.Items.Count > 6 && s.Items[6] is SNumber gd ? gd.Value : 2650)
                          {
                              // (boulders FRACTION SIZE MATERIAL) after the numbers (#88)
                              Boulders = s.Items.OfType<SList>().FirstOrDefault(b => b.Head == "boulders") is { } b
                                  ? (b.Items is [_, SNumber f, SNumber sz, SSymbol bm] && f.Value is >= 0 and <= 1 && sz.Value > 0
                                        ? new BoulderSpec(f.Value, sz.Value, bm.Name)
                                        : throw new MachineFormatException($"{file}: a soil's boulders are (boulders FRACTION SIZE MATERIAL), fraction 0 to 1, size over 0"))
                                  : null,
                          }).ToList();
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
            SettleOnLoad = root.Field("settle")?.Items.ElementAtOrDefault(1) is SBool { Value: true } or SNumber { Value: > 0 },
            SettleRate = root.Field("settle")?.Items.ElementAtOrDefault(1) is SNumber { Value: > 0 } rate ? rate.Value : 0,
            Wind = root.Field("wind")?.Field("corridor") is { Items.Count: 10 } w
                ? new WindField(Num(w.Items[1]), Num(w.Items[2]), Num(w.Items[3]), Num(w.Items[4]), Num(w.Items[5]), Num(w.Items[6]), Num(w.Items[7]), Num(w.Items[8]), Num(w.Items[9]))
                : root.Field("wind") is not null ? throw new MachineFormatException($"{file}: map {name.Name}: (wind (corridor THROUGH-X THROUGH-Z NOTCH-DEG SPEED WIDTH BASE DAILY PEAK-HOUR GUST))") : null,
            Sources = root.Fields("source").Select(s => new MapSource(((SSymbol)s.Items[1]).Name, Num(s.Items[2]), Num(s.Items[3]), Num(s.Items[4]))).ToList(),
        };
    }

    /// <summary>The map written back in the shape <see cref="Parse"/> reads (after earthworks, the ground as it now stands).</summary>
    public string Write()
    {
        static string N(double v) => SExprWriter.Number(v);
        var sb = new StringBuilder();
        sb.Append($"(map {Name}\n  (origin {N(X0)} {N(Z0)}) (cell {N(Cell)}) (size {Nx} {Nz}) (edges {(OpenEdges ? "open" : "closed")}) (roughness {N(Roughness)}){(SettleOnLoad ? (SettleRate > 0 ? $" (settle {N(SettleRate)})" : " (settle #t)") : "")}\n");
        if (Wind is { } wind)
            sb.Append($"  (wind (corridor {N(wind.ThroughX)} {N(wind.ThroughZ)} {N(wind.NotchDeg)} {N(wind.Speed)} {N(wind.Width)} {N(wind.Base)} {N(wind.Daily)} {N(wind.PeakHour)} {N(wind.Gust)}))\n");
        sb.Append("  (soils").Append(string.Concat(Soils.Select(s => $" ({s.Material} {N(s.Infiltration)} {N(s.Cohesion)} {N(s.Friction)} {N(s.Density)} {N(s.GrainSize)} {N(s.GrainDensity)}{(s.Boulders is { } b ? $" (boulders {N(b.Fraction)} {N(b.Size)} {b.Material})" : "")})"))).Append(")\n");
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
