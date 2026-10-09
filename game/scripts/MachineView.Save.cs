using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// What the game holds that the simulation core does not, for saving a world (issue #67): the rigid bodies'
/// poses and velocities (in the view's own frame, so a machine can be put back wherever it stands), each
/// rope's wound length and whether it has let go or broken, and the running turn counts of the cams and ratchets.
/// The sim's own state is saved by <see cref="RuntimeState"/>. Restored beside it, the machine is
/// where it was, moving as it was; the engine's solver forgets the contacts it had warm-started, so a machine
/// in the middle of a collision carries on within a hair rather than to the last digit.
/// </summary>
public partial class MachineView
{
    /// <summary>The game-side state as <c>(view (body name …) (rope id …) (cam id …) (ratchet id …))</c>.</summary>
    public SList CaptureView()
    {
        var items = new List<SExpr> { new SSymbol("view") };
        SExpr N(double v) => new SNumber(v);
        foreach (var b in TracedBodies().OrderBy(b => b.Name.ToString(), StringComparer.Ordinal))
        {
            var t = b.Transform;
            var o = t.Origin; var x = t.Basis.X; var y = t.Basis.Y; var z = t.Basis.Z;
            var lv = b.LinearVelocity; var av = b.AngularVelocity;
            items.Add(new SList([new SSymbol("body"), new SSymbol(b.Name.ToString()),
                N(o.X), N(o.Y), N(o.Z), N(x.X), N(x.Y), N(x.Z), N(y.X), N(y.Y), N(y.Z), N(z.X), N(z.Y), N(z.Z),
                N(lv.X), N(lv.Y), N(lv.Z), N(av.X), N(av.Y), N(av.Z)]));
        }
        foreach (var r in _ropes)
            items.Add(new SList([new SSymbol("rope"), new SSymbol(r.Spec.Id), N(r.Wound), new SBool(r.Released), new SBool(r.Broken), N(r.ArmTurned)]));
        foreach (var c in _camViews)
            items.Add(new SList([new SSymbol("cam"), new SSymbol(c.Cam.Id), N(c.Theta)]));
        foreach (var r in _ratchetViews)
            items.Add(new SList([new SSymbol("ratchet"), new SSymbol(r.Ratchet.Id), N(r.Theta)]));
        return new SList(items);
    }

    /// <summary>Puts the bodies, ropes and counters back as <see cref="CaptureView"/> found them. Returns how many entries found nothing to set.</summary>
    public int RestoreView(SList view)
    {
        int missed = 0;
        var bodies = TracedBodies().ToDictionary(b => b.Name.ToString(), b => b);
        static double D(SExpr e) => e is SNumber n ? n.Value : 0;
        foreach (var entry in view.Items.Skip(1).OfType<SList>())
        {
            var it = entry.Items;
            switch (entry.Head)
            {
                case "body" when it.Count == 20 && it[1] is SSymbol name && bodies.TryGetValue(name.Name, out var b):
                {
                    float F(int i) => (float)D(it[i]);
                    var basis = new Basis(new Vector3(F(5), F(6), F(7)), new Vector3(F(8), F(9), F(10)), new Vector3(F(11), F(12), F(13)));
                    var transform = new Transform3D(basis, new Vector3(F(2), F(3), F(4)));
                    b.Transform = transform;
                    PhysicsServer3D.BodySetState(b.GetRid(), PhysicsServer3D.BodyState.Transform, b.GetParent<Node3D>().GlobalTransform * transform);
                    b.LinearVelocity = new Vector3(F(14), F(15), F(16));
                    b.AngularVelocity = new Vector3(F(17), F(18), F(19));
                    break;
                }
                case "rope" when it.Count == 6 && it[1] is SSymbol rid && _ropes.FirstOrDefault(r => r.Spec.Id == rid.Name) is { } rope:
                    rope.Wound = (float)D(it[2]);
                    rope.Released = it[3] is SBool { Value: true };
                    rope.Broken = it[4] is SBool { Value: true };
                    rope.ArmTurned = (float)D(it[5]);
                    break;
                case "cam" when it.Count == 3 && it[1] is SSymbol cid && _camViews.FirstOrDefault(c => c.Cam.Id == cid.Name) is { } cam:
                    cam.Theta = D(it[2]);
                    cam.LastRaw = RawAngle(cam.Wheel, cam.Axis);
                    break;
                case "ratchet" when it.Count == 3 && it[1] is SSymbol pid && _ratchetViews.FirstOrDefault(r => r.Ratchet.Id == pid.Name) is { } pawl:
                    pawl.Theta = D(it[2]);
                    pawl.LastRaw = RawAngle(pawl.Wheel, pawl.Axis);
                    break;
                default: missed++; break;
            }
        }
        ResyncGearAngles();   // the trains take up from where their gears now stand (MachineView.Gears.cs)
        return missed;
    }
}
