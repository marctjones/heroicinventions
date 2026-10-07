using Godot;

namespace HeroicInventions;

/// <summary>
/// Measuring a machine's framing (#86): its bounds projected onto the
/// screen against the part of the window the side panels don't cover.
/// </summary>
public partial class Main
{
    private readonly bool _framingReport = OS.GetEnvironment("HEROIC_FRAMING_REPORT") == "1";

    /// <summary>The window region the panels leave clear, in viewport pixels.</summary>
    private Rect2 ClearArea()
    {
        var size = GetViewport().GetVisibleRect().Size;
        float left = _leftPanel.Visible && _leftPanel.Size.X > 0 ? _leftPanel.GetGlobalRect().End.X : size.X * 0.2f;
        float right = _infoPanel.Visible && _infoPanel.Size.X > 0 ? _infoPanel.GetGlobalRect().Position.X : size.X * 0.75f;
        if (right - left < size.X * 0.3f) { left = 0; right = size.X; }   // panels hidden or squeezed: use the whole width
        return new Rect2(left, 0, right - left, size.Y);
    }

    /// <summary>Where the box's eight corners land on screen, as one rectangle; null if any is behind the camera.</summary>
    private Rect2? ScreenRect(Aabb box)
    {
        Rect2? rect = null;
        for (int i = 0; i < 8; i++)
        {
            var corner = box.GetEndpoint(i);
            if (_camera.IsPositionBehind(corner)) return null;
            var p = _camera.UnprojectPosition(corner);
            rect = rect is { } r ? r.Expand(p) : new Rect2(p, Vector2.Zero);
        }
        return rect;
    }

    /// <summary>
    /// HEROIC_FRAMING_REPORT=1: how much of the clear area a machine fills under its profile. A report only:
    /// re-framing from bounds was tried and withdrawn, since a machine's bounds include its ponds, channels and
    /// launch arcs (a trebuchet's profile leaves room for the throw on purpose), so the numbers flag profiles to
    /// look at, and a person fixes the profile.
    /// </summary>
    private void CheckFraming(string name, Node3D view)
    {
        if (!_framingReport) return;
        var box = BoundsOf(view);
        if (box.Size == Vector3.Zero) return;
        var clear = ClearArea();
        string verdict = ScreenRect(box) is { } r && r.Area > 0
            ? $"fills {r.Intersection(clear).Area / clear.Area:P0} of the clear area, {1 - r.Intersection(clear).Area / r.Area:P0} of it outside"
            : "part of its bounds is behind the camera (not measured)";
        GD.Print($"[framing] {name}: {verdict}");
    }
}
