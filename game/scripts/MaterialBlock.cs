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

    public MaterialBlock(MaterialDef material, float size, Color color)
    {
        Material = material;
        Mass = (float)material.MassOf(size * size * size);
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = (float)material.Friction, Bounce = (float)material.Restitution };
        // A block on a slope just past its friction angle starts creeping at
        // a fraction of a m/s² — slow enough that the engine would put it
        // to sleep before it ever got going, and a demo about which blocks
        // slide would quietly get the marginal one wrong.
        CanSleep = false;
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * size } });
        AddChild(Shapes.Box(Vector3.One * size, Shapes.Mat(color)));
    }

    // Godot needs a parameterless constructor to instantiate scripts from scenes.
    public MaterialBlock() : this(MaterialLibrary.LoadDefault()["oak"], 0.2f, Colors.SaddleBrown) { }
}
