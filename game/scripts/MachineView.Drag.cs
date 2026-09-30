using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Air drag (issue #33): F = ½ ρ C_d A v² against the velocity, on the blocks and balls that name a #:drag-coefficient. ρ is the
/// air where the part stands (its zone), A the face the body presents to its own motion. Nothing else slows a body in the air.
/// </summary>
public partial class MachineView
{
    private readonly List<(string Id, RigidBody3D Body, float Cd, Vector3 Size, bool Sphere)> _draggers = [];

    private void RegisterDrag(PartSpec part, RigidBody3D body, Vector3 size, bool sphere)
    {
        if (part.Props.GetValueOrDefault("drag-coefficient") is SNumber { Value: > 0 } cd)
            _draggers.Add((part.Id, body, (float)cd.Value, size, sphere));
    }

    /// <summary>The face a body of this size presents along a direction of travel: a sphere's disc, a box's projection on it.</summary>
    internal static float FrontalArea(Vector3 size, bool sphere, Basis basis, Vector3 direction)
    {
        if (sphere) return Mathf.Pi * size.X * size.X / 4;                 // size is the diameter
        var d = direction.Normalized();
        return Mathf.Abs(d.Dot(basis.X.Normalized())) * size.Y * size.Z
             + Mathf.Abs(d.Dot(basis.Y.Normalized())) * size.X * size.Z
             + Mathf.Abs(d.Dot(basis.Z.Normalized())) * size.X * size.Y;
    }

    /// <summary>Each tick, before the physics step: the drag on every body that has a coefficient.</summary>
    private void ApplyDrag()
    {
        foreach (var (id, body, cd, size, sphere) in _draggers)
        {
            if (!IsInstanceValid(body) || body.Freeze) continue;
            var state = PhysicsServer3D.BodyGetDirectState(body.GetRid());
            var v = state.LinearVelocity;
            float speed = v.Length();
            if (speed < 1e-6f) continue;
            float rho = (float)Runtime.ZoneOf(id).AirDensity;
            float area = FrontalArea(size, sphere, state.Transform.Basis, v);
            body.ApplyCentralForce(-0.5f * rho * cd * area * speed * v);
        }
    }
}
