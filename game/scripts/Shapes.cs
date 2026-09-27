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

    public static readonly Color Bronze = new(0.80f, 0.56f, 0.29f);
    public static readonly Color Copper = new(0.72f, 0.45f, 0.20f);
    public static readonly Color Water = new(0.25f, 0.55f, 0.85f);
    public static readonly Color Stone = new(0.72f, 0.70f, 0.66f);
}
