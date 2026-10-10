using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Blocks buried in a world's ground (issue #54). Each tick a block's cover
/// is read from the map: the ground's height over it less the top of the
/// block. Covered by more than a quarter of its height it is held where it
/// lies (frozen), and the force it would take to pull it straight out is
/// worked out (Burial.PullOut: its weight, the soil on its lid, the soil's
/// grip on its sides). Uncovered past that, it is let go as an ordinary
/// body, lifted to stand on the ground where it was dug out. A slide that
/// buries a block that was standing free holds it the same way. A buried
/// block shows as a tag on the ground above it: how deep, and the pull (for the rover's cargo only once found, #240: MachineView.Found.cs).
/// </summary>
public partial class MachineView
{
    /// <summary>The ground of the world this machine stands in, if it has a map (set by Main).</summary>
    public Terrain? Ground { get; set; }

    private sealed class Buried
    {
        public required RigidBody3D Body;
        public required float Size;
        public double Cover, Pull;
        public bool Held;
        public Label3D? Tag;
    }
    private readonly Dictionary<string, Buried> _buried = [];

    /// <summary>Before each physics step: which blocks the ground holds, and which it has let go.</summary>
    private void CheckBurial()
    {
        if (Ground is not { } g) return;
        if (_buried.Count == 0)
            foreach (var part in Runtime.Def.Parts.Where(p => p.Kind == "block"))
                if (_bodiesById.TryGetValue(part.Id, out var body))
                    _buried[part.Id] = new Buried { Body = body, Size = (float)part.Number("size") };
        double gravity = Runtime.Outside.Gravity;
        foreach (var (id, b) in _buried)
        {
            if (!IsInstanceValid(b.Body)) continue;
            var at = PhysicsServer3D.BodyGetDirectState(b.Body.GetRid()).Transform.Origin;
            double surface = g.HeightAt(at.X, at.Z);
            b.Cover = surface - (at.Y + b.Size / 2);
            var cell = g.CellAt(at.X, at.Z);
            var soil = cell is { } c ? g.SoilOf(c) : g.Soils[0];
            bool loose = cell is { } lc && g.Loose[lc];
            b.Pull = b.Cover > 0 ? Burial.PullOut(b.Cover, b.Size, b.Body.Mass, soil, gravity, loose) : b.Body.Mass * gravity;
            bool held = Burial.Held(b.Cover, b.Size);
            if (held)
            {
                b.Body.Freeze = true;   // held by the ground over it (again, if a pause and run unfroze it)
                b.Tag ??= NewBurialTag();
            }
            else if (!held && b.Held)
            {
                // dug out: stood on the ground where it lay, free
                b.Body.Freeze = false;
                b.Body.GlobalPosition = new Vector3(at.X, (float)surface + b.Size / 2 + 0.01f, at.Z);
                b.Body.LinearVelocity = b.Body.AngularVelocity = Vector3.Zero;
            }
            b.Held = held;
            if (b.Tag is { } tag)
            {
                tag.Visible = held;
                tag.Position = new Vector3(at.X, (float)surface + 0.4f, at.Z);
                tag.Text = BurialTagText(id, b);   // no depth or pull until found (#240, MachineView.Found.cs)
            }
        }
    }

    private Label3D NewBurialTag()
    {
        var tag = new Label3D { FontSize = 26, OutlineSize = 8, PixelSize = 0.008f, Modulate = new Color(1, 0.85f, 0.5f), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
        AddChild(tag);
        return tag;
    }

    /// <summary>The burial readings, for the trace: cover (m), buried (0/1), pull-out (N).</summary>
    private IEnumerable<(string Key, double Value)> BurialFields() =>
        _buried.SelectMany(kv => new[]
        {
            ($"{kv.Key}.cover", kv.Value.Cover), ($"{kv.Key}.buried", kv.Value.Held ? 1.0 : 0.0), ($"{kv.Key}.pull-out", kv.Value.Pull),
        });
}
