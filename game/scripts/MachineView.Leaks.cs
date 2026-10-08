using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// A hole in a tank's wall: a dark round hole in the +x side, and out of it
/// the jet, drawn as the parabola it really is. It leaves level at
/// v = √(2·g·h) and falls y = g·t²/2, so it carries x = v·√(2y/g) before it
/// lands, ground or catch tank: the higher the water above the hole, the
/// farther it throws, and the arc shortens as the level sinks. Its width
/// follows continuity, r = √(Q/(π·v)). A seep with no hole is a pale wisp
/// over the surface, while any water is left to lose.
/// </summary>
public partial class MachineView
{
    private const int JetSegments = 18;
    private readonly List<(TankLeak leak, Vector3 hole, MeshInstance3D[] jet)> _leakViews = [];
    private readonly List<(TankLeak leak, MeshInstance3D wisp)> _seepViews = [];

    private void BuildLeaks()
    {
        foreach (var (id, leak) in Runtime.Leaks)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var at = V(part.At);
            AddLabel(id, at + new Vector3(0.12f, 0.08f, 0));
            if (leak.Area > 0 || leak.Bore > 0)
            {
                float r = Mathf.Max(0.008f, Mathf.Sqrt((float)(leak.Bore > 0 ? Math.PI * leak.Bore * leak.Bore / 4 : leak.Area) / Mathf.Pi));
                var hole = Shapes.Cylinder(r, 0.012f, Shapes.Mat(new Color(0.05f, 0.04f, 0.03f)));
                hole.RotationDegrees = new Vector3(0, 0, 90);
                hole.Position = at;
                AddChild(hole);

                var water = Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.75f);
                var jet = new MeshInstance3D[JetSegments];
                for (int i = 0; i < JetSegments; i++) { jet[i] = Shapes.Cylinder(1, 1, water); jet[i].Visible = false; AddChild(jet[i]); }
                _leakViews.Add((leak, at, jet));
            }
            if (leak.Evaporation > 0)
            {
                var wisp = Shapes.Sphere(1, Shapes.Mat(new Color(0.9f, 0.95f, 1f), roughness: 1, alpha: 0.35f));
                wisp.Visible = false;
                AddChild(wisp);
                _seepViews.Add((leak, wisp));
            }
        }
    }

    private void DrawLeaks()
    {
        foreach (var (leak, hole, jet) in _leakViews)
        {
            double head = leak.Head;
            // into its catch tank, or onto the ground: a map's, under the hole, where the sim pours it (#90)
            double catchY = leak.Catch is { } c ? c.SurfaceElevation : Ground?.HeightAt(hole.X, hole.Z) ?? 0;
            double drop = hole.Y - catchY;
            bool flowing = leak.Flow > 1e-9 && drop > 1e-3;
            double g = Runtime.Outside.Gravity;
            double v = Math.Sqrt(2 * g * head), fall = flowing ? Math.Sqrt(2 * drop / g) : 0;
            float r = flowing ? Mathf.Clamp(Mathf.Sqrt((float)(leak.Flow / (Math.PI * v))), 0.002f, 0.03f) : 0;
            for (int i = 0; i < JetSegments; i++)
            {
                jet[i].Visible = flowing;
                if (!flowing) continue;
                Vector3 P(int k) { double t = fall * k / JetSegments; return hole + new Vector3((float)(v * t), (float)(-4.905 * t * t), 0); }
                Vector3 a = P(i), b = P(i + 1), d = b - a;
                float len = d.Length();
                jet[i].Position = (a + b) / 2;
                jet[i].Quaternion = new Quaternion(Vector3.Up, d / len);
                jet[i].Scale = new Vector3(r, len * 1.05f, r);
            }
        }
        foreach (var (leak, wisp) in _seepViews)
        {
            wisp.Visible = leak.Tank.WaterVolume > 0;
            float side = Mathf.Sqrt((float)leak.Tank.Area);
            wisp.Scale = new Vector3(side * 0.45f, 0.03f, side * 0.45f);
            wisp.Position = new Vector3((float)Runtime.Def.Part(leak.Tank.Name)!.At.X, (float)leak.Tank.SurfaceElevation + 0.06f,
                                        (float)Runtime.Def.Part(leak.Tank.Name)!.At.Z);
        }
    }
}
