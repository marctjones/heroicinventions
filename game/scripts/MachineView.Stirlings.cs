using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Hot-air engines (issue #65): an iron cylinder with its receiver on top,
/// facing up to the mirrors, and a flywheel on the side that turns at the
/// sim's speed. The receiver's face warms from a cold grey-blue to a
/// hot-iron red as its temperature climbs (these run at tens of degrees,
/// not glowing heat); a label gives the hot end, the work and the speed.
/// </summary>
public partial class MachineView
{
    private readonly List<(StirlingEngine Engine, StandardMaterial3D Face, Node3D Flywheel, Label3D Label)> _stirlingViews = [];

    private void BuildStirlings()
    {
        foreach (var (id, engine) in Runtime.Stirlings)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var at = V(part.At);
            float r = Mathf.Clamp(Mathf.Sqrt((float)engine.Aperture / Mathf.Pi), 0.1f, 1f);
            var body = Shapes.Cylinder(r * 0.6f, 0.5f, Surface(part.Material));
            body.Position = at + new Vector3(0, 0.25f, 0);
            AddChild(body);
            var face = Shapes.Mat(new Color(0.25f, 0.28f, 0.32f), metallic: 0.4f, roughness: 0.5f);
            var receiver = Shapes.Cylinder(r, 0.06f, face);
            receiver.Position = at + new Vector3(0, 0.53f, 0);
            AddChild(receiver);
            // a flywheel on a horizontal shaft beside it, spokes to show it turning
            var flywheel = new Node3D { Position = at + new Vector3(r * 0.6f + 0.12f, 0.3f, 0) };
            var iron = Surface("iron");
            var rim = Shapes.Cylinder(0.25f, 0.05f, iron);
            rim.Rotation = new Vector3(0, 0, Mathf.Pi / 2);
            flywheel.AddChild(rim);
            var spoke = Shapes.Box(new Vector3(0.06f, 0.46f, 0.03f), Shapes.Mat(new Color(0.12f, 0.1f, 0.09f)));
            spoke.Position = new Vector3(0.03f, 0, 0);
            flywheel.AddChild(spoke);
            AddChild(flywheel);
            var label = new Label3D
            {
                Position = at + new Vector3(0, 1.3f, 0),
                FontSize = 24, OutlineSize = 6, PixelSize = 0.01f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            };
            AddChild(label);
            _stirlingViews.Add((engine, face, flywheel, label));
        }
    }

    private void DrawStirlings()
    {
        foreach (var (engine, face, flywheel, label) in _stirlingViews)
        {
            // −60 °C a cold grey-blue, 200 °C a hot-iron red
            float warm = Mathf.Clamp(((float)engine.HotTemperature + 60) / 260, 0, 1);
            face.AlbedoColor = new Color(0.25f, 0.28f, 0.32f).Lerp(new Color(0.75f, 0.2f, 0.08f), warm);
            flywheel.Rotation = new Vector3((float)engine.Angle, 0, 0);
            label.Text = $"{engine.Name}: {engine.HotTemperature:0} °C hot, {engine.ColdTemperature:0} °C cold\n{engine.ShaftPower:0} W at {engine.Rpm:0} rpm";
        }
    }
}
