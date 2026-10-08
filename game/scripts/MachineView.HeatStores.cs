using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Stored heat you can see (issue #71).
/// <list type="bullet">
/// <item>A heat store (a bed of rock, the battery bank, a tank of hot water) is a block the size of its volume, washed
/// with the warmth tint of its own temperature (<see cref="Skins.Warm"/>, art direction 12.9): cold blue-grey in a frost,
/// ochre by 100 °C, dull red by 400. A label gives the number.</item>
/// <item>A lidded bin round a store is an open-fronted insulated box with a lid hinged at the back, which swings up as the
/// thermostat opens it, so a store that is letting its heat out looks open.</item>
/// <item>A room with a wall heat soaks into (a regolith vault) shows the wall as a cut-away block of earth, and its inner
/// face as a thin sheet washed with the temperature of that surface, so the warmth going into the wall can be seen
/// arriving: a label says how deep it has gone.</item>
/// </list>
/// </summary>
public partial class MachineView
{
    private sealed record StoreView(HeatStore Store, StandardMaterial3D Block, Label3D Label, HeatBin? Bin, Node3D? Lid, MeshInstance3D? Ice, float Height, float BaseY);
    private sealed record VaultView(Enclosure Room, List<StandardMaterial3D> Liners, Label3D Label);
    private readonly List<StoreView> _storeViews = [];
    private readonly List<VaultView> _vaultViews = [];

    /// <summary>The length of one side of a cube of this store's volume (a tank's diameter): how big to draw it.</summary>
    private static float StoreSide(HeatStore store) => Mathf.Max(0.06f, Mathf.Pow((float)(store.Mass / store.Substance.Density), 1f / 3f));

    private void BuildHeatStores()
    {
        foreach (var (id, store) in Runtime.HeatStores)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var at = V(part.At);
            float s = StoreSide(store);
            bool water = store.Substance.Name == "water";
            var mat = water ? Shapes.Mat(Shapes.Water, roughness: 0.3f) : PartSurface(part, s);
            MeshInstance3D block;
            MeshInstance3D? ice = null;
            float height = s;
            if (water)
            {
                // a tank of water: V = 2 pi r^3, as tall as it is wide
                float r = s / Mathf.Pow(2 * Mathf.Pi, 1f / 3f) * 0.9f;
                block = Shapes.Cylinder(r, 2 * r, mat);
                block.Position = at + new Vector3(0, r, 0);
                height = 2 * r;
                // what has frozen: a pale cylinder rising from the bottom, as much of the tank as is ice
                ice = Shapes.Cylinder(r * 1.01f, 2 * r, Shapes.Mat(new Color(0.88f, 0.95f, 1f), roughness: 0.4f));
                ice.Position = at + new Vector3(0, r, 0);
                ice.Visible = false;
                AddChild(ice);
            }
            else
            {
                block = Shapes.Box(new Vector3(s, s, s), mat);
                block.Position = at + new Vector3(0, s / 2, 0);
            }
            AddChild(block);
            var label = new Label3D
            {
                FontSize = 24, OutlineSize = 6, PixelSize = Mathf.Clamp(s * 0.012f, 0.0025f, 0.006f), NoDepthTest = true,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = at + new Vector3(0, s + 0.12f, 0),
            };
            AddChild(label);

            // the bin: an insulated box, open at the front (+z) so what is in it shows, and a lid hinged at the back
            Node3D? lid = null;
            if (store.Bin is { } bin)
            {
                float inner = s + 0.03f, wall = 0.025f, outer = inner + 2 * wall;
                var woodMat = Surface(Runtime.Def.Part(bin.Name)?.Material ?? "oak");
                void Slab(Vector3 size, Vector3 pos) { var b = Shapes.Box(size, woodMat); b.Position = at + pos; AddChild(b); }
                Slab(new Vector3(outer, wall, outer), new Vector3(0, wall / 2 - 0.0f, 0));                                   // floor
                Slab(new Vector3(outer, inner, wall), new Vector3(0, wall + inner / 2, -(inner / 2 + wall / 2)));            // back
                Slab(new Vector3(wall, inner, inner), new Vector3(-(inner / 2 + wall / 2), wall + inner / 2, 0));            // sides
                Slab(new Vector3(wall, inner, inner), new Vector3(inner / 2 + wall / 2, wall + inner / 2, 0));
                block.Position += new Vector3(0, wall, 0);                                                                    // the store sits on the floor
                label.Position += new Vector3(0, wall + 0.08f, 0);
                // the lid: pivots on the top of the back wall; thicker than a wall, as it is insulated
                lid = new Node3D { Position = at + new Vector3(0, wall + inner, -(outer / 2)) };
                AddChild(lid);
                var lidMesh = Shapes.Box(new Vector3(outer, 0.04f, outer), woodMat);
                lidMesh.Position = new Vector3(0, 0.02f, outer / 2);
                lid.AddChild(lidMesh);
            }
            _storeViews.Add(new StoreView(store, mat, label, store.Bin, lid, ice, height, at.Y));
        }

        // a room with a wall heat soaks into: the earth round it, cut away at the front (+z) and the top, and a sheet on its inner face
        foreach (var (id, room) in Runtime.Enclosures)
        {
            if (room.Wall is not { } wall) continue;
            _building = id;
            var part = Runtime.Def.Part(id)!;
            float w = (float)part.Number("size-x"), h = (float)part.Number("size-y"), d = (float)part.Number("size-z"), t = (float)wall.Thickness;
            var at = V(part.At);
            string earth = part.Symbol("wall", "regolith");
            void Block(Vector3 size, Vector3 pos, StandardMaterial3D m) { var b = Shapes.Box(size, m); b.Position = at + pos; AddChild(b); }
            var earthMat = Surface(earth);
            Block(new Vector3(w + 2 * t, h, t), new Vector3(0, h / 2, -(d / 2 + t / 2)), earthMat);                       // back
            Block(new Vector3(t, h, d), new Vector3(-(w / 2 + t / 2), h / 2, 0), earthMat);                                // sides
            Block(new Vector3(t, h, d), new Vector3(w / 2 + t / 2, h / 2, 0), earthMat);
            // the inner face: 1 cm sheets on the back, the sides and the floor, washed with the surface's own temperature
            var liners = new List<StandardMaterial3D>();
            void Liner(Vector3 size, Vector3 pos)
            {
                var m = Surface(earth);
                liners.Add(m);
                Block(size, pos, m);
            }
            const float sheet = 0.01f;
            Liner(new Vector3(w, h, sheet), new Vector3(0, h / 2, -(d / 2 - sheet / 2)));
            Liner(new Vector3(sheet, h, d), new Vector3(-(w / 2 - sheet / 2), h / 2, 0));
            Liner(new Vector3(sheet, h, d), new Vector3(w / 2 - sheet / 2, h / 2, 0));
            Liner(new Vector3(w, sheet, d), new Vector3(0, 0.012f + sheet / 2, 0));
            var label = new Label3D
            {
                FontSize = 24, OutlineSize = 6, PixelSize = 0.004f, NoDepthTest = true,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = at + new Vector3(0, h + t * 0.4f + 0.55f, 0),
            };
            AddChild(label);
            _vaultViews.Add(new VaultView(room, liners, label));
        }
    }

    private void DrawHeatStores()
    {
        foreach (var v in _storeViews)
        {
            var s = v.Store;
            Skins.Warm(v.Block, s.Temperature);   // the stored heat, on the store (art direction 12.9)
            Skins.Glow(v.Block, s.Temperature);   // and a glow from 500 °C, as for any hot thing
            if (v.Ice is { } ice)
            {
                ice.Visible = s.Frozen > 0.001;
                float share = Mathf.Max(0.001f, (float)s.Frozen);
                ice.Scale = new Vector3(1, share, 1);
                ice.Position = new Vector3(ice.Position.X, v.BaseY + share * v.Height / 2, ice.Position.Z);
            }
            if (v.Lid is not null && v.Bin is { } bin) v.Lid.RotationDegrees = new Vector3(-105f * (float)bin.Open, 0, 0);
            string text = $"{s.Name}: {s.Temperature:0} °C";
            if (s.Substance.Latent > 0 && s.Frozen > 0) text += $", {s.Frozen * 100:0}% ice";
            if (v.Bin is { } b) text += b.Open >= 0.995 ? "\nlid open" : b.Open > 0.005 ? $"\nlid {b.Open * 100:0}% open" : $"\nlid shut, leaks {b.Leak:0.##} W/K";
            v.Label.Text = text;
        }
        foreach (var v in _vaultViews)
        {
            var wall = v.Room.Wall!;
            foreach (var m in v.Liners) Skins.Warm(m, wall.SurfaceTemperature);
            v.Label.Text = $"{v.Room.Name}: wall {wall.SurfaceTemperature:0} °C, warmth {wall.PenetrationDepth * 100:0.#} cm deep\nsoaked {wall.Absorbed / 1e6:0.##} MJ, now {wall.Flux:0.#} W";
        }
    }
}
