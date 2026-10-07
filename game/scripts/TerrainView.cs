using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// A world's ground drawn and made solid (issue #37). The ground is a mesh
/// through the cells' centre heights, each vertex coloured by its soil and
/// shaded by its slope; Jolt gets the same heights as a HeightMapShape3D,
/// so what is seen is what bodies land on. The water loose on it is a
/// second mesh, a quad over each wet cell at its surface (the corners
/// shared with wet neighbours so a pool reads as one sheet), deeper water
/// darker, rebuilt a few times a second from the solver's depths.
///
/// The boulders a slide leaves (issue #88) are rigid bodies here: cubes of the
/// rock the soil holds, made as the slide makes them and laid on the debris
/// the ground's collision has just been rebuilt to; every tick their poses
/// are written back to the simulation, for the trace and the save.
/// </summary>
public partial class TerrainView : Node3D
{
    public const uint GroundLayer = 1;
    private Terrain _ground = null!;
    private ShallowWater2D _water = null!;
    private MeshInstance3D _waterMesh = null!;
    private double _sinceDrawn;
    private MeshInstance3D _groundMesh = null!;
    private StaticBody3D _body = null!;
    private int _shownVersion;
    private double[] _startHeights = [];   // the ground as it was loaded: what was scoured or laid down since shows (#53)

    private MaterialLibrary? _materials;
    private readonly Dictionary<Boulder, MaterialBlock> _boulders = [];
    private int _bouldersReplaced;

    public void Show(Terrain ground, ShallowWater2D water, MaterialLibrary? materials = null)
    {
        _ground = ground;
        _groundMaterial = null;   // a new map: its own size, cells and wet mask
        _materials = materials;
        _boulders.Clear();
        _bouldersReplaced = ground.BouldersReplaced;
        _water = water;
        _startHeights = (double[])ground.Heights.Clone();
        foreach (var c in GetChildren()) c.QueueFree();
        AddChild(_groundMesh = new MeshInstance3D { Mesh = GroundMesh(), Name = "Ground" });
        AddChild(_body = Collision());
        _shownVersion = ground.Version;
        _waterMesh = new MeshInstance3D
        {
            Name = "Water",
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.15f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        AddChild(_waterMesh);
        DrawWater();
    }

    private Vector3 Centre(int i, int j) => new((float)_ground.CellX(i), (float)_ground.Heights[i + j * _ground.Nx], (float)_ground.CellZ(j));

    private ArrayMesh GroundMesh()
    {
        int nx = _ground.Nx, nz = _ground.Nz, n = nx * nz;
        var light = new Vector3(0.4f, 1, 0.3f).Normalized();
        // Each soil's look worked out once, not once per cell: a little greyer than its table colour, so crates, a
        // rover and machines keep their own colour against what they stand on, each layer keeping its hue so the
        // crater's bands tell apart; ice-cemented ground glints where dry soil is matt.
        var soilLook = _ground.Soils.Select(s =>
        {
            var raw = Shapes.ColorFor(s.Material);
            return (Colour: Color.FromHsv(raw.H, raw.S * 0.85f, raw.V), Roughness: s.Material.Contains("ice") ? 0.3f : 0.95f);
        }).ToArray();
        // The light falls on a smoothed copy of the ground: a slide's scar is cut cell by cell, a stair of 5 m treads,
        // and shaded from the true heights every tread lit up as a step. The mesh's points stay the true heights, so
        // what is seen is still what bodies land on; only the shading is smoothed.
        var soft = Smooth(_ground.Heights.ToArray(), nx, nz);
        Vector3 Soft(int i, int j) => new((float)_ground.CellX(i), (float)soft[i + j * nx], (float)_ground.CellZ(j));
        var vertices = new Vector3[n];
        var normals = new Vector3[n];
        var shades = new Color[n];
        // Each cell's ground colour (roughness in alpha) goes to a texture the shader samples smoothly by position;
        // as vertex colours, anything that changed from cell to cell showed as stair-steps along the mesh's diagonals.
        var colours = new double[4][];
        for (int ch = 0; ch < 4; ch++) colours[ch] = new double[n];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = i + j * nx;
                var dx = Soft(Math.Min(i + 1, nx - 1), j) - Soft(Math.Max(i - 1, 0), j);
                var dz = Soft(i, Math.Min(j + 1, nz - 1)) - Soft(i, Math.Max(j - 1, 0));
                var normal = dz.Cross(dx).Normalized();
                // hillshade, as on a map: lit from one high fixed side whatever the sun, strong enough that every
                // slope reads (readable over realistic; the sun's own light and shadows come on top)
                float shade = 0.45f + 0.55f * Mathf.Max(0, normal.Dot(light));
                var (c, roughness) = soilLook[_ground.Soil[k]];
                if (_ground.Loose[k]) c = c.Lightened(0.18f);   // spoil and slumped ground: loose, paler (#44)
                // ground the water has cut away shows darker and wetter, ground it has laid down paler (#53); a 5 cm change at full strength
                double m = _ground.Heights[k] - _startHeights[k];
                if (Math.Abs(m) > 0.002)
                    c = m < 0 ? c.Darkened(Mathf.Clamp((float)(-m / 0.05), 0, 0.45f)) : c.Lerp(new Color(0.98f, 0.95f, 0.85f), Mathf.Clamp((float)(m / 0.05), 0, 0.6f));
                colours[0][k] = c.R; colours[1][k] = c.G; colours[2][k] = c.B; colours[3][k] = roughness;
                vertices[k] = Centre(i, j);
                normals[k] = normal;
                shades[k] = new Color(shade, shade, shade);
            }
        // The colours are smoothed over their neighbours before they're drawn: soils and the scour and deposit cues
        // change cell by cell (a slide lays its rubble down as its own soil), so their edges are 5 m staircases in the
        // data. Smoothed, an edge reads as the line it is. Two passes soften it over about two cells.
        var bytes = new byte[n * 4];
        for (int ch = 0; ch < 4; ch++)
        {
            var smooth = Smooth(colours[ch], nx, nz);
            for (int k = 0; k < n; k++) bytes[k * 4 + ch] = (byte)Math.Clamp((int)Math.Round(smooth[k] * 255), 0, 255);
        }
        // the mesh in one call from arrays (SurfaceTool's per-vertex calls cost tens of ms on a 170 x 170 crater)
        if (_indices is null || _indices.Length != (nx - 1) * (nz - 1) * 6)
        {
            _indices = new int[(nx - 1) * (nz - 1) * 6];
            int t = 0;
            for (int j = 0; j + 1 < nz; j++)
                for (int i = 0; i + 1 < nx; i++)
                {
                    int a = i + j * nx, b = a + 1, c = a + nx, d = c + 1;
                    _indices[t++] = a; _indices[t++] = b; _indices[t++] = c;
                    _indices[t++] = b; _indices[t++] = d; _indices[t++] = c;
                }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = shades;
        arrays[(int)Mesh.ArrayType.Index] = _indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _groundMaterial ??= GroundMaterial();
        _groundMaterial.SetShaderParameter("cells", ImageTexture.CreateFromImage(Image.CreateFromData(nx, nz, false, Image.Format.Rgba8, bytes)));
        mesh.SurfaceSetMaterial(0, _groundMaterial);
        return mesh;
    }

    private int[]? _indices;

    /// <summary>
    /// Two passes of a 3×3 box blur over a cell field, edges clamped: a staircase edge becomes a smooth one. It uses
    /// <paramref name="field"/> as scratch space, so pass it an array of its own.
    /// </summary>
    private static double[] Smooth(double[] field, int nx, int nz)
    {
        double[] from = field, to = new double[field.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int j = 0; j < nz; j++)
            {
                int jm = Math.Max(j - 1, 0) * nx, j0 = j * nx, jp = Math.Min(j + 1, nz - 1) * nx;
                for (int i = 0; i < nx; i++)
                {
                    int im = Math.Max(i - 1, 0), ip = Math.Min(i + 1, nx - 1);
                    to[j0 + i] = (from[jm + im] + from[jm + i] + from[jm + ip] + from[j0 + im] + from[j0 + i] + from[j0 + ip]
                                  + from[jp + im] + from[jp + i] + from[jp + ip]) / 9;
                }
            }
            (from, to) = (to, from);
        }
        return from;
    }

    private static Shader? _groundShader;
    private ShaderMaterial? _groundMaterial;
    private byte[]? _wetBytes;
    private ImageTexture? _wetTexture;

    /// <summary>
    /// The ground's surface (issue #103; readable over realistic): its soils' colours, toon-lit so a slope facing
    /// the sun steps clearly lighter than one facing away, and contour lines, the map-maker's way to make a shape
    /// legible at any distance. Lines fall every <see cref="ContourInterval"/> metres of height, every fifth one
    /// heavier, their width fixed on screen, and the fine ones fade where they would crowd into a smudge.
    /// </summary>
    private ShaderMaterial GroundMaterial()
    {
        _groundShader ??= new Shader
        {
            Code = """
                shader_type spatial;
                render_mode diffuse_toon, specular_toon, cull_disabled;
                uniform float interval = 1.0;
                uniform sampler2D cells : source_color, filter_linear, repeat_disable;   // a cell's colour, roughness in alpha
                uniform sampler2D wet : filter_linear, repeat_disable;                   // 1 where water stands
                uniform vec2 origin;   // the map's corner (X0, Z0)
                uniform vec2 size;     // and its width and depth
                varying float height;
                varying vec2 at;
                void vertex() {
                    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
                    height = world.y;
                    at = (world.xz - origin) / size;
                }
                float line(float h, float px) {
                    // 1 on a contour, 0 away from it, about px pixels wide whatever the distance
                    float w = fwidth(h);
                    return 1.0 - smoothstep(0.0, w * px, abs(fract(h - 0.5) - 0.5));
                }
                void fragment() {
                    float h = height / interval;
                    // fine lines fade once they come closer than ~6 pixels apart; heavy ones once closer than ~4
                    float fine = line(h, 1.0) * clamp(1.0 - (fwidth(h) - 0.12) / 0.1, 0.0, 1.0);
                    float heavy = line(h / 5.0, 1.6) * clamp(1.0 - (fwidth(h / 5.0) - 0.2) / 0.1, 0.0, 1.0);
                    vec4 cell = texture(cells, at);
                    // wet ground darker and glossy, as wet sand is: water seeping shows before it pools
                    float damp = texture(wet, at).r;
                    vec3 soil = cell.rgb * (1.0 - 0.4 * damp);
                    ALBEDO = soil * COLOR.r * (1.0 - 0.16 * fine - 0.32 * heavy);   // COLOR.r: the hillshade
                    ROUGHNESS = mix(cell.a, 0.25, damp);
                    SPECULAR = 0.5 * (1.0 - ROUGHNESS);
                }
                """,
        };
        var mat = new ShaderMaterial { Shader = _groundShader };
        mat.SetShaderParameter("interval", ContourInterval());
        mat.SetShaderParameter("origin", new Vector2((float)_ground.X0, (float)_ground.Z0));
        mat.SetShaderParameter("size", new Vector2((float)_ground.Width, (float)_ground.Depth));
        _wetBytes = null;
        _wetTexture = ImageTexture.CreateFromImage(Image.CreateEmpty(_ground.Nx, _ground.Nz, false, Image.Format.R8));
        mat.SetShaderParameter("wet", _wetTexture);
        return mat;
    }

    /// <summary>A round contour interval, 1, 2 or 5 times a power of ten, giving about a dozen lines over the map's relief.</summary>
    private float ContourInterval()
    {
        double relief = Math.Max(_ground.Heights.Max() - _ground.Heights.Min(), 0.5);
        double raw = relief / 12, power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double step = raw / power < 1.5 ? 1 : raw / power < 3.5 ? 2 : 5;
        return (float)(step * power);
    }

    /// <summary>
    /// Jolt's height map: Godot's HeightMapShape3D spaces its samples 1 unit
    /// apart centred on its node, x fastest, so it is scaled by the cell and
    /// centred on the grid of cell centres: the same heights the mesh uses.
    /// </summary>
    private StaticBody3D Collision()
    {
        int nx = _ground.Nx, nz = _ground.Nz;
        var data = new float[nx * nz];
        for (int k = 0; k < data.Length; k++) data[k] = (float)_ground.Heights[k];
        var shape = new HeightMapShape3D { MapWidth = nx, MapDepth = nz, MapData = data };
        var body = new StaticBody3D { Name = "GroundBody", CollisionLayer = GroundLayer, CollisionMask = 0 };
        float cell = (float)_ground.Cell;
        var centre = new Vector3((float)(_ground.X0 + _ground.Width / 2), 0, (float)(_ground.Z0 + _ground.Depth / 2));
        body.AddChild(new CollisionShape3D
        {
            Shape = shape,
            Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(cell, 1, cell)), centre),
        });
        body.PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.6f, Bounce = 0.1f };
        return body;
    }

    /// <summary>Redraws the water after a tick, five times a second; makes new boulders and reads back where the others are, every tick.</summary>
    public void Refresh(double dt)
    {
        Boulders();
        if ((_sinceDrawn += dt) < 0.2) return;
        _sinceDrawn = 0;
        Reshape();
        DrawWater();
    }

    private void Reshape()
    {
        if (_ground.Version == _shownVersion) return;
        // dug or heaped (issue #44): the ground's shape and its collision change with it
        _shownVersion = _ground.Version;
        _groundMesh.Mesh = GroundMesh();
        _body.QueueFree();
        AddChild(_body = Collision());
    }

    /// <summary>
    /// The boulders (issue #88): a body for each one the ground has that has none yet, laid where the slide put it
    /// (the ground reshaped first, so it lies on the debris and not inside the cliff that was); then every body's pose
    /// and motion written back to its boulder. A save's boulders, loaded, replace the bodies there were.
    /// </summary>
    private void Boulders()
    {
        if (_ground.BouldersReplaced != _bouldersReplaced)
        {
            _bouldersReplaced = _ground.BouldersReplaced;
            foreach (var body in _boulders.Values) body.QueueFree();
            _boulders.Clear();
        }
        if (_boulders.Count < _ground.Boulders.Count && _materials is not null)
        {
            Reshape();
            foreach (var b in _ground.Boulders.Where(b => !_boulders.ContainsKey(b)))
            {
                var body = new MaterialBlock(_materials[b.Material], Vector3.One * (float)b.Size, Shapes.ColorFor(b.Material)) { Name = b.Id };
                body.Transform = new Transform3D(new Basis(new Quaternion((float)b.Qx, (float)b.Qy, (float)b.Qz, (float)b.Qw).Normalized()),
                                                 new Vector3((float)b.X, (float)b.Y, (float)b.Z));
                body.LinearVelocity = new Vector3((float)b.Vx, (float)b.Vy, (float)b.Vz);
                body.AngularVelocity = new Vector3((float)b.Wx, (float)b.Wy, (float)b.Wz);
                AddChild(body);
                _boulders[b] = body;
            }
            return;   // read back from the next tick on, once the engine has them
        }
        foreach (var (b, body) in _boulders)
        {
            var t = body.GlobalTransform;
            var q = t.Basis.GetRotationQuaternion();
            (b.X, b.Y, b.Z) = (t.Origin.X, t.Origin.Y, t.Origin.Z);
            (b.Qx, b.Qy, b.Qz, b.Qw) = (q.X, q.Y, q.Z, q.W);
            (b.Vx, b.Vy, b.Vz) = (body.LinearVelocity.X, body.LinearVelocity.Y, body.LinearVelocity.Z);
            (b.Wx, b.Wy, b.Wz) = (body.AngularVelocity.X, body.AngularVelocity.Y, body.AngularVelocity.Z);
        }
    }

    private void DrawWater()
    {
        int nx = _ground.Nx, nz = _ground.Nz;
        var depths = _water.Depths;
        // on the ground mesh's own grid (cell centres), each point raised by its
        // cell's water; a dry point sits just under the ground, so a wet
        // patch's edge dips out of sight where the water ends
        bool Wet(int c) => depths[c] > 0.003;
        Vector3 Point(int c)
        {
            int i = c % nx, j = c / nx;
            double y = Wet(c) ? _water.SurfaceAt(c) + 0.01 : _ground.Heights[c] - 0.03;
            return new Vector3((float)_ground.CellX(i), (float)y, (float)_ground.CellZ(j));
        }
        // the palette's water (Shapes.Water), paler where shallow and darker where deep, never the sky's pale blue
        Color Colour(int c)
        {
            float deep = Mathf.Clamp((float)depths[c] / 0.6f, 0, 1);
            var water = Shapes.Water.Lightened(0.3f).Lerp(Shapes.Water.Darkened(0.3f), deep);
            return water with { A = Wet(c) ? 0.65f + 0.3f * deep : 0 };
        }
        if (_wetTexture is not null)
        {
            // built as bytes and sent once, and only when some cell's wetness changed: per-cell SetPixel cost ~10 ms a frame on the crater
            var wet = new byte[depths.Count];
            for (int c = 0; c < wet.Length; c++) wet[c] = depths[c] > 0.0005 ? (byte)255 : (byte)0;
            if (_wetBytes is null || !wet.AsSpan().SequenceEqual(_wetBytes))
            {
                _wetBytes = wet;
                _wetTexture.Update(Image.CreateFromData(nx, nz, false, Image.Format.R8, wet));
            }
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int triangles = 0;
        void Triangle(int a, int b, int c)
        {
            if (!Wet(a) && !Wet(b) && !Wet(c)) return;
            foreach (int k in new[] { a, b, c })
            {
                st.SetColor(Colour(k));
                st.SetNormal(Vector3.Up);
                st.AddVertex(Point(k));
            }
            triangles++;
        }
        for (int j = 0; j + 1 < nz; j++)
            for (int i = 0; i + 1 < nx; i++)
            {
                int a = i + j * nx, b = a + 1, c = a + nx, d = c + 1;
                Triangle(a, b, c);
                Triangle(b, d, c);
            }
        _waterMesh.Mesh = triangles > 0 ? st.Commit() : null;
    }
}
