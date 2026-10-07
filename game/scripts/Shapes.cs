using Godot;

namespace HeroicInventions;

/// <summary>Small helpers for building placeholder geometry in code until real models exist.</summary>
public static class Shapes
{
    /// <summary>
    /// A flat-coloured surface in the game's look (docs/art-direction.md): toon shading, light falling off
    /// in flat bands, and, when it is opaque, the black outline (<see cref="Skins.Outline"/>). Glass, water
    /// and other see-through things get no line; so does the ground, which asks for none.
    /// </summary>
    public static StandardMaterial3D Mat(Color color, float metallic = 0, float roughness = 0.8f, float alpha = 1, bool outline = true) => new()
    {
        AlbedoColor = new Color(color, alpha),
        Metallic = metallic,
        Roughness = roughness,
        Transparency = alpha < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Toon,
        NextPass = alpha < 1 || !outline ? null : Skins.Outline,
    };

    public static MeshInstance3D Box(Vector3 size, StandardMaterial3D mat) =>
        new() { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat };

    public static MeshInstance3D Sphere(float radius, StandardMaterial3D mat) =>
        new() { Mesh = new SphereMesh { Radius = radius, Height = radius * 2 }, MaterialOverride = mat };

    /// <summary>A cylinder along the local Y axis.</summary>
    public static MeshInstance3D Cylinder(float radius, float height, StandardMaterial3D mat) =>
        new() { Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height }, MaterialOverride = mat };

    /// <summary>
    /// A cylinder running from <paramref name="a"/> to <paramref name="b"/>.
    /// </summary>
    public static MeshInstance3D Rod(Vector3 a, Vector3 b, float radius, StandardMaterial3D mat)
    {
        var dir = b - a;
        var rod = Cylinder(radius, Mathf.Max(dir.Length(), 0.001f), mat);
        rod.Position = (a + b) / 2;
        var up = dir.Normalized();
        var axis = Vector3.Up.Cross(up);
        if (axis.LengthSquared() > 1e-8f)
            rod.Basis = new Basis(axis.Normalized(), Vector3.Up.AngleTo(up));
        else if (up.Y < 0)
            rod.Basis = new Basis(Vector3.Right, Mathf.Pi);
        return rod;
    }

    /// <summary>A material's colour, from the material table (racket/heroic/materials.rktd).</summary>
    public static Color ColorFor(string materialId) => Skins.ColorOf(materialId);

    public static readonly Color Bronze = Color.FromHtml("#CC8F4A");
    public static readonly Color Copper = new(0.72f, 0.45f, 0.20f);
    public static readonly Color Water = new(0.25f, 0.55f, 0.85f);
    public static readonly Color Stone = Color.FromHtml("#A8A298");   // darker than the old #B8B2A8, so the floor stops clipping white
}
