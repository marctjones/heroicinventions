using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// A windmill, turned by the sim (<see cref="Windmill"/>) rather than by
/// Jolt: four sails on stocks crossing at a hub, facing the wind along +Z
/// (towards the default camera), turning in the X–Y plane. Each sail is a
/// lattice of bars — a whip down the middle, cross-bars, and a cloth
/// panel on the lattice's outer part.
/// </summary>
public partial class MachineView
{
    private readonly List<(Windmill mill, Node3D sails)> _windmills = [];

    private void BuildWindmill(PartSpec part)
    {
        var mill = Runtime.Windmills[part.Id];
        var wood = Surface(part.Material);
        var cloth = Shapes.Mat(new Color(0.92f, 0.88f, 0.78f), roughness: 0.95f);
        var hub = V(part.At);
        float r = (float)mill.Radius;

        // the windshaft, running back into the buck behind the sails
        AddChild(Shapes.Rod(hub, hub + new Vector3(0, 0.15f, -2.5f), 0.25f, wood));

        var sails = new Node3D { Position = hub };
        AddChild(sails);
        sails.AddChild(Shapes.Sphere(0.45f, wood));
        const int count = 4;
        float width = r * 0.22f;
        for (int i = 0; i < count; i++)
        {
            float a = i * Mathf.Tau / count;
            var arm = new Node3D { Rotation = new Vector3(0, 0, -a) };
            sails.AddChild(arm);
            arm.AddChild(Shapes.Rod(Vector3.Zero, new Vector3(0, r, 0), 0.12f, wood));
            // cross-bars, and the whip-side rail they meet
            for (int b = 1; b <= 8; b++)
            {
                float y = r * (0.2f + 0.8f * b / 8f);
                var bar = Shapes.Box(new Vector3(width, 0.06f, 0.06f), wood);
                bar.Position = new Vector3(width / 2, y, 0);
                arm.AddChild(bar);
            }
            arm.AddChild(Shapes.Rod(new Vector3(width, r * 0.2f, 0), new Vector3(width, r, 0), 0.05f, wood));
            var panel = Shapes.Box(new Vector3(width * 0.95f, r * 0.8f, 0.02f), cloth);
            panel.Position = new Vector3(width / 2, r * 0.6f, -0.05f);
            arm.AddChild(panel);
        }
        _windmills.Add((mill, sails));
        if (part.Props.GetValueOrDefault("wind-from-map") is SBool { Value: true })
        {
            // a mill that takes the map's wind (issue #61) shows what it is getting, which changes with the gusts and the hour
            var label = new Label3D
            {
                Position = hub + new Vector3(0, r + 0.8f, 0), FontSize = 24, OutlineSize = 6, PixelSize = 0.0025f * Mathf.Max(1, r / 2),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            };
            AddChild(label);
            _fieldMillLabels.Add((part.Id, mill, label));
        }
        else AddLabel(part.Id, hub + new Vector3(0, r + 0.8f, 0));
    }

    private readonly List<(string Id, Windmill Mill, Label3D Label)> _fieldMillLabels = [];

    private void DrawWindmills()
    {
        // seen from the wind's side the sails turn anticlockwise, as English mills' do
        foreach (var (mill, sails) in _windmills)
            sails.Rotation = new Vector3(0, 0, (float)mill.Angle);
        foreach (var (id, mill, label) in _fieldMillLabels)
            label.Text = $"{id}\nwind {mill.Wind:F1} m/s\n{mill.WindPower * mill.PowerCoefficient / 1000:F2} kW";
    }
}
