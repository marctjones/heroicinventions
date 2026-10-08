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

    private Rect2 SettledClearArea()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        return vp.X >= 900 ? new Rect2(230, 0, vp.X - 230 - 300, vp.Y) : new Rect2(0, 0, vp.X, vp.Y);
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
    /// Slides the orbit pivot sideways so the machine's bounds sit centred in the clear area, not the whole window
    /// (#86): the left and right panels are unequal, so anything wider than ~60% of the frame landed under one.
    /// Only a shift along the camera's right axis: each profile's distance and angles are kept (re-framing that
    /// changed the distance was withdrawn). Throwers keep their profile, which leaves room for the throw on purpose.
    /// </summary>
    private void CentreInClearArea(string name, Node3D view)
    {
        if (FollowBody.ContainsKey(name)) return;
        var box = BoundsOf(view);
        if (box.Size == Vector3.Zero || ScreenRect(box) is not { } rect) return;
        // not ClearArea(): the menu is still expanded (a wide list) when a machine is selected and collapses just
        // after, so the panels are measured at their settled widths: the 224 px left column, the ~290 px info panel
        var clear = SettledClearArea();
        float dx = rect.GetCenter().X - clear.GetCenter().X;
        // pixels to metres at the box's depth: the view is 2 d tan(fov/2) tall
        float depth = (box.GetCenter() - _camera.GlobalPosition).Dot(-_camera.GlobalTransform.Basis.Z);
        float perPixel = 2 * depth * Mathf.Tan(Mathf.DegToRad(_camera.Fov / 2)) / GetViewport().GetVisibleRect().Size.Y;
        if (Mathf.Abs(dx) < 4 || depth <= 0) return;
        _orbit.Pivot += _camera.GlobalTransform.Basis.X * dx * perPixel;   // camera and pivot move right together: the machine moves left
        _orbit.Apply();
        _homePivot = _orbit.Pivot;
        _homeProfile = new CameraProfile(_camera.GlobalPosition, _orbit.Pivot, _homeProfile.FovDegrees);
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
        var clear = SettledClearArea();
        string verdict = ScreenRect(box) is { } r && r.Area > 0
            ? $"fills {r.Intersection(clear).Area / clear.Area:P0} of the clear area, {1 - r.Intersection(clear).Area / r.Area:P0} of it outside"
            : "part of its bounds is behind the camera (not measured)";
        var c = box.GetCenter();
        GD.Print($"[framing] {name}: {verdict}; bounds centre ({c.X:F2} {c.Y:F2} {c.Z:F2}) size ({box.Size.X:F2} {box.Size.Y:F2} {box.Size.Z:F2})");
    }
}
