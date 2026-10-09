using Godot;
using HeroicInventions.Sim.Machines;
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
/// <item>A store that is a body (<c>#:movable #t</c>, issue #206) is a rigid cube of its material that the rover's hand or a person's can push:
/// it is tinted with its own temperature as it goes, and its place is handed to the sim every tick, so a rock pushed into a lidded bin becomes
/// that bin's store (and out again). A bin with no store of its own stands empty, walled on three sides and open at the front (+z) so a rock can be pushed in
/// over the ground; its lid and its label show what is in it.</item>
/// <item>A room with a wall heat soaks into (a regolith vault) shows the wall as a cut-away block of earth, and its inner
/// face as a thin sheet washed with the temperature of that surface, so the warmth going into the wall can be seen
/// arriving: a label says how deep it has gone.</item>
/// </list>
/// </summary>
public partial class MachineView
{
    private sealed record StoreView(HeatStore Store, StandardMaterial3D Block, Label3D Label, HeatBin? Bin, Node3D? Lid, MeshInstance3D? Ice, float Height, float BaseY, RigidBody3D? Body = null);
    private sealed record BinView(HeatBin Bin, Node3D Lid, Label3D Label);
    private readonly List<BinView> _binViews = [];
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
            if (store.Movable) { BuildMovableStore(part, store); continue; }
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

        // a bin that takes what is pushed into it (#206)
        foreach (var (id, bin) in Runtime.HeatBins)
            if (bin.Fixed is null) { _building = id; BuildReceivingBin(bin); }

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

    /// <summary>A store that is a body: a cube of its material that Jolt moves (the rover's hand pushes it), with its label riding on it.</summary>
    private void BuildMovableStore(PartSpec part, HeatStore store)
    {
        float s = (float)store.Side;
        var mat = PartSurface(part, s);
        var block = new MaterialBlock(_materials[store.Substance.Name], Vector3.One * s, Shapes.ColorFor(part.Material))
        {
            Name = part.Id, Position = V(part.At) + new Vector3(0, s / 2, 0), Freeze = true,
        };
        foreach (var visual in block.GetChildren().OfType<MeshInstance3D>()) visual.MaterialOverride = mat;
        AddChild(block);
        var label = new Label3D
        {
            FontSize = 24, OutlineSize = 6, PixelSize = Mathf.Clamp(s * 0.012f, 0.0025f, 0.006f), NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, s / 2 + 0.12f, 0),
        };
        block.AddChild(label);
        Blocks.Add(block);
        _freezable.Add(block);
        _bodiesById[part.Id] = block;
        _storeViews.Add(new StoreView(store, mat, label, null, null, null, s, (float)part.At.Y, block));
    }

    /// <summary>
    /// A bin for rock that is pushed in (issue #206): a floor-less box on the ground, walled at the back and both sides and open at the front (+z),
    /// its cavity <see cref="HeatBin.Inner"/> across, the same insulated wood and hinged lid as a bin round a fixed store. The walls are solid (a rock
    /// pushed against one stops); there is no floor to climb, so a rock slides in over the ground. The hinge is on the back wall's top.
    /// </summary>
    private void BuildReceivingBin(HeatBin bin)
    {
        var at = new Vector3((float)bin.X, (float)bin.Y, (float)bin.Z);
        float inner = (float)bin.Inner, wall = 0.025f, outer = inner + 2 * wall;
        var woodMat = Surface(Runtime.Def.Part(bin.Name)?.Material ?? "oak");
        void Slab(Vector3 size, Vector3 pos)
        {
            var body = new StaticBody3D { Position = at + pos };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            body.AddChild(Shapes.Box(size, woodMat));
            AddChild(body);
        }
        Slab(new Vector3(outer, inner, wall), new Vector3(0, inner / 2, -(inner / 2 + wall / 2)));                         // back
        Slab(new Vector3(wall, inner, inner), new Vector3(-(inner / 2 + wall / 2), inner / 2, 0));                          // sides
        Slab(new Vector3(wall, inner, inner), new Vector3(inner / 2 + wall / 2, inner / 2, 0));
        var lid = new Node3D { Position = at + new Vector3(0, inner, -(outer / 2)) };
        AddChild(lid);
        var lidMesh = Shapes.Box(new Vector3(outer, 0.04f, outer), woodMat);
        lidMesh.Position = new Vector3(0, 0.02f, outer / 2);
        lid.AddChild(lidMesh);
        var label = new Label3D
        {
            FontSize = 24, OutlineSize = 6, PixelSize = 0.004f, NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = at + new Vector3(0, inner + 0.3f, 0),
        };
        AddChild(label);
        _binViews.Add(new BinView(bin, lid, label));
    }

    /// <summary>Hands the sim the place of every store that is a body, each tick before it steps: a rock pushed into a bin is in it from this moment.</summary>
    private void FeedHeatStores()
    {
        foreach (var v in _storeViews)
        {
            if (v.Body is not { } body || !IsInstanceValid(body)) continue;
            var p = ToLocal(body.GlobalPosition);
            Runtime.MoveHeatStore(v.Store.Name, p.X, p.Y - v.Height / 2, p.Z);
        }
    }

    private void DrawHeatStores()
    {
        foreach (var bv in _binViews)
        {
            bv.Lid.RotationDegrees = new Vector3(-105f * (float)bv.Bin.Open, 0, 0);
            var held = bv.Bin.Store;
            string lid = bv.Bin.Open >= 0.995 ? "lid open" : bv.Bin.Open > 0.005 ? $"lid {bv.Bin.Open * 100:0}% open" : $"lid shut, leaks {bv.Bin.Leak:0.##} W/K";
            bv.Label.Text = $"{bv.Bin.Name}: {(held is null ? "empty" : $"{held.Name} {held.Temperature:0} °C")}\n{lid}";
        }
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
            if (s.Movable) text += s.Bin is { } inBin ? $"\nin {inBin.Name}" : "\nloose";
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
