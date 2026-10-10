using Godot;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// A plain block of a material drawn as that material, not as a coloured box (art direction 12.19, owner 2026-10-10: "cartoonish
/// but recognisable"). The physics is the block's box (MaterialBlock): every look here is the visual mesh only, and it keeps the
/// mesh's bounding box exactly the block's (faces stay on the box's planes), so framing, picking and the rover's reach to it are
/// unchanged; extra pieces are scenery (never picked, never in a reach box).
/// <list type="bullet">
/// <item>Stone: rough faceted rock, its corners knocked off by different amounts (a truncated cube, cuts 10 to 24% of a side).</item>
/// <item>Wood: cut timber, the end grain's rings on the two ends (the grain runs along X, as <see cref="Skins.OrientGrain"/> lays it
/// on a cube), and bark left along the four long edges. Plank seams come from <see cref="Skins.PlankSeams"/> as before.</item>
/// <item>Metal: a cast ingot, its sides sloping in to a smaller top (the top 72% of the base's width).</item>
/// </list>
/// Other materials (soil, fibre, glass) keep the plain box.
/// </summary>
public static class BlockLooks
{
    /// <summary>Dresses <paramref name="mesh"/> (the block's own cube, of <paramref name="size"/>) as its material; <paramref name="seed"/> keeps a rock's facets the same each run.</summary>
    public static void Dress(Node3D body, MeshInstance3D mesh, MaterialDef material, Vector3 size, string seed)
    {
        var mat = mesh.MaterialOverride as StandardMaterial3D;
        float s = Mathf.Min(size.X, Mathf.Min(size.Y, size.Z));
        switch (material.Category)
        {
            case MaterialCategory.Stone:
                uint hash = Hash(seed);
                mesh.Mesh = Truncated(size, corner => s * (0.10f + 0.14f * ((hash >> (corner * 3)) & 7) / 7f));
                break;
            case MaterialCategory.Metal:
                mesh.Mesh = Ingot(size, 0.14f);
                break;
            case MaterialCategory.Wood:
                var wood = mat?.AlbedoColor ?? Skins.ColorOf("oak");
                var face = Shapes.Mat(wood.Lightened(0.28f), roughness: 0.9f, outline: false);   // the sawn end, paler than the sides
                var ring = Shapes.Mat(wood.Darkened(0.35f), roughness: 0.9f, outline: false);
                float r = Mathf.Min(size.Y, size.Z) / 2;
                foreach (float x in new[] { -1f, 1f })
                {
                    var end = new Vector3(x * (size.X / 2 + 0.0015f), 0, 0);
                    var disc = Shapes.Cylinder(r * 0.86f, 0.002f, face);
                    disc.Basis = new Basis(Vector3.Back, Mathf.Pi / 2);
                    disc.Position = end;
                    Add(body, disc);
                    foreach (float k in new[] { 0.22f, 0.45f, 0.67f, 0.84f })
                    {
                        float w = Mathf.Max(0.003f, r * 0.035f);
                        var line = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = r * k - w, OuterRadius = r * k + w, Rings = 32, RingSegments = 4 }, MaterialOverride = ring };
                        line.Basis = disc.Basis * Basis.FromScale(new Vector3(1, 0.15f, 1));   // flattened onto the face
                        line.Position = end + new Vector3(x * 0.001f, 0, 0);
                        Add(body, line);
                    }
                    var pith = Shapes.Cylinder(Mathf.Max(0.004f, r * 0.06f), 0.003f, ring);
                    pith.Basis = disc.Basis;
                    pith.Position = end + new Vector3(x * 0.001f, 0, 0);
                    Add(body, pith);
                }
                // bark left on the four long edges, standing a little proud, rough and dark
                float b = s * 0.09f;
                var bark = new List<(Vector3, Vector3)>();
                foreach (float y in new[] { -1f, 1f })
                    foreach (float z in new[] { -1f, 1f })
                        bark.Add((new Vector3(0, y * (size.Y / 2 - b * 0.3f), z * (size.Z / 2 - b * 0.3f)), new Vector3(size.X * 0.97f, b, b)));
                Add(body, Boxes(bark, Shapes.Mat(wood.Darkened(0.5f).Lerp(new Color(0.3f, 0.27f, 0.24f), 0.4f), roughness: 1f)));
                break;
        }
    }

    private static void Add(Node3D body, Node3D piece)
    {
        MachineView.MarkScenery(piece);
        body.AddChild(piece);
    }

    public static MeshInstance3D Boxes(IEnumerable<(Vector3 Centre, Vector3 Size)> boxes, StandardMaterial3D mat)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (c, size) in boxes) st.AppendFrom(new BoxMesh { Size = size }, 0, new Transform3D(Basis.Identity, c));
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = mat };
    }

    private static uint Hash(string text)
    {
        uint hash = 2166136261;   // FNV-1a, the same each run
        foreach (char c in text) hash = (hash ^ c) * 16777619;
        return hash;
    }

    /// <summary>
    /// A box of <paramref name="size"/> with each of its eight corners cut off by <paramref name="cut"/>(corner) along each edge: six
    /// octagons on the box's own planes (so its bounding box is the box's) and eight corner triangles, flat-shaded.
    /// </summary>
    public static ArrayMesh Truncated(Vector3 size, Func<int, float> cut)
    {
        var h = size / 2;
        var corners = new List<(Vector3 Sign, Vector3[] P)>();
        for (int i = 0; i < 8; i++)
        {
            var sgn = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
            float c = Mathf.Min(cut(i), 0.45f * Mathf.Min(size.X, Mathf.Min(size.Y, size.Z)));
            var at = sgn * h;
            // P[a]: the corner moved in along axis a
            var p = new Vector3[3];
            for (int a = 0; a < 3; a++) { var q = at; q[a] -= sgn[a] * c; p[a] = q; }
            corners.Add((sgn, p));
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Polygon(List<Vector3> pts, Vector3 outward) => Face(st, pts, outward);
        for (int a = 0; a < 3; a++)
            foreach (float side in new[] { -1f, 1f })
            {
                var pts = new List<Vector3>();
                foreach (var (sgn, p) in corners.Where(c => c.Sign[a] == side))
                    for (int b = 0; b < 3; b++) if (b != a) pts.Add(p[b]);
                var outward = Vector3.Zero; outward[a] = side;
                Polygon(pts, outward);
            }
        foreach (var (sgn, p) in corners) Polygon([.. p], sgn.Normalized());
        return st.Commit();
    }

    /// <summary>A cast ingot in a box of <paramref name="size"/>: the base fills the box's bottom, the sides slope in by <paramref name="inset"/> of each side to the top.</summary>
    public static ArrayMesh Ingot(Vector3 size, float inset)
    {
        var h = size / 2;
        Vector3 P(float x, float y, float z) => new(x * h.X * (y > 0 ? 1 - 2 * inset : 1), y * h.Y, z * h.Z * (y > 0 ? 1 - 2 * inset : 1));
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (float y in new[] { -1f, 1f }) Face(st, [P(-1, y, -1), P(1, y, -1), P(1, y, 1), P(-1, y, 1)], new Vector3(0, y, 0));
        foreach (float x in new[] { -1f, 1f }) Face(st, [P(x, -1, -1), P(x, -1, 1), P(x, 1, 1), P(x, 1, -1)], new Vector3(x, 0, 0));
        foreach (float z in new[] { -1f, 1f }) Face(st, [P(-1, -1, z), P(1, -1, z), P(1, 1, z), P(-1, 1, z)], new Vector3(0, 0, z));
        return st.Commit();
    }

    /// <summary>A flat convex face through <paramref name="pts"/> (any order), facing <paramref name="outward"/> (roughly).</summary>
    private static void Face(SurfaceTool st, List<Vector3> pts, Vector3 outward)
    {
        {
            var centre = pts.Aggregate(Vector3.Zero, (a, v) => a + v) / pts.Count;
            var u = (pts[0] - centre).Normalized();
            var v = outward.Cross(u);
            var sorted = pts.OrderBy(q => Mathf.Atan2((q - centre).Dot(v), (q - centre).Dot(u))).ToList();   // counter-clockwise about outward
            var n = (sorted[1] - sorted[0]).Cross(sorted[2] - sorted[0]).Normalized();
            if (n.Dot(outward) < 0) n = -n;
            for (int k = 1; k + 1 < sorted.Count; k++)
                foreach (var q in new[] { sorted[0], sorted[k + 1], sorted[k] })   // clockwise seen from outside: Godot's front face
                {
                    st.SetNormal(n);
                    st.AddVertex(q);
                }
        }
    }
}
