using System.Globalization;
using Godot;

namespace HeroicInventions;

/// <summary>
/// A drag, as something that can be done again (#159): the part grabbed, the grabbed point in that part's own frame,
/// and the hand's target at keyframed sim times, linear between them and held after the last. The mouse makes one of
/// these as it goes; HEROIC_DRAG makes the same thing from text; both are applied through the same spring
/// (MachineView.Hand.cs), so a replay does what the recording did.
/// </summary>
public sealed class DragRecord
{
    public string PartId = "";
    public Vector3 GrabLocal;
    public double GrabAt;
    public double? ReleaseAt;
    public readonly List<(double Time, Vector3 Target)> Keys = [];

    /// <summary>Where the hand is at a time: along the straight line between the keys either side, at the last key (or before the first) outside them. Of keys at one time the later wins.</summary>
    public Vector3 TargetAt(double time)
    {
        if (Keys.Count == 0) return Vector3.Zero;
        int i = Keys.FindLastIndex(k => k.Time <= time);
        if (i < 0) return Keys[0].Target;
        if (i == Keys.Count - 1) return Keys[i].Target;
        var (t0, p0) = Keys[i];
        var (t1, p1) = Keys[i + 1];
        return p0.Lerp(p1, (float)((time - t0) / (t1 - t0)));
    }

    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string V(Vector3 v) => $"{N(v.X)} {N(v.Y)} {N(v.Z)}";

    /// <summary>The HEROIC_DRAG text that replays it. (The first key, where the grabbed point is when it is grabbed, is not written: replay finds it.)</summary>
    public string ToReplay()
    {
        var parts = new List<string> { $"drag {PartId} {V(GrabLocal)} at {N(GrabAt)}" };
        parts.AddRange(Keys.Skip(1).Select(k => $"to {V(k.Target)} at {N(k.Time)}"));
        parts.Add($"release at {N(ReleaseAt ?? Keys[^1].Time)}");
        return string.Join("; ", parts);
    }
}

/// <summary>
/// Where a person's actions on a machine are noted. The operator log (#153) is meant to be the one place that does
/// this; until it merges, a drag and a hook are printed as the lines that replay them.
/// TODO(#153): replace PrintedHandActions with a sink that calls the operator log's entry point.
/// </summary>
public interface IHandActions
{
    void RecordDrag(MachineView view, DragRecord drag);
    void RecordHook(MachineView view, string rope, string action, string? load, Vector3 point);
}

public sealed class PrintedHandActions : IHandActions
{
    public void RecordDrag(MachineView view, DragRecord drag) => GD.Print($"[hand] HEROIC_DRAG=\"{drag.ToReplay()}\"");
    public void RecordHook(MachineView view, string rope, string action, string? load, Vector3 point) =>
        GD.Print($"[hand] {rope} {action}{(load is null ? "" : $" {load}")} at {point.X:F3} {point.Y:F3} {point.Z:F3}");
}

/// <summary>
/// Dragging bodies with the mouse (#159). A press on a dynamic body takes hold of the point under the cursor; the
/// point is pulled toward the cursor's place on a plane facing the camera (shift: a vertical plane; the wheel:
/// nearer or farther) by the hand's spring, drawn as a line with its force in N. Release lets the body go with the
/// velocity it has. A press on empty space or on a part that doesn't move still orbits the camera.
/// </summary>
public partial class Main
{
    /// <summary>
    /// Whether a person may take hold of this body. A machine run lets them take any; the game (#163) will filter to
    /// what the rover could reach by replacing this.
    /// </summary>
    public Func<MachineView, string, RigidBody3D, bool> CanGrab { get; set; } = (_, _, _) => true;

    /// <summary>Where grabs and hooks are noted (see <see cref="IHandActions"/>).</summary>
    public IHandActions HandActions { get; set; } = new PrintedHandActions();

    private sealed class ActiveHand
    {
        public required MachineView View;
        public required HandSpring Spring;
        public required DragRecord Record;
        public bool Interactive;
        public Vector2 Screen, PressedAt;
        public bool Moved, Vertical, First = true;
        public Vector3 PlanePoint;
    }

    private ActiveHand? _hand;
    private readonly List<DragRecord> _pendingDrags = [];
    private MeshInstance3D? _handLine;
    private MeshInstance3D? _handTarget;
    private Label3D? _handLabel;

    // ------------------------------------------------------------------ the mouse

    /// <summary>Called first by _UnhandledInput: true when the event belongs to a drag and the camera must not also act on it.</summary>
    private bool HandleHandInput(InputEvent @event)
    {
        if (_hand is { Interactive: true } h)
        {
            switch (@event)
            {
                case InputEventMouseMotion motion:
                    h.Screen = motion.Position;
                    if (motion.Position.DistanceTo(h.PressedAt) >= 4) h.Moved = true;
                    if (motion.ShiftPressed != h.Vertical) SwitchPlane(h, motion.ShiftPressed);
                    return true;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                    bool moved = h.Moved;
                    EndHand();
                    return moved;   // a press and release in place is still a click on the machine
                case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                    // nearer or farther: the plane the cursor moves on slides along the view
                    h.PlanePoint += ViewForward() * (wheel.ButtonIndex == MouseButton.WheelUp ? -0.1f : 0.1f) * Mathf.Max(1f, _orbit.Distance * 0.1f);
                    return true;
                case InputEventPanGesture or InputEventMagnifyGesture:
                    return true;
            }
            return false;
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press && !_joining && _hand is null
            && DraggableAt(press.Position) is { } found
            && CanGrab(found.View, found.PartId, found.Body))
        {
            _pressAt = press.Position;
            BeginInteractiveHand(found, press);
            return true;
        }
        return false;
    }

    private Vector3 ViewForward() => -_camera.GlobalBasis.Z;

    /// <summary>The plane the cursor moves on: facing the camera, or (shift) standing upright, facing the camera's heading.</summary>
    private Vector3 PlaneNormal(bool vertical)
    {
        var forward = ViewForward();
        if (!vertical) return forward;
        var flat = new Vector3(forward.X, 0, forward.Z);
        return flat.LengthSquared() > 1e-4f ? flat.Normalized() : forward;
    }

    private void SwitchPlane(ActiveHand h, bool vertical)
    {
        h.Vertical = vertical;
        h.PlanePoint = h.Spring.Target;   // the new plane passes through where the hand is now: no jump
    }

    /// <summary>Where the cursor's ray meets the hand's plane, or null if it is parallel to it or points away.</summary>
    private Vector3? CursorOnPlane(ActiveHand h)
    {
        var origin = _camera.ProjectRayOrigin(h.Screen);
        var dir = _camera.ProjectRayNormal(h.Screen);
        var normal = PlaneNormal(h.Vertical);
        float denom = dir.Dot(normal);
        if (Mathf.Abs(denom) < 1e-4f) return null;
        float t = (h.PlanePoint - origin).Dot(normal) / denom;
        return t > 0 ? origin + dir * t : null;
    }

    /// <summary>
    /// The body under the cursor that a hand can take hold of, and the point of it under the cursor. The nearest thing
    /// the ray meets decides: a dynamic body is grabbed, a part that doesn't move is not (the camera orbits instead);
    /// the ground and the scale figure belong to no part and are looked through.
    /// </summary>
    private (MachineView View, string PartId, RigidBody3D Body, Vector3 Point)? DraggableAt(Vector2 screen)
    {
        var from = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        var space = _camera.GetWorld3D().DirectSpaceState;
        var skip = new Godot.Collections.Array<Rid>();
        for (int tries = 0; tries < 32; tries++)
        {
            var hit = space.IntersectRay(new PhysicsRayQueryParameters3D { From = from, To = from + dir * 2000f, Exclude = skip });
            if (hit.Count == 0) return null;
            var collider = (CollisionObject3D)hit["collider"].AsGodotObject();
            if (PartOf(collider) is not { } part) { skip.Add(collider.GetRid()); continue; }
            if (collider is not RigidBody3D body || body.Freeze) return null;
            return (part.View, part.PartId, body, hit["position"].AsVector3());
        }
        return null;
    }

    private void BeginInteractiveHand((MachineView View, string PartId, RigidBody3D Body, Vector3 Point) found, InputEventMouseButton press)
    {
        double now = found.View.Runtime.Time;
        var record = new DragRecord { PartId = found.PartId, GrabLocal = found.Body.ToLocal(found.Point), GrabAt = now };
        record.Keys.Add((now, found.Point));
        var spring = new HandSpring { Body = found.Body, GrabLocal = record.GrabLocal, Target = found.Point };
        _hand = new ActiveHand
        {
            View = found.View, Spring = spring, Record = record, Interactive = true,
            Screen = press.Position, PressedAt = press.Position, Vertical = press.ShiftPressed, PlanePoint = found.Point,
        };
        found.View.Hand = spring;
    }

    // ------------------------------------------------------------------ replay (HEROIC_DRAG)

    /// <summary>
    /// HEROIC_DRAG="drag PART lx ly lz at T; to x y z at T; ...; release at T; ...": what a person's hand does, as text.
    /// "drag" takes hold of PART at the point (lx ly lz) of its own frame at sim time T; each "to" is where the hand is at
    /// a time (it moves in a straight line between them); "release" lets go. A rope's free hook is PART "ROPE.hook".
    /// </summary>
    private void ParseHeroicDrag(string text)
    {
        var inv = CultureInfo.InvariantCulture;
        DragRecord? open = null;
        foreach (var item in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var w = item.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool Num(int i, out float v) { v = 0; return i < w.Length && float.TryParse(w[i], NumberStyles.Float, inv, out v); }
            bool ok = false;
            if (w is ["drag", ..] && w.Length == 7 && w[5] == "at" && Num(2, out float lx) && Num(3, out float ly) && Num(4, out float lz) && double.TryParse(w[6], inv, out double t0))
            {
                if (open is not null) { SettingFailed($"HEROIC_DRAG: '{item}' begins a drag before the last one is released"); return; }
                open = new DragRecord { PartId = w[1], GrabLocal = new Vector3(lx, ly, lz), GrabAt = t0 };
                ok = true;
            }
            else if (w is ["to", ..] && w.Length == 6 && w[4] == "at" && open is not null && Num(1, out float x) && Num(2, out float y) && Num(3, out float z) && double.TryParse(w[5], inv, out double t))
            {
                open.Keys.Add((t, new Vector3(x, y, z)));
                ok = true;
            }
            else if (w is ["release", "at", ..] && w.Length == 3 && open is not null && double.TryParse(w[2], inv, out double tr))
            {
                open.ReleaseAt = tr;
                _pendingDrags.Add(open);
                open = null;
                ok = true;
            }
            if (!ok) { SettingFailed($"HEROIC_DRAG: expected 'drag PART lx ly lz at T', 'to x y z at T' or 'release at T', got '{item}'"); return; }
        }
        if (open is not null) SettingFailed("HEROIC_DRAG: the last drag is never released");
    }

    private MachineView? ViewHolding(string partId)
    {
        IEnumerable<MachineView> views = _views.Count > 0 ? _views : _current is not null ? [_current] : [];
        return views.FirstOrDefault(v => v.HandBody(partId) is not null);
    }

    private void ReportUnappliedDrags()
    {
        foreach (var d in _pendingDrags) SettingFailed($"HEROIC_DRAG: '{d.PartId}' at {d.GrabAt} was never grabbed: the run ended first or there is no such body");
        if (_hand is { Interactive: false }) SettingFailed($"HEROIC_DRAG: '{_hand.Record.PartId}' was never released: the run ended first");
        _pendingDrags.Clear();
    }

    // ------------------------------------------------------------------ each tick

    /// <summary>Before the step: a replayed drag takes hold or lets go when its time comes, and the hand's target is set for this tick.</summary>
    private void PreStepHand()
    {
        double dt = GetPhysicsProcessDeltaTime();
        if (_hand is null && _pendingDrags.Count > 0)
        {
            var rec = _pendingDrags[0];
            var view = ViewHolding(rec.PartId) ?? _current;
            if (view is not null && view.Runtime.Time + 1e-9 >= rec.GrabAt)
            {
                _pendingDrags.RemoveAt(0);
                var body = view.HandBody(rec.PartId);
                if (body is null || body.Freeze || !CanGrab(view, rec.PartId, body))
                    SettingFailed($"HEROIC_DRAG: cannot take hold of '{rec.PartId}' at {rec.GrabAt}");
                else
                {
                    var spring = new HandSpring { Body = body, GrabLocal = rec.GrabLocal };
                    spring.Target = spring.GrabWorld;
                    rec.Keys.Insert(0, (rec.GrabAt, spring.GrabWorld));   // the hand starts where it takes hold
                    _hand = new ActiveHand { View = view, Spring = spring, Record = rec };
                    view.Hand = spring;
                }
            }
        }
        if (_hand is { } gone && !IsInstanceValid(gone.Spring.Body)) { gone.View.Hand = null; _hand = null; }   // hooked away from under the hand
        if (_hand is not { } h) return;
        double now = h.View.Runtime.Time;
        Vector3 target;
        if (h.Interactive)
        {
            target = CursorOnPlane(h) ?? h.Spring.Target;
            if (target.DistanceTo(h.Spring.Target) > 0.001f)
            {
                // the hand stood still until now: say so, or a replay would drift toward this key from the last one
                if (h.Record.Keys[^1].Time < now - dt * 1.5) h.Record.Keys.Add((now - dt, h.Spring.Target));
                h.Record.Keys.Add((now, target));
            }
        }
        else
        {
            if (now + 1e-9 >= (h.Record.ReleaseAt ?? now)) { EndHand(); return; }
            target = h.Record.TargetAt(now);
        }
        var velocity = h.First ? Vector3.Zero : (target - h.Spring.Target) / (float)dt;
        h.Spring.TargetVelocity = h.Spring.TargetVelocity.Lerp(velocity, 0.3f);   // smoothed the same way in a replay as in the live drag, so they agree
        h.Spring.Target = target;
        h.First = false;
    }

    /// <summary>After the step: the spring and its force on the screen.</summary>
    private void PostStepHand()
    {
        if (_hand is not { } h) { if (_handLine is not null) { _handLine.Visible = false; _handTarget!.Visible = false; _handLabel!.Visible = false; } return; }
        EnsureHandNodes();
        var a = h.Spring.GrabWorld;
        var b = h.Spring.Target;
        var d = b - a;
        float len = Mathf.Max(d.Length(), 1e-4f);
        var up = d / len;
        var axis = Vector3.Up.Cross(up);
        var basis = axis.LengthSquared() > 1e-8f ? new Basis(axis.Normalized(), Vector3.Up.AngleTo(up))
                  : up.Y < 0 ? new Basis(Vector3.Right, Mathf.Pi) : Basis.Identity;
        float thick = Mathf.Max(0.004f, _orbit.Distance * 0.002f);
        _handLine!.Transform = new Transform3D(basis * Basis.FromScale(new Vector3(thick, len, thick)), (a + b) / 2);
        _handTarget!.Position = b;
        _handTarget.Scale = Vector3.One * thick * 3;
        _handLabel!.Position = (a + b) / 2 + Vector3.Up * (0.08f + thick * 6);
        _handLabel.Text = $"{h.Spring.Force:F0} N";
        _handLine.Visible = _handTarget.Visible = _handLabel.Visible = true;
    }

    private void EnsureHandNodes()
    {
        if (_handLine is not null) return;
        var look = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.75f, 0.1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        _handLine = new MeshInstance3D { Name = "HandSpring", Mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1, RadialSegments = 6 }, MaterialOverride = look };
        _handTarget = new MeshInstance3D { Name = "HandTarget", Mesh = new SphereMesh { Radius = 0.5f, Height = 1, RadialSegments = 8, Rings = 4 }, MaterialOverride = look };
        _handLabel = new Label3D
        {
            Name = "HandForce", FontSize = 24, OutlineSize = 6, PixelSize = 0.006f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, Modulate = new Color(1f, 0.85f, 0.3f),
        };
        AddChild(_handLine);
        AddChild(_handTarget);
        AddChild(_handLabel);
    }

    private void EndHand()
    {
        if (_hand is not { } h) return;
        _hand = null;
        h.View.Hand = null;
        double now = h.View.Runtime.Time;
        h.Record.ReleaseAt = now;
        if (h.Interactive)
        {
            if (h.Record.Keys[^1].Time < now) h.Record.Keys.Add((now, h.Spring.Target));
            HandActions.RecordDrag(h.View, h.Record);
        }
        // a hook let go near a load is hooked to it (#160)
        if (IsInstanceValid(h.Spring.Body)) h.View.DropHook(h.Spring.Body);
    }
}
