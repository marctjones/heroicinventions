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
    /// wheels by their inertia, so the arbor turns as one.
    /// </summary>
    private sealed class JoltShaft(RigidBody3D lead, IReadOnlyList<RigidBody3D> bodies, (Vector3 Pivot, Vector3 Axis) hinge) : IShaft
    {
        public double AngularVelocity => PhysicsServer3D.BodyGetDirectState(lead.GetRid()).AngularVelocity.Dot(hinge.Axis);
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
