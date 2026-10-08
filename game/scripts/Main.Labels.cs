using Godot;

namespace HeroicInventions;

/// <summary>
/// Label density (#148): a machine with many parts (the Hierapolis sawmill) or many events (the drop test's impact
/// readouts) wrote labels over each other until none could be read. A few times a second every visible label in the
/// running machines is laid out on screen, most important first, and any that would overlap one already placed fades
/// out until there's room again. Readouts (any label with a number in it: a temperature, a pressure, an impact) come
/// before plain part names, then bigger text, then newer labels before older ones, so the latest impact shows and the
/// first one makes way. L shows every label regardless.
///
/// It hides a label by taking its text and outline alpha to zero (remembering what they were, so a tinted label keeps
/// its tint), never through Visible, which builders set themselves (a rope's bar label, a released rope's tag,
/// silhouette mode). GeometryInstance3D.Transparency was tried first: a Label3D drawn with no depth test ignores it.
/// </summary>
public partial class Main
{
    private bool _allLabels;
    private int _declutterTick;
    private const int DeclutterEvery = 3;   // physics ticks between layouts: twenty a second at 60 Hz (impact readouts drift upward)

    private void ToggleAllLabels() => _allLabels = !_allLabels;

    // labels faded out by the layout, kept faded just before every frame is drawn (see Show)
    private static readonly HashSet<Label3D> Faded = [];
    private static bool _hooked;

    private void DeclutterLabels()
    {
        if (++_declutterTick < DeclutterEvery) return;
        _declutterTick = 0;
        var views = _views.Count > 0 ? _views.Cast<Node>() : _current is not null ? [_current] : [];
        var labels = new List<(Label3D Label, Vector2 Anchor, Vector2 Size, float PerPixel, float Line, int Rank, ulong Age)>();
        float tanHalf = Mathf.Tan(Mathf.DegToRad(_camera.Fov / 2));
        float screenH = GetViewport().GetVisibleRect().Size.Y;
        foreach (var view in views)
        {
            if (!IsInstanceValid(view)) continue;
            foreach (var node in view.FindChildren("*", "Label3D", true, false))
            {
                var l = (Label3D)node;
                if (!l.IsVisibleInTree() || string.IsNullOrWhiteSpace(l.Text)) continue;
                if (_allLabels || _camera.IsPositionBehind(l.GlobalPosition)) { Show(l, true); Place(l, Vector2.Zero, 1); continue; }
                // its size on screen: a fixed-size label's texture pixels map to about pixelSize · height / (2 tan(fov/2))
                // screen pixels each (Skins.ScreenLabel); a world-sized one shrinks with distance
                float distance = Mathf.Max(0.01f, _camera.GlobalPosition.DistanceTo(l.GlobalPosition));
                float perPixel = l.PixelSize * screenH / (2 * tanHalf) / (l.FixedSize ? 1 : distance);
                var font = l.Font ?? ThemeDB.FallbackFont;
                var size = font.GetMultilineStringSize(l.Text, HorizontalAlignment.Center, l.Width > 0 && l.AutowrapMode != TextServer.AutowrapMode.Off ? l.Width : -1, l.FontSize)
                           * perPixel + new Vector2(4, 4);
                int rank = l.Text.Any(char.IsDigit) ? 2 : 1;
                labels.Add((l, _camera.UnprojectPosition(l.GlobalPosition), size, perPixel, font.GetHeight(l.FontSize) * perPixel, rank, l.GetInstanceId()));
            }
        }
        if (_allLabels) return;
        var keepClear = KeepClearRects(views);
        // the window, and the panels over it: a label is not written under the HUD (they are boxes, not the full height:
        // below the info panel the scene shows again)
        var window = GetViewport().GetVisibleRect();
        foreach (var panel in new Control[] { _leftPanel, _infoPanel })
            if (panel.Visible && panel.Size.X > 0) keepClear.Add(panel.GetGlobalRect().Grow(4));
        var placed = new List<Rect2>();
        foreach (var (label, anchor, size, perPixel, line, rank, _) in labels.OrderByDescending(x => x.Rank).ThenByDescending(x => x.Label.FontSize).ThenByDescending(x => x.Age))
        {
            // where it may go, in screen pixels from its anchor: on it, then beside it, then (a name only) above and below.
            // A number stays at its anchor's height: a level mark's number, a water level, a gauge's reading belong to
            // that height. Only a billboard can be moved on screen this way.
            var moves = label.Billboard == BaseMaterial3D.BillboardModeEnum.Disabled
                ? [Vector2.Zero]
                : LabelMoves(size, line, Moved(label, perPixel), sideways: rank == 2);
            Rect2? chosen = null;
            foreach (var move in moves)
            {
                var rect = new Rect2(anchor + move - size / 2, size);
                if (placed.Any(p => p.Intersects(rect)) || keepClear.Any(k => k.Intersects(rect))) continue;
                if (window.HasPoint(anchor) && !window.Encloses(rect)) continue;   // not slid off the window
                chosen = rect;
                Place(label, move, perPixel);
                break;
            }
            Show(label, chosen is not null);
            if (chosen is { } c) placed.Add(c);
            else Place(label, Vector2.Zero, perPixel);
        }
        ReportLabels(labels.Count, placed, keepClear);
    }

    private ulong _labelReportAt;

    /// <summary>
    /// HEROIC_FRAMING_REPORT=1: every 5 s, how many labels are shown, moved aside and faded, and how many things
    /// (dials, a glint, the figure, a trail) labels were kept off: the label half of #190's check, as numbers.
    /// </summary>
    private void ReportLabels(int all, List<Rect2> placed, List<Rect2> keepClear)
    {
        if (!_framingReport || Time.GetTicksMsec() < _labelReportAt) return;
        _labelReportAt = Time.GetTicksMsec() + 5000;
        int moved = 0;
        var views = _views.Count > 0 ? _views.Cast<Node>() : _current is not null ? [_current] : [];
        foreach (var view in views)
            if (IsInstanceValid(view))
                foreach (var node in view.FindChildren("*", "Label3D", true, false))
                    if (node is Label3D l && l.Offset != Vector2.Zero && !Faded.Contains(l)) moved++;
        GD.Print($"[labels] {_currentName}: {placed.Count} of {all} shown ({moved} moved aside), {all - placed.Count} faded; {keepClear.Count} kept clear");
    }

    /// <summary>
    /// The places a label may go, nearest first. Its current place (if it was moved) comes straight after its anchor,
    /// so a label that had to step aside stays put while it is still clear instead of hopping back and forth. A long
    /// readout wrapped into three lines or more grows upward from its anchor (its last line there) before anything else:
    /// centred, it hung down over the part it names (the sluice box's riffles readout over its gold).
    /// </summary>
    private static Vector2[] LabelMoves(Vector2 size, float line, Vector2 current, bool sideways)
    {
        float sx = size.X / 2 + 22, sy = size.Y + 2;
        var rise = size.Y > 2.5f * line ? new Vector2(0, -(size.Y - line) / 2) : Vector2.Zero;
        var moves = new List<Vector2> { rise };
        if (current != rise && (!sideways || Mathf.IsEqualApprox(current.Y, rise.Y))) moves.Add(current);
        // a number goes one step aside at most: further off, it would read as another thing's number
        moves.AddRange([rise + new Vector2(sx, 0), rise + new Vector2(-sx, 0)]);
        if (rise != Vector2.Zero) moves.Add(Vector2.Zero);
        if (!sideways) moves.AddRange([new(0, -sy), new(0, sy), new(sx, -sy), new(-sx, -sy), new(0, -2 * sy)]);
        return [.. moves];
    }

    /// <summary>The label's present shift on screen, in pixels (Label3D.Offset is in its texture's pixels, y up).</summary>
    private static Vector2 Moved(Label3D label, float perPixel) => new(label.Offset.X * perPixel, -label.Offset.Y * perPixel);

    private static void Place(Label3D label, Vector2 move, float perPixel)
    {
        var offset = perPixel > 0 ? new Vector2(move.X / perPixel, -move.Y / perPixel) : Vector2.Zero;
        if (label.Offset.DistanceSquaredTo(offset) > 0.25f) label.Offset = offset;
    }

    // what no label may cover, refreshed about once a second (dials don't come and go, but they move with a billowing room)
    private readonly List<GeometryInstance3D> _keepClearNodes = [];
    private int _keepClearAge = 1000;

    /// <summary>
    /// What a label must not be written over (#190): a gauge's face (its needle is the reading), a gold glint over a
    /// sluice's heap, the scale figure, and the throw's trail marks (Main.Trail.cs). Dials and glints are found by their
    /// shape, a flat disc at most 12.5 mm thick, so any instrument face built the same way is covered too.
    /// </summary>
    private List<Rect2> KeepClearRects(IEnumerable<Node> views)
    {
        if (++_keepClearAge > 20)
        {
            _keepClearAge = 0;
            _keepClearNodes.Clear();
            foreach (var view in views)
            {
                if (!IsInstanceValid(view)) continue;
                foreach (var node in view.FindChildren("*", "GeometryInstance3D", true, false))
                    if (node is GeometryInstance3D g && (g.HasMeta(KeepClearMeta) || IsInstrumentFace(g)))
                        _keepClearNodes.Add(g);
            }
        }
        var rects = new List<Rect2>();
        _keepClearNodes.RemoveAll(n => !IsInstanceValid(n));
        foreach (var n in _keepClearNodes)
            if (n.IsVisibleInTree() && ScreenRect(n.GlobalTransform * n.GetAabb()) is { } r && r.Size.X < 400) rects.Add(r.Grow(2));
        if (_figure is not null && IsInstanceValid(_figure) && _figure.Visible && !_camera.IsPositionBehind(_figure.GlobalPosition))
            rects.Add(FigureOnScreen(_figure.GlobalPosition));
        return rects;
    }

    /// <summary>
    /// A dial's face or a gold glint: a flat round disc (a cylinder at most 12.5 mm thick) at most a metre across in the
    /// world (a glint spreads to its channel's width) that is near white and opaque (a dial) or unshaded (a glint). Jar
    /// seals, leak holes and valve discs are the same shape but dark, and a vessel's water film is transparent and lit:
    /// all left out (the Baghdad battery's jar seals had kept every label off its jars).
    /// </summary>
    private static bool IsInstrumentFace(GeometryInstance3D g)
    {
        if (g is not MeshInstance3D { Mesh: CylinderMesh disc } m || disc.Height > 0.0125f || !Mathf.IsEqualApprox(disc.TopRadius, disc.BottomRadius)) return false;
        var size = (m.GlobalTransform * m.GetAabb()).Size;
        float thin = Mathf.Min(size.X, Mathf.Min(size.Y, size.Z)), wide = Mathf.Max(size.X, Mathf.Max(size.Y, size.Z));
        if (thin > 0.0125f || wide > 1f || wide < 0.02f) return false;
        if (m.MaterialOverride is not StandardMaterial3D mat) return false;
        if (mat.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded) return true;   // a glint
        // a dial's face: opaque and near white (a jar's seal or a leak's hole is a disc too, and dark)
        var c = mat.AlbedoColor;
        return mat.Transparency == BaseMaterial3D.TransparencyEnum.Disabled && c.V > 0.85f && c.S < 0.15f;
    }

    /// <summary>Meta on a mesh Main draws that labels must keep off (the throw's trail and landing marks).</summary>
    private const string KeepClearMeta = "keep_clear";

    /// <summary>
    /// Fades a label out, or brings it back. Some builders write their label's alpha every frame (an impact readout
    /// fades over 1.5 s, a digger's tag is tinted), so a faded label is re-faded just before each frame is drawn,
    /// after every builder has run, and the alpha a builder last wrote is what it gets back.
    /// </summary>
    private static void Show(Label3D label, bool shown)
    {
        if (!_hooked)
        {
            RenderingServer.FramePreDraw += Refade;
            _hooked = true;
        }
        bool faded = Faded.Contains(label);
        if (shown == !faded) return;
        if (!shown)
        {
            label.SetMeta("declutter_alpha", new Vector2(label.Modulate.A, label.OutlineModulate.A));
            Faded.Add(label);
            Fade(label);
        }
        else
        {
            Faded.Remove(label);
            var a = (Vector2)label.GetMeta("declutter_alpha");
            label.Modulate = label.Modulate with { A = a.X };
            label.OutlineModulate = label.OutlineModulate with { A = a.Y };
            label.RemoveMeta("declutter_alpha");
        }
    }

    private static void Fade(Label3D label)
    {
        label.Modulate = label.Modulate with { A = 0 };
        label.OutlineModulate = label.OutlineModulate with { A = 0 };
    }

    private static void Refade()
    {
        Faded.RemoveWhere(l => !IsInstanceValid(l));
        foreach (var label in Faded)
        {
            if (label.Modulate.A > 0)   // a builder wrote it since: that's the alpha to give back
                label.SetMeta("declutter_alpha", new Vector2(label.Modulate.A, ((Vector2)label.GetMeta("declutter_alpha")).Y));
            Fade(label);
        }
    }
}
