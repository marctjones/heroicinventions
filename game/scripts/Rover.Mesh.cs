using Godot;

namespace HeroicInventions;

/// <summary>
/// What the rover looks like (issue #94). Drawn in the game's look (Shapes.Mat: toon shading and the black outline, so it reads
/// at any distance), and coloured for legibility over realism: a pale body and dark panel against the crater's dark ground,
/// bronze for the arm (the workshop's accent, art-direction §3) so the part that works stands out from the part that carries.
/// Every mesh carries a <c>part_id</c> ("rover/wheel-0"), the tag the audit looks for.
/// </summary>
public sealed partial class Rover
{
    private static readonly Color Body = Color.FromHtml("#E6DEC8"), Panel = Color.FromHtml("#34425E"), Tyre = Color.FromHtml("#3B3733"),
                                  Spoke = Color.FromHtml("#F1E9D8"), Arm = Shapes.Bronze, Lens = Color.FromHtml("#14110F"), Soil = Color.FromHtml("#9C5A3C");

    private MeshInstance3D Part(Node parent, string id, MeshInstance3D mesh, Vector3? at = null)
    {
        mesh.Name = id;
        mesh.SetMeta("part_id", $"rover/{id}");
        if (at is { } p) mesh.Position = p;
        parent.AddChild(mesh);
        return mesh;
    }

    private void BuildMesh()
    {
        var body = Shapes.Mat(Body, roughness: 0.6f);
        var panel = Shapes.Mat(Panel, roughness: 0.35f);
        var arm = Shapes.Mat(Arm, metallic: 0.3f, roughness: 0.5f);
        var lens = Shapes.Mat(Lens, roughness: 0.2f);

        // the deck and its warm electronics box, the solar panel above them, and a rail round the front
        Part(Chassis, "deck", Shapes.Box(new Vector3(1.0f, 0.18f, 1.5f), body), new Vector3(0, 0.22f, 0));
        Part(Chassis, "electronics-box", Shapes.Box(new Vector3(0.7f, 0.14f, 0.5f), body), new Vector3(0, 0.38f, 0.45f));
        Part(Chassis, "solar-panel", Shapes.Box(new Vector3(1.3f, 0.04f, 0.8f), panel), new Vector3(0, 0.49f, 0.35f));
        foreach (var (x, i) in new[] { (-0.55f, 0), (0.55f, 1) })
            Part(Chassis, $"panel-strut-{i}", Shapes.Box(new Vector3(0.04f, 0.1f, 0.04f), body), new Vector3(x, 0.43f, 0.35f));

        // the mast, with its camera head: two lenses looking forward (-z)
        Part(Chassis, "mast", Shapes.Cylinder(0.025f, 0.7f, body), new Vector3(0.3f, 0.8f, 0.55f));
        var head = Part(Chassis, "camera-head", Shapes.Box(new Vector3(0.26f, 0.1f, 0.12f), body), new Vector3(0.3f, 1.2f, 0.55f));
        foreach (var (x, i) in new[] { (-0.07f, 0), (0.07f, 1) })
        {
            var eye = Part(head, $"camera-lens-{i}", Shapes.Cylinder(0.035f, 0.05f, lens), new Vector3(x, 0, -0.07f));
            eye.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
        }

        // six wheels: a dark tyre, a bronze hub, and three cream spokes standing proud of the tyre's faces so a turning wheel shows it
        var tyre = Shapes.Mat(Tyre, roughness: 0.9f);
        var hub = Shapes.Mat(Arm, metallic: 0.3f, roughness: 0.5f);
        var spoke = Shapes.Mat(Spoke, roughness: 0.6f);
        float r = (float)RoverSpec.WheelRadius, w = (float)RoverSpec.WheelWidth;
        for (int k = 0; k < _wheels.Count; k++)
        {
            var wheel = _wheels[k];
            Part(wheel, $"wheel-{k}-tyre", Shapes.Cylinder(r, w, tyre));
            Part(wheel, $"wheel-{k}-hub", Shapes.Cylinder(r * 0.55f, w + 0.02f, hub));
            // the cylinder's axis is y, so the spokes lie in x-z: three bars through the centre, 60 degrees apart
            for (int s = 0; s < 3; s++)
            {
                var bar = Part(wheel, $"wheel-{k}-spoke-{s}", Shapes.Box(new Vector3(r * 1.9f, w + 0.03f, 0.025f), spoke));
                bar.Rotation = new Vector3(0, s * Mathf.Pi / 3, 0);
            }
        }
    }

    // the arm's meshes are built with it (Rover.Backhoe.cs), through the same tagging
    private MeshInstance3D ArmPart(Node parent, string id, Vector3 size, Color color, Vector3 at) =>
        Part(parent, id, Shapes.Box(size, Shapes.Mat(color, metallic: color == Arm ? 0.3f : 0, roughness: 0.5f)), at);
}
