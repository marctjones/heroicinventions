using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Glass panes (issue #57): a frame of small squares set in an enclosure's
/// wall or roof, clear for silica and nearly black for basalt; while the sun
/// shines through, a pool of its light lies on the floor below, as bright as
/// the power coming in. A cracked pane is gone from its frame, and the room
/// behind it has lost its air.
/// </summary>
public partial class MachineView
{
    private readonly List<(Pane Pane, List<MeshInstance3D> Glass, MeshInstance3D Pool, StandardMaterial3D PoolMat)> _paneViews = [];

    private void BuildPanes()
    {
        foreach (var (id, pane) in Runtime.Panes)
        {
            var part = Runtime.Def.Part(id)!;
            var at = V(part.At);
            int across = (int)Math.Ceiling(Math.Sqrt(pane.Count));
            float a = (float)pane.Side;
            var frameMat = Shapes.Mat(new Color(0.35f, 0.28f, 0.2f));
            var glassMat = Shapes.Mat(pane.Transmittance > 0.5 ? new Color(0.8f, 0.92f, 0.96f) : new Color(0.08f, 0.07f, 0.07f),
                                      metallic: 0.1f, roughness: 0.05f, alpha: pane.Transmittance > 0.5 ? 0.3f : 0.9f);
            var face = pane.Facing;
            var normal = new Vector3((float)face.X, (float)face.Y, (float)face.Z);
            var basis = Basis.LookingAt(-normal, Mathf.Abs(normal.Y) > 0.9f ? Vector3.Forward : Vector3.Up);
            var glass = new List<MeshInstance3D>();
            for (int i = 0; i < pane.Count; i++)
            {
                float u = (i % across - (across - 1) / 2f) * a, v = (i / across - (across - 1) / 2f) * a;
                var offset = basis * new Vector3(u, v, 0);
                var g = Shapes.Box(new Vector3(a * 0.92f, a * 0.92f, (float)pane.Thickness), glassMat);
                g.Basis = basis;
                g.Position = at + offset;
                AddChild(g);
                glass.Add(g);
                var frame = Shapes.Box(new Vector3(a, a, 0.004f), frameMat);
                frame.Basis = basis;
                frame.Position = at + offset - normal * 0.006f;
                frame.Scale = new Vector3(1, 1, 1);
                frame.Visible = false;   // the frame shows only where the glass has gone
                AddChild(frame);
                glass.Add(frame);
            }
            // the sunlit patch on the floor under a roof
            var poolMat = Shapes.Mat(new Color(1f, 0.93f, 0.7f), roughness: 1, alpha: 0.35f);
            poolMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            float side = across * a;
            var pool = Shapes.Box(new Vector3(side, 0.005f, side), poolMat);
            var room = Runtime.Def.Part(part.Symbol("on", ""));
            pool.Position = new Vector3(at.X, (float)(room?.At.Y ?? 0) + 0.01f, at.Z);
            pool.Visible = false;
            AddChild(pool);
            AddLabel(id, at + normal * 0.2f);
            _paneViews.Add((pane, glass, pool, poolMat));
        }
    }

    private void DrawPanes()
    {
        foreach (var (pane, glass, pool, poolMat) in _paneViews)
        {
            for (int i = 0; i < glass.Count; i += 2)
            {
                glass[i].Visible = !pane.Cracked;
                glass[i + 1].Visible = pane.Cracked;
            }
            double flux = pane.Area > 0 ? pane.Gain / pane.Area : 0;
            pool.Visible = pane.Facing.Y > 0.5 && flux > 1;
            poolMat.AlbedoColor = poolMat.AlbedoColor with { A = (float)Math.Clamp(flux / 1000, 0.05, 0.6) };
        }
    }
}
