using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// A sluice box (issue #53): a wooden trough with cross riffles set in its
/// channel, the gold it has caught heaped behind the riffles (a pile that
/// grows with the mass kept) and the sand it has caught beside it, with a
/// tag giving the flow's drag and the cut-off density: what is denser stays.
/// </summary>
public partial class MachineView
{
    private readonly List<(SluiceBox Box, MeshInstance3D Gold, MeshInstance3D Sand, Label3D Tag)> _sluiceBoxViews = [];

    private void BuildSluiceBox(PartSpec part)
    {
        var box = Runtime.SluiceBoxes[part.Id];
        var at = V(part.At);
        float w = (float)box.Channel.Width, len = 1.2f;
        var wood = Surface(part.Material);
        var floor = Shapes.Box(new Vector3(len, 0.02f, w), wood);
        floor.Position = at;
        AddChild(floor);
        for (int k = 0; k < 6; k++)
        {
            var riffle = Shapes.Box(new Vector3(0.025f, 0.03f, w), wood);
            riffle.Position = at + new Vector3(-len / 2 + (k + 0.5f) * len / 6, 0.025f, 0);
            AddChild(riffle);
        }
        var gold = Shapes.Box(new Vector3(1, 1, 1), Shapes.Mat(new Color(0.95f, 0.78f, 0.2f), metallic: 0.9f, roughness: 0.3f));
        gold.Position = at + new Vector3(-len / 4, 0.02f, 0);
        AddChild(gold);
        var sand = Shapes.Box(new Vector3(1, 1, 1), Shapes.Mat(Shapes.ColorFor("sand")));
        sand.Position = at + new Vector3(len / 4, 0.02f, 0);
        AddChild(sand);
        var tag = new Label3D { Position = at + new Vector3(0, 0.35f, 0), FontSize = 24, OutlineSize = 6, PixelSize = 0.0025f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
        AddChild(tag);
        _sluiceBoxViews.Add((box, gold, sand, tag));
    }

    private void DrawSluiceBoxes()
    {
        foreach (var (box, gold, sand, tag) in _sluiceBoxViews)
        {
            // a heap as big as what's kept (grains and pores, ~60% solid), flattened behind the riffles
            static Vector3 Heap(double kg, double density) { float s = Mathf.Max(0.001f, (float)Math.Cbrt(kg / (density * 0.6))); return new Vector3(s * 1.6f, s * 0.4f, s * 1.6f); }
            gold.Scale = Heap(box.KeptHeavy, box.HeavyDensity);
            sand.Scale = Heap(box.KeptLight, box.LightDensity);
            tag.Text = $"{box.Name} · {box.Shear:F2} Pa · keeps what's over {box.Cutoff:F0} kg/m³\ngold {box.KeptHeavy * 1000:F0} g kept, {box.PassedHeavy * 1000:F0} g lost · sand {box.KeptLight:F2} kg caught";
        }
    }
}
