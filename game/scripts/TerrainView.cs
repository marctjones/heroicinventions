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
        _patches.Clear(); _shownPatches = 0; _indices = null;
        AddChild(_groundMesh = new MeshInstance3D { Mesh = GroundMesh(), Name = "Ground" });
        AddChild(_body = Collision());
        _shownVersion = ground.Version;
        if (TickProfile.On) { AddChild(new TickProfile.Edge(true)); AddChild(new TickProfile.Edge(false)); }
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

    // The ground mesh's arrays are kept between reshapes, and a reshape redoes only what a change can reach (#188):
    // the cells that changed (box), their smoothed heights and colours two cells round that (each smoothing pass
    // reaches one cell), and their shading three cells round (it reads the smoothed heights a cell either side).
    // Every value is worked out by the same sums in the same order as the whole ground would be, so the mesh is the
    // same bit for bit (checked in HEROIC_TICK_PROFILE runs).
    private Vector3[] _vertices = [], _normals = [];
    private Color[] _shades = [];
    private double[] _soft = [], _scratch = [];
    private double[][] _colours = [[], [], [], []], _smoothed = [[], [], [], []];
    private byte[] _colourBytes = [];
    private (Color Colour, float Roughness)[] _soilLook = [];
    private ArrayMesh? _mesh;
    private ImageTexture? _cellsTexture;
    private double[] _shownHeights = [];
    private int[] _shownSoil = [];
    private bool[] _shownLoose = [];

    private readonly record struct Box(int I0, int J0, int I1, int J1)
    {
        public Box Grow(int by, int nx, int nz) => new(Math.Max(I0 - by, 0), Math.Max(J0 - by, 0), Math.Min(I1 + by, nx - 1), Math.Min(J1 + by, nz - 1));
    }

    /// <summary>The box of cells whose height, soil or looseness differ from what the mesh was last made from; null if none.</summary>
    private Box? Changed()
    {
        int nx = _ground.Nx, n = _ground.Heights.Length, i0 = int.MaxValue, j0 = int.MaxValue, i1 = -1, j1 = -1;
        var h = _ground.Heights; var soil = _ground.Soil; var loose = _ground.Loose;
        for (int k = 0; k < n; k++)
        {
            if (h[k] == _shownHeights[k] && soil[k] == _shownSoil[k] && loose[k] == _shownLoose[k]) continue;
            int i = k % nx, j = k / nx;
            i0 = Math.Min(i0, i); i1 = Math.Max(i1, i); j0 = Math.Min(j0, j); j1 = Math.Max(j1, j);
        }
        return i1 < 0 ? null : new Box(i0, j0, i1, j1);
    }

    private ArrayMesh GroundMesh()
    {
        int nx = _ground.Nx, nz = _ground.Nz, n = nx * nz;
        // Each soil's look worked out once, not once per cell: a little greyer than its table colour, so crates, a
        // rover and machines keep their own colour against what they stand on, each layer keeping its hue so the
        // crater's bands tell apart; ice-cemented ground glints where dry soil is matt.
        _soilLook = _ground.Soils.Select(s =>
        {
            var raw = Shapes.ColorFor(s.Material);
            return (Colour: Color.FromHsv(raw.H, raw.S * 0.85f, raw.V), Roughness: s.Material.Contains("ice") ? 0.3f : 0.95f);
        }).ToArray();
        _vertices = new Vector3[n]; _normals = new Vector3[n]; _shades = new Color[n];
        _soft = new double[n]; _scratch = new double[n]; _colourBytes = new byte[n * 4];
        for (int ch = 0; ch < 4; ch++) { _colours[ch] = new double[n]; _smoothed[ch] = new double[n]; }
        _shownHeights = (double[])_ground.Heights.Clone();
        _shownSoil = (int[])_ground.Soil.Clone();
        _shownLoose = (bool[])_ground.Loose.Clone();
        UpdateGround(new Box(0, 0, nx - 1, nz - 1));
        _mesh = new ArrayMesh();
        PutMesh();
        _groundMaterial ??= GroundMaterial();
        _cellsTexture = ImageTexture.CreateFromImage(Image.CreateFromData(nx, nz, false, Image.Format.Rgba8, _colourBytes));
        _groundMaterial.SetShaderParameter("cells", _cellsTexture);
        _mesh.SurfaceSetMaterial(0, _groundMaterial);
        return _mesh;
    }

    /// <summary>The mesh after a change: the arrays redone round <paramref name="box"/>, uploaded whole (a few hundred KB).</summary>
    private void ReshapeGround(Box box)
    {
        int nx = _ground.Nx, nz = _ground.Nz;
        long t = TickProfile.Start();
        UpdateGround(box);
        TickProfile.Stop("reshape-arrays", t);
        t = TickProfile.Start();
        _mesh!.ClearSurfaces();
        PutMesh();
        _mesh.SurfaceSetMaterial(0, _groundMaterial);
        _cellsTexture!.Update(Image.CreateFromData(nx, nz, false, Image.Format.Rgba8, _colourBytes));
        TickProfile.Stop("reshape-upload", t);
        var h = _ground.Heights; var soil = _ground.Soil; var loose = _ground.Loose;
        for (int j = box.J0; j <= box.J1; j++)
            for (int i = box.I0; i <= box.I1; i++)
            {
                int k = i + j * nx;
                (_shownHeights[k], _shownSoil[k], _shownLoose[k]) = (h[k], soil[k], loose[k]);
            }
    }

    private void PutMesh()
    {
        int nx = _ground.Nx, nz = _ground.Nz;
        // the mesh in one call from arrays (SurfaceTool's per-vertex calls cost tens of ms on a 170 x 170 crater); the squares
        // under a patch of worked ground are left out, the patch's own finer mesh being the ground there (#63)
        if (_indices is null || _indicesFor != _ground.WorkedPatches)
        {
            _indicesFor = _ground.WorkedPatches;
            var indices = new List<int>((nx - 1) * (nz - 1) * 6);
            for (int j = 0; j + 1 < nz; j++)
                for (int i = 0; i + 1 < nx; i++)
                {
                    if (_ground.Worked.Count > 0 && InPatch(i, j)) continue;
                    int a = i + j * nx, b = a + 1, c = a + nx, d = c + 1;
                    indices.Add(a); indices.Add(b); indices.Add(c);
                    indices.Add(b); indices.Add(d); indices.Add(c);
                }
            _indices = indices.ToArray();
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices;
        arrays[(int)Mesh.ArrayType.Normal] = _normals;
        arrays[(int)Mesh.ArrayType.Color] = _shades;
        arrays[(int)Mesh.ArrayType.Index] = _indices;
        _mesh!.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }

    private Vector3 Soft(int i, int j) => new((float)_ground.CellX(i), (float)_soft[i + j * _ground.Nx], (float)_ground.CellZ(j));

    private void UpdateGround(Box box)
    {
        int nx = _ground.Nx, nz = _ground.Nz;
        var light = new Vector3(0.4f, 1, 0.3f).Normalized();
        var near2 = box.Grow(2, nx, nz); var near3 = box.Grow(3, nx, nz);
        // The light falls on a smoothed copy of the ground: a slide's scar is cut cell by cell, a stair of 5 m treads,
        // and shaded from the true heights every tread lit up as a step. The mesh's points stay the true heights, so
        // what is seen is still what bodies land on; only the shading is smoothed.
        SmoothInto(_ground.Heights, _soft, nx, nz, near2);
        // Each cell's ground colour (roughness in alpha) goes to a texture the shader samples smoothly by position;
        // as vertex colours, anything that changed from cell to cell showed as stair-steps along the mesh's diagonals.
        for (int j = box.J0; j <= box.J1; j++)
            for (int i = box.I0; i <= box.I1; i++)
            {
                int k = i + j * nx;
                var (c, roughness) = _soilLook[_ground.Soil[k]];
                if (_ground.Loose[k]) c = c.Lightened(0.18f);   // spoil and slumped ground: loose, paler (#44)
                // ground the water has cut away shows darker and wetter, ground it has laid down paler (#53); a 5 cm change at full strength
                double m = _ground.Heights[k] - _startHeights[k];
                if (Math.Abs(m) > 0.002)
                    c = m < 0 ? c.Darkened(Mathf.Clamp((float)(-m / 0.05), 0, 0.45f)) : c.Lerp(new Color(0.98f, 0.95f, 0.85f), Mathf.Clamp((float)(m / 0.05), 0, 0.6f));
                _colours[0][k] = c.R; _colours[1][k] = c.G; _colours[2][k] = c.B; _colours[3][k] = roughness;
                _vertices[k] = Centre(i, j);
            }
        for (int j = near3.J0; j <= near3.J1; j++)
            for (int i = near3.I0; i <= near3.I1; i++)
            {
                int k = i + j * nx;
                var dx = Soft(Math.Min(i + 1, nx - 1), j) - Soft(Math.Max(i - 1, 0), j);
                var dz = Soft(i, Math.Min(j + 1, nz - 1)) - Soft(i, Math.Max(j - 1, 0));
                var normal = dz.Cross(dx).Normalized();
                // hillshade, as on a map: lit from one high fixed side whatever the sun, strong enough that every
                // slope reads (readable over realistic; the sun's own light and shadows come on top)
                float shade = 0.45f + 0.55f * Mathf.Max(0, normal.Dot(light));
                _normals[k] = normal;
                _shades[k] = new Color(shade, shade, shade);
            }
        // The colours are smoothed over their neighbours before they're drawn: soils and the scour and deposit cues
        // change cell by cell (a slide lays its rubble down as its own soil), so their edges are 5 m staircases in the
        // data. Smoothed, an edge reads as the line it is. Two passes soften it over about two cells.
        for (int ch = 0; ch < 4; ch++)
        {
            SmoothInto(_colours[ch], _smoothed[ch], nx, nz, near2);
            for (int j = near2.J0; j <= near2.J1; j++)
                for (int i = near2.I0; i <= near2.I1; i++)
                {
                    int k = i + j * nx;
                    _colourBytes[k * 4 + ch] = (byte)Math.Clamp((int)Math.Round(_smoothed[ch][k] * 255), 0, 255);
                }
        }
    }

    /// <summary>The mesh a whole rebuild would make, against the one kept up by changes: a mismatch is printed (HEROIC_TICK_PROFILE).</summary>
    private void CheckMesh()
    {
        var kept = (_vertices, _normals, _shades, _colourBytes, _soft);
        int n = _ground.Nx * _ground.Nz;
        _vertices = new Vector3[n]; _normals = new Vector3[n]; _shades = new Color[n]; _colourBytes = new byte[n * 4]; _soft = new double[n];
        UpdateGround(new Box(0, 0, _ground.Nx - 1, _ground.Nz - 1));
        int bad = 0;
        for (int k = 0; k < n; k++)
            if (_vertices[k] != kept._vertices[k] || _normals[k] != kept._normals[k] || _shades[k] != kept._shades[k]) bad++;
        for (int k = 0; k < n * 4; k++) if (_colourBytes[k] != kept._colourBytes[k]) bad++;
        (_vertices, _normals, _shades, _colourBytes, _soft) = kept;
        _meshChecks++; _meshMismatches += bad;
        if (bad > 0) GD.Print($"[tick] MESH MISMATCH: {bad} values differ from a whole rebuild");
    }
    private int _meshChecks, _meshMismatches;

    private int[]? _indices;
    private int _indicesFor = -1;

    /// <summary>
    /// Two passes of a 3×3 box blur over a cell field, edges clamped: a staircase edge becomes a smooth one. The
    /// result is written for the cells in <paramref name="window"/> only (the first pass is worked out one cell
    /// wider, in <see cref="_scratch"/>), the sums in the order a whole-map blur would take them.
    /// </summary>
    private void SmoothInto(double[] field, double[] result, int nx, int nz, Box window)
    {
        var wide = window.Grow(1, nx, nz);
        Blur(field, _scratch, nx, nz, wide);
        Blur(_scratch, result, nx, nz, window);
    }

    private static void Blur(double[] from, double[] to, int nx, int nz, Box w)
    {
        for (int j = w.J0; j <= w.J1; j++)
        {
            int jm = Math.Max(j - 1, 0) * nx, j0 = j * nx, jp = Math.Min(j + 1, nz - 1) * nx;
            for (int i = w.I0; i <= w.I1; i++)
            {
                int im = Math.Max(i - 1, 0), ip = Math.Min(i + 1, nx - 1);
                to[j0 + i] = (from[jm + im] + from[jm + i] + from[jm + ip] + from[j0 + im] + from[j0 + i] + from[j0 + ip]
                              + from[jp + im] + from[jp + i] + from[jp + ip]) / 9;
            }
        }
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
                uniform float patch = 0.0;   // 1 on a patch of worked ground (#63): COLOR.g is how far it has been dug (dark) or heaped (pale)
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
                    vec3 lit = soil;
                    if (patch > 0.5) {
                        float tint = (COLOR.g - 0.5) * 2.0;
                        lit = tint < 0.0 ? soil * (1.0 + 0.55 * tint) : mix(soil, vec3(0.98, 0.95, 0.85), 0.45 * tint);
                    }
                    ALBEDO = lit * COLOR.r * (1.0 - 0.16 * fine - 0.32 * heavy);   // COLOR.r: the hillshade
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
        // Under a patch of worked ground the patch's own body is the ground, which a dig can take below the map's: the
        // coarse body is sunk there, below the patch's lowest point, or the rover would ride the old surface over a trench (#63).
        foreach (var w in _ground.Worked)
        {
            float floor = (float)(w.Fine.Heights.Min() - 20);
            for (int j = w.Bj0 + 1; j < w.Bj1; j++)
                for (int i = w.Bi0 + 1; i < w.Bi1; i++) data[i + j * nx] = floor;
        }
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
        long whole = TickProfile.Start();
        if (TickProfile.On)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_lastRefresh != 0) TickProfile.Add("period", System.Diagnostics.Stopwatch.GetElapsedTime(_lastRefresh, now).TotalMilliseconds);
            _lastRefresh = now;
            _tick++;
        }
        long t = TickProfile.Start();
        SyncWorked();
        TickProfile.Stop("worked", t);
        t = TickProfile.Start();
        Boulders();
        TickProfile.Stop("boulders", t);
        if ((_sinceDrawn += dt) < 0.2) { TickProfile.Stop("refresh", whole); return; }
        _sinceDrawn = 0;
        Reshape();
        t = TickProfile.Start();
        DrawWater();
        TickProfile.Stop("drawwater", t);
        TickProfile.Stop("refresh", whole);
    }

    private long _lastRefresh;
    private int _tick, _lastReshapeTick = -1;

    public override void _ExitTree() => TickProfile.Report();

    private void Reshape()
    {
        if (_ground.Version == _shownVersion) return;
        // dug or heaped (issue #44): the ground's shape and its collision change with it
        _shownVersion = _ground.Version;
        _lastReshapeTick = _tick; TickProfile.ReshapeTick = TickProfile.Ticks - 1;
        long t = TickProfile.Start();
        if (Changed() is not { } box) return;
        ReshapeGround(box);
        TickProfile.Stop("reshape-mesh", t);
        t = TickProfile.Start();
        // A new body each time, not new heights in the old shape: Jolt takes it as a new surface, and the crates and
        // machines standing on it feel it (a crate at rest reads an impact impulse), which the traced runs include
        // (#188: updating the shape in place changed the crates' and boulders' numbers). It costs ~1 ms.
        _body.QueueFree();
        AddChild(_body = Collision());
        TickProfile.Stop("reshape-shape", t);
        if (TickProfile.Check) CheckMesh();   // outside the timings above
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

    private int[] _waterTri = [];

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
        // the mesh in one call from arrays (as the ground's is), not vertex by vertex through a SurfaceTool
        if (_waterTri.Length != (nx - 1) * (nz - 1) * 6) _waterTri = new int[(nx - 1) * (nz - 1) * 6];
        int count = 0;
        for (int j = 0; j + 1 < nz; j++)
            for (int i = 0; i + 1 < nx; i++)
            {
                int a = i + j * nx, b = a + 1, c = a + nx, d = c + 1;
                if (Wet(a) || Wet(b) || Wet(c)) { _waterTri[count++] = a; _waterTri[count++] = b; _waterTri[count++] = c; }
                if (Wet(b) || Wet(d) || Wet(c)) { _waterTri[count++] = b; _waterTri[count++] = d; _waterTri[count++] = c; }
            }
        if (count == 0) { _waterMesh.Mesh = null; return; }
        var vertices = new Vector3[count]; var colours = new Color[count]; var normals = new Vector3[count];
        for (int v = 0; v < count; v++)
        {
            int k = _waterTri[v];
            (vertices[v], colours[v], normals[v]) = (Point(k), Colour(k), Vector3.Up);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _waterMesh.Mesh = mesh;
    }
}
