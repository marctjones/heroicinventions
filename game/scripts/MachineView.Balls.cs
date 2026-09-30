using Godot;

namespace HeroicInventions;

/// <summary>Loose balls (issue #52): free spherical bodies that roll down ramps and grooves and drop onto things. See <see cref="MaterialBall"/>.</summary>
public partial class MachineView
{
    private void BuildBall(Sim.Machines.PartSpec part)
    {
        float radius = (float)part.Number("radius");
        var ball = new MaterialBall(_materials[part.Material], radius, Shapes.ColorFor(part.Material))
        {
            Name = part.Id,
            Position = V(part.At),
            Freeze = true,
        };
        foreach (var visual in ball.GetChildren().OfType<MeshInstance3D>().Take(1))
            visual.MaterialOverride = PartSurface(part, radius * 2);
        AddChild(ball);
        string materialName = char.ToUpper(part.Material[0]) + part.Material[1..];
        AddLabel($"{materialName}\n{ball.Mass:0.##} kg", new Vector3(0, radius + 0.05f, 0), ball);
        _freezable.Add(ball);
        _bodiesById[part.Id] = ball;
    }
}
