using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Two hands-on actions that are neither a field toggle nor a plain drag (#162), both in a machine run:
///
///  - Aim a mirror. Each mirror shows the spot its light lands on (pale on its receiver, red off it). Drag the spot and
///    the mirror is aimed where it is let go: three logged actions, <c>mirror aim-dx/aim-dy/aim-dz</c> (offsets from the mirror). (Shift drags it
///    up and down instead of across.) Alt+click a mirror to pick it, then Alt+click any part of the machine to aim it
///    at that part (<c>mirror aim-part N</c>, N the part's place in the machine), or the ground to aim at that point.
///    Tracking is the mirror's <c>track</c> field (Shift+click on it), which holds the plate where it stands when off.
///  - Pick where a digger works. A click on bare ground, in a world with a map, sends the nearest digging gang
///    there: <c>digger site-x</c> and <c>site-z</c>.
///
/// Everything goes through <see cref="Operate"/>, so it is logged and a demo operator hands over, like a click.
/// </summary>
public partial class Main
{
    private const float SpotGrab = 20;   // pixels from a mirror's spot at which a press takes it

    private sealed class SpotDrag
    {
        public required MachineView View;
        public required string MirrorId;
        public required Mirror Mirror;
        public Vector3 Aim;
        public bool Vertical, Moved;
        public Vector2 PressedAt;
    }

    private SpotDrag? _spotDrag;
    private (MachineView View, string Id)? _aimMirror;
    private Vector2 _groundPress;
    private bool _groundPressing;

    /// <summary>Called first from _UnhandledInput: true when the event belongs to aiming and nothing else must act on it.</summary>
    private bool AimInput(InputEvent e)
    {
        if (_current is null || !IsInstanceValid(_current)) return false;
        if (_spotDrag is { } d)
        {
            switch (e)
            {
                case InputEventMouseMotion m:
                    if (m.Position.DistanceTo(d.PressedAt) >= 4) d.Moved = true;
                    d.Vertical = m.ShiftPressed;
                    MoveSpot(d, m.Position);
                    return true;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                    FinishSpot(d);
                    return true;
            }
            return true;
        }
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb || GetViewport().GuiGetHoveredControl() is not null) return false;
        if (mb.Pressed)
        {
            if (mb.AltPressed) return true;   // an Alt+click is aiming's, whole: the press and its release (nothing else may click, orbit or drag)
            if (SpotAt(mb.Position) is { } hit)
            {
                _aimMirror = (_current, hit.Id);
                _spotDrag = new SpotDrag { View = _current, MirrorId = hit.Id, Mirror = hit.Mirror, Aim = hit.Spot, PressedAt = mb.Position };
                return true;
            }
            _groundPress = mb.Position;
            _groundPressing = true;
            return false;
        }
        if (mb.AltPressed) { AltClick(mb.Position); return true; }
        if (_groundPressing && mb.Position.DistanceTo(_groundPress) < ClickSlop) GroundClick(mb.Position);
        _groundPressing = false;
        return false;
    }

    private (string Id, Mirror Mirror, Vector3 Spot)? SpotAt(Vector2 screen)
    {
        (string, Mirror, Vector3)? best = null;
        float bestPx = SpotGrab;
        foreach (var (id, mirror, spot) in _current!.MirrorSpots())
        {
            if (_camera.IsPositionBehind(spot)) continue;
            float px = _camera.UnprojectPosition(spot).DistanceTo(screen);
            if (px < bestPx) { bestPx = px; best = (id, mirror, spot); }
        }
        return best;
    }

    /// <summary>Where the cursor's ray meets the plane the spot is dragged on: level with it, or (Shift) facing the camera.</summary>
    private Vector3? SpotPlanePoint(SpotDrag d, Vector2 screen)
    {
        var from = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        var plane = d.Vertical ? new Plane(-_camera.GlobalBasis.Z, d.Aim) : new Plane(Vector3.Up, d.Aim);
        return plane.IntersectsRay(from, dir);
    }

    private void MoveSpot(SpotDrag d, Vector2 screen)
    {
        if (SpotPlanePoint(d, screen) is not { } p) return;
        d.Aim = p;
        // the spot follows the hand; the mirror is aimed where it is let go (one logged action, not a hundred)
        d.View.ShowSpotAt(d.Mirror, p);
    }

    private void FinishSpot(SpotDrag d)
    {
        _spotDrag = null;
        if (!d.Moved)
        {
            // a press and release in place is a click on whatever is drawn there (the spot sits on its receiver, whose controls stay reachable)
            d.View.ShowSpotAt(d.Mirror, null);
            ClickAt(d.PressedAt, false);
            return;
        }
        AimMirrorAt(d.View, d.MirrorId, d.Aim);
    }

    /// <summary>Aims a mirror at a world point: three logged actions. The pointer's own place is the sim's (the view and sim share a frame).</summary>
    private void AimMirrorAt(MachineView view, string mirror, Vector3 p)
    {
        var at = view.Runtime.Mirrors[mirror].At;   // as offsets from the mirror, so the log holds in a machine moved about in a world
        Operate(view, mirror, "aim-dx", p.X - at.X);
        Operate(view, mirror, "aim-dy", p.Y - at.Y);
        Operate(view, mirror, "aim-dz", p.Z - at.Z);
        view.ShowSpotAt(view.Runtime.Mirrors[mirror], null);
        Toast($"{mirror}: aimed at ({p.X:F2} {p.Y:F2} {p.Z:F2})");
    }

    private void AimMirrorAtPart(MachineView view, string mirror, string partId)
    {
        int index = view.Runtime.Def.Parts.ToList().FindIndex(x => x.Id == partId);
        if (index < 0) { Toast($"{partId} is not a part a mirror can be aimed at"); return; }
        Operate(view, mirror, "aim-part", index);
        Toast($"{mirror}: aimed at {partId}");
    }

    /// <summary>Alt+click: on a mirror picks it for aiming; on any other part aims the picked mirror at it; on the ground, at that point.</summary>
    private void AltClick(Vector2 screen)
    {
        if (OperablePartAt(screen) is { } part)
        {
            if (part.View.Runtime.Mirrors.ContainsKey(part.Id)) { _aimMirror = part; Toast($"{part.Id} picked: Alt+click a part or the ground to aim it"); }
            else if (_aimMirror is { } m && m.View == part.View) AimMirrorAtPart(m.View, m.Id, part.Id);
            else Toast("Alt+click a mirror first, then what it should light");
        }
        else if (_aimMirror is { } m2 && GroundPoint(screen) is { } g) AimMirrorAt(m2.View, m2.Id, g);
    }

    /// <summary>The ground under a pixel: the first thing the ray meets that no part owns, null if it meets nothing.</summary>
    private Vector3? GroundPoint(Vector2 screen)
    {
        var from = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        var space = _camera.GetWorld3D().DirectSpaceState;
        var skip = new Godot.Collections.Array<Rid>();
        for (int tries = 0; tries < 32; tries++)
        {
            var hit = space.IntersectRay(new PhysicsRayQueryParameters3D { From = from, To = from + dir * 2000f, Exclude = skip });
            if (hit.Count == 0) return null;
            var body = (CollisionObject3D)hit["collider"].AsGodotObject();
            if (PartOf(body) is null) return hit["position"].AsVector3();
            skip.Add(body.GetRid());
        }
        return null;
    }

    /// <summary>A click on bare ground in a world with a map: the nearest digging gang goes to work there.</summary>
    private void GroundClick(Vector2 screen)
    {
        if (_groundSim is null || _current!.Runtime.Diggers.Count == 0 || PartAt(screen) is not null) return;
        if (GroundPoint(screen) is not { } p) return;
        SendDigger(p);
    }

    private void SendDigger(Vector3 p)
    {
        var diggers = _current!.Runtime.Diggers;
        string id = diggers.OrderBy(kv => Math.Pow(kv.Value.X0 - p.X, 2) + Math.Pow(kv.Value.Z0 - p.Z, 2)).First().Key;
        Operate(_current, id, "site-x", p.X);
        Operate(_current, id, "site-z", p.Z);
        Toast($"{id}: now digging at ({p.X:F1} {p.Z:F1})");
    }

    /// <summary>Scripted steps: "spots" prints each mirror's spot and where it is drawn; "dragspot ID X Y Z" drags a spot to a world point
    /// (the real press, motion and release); "groundpx X Z Y" prints the window pixel of a ground point; "aimpart MIRROR PART" and "senddigger X Z" are the Alt+click and ground click.</summary>
    private ScriptedInput.Step? AimStep(string[] w)
    {
        if (_current is null) return null;
        switch (w[0])
        {
            case "spots":
                foreach (var (id, m, spot) in _current.MirrorSpots())
                    GD.Print($"[aim] {id} spot ({spot.X:F2} {spot.Y:F2} {spot.Z:F2}) at pixel {_camera.UnprojectPosition(spot)}, hits receiver {m.OnReceiver}, power {m.Delivered:F1} W, track {m.Track}");
                return ScriptedInput.Step.Continue;
            case "dragspot":
            {
                var from = _current.MirrorSpots().First(s => s.Id == w[1]).Spot;
                var to = new Vector3(float.Parse(w[2]), float.Parse(w[3]), float.Parse(w[4]));
                // the cursor's path: the pixel the spot is drawn at, to the pixel the target is drawn at (the plane level with the spot, so keep its height)
                var a = _camera.UnprojectPosition(from);
                var b = _camera.UnprojectPosition(new Vector3(to.X, from.Y, to.Z));
                SyntheticMouse(a, true); for (int k = 1; k <= 10; k++) SyntheticMouse(a.Lerp(b, k / 10f), null); SyntheticMouse(b, false);
                if (Math.Abs(to.Y - from.Y) > 1e-6f) Operate(_current, w[1], "aim-dy", to.Y - _current.Runtime.Mirrors[w[1]].At.Y);   // height is the Shift drag's
                return ScriptedInput.Step.Next;
            }
            case "groundpx":   // the window pixel a ground point at height Y is drawn at, for a real "down"/"up" there
            {
                var px = _camera.UnprojectPosition(new Vector3(float.Parse(w[1]), float.Parse(w[3]), float.Parse(w[2]))) * (GetWindow().Size.X / GetViewport().GetVisibleRect().Size.X);
                GD.Print($"[aim] ground ({w[1]} {w[2]}) at window pixel {px.X:0} {px.Y:0}");
                return ScriptedInput.Step.Continue;
            }
            case "aimpart":
                AimMirrorAtPart(_current, w[1], w[2]);
                return ScriptedInput.Step.Continue;
            case "senddigger":
                SendDigger(new Vector3(float.Parse(w[1]), 0, float.Parse(w[2])));
                return ScriptedInput.Step.Continue;
        }
        return null;
    }

    private void SyntheticMouse(Vector2 viewportAt, bool? button)
    {
        // injected events are in window pixels; the viewport (the canvas the camera projects into) may be scaled to the window
        var at = viewportAt * (GetWindow().Size.X / GetViewport().GetVisibleRect().Size.X);
        Input.ParseInputEvent(button is null
            ? new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = Vector2.Zero }
            : new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = button.Value, Position = at, GlobalPosition = at });
    }
}
