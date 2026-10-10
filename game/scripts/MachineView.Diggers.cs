using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// A digging gang (issue #44). The trench itself is the ground's own shape
/// (TerrainView redraws the map as it is dug); here, the gang: stakes and a
/// line marking out the trench, a figure for each labourer working along
/// its floor at the depth reached (swinging while they dig, standing on the
/// bank once they have climbed out), and a tag with the depth, the spoil
/// moved and the work done. A wall that falls in turns the tag red.
/// </summary>
public partial class MachineView
{
    private readonly List<(Digger Digger, List<Node3D> Men, Label3D Tag, float BaseY)> _diggers = [];
    /// <summary>Each gang's stakes and line, which move with its site (#162).</summary>
    private readonly Dictionary<Digger, Node3D> _diggerSites = [];

    private void BuildDigger(PartSpec part)
    {
        var d = Runtime.Diggers[part.Id];
        var wood = Surface("oak");
        float x0 = (float)d.X0, z0 = (float)d.Z0, len = (float)d.Length, w = (float)d.Width, y = (float)part.At.Y;
        var site = new Node3D();
        AddChild(site);
        _diggerSites[d] = site;
        foreach (var (sx, sz) in new[] { (0f, -1f), (0f, 1f), (1f, -1f), (1f, 1f) })
            site.AddChild(Shapes.Rod(new Vector3(x0 + sx * len, y - 0.2f, z0 + sz * w / 2), new Vector3(x0 + sx * len, y + 0.6f, z0 + sz * w / 2), 0.03f, wood));
        var line = Shapes.Mat(new Color(0.9f, 0.85f, 0.7f));
        foreach (float sz in new[] { -1f, 1f })
            site.AddChild(Shapes.Rod(new Vector3(x0, y + 0.5f, z0 + sz * w / 2), new Vector3(x0 + len, y + 0.5f, z0 + sz * w / 2), 0.008f, line));
        int count = Math.Clamp((int)Math.Round(d.Power / 150), 1, 6);   // a labourer keeps up about 150 W
        var men = new List<Node3D>();
        for (int k = 0; k < count; k++)
        {
            var man = new Node3D { Position = new Vector3(x0 + len * (k + 0.5f) / count, y, z0) };
            // a labourer with a spade (Figures, 12.20), side on to the trench so the swing reads
            var person = Figures.Person("#8C6E4E", Figures.Pose.Digging, 0.9f);
            person.Basis = person.Basis * new Basis(Vector3.Up, Mathf.Pi / 2);
            man.AddChild(person);
            AddChild(man);
            men.Add(man);
        }
        var tag = new Label3D { Position = new Vector3(x0 + len / 2, y + 2.2f, z0), FontSize = 28, OutlineSize = 8, PixelSize = 0.004f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
        AddChild(tag);
        _diggers.Add((d, men, tag, y));
    }

    private void DrawDiggers()
    {
        foreach (var (d, men, tag, baseY) in _diggers)
        {
            bool working = !d.Done && d.Cells.Count > 0;
            // sent to a new site by a click on the ground (#162): the stakes, the gang and its tag go with it, to the ground's height there
            float rise = d.SiteHeight is { } ground ? (float)ground - baseY : 0;
            if (_diggerSites.TryGetValue(d, out var site)) site.Position = new Vector3((float)(d.X0 - d.StartX), rise, (float)(d.Z0 - d.StartZ));
            float y = baseY + rise;
            int n = men.Count;
            for (int k = 0; k < men.Count; k++)
            {
                // in the trench at the depth reached while they dig; out on the bank beside it once done
                float floor = y - (float)d.Depth;
                float mx = (float)(d.X0 + d.Length * (k + 0.5f) / n);
                men[k].Position = working
                    ? new Vector3(mx, floor, (float)d.Z0)
                    : new Vector3(mx, y, (float)(d.Z0 - d.Width / 2 - 0.8));
                men[k].Rotation = new Vector3(0, 0, working ? 0.35f * Mathf.Sin((float)(Runtime.Time * 3 + k)) : 0);
            }
            tag.Position = new Vector3((float)(d.X0 + d.Length / 2), y + 2.2f, (float)d.Z0);
            tag.Text = d.Cells.Count == 0 ? $"{d.Name}: no ground to dig"
                : $"{d.Name} · {d.Depth:F2} m deep · {d.Dug:F1} m³ out · {d.Work / 1000:F0} kJ" + (d.Collapsed ? $"\nthe wall fell in at {d.CollapseDepth:F2} m" : d.Done ? " · done" : "");
            tag.Modulate = d.Collapsed ? new Color(1, 0.35f, 0.3f) : Colors.White;
        }
    }
}
