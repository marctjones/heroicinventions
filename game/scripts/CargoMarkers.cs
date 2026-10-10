using Godot;
using HeroicInventions.Sim.Fluids;

namespace HeroicInventions;

/// <summary>One marker the rover knows of: a rough area (a disc), never a point. <see cref="X"/>, <see cref="Z"/> is the disc's centre, which is not the crate's place (<see cref="HeroicInventions.Sim.Game.RoughArea"/>).</summary>
/// <param name="Rough">False for a marker placed by a scripted step at an exact spot (tests); it is drawn as a small ring.</param>
public sealed record Marker(string Id, string Name, double X, double Z, double Radius, bool Rough = true);

/// <summary>
/// The rough areas of the rover's cargo, drawn in the world (#236, owner decision on #240): for each marker a disc laid on the ground
/// (a translucent fill and a ring, a dark casing under the bright core so it reads on pale rubble, dark rock and at night, when the
/// hillshade is brightest), and the cargo's name over its centre. The names say nothing of depth or cover. It follows the markers
/// <see cref="Markers"/> hands it each frame (so it follows a crate as it creeps), is rebuilt only when a centre has moved or the
/// ground has changed, and is a view: nothing here is solid or selectable. The chosen marker (<see cref="Chosen"/>) is drawn in amber.
/// Build mode's own marker (BuildMode.Buried.cs) is separate: the same disc until the crate is found, then its exact place and depth.
/// </summary>
public partial class CargoMarkers : Node3D
{
    public required Func<Terrain?> Ground { get; init; }
    public required Func<IReadOnlyList<Marker>> Markers { get; init; }
    public required Func<string?> Chosen { get; init; }
    public required Func<bool> Visible3D { get; init; }

    private const int Segments = 96;
    private const float Lift = 0.25f;   // m above the ground, so the fill does not fight the surface
    private static readonly Color Cyan = new(0.25f, 0.92f, 1f), Amber = new(1f, 0.78f, 0.2f);

    private sealed class Drawn
    {
        public MeshInstance3D Disc = null!;
        public Label3D Label = null!;
        public (double X, double Z, double R) Built;
        public int Version = -1;
        public bool Chosen;
    }

    private readonly Dictionary<string, Drawn> _drawn = [];
    private static StandardMaterial3D? _fill, _casing, _core, _coreChosen;

    private static StandardMaterial3D Flat(Color c) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = c, CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = 5,
    };

    public override void _Process(double delta)
    {
        bool on = Visible3D() && Ground() is not null;
        var markers = on ? Markers() : [];
        var ids = markers.Select(m => m.Id).ToHashSet();
        foreach (var id in _drawn.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            _drawn[id].Disc.QueueFree(); _drawn[id].Label.QueueFree();
            _drawn.Remove(id);
        }
        if (!on) return;
        var ground = Ground()!;
        string? chosen = Chosen();
        foreach (var m in markers)
        {
            double radius = m.Rough ? m.Radius : 1.5;
            if (!_drawn.TryGetValue(m.Id, out var d)) _drawn[m.Id] = d = Make(m);
            bool isChosen = m.Id == chosen;
            if (d.Version != ground.Version || d.Chosen != isChosen || Math.Abs(d.Built.X - m.X) > 0.15 || Math.Abs(d.Built.Z - m.Z) > 0.15 || d.Built.R != radius)
            {
                d.Version = ground.Version; d.Chosen = isChosen; d.Built = (m.X, m.Z, radius);
                d.Disc.Mesh = DiscMesh(ground.HeightAt, m.X, m.Z, radius, isChosen);
                var label = (float)ground.HeightAt(m.X, m.Z);
                d.Label.Position = new Vector3((float)m.X, label + 2.2f, (float)m.Z);
                d.Label.Modulate = isChosen ? Amber : new Color(0.85f, 1f, 1f);
            }
        }
    }

    private Drawn Make(Marker m)
    {
        var disc = new MeshInstance3D { Name = $"Area-{m.Id}", TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var label = new Label3D
        {
            Name = $"Name-{m.Id}", Text = m.Name, TopLevel = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true, NoDepthTest = true, PixelSize = 0.0011f, FontSize = 24, OutlineSize = 10,
            OutlineModulate = new Color(0.02f, 0.03f, 0.05f, 0.95f), RenderPriority = 12,
        };
        AddChild(disc); AddChild(label);
        return new Drawn { Disc = disc, Label = label };
    }

    /// <summary>The disc as three surfaces: the fill (a fan of rings), the dark casing and the bright ring, every vertex on the ground (<paramref name="height"/>). Build mode's marker draws the same disc before a crate is found (BuildMode.Buried.cs).</summary>
    internal static ArrayMesh DiscMesh(Func<double, double, double> height, double cx, double cz, double radius, bool chosen)
    {
        _fill ??= Flat(new Color(Cyan, 0.09f));
        _casing ??= Flat(new Color(0.02f, 0.04f, 0.07f, 0.55f));
        _core ??= Flat(new Color(Cyan, 0.95f));
        _coreChosen ??= Flat(new Color(Amber, 0.98f));
        Vector3 At(double r, int s)
        {
            double a = 2 * Math.PI * s / Segments;
            double x = cx + r * Math.Cos(a), z = cz + r * Math.Sin(a);
            return new Vector3((float)x, (float)height(x, z) + Lift, (float)z);
        }
        var mesh = new ArrayMesh();
        // fill: the centre, then four rings
        {
            const int rings = 4;
            var v = new List<Vector3> { new((float)cx, (float)height(cx, cz) + Lift, (float)cz) };
            for (int k = 1; k <= rings; k++) for (int s = 0; s < Segments; s++) v.Add(At(radius * k / rings, s));
            var idx = new List<int>();
            for (int s = 0; s < Segments; s++) idx.AddRange([0, 1 + s, 1 + (s + 1) % Segments]);
            for (int k = 1; k < rings; k++)
                for (int s = 0; s < Segments; s++)
                {
                    int a = 1 + (k - 1) * Segments + s, b = 1 + (k - 1) * Segments + (s + 1) % Segments, c = a + Segments, d = b + Segments;
                    idx.AddRange([a, c, b, b, c, d]);
                }
            AddSurface(mesh, v, idx, _fill);
        }
        void Ring(double inner, double outer, StandardMaterial3D material)
        {
            var v = new List<Vector3>(); var idx = new List<int>();
            for (int s = 0; s < Segments; s++) { v.Add(At(inner, s)); v.Add(At(outer, s)); }
            for (int s = 0; s < Segments; s++)
            {
                int a = 2 * s, b = 2 * s + 1, c = 2 * ((s + 1) % Segments), d = c + 1;
                idx.AddRange([a, b, c, c, b, d]);
            }
            AddSurface(mesh, v, idx, material);
        }
        double w = chosen ? 0.45 : 0.3;
        Ring(radius - w - 0.35, radius + w + 0.35, _casing);
        Ring(radius - w, radius + w, chosen ? _coreChosen : _core);
        return mesh;
    }

    private static void AddSurface(ArrayMesh mesh, List<Vector3> vertices, List<int> indices, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }
}
