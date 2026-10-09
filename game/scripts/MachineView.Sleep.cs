using Godot;

namespace HeroicInventions;

/// <summary>What a sleep (issue #207) needs to know of a machine's physics-engine side.</summary>
public partial class MachineView
{
    /// <summary>
    /// True while a sleep lets the physics engine run at speed (SleepControl, live mode): a step skips redrawing the scene, which
    /// nothing in the next step reads, and the sleep shows the state once, on waking. The simulation and the rigid bodies step as ever.
    /// </summary>
    public static bool Hurrying { get; set; }

    /// <summary>
    /// True when part of this machine lives in the physics engine and is coupled to the simulation: geared trains and driven
    /// shafts, axles, or any body that is awake and moving. A sleep that pauses the engine would leave these where they were
    /// while the simulation's clock ran on, and they would catch up on waking.
    /// </summary>
    public bool JoltDriven =>
        _drivenLinks.Count > 0 || _gearFollowers.Count > 0 || _axles.Count > 0 ||
        _freezable.Any(b => !b.Sleeping && (b.LinearVelocity.LengthSquared() > 1e-6f || b.AngularVelocity.LengthSquared() > 1e-6f));
}
