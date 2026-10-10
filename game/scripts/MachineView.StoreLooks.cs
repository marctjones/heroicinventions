using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// The heat stores, bins and vaults drawn as things you could name (art direction 12.21, owner 2026-10-10: "cartoonish but
/// recognizable"), with their state still on them. Look only: every piece is scenery (<see cref="MarkScenery"/>: never picked,
/// never in a reach box, never in <see cref="BoxSize"/>), and a store's own mesh keeps its box exactly.
/// <list type="bullet">
/// <item>A store of rock is a lump of rock (<see cref="BlockLooks.Truncated"/>, its corners knocked off by 18 to 38% of a side), still
/// washed with its temperature (<see cref="Skins.Warm"/>, <see cref="Skins.Glow"/>). Cracks run over its top and sides: white with rime
/// below 0 °C, dark at room heat, and from 35 °C glowing dull red, orange by 200 °C, yellow by 400 and yellow-white by 800. The glow is
/// a drawn gauge, not incandescence (rock does not glow below about 500 °C): it is there so a hot rock reads as hot at a glance.</item>
/// <item>A store of cells (a battery bank's) is a dark case with ribs for its cells and a red and a black terminal on top.</item>
/// <item>A bin has dark corner battens, a lid lined with pale wool underneath (so a lid that lifts shows a pale face, more as it opens),
/// two iron hinge straps and a handle.</item>
/// <item>A vault's earth shows its layers: a greyer, stony bed in the lower part of every face, with stones in the cut. Timber
/// props stand at the front corners of the dug room under a lintel. A vault under the ground has a hatch on the surface over it: a
/// timber collar round a shaft whose mouth is washed with the room's air temperature, a ladder's top standing out of it and the
/// trapdoor thrown back.</item>
/// </list>
/// </summary>
public partial class MachineView
{
    /// <summary>A rock store's cracks: the rime in them (cold) and their glow (hot), drawn over one mesh.</summary>
    private sealed record Cracks(MeshInstance3D Rime, StandardMaterial3D RimeMat, MeshInstance3D Glow, StandardMaterial3D GlowMat);

    /// <summary>Whether a store is drawn as rock: anything but water and a battery's cells.</summary>
    private static bool IsRock(HeatStore store) => store.Substance.Name is not ("water" or "cells");

    /// <summary>Dresses a store's cube (<paramref name="block"/>, side <paramref name="s"/>, centred on its own origin) as a lump of rock with cracks.</summary>
    private static Cracks DressRock(MeshInstance3D block, float s, string seed)
    {
        uint hash = Fnv(seed);
        block.Mesh = BlockLooks.Truncated(Vector3.One * s, corner => s * (0.18f + 0.2f * ((hash >> (corner * 3)) & 7) / 7f));
        var mesh = CrackMesh(s, hash);
        var rimeMat = Shapes.Mat(new Color(0.9f, 0.95f, 1f), roughness: 0.6f, outline: false);
        var glowMat = Shapes.Mat(new Color(0.75f, 0.12f, 0.04f), outline: false);
        glowMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;   // its own light: it reads at night too
        var rime = new MeshInstance3D { Mesh = mesh, MaterialOverride = rimeMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var glow = new MeshInstance3D { Mesh = mesh, MaterialOverride = glowMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        Decorate(block, rime);
        Decorate(block, glow);
        return new Cracks(rime, rimeMat, glow, glowMat);
    }

    /// <summary>
    /// Cracks over the top and the four sides of a cube of side <paramref name="s"/>: on each face a zigzag of four strokes across its
    /// middle and one branch, kept inside the face's octagon (within 0.6 of the half-side, so a deep corner cut does not reach them),
    /// each stroke a flat strip 1.5 mm proud of the face.
    /// </summary>
    private static ArrayMesh CrackMesh(float s, uint hash)
    {
        float h = s / 2, w = Mathf.Max(0.004f, s * 0.035f);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var faces = new (Vector3 N, Vector3 U)[] { (Vector3.Up, Vector3.Right), (Vector3.Right, Vector3.Back), (Vector3.Left, Vector3.Back), (Vector3.Back, Vector3.Right), (Vector3.Forward, Vector3.Right) };
        int f = 0;
        foreach (var (n, u) in faces)
        {
            var v = n.Cross(u);
            float J(int i) => ((((hash >> ((f * 5 + i) % 29)) & 7) / 7f) - 0.5f) * 0.8f;   // -0.4 to 0.4, fixed by the store's name
            Vector3 P(float a, float b) => n * (h + 0.0015f) + u * (a * h) + v * (b * h);
            var pts = new[] { P(-0.6f, J(0) * 0.6f), P(-0.25f, J(1)), P(0.1f, J(2)), P(0.35f, J(3)), P(0.6f, J(4) * 0.6f) };
            for (int i = 0; i + 1 < pts.Length; i++) Stroke(st, pts[i], pts[i + 1], n, w);
            Stroke(st, pts[2], P(0.1f + 0.2f * Mathf.Sign(J(5) + 0.01f), Mathf.Clamp(J(2) + (J(2) > 0 ? -0.45f : 0.45f), -0.55f, 0.55f)), n, w * 0.8f);
            f++;
        }
        return st.Commit();
    }

    /// <summary>A flat strip from <paramref name="a"/> to <paramref name="b"/> lying on a face of normal <paramref name="n"/>.</summary>
    private static void Stroke(SurfaceTool st, Vector3 a, Vector3 b, Vector3 n, float width)
    {
        var x = (b - a).Normalized();
        var z = x.Cross(n).Normalized();
        st.AppendFrom(new BoxMesh { Size = new Vector3((b - a).Length() + width * 0.6f, 0.001f, width) }, 0, new Transform3D(new Basis(x, n, z), (a + b) / 2));
    }

    /// <summary>
    /// The colour a rock's cracks glow at <paramref name="celsius"/>, and how far in (0: not shown): from 35 °C a dull red coming up
    /// out of the dark, orange by 200 °C, yellow by 400, yellow-white by 800. Brighter than incandescence would be on purpose.
    /// </summary>
    public static (Color Colour, float Share) CrackGlow(double celsius)
    {
        float c = (float)celsius;
        if (c < 35) return (new Color(0.75f, 0.12f, 0.04f), 0);
        var red = new Color(0.78f, 0.12f, 0.04f); var orange = new Color(1f, 0.46f, 0.08f); var yellow = new Color(1f, 0.84f, 0.3f); var white = new Color(1f, 0.97f, 0.86f);
        var dark = new Color(0.22f, 0.1f, 0.08f);
        if (c < 80) return (dark.Lerp(red, (c - 35) / 45f), 1);
        if (c < 200) return (red.Lerp(orange, (c - 80) / 120f), 1);
        if (c < 400) return (orange.Lerp(yellow, (c - 200) / 200f), 1);
        return (yellow.Lerp(white, Mathf.Clamp((c - 400) / 400f, 0, 1)), 1);
    }

    /// <summary>How white with rime a cold rock's cracks are at <paramref name="celsius"/>: none at 0 °C, full by -20.</summary>
    public static float CrackRime(double celsius) => Mathf.Clamp(-(float)celsius / 20f, 0, 1);

    private static void ShowCracks(Cracks c, double celsius)
    {
        var (glow, share) = CrackGlow(celsius);
        c.Glow.Visible = share > 0;
        c.GlowMat.AlbedoColor = glow;
        float rime = CrackRime(celsius);
        c.Rime.Visible = rime > 0.02f;
        c.RimeMat.AlbedoColor = new Color(0.45f, 0.45f, 0.5f).Lerp(new Color(0.92f, 0.96f, 1f), rime);
    }

    /// <summary>A battery's cells in place of a plain box (<paramref name="block"/>, side <paramref name="s"/>): ribs down its sides for the cells, a red and a black terminal on top.</summary>
    private static void DressCells(MeshInstance3D block, float s)
    {
        float h = s / 2, t = Mathf.Max(0.003f, s * 0.025f);
        var ribs = new List<(Vector3, Vector3)>();
        foreach (float k in new[] { -0.5f, 0f, 0.5f })
        {
            ribs.Add((new Vector3(k * h, 0, h + t / 2), new Vector3(t * 1.5f, s * 0.86f, t)));
            ribs.Add((new Vector3(k * h, 0, -h - t / 2), new Vector3(t * 1.5f, s * 0.86f, t)));
            ribs.Add((new Vector3(h + t / 2, 0, k * h), new Vector3(t, s * 0.86f, t * 1.5f)));
            ribs.Add((new Vector3(-h - t / 2, 0, k * h), new Vector3(t, s * 0.86f, t * 1.5f)));
        }
        Decorate(block, Merged(ribs, Shapes.Mat(new Color(0.16f, 0.16f, 0.18f), roughness: 0.7f, outline: false)));
        foreach (var (x, c) in new[] { (-0.5f, new Color(0.85f, 0.15f, 0.12f)), (0.5f, new Color(0.1f, 0.1f, 0.11f)) })
        {
            var post = Shapes.Cylinder(s * 0.08f, s * 0.12f, Shapes.Mat(c, metallic: 0.3f, roughness: 0.4f));
            post.Position = new Vector3(x * h, h + s * 0.06f, 0);
            Decorate(block, post);
        }
    }

    /// <summary>
    /// A bin's dressing (12.21): dark battens up its four outer corners, and on the lid (which swings, so it
    /// goes with it) a pale wool lining underneath, two iron hinge straps from the back and a handle at the front. <paramref name="at"/> is
    /// the bin's centre on the ground, <paramref name="floor"/> its floor's thickness (0 for a bin a rock is pushed into).
    /// </summary>
    private void DressBin(Vector3 at, float inner, float wall, float floor, Node3D lid, StandardMaterial3D wood)
    {
        float outer = inner + 2 * wall, o = outer / 2, b = Mathf.Max(0.012f, outer * 0.07f), top = floor + inner;
        var battens = new List<(Vector3, Vector3)>();
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                battens.Add((at + new Vector3(x * (o - b / 2 + 0.002f), top / 2, z < 0 ? -(o - b / 2 + 0.002f) : inner / 2 - b / 2 + 0.002f), new Vector3(b, top, b)));
        var dark = Shapes.Mat(wood.AlbedoColor.Darkened(0.45f), roughness: 0.9f);
        var bodyPieces = Merged(battens, dark);
        MarkScenery(bodyPieces);
        AddChild(bodyPieces);
        // the lid's pieces, in the lid's frame: the hinge at the origin, the lid running +z from it, 4 cm thick
        float lt = 0.04f;
        var wool = Shapes.Box(new Vector3(outer * 0.9f, 0.006f, outer * 0.9f), Shapes.Mat(new Color(0.9f, 0.86f, 0.76f), roughness: 1f, outline: false));
        wool.Position = new Vector3(0, -0.003f, outer / 2);
        Decorate(lid, wool);
        var iron = Shapes.Mat(Skins.ColorOf("iron").Darkened(0.25f), metallic: 0.5f, roughness: 0.5f);
        var straps = new List<(Vector3, Vector3)>();
        foreach (float x in new[] { -0.3f, 0.3f })
            straps.Add((new Vector3(x * outer, lt + 0.002f, outer * 0.3f), new Vector3(outer * 0.08f, 0.004f, outer * 0.6f)));
        Decorate(lid, Merged(straps, iron));
        var handle = Shapes.Box(new Vector3(outer * 0.3f, 0.012f, 0.012f), iron);
        handle.Position = new Vector3(0, lt + 0.012f, outer - 0.02f);
        Decorate(lid, handle);
    }

    /// <summary>
    /// A vault's earth as dug ground (12.21), over the three earth blocks round the room (<paramref name="blocks"/>, their centres and
    /// sizes in this view): a greyer stony bed in the lower part of every block, stones scattered in the cut, and timber props at
    /// the room's front corners under a lintel, with a beam across the back.
    /// </summary>
    private void DressVault(Vector3 at, float w, float h, float d, IEnumerable<(Vector3 Centre, Vector3 Size)> blocks, Color earth, string seed)
    {
        var beds = new List<(Vector3, Vector3)>();
        foreach (var (c, size) in blocks)
            beds.Add((c + new Vector3(0, -size.Y / 2 + size.Y * 0.19f, 0), new Vector3(size.X + 0.006f, size.Y * 0.38f, size.Z + 0.006f)));   // thick, so it reads as a layer, not a seam
        var bedMesh = Merged(beds, Shapes.Mat(earth.Lerp(new Color(0.52f, 0.48f, 0.45f), 0.35f), roughness: 1f, outline: false));   // greyer, as light as the earth
        MarkScenery(bedMesh);
        AddChild(bedMesh);
        // stones in the stony bed: scattered over the two side blocks' front faces (the cut), half sunk, of mixed sizes
        var rng = new RandomNumberGenerator { Seed = Fnv(seed) };
        var stone = Shapes.Mat(Skins.ColorOf("basalt").Lightened(0.35f), roughness: 0.9f);
        foreach (var (c, size) in blocks.Where(b => Mathf.Abs(b.Centre.X - at.X) > 1e-3f))
            for (int k = 0; k < 5; k++)
            {
                float r = Mathf.Clamp(Mathf.Min(size.X, size.Y) * rng.RandfRange(0.025f, 0.055f), 0.008f, 0.03f);
                var pebble = new MeshInstance3D { Mesh = BlockLooks.Truncated(new Vector3(r * rng.RandfRange(1.8f, 2.6f), r * rng.RandfRange(1.3f, 1.8f), r * 2f), _ => r * 0.6f), MaterialOverride = stone };
                pebble.Position = c + new Vector3(size.X * rng.RandfRange(-0.4f, 0.4f), -size.Y / 2 + size.Y * rng.RandfRange(0.06f, 0.34f), size.Z / 2 - r * 0.2f);
                pebble.Rotation = new Vector3(0, 0, rng.RandfRange(-0.6f, 0.6f));
                MarkScenery(pebble);
                AddChild(pebble);
            }
        // timber props: a post at each front corner of the room, a lintel over them, a beam across the back
        float p = Mathf.Max(0.025f, w * 0.06f);
        var timber = new List<(Vector3, Vector3)>();
        foreach (float x in new[] { -1f, 1f })
            timber.Add((at + new Vector3(x * (w / 2 - p / 2), h / 2, d / 2 - p / 2), new Vector3(p, h, p)));
        timber.Add((at + new Vector3(0, h - p / 2, d / 2 - p / 2), new Vector3(w, p, p)));
        timber.Add((at + new Vector3(0, h - p / 2, -d / 2 + p / 2 + 0.011f), new Vector3(w - 0.022f, p, p)));
        var props = Merged(timber, Shapes.Mat(Skins.ColorOf("oak").Darkened(0.25f), roughness: 0.9f));
        MarkScenery(props);
        AddChild(props);
    }

    /// <summary>A vault's hatch on the ground over it, shown while the room is under the surface (see <see cref="ShowHatch"/>).</summary>
    private sealed record Hatch(Node3D Root, StandardMaterial3D Mouth, float Top);

    /// <summary>
    /// A hatch for a room <paramref name="w"/> by <paramref name="d"/> (12.21): a timber collar round a square shaft on the ground, its
    /// mouth a flat sheet washed with the room's air temperature, a ladder's two rails and top rungs standing out of it, and the trapdoor
    /// thrown back against the collar. Built at the origin; <see cref="ShowHatch"/> stands it on the ground each frame.
    /// </summary>
    private Hatch BuildHatch(float w, float d, float top)
    {
        var root = new Node3D { Name = "VaultHatch", Visible = false };
        MarkScenery(root);
        AddChild(root);
        float a = Mathf.Clamp(Mathf.Min(w, d) * 0.9f, 0.4f, 0.8f), b = a * 0.12f;
        var wood = Shapes.Mat(Skins.ColorOf("oak").Darkened(0.2f), roughness: 0.9f);
        var collar = new List<(Vector3, Vector3)>();
        foreach (float s in new[] { -1f, 1f })
        {
            collar.Add((new Vector3(0, b / 2, s * (a / 2 + b / 2)), new Vector3(a + 2 * b, b, b)));
            collar.Add((new Vector3(s * (a / 2 + b / 2), b / 2, 0), new Vector3(b, b, a)));
        }
        root.AddChild(Merged(collar, wood));
        var mouthMat = Shapes.Mat(new Color(0.42f, 0.4f, 0.4f), roughness: 1f, outline: false);
        var mouth = Shapes.Box(new Vector3(a, 0.004f, a), mouthMat);
        mouth.Position = new Vector3(0, 0.01f, 0);
        root.AddChild(mouth);
        // the ladder's top, at the shaft's back
        float rail = Mathf.Max(0.015f, a * 0.04f), lw = a * 0.45f, rise = a * 0.9f;
        var ladder = new List<(Vector3, Vector3)>();
        foreach (float x in new[] { -lw / 2, lw / 2 }) ladder.Add((new Vector3(x, rise / 2, -a / 2 + rail), new Vector3(rail, rise, rail)));
        foreach (float y in new[] { 0.3f, 0.65f }) ladder.Add((new Vector3(0, rise * y, -a / 2 + rail), new Vector3(lw, rail * 0.8f, rail * 0.8f)));
        root.AddChild(Merged(ladder, wood));
        // the trapdoor, hinged on the front of the collar and thrown back past upright, onto the ground in front
        var door = new Node3D { Position = new Vector3(0, b, a / 2 + b), Basis = new Basis(Vector3.Right, Mathf.DegToRad(110)) };
        root.AddChild(door);
        var plank = Shapes.Box(new Vector3(a, b * 0.5f, a), Shapes.Mat(Skins.ColorOf("oak"), roughness: 0.9f));
        plank.Position = new Vector3(0, b * 0.25f, -a / 2);
        door.AddChild(plank);
        var iron = Shapes.Mat(Skins.ColorOf("iron").Darkened(0.25f), metallic: 0.5f, roughness: 0.5f);
        var straps = new List<(Vector3, Vector3)>();
        foreach (float x in new[] { -0.3f, 0.3f }) straps.Add((new Vector3(x * a, b * 0.5f + 0.002f, -a / 2), new Vector3(a * 0.08f, 0.004f, a)));
        door.AddChild(Merged(straps, iron));
        return new Hatch(root, mouthMat, top);
    }

    /// <summary>Stands a vault's hatch on the ground over its room's centre (<paramref name="centre"/>, in this view), shown only while the room's top is under the surface there.</summary>
    private void ShowHatch(Hatch hatch, Vector3 centre, double airC)
    {
        if (Ground is not { } g || !IsInsideTree()) { hatch.Root.Visible = false; return; }
        var world = ToGlobal(centre);
        float surface = (float)g.HeightAt(world.X, world.Z);
        bool buried = surface > ToGlobal(centre with { Y = hatch.Top }).Y + 0.05f;
        hatch.Root.Visible = buried;
        if (!buried) return;
        // lying on the slope: its up is the ground's normal there, from the heights 40 cm either side
        float e = 0.4f;
        var normal = new Vector3((float)(g.HeightAt(world.X - e, world.Z) - g.HeightAt(world.X + e, world.Z)), 2 * e,
                                 (float)(g.HeightAt(world.X, world.Z - e) - g.HeightAt(world.X, world.Z + e))).Normalized();
        hatch.Root.Transform = new Transform3D(GlobalBasis.Inverse() * new Basis(new Quaternion(Vector3.Up, normal)), ToLocal(new Vector3(world.X, surface, world.Z)));
        hatch.Mouth.AlbedoColor = Skins.Warmed(new Color(0.42f, 0.4f, 0.4f), airC).Lerp(new Color(0.94f, 0.97f, 1f), Mathf.Clamp(-(float)airC / 40f, 0, 0.6f));
    }

    private static uint Fnv(string text)
    {
        uint hash = 2166136261;   // FNV-1a: the same each run
        foreach (char c in text) hash = (hash ^ c) * 16777619;
        return hash;
    }
}
