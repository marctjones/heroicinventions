using Godot;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// A cube whose Jolt mass and friction come from the material table, so
/// an oak block and a granite block of the same size behave differently.
/// </summary>
public partial class MaterialBlock : RigidBody3D
{
    public MaterialDef Material { get; }

    public MaterialBlock(MaterialDef material, Vector3 size, Color color)
    {
        Material = material;
        Mass = (float)material.MassOf(size.X * size.Y * size.Z);
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = (float)material.Friction, Bounce = (float)material.Restitution };
        // A block on a slope just past its friction angle starts creeping at
        // a fraction of a m/s² — slow enough that the engine would put it
        // to sleep before it ever got going, and a demo about which blocks
        // slide would quietly get the marginal one wrong.
        CanSleep = false;
        // Loose blocks are what gets thrown: a catapulta's bolt leaves at
        // 25 m/s, 0.4 m per physics tick, far more than its own 2.5 cm
        // section or the floor's thickness, so without a swept test it can
        // pass through the ground between two ticks and fall forever.
        ContinuousCd = true;
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(Shapes.Box(size, Shapes.Mat(color)));
    }

    // Godot needs a parameterless constructor to instantiate scripts from scenes.
    public MaterialBlock() : this(MaterialLibrary.LoadDefault()["oak"], Vector3.One * 0.2f, Colors.SaddleBrown) { }
}
