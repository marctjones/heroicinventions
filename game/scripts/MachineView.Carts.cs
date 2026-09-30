using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// Carts (issue #25): a wheel #:on a block turns on an axle that block
/// carries, not the world's — the block is a chassis, and the wheels roll
/// it along on whatever they stand on. They are ordinary Jolt bodies on
/// hinges (the #34 evaluation: rigid wheels roll true; VehicleBody3D holds
/// a slope it should run down).
///
/// Jolt has no rolling resistance, so each tick a carried wheel is given a
/// torque C_rr·N·r against its turn, N the load on it: the push of the
/// ground in the last step, from its contact impulses. C_rr is the wheel's
/// #:rolling-resistance, or 0.002 for metal on metal (a railway wheel on its
/// rail) and 0.04 for anything else (a 19th-century stage coach on a dirt
/// road; Wikipedia's table of coefficients). A cart and its wheels also run
/// without Godot's default 0.1/s damping: their drag is the rolling
/// resistance (#33 will take the damping off everything, for air drag).
/// </summary>
public partial class MachineView
{
    private sealed class CarriedWheel
    {
        public required RigidBody3D Wheel, Chassis;
        public required Vector3 LocalAxis;     // the axle, in the chassis's frame
        public required float Radius;
        public float? RollingResistance;       // null: from the materials in contact
        public required bool Metal;
        public float Load, Resisting;          // last tick: N on it, and the torque against it (N·m)
    }

    private readonly List<CarriedWheel> _carried = [];
    private readonly List<(PartSpec Part, RigidBody3D Wheel, Vector3 Axis)> _toCarry = [];
    private readonly Dictionary<ulong, string> _surfaceMaterials = [];

    private const float MetalOnMetal = 0.002f, OtherRolling = 0.04f;

    private static void Undamped(RigidBody3D b)
    {
        b.LinearDampMode = RigidBody3D.DampMode.Replace; b.LinearDamp = 0;
        b.AngularDampMode = RigidBody3D.DampMode.Replace; b.AngularDamp = 0;
    }

    /// <summary>Hinges each carried wheel to its chassis, once every body stands where it starts.</summary>
    private void BuildCarriedWheels()
    {
        foreach (var (part, wheel, axis) in _toCarry)
        {
            string on = part.Symbol("on", "");
            if (!_bodiesById.TryGetValue(on, out var chassis)) { GD.PrintErr($"wheel {part.Id}: no chassis {on}"); continue; }
            var joint = new HingeJoint3D { Name = $"{part.Id}-axle", Transform = new Transform3D(AxleBasis(axis), wheel.GlobalPosition) };
            AddChild(joint);
            joint.NodeA = joint.GetPathTo(chassis);
            joint.NodeB = joint.GetPathTo(wheel);
            Undamped(wheel);
            Undamped(chassis);
            _carried.Add(new CarriedWheel
            {
                Wheel = wheel, Chassis = chassis,
                LocalAxis = chassis.GlobalBasis.Inverse() * axis,
                Radius = (float)part.Number("radius", part.Number("pitch-radius", 0.1)),
                RollingResistance = part.Props.GetValueOrDefault("rolling-resistance") is SNumber rr ? (float)rr.Value : null,
                Metal = _materials[part.Material].Category == MaterialCategory.Metal,
            });
        }
    }

    /// <summary>
    /// Rolling resistance: C_rr·N·r against each carried wheel's turn on its
    /// axle. N is an equal share of the whole cart's weight among the wheels
    /// touching something, pressed along the contact's normal (m g cos t on a
    /// slope). Not the contact impulse: Godot's Jolt reports an estimate that
    /// counts only the wheel's own mass, not the chassis bearing on it through
    /// its axle (it read 24 N under a wheel carrying 70).
    /// </summary>
    private void RollCarriedWheels()
    {
        float dt = (float)GetPhysicsProcessDeltaTime();
        foreach (var cart in _carried.GroupBy(c => c.Chassis))
        {
            var chassis = cart.Key;
            var wheels = cart.ToList();
            var touching = new List<(CarriedWheel Wheel, Vector3 Normal, bool OnMetal)>();
            foreach (var c in wheels)
            {
                var state = PhysicsServer3D.BodyGetDirectState(c.Wheel.GetRid());
                var normal = Vector3.Zero;
                bool onMetal = false;
                for (int i = 0; i < state.GetContactCount(); i++)
                {
                    ulong other = state.GetContactColliderId(i);
                    if (other == chassis.GetInstanceId()) continue;
                    normal += state.GetContactLocalNormal(i);
                    onMetal |= _surfaceMaterials.TryGetValue(other, out var m) && _materials[m].Category == MaterialCategory.Metal;
                }
                c.Load = 0;
                c.Resisting = 0;
                if (normal.LengthSquared() > 1e-8f) touching.Add((c, normal.Normalized(), onMetal));
            }
            if (touching.Count == 0) continue;
            var gravity = PhysicsServer3D.BodyGetDirectState(chassis.GetRid()).TotalGravity * chassis.GravityScale;
            float weight = (chassis.Mass + wheels.Sum(w => w.Wheel.Mass)) / touching.Count;
            foreach (var (c, normal, onMetal) in touching)
            {
                float load = Mathf.Max(0, -weight * gravity.Dot(normal));
                float crr = c.RollingResistance ?? (c.Metal && onMetal ? MetalOnMetal : OtherRolling);
                var axis = (chassis.GlobalBasis * c.LocalAxis).Normalized();
                float turning = (c.Wheel.AngularVelocity - chassis.AngularVelocity).Dot(axis);
                c.Load = load;
                if (Mathf.Abs(turning) * c.Radius < 1e-3f) continue;
                // never more than stops its turn this tick
                var state = PhysicsServer3D.BodyGetDirectState(c.Wheel.GetRid());
                float inertia = axis.Dot(state.InverseInertiaTensor.Inverse() * axis);
                float torque = Mathf.Min(crr * load * c.Radius, inertia * Mathf.Abs(turning) / dt);
                c.Resisting = torque;
                c.Wheel.ApplyTorque(-Mathf.Sign(turning) * torque * axis);
            }
        }
    }
}
