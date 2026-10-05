using Godot;
using HeroicInventions.Sim.Fluids;

namespace HeroicInventions;

/// <summary>
/// A drain (issue #90): an iron grate flush with the ground, its bars across a dark pit, and while water runs into it
/// a blue swirl over it, wider the more it takes (its radius going as the flow's square root, as a pool's would).
/// Its tag reads how much it is taking, and how deep the water stands over it. The cistern it fills is the tank
/// #:into, drawn as any tank is, wherever it stands (usually sunk in the ground under it).
/// </summary>
public partial class MachineView
{
    private readonly List<(Drain Drain, MeshInstance3D Swirl, Label3D Tag, Vector3 At)> _drainViews = [];

    private void BuildDrains()
    {
        foreach (var (id, drain) in Runtime.Drains)
        {
            var part = Runtime.Def.Part(id)!;
            float side = Mathf.Max(0.05f, (float)drain.Perimeter / 4);
            // on a map, at the ground's surface there; on the floor, where it was put
            float y = Ground is { } g ? (float)g.HeightAt(part.At.X, part.At.Z) : (float)part.At.Y;
            var at = new Vector3((float)part.At.X, y, (float)part.At.Z);
            var pit = Shapes.Box(new Vector3(side, 0.004f, side), Shapes.Mat(new Color(0.03f, 0.03f, 0.04f)));
            pit.Position = at + new Vector3(0, 0.004f, 0);
            AddChild(pit);
            var iron = Surface(part.Material);
            int bars = 5;
            for (int i = 0; i < bars; i++)
            {
                var bar = Shapes.Box(new Vector3(side, 0.012f, side / (2 * bars)), iron);
                bar.Position = at + new Vector3(0, 0.01f, side * ((i + 0.5f) / bars - 0.5f));
                AddChild(bar);
            }
            var swirl = Shapes.Cylinder(1, 0.004f, Shapes.Mat(Shapes.Water, roughness: 0.1f, alpha: 0.7f));
            swirl.Position = at + new Vector3(0, 0.02f, 0);
            swirl.Visible = false;
            AddChild(swirl);
            var tag = new Label3D
            {
                Position = at + new Vector3(0, 0.35f, 0), FontSize = 24, OutlineSize = 6, PixelSize = 0.004f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            };
            AddChild(tag);
            _drainViews.Add((drain, swirl, tag, at));
        }
        DrawDrains();
    }

    private void DrawDrains()
    {
        foreach (var (drain, swirl, tag, _) in _drainViews)
        {
            bool running = drain.Flow > 1e-6;
            swirl.Visible = running;
            // 1 L/s fills a swirl 0.25 m across
            float r = Mathf.Clamp(0.125f * Mathf.Sqrt((float)(drain.Flow * 1000)), 0.03f, 1.5f);
            if (running) swirl.Scale = new Vector3(r, 1, r);
            swirl.RotateY(0.15f);
            tag.Text = $"{drain.Name}\n" + (!drain.Attached ? "no ground to drain"
                       : running ? $"{drain.Flow * 1000:F2} L/s into {drain.Into.Name}\n{drain.Depth * 100:F1} cm over it"
                       : drain.Into.WaterVolume >= drain.Into.Capacity - 1e-9 ? $"{drain.Into.Name} full: backed up" : "dry");
        }
    }
}
