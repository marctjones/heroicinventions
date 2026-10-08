using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// The greenhouse (issue #42), seen. Trees stand in their bed and grow as
/// their wood does (a crown's size goes as the cube root of the wood per
/// square metre), dark and still while they are frozen or in the dark, a
/// brighter green while they are growing; cut, they drop back to stumps. A
/// melter glows while it melts; an electrolyser bubbles while it splits.
/// </summary>
public partial class MachineView
{
    private readonly List<(Plants Plants, List<Node3D> Trees, StandardMaterial3D Leaves)> _plantViews = [];
    private readonly List<(Melter Melter, StandardMaterial3D Glow)> _melterViews = [];
    private readonly List<(Electrolyser Cell, GpuParticles3D Bubbles)> _electrolyserViews = [];

    private void BuildGreenhouse()
    {
        foreach (var (id, plants) in Runtime.Plants)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            float side = Mathf.Sqrt((float)plants.Area);
            var bed = Shapes.Box(new Vector3(side, 0.15f, side), Shapes.Mat(new Color(0.3f, 0.22f, 0.15f)));
            bed.Position = V(part.At) + new Vector3(0, 0.075f, 0);
            AddChild(bed);
            var bark = Shapes.Mat(new Color(0.4f, 0.3f, 0.2f));
            var leaves = Shapes.Mat(new Color(0.2f, 0.45f, 0.18f));
            leaves.EmissionEnabled = true;
            leaves.Emission = new Color(0.25f, 0.6f, 0.2f);
            var trees = new List<Node3D>();
            int across = Math.Max(1, (int)Math.Round(side / 0.7f));
            for (int i = 0; i < across * across; i++)
            {
                var tree = new Node3D { Position = V(part.At) + new Vector3(((i % across) + 0.5f) / across * side - side / 2, 0.15f, ((i / across) + 0.5f) / across * side - side / 2) };
                var trunk = Shapes.Cylinder(0.04f, 1f, bark);
                trunk.Position = new Vector3(0, 0.5f, 0);
                tree.AddChild(trunk);
                var crown = Shapes.Sphere(0.3f, leaves);
                crown.Position = new Vector3(0, 1.1f, 0);
                tree.AddChild(crown);
                AddChild(tree);
                trees.Add(tree);
            }
            AddLabel(id, V(part.At) + new Vector3(0, 1.6f, 0));
            _plantViews.Add((plants, trees, leaves));
        }
        foreach (var (id, melter) in Runtime.Melters)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var glow = Shapes.Mat(new Color(0.35f, 0.33f, 0.32f), metallic: 0.6f);
            glow.EmissionEnabled = true;
            glow.Emission = new Color(1f, 0.4f, 0.1f);
            var drum = Shapes.Cylinder(0.2f, 0.5f, glow);
            drum.Position = V(part.At) + new Vector3(0, 0.25f, 0);
            AddChild(drum);
            AddLabel(id, V(part.At) + new Vector3(0, 0.7f, 0));
            _melterViews.Add((melter, glow));
        }
        foreach (var (id, cell) in Runtime.Electrolysers)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var jar = Shapes.Box(new Vector3(0.3f, 0.4f, 0.3f), Shapes.Mat(new Color(0.8f, 0.9f, 0.95f), roughness: 0.1f, alpha: 0.4f));
            jar.Position = V(part.At) + new Vector3(0, 0.2f, 0);
            AddChild(jar);
            var bubbles = SteamCloud(V(part.At) + new Vector3(0, 0.1f, 0), amount: 30, radius: 0.08f, lifetime: 0.6f);
            AddLabel(id, V(part.At) + new Vector3(0, 0.6f, 0));
            _electrolyserViews.Add((cell, bubbles));
        }
    }

    private void DrawGreenhouse()
    {
        foreach (var (plants, trees, leaves) in _plantViews)
        {
            // 1 kg of wood a m² is a full-grown coppice: crowns 1:1; a bare bed shows stumps
            float grown = Mathf.Clamp(Mathf.Pow((float)(plants.Wood / plants.Area), 1f / 3), 0.08f, 1.4f);
            foreach (var t in trees) t.Scale = new Vector3(grown, grown, grown);
            leaves.EmissionEnergyMultiplier = plants.GrowthRate > 0 ? 0.35f : 0;
        }
        foreach (var (melter, glow) in _melterViews)
            glow.EmissionEnergyMultiplier = melter.Rate > 0 ? 0.8f : 0;
        foreach (var (cell, bubbles) in _electrolyserViews)
            bubbles.Emitting = cell.Rate > 0;
    }
}
