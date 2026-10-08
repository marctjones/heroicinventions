using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The palette a newcomer can read (#165): build mode opens on eight
/// everyday parts with plain names (a beam, a block, a ball, a ramp, a post,
/// a wheel, a rope, a water tank); "Show all parts" lists the rest, grouped
/// by what you are making. The catalogue's many sizes of one thing (fifteen
/// gears, three pulleys…) are one entry each, the size picked on the part's
/// card. Hovering or picking an entry shows its "What is this?" card: one
/// sentence on what it does, one on what it joins to, and the materials it
/// can be made of as one-click swatches.
///
/// Presentation only: every entry still places a part from
/// <see cref="PartTemplates"/> or the catalogue through the same command.
/// </summary>
public partial class BuildMode
{
    /// <summary>
    /// One line in the palette: a part kind, a cluster of catalogue sizes of
    /// one thing (<see cref="Variants"/>, smallest first), or a join tool.
    /// </summary>
    private sealed record Entry(string Key, string Label, string Group, string Says, string Joins,
                                IReadOnlyList<string> Variants, LinkGestures.Kind? Join = null);

    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, Entry> _entryByKey = [];
    private readonly Dictionary<string, string> _entryOfVariant = [];   // concrete palette id -> its entry's key
    private readonly Dictionary<string, string> _variantChosen = [];   // entry key -> the size picked on its card
    private readonly Dictionary<string, Texture2D> _thumbs = [];        // entry key -> its thumbnail, kept across list rebuilds
    private bool _showAll;
    private string? _placingKey;      // the entry being placed
    private string? _placeMaterial;   // what it will be made of: its own usual material, or the swatch picked
    private CheckButton _showAllToggle = null!;
    private Control _joinBox = null!;

    /// <summary>The parts build mode opens with, in order.</summary>
    public static readonly string[] StarterKeys = ["lever", "block", "ball", "ramp", "post", "cart-wheel", "rope", "tank"];

    /// <summary>
    /// Starter parts in plain words: the name a person would use, what it
    /// does, and what it connects to. These replace <see cref="PartInfo"/>'s
    /// wording on the card.
    /// </summary>
    private static readonly Dictionary<string, (string Label, string Says, string Joins)> StarterInfo = new()
    {
        ["lever"] = ("Beam", "A long plank balanced on a pivot at its middle, like a see-saw. Put weights on it: the side that turns harder goes down.",
                     "Blocks and balls rest on top of it. A rope can tie it to something."),
        ["block"] = ("Block", "A solid cube. What it is made of decides its weight: stone is heavy, wood is light.",
                     "Sits on the ground or on top of another part. A rope can tie it to something."),
        ["ball"] = ("Ball", "A solid ball. It rolls where a block would slide.",
                    "Rests on the ground or on a part. Put it at the top of a ramp to watch it roll."),
        ["ramp"] = ("Ramp", "A fixed slope. Blocks slide down it if it is steep or slippery enough; balls roll.",
                    "Put a block or a ball on it. It stays where you put it."),
        ["post"] = ("Post", "A solid pillar that never moves. Stand things on it or tie a rope to it.",
                    "Parts rest on its top. A rope can tie it to something."),
        ["cart-wheel"] = ("Wheel", "A spoked wheel on an axle. It turns when something pushes or pulls it round.",
                          "Under Show all parts: Axle joins wheels on one shaft, Belt and Gear mesh turn one from another."),
        ["rope"] = ("Rope", "A rope ties two things together: click one, then the other.",
                    "Ties any two parts. Change how long it is in the panel on the right."),
        ["tank"] = ("Water tank", "A tank that holds water.",
                    "Its blue dots are where water goes in and out: click one dot, then a dot on another tank, to run a pipe between them."),
    };

    /// <summary>
    /// What a part is made of unless a swatch says otherwise, extra numbers
    /// it starts with, and how far its #:at sits above the surface it is put
    /// on (a beam's pivot stands up on its legs, clear of the ground).
    /// </summary>
    private static readonly Dictionary<string, (string? Material, (string Key, double Value)[] Props, float Lift)> EntryDefaults = new()
    {
        ["lever"] = ("pine", [("length", 2.0)], 0.4f),
        ["block"] = ("granite", [], 0),
        ["ball"] = ("iron", [], 0),
        ["ramp"] = ("limestone", [], 0),
        ["post"] = ("oak", [], 0),
        ["cart-wheel"] = ("oak", [], 0),
        ["tank"] = (null, [("water", 0.01)], 0),   // two-thirds full: it reads as a water tank
    };

    /// <summary>The swatches shown first, in order; "More…" lists every material.</summary>
    private static readonly string[] SwatchMaterials = ["oak", "pine", "granite", "limestone", "bronze", "iron"];

    /// <summary>Catalogue shapes as one entry each: plain name, group, what it does, what it joins.</summary>
    private static readonly Dictionary<string, (string Label, string Group, string Says, string Joins)> ClusterInfo = new()
    {
        ["gear"] = ("Gear", "Wheels and power", "A toothed wheel: one gear turns another, faster or slower by their sizes.", "Gear mesh joins two gears; Axle fixes it on a shaft with other wheels."),
        ["antikythera-gear"] = ("Tiny bronze gear (Antikythera)", "Wheels and power", "A small bronze gear with triangular teeth, as in the Antikythera mechanism.", "Gear mesh joins two gears; Axle fixes it on a shaft with other wheels."),
        ["pulley"] = ("Pulley", "Weights and levers", "A grooved wheel a rope runs over, to change which way it pulls.", "A rope runs over it; Axle fixes it on a shaft."),
        ["drum"] = ("Winding drum", "Weights and levers", "A drum a rope winds onto, to lift a load as it turns.", "A rope winds on it; Axle or Belt turns it."),
        ["disc-wheel"] = ("Solid wheel or millstone", "Wheels and power", "A solid disc on an axle: a plain wheel, or a millstone.", "Axle fixes it on a shaft; Belt or Gear mesh turns it."),
        ["cart-wheel"] = ("Wheel", "Wheels and power", "A spoked wheel on an axle.", "Axle fixes it on a shaft; Belt or Gear mesh turns it."),
        ["screw"] = ("Water screw (Archimedes)", "Water", "A screw in a tube: turned, it lifts water up its slope.", "Its ends sit in water below and over a tank above."),
        ["treadwheel"] = ("Treadwheel", "Weights and levers", "A big wheel people walk inside to turn it: a crane's engine.", "Axle fixes a drum on its shaft for the rope."),
        ["noria"] = ("Noria (water-lifting wheel)", "Water", "A wheel with pots on its rim that lifts water as the stream turns it.", "Stands in a channel's flow."),
        ["catapult-frame"] = ("Catapult frame", "Weights and levers", "The frame of a bolt-throwing catapult, sized by Vitruvius's rules.", "Holds a sprung arm."),
    };

    private static readonly string[] FullGroupOrder = ["Weights and levers", "Water", "Heat and steam", "Wheels and power", "Rooms, walls and ground", "Joining parts"];

    /// <summary>The full list's groups, by what you are making rather than by engine category.</summary>
    private static string GroupFor(string kind, string partInfoGroup) => kind == "post" ? "Weights and levers" : partInfoGroup switch
    {
        "Fire, water and steam" => "Heat and steam",
        "Structure" => "Rooms, walls and ground",
        _ => partInfoGroup,
    };

    /// <summary>A catalogue entry's cluster: its shape, with Antikythera's triangular-toothed gears kept apart from the involute ones.</summary>
    private static string ClusterOf(CatalogueEntry e) =>
        e.ShapeKind == "gear" && e.ShapeProps.GetValueOrDefault("profile") is SSymbol { Name: "triangular" } ? "antikythera-gear" : e.ShapeKind;

    private void BuildEntries(IReadOnlyList<CatalogueEntry> catalogue)
    {
        foreach (string kind in PartTemplates.PrimitiveKinds)
        {
            var info = PartInfo.GetValueOrDefault(kind);
            string label = StarterInfo.TryGetValue(kind, out var s) ? s.Label : info.Label ?? kind;
            string says = StarterInfo.TryGetValue(kind, out s) ? s.Says : info.Description ?? "";
            string joins = StarterInfo.TryGetValue(kind, out s) ? s.Joins : JoinsFromPorts(kind);
            AddEntry(new Entry(kind, label, GroupFor(kind, info.Group ?? "Rooms, walls and ground"), says, joins, [kind]));
        }
        foreach (var cluster in catalogue.GroupBy(ClusterOf))
        {
            var info = ClusterInfo.TryGetValue(cluster.Key, out var c) ? c
                : (Pretty(cluster.Key), "Wheels and power", cluster.First().Description, "");
            var variants = cluster.OrderBy(e => e.Volume).Select(e => e.Id).ToList();
            AddEntry(new Entry(cluster.Key, info.Item1, info.Item2, info.Item3, info.Item4, variants));
        }
        var rope = StarterInfo["rope"];
        AddEntry(new Entry("rope", rope.Label, "Joining parts", rope.Says, rope.Joins, [], LinkGestures.Kind.Rope));
        // a starter wheel is the middle size, a cart wheel a person would know
        if (_entryByKey.TryGetValue("cart-wheel", out var wheel) && wheel.Variants.Count > 1)
            _variantChosen["cart-wheel"] = wheel.Variants.FirstOrDefault(v => v.Contains("60")) ?? wheel.Variants[0];
    }

    private void AddEntry(Entry e)
    {
        _entries.Add(e);
        _entryByKey[e.Key] = e;
        foreach (var v in e.Variants) _entryOfVariant[v] = e.Key;
    }

    private static string Pretty(string id) => char.ToUpperInvariant(id[0]) + id[1..].Replace('-', ' ');

    /// <summary>What a part with connection points joins to, from the kinds of its points.</summary>
    private static string JoinsFromPorts(string kind)
    {
        var ports = PartTemplates.Create(kind, "x", new Vec3(0, 0, 0), "oak").Ports.Select(p => p.Kind).Distinct().ToList();
        if (ports.Contains("water") && ports.Contains("steam")) return "Its blue dots carry water and its white dots steam: click a dot, then a dot on another part, to join them.";
        if (ports.Contains("water")) return "Its blue dots carry water: click one, then a dot on another part, to run a pipe.";
        if (ports.Contains("steam")) return "Its white dot carries steam: click it, then a dot on another part, to join them.";
        return "";
    }

    /// <summary>The concrete palette id an entry places now: its only kind, or the size picked on its card.</summary>
    private string VariantOf(string key) =>
        _variantChosen.TryGetValue(key, out var v) ? v
        : _entryByKey.TryGetValue(key, out var e) && e.Variants.Count > 0 ? e.Variants[0]
        : key;

    /// <summary>The entry a palette id belongs to (an entry key, or one of a cluster's sizes).</summary>
    private string EntryKeyOf(string id) => _entryByKey.ContainsKey(id) ? id : _entryOfVariant.GetValueOrDefault(id, id);

    /// <summary>The material a part from this entry is made of unless a swatch says otherwise.</summary>
    private string UsualMaterial(string key) => EntryDefaults.TryGetValue(EntryKeyOf(key), out var d) && d.Material is { } m ? m : _material;

    private (string Key, double Value)[] ExtraProps(string key) => EntryDefaults.TryGetValue(EntryKeyOf(key), out var d) ? d.Props : [];
    private float Lift(string key) => EntryDefaults.TryGetValue(EntryKeyOf(key), out var d) ? d.Lift : 0;

    /// <summary>A catalogue size in plain words: "60 cm across", "24 teeth, 12 cm across", with small/medium/large when there are two or three.</summary>
    private string VariantLabel(Entry entry, string id)
    {
        var item = _palette.First(p => p.Id == id);
        if (item.Catalogue is not { } c) return item.Label;
        string text;
        if (c.ShapeProps.GetValueOrDefault("teeth") is SNumber teeth && c.ShapeProps.GetValueOrDefault("pitch-radius") is SNumber pitch)
            text = $"{teeth.Value:0} teeth, {pitch.Value * 200:0.#} cm across";
        else
        {
            var prefixes = entry.Variants.Select(v => _palette.First(p => p.Id == v).Label.Split(", ")[0]).Distinct().Count();
            string[] parts = c.Description.Split(", ");
            text = prefixes == 1 && parts.Length > 1 && !parts[1].StartsWith("by ") ? parts[1]
                 : System.Text.RegularExpressions.Regex.Match(c.Description, @"\(([^)]*)\)") is { Success: true } m && entry.Key == "catapult-frame" ? $"for a {m.Groups[1].Value} bolt"
                 : c.Description;
        }
        int i = entry.Variants.ToList().IndexOf(id);
        string[] size = entry.Variants.Count switch { 2 => ["small", "large"], 3 => ["small", "medium", "large"], _ => [] };
        return size.Length > 0 && i >= 0 ? $"{size[i]}: {text}" : text;
    }

    // ------------------------------------------------------------ the list

    /// <summary>The palette's own column: the "Show all parts" switch, the list, and the "What is this?" card.</summary>
    private void BuildPaletteUi(VBoxContainer leftCol)
    {
        var head = new HBoxContainer();
        head.AddChild(new Label { Text = "Parts: pick one, then click in the scene", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) });
        _showAllToggle = new CheckButton { Text = "Show all", TooltipText = "Show every part, grouped by what you are making", FocusMode = Control.FocusModeEnum.None };
        _showAllToggle.Toggled += on => SetShowAll(on);
        head.AddChild(_showAllToggle);
        leftCol.AddChild(head);

        _paletteList = new ItemList { CustomMinimumSize = new Vector2(230, 200), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _paletteList.ItemSelected += index => StartEntry(_paletteList.GetItemMetadata((int)index).AsString());
        _paletteList.GuiInput += e =>
        {
            if (e is InputEventMouseMotion m)
            {
                int i = _paletteList.GetItemAtPosition(m.Position, exact: true);
                ShowCard(i >= 0 && _paletteList.IsItemSelectable(i) ? _paletteList.GetItemMetadata(i).AsString() : _placingKey);
            }
        };
        _paletteList.MouseExited += () => ShowCard(_placingKey);
        leftCol.AddChild(_paletteList);
        BuildCard(leftCol);
        FillPalette();
    }

    private void SetShowAll(bool on)
    {
        _showAll = on;
        if (_showAllToggle.ButtonPressed != on) _showAllToggle.SetPressedNoSignal(on);
        _joinBox.Visible = on;
        FillPalette();
    }

    /// <summary>(Re)fills the list: the starter set as a grid of pictures, or everything in groups.</summary>
    private void FillPalette()
    {
        _paletteList.Clear();
        _paletteList.FixedIconSize = new Vector2I(PaletteThumbnails.Size, PaletteThumbnails.Size);
        if (!_showAll)
        {
            _paletteList.MaxColumns = 2;
            _paletteList.IconMode = ItemList.IconModeEnum.Top;
            _paletteList.FixedColumnWidth = 104;
            _paletteList.SameColumnWidth = true;
            foreach (var key in StarterKeys)
                if (_entryByKey.TryGetValue(key, out var e)) AddListItem(e, e.Label);
        }
        else
        {
            _paletteList.MaxColumns = 1;
            _paletteList.IconMode = ItemList.IconModeEnum.Left;
            _paletteList.FixedColumnWidth = 0;
            foreach (var group in _entries.GroupBy(e => e.Group).OrderBy(g => Array.IndexOf(FullGroupOrder, g.Key) is var i and >= 0 ? i : 99))
            {
                int header = _paletteList.AddItem(group.Key);
                _paletteList.SetItemSelectable(header, false);
                _paletteList.SetItemCustomFgColor(header, new Color(1f, 0.8f, 0.45f));
                foreach (var e in group.OrderBy(e => Array.IndexOf(StarterKeys, e.Key) is var i and >= 0 ? i : 99).ThenBy(e => e.Label))
                    AddListItem(e, "   " + e.Label + (e.Variants.Count > 1 ? $" ({e.Variants.Count} sizes)" : ""));
            }
        }
        if (_placingKey is { } placing) SelectListItem(placing);
        // thumbnails render one a frame in the background, offscreen (none when headless: nothing would draw them)
        if (DisplayServer.GetName() == "headless") return;
        var queue = Enumerable.Range(0, _paletteList.ItemCount)
            .Where(i => _paletteList.IsItemSelectable(i))
            .Select(i => (i, _paletteList.GetItemMetadata(i).AsString()))
            .Where(q => !_thumbs.ContainsKey(q.Item2) && q.Item2 != "rope").ToList();
        if (queue.Count > 0)
            AddChild(new PaletteThumbnails(this, queue, (i, texture) =>
            {
                if (i < _paletteList.ItemCount) _thumbs[_paletteList.GetItemMetadata(i).AsString()] = texture;
                if (i < _paletteList.ItemCount) _paletteList.SetItemIcon(i, texture);
            }));
    }

    private void AddListItem(Entry e, string text)
    {
        int i = _paletteList.AddItem(text);
        _paletteList.SetItemMetadata(i, e.Key);
        _paletteList.SetItemTooltip(i, e.Says);
        if (_thumbs.TryGetValue(e.Key, out var t)) _paletteList.SetItemIcon(i, t);
        else if (e.Key == "rope" && RopeIcon() is { } rope) _paletteList.SetItemIcon(i, rope);
    }

    /// <summary>The coiled-rope picture (drawn by the skins session), when it is there.</summary>
    private static Texture2D? RopeIcon()
    {
        const string path = "res://icons/rope.png";
        if (!Godot.FileAccess.FileExists(path)) return null;
        var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
        return image is null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
    }

    private int ListIndexOf(string key) =>
        Enumerable.Range(0, _paletteList.ItemCount).FirstOrDefault(i => _paletteList.GetItemMetadata(i).AsString() == key, -1);

    private void SelectListItem(string key)
    {
        int i = ListIndexOf(key);
        if (i >= 0) { _paletteList.Select(i); _paletteList.EnsureCurrentIsVisible(); }
    }

    /// <summary>A palette entry picked: a part starts placing (in its usual material), the rope starts its join tool.</summary>
    private void StartEntry(string key)
    {
        key = EntryKeyOf(key);
        if (!_entryByKey.TryGetValue(key, out var entry)) { GD.Print($"[BuildMode] no palette entry {key}"); return; }
        if (entry.Join is { } join)
        {
            StartLink(join);
            _placingKey = key;
            ShowCard(key);
            _placingKey = null;
            return;
        }
        _placeMaterial = UsualMaterial(key);
        _placingKey = key;
        StartPlacing(VariantOf(key));
        ShowCard(key);
    }

    // ------------------------------------------------------------ the card

    private PanelContainer _card = null!;
    private Label _cardTitle = null!, _cardSays = null!, _cardJoins = null!;
    private TextureRect _cardPicture = null!;
    private OptionButton _cardSize = null!;
    private Label _cardMadeOf = null!;
    private HFlowContainer _cardSwatches = null!;
    private string? _cardKey;

    private void BuildCard(VBoxContainer leftCol)
    {
        _card = new PanelContainer { Visible = false };
        var col = new VBoxContainer();
        _card.AddChild(col);
        var top = new HBoxContainer();
        _cardPicture = new TextureRect { CustomMinimumSize = new Vector2(48, 48), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        top.AddChild(_cardPicture);
        _cardTitle = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        _cardTitle.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.5f));
        top.AddChild(_cardTitle);
        col.AddChild(top);
        _cardSays = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(230, 0) };
        _cardSays.AddThemeFontSizeOverride("font_size", 13);
        col.AddChild(_cardSays);
        _cardJoins = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(230, 0), Modulate = new Color(0.75f, 0.9f, 1f) };
        _cardJoins.AddThemeFontSizeOverride("font_size", 12);
        col.AddChild(_cardJoins);
        _cardSize = new OptionButton { Visible = false, TooltipText = "Which size to place" };
        _cardSize.ItemSelected += index =>
        {
            if (_cardKey is not { } key || !_entryByKey.TryGetValue(key, out var e)) return;
            _variantChosen[key] = e.Variants[(int)index];
            if (_placingKey == key) { StartPlacing(VariantOf(key)); ShowCard(key); }
        };
        col.AddChild(_cardSize);
        _cardMadeOf = new Label { Text = "Made of:" };
        _cardMadeOf.AddThemeFontSizeOverride("font_size", 12);
        col.AddChild(_cardMadeOf);
        _cardSwatches = new HFlowContainer();
        col.AddChild(_cardSwatches);
        leftCol.AddChild(_card);
    }

    /// <summary>The "What is this?" card for a palette entry: what it does, what it joins, and (for the part being placed) its size and material.</summary>
    private void ShowCard(string? key)
    {
        _cardKey = key;
        if (key is null || !_entryByKey.TryGetValue(key, out var e)) { _card.Visible = false; return; }
        _card.Visible = true;
        _cardTitle.Text = $"What is this? {e.Label}";
        _cardSays.Text = e.Says;
        _cardJoins.Text = e.Joins;
        _cardJoins.Visible = e.Joins != "";
        _cardPicture.Texture = _thumbs.GetValueOrDefault(key) ?? (key == "rope" ? RopeIcon() : null);
        _cardPicture.Visible = _cardPicture.Texture is not null;
        bool placing = key == _placingKey && e.Join is null;
        _cardSize.Visible = placing && e.Variants.Count > 1;
        if (_cardSize.Visible)
        {
            _cardSize.Clear();
            foreach (var v in e.Variants) _cardSize.AddItem(VariantLabel(e, v));
            _cardSize.Select(Math.Max(0, e.Variants.ToList().IndexOf(VariantOf(key))));
        }
        _cardMadeOf.Visible = _cardSwatches.Visible = placing;
        if (placing)
            FillSwatches(_cardSwatches, _placeMaterial ?? UsualMaterial(key), UsualMaterial(key), m =>
            {
                _placeMaterial = m;
                if (_placingPaletteId is { } id) StartPlacing(id);
                ShowCard(key);
            });
    }

    /// <summary>
    /// A row of material swatches (a colour chip and a plain name), the
    /// current one pressed, the part's usual one marked; "More…" lists every
    /// material. Picking one calls <paramref name="pick"/>.
    /// </summary>
    private void FillSwatches(HFlowContainer row, string current, string? usual, Action<string> pick)
    {
        foreach (var c in row.GetChildren()) { row.RemoveChild(c); c.QueueFree(); }
        var shown = SwatchMaterials.Where(m => _materials.TryGet(m, out _)).ToList();
        if (!shown.Contains(current)) shown.Add(current);
        foreach (var id in shown)
        {
            var image = Image.CreateEmpty(14, 14, false, Image.Format.Rgba8);
            image.Fill(Shapes.ColorFor(id));
            string name = _materials[id].Name;
            var b = new Button
            {
                Text = id == usual ? $"{name} (usual)" : name,
                Icon = ImageTexture.CreateFromImage(image),
                ToggleMode = true,
                ButtonPressed = id == current,
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = $"{name}: {_materials[id].Density:0} kg a cubic metre",
            };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.SetMeta("material", id);
            if (id == current) b.Modulate = new Color(1.15f, 1.15f, 0.8f);
            b.Pressed += () => pick(id);
            row.AddChild(b);
        }
        var more = new OptionButton { FocusMode = Control.FocusModeEnum.None, TooltipText = "Every material" };
        more.AddThemeFontSizeOverride("font_size", 12);
        more.AddItem("More…");
        var all = _materials.All.OrderBy(m => m.Name).ToList();
        foreach (var m in all) more.AddItem(m.Name);
        more.ItemSelected += index => { if (index > 0) pick(all[(int)index - 1].Id); };
        row.AddChild(more);
    }
}
