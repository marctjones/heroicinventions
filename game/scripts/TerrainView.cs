using Godot;
using HeroicInventions.Sim.Fluids;

namespace HeroicInventions;

/// <summary>
/// A world's ground drawn and made solid (issue #37). The ground is a mesh
/// through the cells' centre heights, each vertex coloured by its soil and
/// shaded by its slope; Jolt gets the same heights as a HeightMapShape3D,
/// so what is seen is what bodies land on. The water loose on it is a
/// second mesh, a quad over each wet cell at its surface (the corners
/// shared with wet neighbours so a pool reads as one sheet), deeper water
/// darker, rebuilt a few times a second from the solver's depths.
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

    public void Show(Terrain ground, ShallowWater2D water)
    {
        _ground = ground;
        _water = water;
        foreach (var c in GetChildren()) c.QueueFree();
        AddChild(_groundMesh = new MeshInstance3D { Mesh = GroundMesh(), Name = "Ground" });
        AddChild(_body = Collision());
        _shownVersion = ground.Version;
        _waterMesh = new MeshInstance3D
        {
            Name = "Water",
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.15f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        AddChild(_waterMesh);
        DrawWater();
    }

    private Vector3 Centre(int i, int j) => new((float)_ground.CellX(i), (float)_ground.Heights[i + j * _ground.Nx], (float)_ground.CellZ(j));

    private ArrayMesh GroundMesh()
    {
        int nx = _ground.Nx, nz = _ground.Nz;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var light = new Vector3(0.4f, 1, 0.3f).Normalized();
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                var soil = _ground.Soils[_ground.Soil[i + j * nx]].Material;
                // shade by slope so the lie of the land shows even in flat light
                var dx = Centre(Math.Min(i + 1, nx - 1), j) - Centre(Math.Max(i - 1, 0), j);
                var dz = Centre(i, Math.Min(j + 1, nz - 1)) - Centre(i, Math.Max(j - 1, 0));
                var normal = dz.Cross(dx).Normalized();
                float shade = 0.65f + 0.35f * Mathf.Max(0, normal.Dot(light));
                var c = Shapes.ColorFor(soil);
                if (_ground.Loose[i + j * nx]) c = c.Lightened(0.18f);   // spoil and slumped ground: loose, paler (#44)
                st.SetColor(new Color(c.R * shade, c.G * shade, c.B * shade));
                st.SetNormal(normal);
                st.AddVertex(Centre(i, j));
            }
        for (int j = 0; j + 1 < nz; j++)
            for (int i = 0; i + 1 < nx; i++)
            {
                int a = i + j * nx, b = a + 1, c = a + nx, d = c + 1;
                st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
                st.AddIndex(b); st.AddIndex(d); st.AddIndex(c);
            }
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
        return mesh;
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

    /// <summary>Redraws the water after a tick, five times a second.</summary>
    public void Refresh(double dt)
    {
        if ((_sinceDrawn += dt) < 0.2) return;
        _sinceDrawn = 0;
        if (_ground.Version != _shownVersion)
        {
            // dug or heaped (issue #44): the ground's shape and its collision change with it
            _shownVersion = _ground.Version;
            _groundMesh.Mesh = GroundMesh();
            _body.QueueFree();
            AddChild(_body = Collision());
        }
        DrawWater();
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
        Color Colour(int c)
        {
            float deep = Mathf.Clamp((float)depths[c] / 0.6f, 0, 1);
            return new Color(0.30f - 0.15f * deep, 0.55f - 0.2f * deep, 0.85f - 0.15f * deep, Wet(c) ? 0.6f + 0.3f * deep : 0);
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
