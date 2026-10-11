using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// "Put a gear on this shaft" (building plan, phase 2). The inspector of a selected windmill or wheel has a button that starts placing a
/// gear (the size from the gear's card, as any placing), its ghost held on the part's axle line (<see cref="ShaftSlots"/>: a windmill's
/// windshaft behind the hub, a wheel's own axis) and slid along it by the mouse, starting where the next gear goes by default (1.3 m
/// behind a bare windmill's hub, where the route's first wheel is; else 0.1 m behind the hindmost wheel on the axle). A click places the
/// gear with an ordinary (wheel ...) and joins it to the selected part by an ordinary (arbor selected new) (for a wheel already on an
/// arbor, (arbor lead new), the arbor's lead: <see cref="ShaftSlots.PartnerOf"/>): the same commands the console
/// and the Axle tool make, so nothing in the physics changes. The constraint is a separate mode MoveGhost asks first; the free ghost is as
/// before. Alt held frees the ghost (as it does a windmill over a post): the gear goes where the free ghost is, still joined on the axle.
/// The axle line is drawn while the mode is on, and while the mouse is over the button.
/// </summary>
public partial class BuildMode
{
    private ShaftSlots.Shaft? _shaft;   // the axle the gear being placed goes on, while it is
    private float _shaftT;              // where its ghost is along it (metres behind the selected part's #:at)
    private bool _ghostOnShaft;         // whether the ghost is held on the line now (not Alt-freed)
    private MeshInstance3D? _axleLine;

    private static Vector3 V(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>The inspector's row for a windmill or a wheel: the button that starts putting a gear on its shaft.</summary>
    private void AddGearOnButton(PartSpec part)
    {
        if (!ShaftSlots.Takes(part)) return;
        var b = new Button
        {
            Text = "Put a gear on this shaft",
            TooltipText = part.Kind == "windmill"
                ? "A gear on the windshaft behind the sails (the first 1.3 m back), turned by the sails. Pick its size on the card; slide it along with the mouse"
                : "A gear on this wheel's axle, 0.1 m behind the last one on it, turning with it. Pick its size on the card; slide it along with the mouse",
        };
        b.SetMeta("gear_on", part.Id);
        b.Pressed += () => StartGearOn(part.Id);
        b.MouseEntered += () => { if (ShaftSlots.Of(_session.Document, part.Id) is { } s) DrawAxle(s, (float)s.DefaultT); };
        b.MouseExited += () => { if (_shaft is null) HideAxle(); };
        _inspector.AddChild(b);
    }

    /// <summary>The button pressed: placing a gear starts (the card's size), its ghost on the axle at the default spot.</summary>
    private void StartGearOn(string id)
    {
        if (ShaftSlots.Of(_session.Document, id) is not { } shaft) return;
        StartEntry("gear");
        if (_placingPaletteId is null) return;
        _shaft = shaft;
        _shaftT = (float)shaft.DefaultT;
        AimGhostOnShaft();
        _ghost!.Position = V(shaft.At(_shaftT));
        _ghost.Visible = true;
        _ghostOnShaft = true;
        DrawAxle(shaft, _shaftT);
        ShowCard("gear");
        _status.Text = ShaftStatus();
        GD.Print($"[BuildMode] gear on {id}'s shaft: placing {_placingPaletteId}, the ghost {F(_shaftT)} m behind {id} along its axle "
            + $"({string.Join(" ", new[] { shaft.Behind.X, shaft.Behind.Y, shaft.Behind.Z }.Select(c => F(Math.Abs(c) < 1e-9 ? 0 : c)))}), at {Xyz(_ghost.Position)}; on the axle now: {string.Join(", ", shaft.Mates)}");
    }

    private string ShaftStatus() => _shaft is { } s
        ? $"A gear on {s.Anchor}'s shaft, {F(_shaftT)} m behind it: move the mouse to slide it along, click to put it there and join it on the axle. Alt places it freely (still joined), Esc cancels"
        : "";

    /// <summary>Turns the ghost (a gear built along +z) onto the axle's direction.</summary>
    private void AimGhostOnShaft()
    {
        if (_ghost is null || _shaft is not { } s) return;
        var along = V(s.Behind).Normalized();
        var z = new Vector3(0, 0, 1);
        _ghost.Basis = Mathf.Abs(along.Dot(z)) > 0.9999f ? Basis.Identity : new Basis(new Quaternion(z, -along));   // a gear is the same either way round
    }

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
            if (Mathf.Abs(t - (float)s.DefaultT) < 0.03f) t = (float)s.DefaultT;
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
        string id = $"wheel_{_serial++}";
        while (_session.Document.HasName(id)) id = $"wheel_{_serial++}";
        var at = _ghost.Position;
        var rel = at - V(s.Point);
        float along = rel.Dot(V(s.Behind));
        float off = (rel - V(s.Behind) * along).Length();
        if (!RunCommand($"(wheel {id} #:at {Xyz(at)} #:catalogue {paletteId} #:material {_placeMaterial ?? UsualMaterial("gear")})")) return;
        foreach (var (key, value) in s.WheelProps) RunCommand($"(set {id} #:{key} {value})");
        if (!RunCommand($"(arbor {s.Partner} {id})")) return;   // the axle's lead, for a wheel already on one (ShaftSlots.PartnerOf)
        GD.Print($"[BuildMode] gear on {s.Anchor}'s shaft: {id} ({paletteId}) at {Xyz(at)}, {F(along)} m behind {s.Anchor} along its axle, on {s.Partner}'s arbor"
            + (_ghostOnShaft ? $" (the default spot {F(s.DefaultT)} m)" : $", placed freely (Alt): {F(off)} m off the axle line"));
        string anchor = s.Anchor;
        if (keepPlacing && ShaftSlots.Of(_session.Document, anchor) is { } next)
        {
            // Shift: the next gear on the same axle, from where the next one goes now
            _shaft = next;
            _shaftT = (float)next.DefaultT;
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
        float back = Mathf.Max(t, (float)s.DefaultT) + 0.5f;
        float front = s.Anchor is { } a && _session.Document.Parts.GetValueOrDefault(a)?.Kind == "windmill" ? 0 : -0.3f;
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
    /// </summary>
    private ScriptedInput.Step GearOnStep(string[] w)
    {
        string size = $"involute-gear-m5-{w[1]}";
        bool hover = w.Length > 2 && w[2] == "hover";
        if (_shaft is { } s && s.Anchor == _gearOnFor && _placingPaletteId == size)
        {
            _gearOnFor = null;
            _gearOnTries = 0;
            if (!hover) PlaceGearOnShaft(size, keepPlacing: false);
            return ScriptedInput.Step.Next;
        }
        if (_gearOnFor is null)
        {
            if (_selectedId is not { } id || !_session.Document.Parts.TryGetValue(id, out var part) || !ShaftSlots.Takes(part))
            { GD.Print($"[BuildMode] gear-on: select a windmill or a wheel first (selected: {_selectedId ?? "none"})"); return ScriptedInput.Step.Next; }
            if (_palette.All(p => p.Id != size)) { GD.Print($"[BuildMode] gear-on: no gear {size} in the catalogue"); return ScriptedInput.Step.Next; }
            _variantChosen["gear"] = size;   // the card's size, as picking it there does
            _gearOnFor = id;
        }
        if (++_gearOnTries > 60) { GD.Print($"[BuildMode] gear-on: the button didn't start placing on {_gearOnFor}"); _gearOnFor = null; _gearOnTries = 0; return ScriptedInput.Step.Next; }
        var button = Descendants(_inspector).OfType<Button>().FirstOrDefault(b => b.HasMeta("gear_on") && b.GetMeta("gear_on").AsString() == _gearOnFor);
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
