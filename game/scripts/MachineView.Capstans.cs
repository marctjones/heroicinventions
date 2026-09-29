using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// A bollard with a rope round it (<see cref="Capstan"/>): the post, a coil
/// of rope showing its turns, the load hanging from one end, and a hauler
/// holding the other, standing off towards the camera. The rope to the
/// load lengthens as it runs out and shortens as it's hauled in.
/// </summary>
public partial class MachineView
{
    private readonly List<(Capstan capstan, MeshInstance3D load, MeshInstance3D fall, Vector3 top, float side)> _capstans = [];

    private void BuildCapstan(PartSpec part)
    {
        var capstan = Runtime.Capstans[part.Id];
        var wood = Surface(part.Material);
        var rope = Surface(part.Symbol("rope", "hemp"));
        var top = V(part.At);
        float r = (float)part.Number("radius", 0.15);

        var post = Shapes.Cylinder(r, top.Y + 0.25f, wood);
        post.Position = new Vector3(top.X, (top.Y + 0.25f) / 2, top.Z);
        AddChild(post);
        int rings = Math.Max(1, (int)Math.Round(capstan.Turns * 3));
        for (int i = 0; i < rings; i++)
        {
            var coil = Shapes.Cylinder(r + 0.025f, 0.03f, rope);
            coil.Position = top + new Vector3(0, -0.04f * i, 0);
            AddChild(coil);
        }

        // the hauler's end runs back and to the left, to a figure standing on the ground holding it
        var feet = new Vector3(top.X - 0.8f, 0, top.Z - 0.7f);
        var hand = feet + new Vector3(0.2f, 1.1f, 0);
        AddChild(Shapes.Rod(top + new Vector3(-r, 0, 0), hand, 0.015f, rope));
        var body = Shapes.Mat(new Color(0.35f, 0.3f, 0.45f), roughness: 0.9f);
        AddChild(Shapes.Rod(feet, feet + new Vector3(0, 1.45f, 0), 0.13f, body));
        var head = Shapes.Sphere(0.12f, body);
        head.Position = feet + new Vector3(0, 1.62f, 0);
        AddChild(head);

        // the load's end drops from the post's +X side
        float side = (float)Math.Cbrt(capstan.LoadMass / 2700.0);
        var load = Shapes.Box(new Vector3(side, side, side), Surface("granite"));
        AddChild(load);
        var fall = Shapes.Rod(Vector3.Zero, Vector3.Up, 0.015f, rope);
        AddChild(fall);
        _capstans.Add((capstan, load, fall, top + new Vector3(r, 0, 0), side));
        AddLabel($"{part.Id}: {capstan.Turns:0.##} turn{(capstan.Turns == 1 ? "" : "s")}", top + new Vector3(0, 0.6f, 0));
    }

    private void DrawCapstans()
    {
        foreach (var (capstan, load, fall, top, side) in _capstans)
        {
            var hook = new Vector3(top.X, (float)capstan.Height + side, top.Z);
            load.Position = hook - new Vector3(0, side / 2, 0);
            // a unit rod along +Y centred on its middle, stretched from the hook up to the post
            float len = Math.Max(0.01f, top.Y - hook.Y);
            fall.Position = hook + new Vector3(0, len / 2, 0);
            fall.Scale = new Vector3(1, len, 1);
        }
    }
}
