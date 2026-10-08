using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// Catches and tethers (issue #155): what holds a machine until someone lets it go.
///
///   catch   a lever's hinge held at #:catch-deg: its two stops pinned together at that angle (a zero-width
///           limit, as a 6-DOF joint locks a motion), so Jolt holds the arm however hard it is pulled, and the
///           blueprint's own stops put back when the field <c>catch</c> goes to 0. Holding at the angle,
///           not stopping one way, means no one has to work out which way the load pushes. Set to 1 again it
///           waits for the arm to come back to that angle (a latch that drops in), not yanks it there.
///           With #:catch-side (#161) it holds one way only, as a pawl does: it replaces only the stop on that side
///           with one at the catch's angle, so a windlass can wind the arm back past it, and set while the arm is
///           anywhere behind it, it drops in at once.
///   tether  a rope (#:tether) whose field <c>tether</c> 0 lets go of both its ends.
///   pawl    see <see cref="DriveRatchets"/>: a ratchet's pawl lifted clear of its teeth.
///
/// What a catch carries: Jolt does not report a joint's reaction, so it is found from the arm's statics. Held
/// still, the arm's angular momentum doesn't change, so the catch gives exactly the opposite of every other
/// torque on it about the hinge: its own weight at its centre of mass, the pull of each rope tied to it (this
/// tick's tension, along the rope), and a torsion spring's −k(θ − rest). (A block lying on the arm would be
/// missed; none of the held machines has one.)
/// </summary>
public partial class MachineView
{
    private sealed class CatchView
    {
        public required Catch Catch;
        public required RigidBody3D Body;
        public required HingeJoint3D Joint;
        public required Vector3 Pivot, Axis;
        public required float Hold, Lower, Upper;   // rad, in the arm's own sense: the catch, and the blueprint's stops
        public required MeshInstance3D Latch;
        public required StandardMaterial3D LatchMaterial;
        public required Label3D Label;
        public double Angle, LastRaw;
        public bool Applied, Pinned;   // engaged; and (a one-way catch) holding both ways, the arm pushed into it
    }

    private sealed record TetherView(Catch Catch, Rope Rope);

    private readonly List<CatchView> _catchViews = [];
    private readonly List<TetherView> _tetherViews = [];

    /// <summary>A lever's catch, from <see cref="BuildLever"/>: <paramref name="lowerDeg"/>, <paramref name="upperDeg"/> its stops.</summary>
    private void BuildCatch(PartSpec part, RigidBody3D body, HingeJoint3D joint, Basis toAxis, Vector3 axis, float lowerDeg, float upperDeg, float depth)
    {
        if (!Runtime.Catches.TryGetValue(part.Id, out var c) || c.Kind != "catch") return;
        float hold = Mathf.DegToRad((float)part.Number("catch-deg"));
        float reach = (float)(part.Number("length") * (1 - part.Number("pivot-fraction", 0.5)));
        // drawn as an iron latch beside the arm's far end where the catch holds it (a trebuchet's trigger
        // near the ground, an onager's slip-hook): red while it holds, as a ratchet's pawl is
        var pivot = V(part.At);
        var tip = pivot + toAxis * new Vector3(reach * Mathf.Cos(hold), reach * Mathf.Sin(hold), 0) + axis * (depth / 2 + 0.04f);
        var mat = Shapes.Mat(new Color(0.9f, 0.3f, 0.2f), metallic: 0.5f);
        var latch = Shapes.Box(new Vector3(0.06f, 0.06f, 0.06f), mat);
        latch.Position = tip;
        AddChild(latch);
        var label = new Label3D
        {
            Position = tip + new Vector3(0, 0.12f, 0),
            FontSize = 24, OutlineSize = 6, PixelSize = 0.0025f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
        };
        AddChild(label);
        _catchViews.Add(new CatchView
        {
            Catch = c, Body = body, Joint = joint, Pivot = pivot, Axis = axis,
            Hold = hold, Lower = Mathf.DegToRad(lowerDeg), Upper = Mathf.DegToRad(upperDeg),
            Latch = latch, LatchMaterial = mat, Label = label,
            Angle = Mathf.DegToRad((float)part.Number("start-angle-deg")), LastRaw = RawAngle(body, axis),
        });
    }

    /// <summary>Tether ropes, once every body is built.</summary>
    private void BuildTethers()
    {
        foreach (var rope in _ropes)
            if (Runtime.Catches.TryGetValue(rope.Spec.Id, out var c) && c.Kind == "tether")
            {
                // ropes are built before some bodies (a lantern's envelope), and an end on a body not yet built
                // was taken for a fixed point: find the ends again now they all exist
                if (rope.Drum is null) (rope.A, rope.ALocal) = ResolveEnd(rope.Spec.From);
                (rope.B, rope.BLocal) = ResolveEnd(rope.Spec.To);
                _tetherViews.Add(new TetherView(c, rope));
            }
    }

    /// <summary>Each tick, before the step: engages or opens each catch and tether as its field says, and reckons what it carries.</summary>
    private void DriveCatches()
    {
        foreach (var v in _catchViews)
        {
            double raw = RawAngle(v.Body, v.Axis);
            double before = v.Angle;
            v.Angle += Unwrap(raw - v.LastRaw);
            v.LastRaw = raw;
            bool held = v.Catch.HeldAt(Runtime.Time);
            int side = v.Catch.Side;
            if (held && !v.Applied)
            {
                // the latch drops in only where the arm is at its angle (or has just passed it); a one-way catch
                // anywhere behind it too, where it then stops the arm coming forward past it
                bool there = Math.Abs(v.Angle - v.Hold) < Mathf.DegToRad(0.5f) || Math.Sign(before - v.Hold) != Math.Sign(v.Angle - v.Hold)
                             || (side != 0 && side * (v.Angle - v.Hold) < 0);
                if (there)
                {
                    SetStops(v, v.Hold, v.Hold, true);   // pinned, until the one-way catch below finds the arm being wound back
                    v.Pinned = true;
                    if (side != 0) GD.Print($"catch {v.Catch.Id} set at {Runtime.Time:F2}s, the arm {Mathf.RadToDeg((float)v.Angle):F1} deg against its {Mathf.RadToDeg(v.Hold):F1}");
                }
            }
            else if (!held && v.Applied)
            {
                SetStops(v, v.Lower, v.Upper, false);
                v.Body.Sleeping = false;
                GD.Print($"catch {v.Catch.Id} let go at {Runtime.Time:F2}s, carrying {Math.Abs(v.Catch.Load):F1} N·m");
            }
            float tau = v.Applied ? TorqueAboutHinge(v) : 0;   // everything on the arm but the catch, in the arm's sense
            if (v.Applied && side != 0)
            {
                // A one-way catch is a pawl: while the arm lies on it and is pushed forward into it, it holds as firmly as a
                // two-way one, both stops pinned at its angle (one stop alone, Jolt lets a light arm under a strong skein
                // through it by degrees in a tick and only slowly pushes it back); pulled back off it (a windlass winding the
                // arm past it), only the forward stop is there, so it turns back freely and the pawl drops in behind it.
                bool resting = side * (v.Angle - v.Hold) > -Mathf.DegToRad(0.5f);
                bool pin = resting && side * tau >= 0;
                if (pin != v.Pinned)
                {
                    if (pin) SetStops(v, v.Hold, v.Hold, true);
                    else if (side > 0) SetStops(v, v.Lower, v.Hold, true);
                    else SetStops(v, v.Hold, v.Upper, true);
                    v.Pinned = pin;
                }
            }
            // a one-way catch carries only what pushes the arm into it; wound back off it, nothing
            float load = v.Applied && (side == 0 || v.Pinned) ? -tau : 0;
            v.Catch.Carry(load);
            v.LatchMaterial.AlbedoColor = v.Applied ? new Color(0.9f, 0.3f, 0.2f) : new Color(0.6f, 0.6f, 0.65f);
            v.Label.Text = v.Applied ? $"catch {Math.Abs(v.Catch.Load):0} N·m" : held ? "catch set" : "catch open";
        }
        foreach (var (c, rope) in _tetherViews)
        {
            bool held = c.HeldAt(Runtime.Time);
            if (!held && !rope.Released)
            {
                rope.Released = true;
                rope.Tension = rope.TensionFrom = rope.TensionTo = 0;   // let go, it carries nothing
                foreach (var seg in rope.Segments) seg.Visible = false;
                foreach (var end in new[] { rope.A, rope.B }) if (end is not null) end.Sleeping = false;
                GD.Print($"tether {c.Id} let go at {Runtime.Time:F2}s, holding {c.Load:F1} N");
            }
            else if (held && rope.Released && !rope.Broken)
            {
                // tied again only if its ends are within its length: a cord can't be knotted round a lantern 10 m up
                var path = RopePath(rope);
                float length = 0;
                for (int i = 1; i < path.Count; i++) length += path[i].DistanceTo(path[i - 1]);
                if (length <= rope.Spec.Length)
                {
                    rope.Released = false;
                    foreach (var seg in rope.Segments) seg.Visible = true;
                }
            }
            c.Carry(rope.Active ? rope.Tension : 0);
        }
    }

    /// <summary>Sets the hinge's stops at [lower, upper] in the arm's sense (the hinge measures the other way: see BuildLever).</summary>
    private static void SetStops(CatchView v, float lower, float upper, bool applied)
    {
        v.Joint.SetParam(HingeJoint3D.Param.LimitUpper, -lower);
        v.Joint.SetParam(HingeJoint3D.Param.LimitLower, -upper);
        v.Applied = applied;
    }

    /// <summary>Every torque on the held arm about its hinge but the catch's, N·m, in the arm's sense.</summary>
    private float TorqueAboutHinge(CatchView v)
    {
        var body = v.Body;
        var com = body.GlobalTransform * _comOffset.GetValueOrDefault(body);
        float tau = (com - v.Pivot).Cross(Vector3.Down * body.Mass * (float)_shownGravity).Dot(v.Axis);
        foreach (var r in _ropes)
        {
            if (!r.Active || (r.A != body && r.B != body)) continue;
            var path = RopePath(r);
            if (r.A == body) tau += (path[0] - v.Pivot).Cross((path[1] - path[0]).Normalized() * r.TensionFrom).Dot(v.Axis);
            if (r.B == body) tau += (path[^1] - v.Pivot).Cross((path[^2] - path[^1]).Normalized() * r.TensionTo).Dot(v.Axis);
        }
        foreach (var s in _springs)
            if (s.Body == body) tau += (float)(-s.Stiffness * (s.Angle - s.Rest));
        return tau;
    }
}
