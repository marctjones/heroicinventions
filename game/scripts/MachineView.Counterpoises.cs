using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// Heron's temple doors: a pair of leaves on upright spindles, swung open
/// together by the counterpoise's turning (<see cref="Counterpoise"/>). A
/// rope runs from the spindle over a pulley down to the hanging vessel,
/// and another to the counterweight, a stone block on the far side.
/// </summary>
public partial class MachineView
{
    private readonly List<(Counterpoise cp, Node3D left, Node3D right, MeshInstance3D weight, Vector3 weightAt, MeshInstance3D rope, Vector3 pulley)> _counterpoises = [];

    private void BuildCounterpoise(PartSpec part)
    {
        var cp = Runtime.Counterpoises[part.Id];
        var wood = Surface(part.Material);
        var stone = Surface("granite");
        var at = V(part.At);
        float w = (float)part.Number("leaf-width", 1), h = (float)part.Number("leaf-height", 2);

        // the doorway: two jambs and a lintel, the spindles standing at the jambs
        foreach (float side in new[] { -1f, 1f })
        {
            var jamb = Shapes.Box(new Vector3(0.15f, h + 0.2f, 0.25f), stone);
            jamb.Position = new Vector3(at.X + side * (w + 0.08f), (h + 0.2f) / 2, at.Z);
            AddChild(jamb);
        }
        var lintel = Shapes.Box(new Vector3(2 * w + 0.5f, 0.2f, 0.3f), stone);
        lintel.Position = new Vector3(at.X, h + 0.3f, at.Z);
        AddChild(lintel);

        Node3D Leaf(float side)
        {
            var hinge = new Node3D { Position = new Vector3(at.X + side * w, 0, at.Z) };
            AddChild(hinge);
            var leaf = Shapes.Box(new Vector3(w * 0.98f, h, 0.06f), wood);
            leaf.Position = new Vector3(-side * w / 2, h / 2 + 0.05f, 0);
            hinge.AddChild(leaf);
            hinge.AddChild(Shapes.Rod(new Vector3(0, 0, 0), new Vector3(0, h + 0.2f, 0), (float)cp.Radius, wood));
            return hinge;
        }
        var left = Leaf(-1); var right = Leaf(1);

        // the vessel's rope drops from a pulley above it; the counterweight hangs behind the doors
        var vessel = Runtime.Def.Part(cp.Vessel.Name)!;
        var pulley = new Vector3((float)vessel.At.X, h + 0.3f, (float)vessel.At.Z);
        var rope = Shapes.Cylinder(0.008f, 1, Shapes.Mat(new Color(0.55f, 0.45f, 0.3f)));
        AddChild(rope);
        AddChild(Shapes.Rod(pulley, new Vector3(at.X + w, h + 0.3f, at.Z), 0.008f, Shapes.Mat(new Color(0.55f, 0.45f, 0.3f))));
        float side3 = Mathf.Pow((float)cp.Counterweight / 2500f, 1 / 3f);
        var weight = Shapes.Box(Vector3.One * side3, stone);
        var weightAt = new Vector3(at.X - w, 0.4f + side3 / 2, at.Z - 0.4f);
        AddChild(weight);
        _counterpoises.Add((cp, left, right, weight, weightAt, rope, pulley));
        AddLabel(part.Id, new Vector3(at.X, h + 0.55f, at.Z));
    }

    private void DrawCounterpoises()
    {
        foreach (var (cp, left, right, weight, weightAt, rope, pulley) in _counterpoises)
        {
            float a = (float)cp.Angle;
            left.Rotation = new Vector3(0, a, 0);    // both swing inward, away from the altar side
            right.Rotation = new Vector3(0, -a, 0);
            weight.Position = weightAt + Vector3.Up * (float)(cp.Radius * cp.Angle);
            float top = (float)(cp.Vessel.BaseElevation + cp.Vessel.Height);
            float len = Mathf.Max(pulley.Y - top, 0.01f);
            rope.Scale = new Vector3(1, len, 1);
            rope.Position = new Vector3(pulley.X, top + len / 2, pulley.Z);
        }
    }
}
