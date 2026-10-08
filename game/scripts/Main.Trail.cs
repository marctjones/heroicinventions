using Godot;

namespace HeroicInventions;

/// <summary>
/// A thrown body's flight, drawn (readable over realistic): a dotted trail behind the stone or bolt a machine
/// throws, a gold disc where it first touches down (its range), and where it comes to rest a ring and the distances.
/// A 20 cm stone 15 m away is a speck on screen, and the throw is over in a second, so without these the one thing a
/// catapult is for (how far it threw) was seen only as a number in the panel. Drawn for the machines in <see cref="FollowBody"/>, whether or not the
/// camera is still following.
/// </summary>
public partial class Main
{
    private RigidBody3D? _trailBody;
    private Node3D? _trailRoot;
    private Vector3 _trailStart, _trailLastDot;
    private double _trailResting;
    private bool _trailLanded;
    private float _trailHighest;              // how far above its start it has risen
    private Vector3? _trailTouchdown;         // where it first came back to the ground after flying

    private const float TrailSpacing = 0.6f;   // m between dots
    private const int TrailMaxDots = 200;

    private void StartTrail(MachineView view, RigidBody3D? body)
    {
        _trailBody = body;
        _trailLanded = false;
        _trailResting = 0;
        _trailHighest = 0;
        _trailTouchdown = null;
        if (body is null) { _trailRoot = null; return; }
        _missileSeat = body.GlobalPosition;
        _missileReloaded = false;
        _landedAtMs = 0;
        if (_trailRoot is not null && IsInstanceValid(_trailRoot)) _trailRoot.QueueFree();   // the last throw's record goes when the next throw leaves
        _trailRoot = new Node3D { Name = "Trail" };
        MachineView.MarkScenery(_trailRoot);   // a record of where it flew, not a part
        view.AddChild(_trailRoot);   // freed with the machine
        _trailStart = _trailLastDot = body.GlobalPosition;
    }

    private void DrawTrail(double delta)
    {
        if (_trailBody is null || _trailRoot is null || !IsInstanceValid(_trailBody) || !IsInstanceValid(_trailRoot) || _trailLanded) return;
        var at = _trailBody.GlobalPosition;
        if (at.DistanceTo(_trailLastDot) >= TrailSpacing && _trailRoot.GetChildCount() < TrailMaxDots)
        {
            var dot = Shapes.Sphere(0.06f, Shapes.Mat(new Color(0.97f, 0.97f, 0.92f)));
            dot.Position = at;
            dot.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            dot.SetMeta(KeepClearMeta, true);   // a label steps off the flight rather than writing over it (Main.Labels.cs)
            _trailRoot.AddChild(dot);
            _trailLastDot = at;
        }
        float across = new Vector2(at.X - _trailStart.X, at.Z - _trailStart.Z).Length();
        // the first touchdown: back near its starting height after rising well above it. That is the throw's range;
        // anything after is bouncing, rolling or sliding along the ground
        _trailHighest = Mathf.Max(_trailHighest, at.Y - _trailStart.Y);
        if (_trailTouchdown is null && _trailHighest > 0.5f && at.Y - _trailStart.Y < 0.15f && across > 1)
        {
            _trailTouchdown = at;
            var mark = Shapes.Cylinder(0.3f, 0.02f, Shapes.Mat(new Color(0.95f, 0.75f, 0.2f)));
            mark.Position = at with { Y = at.Y - 0.05f };
            mark.SetMeta(KeepClearMeta, true);
            _trailRoot.AddChild(mark);
        }
        // at rest: it has gone a fair way and has been nearly still for half a second
        _trailResting = _trailBody.LinearVelocity.Length() < 0.2f ? _trailResting + delta : 0;
        if (across < 2 || _trailResting < 0.5) return;
        _trailLanded = true;
        var ring = Shapes.Cylinder(0.45f, 0.02f, Shapes.Mat(new Color(0.95f, 0.3f, 0.15f)));
        ring.Position = at with { Y = at.Y - 0.05f };
        ring.SetMeta(KeepClearMeta, true);
        _landedAtMs = Time.GetTicksMsec();
        _trailRoot.AddChild(ring);
        _trailRoot.AddChild(new Label3D
        {
            Text = _trailTouchdown is { } t && across - Flat(t) > 0.5f ? $"flew {Flat(t):0.0} m\nrolled to {across:0.0} m" : $"{across:0.0} m",
            Position = at + new Vector3(0, 0.9f, 0),
            FontSize = 32,
            OutlineSize = 8,
            PixelSize = 0.0012f,
            FixedSize = true,   // the same size on screen however far the stone went
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
        });
        GD.Print($"[trail] {_trailBody.Name} first touched down {(_trailTouchdown is { } d ? Flat(d) : across):0.00} m out and came to rest {across:0.00} m from where it started"
                 + $" (from ({_trailStart.X:F1} {_trailStart.Z:F1}) to ({at.X:F1} {at.Z:F1}) in x z)");
    }

    private float Flat(Vector3 p) => new Vector2(p.X - _trailStart.X, p.Z - _trailStart.Z).Length();

    // ---- the camera on a throw ----------------------------------------------------------------------------------

    private Vector3 _missileSeat;      // where the stone or bolt lay before this throw
    private bool _missileReloaded;     // it has been laid back in its sling or on its string since the last throw landed
    private ulong _landedAtMs;         // when the last throw came to rest (real time), 0 while it is still going

    /// <summary>How long the camera stays on a throw's landing before going back to the machine, real seconds.</summary>
    private const float LandingHold = 2.5f;

    /// <summary>
    /// Once a thrown stone or bolt (or a rising lantern) is clear of the machine, eases the camera to look between the
    /// two and pulls back far enough to keep both in view, so the flight and the landing are on screen. When it has come
    /// to rest the camera holds on the landing for a moment and then goes back to the machine, which re-spans and reloads
    /// (#161); the next throw is followed again, with a fresh trail (#190).
    /// </summary>
    private void FollowMissile()
    {
        if (_follow is null || !IsInstanceValid(_follow)) return;
        var target = _follow.GlobalPosition;
        // how far it has gone from where it lay: across the ground (a thrown stone) or up (a lantern rising)
        float across = new Vector2(target.X - _missileSeat.X, target.Z - _missileSeat.Z).Length(), up = target.Y - _missileSeat.Y;
        float separation = Mathf.Max(across, up);
        bool atSeat = separation < 1f;
        if (atSeat && _trailLanded) _missileReloaded = true;
        if (!atSeat && _missileReloaded && _current is not null)
        {
            var seat = _missileSeat;
            StartTrail(_current, _follow);   // the next throw: a new trail from the seat
            _trailStart = _trailLastDot = _missileSeat = seat;
        }
        bool home = atSeat || (_trailLanded && _landedAtMs > 0 && (Time.GetTicksMsec() - _landedAtMs) / 1000f > LandingHold);
        Vector3 wantPivot;
        float wantDistance;
        if (home)
        {
            wantPivot = _homePivot;
            wantDistance = _homeDistance;
        }
        else
        {
            // the machine and the missile both in the clear area between the panels: the pivot half way between them, then
            // shifted so that half way lands at the clear area's centre, and far enough back that the whole span fits its
            // width (a throw across the screen) or the window's height (a lantern going up)
            var vp = GetViewport().GetVisibleRect().Size;
            var clear = SettledClearArea();
            float tanHalf = Mathf.Tan(Mathf.DegToRad(_camera.Fov / 2));
            float halfSpan = separation * 0.6f + 2f;   // with a margin: the camera eases after it, and a stone rolls on
            float distance = up > across
                ? halfSpan * 1.25f / tanHalf
                : halfSpan / (tanHalf * vp.X / vp.Y * clear.Size.X / vp.X);
            wantDistance = Mathf.Max(_homeDistance, distance);
            var mid = (_missileSeat + target) / 2;
            float perPixel = 2 * wantDistance * tanHalf / vp.Y;
            wantPivot = mid with { Y = Mathf.Max(mid.Y, _homePivot.Y) } + _camera.GlobalBasis.X * (vp.X / 2 - clear.GetCenter().X) * perPixel;
        }
        if (_orbit.Pivot.DistanceTo(wantPivot) < 0.005f && Mathf.Abs(_orbit.Distance - wantDistance) < 0.005f) return;
        _orbit.Pivot = _orbit.Pivot.Lerp(wantPivot, 0.06f);
        _orbit.Distance = Mathf.Lerp(_orbit.Distance, wantDistance, 0.06f);
        _orbit.Apply();
    }
}
