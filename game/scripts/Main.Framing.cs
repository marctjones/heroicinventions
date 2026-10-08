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

    /// <summary>
    /// The clear area once the menu has collapsed, in viewport units. The viewport is 1600 x 1000 whatever the window
    /// (project.godot stretches canvas items), so at 1280 x 800 the 236 px left column and the info panel from 976 px are
    /// 295 and 1220 units: the old 230 / 300 were window pixels, and let a machine run 100 px under the info panel.
    /// The info panel is measured when it is up; the left column is taken at its collapsed width, since the menu is still
    /// open when a machine is selected and collapses just after.
    /// </summary>
    private Rect2 SettledClearArea()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        if (vp.X < 900) return new Rect2(0, 0, vp.X, vp.Y);
        float right = _infoPanel.Visible && _infoPanel.Size.X > 0 ? _infoPanel.GetGlobalRect().Position.X : vp.X - SettledInfoWidth;
        return new Rect2(SettledLeft, 0, right - SettledLeft, vp.Y);
    }

    private const float SettledLeft = 296, SettledInfoWidth = 380;   // viewport units (see SettledClearArea)

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
    /// Fits a machine into the clear area between the panels (#86, #190): slides the orbit pivot sideways so its bounds
    /// sit centred between the panels, not in the whole window (the panels are unequal, so anything wider than ~60% of
    /// the frame landed under one); then, if the bounds are still wider or taller than the clear area, backs the camera
    /// off along its own line of sight until they fit (each profile's angles kept; at most <see cref="MostPullBack"/> its
    /// distance), and nudges the pivot up or down only as far as needed to keep the top and bottom in. Never closer than
    /// the profile: a profile's distance is the most it is zoomed in. Throwers keep their own profile (Main.Trail.cs).
    /// </summary>
    private void CentreInClearArea(string name, Node3D view)
    {
        if (FollowBody.ContainsKey(name)) return;
        var box = BoundsOf(view);
        if (box.Size == Vector3.Zero) return;
        // not ClearArea(): the menu is still expanded (a wide list) when a machine is selected and collapses just
        // after, so the panels are taken at their settled widths
        // a profile that looks away from the machine as built is looking at where the action will be (material-samples:
        // the cubes start above the frame and fall into it): leave it
        if (PartsOnScreen(view) is not { } parts || parts.Intersection(SettledClearArea()).Area < 0.2f * parts.Area) return;
        FitBox(box, () => PartsOnScreen(view), _orbit.Distance, MostPullBack);
        _homePivot = _orbit.Pivot;
        _homeDistance = _orbit.Distance;
        _homeProfile = new CameraProfile(_camera.GlobalPosition, _orbit.Pivot, _homeProfile.FovDegrees);
    }

    /// <summary>
    /// Where a machine's parts land on screen: the union of each part's own box projected, not the projection of the one
    /// box round them all. That big box's near corners stand in empty air in front of a deep machine (mirrors spread
    /// round a pot, a beam engine's cistern) and projected as much as 80% "outside" for a machine that sat whole in its
    /// frame. Parts deeper than <see cref="MineDepth"/> are left out. Null if a part is behind the camera.
    /// </summary>
    private Rect2? PartsOnScreen(Node view)
    {
        Rect2? all = null;
        foreach (var vi in Descendants(view).OfType<VisualInstance3D>())
        {
            if (vi is GpuParticles3D or CpuParticles3D or Label3D) continue;
            var local = vi.GetAabb();
            if (local.Size == Vector3.Zero) continue;
            var world = vi.GlobalTransform * local;
            // a shaft is framed at its head: what lies deeper than MineDepth (the Newcomen engine's 48 m pump rod) is cut off
            if (world.End.Y < -MineDepth) continue;
            if (world.Position.Y < -MineDepth) world = new Aabb(world.Position with { Y = -MineDepth }, world.Size with { Y = world.End.Y + MineDepth });
            if (ScreenRect(world) is not { } r) return null;
            all = all is { } a ? a.Merge(r) : r;
        }
        return all;
    }

    /// <summary>Moves the orbit camera so <paramref name="box"/> fits the clear area (see <see cref="CentreInClearArea"/>); true if it all fits.</summary>
    private bool FitBox(Aabb box, Func<Rect2?> onScreen, float startDistance, float mostPullBack)
    {
        var clear = SettledClearArea().Grow(-FrameMargin);
        for (int pass = 0; pass < 4; pass++)
        {
            if (onScreen() is not { } rect) return false;
            float scale = Mathf.Max(rect.Size.X / clear.Size.X, rect.Size.Y / clear.Size.Y);
            if (scale > 1.01f && _orbit.Distance < startDistance * mostPullBack)
            {
                _orbit.Distance = Mathf.Min(_orbit.Distance * scale, startDistance * mostPullBack);
                _orbit.Apply();
                if (onScreen() is not { } r2) return false;
                rect = r2;
            }
            // pixels to metres at the box's depth: the view is 2 d tan(fov/2) tall
            float depth = (box.GetCenter() - _camera.GlobalPosition).Dot(-_camera.GlobalTransform.Basis.Z);
            if (depth <= 0) return false;
            float perPixel = 2 * depth * Mathf.Tan(Mathf.DegToRad(_camera.Fov / 2)) / GetViewport().GetVisibleRect().Size.Y;
            float dx = rect.GetCenter().X - clear.GetCenter().X;
            // up or down only if an edge is out: a profile's height was chosen by eye
            float dy = rect.Size.Y > clear.Size.Y ? rect.GetCenter().Y - clear.GetCenter().Y
                : rect.Position.Y < clear.Position.Y ? rect.Position.Y - clear.Position.Y
                : rect.End.Y > clear.End.Y ? rect.End.Y - clear.End.Y : 0;
            if (Mathf.Abs(dx) < 4 && Mathf.Abs(dy) < 4 && scale <= 1.01f) break;
            // camera and pivot move together: moving them right moves the machine left on screen, up moves it down
            _orbit.Pivot += _camera.GlobalTransform.Basis.X * dx * perPixel - _camera.GlobalTransform.Basis.Y * dy * perPixel;
            _orbit.Apply();
        }
        return onScreen() is { } fitted && clear.Grow(FrameMargin).Encloses(fitted);
    }

    private const float MineDepth = 3;   // m below the ground that framing looks: a buried cistern yes, a mine no

    /// <summary>The most <see cref="CentreInClearArea"/> backs a camera off: a machine whose bounds hold a 65 m race fits, a speck doesn't get lost.</summary>
    private const float MostPullBack = 2.2f;

    /// <summary>Pixels kept clear inside the clear area's edges when fitting, so a machine doesn't touch a panel.</summary>
    private const float FrameMargin = 24;

    /// <summary>
    /// HEROIC_FRAMING_REPORT=1: how much of the clear area a machine fills once framed (its profile, then
    /// <see cref="CentreInClearArea"/>), and how much of it is still outside: of the one box round it all (the old measure)
    /// and of its parts' own boxes (what the eye sees). Bounds include ponds, channels and the ground a buried part sits
    /// in, so a number is a flag to look at a frame, not a verdict. History: re-framing from bounds was tried in #86 and
    /// withdrawn, because the box round a machine holds its ponds and launch arcs; it came back in #190 capped at
    /// <see cref="MostPullBack"/> x the profile's distance, measured on the parts, and skipped for throwers.
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
        string parts = PartsOnScreen(view) is { } p && p.Area > 0
            ? $"parts fill {p.Intersection(clear).Area / clear.Area:P0}, {1 - p.Intersection(clear).Area / p.Area:P0} outside"
            : "parts not measured";
        var c = box.GetCenter();
        GD.Print($"[framing] {name}: {verdict}; {parts}; bounds centre ({c.X:F2} {c.Y:F2} {c.Z:F2}) size ({box.Size.X:F2} {box.Size.Y:F2} {box.Size.Z:F2})");
    }

    // ---- the action stays in frame (#190) -----------------------------------------------------------------------

    private Node3D? _watched;          // the machine whose loose bodies are watched
    private Aabb _watchBase;           // its bounds as built
    private readonly Dictionary<RigidBody3D, Aabb> _followed = [];   // bodies gone off it, and where they are
    private Vector3 _watchPivot;       // where the camera eases to, so the grown box fits
    private float _watchDistance;
    private bool _watchWidened, _watchHooked, _watchTaken;
    private int _watchTick;
    private (int, int) _watchReported;
    private float _watchSize;          // the machine's own largest dimension, as built
    private Vector3 _watchCentre;
    private readonly HashSet<RigidBody3D> _letGo = [];   // bodies gone further than the camera may follow

    /// <summary>
    /// Keeps what a machine sets moving in frame (#190): balls that roll off the foot of a ramp, carts that run down a
    /// slope and on along the floor. A few times a second every body of the machine that has gone well outside what is
    /// framed (more than a quarter of the machine's size, at least half a metre: a pendulum's swing doesn't count) is
    /// followed, and the camera eases back and across, as <see cref="CentreInClearArea"/> would, to fit it. A body
    /// the camera can't fit without backing off more than it may is let go (it rolls on out of frame, and the camera
    /// goes back to what it can still frame); one
    /// that falls below the ground (a crate with nothing under it) or goes further than the machine is big plus 30 m is
    /// not followed, nor are machines that throw (they have their own camera), worlds, and anything once the player has
    /// moved the camera.
    /// </summary>
    private void KeepActionInFrame()
    {
        if (!_watchHooked) { _orbit.MovedByPlayer += () => _watchTaken = true; _watchHooked = true; }
        if (_current is null || !IsInstanceValid(_current) || _world is not null || _currentName is null || FollowBody.ContainsKey(_currentName)) return;
        if (_watched != _current)
        {
            _watched = _current;
            _watchBase = BoundsOf(_current);
            _watchSize = _watchBase.Size[(int)_watchBase.Size.MaxAxisIndex()];
            _watchCentre = _watchBase.GetCenter();
            _followed.Clear();
            _watchReported = (0, 0);
            _watchWidened = _watchTaken = false;
            _watchTick = 0;
            _letGo.Clear();
        }
        if (_watchTaken || _watchBase.Size == Vector3.Zero || ScreenRect(_watchBase) is null) return;
        if (++_watchTick % 10 == 0) GrowWatchBox();
        if (!_watchWidened) return;
        if (_orbit.Pivot.DistanceTo(_watchPivot) < 0.005f && Mathf.Abs(_orbit.Distance - _watchDistance) < 0.005f) return;
        _orbit.Pivot = _orbit.Pivot.Lerp(_watchPivot, 0.05f);
        _orbit.Distance = Mathf.Lerp(_orbit.Distance, _watchDistance, 0.05f);
        _orbit.Apply();
    }

    private void GrowWatchBox()
    {
        // measured on the machine as built, not the grown box: a box grown along a cart's run would excuse a cart that
        // had gone a quarter of that run further
        float size = _watchSize, slack = Mathf.Max(0.5f, size * 0.25f);
        bool changed = false;
        foreach (var node in _current!.FindChildren("*", "RigidBody3D", true, false))
        {
            var body = (RigidBody3D)node;
            if (body.Freeze || !body.IsVisibleInTree() || _letGo.Contains(body)) continue;
            var at = body.GlobalPosition;
            bool away = at.Y >= _watchBase.Position.Y - 0.5f && at.DistanceTo(_watchCentre) <= size + 30   // not falling for ever, nor far off
                        && !_watchBase.Grow(slack).HasPoint(at);
            if (!away) { changed |= _followed.Remove(body); continue; }
            var extent = new Aabb(at - Vector3.One * 0.3f, Vector3.One * 0.6f);
            if (_followed.TryGetValue(body, out var was) && was.Position.DistanceTo(extent.Position) < 0.05f) continue;
            _followed[body] = extent;
            if (!FitWatched(out _, out _)) { _followed.Remove(body); _letGo.Add(body); }   // further than the camera may back off: let it go
            changed = true;
        }
        if (!changed) return;
        // what is followed now (perhaps nothing: the ball has rolled away and the camera goes back to the machine)
        FitWatched(out _watchPivot, out _watchDistance);
        _watchWidened = true;
        if (_framingReport && (_followed.Count, _letGo.Count) != _watchReported)
            GD.Print($"[framing] {_currentName}: following {_followed.Count} bod{(_followed.Count == 1 ? "y" : "ies")} out of the frame ({_letGo.Count} let go); camera to {_watchDistance:F1} m (home {_homeDistance:F1} m)");
        _watchReported = (_followed.Count, _letGo.Count);
    }

    /// <summary>
    /// Where the camera would sit to fit the machine and every followed body: fitted from the home view, then the camera
    /// put back where it was, so it can ease there. True if it all fits within <see cref="MostPullBack"/>.
    /// </summary>
    private bool FitWatched(out Vector3 pivot, out float distance)
    {
        var box = _watchBase;
        foreach (var e in _followed.Values) box = box.Merge(e);
        var (wasPivot, wasDistance) = (_orbit.Pivot, _orbit.Distance);
        _orbit.Pivot = _homePivot;
        _orbit.Distance = _homeDistance;
        _orbit.Apply();
        bool fits = _followed.Count == 0 || FitBox(box, () => ScreenRect(box), _homeDistance, MostPullBack);
        (pivot, distance) = (_orbit.Pivot, _orbit.Distance);
        _orbit.Pivot = wasPivot;
        _orbit.Distance = wasDistance;
        _orbit.Apply();
        return fits;
    }
}
