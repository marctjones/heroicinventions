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
        _trailRoot = new Node3D { Name = "Trail" };
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
            _trailRoot.AddChild(mark);
        }
        // at rest: it has gone a fair way and has been nearly still for half a second
        _trailResting = _trailBody.LinearVelocity.Length() < 0.2f ? _trailResting + delta : 0;
        if (across < 2 || _trailResting < 0.5) return;
        _trailLanded = true;
        var ring = Shapes.Cylinder(0.45f, 0.02f, Shapes.Mat(new Color(0.95f, 0.3f, 0.15f)));
        ring.Position = at with { Y = at.Y - 0.05f };
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
        GD.Print($"[trail] {_trailBody.Name} first touched down {(_trailTouchdown is { } d ? Flat(d) : across):0.00} m out and came to rest {across:0.00} m from where it started");
    }

    private float Flat(Vector3 p) => new Vector2(p.X - _trailStart.X, p.Z - _trailStart.Z).Length();
}
