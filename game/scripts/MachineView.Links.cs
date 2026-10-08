using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// What a world's links (issue #78) need from a machine's view: its bodies
/// turning on axles as shaft ends, and where its parts and ports stand, so
/// the links can be drawn between machines.
/// </summary>
public partial class MachineView
{
    /// <summary>
    /// A Jolt body on a hinge, with any wheels locked to it on one arbor, as
    /// one end of a shaft: its speed read from the physics server (a node
    /// shows the previous tick), its inertia about the hinge line from the
    /// engine's own inertia tensor, and an impulse shared among the arbor's
    /// wheels by their inertia, so the arbor turns as one. Its speed is the
    /// arbor's angular momentum over its inertia: the lock between an
    /// arbor's wheels gives a little, and the speed an exchange corrects
    /// should be the one its impulse, shared by inertia, changes.
    /// </summary>
    private sealed class JoltShaft(RigidBody3D lead, IReadOnlyList<RigidBody3D> bodies, (Vector3 Pivot, Vector3 Axis) hinge) : IShaft
    {
        public double AngularVelocity
        {
            get
            {
                if (bodies.Count == 1) return PhysicsServer3D.BodyGetDirectState(lead.GetRid()).AngularVelocity.Dot(hinge.Axis);
                double momentum = 0, inertia = 0;
                foreach (var b in bodies)
                {
                    double i = InertiaAbout(b, hinge);
                    momentum += i * PhysicsServer3D.BodyGetDirectState(b.GetRid()).AngularVelocity.Dot(hinge.Axis);
                    inertia += i;
                }
                return inertia > 0 ? momentum / inertia : 0;
            }
        }
        public double ShaftInertia => bodies.Sum(b => (double)InertiaAbout(b, hinge));
        public void AddAngularImpulse(double impulse)
        {
            double total = ShaftInertia;
            if (!(total > 0)) return;
            foreach (var b in bodies)
                PhysicsServer3D.BodyGetDirectState(b.GetRid())
                    .ApplyTorqueImpulse(hinge.Axis * (float)(impulse * InertiaAbout(b, hinge) / total));
        }
    }

    /// <summary>The body named <paramref name="id"/> as a shaft end, if it turns on a hinge; null for a part the sim turns (a windmill), or one that doesn't turn.</summary>
    public IShaft? ShaftEnd(string id)
    {
        if (!_bodiesById.TryGetValue(id, out var body) || !_hinges.TryGetValue(body, out var hinge)) return null;
        return new JoltShaft(body, [body, .. _arborMates.GetValueOrDefault(body, [])], hinge);
    }

    /// <summary>
    /// Locks <paramref name="id"/>'s axle to <paramref name="other"/>'s <paramref name="otherId"/> inside Jolt's step
    /// (#191), as the wheels on one arbor are locked (<see cref="BuildArbors"/>): a hinge between the two bodies
    /// allowed no rotation at all. A shaft's momentum exchange runs once a tick, before Jolt steps; what acts on
    /// one end inside the step (a walkers' motor holding its wheel at 3 rpm) never reached the other, which ran a
    /// tick of the shaft's torque behind. Locked, the motor turns both, and the rope's load on the far end is felt
    /// by it within the step, as in one machine. Only a shaft a lock can be: the two axles on one line, the far
    /// end turning <paramref name="ratio"/> times this one with ratio × (axis · other axis) = +1. Each end's
    /// bodies also become the other's arbor-mates, so a rope pulling on one end knows it has the whole shaft's
    /// inertia to turn, as in one machine (seeing the drum's 4.5 kg·m² alone, the split crane's rope paid out
    /// at the start and dropped the stone onto the ground). Returns the lock (added under
    /// <paramref name="parent"/>) and what undoes it, or the reason it can't be one. Undo before the links
    /// are resolved again: a shaft end is its body and its own machine's arbor-mates.
    /// </summary>
    public (Action? Unlock, string? Why) LockAxleTo(string id, MachineView other, string otherId, double ratio, Node parent)
    {
        if (!_bodiesById.TryGetValue(id, out var a) || !_hinges.TryGetValue(a, out var ha)) return (null, $"{Name}'s {id} doesn't turn on a Jolt hinge");
        if (!other._bodiesById.TryGetValue(otherId, out var b) || !other._hinges.TryGetValue(b, out var hb)) return (null, $"{other.Name}'s {otherId} doesn't turn on a Jolt hinge");
        var axisA = ha.Axis.Normalized();
        if (Math.Abs(ratio * axisA.Dot(hb.Axis.Normalized()) - 1) > 1e-4)
            return (null, $"ratio {ratio} with axles {ha.Axis} and {hb.Axis}: not one shaft turning as one piece");
        var off = hb.Pivot - ha.Pivot;
        if ((off - axisA * axisA.Dot(off)).Length() > 1e-3f)
            return (null, $"the axles are {(off - axisA * axisA.Dot(off)).Length():F3} m off one line");
        List<RigidBody3D> mine = [a, .. _arborMates.GetValueOrDefault(a, [])], theirs = [b, .. other._arborMates.GetValueOrDefault(b, [])];
        var lock_ = new HingeJoint3D { Name = $"lock-{Name}-{id}-{other.Name}-{otherId}", Transform = new Transform3D(AxleBasis(axisA), b.GlobalPosition) };
        parent.AddChild(lock_);
        lock_.NodeA = lock_.GetPathTo(a);
        lock_.NodeB = lock_.GetPathTo(b);
        lock_.SetFlag(HingeJoint3D.Flag.UseLimit, true);
        lock_.SetParam(HingeJoint3D.Param.LimitUpper, 0);
        lock_.SetParam(HingeJoint3D.Param.LimitLower, 0);
        static void Join(MachineView view, List<RigidBody3D> bodies, List<RigidBody3D> mates)
        {
            foreach (var body in bodies)
                view._arborMates[body] = [.. view._arborMates.GetValueOrDefault(body, []), .. mates];
        }
        static void Part(MachineView view, List<RigidBody3D> bodies, List<RigidBody3D> mates)
        {
            if (!IsInstanceValid(view)) return;
            foreach (var body in bodies)
                if (view._arborMates.TryGetValue(body, out var list))
                {
                    list.RemoveAll(mates.Contains);
                    if (list.Count == 0) view._arborMates.Remove(body);
                }
        }
        Join(this, mine, theirs);
        Join(other, theirs, mine);
        return (() =>
        {
            Part(this, mine, theirs);
            Part(other, theirs, mine);
            if (IsInstanceValid(lock_)) lock_.QueueFree();
        }, null);
    }

    /// <summary>Where a link to <paramref name="id"/> attaches: a moving body's centre now, else the part's place (a tank's port, a windmill's hub).</summary>
    public Vector3? LinkPoint(string id, string? port)
    {
        if (Runtime.Def.Part(id) is not { } part) return null;
        if (port is not null)
            return part.Ports.Any(p => p.Name == port) ? PortPosition(new PortRef(id, port)) : null;
        return _bodiesById.TryGetValue(id, out var body) ? body.GlobalPosition : V(part.At);
    }

    /// <summary>Steps the machine without writing its trace frame, for a world that has its shafts to couple before it records.</summary>
    public void SimulateUntraced(double dt) => Simulate(dt, trace: false);

    /// <summary>The trace frame a <see cref="SimulateUntraced"/> step left unwritten.</summary>
    public void TraceStep(double dt) => TraceTick(dt);
}
