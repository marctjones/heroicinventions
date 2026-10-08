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
        var labels = new List<(Label3D Label, Rect2 Rect, int Rank, ulong Age)>();
        float tanHalf = Mathf.Tan(Mathf.DegToRad(_camera.Fov / 2));
        float screenH = GetViewport().GetVisibleRect().Size.Y;
        foreach (var view in views)
        {
            if (!IsInstanceValid(view)) continue;
            foreach (var node in view.FindChildren("*", "Label3D", true, false))
            {
                var l = (Label3D)node;
                if (!l.IsVisibleInTree() || string.IsNullOrWhiteSpace(l.Text)) continue;
                if (_allLabels || _camera.IsPositionBehind(l.GlobalPosition)) { Show(l, true); continue; }
                // its size on screen: a fixed-size label's texture pixels map to about pixelSize · height / (2 tan(fov/2))
                // screen pixels each (Skins.ScreenLabel); a world-sized one shrinks with distance
                float distance = Mathf.Max(0.01f, _camera.GlobalPosition.DistanceTo(l.GlobalPosition));
                float perPixel = l.PixelSize * screenH / (2 * tanHalf) / (l.FixedSize ? 1 : distance);
                var font = l.Font ?? ThemeDB.FallbackFont;
                var size = font.GetMultilineStringSize(l.Text, HorizontalAlignment.Center, l.Width > 0 && l.AutowrapMode != TextServer.AutowrapMode.Off ? l.Width : -1, l.FontSize)
                           * perPixel;
                var centre = _camera.UnprojectPosition(l.GlobalPosition);
                var rect = new Rect2(centre - size / 2, size).Grow(2);
                int rank = l.Text.Any(char.IsDigit) ? 2 : 1;
                labels.Add((l, rect, rank, l.GetInstanceId()));
            }
        }
        if (_allLabels) return;
        var placed = new List<Rect2>();
        foreach (var (label, rect, _, _) in labels.OrderByDescending(x => x.Rank).ThenByDescending(x => x.Label.FontSize).ThenByDescending(x => x.Age))
        {
            bool clear = !placed.Any(p => p.Intersects(rect));
            Show(label, clear);
            if (clear) placed.Add(rect);
        }
    }

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
