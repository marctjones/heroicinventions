using Godot;
using HeroicInventions.Sim;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Water holding things up (issue #29). A sim float is drawn as a cork
/// disc riding its tank's water at the height the sim gives. And every
/// moving body standing in a tank's water is buoyed by it: an upthrust of
/// the water it pushes aside, ρ g V under water, at the middle of the part
/// under water, and slowed by the water it drags through,
/// ½ ρ Cd A |v| v on the part under water (a cube's Cd, 1.05), with its
/// spin damped the same way. A body is taken as the box its meshes fill,
/// cut level at the surface; tanks have no walls to hit, so what floats is
/// kept to the water by the forces alone.
/// </summary>
public partial class MachineView
{
    private readonly List<(Float Float, MeshInstance3D Disc)> _floatViews = [];
    private readonly Dictionary<RigidBody3D, Vector3> _boxSize = [];   // local extents of each body's meshes
    private const float DragCoefficient = 1.05f;

    private void BuildFloat(PartSpec part)
    {
        var f = Runtime.Floats[part.Id];
        float side = Mathf.Sqrt((float)f.Area);
        var disc = Shapes.Cylinder(side / 2, (float)f.Height, Surface(part.Material));
        AddChild(disc);
        _floatViews.Add((f, disc));
        AddLabel(part.Id, new Vector3(0, (float)f.Height / 2 + 0.05f, 0), disc);
    }

    private void DrawFloats()
    {
        foreach (var (f, disc) in _floatViews)
        {
            var spec = Runtime.Def.Parts.First(p => p.Kind == "tank" && Runtime.Tanks[p.Id] == f.Tank);
            disc.Position = new Vector3((float)spec.At.X, (float)(f.Bottom + f.Height / 2), (float)spec.At.Z);
        }
    }

    /// <summary>Each tick, before the physics step: upthrust and drag on every body in a tank's water.</summary>
    private void ApplyBuoyancy()
    {
        if (_freezable.Count == 0) return;
        double rho = Physics.WaterDensity, g = Runtime.Outside.Gravity;
        foreach (var (id, tank) in Runtime.Tanks)
        {
            _building = id;
            if (tank.WaterVolume <= 0 || Runtime.Def.Part(id) is not { } spec) continue;
            float half = Mathf.Sqrt((float)tank.Area) / 2, surface = (float)tank.SurfaceElevation;
            var centre = V(spec.At);
            foreach (var body in _freezable)
            {
                if (!IsInstanceValid(body) || body.Freeze) continue;
                var state = PhysicsServer3D.BodyGetDirectState(body.GetRid());
                var com = state.Transform.Origin + state.CenterOfMass;
                if (Mathf.Abs(com.X - centre.X) > half || Mathf.Abs(com.Z - centre.Z) > half) continue;
                var size = BoxSize(body);
                // the box's vertical reach as it now lies, and how much of it is under water
                var basis = state.Transform.Basis;
                float reach = Mathf.Abs(basis.Y.Y) * size.Y + Mathf.Abs(basis.X.Y) * size.X + Mathf.Abs(basis.Z.Y) * size.Z;
                float bottom = com.Y - reach / 2;
                float under = Mathf.Clamp((surface - bottom) / reach, 0, 1);
                if (under <= 0 || bottom < (float)tank.BaseElevation - reach) continue;
                double volume = size.X * size.Y * size.Z * under;
                var up = new Vector3(0, (float)(rho * g * volume), 0);
                var buoyancyAt = new Vector3(com.X, bottom + under * reach / 2, com.Z) - state.Transform.Origin;
                body.ApplyForce(up, buoyancyAt);
                var v = state.LinearVelocity;
                float area = Mathf.Pow(size.X * size.Y * size.Z, 2f / 3f) * under;
                body.ApplyCentralForce(-0.5f * (float)rho * DragCoefficient * area * v.Length() * v);
                // spin slowed by the water round it: a torque against it, of the same form, on the arm of half the box
                var w = state.AngularVelocity;
                float arm = size.Length() / 4;
                body.ApplyTorque(-0.5f * (float)rho * DragCoefficient * area * arm * arm * arm * w.Length() * w);
            }
        }
    }

    private Vector3 BoxSize(RigidBody3D body)
    {
        if (_boxSize.TryGetValue(body, out var s)) return s;
        Aabb? box = null;
        foreach (var mi in body.GetChildren().OfType<MeshInstance3D>().Where(m => !m.HasMeta("scenery")))   // the body's own box, not a look riding on it (12.19)
        {
            var local = mi.Transform * mi.GetAabb();
            box = box is { } b ? b.Merge(local) : local;
        }
        return _boxSize[body] = box?.Size ?? new Vector3(0.1f, 0.1f, 0.1f);
    }
}
