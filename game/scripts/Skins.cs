using Godot;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// How a part looks, by rule rather than by hand (docs/art-direction.md). Every solid surface in a
/// machine comes from <see cref="For"/>, so a part nobody has drawn before, in a material nobody has
/// given a look, still comes out right, and parts that were never meant to stand together still sit
/// well side by side. Three layers:
///
/// 1. <b>What it is made of.</b> The material table gives a colour and a finish (grain, cast, wrought,
///    veined, …); a material without a finish takes its category's. The finish is a procedural surface
///    in the part's own space, so it turns with a turning wheel, and its scale is in metres, so oak looks
///    like oak whether it is a clock's arbor or a crane's jib. Roughness stays the material's friction.
/// 2. <b>How it sits with others.</b> One toon shading and one black outline for every solid part, its
///    width fixed in screen pixels, so a millimetre gear and a 10 m arm carry the same line and neighbours
///    never merge. Wood's grain runs along each piece's longest side (<see cref="OrientGrain"/>), as a
///    carpenter would cut it, and no two neighbouring parts are the same shade (<see cref="Vary"/>).
/// 3. <b>What it is doing.</b> Heat shows as the glow of hot iron (<see cref="Glow"/>), strain as a rim
///    that warms to amber and red (<see cref="Rim"/>): one helper each, so every part shows them alike.
/// </summary>
public static class Skins
{
    public enum Finish { Plain, Grain, Cast, Wrought, Polished, Dressed, Veined, Crystalline, Fibre, Granular, Clear }

    private static readonly MaterialLibrary Library = MaterialLibrary.LoadDefault();
    private static readonly HashSet<string> Warned = [];

    /// <summary>The colour of a material: the table's hex, else a grey stone (and one warning).</summary>
    public static Color ColorOf(string materialId)
    {
        if (Library.TryGet(materialId, out var m) && m.Color is { Length: > 0 } hex) return Color.FromHtml(hex);
        if (Warned.Add(materialId)) GD.PushWarning($"material '{materialId}' has no colour in materials.rktd; drawn as stone");
        return Shapes.Stone;
    }

    public static Finish FinishOf(MaterialDef m) =>
        m.Finish is { Length: > 0 } f && Enum.TryParse<Finish>(f, ignoreCase: true, out var finish) ? finish
        : m.Category switch
        {
            MaterialCategory.Wood => Finish.Grain,
            MaterialCategory.Metal => Finish.Cast,
            MaterialCategory.Stone => Finish.Dressed,
            MaterialCategory.Fiber => Finish.Fibre,
            MaterialCategory.Soil => Finish.Granular,
            _ => Finish.Plain,
        };

    /// <summary>
    /// The surface of a part made of <paramref name="m"/>: colour, finish, toon shading, outline. Roughness
    /// follows the real friction coefficient (0.30 bronze to 0.60 granite across the table), so a slippery
    /// surface reads as polished and a grippy one as coarse; metals also get metallic reflectance.
    /// </summary>
    public static StandardMaterial3D For(MaterialDef m)
    {
        float roughness = Mathf.Clamp(0.12f + (float)(m.Friction - 0.30) / 0.30f * 0.83f, 0.1f, 0.95f);
        // metals half-metallic, not 0.8: a studio sky gives a full mirror nothing bright to show, and bronze went
        // as dark as oak beside it. At 0.5 the table's colour carries and the toon highlight still says metal.
        var mat = Shapes.Mat(ColorOf(m.Id), metallic: m.Category == MaterialCategory.Metal ? 0.5f : 0, roughness: roughness);
        ApplyFinish(mat, FinishOf(m));
        mat.SetMeta(MaterialMeta, m.Id);
        return mat;
    }

    // ── layer 1: the finish ─────────────────────────────────────────────────────────────────────────

    /// <summary>How each finish is drawn: a noise, banded through a value ramp, repeated so many times a metre.</summary>
    private sealed record Recipe(FastNoiseLite.NoiseTypeEnum Noise, float Frequency, float[] Ramp, float PerMetre,
                                 bool Directional = false, int Octaves = 3, FastNoiseLite.FractalTypeEnum Fractal = FastNoiseLite.FractalTypeEnum.Fbm,
                                 float RoughnessSpread = 0);

    // Ramps are value multipliers over the noise from 0 to 1, evenly spaced. Variation is kept to a few
    // per cent except where the material really is figured (wood's rings, marble's veins), so state cues
    // and the palette's contrast stay readable over it.
    private static readonly Dictionary<Finish, Recipe> Recipes = new()
    {
        // rings: banding the noise gives contour lines, which along a stretched axis read as grain
        [Finish.Grain] = new(FastNoiseLite.NoiseTypeEnum.Perlin, 0.012f, [0.86f, 1f, 0.9f, 1f, 0.84f, 0.98f, 0.9f, 1f], 6, Directional: true),
        // sand-cast bronze: soft mottling, and duller where it is rougher
        [Finish.Cast] = new(FastNoiseLite.NoiseTypeEnum.SimplexSmooth, 0.01f, [0.88f, 0.96f, 1f, 0.97f], 8, RoughnessSpread: 0.25f),
        // wrought iron is fibrous: slag drawn out into fine lines along the bar
        [Finish.Wrought] = new(FastNoiseLite.NoiseTypeEnum.Perlin, 0.03f, [0.82f, 1f, 0.9f, 1f], 10, Directional: true, RoughnessSpread: 0.2f),
        [Finish.Polished] = new(FastNoiseLite.NoiseTypeEnum.SimplexSmooth, 0.006f, [0.96f, 1f], 4, RoughnessSpread: 0.08f),
        // tool-dressed stone: a fine even speckle
        [Finish.Dressed] = new(FastNoiseLite.NoiseTypeEnum.Value, 0.25f, [0.9f, 1f, 0.95f, 1f], 6, Octaves: 2),
        [Finish.Veined] = new(FastNoiseLite.NoiseTypeEnum.Perlin, 0.008f, [1f, 1f, 0.97f, 0.78f, 0.97f, 1f, 1f], 3, Octaves: 4, Fractal: FastNoiseLite.FractalTypeEnum.Ridged),
        // granite's crystals: cells of different shade
        [Finish.Crystalline] = new(FastNoiseLite.NoiseTypeEnum.Cellular, 0.08f, [0.74f, 0.96f, 0.86f, 1f], 10, Octaves: 1),
        // twisted fibres: fine lines along the strand
        [Finish.Fibre] = new(FastNoiseLite.NoiseTypeEnum.Perlin, 0.06f, [0.78f, 1f, 0.86f, 1f], 30, Directional: true),
        [Finish.Granular] = new(FastNoiseLite.NoiseTypeEnum.Value, 0.4f, [0.85f, 1f, 0.92f], 12, Octaves: 1),
    };

    private static readonly Dictionary<Finish, (Texture2D Albedo, Texture2D? Roughness)> Textures = [];

    private static void ApplyFinish(StandardMaterial3D mat, Finish finish)
    {
        if (!Recipes.TryGetValue(finish, out var r)) return;
        if (!Textures.TryGetValue(finish, out var tex))
            Textures[finish] = tex = (NoiseTexture(r, r.Ramp), r.RoughnessSpread > 0 ? NoiseTexture(r, [1 - r.RoughnessSpread, 1]) : null);
        mat.AlbedoTexture = tex.Albedo;
        if (tex.Roughness is not null)
        {
            mat.RoughnessTexture = tex.Roughness;
            mat.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
        }
        // projected in the part's own space, so the surface moves with the part and needs no UVs
        mat.Uv1Triplanar = true;
        mat.Uv1TriplanarSharpness = 4;
        mat.Uv1Scale = Vector3.One * r.PerMetre;
        mat.SetMeta(FinishMeta, (int)finish);
    }

    private const string FinishMeta = "skin_finish";
    private const string MaterialMeta = "skin_material";

    private static NoiseTexture2D NoiseTexture(Recipe r, float[] ramp)
    {
        var gradient = new Gradient();
        // Gradient starts with two points; set them, then add the rest
        gradient.SetOffset(0, 0);
        gradient.SetColor(0, Grey(ramp[0]));
        gradient.SetOffset(1, 1);
        gradient.SetColor(1, Grey(ramp[^1]));
        for (int i = 1; i < ramp.Length - 1; i++) gradient.AddPoint(i / (float)(ramp.Length - 1), Grey(ramp[i]));
        return new NoiseTexture2D
        {
            Width = 256,
            Height = 256,
            Seamless = true,
            GenerateMipmaps = true,
            ColorRamp = gradient,
            Noise = new FastNoiseLite
            {
                NoiseType = r.Noise,
                Frequency = r.Frequency,
                FractalOctaves = r.Octaves,
                FractalType = r.Fractal,
                Seed = 1,
                CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.CellValue,
            },
        };
    }

    // ramps only darken (the texture multiplies the colour), so the table's colour is the surface's brightest
    private static Color Grey(float v) => new(v, v, v);

    // ── layer 2: parts together ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The comic-book line (owner decision, art direction §4): pure black, drawn by pushing the part's back
    /// faces outward along their normals. The push is measured in pixels at the part's distance, with a
    /// floor in metres, so every part, a clock's arbor or a trebuchet's arm, near or far, keeps a line of the
    /// same weight. No caller has to know its part's size. Shared by every outlined material.
    /// </summary>
    public static readonly ShaderMaterial Outline = OutlineIn(Colors.Black);

    /// <summary>
    /// The line for a 40-pixel palette thumbnail (#165): at 0.3% of a 96-pixel render the normal line is a third
    /// of a pixel and vanishes, so a thumbnail's parts draw it about 2.5 px wide instead.
    /// </summary>
    public static readonly ShaderMaterial ThumbnailOutline = Wide(OutlineIn(Colors.Black));

    private static ShaderMaterial Wide(ShaderMaterial m) { m.SetShaderParameter("width", 0.026f); return m; }

    /// <summary>Every surface under <paramref name="root"/> that carries the plain line gets <paramref name="outline"/> instead.</summary>
    public static void UseOutline(Node root, ShaderMaterial outline)
    {
        foreach (var mesh in Meshes(root))
        {
            if (mesh.MaterialOverride is not BaseMaterial3D m) continue;
            if (m.NextPass == Outline) m.NextPass = outline;
            else if (m.NextPass is ShaderMaterial { NextPass: var after } mark && mark.HasMeta(MarkMeta) && after == Outline) mark.NextPass = outline;
        }
    }

    private static Shader? _outlineShader;

    private static ShaderMaterial OutlineIn(Color color)
    {
        _outlineShader ??= new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, cull_front, shadows_disabled, depth_draw_opaque;
                uniform vec4 color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
                uniform float width = 0.003;      // line width as a share of the screen's height (2.4 px at 800)
                uniform float least = 0.0003;     // and never under 0.3 mm in the world
                void vertex() {
                    // Out from the part's centre, not along each face's normal: a box's three faces meet at a
                    // corner with three different normals and would split apart there, leaving the line broken.
                    // sign() moves all three the same way, so the hull stays closed. Every generated part is
                    // built around its own centre, which is what makes this work.
                    vec3 out_dir = normalize((MODELVIEW_MATRIX * vec4(sign(VERTEX), 0.0)).xyz);
                    vec4 at = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
                    // An orthographic projection is the sun's shadow pass, where a widened hull would cast a fat
                    // false shadow, so there it keeps the part's own shape; the player's camera is perspective.
                    bool ortho = PROJECTION_MATRIX[3][3] == 1.0;
                    // the screen's height in metres at this depth, times the share the line takes of it; abs(),
                    // since Vulkan and Metal flip y in the projection and [1][1] comes out negative there
                    float span = abs(at.z) * 2.0 / abs(PROJECTION_MATRIX[1][1]);
                    at.xyz += ortho ? vec3(0.0) : out_dir * max(least, width * span);
                    POSITION = PROJECTION_MATRIX * at;
                }
                void fragment() {
                    ALBEDO = color.rgb;
                }
                """,
        };
        var mat = new ShaderMaterial { Shader = _outlineShader };
        mat.SetShaderParameter("color", color);
        return mat;
    }

    /// <summary>
    /// Every mesh under <paramref name="root"/> whose surface has grain gets it along its own longest side,
    /// so a beam's grain runs along the beam and a wheel's across its face, with no builder saying which.
    /// A surface shared by several meshes takes the direction most of them want: it's never copied, since
    /// builders keep a handle on the materials they change at run time.
    /// </summary>
    public static void OrientGrain(Node root)
    {
        var votes = new Dictionary<StandardMaterial3D, (Recipe Recipe, int[] Axes)>();
        foreach (var mesh in Meshes(root))
        {
            if (mesh.MaterialOverride is not StandardMaterial3D mat || !mat.HasMeta(FinishMeta)) continue;
            if (!Recipes.TryGetValue((Finish)(int)mat.GetMeta(FinishMeta), out var r) || !r.Directional) continue;
            var size = mesh.GetAabb().Size;
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            if (!votes.TryGetValue(mat, out var v)) votes[mat] = v = (r, new int[3]);
            v.Axes[axis]++;
        }
        foreach (var (mat, (r, v)) in votes)
        {
            int axis = v[0] >= v[1] && v[0] >= v[2] ? 0 : v[1] >= v[2] ? 1 : 2;
            float across = r.PerMetre, along = r.PerMetre * 0.08f;   // a long, slow streak
            mat.Uv1Scale = new Vector3(axis == 0 ? along : across, axis == 1 ? along : across, axis == 2 ? along : across);
        }
    }

    /// <summary>
    /// Where the physics joins two bodies, draw what would join them in a workshop (issue #102): a hinge gets a
    /// collar through both parts with a cap at each end, a ball joint a ball. The fitting is sized from the thinner
    /// of the two parts (a thicker beam gets a thicker pin), and made of the harder of their materials if either
    /// is a metal, else of iron, as a millwright would. It rides on one of the bodies, so it turns and swings with
    /// it. Only joints the machine declares are fitted: chain links (unnamed pins, one per link), welds and
    /// slides draw themselves. Run once after a view is built.
    /// </summary>
    public static void FitJoints(Node root, Func<string, StandardMaterial3D> surface)
    {
        foreach (var joint in Joints(root).ToList())
        {
            bool hinge = joint is HingeJoint3D;
            bool ball = joint is ConeTwistJoint3D || joint is PinJoint3D && !joint.Name.ToString().StartsWith('@');
            if (!hinge && !ball) continue;
            var a = joint.NodeA.IsEmpty ? null : joint.GetNodeOrNull<Node3D>(joint.NodeA);
            var b = joint.NodeB.IsEmpty ? null : joint.GetNodeOrNull<Node3D>(joint.NodeB);
            var bodies = new[] { a, b }.Where(n => n is not null).Select(n => n!).ToList();
            if (bodies.Count == 0) continue;
            // the thinner part sets the pin: the smallest extent of each body's own meshes
            float thick = bodies.Select(Thickness).Where(t => t > 0).DefaultIfEmpty(0.02f).Min();
            float s = Mathf.Clamp(thick, 0.006f, 0.12f);   // a millwright's pin, not the whole width of a one-piece wheel
            string metal = bodies.Select(MaterialOf).FirstOrDefault(id => id is not null && Library.TryGet(id, out var m) && m.Category == MaterialCategory.Metal) ?? "iron";
            var mat = surface(metal);
            var fitting = new Node3D { Name = $"fitting-{joint.Name}" };
            if (hinge)
            {
                // the hinge turns about the joint's own Z: the collar lies along it, a cap at each end
                var along = new Basis(Vector3.Right, Mathf.Pi / 2);
                var collar = Shapes.Cylinder(s * 0.45f, s * 1.8f, mat);
                collar.Basis = along;
                fitting.AddChild(collar);
                foreach (float end in new[] { -0.9f, 0.9f })
                {
                    var cap = Shapes.Cylinder(s * 0.7f, s * 0.25f, mat);
                    cap.Basis = along;
                    cap.Position = new Vector3(0, 0, end * s);
                    fitting.AddChild(cap);
                }
            }
            else fitting.AddChild(Shapes.Sphere(s * 0.6f, mat));
            var host = bodies[0];
            host.AddChild(fitting);
            fitting.GlobalTransform = joint.GlobalTransform;
            if (OS.GetEnvironment("HEROIC_DEBUG_PHYSICS") == "1")
                GD.Print($"fitting {joint.Name}: {(hinge ? "collar" : "ball")} of {metal}, pin {s * 1000:0} mm, on {host.Name}");
        }
    }

    private static IEnumerable<Joint3D> Joints(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Joint3D j) yield return j;
            foreach (var deeper in Joints(child)) yield return deeper;
        }
    }

    private static float Thickness(Node3D body)
    {
        float least = float.MaxValue;
        foreach (var mesh in Meshes(body))
        {
            if (mesh.MaterialOverride is BaseMaterial3D { Transparency: not BaseMaterial3D.TransparencyEnum.Disabled }) continue;
            if (mesh.GetParent().Name.ToString().StartsWith("fitting-")) continue;   // another joint's fitting, not the part
            var size = mesh.GetAabb().Size * mesh.Scale.Abs();
            least = Mathf.Min(least, Mathf.Min(size.X, Mathf.Min(size.Y, size.Z)));
        }
        return least == float.MaxValue ? 0 : least;
    }

    private static string? MaterialOf(Node3D body) =>
        Meshes(body).Select(m => m.MaterialOverride).OfType<StandardMaterial3D>()
            .Where(m => m.HasMeta(MaterialMeta)).Select(m => (string)m.GetMeta(MaterialMeta)).FirstOrDefault();

    /// <summary>
    /// Frame and moving parts (#167): a machine reads by what moves, so its standing structure recedes. Every mesh that
    /// rides on no rigid body (posts, frames, supports, footings, fixtures) and is made of wood or stone draws a little
    /// darker and greyer; what moves or carries the load (arms, wheels, beams, the stone) keeps its full colour. Metal,
    /// glass, water and anything glowing are left alone: they're working parts or carry state cues. A material used by
    /// both still and moving meshes is left alone too, and none is copied: builders keep handles on the materials they
    /// change at run time. Run once after a view is built.
    /// </summary>
    public static void RecedeStructure(Node root)
    {
        var users = new Dictionary<StandardMaterial3D, (bool Still, bool Moving)>();
        foreach (var mesh in Meshes(root))
        {
            if (mesh.MaterialOverride is not StandardMaterial3D mat || !mat.HasMeta(FinishMeta)) continue;
            bool moving = false;
            for (Node? n = mesh.GetParent(); n is not null && n != root; n = n.GetParent())
                if (n is RigidBody3D) { moving = true; break; }
            var u = users.GetValueOrDefault(mat);
            users[mat] = moving ? (u.Still, true) : (true, u.Moving);
        }
        foreach (var (mat, u) in users)
        {
            if (!u.Still || u.Moving) continue;
            if (mat.Transparency != BaseMaterial3D.TransparencyEnum.Disabled || mat.EmissionEnabled) continue;
            if ((Finish)(int)mat.GetMeta(FinishMeta) is not (Finish.Grain or Finish.Dressed or Finish.Crystalline or Finish.Veined)) continue;
            var c = mat.AlbedoColor;
            mat.AlbedoColor = Color.FromHsv(c.H, c.S * 0.7f, c.V * 0.8f, c.A);
        }
    }

    /// <summary>
    /// Labels a constant size on screen (#148): a label's size was set in metres, so a close-up (the aeolipile's
    /// "kettle") wrote it as a headline across the machine and a wide shot (a crane) shrank it to specks. Every
    /// Label3D under <paramref name="root"/> keeps its font size, so a heading stays bigger than a tag, but is drawn
    /// at a fixed scale on screen. Run after a view is built; a view also applies it to labels added while it runs.
    /// </summary>
    public static void ScreenLabels(Node root)
    {
        foreach (var node in root.FindChildren("*", "Label3D", true, false))
            if (node is Label3D l && !l.FixedSize) ScreenLabel(l);
    }

    public static void ScreenLabel(Label3D label)
    {
        label.FixedSize = true;
        label.PixelSize = 0.0011f;
        // a heading may stay a little bigger than a tag, but none shouts: readouts written for a far camera used 48 pt
        label.FontSize = Math.Min(label.FontSize, 28);
        label.OutlineSize = Math.Max(label.OutlineSize, 8);
        // a long readout wraps into a short block instead of running into its neighbour's
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.Width = 220;
    }

    /// <summary>
    /// A part's own shade: its brightness nudged up to ±6%, fixed by its name so it's the same every run
    /// (real castings and timbers vary that much anyway), so two neighbouring parts of one material don't
    /// merge into one shape.
    /// </summary>
    public static void Vary(StandardMaterial3D mat, string partId)
    {
        uint hash = 2166136261; // FNV-1a: string.GetHashCode changes from run to run
        foreach (char c in partId) hash = (hash ^ c) * 16777619;
        float shade = 0.94f + 0.12f * (hash % 1000) / 999f;
        var c0 = mat.AlbedoColor;
        mat.AlbedoColor = new Color(c0.R * shade, c0.G * shade, c0.B * shade, c0.A);
    }

    private static IEnumerable<MeshInstance3D> Meshes(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is MeshInstance3D m) yield return m;
            foreach (var deeper in Meshes(child)) yield return deeper;
        }
    }

    // ── layer 3: what it is doing ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The light hot things give off: dark until about 500 °C, then a dull red, orange and at last
    /// yellow-white by 1,500 °C. The one ramp for every hot part, so a crucible, a firebox and a heater
    /// all glow alike at the same heat.
    /// </summary>
    public static (Color Color, float Energy) Incandescence(double celsius)
    {
        float t = Mathf.Clamp(((float)celsius - 500) / 1000, 0, 1);
        var color = new Color(0.8f, 0.1f, 0.02f).Lerp(new Color(1f, 0.55f, 0.15f), Mathf.Min(1, t * 2)).Lerp(new Color(1f, 0.95f, 0.8f), Mathf.Max(0, t * 2 - 1));
        return (color, t * t * 4);
    }

    /// <summary>Makes <paramref name="mat"/> glow as hot as <paramref name="celsius"/>; cool, it shows nothing.</summary>
    public static void Glow(StandardMaterial3D mat, double celsius)
    {
        var (color, energy) = Incandescence(celsius);
        mat.EmissionEnabled = energy > 0;
        mat.Emission = color;
        mat.EmissionEnergyMultiplier = energy;
    }

    /// <summary>
    /// Strain as the outline's colour (art direction §4.7): black at rest, warming through amber to red as
    /// <paramref name="share"/> of the part's limit goes from 0.6 to 1. Below 0.6 it is the plain line.
    /// The only state allowed to touch the outline, so the line means one thing everywhere.
    /// </summary>
    public static void Rim(StandardMaterial3D mat, double share)
    {
        float t = Mathf.Clamp(((float)share - RimFrom) / (1 - RimFrom), 0, 1);
        bool seeThrough = mat.Transparency != BaseMaterial3D.TransparencyEnum.Disabled;
        // at rest: the plain line, or none on glass (a hull behind glass would show through it)
        if (t <= 0) { mat.NextPass = seeThrough ? null : Outline; return; }
        var color = new Color(1f, 0.7f, 0.1f).Lerp(new Color(0.9f, 0.05f, 0.02f), t);
        if (mat.NextPass == Outline || mat.NextPass is not ShaderMaterial own) mat.NextPass = own = OutlineIn(color);
        own.SetShaderParameter("color", color);
        own.SetShaderParameter("width", 0.003f * (1.5f + 2f * t));   // and thickens as it reddens, to 3.5 times the plain line
    }

    // ── layer 3: warmth (#169) ──────────────────────────────────────────────────────────────────────

    private const string WarmBaseMeta = "skin_warm_base";

    /// <summary>
    /// The warmth tint: what colour, and how much of it, a part at <paramref name="celsius"/> is washed with. Below 0 °C a
    /// cool blue-grey, at 20 °C nothing (the material's own colour), a warm ochre by 100 °C, then through to a dull red
    /// by 400 °C, held there: at 500 °C the incandescence ramp (<see cref="Incandescence"/>) starts from that same dull
    /// red at zero energy, so the two join without a step. Never more than a wash (0.65), so the material still reads.
    /// </summary>
    public static (Color Tint, float Share) WarmthTint(double celsius)
    {
        float c = (float)celsius;
        if (c < 20) return (new Color(0.50f, 0.62f, 0.82f), 0.5f * Mathf.Clamp((20 - c) / 20f, 0, 1));
        // a burnt ochre, deeper than bronze and copper themselves so a metal's warming shows on it too
        var ochre = new Color(0.88f, 0.46f, 0.07f);
        if (c <= 100) return (ochre, 0.55f * (c - 20) / 80f);
        float t = Mathf.Clamp((c - 100) / 300f, 0, 1);
        return (ochre.Lerp(new Color(0.72f, 0.10f, 0.04f), t), 0.55f + 0.1f * t);
    }

    /// <summary>A colour washed with the warmth tint of <paramref name="celsius"/>; its alpha is kept.</summary>
    public static Color Warmed(Color own, double celsius)
    {
        var (tint, share) = WarmthTint(celsius);
        return new Color(own.Lerp(tint, share), own.A);
    }

    /// <summary>
    /// Shows <paramref name="mat"/>'s part as warm or cold as <paramref name="celsius"/>. Works on the albedo, from the colour the
    /// material had when first asked (so a part's own shade survives), and leaves emission (<see cref="Glow"/>) and the outline
    /// (<see cref="Rim"/>) alone. For a surface its builder does not recolour every frame; one that is (a room's skin)
    /// folds <see cref="Warmed"/> into its own colour instead.
    /// </summary>
    public static void Warm(StandardMaterial3D mat, double celsius)
    {
        if (!mat.HasMeta(WarmBaseMeta)) mat.SetMeta(WarmBaseMeta, mat.AlbedoColor);
        mat.AlbedoColor = Warmed((Color)mat.GetMeta(WarmBaseMeta), celsius);
    }

    // ── layer 3: turning (#172) ─────────────────────────────────────────────────────────────────────

    /// <summary>The speed above which a stripe aliases at 60 frames a second, in turns a second (900 rpm).</summary>
    public const double BlurFrom = 15;

    private const string MarkMeta = "skin_turn_mark";
    private static Shader? _markShader;

    /// <summary>A turning part's mark: a stripe when slow, a blurred ring when the stripe would alias.</summary>
    public sealed class TurnMark(ShaderMaterial pass)
    {
        /// <summary>The stripe's opacity now (1 up to 15 turns a second, fading to 0 by 30).</summary>
        public float StripeAlpha { get; private set; } = 1;
        /// <summary>The ring's opacity now: 0 up to 15 turns a second, then 25% growing to 75% by 30.</summary>
        public float RingAlpha { get; private set; }

        public void Spin(double turnsPerSecond)
        {
            double f = Math.Abs(turnsPerSecond);
            float k = (float)Math.Clamp((f - BlurFrom) / BlurFrom, 0, 1);
            float stripe = 1 - k, ring = f > BlurFrom ? 0.25f + 0.5f * k : 0;
            if (stripe != StripeAlpha) { StripeAlpha = stripe; pass.SetShaderParameter("stripe", stripe); }
            if (ring != RingAlpha) { RingAlpha = ring; pass.SetShaderParameter("ring", ring); }
        }
    }

    /// <summary>
    /// Everything that turns gets a mark (#172), painted on and not built: a stripe from hub to rim across each face of a wheel,
    /// disc, drum or pulley, along a shaft, and over a ball, in contrast to the part's own colour (dark on a light surface, pale on
    /// a dark one). It is a transparent pass added after the part's own surface and before its outline, so the surface is never
    /// replaced. <paramref name="axis"/> is the part's turning axis in <paramref name="mesh"/>'s own space. The surface must be this
    /// mesh's alone: a pass on a shared material would paint every mesh that uses it.
    /// </summary>
    public static TurnMark? MarkTurning(MeshInstance3D mesh, Vector3 axis)
    {
        if (mesh.MaterialOverride is not BaseMaterial3D mat || mesh.Mesh is null) return null;
        axis = axis.Normalized();
        // the part's radius about the axis: the farthest corner of its box
        var box = mesh.GetAabb();
        float radius = 0.001f;
        for (int i = 0; i < 8; i++)
        {
            var p = box.Position + new Vector3((i & 1) * box.Size.X, ((i >> 1) & 1) * box.Size.Y, ((i >> 2) & 1) * box.Size.Z);
            radius = Mathf.Max(radius, (p - axis * p.Dot(axis)).Length());
        }
        // any direction square to the axis will do for where the stripe starts
        var across = Mathf.Abs(axis.Dot(Vector3.Up)) > 0.9f ? Vector3.Right : Vector3.Up;
        var start = (across - axis * across.Dot(axis)).Normalized();
        var c = mat.AlbedoColor;
        bool lightSurface = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B > 0.4f;
        _markShader ??= new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled;
                uniform vec3 axis;
                uniform vec3 start;
                uniform float radius = 0.1;
                uniform vec4 colour : source_color = vec4(0.08, 0.07, 0.06, 1.0);
                uniform float stripe = 1.0;   // the stripe's opacity: fades as the ring comes up
                uniform float ring = 0.0;     // the blur ring's opacity: 0 until the stripe would alias
                varying vec3 lp;
                varying vec3 ln;
                void vertex() { lp = VERTEX; ln = NORMAL; }
                void fragment() {
                    float along = dot(lp, axis);
                    vec3 q = lp - axis * along;
                    float r = length(q);
                    float a = 0.0;
                    // the stripe: a strip a tenth of the radius either side of one half-plane through the axis,
                    // so on a face it runs hub to rim and on a shaft's side it runs along it
                    float x = dot(q, start);
                    float y = dot(q, cross(axis, start));
                    if (x > 0.0 && abs(y) < 0.1 * radius && r > 0.08 * radius) a = stripe;
                    // the ring: the stripe smeared all the way round; on a face a band over its middle,
                    // on a side (a rim, a shaft) all of it
                    bool face = abs(dot(normalize(ln), axis)) > 0.7;
                    if (face ? (r > 0.4 * radius && r < 0.92 * radius) : (r > 0.6 * radius)) a = max(a, ring);
                    if (a <= 0.0) discard;
                    ALBEDO = colour.rgb;
                    ALPHA = a;
                }
                """,
        };
        var pass = new ShaderMaterial { Shader = _markShader, NextPass = mat.NextPass };
        pass.SetShaderParameter("axis", axis);
        pass.SetShaderParameter("start", start);
        pass.SetShaderParameter("radius", radius);
        pass.SetShaderParameter("colour", lightSurface ? new Color(0.08f, 0.07f, 0.06f) : new Color(0.97f, 0.94f, 0.82f));
        pass.SetMeta(MarkMeta, true);
        mat.NextPass = pass;
        return new TurnMark(pass);
    }

    // ── layer 2b: made things (#102): detail rules, one per feature ─────────────────────────────────

    /// <summary>
    /// Iron hoops round a tank, a few to a tank by its height (about one a side-and-a-bit, one to four): a glass
    /// box with a frame is a vessel, and hoops say it is a built one. Four flat strips make a ring; the ring is a
    /// child of the shell, so a hung tank carries it. <paramref name="iron"/> is the hoops' own material.
    /// </summary>
    private static void Add(Node3D parent, string name, Vector3 size, Vector3 at, StandardMaterial3D mat)
    {
        var box = Shapes.Box(size, mat);
        box.Name = name;
        box.Position = at;
        parent.AddChild(box);
    }

    public static void HoopTank(Node3D shell, float side, float height, StandardMaterial3D iron, bool openFront = false)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(height / (side * 0.9f)), 1, 4);
        float d = Mathf.Clamp(side * 0.02f, 0.004f, 0.015f), band = Mathf.Clamp(side * 0.07f, 0.01f, 0.07f), hs = side / 2;
        for (int i = 1; i <= n; i++)
        {
            float y = -height / 2 + height * i / (n + 1);
            foreach (float sign in new[] { -1f, 1f })
            {
                if (!openFront || sign < 0)   // a cut-away vessel (CutAway) has no hoop across its open front, the +Z face
                    Add(shell, "hoop", new Vector3(side + 2 * d, band, 2 * d), new Vector3(0, y, sign * hs), iron);
                Add(shell, "hoop", new Vector3(2 * d, band * 0.98f, side), new Vector3(sign * hs, y, 0), iron);   // a hair slimmer: no coplanar tops
            }
        }
    }

    /// <summary>
    /// Bands round a boiler: one near each end and a few between, spaced by the height against the radius (a tall
    /// thin boiler gets more), each a short fat disc a little wider than the shell, so it stands out as a ring.
    /// </summary>
    public static void BandBoiler(MeshInstance3D body, float radius, float height, StandardMaterial3D band)
    {
        int inner = Mathf.Clamp(Mathf.RoundToInt(height / (radius * 1.4f)) - 1, 1, 4);
        float wide = radius + Mathf.Clamp(radius * 0.05f, 0.006f, 0.03f), thick = Mathf.Clamp(height * 0.04f, 0.01f, 0.06f);
        var shares = new List<float> { 0.06f, 0.94f };
        for (int i = 1; i <= inner; i++) shares.Add(0.06f + 0.88f * i / (inner + 1));
        foreach (float share in shares)
        {
            var ring = Shapes.Cylinder(wide, thick, band);
            ring.Name = "band";
            ring.Position = new Vector3(0, -height / 2 + height * share, 0);
            body.AddChild(ring);
        }
    }

    /// <summary>
    /// A solid disc wheel given a made look: a rim bead round each face and a hub boss, of the wheel's own
    /// material (the outline gives them their edge). Not spokes: a disc wheel is solid on purpose (the carts demo
    /// compares it with a spoked one), and generated spoked wheels already have theirs. The axle is local Z.
    /// </summary>
    public static void RimWheel(Node3D wheel, float radius, float width, StandardMaterial3D mat)
    {
        float relief = Mathf.Clamp(radius * 0.05f, 0.002f, 0.02f);
        var hub = Shapes.Cylinder(radius * 0.17f, width + relief * 3, mat);
        hub.Name = "hub";
        hub.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
        wheel.AddChild(hub);
        foreach (float side in new[] { 1f, -1f })
            wheel.AddChild(new MeshInstance3D
            {
                Name = "rim",
                Mesh = new TorusMesh { InnerRadius = radius * 0.86f, OuterRadius = radius * 1.01f, Rings = 24, RingSegments = 8 },
                MaterialOverride = mat,
                Position = new Vector3(0, 0, side * (width / 2 + relief * 0.4f)),
                Rotation = new Vector3(Mathf.Pi / 2, 0, 0),
            });
    }

    /// <summary>
    /// Plank seams on wooden boxes: a board wider than about 0.3 m is laid from planks, so dark seams run along
    /// its length, one plank to about 0.2 m of its width, on its two broad faces. Narrow stock (a beam, a post)
    /// is one piece and gets none. Only a box of a wooden material, so a crate, a platform and a cart bed
    /// qualify and a water box doesn't. Run once after a view is built.
    /// </summary>
    public static void PlankSeams(Node root)
    {
        foreach (var mesh in Meshes(root).ToList())
        {
            if (mesh.Mesh is not BoxMesh box || mesh.MaterialOverride is not StandardMaterial3D m || !m.HasMeta(MaterialMeta)) continue;
            if (!Library.TryGet((string)m.GetMeta(MaterialMeta), out var def) || def.Category != MaterialCategory.Wood) continue;
            if (mesh.Name.ToString().StartsWith("seam") || !mesh.Scale.IsEqualApprox(Vector3.One)) continue;
            var size = new[] { box.Size.X, box.Size.Y, box.Size.Z };
            var order = new[] { 0, 1, 2 }.OrderByDescending(i => size[i]).ToArray();   // long, wide, thin; a cube's ties go by axis
            int along = order[0], across = order[1], thin = order[2];
            float length = size[along], wide = size[across], depth = size[thin];
            if (wide < 0.3f || depth < 0.04f) continue;
            int planks = Mathf.Clamp(Mathf.RoundToInt(wide / 0.2f), 2, 6);
            var dark = m.AlbedoColor * 0.38f;
            var seamMat = Shapes.Mat(new Color(dark.R, dark.G, dark.B), roughness: 0.9f, outline: false);
            float seamWidth = Mathf.Clamp(wide * 0.014f, 0.004f, 0.012f), lift = 0.0012f;
            for (int i = 1; i < planks; i++)
                foreach (float face in new[] { -1f, 1f })
                {
                    var dims = Vector3.Zero; var at = Vector3.Zero;
                    dims[along] = length * 0.995f; dims[across] = seamWidth; dims[thin] = lift * 2;
                    at[across] = -wide / 2 + wide * i / planks; at[thin] = face * (depth / 2);
                    var seam = Shapes.Box(dims, seamMat);
                    seam.Name = "seam";
                    seam.Position = at;
                    mesh.AddChild(seam);
                }
        }
    }

    /// <summary>
    /// The hover highlight (#152): a cool cyan hull, wider than the plain line, laid over a mesh as its
    /// <c>MaterialOverlay</c>. An overlay is the mesh's own, so no builder's material is touched (the shared-material
    /// hazard of #151), and it is never the strain rim's amber or red.
    /// </summary>
    public static readonly ShaderMaterial HoverLine = HoverLineMaterial();

    private static ShaderMaterial HoverLineMaterial()
    {
        var m = OutlineIn(new Color(0.35f, 0.9f, 1f));
        m.SetShaderParameter("width", 0.0075f);
        return m;
    }

    // ── pressure you can see, and scales on vessels (#170, #174) ─────────────────────────────────────

    /// <summary>Where the strain rim starts (<see cref="Rim"/>): 60% of a limit. A dial's amber mark stands here.</summary>
    public const float RimFrom = 0.6f;

    /// <summary>The trigger's amber: a mark where something watches a level, and the dial's 60% mark where the strain rim begins.</summary>
    public static readonly Color Watch = new(1f, 0.75f, 0.2f);
    /// <summary>
    /// Air you can see (MachineView.Stacks): the tint of the air shut in over a sealed vessel's water, and of the tube
    /// that joins sealed vessels. A pale warm cream, the one hue in the machine that is neither water blue nor metal, so
    /// it reads as the gas; it thickens with the pressure it is squeezed to (<see cref="AirTint"/>).
    /// </summary>
    public static readonly Color Air = new(0.98f, 0.86f, 0.55f);

    /// <summary>
    /// The head markers' colour (MachineView.Stacks): the "falls h" and "lifts about h" brackets, drawn in the same
    /// deep magenta so a pair reads as one rule. Chosen away from water blue, bronze, amber (<see cref="Watch"/>) and ink.
    /// </summary>
    public static readonly Color Head = new(0.80f, 0.10f, 0.45f);

    /// <summary>The opacity of the air tint over a sealed vessel's headspace: faint at no pressure, thicker as it is squeezed (6 kPa gauge reads about 0.43), saturating near 0.6.</summary>
    public static float AirTint(double gaugePa) => 0.18f + 0.42f * (1f - (float)Math.Exp(-Math.Max(0, gaugePa) / 6000));

    /// <summary>Ink for scale marks; the dial's below-ambient needle is <see cref="Vacuum"/> blue.</summary>
    public static readonly Color Ink = new(0.1f, 0.08f, 0.07f), Vacuum = new(0.2f, 0.45f, 0.95f);

    /// <summary>
    /// The one dial rule: a needle at <paramref name="share"/> of the scale (0 to 1) sits 270 degrees round, from lower
    /// left over the top to lower right, as a rotation about the dial's axis. A share below zero (under the air outside)
    /// swings it the other way from zero, a quarter turn at a full vacuum (share -1), and the caller colours it blue.
    /// </summary>
    public static float GaugeAngle(double share) =>
        share >= 0 ? 135f - 270f * (float)Math.Min(share, 1) : 135f + 90f * (float)Math.Min(-share, 1);

    /// <summary>
    /// How a membrane room stands for its gauge pressure (Pa) over the air outside: slack and nearly flat at nothing,
    /// full height by 1 kPa, then billowing out up to 8% wider and taller as it fills (saturating by about 30 kPa).
    /// Returns the height scale and the width scale.
    /// </summary>
    public static (float Height, float Width) Billow(double gauge)
    {
        if (gauge <= 1000) return (0.12f + 0.88f * (float)Math.Clamp(gauge / 1000, 0, 1), 1f);
        float b = 0.08f * (1 - (float)Math.Exp(-(gauge - 1000) / 10_000));
        return (1 + b, 1 + b);
    }

    /// <summary>
    /// How fast a gas leaves a hole for a pressure difference (Pa): the speed grows as the square root of it (Bernoulli)
    /// and is held at 1 atmosphere's (choked) by the hiss, from a gentle 0.4 m/s drift to 3 m/s. The one rule for a
    /// room's leak, a door's rush and a safety valve's plume, so a harder push always throws farther.
    /// </summary>
    public static float JetSpeed(double deltaPa) => 0.4f + 2.6f * (float)Math.Sqrt(Math.Clamp(Math.Abs(deltaPa) / 101_325, 0, 1));

    /// <summary>The smallest 1-2-5 step (times a power of ten) at which <paramref name="range"/> holds no more than <paramref name="most"/> marks.</summary>
    public static double NiceStep(double range, int most)
    {
        if (range <= 0) return 1;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(range / most)));
        foreach (double m in new[] { 1, 2, 5, 10 })
            if (range / (m * mag) <= most + 1e-9) return m * mag;
        return 10 * mag;
    }

    /// <summary>One merged mesh of box bars in a single flat colour with its own unlit material: a vessel's marks, a dial's ticks.</summary>
    public static MeshInstance3D Bars(IEnumerable<(Vector3 Centre, Vector3 Size)> bars, Color color)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (c, size) in bars)
            st.AppendFrom(new BoxMesh { Size = size }, 0, new Transform3D(Basis.Identity, c));
        var mat = Shapes.Mat(color, roughness: 1, outline: false);
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    // ---- Wave 3: water you can see moving, and what a machine produces (#171, #173) -------------------------------

    /// <summary>
    /// The one water-flow look: bright dashes riding a pipe, advanced from the sim's own clock by flow over the bore's
    /// cross-section. Each pipe gets its own instance (it holds that pipe's length and phase); dashes fade into an even
    /// band above about 15 a second, where a dash would alias at 60 frames a second (the same rule as the turn marks).
    /// </summary>
    public static ShaderMaterial FlowDashes(float length, float pitch)
    {
        var shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, cull_back, depth_draw_never;
                uniform vec3 tint : source_color = vec3(0.66, 0.90, 1.0);
                uniform float length = 1.0;
                uniform float pitch = 0.15;
                uniform float phase = 0.0;
                uniform float duty = 0.45;
                uniform float blur = 0.0;
                varying float along;
                void vertex() { along = VERTEX.y + length * 0.5; }
                void fragment() {
                    float u = fract(along / pitch - phase);
                    float dash = u < duty ? 1.0 : 0.0;
                    ALBEDO = tint;
                    ALPHA = mix(dash, duty, blur) * 0.9;
                }
                """,
        };
        var m = new ShaderMaterial { Shader = shader };
        m.SetShaderParameter("length", length);
        m.SetShaderParameter("pitch", pitch);
        return m;
    }
}
