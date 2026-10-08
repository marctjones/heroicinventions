using Godot;
using HeroicInventions.Sim.Fluids;

namespace HeroicInventions;

/// <summary>
/// The ground the rover has worked (issue #63), drawn and made solid: for each patch of fine cells the terrain holds
/// (<see cref="Terrain.Worked"/>) a mesh through its nodes and a HeightMapShape3D of its own, laid where the map's coarse mesh and
/// body leave off. The coarse mesh has no squares under a patch and its body is sunk there (see <c>PutMesh</c> and
/// <c>Collision</c>), so a trench the rover digs is a trench to drive into and a heap one to drive over.
///
/// A patch's body is replaced each time the patch changes, as the map's is (#188: a new surface, not new heights in the old
/// one, is what bodies resting on it feel), and only the patch's ~15,000 samples, not the map's 28,900. The patch's ring
/// of nodes is the map's own, so the two meshes meet along a line.
/// </summary>
public partial class TerrainView
{
    private sealed class Patch(WorkedGround ground)
    {
        public readonly WorkedGround Ground = ground;
        public MeshInstance3D Node = null!;
        public ArrayMesh Mesh = null!;
        public StaticBody3D Body = null!;
        public int Shown = -1;
        public Vector3[] Vertices = [], Normals = [];
        public Color[] Shades = [];
        public int[] Indices = [];
        public MeshInstance3D? Water;
        // for each node column (row), the two sample points half a fine cell either side: the fine water column (row) there, or -1
        // off the patch's grid, and the map's column (row) there
        public int[] FineCol = [], FineRow = [], MapCol = [], MapRow = [];
        public float[] Surface = [];
        public bool[] Wet = [];
        public int[] Tri = [];
        public Vector3[] WaterVertices = [], WaterNormals = [];
        public Color[] WaterColours = [];
    }

    private readonly List<Patch> _patches = [];
    private int _shownPatches;

    private bool InPatch(int i, int j)
    {
        foreach (var w in _ground.Worked)
            if (i >= w.Bi0 && i + 1 <= w.Bi1 && j >= w.Bj0 && j + 1 <= w.Bj1) return true;
        return false;
    }

    /// <summary>Makes, drops and redraws the meshes and bodies of the ground's worked patches to match what the simulation holds; every tick, it costs a version check per patch.</summary>
    private void SyncWorked()
    {
        if (_groundMesh is null) return;
        if (_ground.WorkedPatches != _shownPatches)
        {
            _shownPatches = _ground.WorkedPatches;
            foreach (var p in _patches.Where(p => !_ground.Worked.Contains(p.Ground)).ToList())
            {
                p.Node.QueueFree(); p.Body.QueueFree(); p.Water?.QueueFree();
                _patches.Remove(p);
            }
            foreach (var w in _ground.Worked.Where(w => _patches.All(p => p.Ground != w)))
            {
                var p = new Patch(w);
                _patches.Add(p);
                MakePatch(p);
            }
            // the map's own mesh loses its squares under the patches, and its body is sunk there
            GD.Print($"[terrain] {_patches.Count} worked patch(es) drawn, {_patches.Sum(p => p.Ground.Nx * p.Ground.Nz)} nodes");
            _mesh!.ClearSurfaces();
            PutMesh();
            _mesh.SurfaceSetMaterial(0, _groundMaterial);
            _body.QueueFree();
            AddChild(_body = Collision());
        }
        foreach (var p in _patches)
            if (p.Ground.Version != p.Shown) RedrawPatch(p);
    }

    private void MakePatch(Patch p)
    {
        var w = p.Ground;
        int nx = w.Nx, nz = w.Nz, n = nx * nz;
        p.Vertices = new Vector3[n]; p.Normals = new Vector3[n]; p.Shades = new Color[n];
        p.Indices = new int[(nx - 1) * (nz - 1) * 6];
        int t = 0;
        for (int j = 0; j + 1 < nz; j++)
            for (int i = 0; i + 1 < nx; i++)
            {
                int a = i + j * nx, b = a + 1, c = a + nx, d = c + 1;   // cut as the map's squares are
                p.Indices[t++] = a; p.Indices[t++] = b; p.Indices[t++] = c;
                p.Indices[t++] = b; p.Indices[t++] = d; p.Indices[t++] = c;
            }
        var material = new ShaderMaterial { Shader = _groundMaterial!.Shader };
        foreach (var name in new[] { "interval", "origin", "size", "cells", "wet" })
            material.SetShaderParameter(name, _groundMaterial.GetShaderParameter(name));
        material.SetShaderParameter("patch", 1f);
        p.Mesh = new ArrayMesh();
        p.Node = new MeshInstance3D { Name = "Worked", Mesh = p.Mesh };
        p.Mesh.ClearSurfaces();
        RedrawPatch(p, material);
        AddChild(p.Node);
    }

    private void RedrawPatch(Patch p, ShaderMaterial? material = null)
    {
        long t0 = TickProfile.Start();
        var w = p.Ground;
        var f = w.Fine;
        int nx = w.Nx, nz = w.Nz;
        var h = f.Heights;
        var light = new Vector3(0.4f, 1, 0.3f).Normalized();
        double c2 = 2 * f.Cell;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = i + j * nx;
                p.Vertices[k] = new Vector3((float)w.NodeX(i), (float)h[k], (float)w.NodeZ(j));
                // the shading from the true heights, a cell either side (the map smooths its 5 m stairs; these cells are small)
                int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, nx - 1), j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, nz - 1);
                var dx = new Vector3((i1 - i0) * (float)f.Cell, (float)(h[i1 + j * nx] - h[i0 + j * nx]), 0);
                var dz = new Vector3(0, (float)(h[i + j1 * nx] - h[i + j0 * nx]), (j1 - j0) * (float)f.Cell);
                var normal = dz.Cross(dx).Normalized();
                float shade = 0.45f + 0.55f * Mathf.Max(0, normal.Dot(light));
                // how far it has been dug (dark, 0.2 m for full) or heaped (pale, 0.1 m), and spoil that has slumped a little paler than the ground
                double delta = h[k] - w.Original[k];
                double tint = delta < 0 ? Math.Max(delta / 0.2, -1) * 0.9 : Math.Min(delta / 0.1, 1);
                if (f.Loose[k]) tint = Math.Max(tint, 0.25);
                p.Normals[k] = normal;
                p.Shades[k] = new Color(shade, (float)(0.5 + 0.5 * tint), shade);
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = p.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = p.Normals;
        arrays[(int)Mesh.ArrayType.Color] = p.Shades;
        arrays[(int)Mesh.ArrayType.Index] = p.Indices;
        var kept = material ?? (ShaderMaterial?)(p.Mesh.GetSurfaceCount() > 0 ? p.Mesh.SurfaceGetMaterial(0) : null);
        p.Mesh.ClearSurfaces();
        p.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        p.Mesh.SurfaceSetMaterial(0, kept);
        TickProfile.Stop("worked-mesh", t0);
        // a new body, not new heights in the old shape (see Reshape)
        t0 = TickProfile.Start();
        if (p.Body is not null) p.Body.QueueFree();
        var data = new float[nx * nz];
        for (int k = 0; k < data.Length; k++) data[k] = (float)h[k];
        var body = new StaticBody3D { Name = "WorkedBody", CollisionLayer = GroundLayer, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D
        {
            Shape = new HeightMapShape3D { MapWidth = nx, MapDepth = nz, MapData = data },
            Transform = new Transform3D(Basis.Identity.Scaled(new Vector3((float)f.Cell, 1, (float)f.Cell)),
                                        new Vector3((float)((w.MinX + w.MaxX) / 2), 0, (float)((w.MinZ + w.MaxZ) / 2))),
        });
        body.PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.6f, Bounce = 0.1f };
        p.Body = body;
        AddChild(body);
        p.Shown = w.Version;
        TickProfile.Stop("worked-shape", t0);
    }
}

public partial class TerrainView
{
    /// <summary>
    /// The water on each worked patch (#200), drawn as the map's is but through the patch's own nodes: each node stands at the
    /// mean surface of the wet cells round it (the patch's fine water cells, and in the patch's outer half-cells, where the
    /// map's coarse cell holds the water, that cell's), so a trench's water is a trench's width and a channel's follows its
    /// bends; a dry node sits just under the ground, so the water's edge dips out of sight where it ends.
    /// </summary>
    private void DrawPatchWater()
    {
        foreach (var p in _patches) DrawPatchWater(p);
    }

    private void DrawPatchWater(Patch p)
    {
        var w = p.Ground;
        if (w.Water is not { } grid) return;
        if (grid.Water.MaxDepth <= 0.003 && !RingWet(w))
        {
            // a dry patch (the rover's trenches before any water comes): nothing to draw, and no node to work out
            if (p.Water is not null) p.Water.Mesh = null;
            return;
        }
        int nx = w.Nx, nz = w.Nz, n = nx * nz;
        var bed = grid.Bed;
        double cf = bed.Cell;
        if (p.FineCol.Length != 2 * nx)
        {
            int Col(double x, double x0, double cell, int count) { int i = (int)Math.Floor((x - x0) / cell); return i >= 0 && i < count ? i : -1; }
            p.FineCol = new int[2 * nx]; p.MapCol = new int[2 * nx]; p.FineRow = new int[2 * nz]; p.MapRow = new int[2 * nz];
            for (int i = 0; i < nx; i++)
                for (int s = 0; s < 2; s++)
                {
                    double x = w.NodeX(i) + (s == 0 ? -cf / 2 : cf / 2);
                    p.FineCol[2 * i + s] = Col(x, bed.X0, cf, bed.Nx);
                    p.MapCol[2 * i + s] = Math.Clamp((int)Math.Floor((x - _ground.X0) / _ground.Cell), 0, _ground.Nx - 1);
                }
            for (int j = 0; j < nz; j++)
                for (int s = 0; s < 2; s++)
                {
                    double z = w.NodeZ(j) + (s == 0 ? -cf / 2 : cf / 2);
                    p.FineRow[2 * j + s] = Col(z, bed.Z0, cf, bed.Nz);
                    p.MapRow[2 * j + s] = Math.Clamp((int)Math.Floor((z - _ground.Z0) / _ground.Cell), 0, _ground.Nz - 1);
                }
            p.Surface = new float[n]; p.Wet = new bool[n]; p.Tri = new int[(nx - 1) * (nz - 1) * 6];
            p.WaterVertices = new Vector3[n]; p.WaterNormals = new Vector3[n]; p.WaterColours = new Color[n];
            Array.Fill(p.WaterNormals, Vector3.Up);
        }
        var fineDepth = grid.Water.Depths; var mapDepth = _water.Depths;
        var ground = w.Fine.Heights;
        bool any = false;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                double sum = 0; int wet = 0;
                for (int t = 0; t < 2; t++)
                    for (int s = 0; s < 2; s++)
                    {
                        int fc = p.FineCol[2 * i + s], fr = p.FineRow[2 * j + t];
                        if (fc >= 0 && fr >= 0)
                        {
                            int c = fc + fr * bed.Nx;
                            if (fineDepth[c] > 0.003) { sum += bed.Heights[c] + fineDepth[c]; wet++; }
                        }
                        else
                        {
                            int c = p.MapCol[2 * i + s] + p.MapRow[2 * j + t] * _ground.Nx;
                            if (!_water.UnderPatch(c) && mapDepth[c] > 0.003) { sum += _ground.Heights[c] + mapDepth[c]; wet++; }
                        }
                    }
                int k = i + j * nx;
                double surface = wet > 0 ? sum / wet : double.NegativeInfinity;
                p.Wet[k] = surface > ground[k] + 0.002;
                p.Surface[k] = (float)(p.Wet[k] ? surface + 0.01 : ground[k] - 0.03);
                any |= p.Wet[k];
                p.WaterVertices[k] = new Vector3((float)w.NodeX(i), p.Surface[k], (float)w.NodeZ(j));
                p.WaterColours[k] = WaterColour(surface - ground[k], p.Wet[k]);
            }
        p.Water ??= MakeWaterNode(p);
        if (!any) { p.Water.Mesh = null; return; }
        int count = 0;
        for (int j = 0; j + 1 < nz; j++)
            for (int i = 0; i + 1 < nx; i++)
            {
                int a = i + j * nx, b = a + 1, c = a + nx, d = c + 1;   // cut as the patch's ground is
                if (p.Wet[a] || p.Wet[b] || p.Wet[c]) { p.Tri[count++] = a; p.Tri[count++] = b; p.Tri[count++] = c; }
                if (p.Wet[b] || p.Wet[d] || p.Wet[c]) { p.Tri[count++] = b; p.Tri[count++] = d; p.Tri[count++] = c; }
            }
        // one vertex a node, shared by the wet triangles round it (indexed, as the patch's ground is)
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = p.WaterVertices;
        arrays[(int)Mesh.ArrayType.Normal] = p.WaterNormals;
        arrays[(int)Mesh.ArrayType.Color] = p.WaterColours;
        arrays[(int)Mesh.ArrayType.Index] = p.Tri.AsSpan(0, count).ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        p.Water.Mesh = mesh;
    }

    /// <summary>Whether any of the map's cells in the patch's outer half-cells (its box less the fine grid) holds water to draw.</summary>
    private bool RingWet(WorkedGround w)
    {
        var depths = _water.Depths;
        for (int j = w.Bj0; j <= w.Bj1; j++)
            for (int i = w.Bi0; i <= w.Bi1; i++)
            {
                int c = i + j * _ground.Nx;
                if (!_water.UnderPatch(c) && depths[c] > 0.003) return true;
            }
        return false;
    }

    private MeshInstance3D MakeWaterNode(Patch p)
    {
        var node = new MeshInstance3D
        {
            Name = "WorkedWater",
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.15f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        AddChild(node);
        return node;
    }
}
