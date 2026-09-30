using Godot;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// A solid sphere whose Jolt mass and friction come from the material table (issue #52): it rolls, where a
/// block slides. Jolt takes its moment of inertia from the sphere (2/5·m·r²), so down a slope of angle θ it
/// accelerates at g·sin θ / (1 + 2/5) = (5/7)·g·sin θ and leaves a ramp of height h at √(10·g·h/7), the two
/// prices of turning as well as falling. Engine damping is off for it (Godot's default 0.1 a second would eat a
/// tenth of the speed in a second and a half), and it is swept along its path so a fast ball cannot pass through a thin target.
/// </summary>
public partial class MaterialBall : RigidBody3D
{
    public MaterialDef Material { get; }
    public float Radius { get; }

    public MaterialBall(MaterialDef material, float radius, Color color)
    {
        Material = material;
        Radius = radius;
        Mass = (float)material.MassOf(4.0 / 3.0 * Math.PI * radius * radius * radius);
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = (float)material.Friction, Bounce = (float)material.Restitution };
        CanSleep = false;
        ContinuousCd = true;
        LinearDampMode = DampMode.Replace; LinearDamp = 0;
        AngularDampMode = DampMode.Replace; AngularDamp = 0;
        AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = radius } });
        AddChild(Shapes.Sphere(radius, Shapes.Mat(color)));
        // a stripe round it, so its turning can be seen
        var stripe = Shapes.Box(new Vector3(radius * 2.02f, radius * 0.16f, radius * 0.16f), Shapes.Mat(new Color(0.1f, 0.08f, 0.07f)));
        AddChild(stripe);
    }
}
