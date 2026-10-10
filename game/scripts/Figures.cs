using Godot;

namespace HeroicInventions;

/// <summary>
/// The friendly person of the scale figure (art direction 12.19), shared by everything that draws a person: the scale figure, a
/// capstan's hauler and a digging gang (12.20). Bold shapes, all outlined: legs and shoes, a tunic with a belt, arms with hands, a
/// head with dot eyes and a straw sun hat. 1.7 m from the soles to the top of the head at <c>scale</c> 1 (the hat's crown adds 2
/// cm), 0.4 m across the shoulders. It faces +Z, its feet at the origin. Drawn only: a person here is never a body.
/// </summary>
public static class Figures
{
    public const float Height = 1.7f;

    /// <summary>How a figure holds its arms.</summary>
    public enum Pose { Standing, Hauling, Digging }

    /// <summary>A person in a <paramref name="tunicHtml"/> tunic (the scale figure's slate blue by default).</summary>
    public static Node3D Person(string tunicHtml = "#62808F", Pose pose = Pose.Standing, float scale = 1f)
    {
        var figure = new Node3D { Name = "Person", Scale = Vector3.One * scale };
        var tunic = Shapes.Mat(Color.FromHtml(tunicHtml), roughness: 0.9f);
        var legs = Shapes.Mat(Color.FromHtml("#47505A"), roughness: 0.9f);
        var leather = Shapes.Mat(Color.FromHtml("#3E3029"), roughness: 0.9f);
        var skin = Shapes.Mat(Color.FromHtml("#D6A27C"), roughness: 0.8f);
        var straw = Shapes.Mat(Color.FromHtml("#B48A52"), roughness: 0.9f);
        const float head = 0.115f;
        void Add(MeshInstance3D m, Vector3 at, Basis? turn = null)
        {
            m.Position = at;
            if (turn is { } b) m.Basis = b;
            m.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            figure.AddChild(m);
        }
        MeshInstance3D Capsule(float r, float h, StandardMaterial3D mat) => new() { Mesh = new CapsuleMesh { Radius = r, Height = h }, MaterialOverride = mat };
        foreach (float x in new[] { -0.085f, 0.085f })
        {
            Add(Capsule(0.07f, 0.86f, legs), new Vector3(x, 0.45f, 0));                            // legs, 0.02 to 0.88 m
            Add(Shapes.Box(new Vector3(0.11f, 0.06f, 0.2f), leather), new Vector3(x, 0.03f, 0.03f));   // shoes, toes forward
        }
        Add(Capsule(0.17f, 0.72f, tunic), new Vector3(0, 1.15f, 0));                                // tunic, 0.79 to 1.51 m
        Add(Shapes.Cylinder(0.172f, 0.05f, leather), new Vector3(0, 1.02f, 0));                      // belt
        foreach (float x in new[] { -1f, 1f })
        {
            if (pose == Pose.Standing)
            {
                Add(Capsule(0.05f, 0.6f, tunic), new Vector3(x * 0.215f, 1.17f, 0), new Basis(Vector3.Back, x * 0.12f));   // arms, hanging a little out
                Add(Shapes.Sphere(0.05f, skin), new Vector3(x * 0.25f, 0.86f, 0));                                         // hands
            }
            else
            {
                // arms reaching forward and down from the shoulder (1.42 m) to the hands at waist height in front: on a rope or a spade's haft
                var shoulder = new Vector3(x * 0.2f, 1.42f, 0);
                var hand = pose == Pose.Hauling ? new Vector3(x * 0.1f, 1.08f, 0.42f) : new Vector3(x * 0.06f, 1.0f - (x + 1) * 0.12f, 0.3f);
                var arm = Capsule(0.05f, (hand - shoulder).Length() + 0.1f, tunic);
                var along = (hand - shoulder).Normalized();
                var side = along.Cross(Vector3.Right).Normalized();
                Add(arm, (shoulder + hand) / 2, new Basis(along.Cross(side).Normalized(), along, side).Orthonormalized());
                Add(Shapes.Sphere(0.05f, skin), hand);
            }
        }
        Add(Shapes.Cylinder(0.045f, 0.08f, skin), new Vector3(0, 1.5f, 0));                           // neck
        Add(Shapes.Sphere(head, skin), new Vector3(0, Height - head, 0));                              // head, its top at 1.7 m
        var dark = Shapes.Mat(Color.FromHtml("#1E1A18"), roughness: 0.6f, outline: false);
        foreach (float x in new[] { -0.04f, 0.04f })
            Add(Shapes.Sphere(0.016f, dark), new Vector3(x, Height - head + 0.015f, head * 0.93f));   // eyes
        Add(Shapes.Cylinder(0.21f, 0.015f, straw), new Vector3(0, Height - 0.075f, 0));                // the hat's brim
        Add(Shapes.Cylinder(0.11f, 0.09f, straw), new Vector3(0, Height - 0.025f, 0));                 // and its low crown
        if (pose == Pose.Digging)
        {
            // a spade held across the body: the haft from the hands down to a blade at the feet in front
            var wood = Shapes.Mat(Color.FromHtml("#8A6A45"), roughness: 0.9f);
            var iron = Shapes.Mat(Color.FromHtml("#5E6166"), metallic: 0.4f, roughness: 0.6f);
            var top = new Vector3(0.08f, 1.0f, 0.32f);
            var foot = new Vector3(0, 0.18f, 0.5f);
            var haft = Shapes.Rod(top, foot, 0.018f, wood);
            figure.AddChild(haft);
            var blade = Shapes.Box(new Vector3(0.2f, 0.26f, 0.02f), iron);
            blade.Position = foot + (foot - top).Normalized() * 0.1f;
            blade.Basis = new Basis(Vector3.Right, Mathf.Atan2(foot.Z - top.Z, top.Y - foot.Y));
            figure.AddChild(blade);
        }
        return figure;
    }
}
