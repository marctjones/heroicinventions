using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// A windmill, turned by the sim (<see cref="Windmill"/>) rather than by
/// Jolt: four sails on stocks crossing at a hub, turning in the plane across
/// their axle. Built facing +Z (towards the default camera), the whole mill
/// then yaws about the vertical at the hub to wherever the sim has it facing
/// (issue #193): a mill with a vane swings into the wind, a fixed one stays.
/// A tail vane on the back of a mill that has one, and an arrow above every
/// mill that points the way the wind blows. Each sail is a
/// lattice of bars — a whip down the middle, cross-bars, and a cloth
/// panel on the lattice's outer part.
/// </summary>
public partial class MachineView
{
    private readonly List<(Windmill mill, Node3D sails)> _windmills = [];
    private readonly List<(Windmill mill, Node3D yaw, Node3D arrow)> _windmillYaw = [];

    private void BuildWindmill(PartSpec part)
    {
        var mill = Runtime.Windmills[part.Id];
        var wood = Surface(part.Material);
        var cloth = Shapes.Mat(new Color(0.92f, 0.88f, 0.78f), roughness: 0.95f);
        var hub = V(part.At);
        float r = (float)mill.Radius;

        // everything that turns with the mill's facing hangs from a pivot at the hub; the sails face +Z in it
        var yaw = new Node3D { Position = hub };
        AddChild(yaw);

        // the windshaft, running back into the buck behind the sails
        yaw.AddChild(Shapes.Rod(Vector3.Zero, new Vector3(0, 0.15f, -2.5f), 0.25f, wood));
        if (mill.Vane)
        {
            // a tail vane: a boom out behind the buck and a fin at its end, which the wind pushes round to lie downwind
            var fin = Shapes.Mat(new Color(0.55f, 0.18f, 0.12f), roughness: 0.9f);
            float boom = 2.5f + r * 0.9f;
            yaw.AddChild(Shapes.Rod(new Vector3(0, 0.15f, -2.5f), new Vector3(0, 0.15f, -boom), 0.1f, wood));
            var blade = Shapes.Box(new Vector3(0.08f, r * 0.35f, r * 0.3f), fin);
            blade.Position = new Vector3(0, 0.15f, -boom);
            yaw.AddChild(blade);
        }

        var sails = new Node3D();
        yaw.AddChild(sails);
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

        // the wind itself: a bright arrow above the mill along the way it blows (built along +Z, turned each frame)
        var arrowMat = Shapes.Mat(new Color(0.15f, 0.75f, 0.95f), roughness: 0.5f);
        var arrow = new Node3D { Position = hub + new Vector3(0, r * 1.35f, 0) };
        float len = r * 0.9f;
        arrow.AddChild(Shapes.Rod(new Vector3(0, 0, -len / 2), new Vector3(0, 0, len / 2), 0.25f, arrowMat));
        foreach (float side in new[] { -1f, 1f })
            arrow.AddChild(Shapes.Rod(new Vector3(0, 0, len / 2), new Vector3(side * len * 0.18f, 0, len / 2 - len * 0.25f), 0.25f, arrowMat));
        AddChild(arrow);
        _windmillYaw.Add((mill, yaw, arrow));
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
        foreach (var (mill, yaw, arrow) in _windmillYaw)
        {
            // azimuth a (from +x toward +z) is a turn of 90 - a degrees about the vertical from +Z; the wind blows from its azimuth towards the opposite one
            yaw.Rotation = new Vector3(0, Mathf.DegToRad((float)(90 - mill.FacingDeg)), 0);
            arrow.Rotation = new Vector3(0, Mathf.DegToRad((float)(90 - mill.WindFromDeg + 180)), 0);
        }
        foreach (var (id, mill, label) in _fieldMillLabels)
            label.Text = $"{id}\nwind {mill.Wind:F1} m/s from {mill.WindFromDeg:F0}°, facing {mill.FacingDeg:F0}°\n{mill.WindPower * mill.PowerCoefficient / 1000:F2} kW";
    }
}
