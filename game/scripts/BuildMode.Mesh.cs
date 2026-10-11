using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// "Add a meshing gear" and the live ratio (building plan, phase 3). The inspector of a selected wheel has a button (under "Put a gear on
/// this shaft") that starts placing a gear (the size from the gear's card), its ghost held on the pitch circle round the wheel
/// (<see cref="MeshSlots"/>): in the wheel's plane, at the two gears' centre distance (<see cref="LinkGestures.MeshCentreDistance"/>,
/// m (zA + zB) / 2, worked out again for each size picked on the card), its angle from the mouse (15 degree steps while grid snap is on),
/// starting at +x as the route's pinions stand. Drawn green on the circle. Alt frees it within the wheel's plane (any distance, where the
/// mouse's ray meets the plane), drawn orange, and still meshed: build mode doesn't refuse it. A click places it with an ordinary
/// (wheel ...) and meshes it by an ordinary (mesh selected new), so nothing in the physics changes (BuildSession rephases the teeth). A
/// mesh not at its centre distance, made here with Alt or by the Gear mesh tool, is drawn orange (<see cref="LinkGestures.MeshFit"/>,
/// the machine view's own rule), and the note says what it will do when it runs (<see cref="LinkGestures.MeshContact"/>): loose (still turns its
/// partner), not touching (passes nothing) or jammed (the pair can't turn). The live ratio (<see cref="TrainRatio"/>) is on the status line and on a label at the
/// train's last gear: "sails -> wheel_8: 256:1".
/// </summary>
public partial class BuildMode
{
    private MeshSlots.Ring? _meshRing;   // the wheel the gear being placed meshes with, while it is
    private float _ringAngle;            // where its ghost is round it (radians from the ring's U)
    private bool _ghostOnRing;           // whether the ghost is on the pitch circle now (not Alt-freed)
    private readonly List<MeshInstance3D> _ringLines = [];
    private string _ratioLogged = "";

    private static readonly Color OnCircle = new(0.3f, 1f, 0.35f), OffCircle = new(1f, 0.45f, 0.05f);

    private static StandardMaterial3D Glow(Color c, float alpha) => new()
    {
        AlbedoColor = c with { A = alpha }, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = 1,
    };

    private static readonly StandardMaterial3D RingGhostOn = Glow(OnCircle, 0.5f), RingGhostOff = Glow(OffCircle, 0.55f);

    /// <summary>The inspector's row for a wheel: the button that starts adding a gear in mesh with it.</summary>
    private void AddMeshWithButton(PartSpec part)
    {
        if (!MeshSlots.Takes(part)) return;
        var b = new Button
        {
            Text = "Add a meshing gear",
            TooltipText = "A gear whose teeth mesh with this one's, its centre on the circle where their pitch circles touch. Pick its size on the card; move the mouse round the circle. Alt places it freely (drawn orange, still meshed)",
        };
        b.SetMeta("mesh_with", part.Id);
        b.Pressed += () => StartMeshWith(part.Id);
        b.MouseEntered += () => { if (MeshSlots.Of(_session.Document, part.Id) is { } r) DrawRing(r, 0, RingRadius(r, _variantChosen.GetValueOrDefault("gear") ?? VariantOf("gear")), true); };
        b.MouseExited += () => { if (_meshRing is null) HideRing(); };
        _inspector.AddChild(b);
    }

    /// <summary>The button pressed: placing a gear starts (the card's size), its ghost on the pitch circle at +x.</summary>
    private void StartMeshWith(string id)
    {
        if (MeshSlots.Of(_session.Document, id) is not { } ring) return;
        StartEntry("gear");
        if (_placingPaletteId is null) return;
        _shaft = null;   // not a gear on a shaft, whatever was being placed before
        HideAxle();
        _meshRing = ring;
        _ringAngle = 0;
        AimGhostOnRing();
        PutGhostOnRing();
        ShowCard("gear");
        var p = _ghost!.Position - V(ring.Centre);
        GD.Print($"[BuildMode] mesh with {id}: placing {_placingPaletteId}, the ghost on the pitch circle {F(RingRadius(ring, _placingPaletteId))} m from {id}'s centre "
            + $"(module x (teeth + teeth) / 2), at {Xyz(_ghost.Position)}, {Xyz(p)} from it");
    }

    /// <summary>The centre distance for the gear being placed (or <paramref name="paletteId"/>) round the ring's wheel.</summary>
    private float RingRadius(MeshSlots.Ring ring, string? paletteId)
    {
        if (!_session.Document.Parts.TryGetValue(ring.Anchor, out var wheel)) return 0;
        var entry = _palette.FirstOrDefault(p => p.Id == paletteId).Catalogue;
        if (entry is null) return (float)LinkGestures.PitchRadius(wheel) * 2;
        return (float)LinkGestures.MeshCentreDistance(wheel, PartTemplates.Create(entry, "ghost", new Vec3(0, 0, 0), "bronze"));
    }

    /// <summary>
    /// How a gear from the card would mesh with the ring's wheel <paramref name="have"/> m from its centre (in its plane): what the
    /// machine view will run it as (<see cref="LinkGestures.MeshFit.Contact"/>). Null when the card or the wheel is missing.
    /// </summary>
    private LinkGestures.MeshFit? RingFit(MeshSlots.Ring ring, double have)
    {
        var entry = _palette.FirstOrDefault(p => p.Id == _placingPaletteId).Catalogue;
        if (entry is null || !_session.Document.Parts.TryGetValue(ring.Anchor, out var wheel)) return null;
        var ghost = PartTemplates.Create(entry, "ghost", new Vec3(0, 0, 0), "bronze");
        return new LinkGestures.MeshFit(ring.Anchor, "ghost", LinkGestures.MeshCentreDistance(wheel, ghost), have,
            wheel.Symbol("shape", "") == "gear" && ghost.Symbol("shape", "") == "gear", LinkGestures.SameModule(wheel, ghost),
            LinkGestures.TipRadius(wheel) + LinkGestures.TipRadius(ghost));
    }

    /// <summary>Whether the gear on the card has teeth the ring's wheel's size.</summary>
    private bool RingModulesMatch(MeshSlots.Ring ring)
    {
        var entry = _palette.FirstOrDefault(p => p.Id == _placingPaletteId).Catalogue;
        return entry is null || !_session.Document.Parts.TryGetValue(ring.Anchor, out var wheel)
            || LinkGestures.SameModule(wheel, PartTemplates.Create(entry, "ghost", new Vec3(0, 0, 0), "bronze"));
    }

    private string RingStatus() => _meshRing is { } r
        ? _ghostOnRing
            ? $"A gear meshing with {r.Anchor}, on the pitch circle {F(RingRadius(r, _placingPaletteId))} m from its centre, at {F(Mathf.RadToDeg(_ringAngle))}°: move the mouse round, click to put it there. Alt places it freely (orange, still meshed), Esc cancels"
                + (RingModulesMatch(r) ? "" : ". Its teeth are another size than " + r.Anchor + "'s (another module): drawn orange; it will run " + RingFit(r, RingRadius(r, _placingPaletteId))?.Note)
            : $"A gear meshing with {r.Anchor}, placed freely (Alt): {F(_ghost!.Position.DistanceTo(V(r.Centre)))} m from its centre, the pitch circle is {F(RingRadius(r, _placingPaletteId))} m. Drawn orange: still meshed; "
              + (RingFit(r, _ghost.Position.DistanceTo(V(r.Centre))) is { } fit ? fit.True ? "it will run true" : $"it will run {fit.Note}" : "")
        : "";

    /// <summary>Turns the ghost (a gear built along +z) parallel to the ring's wheel, seen through what stands round it.</summary>
    private void AimGhostOnRing()
    {
        if (_ghost is null || _meshRing is not { } r) return;
        var along = V(r.Normal).Normalized();
        var z = new Vector3(0, 0, 1);
        _ghost.Basis = Mathf.Abs(along.Dot(z)) > 0.9999f ? Basis.Identity : new Basis(new Quaternion(z, -along));
    }

    private void TintRingGhost(bool on)
    {
        if (_ghost is null) return;
        foreach (var g in Descendants(_ghost).OfType<GeometryInstance3D>())
            if (g is not Label3D) g.MaterialOverlay = on ? RingGhostOn : RingGhostOff;
    }

    /// <summary>The ghost at the ring's angle on its pitch circle (green, or orange when the modules differ).</summary>
    private void PutGhostOnRing()
    {
        if (_ghost is null || _meshRing is not { } r) return;
        float radius = RingRadius(r, _placingPaletteId);
        _ghost.Visible = true;
        _ghost.Position = V(r.At(_ringAngle, radius));
        _ghostOnRing = true;
        bool good = RingModulesMatch(r);
        TintRingGhost(good);
        DrawRing(r, _ringAngle, radius, good);
        _status.Text = RingStatus();
    }

    /// <summary>
    /// The ghost round the ring: where the mouse's ray meets the wheel's plane gives the angle (15 degree steps with grid snap on), the
    /// ghost on the pitch circle there; with <paramref name="free"/> (Alt) the ghost at that point itself, off the circle, drawn orange.
    /// Looking edge-on at the plane the ray tells nothing: the ghost stays.
    /// </summary>
    private void MoveGhostOnRing(Vector2 screen, MeshSlots.Ring r, bool free)
    {
        var (from, dir) = Ray(screen);
        var n = V(r.Normal).Normalized();
        var c = V(r.Centre);
        float denom = dir.Dot(n);
        Vector3? hit = null;
        if (Mathf.Abs(denom) > 1e-3f && (c - from).Dot(n) / denom is var t and > 0) hit = from + dir * t;
        if (free)
        {
            if (hit is not { } h)
            {
                // edge-on to the plane: the ghost stays, on the circle or not as it was
                _ghostOnRing = _ghost!.Position.DistanceTo(V(r.At(_ringAngle, RingRadius(r, _placingPaletteId)))) < 1e-3f;
                return;
            }
            _ghost!.Visible = true;
            _ghost.Position = h;
            _ghostOnRing = false;
            TintRingGhost(false);
            float radius = RingRadius(r, _placingPaletteId);
            DrawRing(r, (float)r.AngleOf(new Vec3(h.X, h.Y, h.Z)), radius, false, h);
            _status.Text = RingStatus();
            return;
        }
        if (hit is { } p)
        {
            float a = (float)r.AngleOf(new Vec3(p.X, p.Y, p.Z));
            if (_gridSnap) a = Mathf.Snapped(a, Mathf.DegToRad(15));
            _ringAngle = a;
        }
        PutGhostOnRing();
    }

    /// <summary>The gear put where its ghost is: (wheel ...), any (set ...) turning it parallel to the wheel, then (mesh selected new).</summary>
    private void PlaceMeshingGear(string paletteId)
    {
        if (_meshRing is not { } r || _ghost is not { Visible: true }) return;
        if (!_session.Document.Parts.ContainsKey(r.Anchor)) { CancelPlacing(); return; }
        string id = $"wheel_{_serial++}";
        while (_session.Document.HasName(id)) id = $"wheel_{_serial++}";
        var at = _ghost.Position;
        bool onRing = _ghostOnRing;
        float angle = _ringAngle;
        if (!RunCommand($"(wheel {id} #:at {Xyz(at)} #:catalogue {paletteId} #:material {_placeMaterial ?? UsualMaterial("gear")})")) return;
        foreach (var (key, value) in r.WheelProps) RunCommand($"(set {id} #:{key} {value})");
        if (!RunCommand($"(mesh {r.Anchor} {id})")) return;
        GD.Print($"[BuildMode] mesh with {r.Anchor}: {id} ({paletteId}) at {Xyz(at)}, {Xyz(at - V(r.Centre))} from {r.Anchor}"
            + (onRing ? $", on the pitch circle at {F(Mathf.RadToDeg(angle))}°" : ", placed freely (Alt)"));
        MeshNote(r.Anchor, id);
        CancelPlacing();
        Select(id);
    }

    /// <summary>The ring drawn over everything: the pitch circle (green, or orange off it), and a spoke from the wheel's centre to the ghost.</summary>
    private void DrawRing(MeshSlots.Ring r, float angle, float radius, bool good, Vector3? freeAt = null)
    {
        HideRing();
        if (radius <= 0) return;
        var mat = Glow(good ? OnCircle : OffCircle, 0.85f);
        var circle = Glow(OnCircle, good ? 0.85f : 0.5f);
        const int Segments = 72;
        for (int i = 0; i < Segments; i++)
        {
            var a = V(r.At(i * Mathf.Tau / Segments, radius));
            var b = V(r.At((i + 1) * Mathf.Tau / Segments, radius));
            AddRingLine(Shapes.Rod(a, b, 0.006f, circle));
        }
        AddRingLine(Shapes.Rod(V(r.Centre), freeAt ?? V(r.At(angle, radius)), 0.008f, mat));
    }

    private void AddRingLine(MeshInstance3D line)
    {
        line.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(line);
        _ringLines.Add(line);
    }

    private void HideRing()
    {
        foreach (var l in _ringLines) l.QueueFree();
        _ringLines.Clear();
    }

    // -------------------------------------------------------------- the live ratio

    /// <summary>The trains' ratios for the status line: "Gear train sails -> wheel_8: 256:1", or "" with no train.</summary>
    private string RatioStatus()
    {
        var trains = TrainRatio.Of(_session.Document);
        return trains.Count == 0 ? "" : "Gear train " + string.Join("; ", trains.Select(t => t.Text));
    }

    private string WithRatio(string status) => RatioStatus() is { Length: > 0 } r ? (status.Length > 0 ? $"{status}   |   {r}" : r) : status;

    /// <summary>
    /// A label at each train's last gear ("sails -> wheel_8: 256:1"), and the log line when a ratio changes. Meshes off their centre
    /// distance are drawn orange by DrawLinks.
    /// </summary>
    private void DrawRatio(Func<string, Vector3?> centre)
    {
        ClearRatioLabels();
        var trains = TrainRatio.Of(_session.Document);
        string logged = string.Join("; ", trains.Select(t => $"{t.Mover} -> {t.Last}, ratio {t.RatioText} ({t.Meshes} mesh{(t.Meshes == 1 ? "" : "es")})"));
        if (logged != _ratioLogged)
        {
            _ratioLogged = logged;
            GD.Print(logged.Length > 0 ? $"[BuildMode] train: {logged}" : "[BuildMode] train: none");
        }
        foreach (var t in trains)
        {
            if (centre(t.Last) is not { } at || !_session.Document.Parts.TryGetValue(t.Last, out var last)) continue;
            var label = new Label3D
            {
                Text = t.Text, Position = at + Vector3.Up * Mathf.Max(0.3f, (float)LinkGestures.PitchRadius(last) + 0.15f),   // over the gears beside it
                FontSize = 32, OutlineSize = 10, PixelSize = 0.0018f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
                Modulate = new Color(1f, 0.95f, 0.6f), OutlineModulate = new Color(0.05f, 0.07f, 0.12f), RenderPriority = 10, OutlineRenderPriority = 9,
            };
            AddChild(label);
            _ratioLabels.Add(label);
        }
    }

    private readonly List<Label3D> _ratioLabels = [];

    private void ClearRatioLabels()
    {
        foreach (var l in _ratioLabels) l.QueueFree();
        _ratioLabels.Clear();
    }

    /// <summary>A mesh's line: amber when its gears stand at their centre distance, orange and thicker when not (allowed; it runs as <see cref="LinkGestures.MeshFit.Contact"/> says).</summary>
    private (Color Colour, float Radius, LinkGestures.MeshFit? Fit) MeshLook(MeshSpec m)
    {
        var doc = _session.Document;
        if (!doc.Parts.TryGetValue(m.A, out var a) || !doc.Parts.TryGetValue(m.B, out var b)) return (new Color(1f, 0.75f, 0.2f), 0.008f, null);
        var fit = LinkGestures.FitOf(a, b);
        return fit.True ? (new Color(1f, 0.75f, 0.2f), 0.008f, fit) : (OffCircle, 0.02f, fit);
    }

    /// <summary>The log's line for a mesh just made: its centre distance, and whether it stands there (drawn amber) or not (orange, allowed).</summary>
    private void MeshNote(string a, string b)
    {
        var doc = _session.Document;
        if (!doc.Parts.TryGetValue(a, out var pa) || !doc.Parts.TryGetValue(b, out var pb)) return;
        var fit = LinkGestures.FitOf(pa, pb);
        GD.Print($"[BuildMode] mesh: {a}, {b}: centre distance {F(fit.Want)} m (pitch radii {F(LinkGestures.PitchRadius(pa))} + {F(LinkGestures.PitchRadius(pb))}), they stand {F(fit.Have)} m apart: "
            + (fit.True ? "on it" : $"drawn orange ({fit.Why}); meshed all the same: it will run {fit.Note}"));
    }

    /// <summary>"a+b" for a true mesh, "a+b~0.31/0.225" for one off its centre distance (have/want, m): the log's meshes field.</summary>
    private string MeshesField() => string.Join(",", _session.Document.Meshes.Select(m => MeshLook(m).Fit is { } f && !f.True
        ? $"{m.A}+{m.B}~{F(f.Have)}/{F(f.Want)}" : $"{m.A}+{m.B}"));

    /// <summary>"mesh-with TEETH [hover]" (a world's script): the selected wheel's "Add a meshing gear", as gear-on is the shaft's.</summary>
    private ScriptedInput.Step MeshWithStep(string[] w) => ButtonStep(w, "mesh_with", "mesh-with", MeshSlots.Takes,
        id => _meshRing is { } r && r.Anchor == id, size => PlaceMeshingGear(size));
}
