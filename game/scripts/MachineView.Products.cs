using Godot;
using HeroicInventions.Sim.Fluids;

namespace HeroicInventions;

/// <summary>
/// What a machine produces, drawn as a quantity in the scene (#173): one rule, <b>accumulation has a size</b>.
/// - a <b>heap</b> grows with the mass collected: its volume is the mass over the material's density (gold behind the
///   riffles at 19,300 kg/m³, the sand beside it at 2,650, flour loose-poured at 600);
/// - a <b>level</b> rises: every tank draws its water at its level (Refresh), and a vessel holding any water at all
///   shows a film, so a gutter that has caught a few millilitres is seen to have caught them;
/// - a <b>pile</b> shrinks with its fuel (hearths) and plants grow with their biomass (Greenhouse), as before.
/// Heaps are exact: a volume read back from the mesh's scale equals mass ÷ density. That is a few cubic centimetres
/// for the gold a placer box keeps in a minute, so a glint ring (below) marks the spot, and says nothing else.
/// </summary>
public partial class MachineView
{
    /// <summary>A heap as a flattened box 1.6 : 0.4 : 1.6, its volume exactly <c>kg / density</c> (box volume is the product of its scale).</summary>
    private static Vector3 Heap(double kg, double density)
    {
        float k = Mathf.Max(1e-5f, (float)Math.Cbrt(kg / density / (1.6 * 0.4 * 1.6)));
        return new Vector3(1.6f * k, 0.4f * k, 1.6f * k);
    }

    /// <summary>
    /// The least a vessel's water is drawn at once it holds any (5 mL or more): a film 4 mm deep, drawn thicker than
    /// life, as the ice sheet is. Above that it is the true level.
    /// </summary>
    private static float ShownLevel(Tank tank) =>
        tank.WaterVolume >= 5e-6 ? Mathf.Max((float)tank.Level, 0.004f) : Mathf.Max((float)tank.Level, 0.001f);

    private readonly Dictionary<SluiceBox, MeshInstance3D> _glints = [];

    /// <summary>
    /// A ring of gold dust over the heap, there only while gold is kept: 14 cm across at least (a pinch is a
    /// cubic centimetre), and wider as the cube root of the heap up to the channel's width. Its own material.
    /// </summary>
    private void BuildGoldGlint(SluiceBox box, MeshInstance3D gold)
    {
        var mat = Shapes.Mat(new Color(1f, 0.78f, 0.08f), metallic: 0.2f, roughness: 0.4f, alpha: 0.9f, outline: false);
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;   // gold, whatever the light and the water over it
        mat.RenderPriority = 10;   // after the water over it, whichever the camera is nearer to
        var ring = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 0.002f, RadialSegments = 24 },
            MaterialOverride = mat,
            Visible = false,
        };
        ring.Position = gold.Position;
        ring.SetMeta("floor", gold.Position.Y - 0.01f);   // the floor board's top
        AddChild(ring);
        _glints[box] = ring;
    }

    private void DrawGoldGlint(SluiceBox box, MeshInstance3D gold, MeshInstance3D sand)
    {
        if (!_glints.TryGetValue(box, out var ring)) return;
        // both heaps sit on the floor, however big they are; the ring floats on the water over the gold, which would hide it
        float floor = (float)ring.GetMeta("floor");
        gold.Position = gold.Position with { Y = floor + gold.Scale.Y / 2 };
        sand.Position = sand.Position with { Y = floor + sand.Scale.Y / 2 };
        ring.Visible = box.KeptHeavy > 0;
        ring.Position = ring.Position with { Y = floor + Mathf.Max((float)box.Channel.Depth, 0.002f) + 0.003f };
        float radius = Mathf.Clamp(2.5f * Mathf.Max(gold.Scale.X, gold.Scale.Z), 0.07f, (float)box.Channel.Width / 2);
        ring.Scale = new Vector3(radius, 1, radius);
        if (FlowReport && Runtime.Time >= _nextProductReport)
        {
            _nextProductReport = Math.Floor(Runtime.Time / 5) * 5 + 5;
            // a volume read back from what is drawn, against the sim's own mass over its density
            double drawn = gold.Scale.X * gold.Scale.Y * gold.Scale.Z, sim = box.KeptHeavy / box.HeavyDensity;
            GD.Print($"[product] t={Runtime.Time:F2} {box.Name}: gold kept {box.KeptHeavy * 1000:F1} g; heap mesh volume {drawn * 1e6:F4} cm3 vs {box.KeptHeavy:F5} kg / {box.HeavyDensity:F0} = {sim * 1e6:F4} cm3; "
                + $"sand {box.KeptLight:F4} kg: {box.KeptLight / box.LightDensity * 1e6:F3} cm3");
        }
    }

    private double _nextProductReport;
    private double _nextFlourReport;
    private bool FlourReportDue => FlowReport && Runtime.Time >= _nextFlourReport;

    /// <summary>With HEROIC_FLOW_REPORT: the flour heap's height and volume read back from the mesh against the trace's flour mass.</summary>
    private void ReportFlour(Millstone m)
    {
        double v = Math.PI * m.Heap.Scale.X * m.Heap.Scale.X * m.Heap.Scale.Y / 3;
        GD.Print($"[product] t={Runtime.Time:F2} {m.Stone.Name}: flour {m.Flour:F4} kg; heap {m.Heap.Scale.Y * 100:F2} cm high, {v * 1000:F3} L = {m.Flour:F4} kg / 600 kg/m3 = {m.Flour / 600 * 1000:F3} L");
    }

    // ---- oxygen: a plant's breath, there only while it is making any ------------------------------------------

    private readonly List<(HeroicInventions.Sim.Thermo.Plants Plants, GpuParticles3D Breath)> _oxygenViews = [];

    /// <summary>
    /// Pale green-white motes rising from a plant bed while its growth outruns its respiration, which is exactly when
    /// the sim adds oxygen to the room (<c>Plants.OxygenMade</c>); none in the dark or the frost. Its own cloud.
    /// </summary>
    private void BuildProducts()
    {
        foreach (var (id, plants) in Runtime.Plants)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            float side = Mathf.Sqrt((float)plants.Area);
            var breath = SteamCloud(V(part.At) + new Vector3(0, 1.4f, 0), amount: 24, radius: side * 0.35f, lifetime: 2.5f);
            if (breath.DrawPass1 is SphereMesh { Material: StandardMaterial3D m })
                m.AlbedoColor = new Color(0.78f, 1f, 0.88f, 0.4f);
            _oxygenViews.Add((plants, breath));
            _building = null;
        }
    }

    private void DrawProducts()
    {
        foreach (var (plants, breath) in _oxygenViews)
            breath.Emitting = plants.GrowthRate > plants.Respiration * plants.Area;
    }
}
