using Godot;

namespace HeroicInventions;

/// <summary>Small helpers for building placeholder geometry in code until real models exist.</summary>
public static class Shapes
{
    public static StandardMaterial3D Mat(Color color, float metallic = 0, float roughness = 0.8f, float alpha = 1) => new()
    {
        AlbedoColor = new Color(color, alpha),
        Metallic = metallic,
        Roughness = roughness,
        Transparency = alpha < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
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

    public static Color ColorFor(string materialId) => materialId switch
    {
        "bronze" => Bronze,
        "iron" => new Color(0.35f, 0.35f, 0.37f),
        "oak" => new Color(0.55f, 0.38f, 0.22f),
        "pine" => new Color(0.82f, 0.66f, 0.43f),
        "cedar" => new Color(0.76f, 0.52f, 0.36f),
        "olive" => new Color(0.60f, 0.52f, 0.34f),
        "marble" => new Color(0.92f, 0.91f, 0.88f),
        "hemp" => new Color(0.78f, 0.70f, 0.52f),
        _ => Stone,
    };

    public static readonly Color Bronze = new(0.80f, 0.56f, 0.29f);
    public static readonly Color Copper = new(0.72f, 0.45f, 0.20f);
    public static readonly Color Water = new(0.25f, 0.55f, 0.85f);
    public static readonly Color Stone = new(0.72f, 0.70f, 0.66f);
}
