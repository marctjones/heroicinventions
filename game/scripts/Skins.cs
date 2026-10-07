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
        float t = Mathf.Clamp(((float)share - 0.6f) / 0.4f, 0, 1);
        if (t <= 0) { mat.NextPass = Outline; return; }
        var color = new Color(1f, 0.7f, 0.1f).Lerp(new Color(0.9f, 0.05f, 0.02f), t);
        if (mat.NextPass == Outline || mat.NextPass is not ShaderMaterial own) mat.NextPass = own = OutlineIn(color);
        own.SetShaderParameter("color", color);
        own.SetShaderParameter("width", 0.003f * (1 + t));   // and thickens as it reddens
    }
}
