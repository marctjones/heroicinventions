using Godot;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// A plain block of a material drawn as that material, not as a coloured box (art direction 12.19, owner 2026-10-10: "cartoonish
/// but recognisable"). The physics is the block's box (MaterialBlock): every look here is the visual mesh only, and it keeps the
/// mesh's bounding box exactly the block's (faces stay on the box's planes), so framing, picking and the rover's reach to it are
/// unchanged; extra pieces are scenery (never picked, never in a reach box).
/// <list type="bullet">
/// <item>Stone: rough faceted rock, its corners knocked off by different amounts (a truncated cube, cuts 16 to 34% of the shortest side; 12.20).</item>
/// <item>A stone hung by a rope at its top: a squared block in a rope sling (<see cref="DressHungStone"/>); a hung block named a bucket: a bucket (<see cref="DressBucket"/>).</item>
/// <item>Wood: cut timber, the end grain's rings on the two ends (the grain runs along X, as <see cref="Skins.OrientGrain"/> lays it
/// on a cube), and bark left along the four long edges. Plank seams come from <see cref="Skins.PlankSeams"/> as before.</item>
/// <item>Metal: a cast ingot, its sides sloping in to a smaller top (the top 72% of the base's width).</item>
/// <item>Fibre: a bale, tied with two twine bands, its ends ruled with the cut stalks (12.20).</item>
/// </list>
/// Other materials (soil, glass) keep the plain box.
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
                mesh.Mesh = Truncated(size, corner => s * (0.16f + 0.18f * ((hash >> (corner * 3)) & 7) / 7f));   // 16 to 34% (12.20: at 10 to 24% a boulder still read as a box)
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
            case MaterialCategory.Fiber:
                // a bale: two twine bands round it across the X axis, and the stalks' cut ends ruled on the two ends (12.20)
                var straw = mat?.AlbedoColor ?? Skins.ColorOf("hemp");
                float t = Mathf.Max(0.004f, s * 0.03f), bw = Mathf.Max(0.008f, s * 0.06f);
                var twine = new List<(Vector3, Vector3)>();
                var hb = size / 2;
                foreach (float x in new[] { -0.28f, 0.28f })
                {
                    float cx = x * size.X;
                    twine.Add((new Vector3(cx, hb.Y + t / 2, 0), new Vector3(bw, t, size.Z + 2 * t)));
                    twine.Add((new Vector3(cx, -hb.Y - t / 2, 0), new Vector3(bw, t, size.Z + 2 * t)));
                    twine.Add((new Vector3(cx, 0, hb.Z + t / 2), new Vector3(bw, size.Y, t)));
                    twine.Add((new Vector3(cx, 0, -hb.Z - t / 2), new Vector3(bw, size.Y, t)));
                }
                Add(body, Boxes(twine, Shapes.Mat(straw.Darkened(0.55f), roughness: 1f)));
                var stalks = new List<(Vector3, Vector3)>();
                foreach (float x in new[] { -1f, 1f })
                    for (int i = 1; i < 6; i++)
                        stalks.Add((new Vector3(x * (hb.X + 0.001f), -hb.Y + size.Y * i / 6f, 0), new Vector3(0.002f, Mathf.Max(0.003f, size.Y * 0.025f), size.Z * 0.9f)));
                Add(body, Boxes(stalks, Shapes.Mat(straw.Darkened(0.3f), roughness: 1f, outline: false)));
                break;
        }
    }

    /// <summary>
    /// A stone hung on a rope at its top (a crane's load, a windlass's weight, a shaduf's counterweight; 12.20): a squared building
    /// block, its twelve edges dressed back (a chamfer, so it reads as cut stone and not as a box), tied in a rope sling, two hemp
    /// bands crossing over its top to an iron ring where the rope takes it. The mesh keeps the block's box exactly; the sling and
    /// ring ride on <paramref name="body"/> as scenery.
    /// </summary>
    public static void DressHungStone(Node3D body, MeshInstance3D mesh, Vector3 size, string seed)
    {
        float s = Mathf.Min(size.X, Mathf.Min(size.Y, size.Z));
        mesh.Mesh = Chamfered(size, s * (0.09f + 0.03f * (Hash(seed) & 3) / 3f));
        var hemp = Shapes.Mat(Skins.ColorOf("hemp").Darkened(0.15f), roughness: 1f);
        float t = Mathf.Max(0.008f, s * 0.045f), w = Mathf.Max(0.012f, s * 0.08f);   // the band's thickness and width
        var h = size / 2;
        var bands = new List<(Vector3, Vector3)>();
        // one band round the block in the XY plane (z = 0), one in the ZY plane (x = 0); standing t proud of each face
        bands.Add((new Vector3(0, h.Y + t / 2, 0), new Vector3(size.X + 2 * t, t, w)));
        bands.Add((new Vector3(0, -h.Y - t / 2, 0), new Vector3(size.X + 2 * t, t, w)));
        foreach (float x in new[] { -1f, 1f }) bands.Add((new Vector3(x * (h.X + t / 2), 0, 0), new Vector3(t, size.Y, w)));
        bands.Add((new Vector3(0, h.Y + t, 0), new Vector3(w, t, size.Z + 2 * t)));
        bands.Add((new Vector3(0, -h.Y - t, 0), new Vector3(w, t, size.Z + 2 * t)));
        foreach (float z in new[] { -1f, 1f }) bands.Add((new Vector3(0, 0, z * (h.Z + t / 2)), new Vector3(w, size.Y, t)));
        Add(body, Boxes(bands, hemp));
        var iron = Shapes.Mat(Skins.ColorOf("iron").Darkened(0.2f), metallic: 0.5f, roughness: 0.5f);
        float ring = Mathf.Max(0.02f, s * 0.12f);
        var loop = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = ring * 0.7f, OuterRadius = ring, Rings = 16, RingSegments = 6 }, MaterialOverride = iron };
        loop.Basis = new Basis(Vector3.Right, Mathf.Pi / 2);   // stood on edge, in the XY plane
        loop.Position = new Vector3(0, h.Y + 1.5f * t + ring * 0.85f, 0);
        Add(body, loop);
    }

    /// <summary>
    /// A shaduf's bucket in place of its block (12.20): a wooden tub, staved and hooped, wider at the rim, with water standing in it
    /// and an iron bail over the top to the rope. Everything is inside the block's box (the tub's rim and base fill it, the bail
    /// reaches its top), so the box the physics and the floats measure is the block's own.
    /// </summary>
    public static void DressBucket(Node3D body, MeshInstance3D mesh, Vector3 size, bool water)
    {
        var h = size / 2;
        float tub = size.Y * 0.68f;   // the tub's height; the bail fills the rest of the box
        mesh.Mesh = Tub(size, tub);
        var oak = Shapes.Mat(Skins.ColorOf("oak"), roughness: 0.9f);
        mesh.MaterialOverride = oak;   // a bucket is wood, whatever its weight is drawn from
        var iron = Shapes.Mat(Skins.ColorOf("iron").Darkened(0.2f), metallic: 0.5f, roughness: 0.5f);
        float y0 = -h.Y;
        foreach (float k in new[] { 0.18f, 0.82f })
        {
            float r = Mathf.Lerp(0.78f, 1f, k) * h.X;
            var hoop = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = r * 0.98f, OuterRadius = r * 1.05f, Rings = 24, RingSegments = 4 }, MaterialOverride = iron };
            hoop.Basis = Basis.FromScale(new Vector3(1, 0.5f, 1));
            hoop.Position = new Vector3(0, y0 + tub * k, 0);
            Add(body, hoop);
        }
        // its mouth: water standing just under the rim, or the dark inside of an empty tub (the tub's own mesh is open, and an inner face
        // would carry the outline over the outside)
        var mouth = Shapes.Cylinder(h.X * 0.96f, 0.004f, water ? Shapes.Mat(Shapes.Water, roughness: 0.2f, outline: false)
                                                              : Shapes.Mat(Skins.ColorOf("oak").Darkened(0.6f), roughness: 1f, outline: false));
        mouth.Scale = new Vector3(1, 1, size.Z / size.X);
        mouth.Position = new Vector3(0, y0 + tub - 0.01f * size.Y, 0);
        Add(body, mouth);
        // the bail: an arch of iron from the rim's two sides to the top of the box, where the rope ties on
        int n = 8;
        float rise = size.Y - tub - 0.006f;
        Vector3 P(int i)
        {
            float a = Mathf.Pi * i / n;
            return new Vector3(-Mathf.Cos(a) * h.X * 0.97f, y0 + tub + Mathf.Sin(a) * rise, 0);
        }
        for (int i = 0; i < n; i++) Add(body, Shapes.Rod(P(i), P(i + 1), Mathf.Max(0.004f, size.X * 0.025f), iron));
    }

    /// <summary>A tub in a box of <paramref name="size"/>: a 16-sided frustum <paramref name="height"/> tall from the box's floor, the base
    /// 78% of the rim, its rim touching the box's sides, open-topped (its mouth is a separate disc), plus a thin bar reaching the box's top so
    /// the mesh's bounding box is the box's exactly.</summary>
    public static ArrayMesh Tub(Vector3 size, float height)
    {
        var h = size / 2;
        const int sides = 16;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Rim(int i, float y, float k) { float a = Mathf.Tau * i / sides; return new Vector3(Mathf.Cos(a) * h.X * k, y, Mathf.Sin(a) * h.Z * k); }
        float lo = -h.Y, hi = -h.Y + height;
        for (int i = 0; i < sides; i++)
        {
            var a0 = Rim(i, lo, 0.78f); var a1 = Rim(i + 1, lo, 0.78f); var b0 = Rim(i, hi, 1f); var b1 = Rim(i + 1, hi, 1f);
            var outward = ((a0 + a1 + b0 + b1) / 4) with { Y = 0 };
            Face(st, [a0, a1, b1, b0], outward);                       // a stave, outside
            Face(st, [new Vector3(0, lo, 0), a0, a1], Vector3.Down);   // the base, underneath
        }
        // the box's top: a sliver at the very top centre, under the bail's crown, so the mesh's box reaches it
        float e = Mathf.Min(h.X, h.Z) * 0.02f;
        Face(st, [new Vector3(-e, h.Y, -e), new Vector3(e, h.Y, -e), new Vector3(e, h.Y, e), new Vector3(-e, h.Y, e)], Vector3.Up);
        return st.Commit();
    }

    /// <summary>
    /// A box of <paramref name="size"/> with its twelve edges chamfered back by <paramref name="c"/>: six faces on the box's own planes
    /// (so its bounding box is the box's), twelve bevels and eight corner triangles, flat-shaded.
    /// </summary>
    public static ArrayMesh Chamfered(Vector3 size, float c)
    {
        var h = size / 2;
        c = Mathf.Min(c, 0.45f * Mathf.Min(size.X, Mathf.Min(size.Y, size.Z)));
        var corners = new List<(Vector3 Sign, Vector3[] P)>();
        for (int i = 0; i < 8; i++)
        {
            var sgn = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
            var p = new Vector3[3];   // P[a]: the corner on face a, moved in along the other two axes
            for (int a = 0; a < 3; a++) { var q = sgn * h; for (int b = 0; b < 3; b++) if (b != a) q[b] -= sgn[b] * c; p[a] = q; }
            corners.Add((sgn, p));
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int a = 0; a < 3; a++)
            foreach (float side in new[] { -1f, 1f })
            {
                var outward = Vector3.Zero; outward[a] = side;
                Face(st, corners.Where(k => k.Sign[a] == side).Select(k => k.P[a]).ToList(), outward);
                for (int b = a + 1; b < 3; b++)
                    foreach (float side2 in new[] { -1f, 1f })
                    {
                        var edge = corners.Where(k => k.Sign[a] == side && k.Sign[b] == side2).SelectMany(k => new[] { k.P[a], k.P[b] }).ToList();
                        var o = outward; o[b] = side2;
                        Face(st, edge, o);
                    }
            }
        foreach (var (sgn, p) in corners) Face(st, [.. p], sgn.Normalized());
        return st.Commit();
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
