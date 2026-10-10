using Godot;
using HeroicInventions.Sim.Game;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Marks the found bank's crate in build mode when the ground lies over it (issue #213). The crate is a body of another machine,
/// not a part on the bench, so build mode neither draws it through the soil nor lets a click find it; a player who means to build
/// a vault round it where it lies (docs/lonely-rover.html) needs to see where. Drawn as a translucent box at the crate's own place
/// with a thin shaft up to the surface, in front of everything, and only while build mode is open (it is a child of build mode, so
/// it goes when build mode does). Nothing here is selectable or moves a thing: it is a view.
/// Owner decision on #240 ("Show the rough area."): the rover's cargo is drawn exactly only once the rover has FOUND it (its top shows,
/// it was freed, the teeth struck it or the rover touched it: Sim/Game/CargoFind.cs, MachineView.Found.cs). Before, the marker is the
/// crate's rough area, the same 6 m disc round the same centre as the cargo markers (RoughArea), with no box, no shaft and no depth.
/// </summary>
public partial class BuriedMarker : Node3D
{
    public Func<double, double, double>? Ground { get; set; }
    /// <summary>The boxes of the parts on the bench now that lie under the ground: each is outlined, or a vault dug round the crate (and the
    /// rock and bin put in it) could not be seen at all under the opaque soil, nor found to be picked (#223).</summary>
    public Func<IEnumerable<Aabb>>? Buried { get; set; }
    private readonly List<MeshInstance3D> _rooms = [];
    private readonly List<(MachineView View, string Store, string Block, MeshInstance3D Box, MeshInstance3D Shaft, Label3D Tag, MeshInstance3D Disc)> _marks = [];
    private readonly Dictionary<MachineView, (double X, double Z)> _discAt = [];
    private readonly Dictionary<MachineView, string> _said = [];
    private double _rescan = 0;

    private static StandardMaterial3D Glow(Color c) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = c, NoDepthTest = true, RenderPriority = 10,
    };

    public override void _Process(double delta)
    {
        if ((_rescan -= delta) <= 0) { _rescan = 1; Rescan(); }
        DrawRooms();
        foreach (var (view, store, block, box, shaft, tag, disc) in _marks)
        {
            bool alive = IsInstanceValid(view);
            if (!alive || Ground is null) { box.Visible = shaft.Visible = tag.Visible = disc.Visible = false; continue; }
            var p = view.StorePoint(store);   // the crate's centre, where its body is now
            float size = (float)view.Runtime.Def.Part(block)!.Number("size");
            float surface = (float)Ground(p.X, p.Z);
            float cover = surface - ((float)p.Y + size / 2);
            bool buried = cover > 0.02f;
            bool found = view.BlockFound(block);
            box.Visible = shaft.Visible = buried && found;
            tag.Visible = buried;
            disc.Visible = buried && !found;
            if (!buried) continue;
            if (found)
            {
                box.Position = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
                ((BoxMesh)box.Mesh).Size = new Vector3(size, size, size) * 1.04f;
                float top = (float)p.Y + size / 2;
                ((BoxMesh)shaft.Mesh).Size = new Vector3(0.04f, surface + 1.5f - top, 0.04f);
                shaft.Position = new Vector3((float)p.X, top + (surface + 1.5f - top) / 2, (float)p.Z);
                tag.Position = new Vector3((float)p.X, surface + 2.4f, (float)p.Z);
                tag.Text = $"found bank lies here, {cover:F2} m down: build the vault round it";
            }
            else
            {
                // the rough area: the cargo markers' disc round the same centre (the crate's place plus its label's fixed offset)
                string label = view.Name.ToString();
                var area = RoughArea.For(label, p.X, p.Z);
                if (!_discAt.TryGetValue(view, out var drawn) || Math.Abs(drawn.X - area.X) > 0.15 || Math.Abs(drawn.Z - area.Z) > 0.15)
                {
                    disc.Mesh = CargoMarkers.DiscMesh(Ground, area.X, area.Z, area.Radius, false);
                    _discAt[view] = (area.X, area.Z);
                }
                tag.Position = new Vector3((float)area.X, (float)Ground(area.X, area.Z) + 3.4f, (float)area.Z);
                tag.Text = $"the {RoughArea.Name(label)} lies somewhere in here, under the rubble: dig to find it";
            }
            if (_said.GetValueOrDefault(view) != tag.Text) { _said[view] = tag.Text; GD.Print($"[BuildMode] buried marker {view.Name}: {tag.Text}"); }
        }
    }

    private void DrawRooms()
    {
        List<Aabb> rooms = Buried is { } list ? list().ToList() : [];
        while (_rooms.Count < rooms.Count)
        {
            var m = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = Glow(new Color(1f, 0.6f, 0.2f, 0.28f)), TopLevel = true };
            AddChild(m);
            _rooms.Add(m);
        }
        for (int i = 0; i < _rooms.Count; i++)
        {
            _rooms[i].Visible = i < rooms.Count;
            if (i >= rooms.Count) continue;
            ((BoxMesh)_rooms[i].Mesh).Size = rooms[i].Size + Vector3.One * 0.12f;
            _rooms[i].Position = rooms[i].GetCenter();
        }
    }

    private void Rescan()
    {
        var known = _marks.Select(m => m.View).ToHashSet();
        foreach (var view in GetTree().Root.FindChildren("*", "Node3D", true, false).OfType<MachineView>())
        {
            if (known.Contains(view)) continue;
            var def = view.Runtime.Def;
            foreach (var bank in def.Parts.Where(p => p.Kind == "battery-bank"))
            {
                if (bank.Symbol("in", "") is not { Length: > 0 } store || WorldZones.CarrierOf(def, store) is not { } on || def.Part(on) is not { Kind: "block" }) continue;
                var box = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = Glow(new Color(0.2f, 0.9f, 1f, 0.35f)) };
                var shaft = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = Glow(new Color(0.2f, 0.9f, 1f, 0.7f)) };
                var tag = new Label3D
                {
                    FontSize = 26, OutlineSize = 8, PixelSize = 0.012f, Modulate = new Color(0.4f, 0.95f, 1f),
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, TopLevel = true,
                };
                var disc = new MeshInstance3D { TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
                AddChild(box); AddChild(shaft); AddChild(tag); AddChild(disc);
                box.TopLevel = shaft.TopLevel = true;
                _marks.Add((view, store, on, box, shaft, tag, disc));
                break;
            }
        }
    }
}
