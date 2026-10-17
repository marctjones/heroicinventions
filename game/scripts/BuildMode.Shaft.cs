using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// "Put a gear on this shaft" (building plan, phase 2). The inspector of a selected windmill or wheel has a button that starts placing a
/// gear (the size from the gear's card, as any placing), or whichever wheel the "Which:" list under it picks (tutorial plan, gap 7: a
/// drum, a pulley, a wheel, a treadwheel…; the button then says so: "Put a wheel on this shaft"), its ghost held on the part's axle
/// line (<see cref="ShaftSlots"/>: a windmill's windshaft behind the hub, a wheel's own axis) and slid along it by the mouse, starting
/// where the next gear goes by default (1.3 m behind a bare windmill's hub, where the route's first wheel is; else 0.1 m behind the
/// hindmost wheel on the axle, or just clear of it when it is wide: <see cref="ShaftSlots.ClearT"/>). A click places the gear with an
/// ordinary (wheel ...) and joins it to the selected part by an ordinary (arbor selected new) (for a wheel already on an arbor, (arbor
/// lead new), the arbor's lead: <see cref="ShaftSlots.PartnerOf"/>): the same commands the console and the Axle tool make, so nothing in the physics changes. The constraint is a separate mode MoveGhost asks first; the free ghost is as
/// before. Alt held frees the ghost (as it does a windmill over a post): the gear goes where the free ghost is, still joined on the axle.
/// The axle line is drawn while the mode is on, and while the mouse is over the button.
/// </summary>
public partial class BuildMode
{
    private ShaftSlots.Shaft? _shaft;   // the axle the gear being placed goes on, while it is
    private float _shaftT;              // where its ghost is along it (metres behind the selected part's #:at)
    private bool _ghostOnShaft;         // whether the ghost is held on the line now (not Alt-freed)
    private MeshInstance3D? _axleLine;
    private string _shaftKey = "gear";  // the parts list entry the button puts on a shaft: the "Which:" list's pick (gap 7)
    private float _shaftDefaultT;       // where it goes by default, clear of any wide wheel already on the axle

    /// <summary>
    /// What the "Which:" list offers, the parts list's own entries: every catalogue wheel, the usual ones first, and a generator (tutorial
    /// plan, slice C: its motor turned by the gear it is put behind, #:on that gear; a click on the gears themselves lands on the sails' box).
    /// </summary>
    private List<string> ShaftKinds()
    {
        string[] first = ["gear", "drum", "pulley", "cart-wheel", "disc-wheel", "treadwheel", "generator"];
        return [.. _entries.Where(e => e.Variants.Count > 0 && e.Variants.All(v => _palette.FirstOrDefault(p => p.Id == v).Catalogue?.PartKind == "wheel") || e.Key == "generator")
                           .OrderBy(e => Array.IndexOf(first, e.Key) is var i and >= 0 ? i : first.Length).ThenBy(e => e.Label).Select(e => e.Key)];
    }

    private string ShaftButtonText() => $"Put a {ShaftSlots.Noun(_shaftKey)} on this shaft";

    /// <summary>The "Which:" list's pick, made by a person, a lesson's step or a script: the inspector's button and list say so at once.</summary>
    private void ChooseShaftKind(string key)
    {
        if (!_entryByKey.ContainsKey(key) || key == _shaftKey) return;
        _shaftKey = key;
        var kinds = ShaftKinds();
        foreach (var c in Descendants(_inspector))
        {
            if (c is Button b && b.HasMeta("gear_on")) b.Text = ShaftButtonText();
            if (c is OptionButton o && o.HasMeta("gear_on_kind")) o.Select(Math.Max(0, kinds.IndexOf(key)));
        }
    }

    private static Vector3 V(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>The inspector's row for a windmill or a wheel: the button that starts putting a gear on its shaft.</summary>
    private void AddGearOnButton(PartSpec part)
    {
        if (!ShaftSlots.Takes(part)) return;
        var b = new Button
        {
            Text = ShaftButtonText(),
            TooltipText = part.Kind == "windmill"
                ? "A gear (or the wheel picked below) on the windshaft behind the sails (the first 1.3 m back), turned by the sails. Pick its size on the card; slide it along with the mouse"
                : "A gear (or the wheel picked below) on this wheel's axle, just behind the last one on it, turning with it. Pick its size on the card; slide it along with the mouse",
        };
        b.SetMeta("gear_on", part.Id);
        b.Pressed += () => StartGearOn(part.Id);
        b.MouseEntered += () => { if (ShaftSlots.Of(_session.Document, part.Id) is { } s) DrawAxle(s, (float)ShaftSlots.ClearT(_session.Document, s, NewWheelHalf())); };
        b.MouseExited += () => { if (_shaft is null) HideAxle(); };
        _inspector.AddChild(b);
        var kinds = ShaftKinds();
        if (kinds.Count < 2) return;
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Which:" });
        var list = new OptionButton
        {
            TooltipText = "What the button puts on the shaft: a gear, a drum to wind a rope on, a pulley, a wheel to turn it by…",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true,
        };
        foreach (var k in kinds) list.AddItem(_entryByKey[k].Label);
        list.Select(Math.Max(0, kinds.IndexOf(_shaftKey)));
        list.SetMeta("gear_on_kind", part.Id);
        list.ItemSelected += i => ChooseShaftKind(kinds[(int)i]);
        row.AddChild(list);
        _inspector.AddChild(row);
    }

    /// <summary>How far the wheel the button would put on reaches along its axle either side of its middle: the card's size of the picked entry.</summary>
    private double NewWheelHalf() =>
        _shaftKey == "generator" ? ShaftSlots.GeneratorHalfLength
        : _palette.FirstOrDefault(p => p.Id == VariantOf(_shaftKey)).Catalogue is { } entry
            ? ShaftSlots.HalfLength(PartTemplates.Create(entry, "probe", new Vec3(0, 0, 0), "oak")) : 0;

    /// <summary>The button pressed: placing a gear starts (the card's size), its ghost on the axle at the default spot.</summary>
    private void StartGearOn(string id)
    {
        if (ShaftSlots.Of(_session.Document, id) is not { } shaft) return;
        string key = _entryByKey.ContainsKey(_shaftKey) ? _shaftKey : "gear";
        StartEntry(key);
        if (_placingPaletteId is null) return;
        _meshRing = null;   // not a gear meshing with a wheel, whatever was being placed before (phase 3)
        HideRing();
        _shaft = shaft;
        _shaftT = _shaftDefaultT = (float)ShaftSlots.ClearT(_session.Document, shaft, NewWheelHalf());
        AimGhostOnShaft();
        _ghost!.Position = V(shaft.At(_shaftT));
        _ghost.Visible = true;
        _ghostOnShaft = true;
        DrawAxle(shaft, _shaftT);
        ShowCard(key);
        _status.Text = ShaftStatus();
        GD.Print($"[BuildMode] {ShaftSlots.Noun(key)} on {id}'s shaft: placing {_placingPaletteId}, the ghost {F(_shaftT)} m behind {id} along its axle "
            + $"({string.Join(" ", new[] { shaft.Behind.X, shaft.Behind.Y, shaft.Behind.Z }.Select(c => F(Math.Abs(c) < 1e-9 ? 0 : c)))}), at {Xyz(_ghost.Position)}; on the axle now: {string.Join(", ", shaft.Mates)}");
    }

    private string ShaftStatus() => _shaft is { } s
        ? $"A {ShaftSlots.Noun(_placingKey ?? "gear")} on {s.Anchor}'s shaft, {F(_shaftT)} m behind it: move the mouse to slide it along, click to put it there and join it on the axle. Alt places it freely (still joined), Esc cancels"
        : "";

    /// <summary>Turns the ghost (a gear, or any catalogue wheel: built along +z) onto the axle's direction.</summary>
    private void AimGhostOnShaft()
    {
        if (_ghost is null || _shaft is not { } s) return;
        var along = V(s.Behind).Normalized();
        var z = new Vector3(0, 0, 1);
        _ghost.Basis = Mathf.Abs(along.Dot(z)) > 0.9999f ? Basis.Identity : new Basis(new Quaternion(z, -along));   // a gear is the same either way round
        // seen through whatever is drawn round the shaft: a windmill's windshaft (0.25 m) is thicker than a module-5 72 (0.185 m at its tips)
        foreach (var g in Descendants(_ghost).OfType<GeometryInstance3D>())
            if (g is not Label3D) g.MaterialOverlay = ShaftGhostOverlay;
    }

    private static readonly StandardMaterial3D ShaftGhostOverlay = new()
    {
        AlbedoColor = new Color(0.2f, 1f, 0.85f, 0.45f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = 1,
    };

    /// <summary>
    /// The ghost held on the axle: the point of the line nearest the mouse's ray, in 5 cm steps from the selected part while grid snap is
    /// on, and onto the default spot within 3 cm of it. Looking straight down the axle the ray tells nothing: the ghost stays.
    /// </summary>
    private void MoveGhostOnShaft(Vector2 screen, ShaftSlots.Shaft s)
    {
        var (from, dir) = Ray(screen);
        var p = V(s.Point);
        var b = V(s.Behind).Normalized();
        float bd = b.Dot(dir);
        float denom = 1 - bd * bd;
        if (denom > 1e-4f)
        {
            var w = p - from;
            float t = (bd * w.Dot(dir) - w.Dot(b)) / denom;   // the line's parameter nearest the ray
            if (_gridSnap) t = Mathf.Snapped(t, GridStep);
            if (Mathf.Abs(t - _shaftDefaultT) < 0.03f) t = _shaftDefaultT;
            _shaftT = t;
        }
        _ghost!.Visible = true;
        _ghost.Position = V(s.At(_shaftT));
        _ghostOnShaft = true;
        DrawAxle(s, _shaftT);
        _status.Text = ShaftStatus();
    }

    /// <summary>The gear put where its ghost is: (wheel ...), any (set ...) turning it onto the axle, then (arbor selected new).</summary>
    private void PlaceGearOnShaft(string paletteId, bool keepPlacing)
    {
        if (_shaft is not { } s || _ghost is not { Visible: true }) return;
        if (!_session.Document.Parts.ContainsKey(s.Anchor)) { CancelPlacing(); return; }
        string material = _placeMaterial ?? UsualMaterial(_placingKey ?? "gear");
        bool generator = _placingKey == "generator";
        string id = NewPartId(generator ? "generator" : "wheel", paletteId, material);   // in a lesson, its role
        var at = _ghost.Position;
        var rel = at - V(s.Point);
        float along = rel.Dot(V(s.Behind));
        float off = (rel - V(s.Behind) * along).Length();
        // a lesson looks once all three are in: the wheel alone, not yet on the axle, is not the player's mistake
        bool checking = _lessonChecking, placed;
        _lessonChecking = true;
        try
        {
            if (generator)
                // a generator is turned by the gear it goes behind (#:on), not keyed to an arbor: one edit, one undo
                placed = RunCommand($"(begin (generator {id} #:at {Xyz(at)} #:material {material}) (set {id} #:on {s.Anchor}))");
            else
            {
                placed = RunCommand($"(wheel {id} #:at {Xyz(at)} #:catalogue {paletteId} #:material {material})");
                if (placed) foreach (var (key, value) in s.WheelProps) RunCommand($"(set {id} #:{key} {value})");
                placed = placed && RunCommand($"(arbor {s.Partner} {id})");   // the axle's lead, for a wheel already on one (ShaftSlots.PartnerOf)
            }
        }
        finally { _lessonChecking = checking; }
        LessonCheck();
        if (!placed) return;
        GD.Print($"[BuildMode] {ShaftSlots.Noun(_placingKey ?? "gear")} on {s.Anchor}'s shaft: {id} ({paletteId}) at {Xyz(at)}, {F(along)} m behind {s.Anchor} along its axle, "
            + (generator ? $"turned by {s.Anchor} (#:on)" : $"on {s.Partner}'s arbor")
            + (_ghostOnShaft ? $" (the default spot {F(_shaftDefaultT)} m)" : $", placed freely (Alt): {F(off)} m off the axle line"));
        string anchor = s.Anchor;
        if (keepPlacing && ShaftSlots.Of(_session.Document, anchor) is { } next)
        {
            // Shift: the next gear on the same axle, from where the next one goes now
            _shaft = next;
            _shaftT = _shaftDefaultT = (float)ShaftSlots.ClearT(_session.Document, next, NewWheelHalf());
            _ghost.Position = V(next.At(_shaftT));
            DrawAxle(next, _shaftT);
            return;
        }
        CancelPlacing();
        Select(id);
    }

    /// <summary>A bright line along the axle (drawn over everything, so it reads through the sails and the gears on it): from a little
    /// in front of the selected part to a little beyond the ghost or the default spot.</summary>
    private void DrawAxle(ShaftSlots.Shaft s, float t)
    {
        HideAxle();
        var p = V(s.Point);
        var b = V(s.Behind).Normalized();
        float back = Mathf.Max(t, _shaft is null ? (float)s.DefaultT : _shaftDefaultT) + 0.5f;
        float front = Mathf.Min(t - 0.5f, s.Anchor is { } a && _session.Document.Parts.GetValueOrDefault(a)?.Kind == "windmill" ? 0 : -0.3f);   // a wheel slid out in front too
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 1f, 0.85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        mat.AlbedoColor = mat.AlbedoColor with { A = 0.85f };
        _axleLine = Shapes.Rod(p + b * front, p + b * back, 0.02f, mat);
        _axleLine.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(_axleLine);
    }

    private void HideAxle()
    {
        _axleLine?.QueueFree();
        _axleLine = null;
    }

    /// <summary>
    /// "gear-on TEETH [hover]" (a world's script, "build gear-on 72"): with a windmill or wheel selected, picks the module-5 gear of that
    /// many teeth on the gear's card, presses the inspector's "Put a gear on this shaft" (a real click), and then (without "hover") places
    /// the gear at the default spot as a click there does; with "hover" the ghost waits on the axle for the mouse (click-at, hover-at).
    /// "gear-on ID [hover]" takes any catalogue wheel instead ("gear-on cart-wheel-120cm"): its entry picked in the "Which:" list and
    /// its size on the card, as a person picks them, then the same button ("Put a wheel on this shaft").
    /// </summary>
    private ScriptedInput.Step GearOnStep(string[] w) => ButtonStep(w, "gear_on", "gear-on", ShaftSlots.Takes,
        id => _shaft is { } s && s.Anchor == id, size => PlaceGearOnShaft(size, keepPlacing: false));

    /// <summary>
    /// A script word that presses one of the selected part's inspector buttons that start placing a gear (<paramref name="meta"/> names
    /// the button): picks the module-5 gear of w[1] teeth on the gear's card, scrolls to the button and clicks it (a real click) until
    /// <paramref name="started"/> says its mode is on, then (without "hover") <paramref name="place"/>s the gear where the ghost starts.
    /// </summary>
    private ScriptedInput.Step ButtonStep(string[] w, string meta, string word, Func<PartSpec, bool> takes, Func<string, bool> started, Action<string> place)
    {
        string size = w[1].All(char.IsDigit) ? $"involute-gear-m5-{w[1]}" : w[1];
        bool hover = w.Length > 2 && w[2] == "hover";
        if (_gearOnFor is { } pressed && started(pressed) && _placingPaletteId == size)
        {
            _gearOnFor = null;
            _gearOnTries = 0;
            if (!hover) place(size);
            return ScriptedInput.Step.Next;
        }
        if (_gearOnFor is null)
        {
            if (_selectedId is not { } id || !_session.Document.Parts.TryGetValue(id, out var part) || !takes(part))
            { GD.Print($"[BuildMode] {word}: select a {(meta == "gear_on" ? "windmill or a wheel" : "wheel")} first (selected: {_selectedId ?? "none"})"); return ScriptedInput.Step.Next; }
            if (_palette.All(p => p.Id != size)) { GD.Print($"[BuildMode] {word}: no gear {size} in the catalogue"); return ScriptedInput.Step.Next; }
            _variantChosen[EntryKeyOf(size)] = size;   // the card's size, as picking it there does
            if (meta == "gear_on") ChooseShaftKind(EntryKeyOf(size));   // and the "Which:" list's entry
            _gearOnFor = id;
        }
        if (++_gearOnTries > 60) { GD.Print($"[BuildMode] {word}: the button didn't start placing on {_gearOnFor}"); _gearOnFor = null; _gearOnTries = 0; return ScriptedInput.Step.Next; }
        var button = Descendants(_inspector).OfType<Button>().FirstOrDefault(b => b.HasMeta(meta) && b.GetMeta(meta).AsString() == _gearOnFor);
        if (button is null || button.Size.X < 1) return ScriptedInput.Step.Again;   // laid out first
        if (_gearOnTries % 10 == 1) { _inspector.GetParent<ScrollContainer>().EnsureControlVisible(button); return ScriptedInput.Step.Again; }   // scrolled to it, as a person would
        if (_gearOnTries % 10 != 3) return ScriptedInput.Step.Again;   // then a click, every 10 frames till it takes
        var at = GetViewport().GetScreenTransform() * button.GetGlobalRect().GetCenter();
        _inputScript!.Mouse(at);
        _inputScript.Mouse(at, MouseButton.Left, true);
        _inputScript.Mouse(at, MouseButton.Left, false);
        return ScriptedInput.Step.Again;
    }

    private string? _gearOnFor;
    private int _gearOnTries;
}
