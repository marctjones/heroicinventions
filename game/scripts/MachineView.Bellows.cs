using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// A bellows beside its hearth: two boards hinged at the nozzle end,
/// worked open and shut. How fast they pump, and how hard the puff of
/// wind it leans into the fire, both scale with the hearth's airflow —
/// nothing to see when it is still.
/// </summary>
public partial class MachineView
{
    private readonly List<(Hearth hearth, Node3D topBoard, GpuParticles3D wind)> _bellowsViews = [];

    private void BuildBellows()
    {
        foreach (var (id, hearth) in Runtime.Bellows)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var at = V(part.At);
            var wood = Surface(part.Material);
            var hinge = at + new Vector3(0, 0.03f, 0.14f);

            var bottom = Shapes.Box(new Vector3(0.16f, 0.015f, 0.28f), wood);
            bottom.Position = at + new Vector3(0, 0.015f, 0);
            AddChild(bottom);

            var top = new Node3D { Position = hinge };
            AddChild(top);
            var topBoard = Shapes.Box(new Vector3(0.16f, 0.015f, 0.28f), wood);
            topBoard.Position = new Vector3(0, 0, -0.14f);
            top.AddChild(topBoard);

            var wind = SteamCloud(at + new Vector3(0, 0.02f, -0.16f), amount: 16, radius: 0.01f, lifetime: 0.5f);
            _bellowsViews.Add((hearth, top, wind));
            AddLabel(id, at + new Vector3(0.2f, 0.05f, 0));
        }
    }

    private void DrawBellows()
    {
        foreach (var (hearth, top, wind) in _bellowsViews)
        {
            float airflow = (float)hearth.Airflow;
            bool working = airflow > 1e-9;
            float rate = 0.5f + 4f * airflow;                      // strokes/s: faster the harder it is worked
            float stroke = working ? 0.25f + 0.1f * airflow : 0f;   // rad the boards gape
            float phase = 0.5f * (1 - Mathf.Cos((float)Runtime.Time * rate * Mathf.Tau));
            top.Rotation = new Vector3(stroke * phase, 0, 0);
            wind.Emitting = working;
        }
    }
}
