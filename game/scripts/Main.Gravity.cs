using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The physics engine's gravity is the environment's (owner, 2026-10-10): the loaded world's planet (a world has one: its first
/// machine's, the one its ground's water already runs under), or, in a machine run, that machine's planet; Earth's 9.81 m/s² with
/// nothing loaded. So on Mars the rover, its crates, the rim's boulders and every Jolt body fall at 3.71, and an Earth machine at
/// 9.81. Set at every load and checked every tick, so a gravity tuned or set live (scene.gravity) is followed the tick it changes.
/// A machine on another planet than the space's still falls at its own g: MachineView scales its bodies by g / the space's g.
/// </summary>
public partial class Main
{
    private double _spaceGravity = double.NaN;

    /// <summary>The runtime whose planet is the environment's: the world's first machine, or the machine being run.</summary>
    private MachineRuntime? EnvironmentRuntime => _views.Count > 0 ? _views[0].Runtime : _current?.Runtime;

    /// <summary>Sets Jolt's area gravity to the environment's g if it has changed, and says so once.</summary>
    private void ApplySpaceGravity()
    {
        var run = EnvironmentRuntime;
        double g = run?.Outside.Gravity ?? HeroicInventions.Sim.Physics.Gravity;
        if (g == _spaceGravity) return;
        _spaceGravity = g;
        PhysicsServer3D.AreaSetParam(GetWorld3D().Space, PhysicsServer3D.AreaParameter.Gravity, (float)g);
        GD.Print($"[physics] gravity {g:0.##} m/s² ({run?.Planet.Id ?? "earth"})");
        // the rover and the ground it digs stand under the same g (both were set once, at the world's load)
        if (RoverIsPlayer) _rover!.GroundGravity = g;
        if (_groundSim is not null && _views.Count > 0) _groundSim.Water.Gravity = g;
    }
}
