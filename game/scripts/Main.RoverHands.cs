using System.Globalization;
using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The rover's hands (issue #163). In a world that places a rover, every click, drag and hook is the rover's own and passes one
/// capability check, the same click, drag and hook paths a machine run uses, filtered by what the rover could do (docs/lonely-rover.html:
/// it moves loads sideways or down, never up, can't turn a treadwheel or push a capstan, #72). A machine run has no rover, installs
/// nothing here, and keeps the free operator.
/// <list type="bullet">
/// <item><b>Reach</b>: <see cref="Rover.ArmReach"/> (1.8 m: boom 0.8 + stick 0.7 + bucket 0.3) from the arm's pivot on the rover's deck.
/// A part (or the point of a body a hand takes hold of) farther than that is refused with "Out of reach: drive N m closer", N the
/// shortfall; a hand dragged beyond it stops at it.</item>
/// <item><b>Force</b>: <see cref="RoverSpec.PushForce"/>, the lesser of the wheels' measured pull (907 N) and the tyres' grip μ m g (685 N on
/// Mars). A body that takes more than that to slide (μ m g with its own friction, 0.6 if it has none) is refused:
/// "Too heavy for the rover's arm: 21.6 t". A hand pulls with no more than that, so a load held by a heavier one stalls.</item>
/// <item><b>Direction</b>: the hand never goes more than <see cref="RoverSpec.LiftSlack"/> (5 cm) above where it took hold: "The rover
/// can't lift loads". Sideways and down are free.</item>
/// <item><b>Controls</b>: a valve, gate, door, latch, catch, tap or switch within reach; not the ones that are a person's own effort
/// (<see cref="Controls.Control.NotRover"/>), and not any field the controls table doesn't name.</item>
/// </list>
/// Allowed actions run through the same <see cref="Operate"/> and hand paths, so they are logged and replayable as before. A refusal
/// is printed as "[rover] refused: WHY", shown in the rover's panel, and logs nothing.
/// </summary>
public partial class Main
{
    private string? _roverSaid;
    private double _roverSaidAt = -100;

    private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    private static double Clock => Time.GetTicksMsec() / 1000.0;

    /// <summary>The rover can't do this: say why, once on the console and in the panel, until the next thing it says.</summary>
    private void RoverRefuse(string why)
    {
        _roverSaid = why;
        _roverSaidAt = Clock;
        GD.Print($"[rover] refused: {why}");
    }

    private void InstallRoverHands()
    {
        Controls.ByRover = true;
        Controls.Refusal = RoverControlRefusal;
        GrabRefusal = RoverGrabRefusal;
    }

    private void RemoveRoverHands()
    {
        Controls.ByRover = false;
        Controls.Refusal = (_, _, _) => null;
        GrabRefusal = (_, _, _, _) => null;
        _roverSaid = null;
    }

    /// <summary>The newest thing the rover said it can't do, while it is fresh (6 s), else null.</summary>
    private string? RoverSaid => _roverSaid is not null && Clock - _roverSaidAt < 6 ? _roverSaid : null;

    /// <summary>The focus follows the rover's hand: the machine it touches becomes the current one (its log, its panels), without moving the camera.</summary>
    private void RoverFocus(MachineView view)
    {
        if (!_viewMachine.TryGetValue(view, out string? name)) return;
        _current = view;
        _currentName = name;
    }

    // ------------------------------------------------------------------ reach

    private readonly Dictionary<(MachineView, string), (ulong Frame, Aabb? Box)> _partBoxes = [];

    /// <summary>The world box round a part's solid meshes (its glass and water are left out when it has solid ones), or null for a part with none drawn.</summary>
    private Aabb? PartBox(MachineView view, string id)
    {
        ulong frame = Engine.GetPhysicsFrames();
        if (_partBoxes.TryGetValue((view, id), out var cached) && cached.Frame == frame) return cached.Box;
        var meshes = view.Meshes().Where(m => m.IsVisibleInTree() && m.Mesh is not null && PartOf(m) is { } o && o.PartId == id).ToList();
        var chosen = meshes.Any(MachineView.IsOpaque) ? meshes.Where(MachineView.IsOpaque).ToList() : meshes;
        Aabb? box = null;
        foreach (var m in chosen)
        {
            var b = m.GlobalTransform * m.Mesh!.GetAabb();
            box = box is { } have ? have.Merge(b) : b;
        }
        _partBoxes[(view, id)] = (frame, box);
        return box;
    }

    /// <summary>How far the box is from the arm's pivot (the nearest point of it), m.</summary>
    private float ReachTo(Aabb box)
    {
        var p = _rover!.ArmBase;
        return p.DistanceTo(box.Position.Max(p.Min(box.End)));
    }

    private string? OutOfReach(float distance) =>
        distance > Rover.ArmReach + 1e-3f ? $"Out of reach: drive {F1(distance - Rover.ArmReach)} m closer" : null;

    private string? ReachRefusal(Aabb? box) => box is { } b ? OutOfReach(ReachTo(b)) : null;

    // ------------------------------------------------------------------ controls

    /// <summary>The capability check of a control (installed as <see cref="Controls.Refusal"/>): what the rover can work at all, then whether it can reach it.</summary>
    private string? RoverControlRefusal(MachineView view, string id, string field)
    {
        if (!RoverIsPlayer) return null;
        string? kind = Controls.KindOf(view, id);
        var mine = Controls.Table.Where(c => kind is not null && c.Kinds.Contains(kind) && c.Field == field).ToList();
        if (mine.Count == 0)
        {
            if (!(kind == "rope" && field == "hook")) return "The rover can't set that by hand";   // a rope's hook is the hand's, within reach (hooking, #160)
        }
        else if (mine.All(c => c.NotRover is not null)) return mine[0].NotRover;
        return ReachRefusal(PartBox(view, id));
    }

    /// <summary>What <see cref="Operate"/> asks in the game: the field's check, then whether this value is one the rover can give (not pouring in what it can't carry).</summary>
    private string? RoverOperateRefusal(MachineView view, string target, string field, double value) =>
        Controls.Refusal(view, target, field) ?? Controls.RefusesValue(view, target, field, value);

    // ------------------------------------------------------------------ hands

    private float HandForceLimit() => RoverIsPlayer ? (float)RoverSpec.PushForce(_rover!.GroundGravity) : float.PositiveInfinity;

    private static string Mass(double kg) => kg >= 1000 ? $"{F1(kg / 1000)} t" : $"{kg:0} kg";

    /// <summary>Whether a body of this mass and friction takes more to slide than the rover can push: the sentence, or null.</summary>
    private string? TooHeavy(double mass, double friction)
    {
        double g = _rover!.GroundGravity, need = friction * mass * g, push = RoverSpec.PushForce(g);
        return need > push
            ? $"Too heavy for the rover's arm: {Mass(mass)} (it takes {F1(need / 1000)} kN to slide, the rover can push {F1(push / 1000)} kN)"
            : null;
    }

    private static double FrictionOf(RigidBody3D body) => body.PhysicsMaterialOverride is { } m ? m.Friction : 0.6;   // 0.6: the ground's, what a body without a material of its own slides on

    /// <summary>The capability check of a grab (installed as <see cref="GrabRefusal"/>): too heavy first (driving closer would not help), then reach.</summary>
    private string? RoverGrabRefusal(MachineView view, string partId, RigidBody3D body, Vector3 point)
    {
        if (!RoverIsPlayer) return null;
        return TooHeavy(body.Mass, FrictionOf(body)) ?? OutOfReach(_rover!.ArmBase.DistanceTo(point));
    }

    /// <summary>A boulder a slide left: the same checks, by the body's box (the point under the cursor is not asked here).</summary>
    private string? RoverBoulderRefusal(MaterialBlock rock)
    {
        if (TooHeavy(rock.Mass, FrictionOf(rock)) is { } heavy) return heavy;
        var shape = rock.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape as BoxShape3D;
        return shape is null ? null : OutOfReach(ReachTo(rock.GlobalTransform * new Aabb(-shape.Size / 2, shape.Size)));
    }

    /// <summary>Where the rover's hand may go: within the arm's reach of its pivot, and no higher than the load was when taken (plus 5 cm). Says so when it holds the hand back.</summary>
    private Vector3 RoverHandTarget(ActiveHand h, Vector3 target)
    {
        var from = _rover!.ArmBase;
        string? said = null;
        var offset = target - from;
        if (offset.Length() > Rover.ArmReach)
        {
            said = OutOfReach(offset.Length());
            target = from + offset.Normalized() * Rover.ArmReach;
        }
        float top = h.Record.Keys[0].Target.Y + (float)RoverSpec.LiftSlack;
        if (target.Y > top)
        {
            if (target.Y > top + 0.01f) said = "The rover can't lift loads";
            target.Y = top;
        }
        if (said is not null && Clock - h.SaidAt > 2) { h.SaidAt = Clock; RoverRefuse(said); }
        return target;
    }

    // ------------------------------------------------------------------ scripted checks

    private Aabb? BoxOfThing(string name)
    {
        if (name.StartsWith("boulder:", StringComparison.Ordinal) && BoulderBody(name == "boulder:last" ? _lastBoulder ?? "" : name[8..]) is { } rock
            && rock.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape is BoxShape3D shape)
            return rock.GlobalTransform * new Aabb(-shape.Size / 2, shape.Size);
        string? label = null, id = name;
        int colon = name.IndexOf(':');
        if (colon > 0) (label, id) = (name[..colon], name[(colon + 1)..]);
        foreach (var view in _views)
        {
            if (label is not null && _byName.GetValueOrDefault(label) != view) continue;
            if (view.Runtime.Def.Part(id) is null && view.Runtime.Def.Ropes.All(r => r.Id != id)) continue;
            if (PartBox(view, id) is { } box) return box;
        }
        return null;
    }

    private (MachineView View, string Id)? FindThing(string name)
    {
        string? label = null, id = name;
        int colon = name.IndexOf(':');
        if (colon > 0) (label, id) = (name[..colon], name[(colon + 1)..]);
        foreach (var view in _views)
        {
            if (label is not null && _byName.GetValueOrDefault(label) != view) continue;
            if (view.Runtime.Def.Part(id) is not null || view.Runtime.Def.Ropes.Any(r => r.Id == id) || view.Runtime.Def.Sources.Any(s => s.Id == id)) return (view, id);
        }
        return null;
    }

    private string? _lastBoulder;   // the boulder "rover nearboulder" chose: "boulder:last"

    private MaterialBlock? BoulderBody(string id) => _terrainView?.GetChildren().OfType<MaterialBlock>().FirstOrDefault(b => b.Name == id);

    /// <summary>Stands the rover facing a box so that the nearest point of it is <paramref name="gap"/> m from the arm's pivot, coming from the side the rover is on.</summary>
    private void PlaceRoverAt(Aabb box, double gap)
    {
        var rover = _rover!;
        var centre = box.GetCenter();
        var side = rover.Chassis.GlobalPosition - centre;
        side.Y = 0;
        side = side.LengthSquared() < 1e-4f ? Vector3.Back : side.Normalized();
        double heading = Mathf.RadToDeg(Mathf.Atan2(side.X, side.Z));   // faces from the rover toward the box: forward = -side
        double distance = box.Size.Length() / 2 + gap + 0.7;
        for (int pass = 0; pass < 4; pass++)
        {
            var at = centre + side * (float)distance;
            rover.Place(at.X, at.Z, heading, _groundSim!.Ground.HeightAt(at.X, at.Z));
            distance += gap - ReachTo(box);
        }
        _roverHeading = RoverHeading();
    }

    /// <summary>
    /// Scripted checks of the rover's hands (headless: pixels mean nothing there, so these press along rays, through the code a mouse press uses):
    /// "rover boulders" lists the ground's boulders; "rover near THING GAP" and "rover nearboulder THING GAP" stand the rover GAP m (arm pivot to
    /// nearest point) from a part, "LABEL:PART", "boulder:ID", or the boulder nearest a part; "rover reach THING" prints the distance and why not;
    /// "rover press THING" presses on it from above; "rover mouse THING [DX DY]" does it with the real mouse (a window, not headless); "rover hand DX DY DZ" holds the hand that far from where it took hold; "rover release";
    /// "rover body THING" prints where its body is; "rover click THING [shift]" is a click on a part; "rover said" prints what the rover last refused.
    /// </summary>
    private ScriptedInput.Step? RoverHandsStep(string[] w)
    {
        var inv = CultureInfo.InvariantCulture;
        switch (w[1])
        {
            case "boulders" when _groundSim is not null:
                foreach (var b in _groundSim.Ground.Boulders)
                    GD.Print($"[rover] boulder {b.Id} at ({b.X:F2} {b.Y:F2} {b.Z:F2}) size {b.Size:F1} m, {Mass(BoulderBody(b.Id)?.Mass ?? b.Volume * 1000)}");
                return ScriptedInput.Step.Continue;
            case "near" when w.Length == 4 && BoxOfThing(w[2]) is { } box && double.TryParse(w[3], inv, out double gap):
                PlaceRoverAt(box, gap);
                GD.Print($"[rover] near {w[2]}: arm pivot {F1(ReachTo(box))} m from it (asked {F1(gap)}), reach {F1(Rover.ArmReach)} m");
                return ScriptedInput.Step.Next;
            case "nearboulder" when w.Length == 4 && BoxOfThing(w[2]) is { } anchor && double.TryParse(w[3], inv, out double bgap) && _groundSim is not null:
            {
                var c = anchor.GetCenter();
                var rock = _groundSim.Ground.Boulders.OrderBy(b => (new Vector3((float)b.X, (float)b.Y, (float)b.Z) - c).Length()).FirstOrDefault();
                if (rock is null || BoulderBody(rock.Id) is not { } body) { GD.Print("[rover] nearboulder: no boulder"); return ScriptedInput.Step.Continue; }
                _lastBoulder = rock.Id;
                PlaceRoverAt(BoxOfThing($"boulder:{rock.Id}")!.Value, bgap);
                GD.Print($"[rover] nearboulder {rock.Id}: {Mass(body.Mass)}, {F1((body.GlobalPosition - c).Length())} m from {w[2]}, arm pivot {F1(ReachTo(BoxOfThing($"boulder:{rock.Id}")!.Value))} m from it");
                return ScriptedInput.Step.Next;
            }
            case "reach" when w.Length == 3 && BoxOfThing(w[2]) is { } what:
                GD.Print($"[rover] reach {w[2]}: {F1(ReachTo(what))} m of {F1(Rover.ArmReach)}; {OutOfReach(ReachTo(what)) ?? "within reach"}");
                return ScriptedInput.Step.Continue;
            case "press" when w.Length == 3 && BoxOfThing(w[2]) is { } target:
            {
                // a ray down at the point of the box nearest the arm's pivot (its top face when that is where the ray ends up), as a cursor over it would cast
                var p = _rover!.ArmBase;
                var aim = target.Position.Max(p.Min(target.End));
                var inward = target.GetCenter() - aim;
                if (inward.LengthSquared() > 1e-6f) aim += inward.Normalized() * Mathf.Min(0.05f, inward.Length());   // (a ray along a face's edge can miss)
                aim.Y = target.End.Y - 0.02f;
                var origin = aim + Vector3.Up * 3;
                bool used = TryPress(origin, Vector3.Down, Vector2.Zero, false);
                GD.Print($"[rover] press {w[2]}: {(used ? "used" : "not used")}, hand {(_hand is null ? "has nothing" : $"holds {_hand.Record.PartId} at {_hand.Spring.GrabWorld.Y:F3} m high")}");
                return ScriptedInput.Step.Continue;
            }
            case "hand" when w.Length == 5 && _hand is { } h:
            {
                var d = new Vector3(float.Parse(w[2], inv), float.Parse(w[3], inv), float.Parse(w[4], inv));
                h.Placed = h.Record.Keys[0].Target + d;
                return ScriptedInput.Step.Next;
            }
            case "mouse" when w.Length >= 3 && BoxOfThing(w[2]) is { } seen:
            {
                // the real mouse path in a real window (tools/gui-check.sh): press at the pixel the thing's top is drawn at, move by DX DY pixels, let go
                var a = _camera.UnprojectPosition(seen.GetCenter() + Vector3.Up * (seen.Size.Y * 0.3f));
                var d = w.Length >= 5 ? new Vector2(float.Parse(w[3], inv), float.Parse(w[4], inv)) : Vector2.Zero;
                SyntheticMouse(a, true);
                for (int k = 1; k <= 10; k++) SyntheticMouse(a + d * (k / 10f), null);
                SyntheticMouse(a + d, false);
                return ScriptedInput.Step.Next;
            }
            case "release":
                EndHand();
                return ScriptedInput.Step.Continue;
            case "body" when w.Length == 3:
            {
                var thing = FindThing(w[2]);
                var body = thing is { } t ? t.View.HandBody(t.Id) : BoulderBody(w[2] == "boulder:last" ? _lastBoulder ?? "" : w[2].Replace("boulder:", ""));
                GD.Print(body is null ? $"[rover] body {w[2]}: none" : $"[rover] body {w[2]}: at ({body.GlobalPosition.X:F3} {body.GlobalPosition.Y:F3} {body.GlobalPosition.Z:F3}), {body.Mass:F1} kg, hand force {(_hand?.Spring.Force ?? 0):F0} N");
                return ScriptedInput.Step.Continue;
            }
            case "operate" when w.Length == 5 && FindThing(w[2]) is { } op && double.TryParse(w[4], inv, out double value):
                GD.Print($"[rover] operate {w[2]} {w[3]} {w[4]}: {(Operate(op.View, op.Id, w[3], value) ? "done" : "refused")}");
                return ScriptedInput.Step.Continue;
            case "click" when w.Length >= 3 && FindThing(w[2]) is { } part:
                ClickPart(part, Vector2.Zero, w.Length > 3 && w[3] == "shift");
                return ScriptedInput.Step.Continue;
            case "said":
                GD.Print($"[rover] said: {_roverSaid ?? "nothing"}");
                return ScriptedInput.Step.Continue;
        }
        return null;
    }
}
