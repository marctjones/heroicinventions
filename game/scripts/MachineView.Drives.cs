using Godot;

namespace HeroicInventions;

/// <summary>
/// Driven wheels a person can start, stop, slow, reverse or let go (issue #154). A driven hinge's motor
/// stands in for the men in a treadwheel or the hand on a crank; its speed and its most torque are the
/// part's <see cref="HeroicInventions.Sim.Mechanics.HingeDrive"/>, fields (drive-rpm, drive-torque) that
/// a person or a demo operator sets at run time. Every tick the motor is set from them again.
/// </summary>
public partial class MachineView
{
    private readonly List<(HingeJoint3D Joint, HeroicInventions.Sim.Mechanics.HingeDrive Drive)> _driveJoints = [];

    private void ApplyDrives()
    {
        foreach (var (joint, drive) in _driveJoints) ApplyDrive(joint, drive);
    }

    private static void ApplyDrive(HingeJoint3D joint, HeroicInventions.Sim.Mechanics.HingeDrive drive)
    {
        // no torque is a drive let go: the motor is off and the wheel turns freely under whatever pulls on it
        bool engaged = drive.Torque > 0;
        joint.SetFlag(HingeJoint3D.Flag.EnableMotor, engaged);
        if (!engaged) return;
        // Negated for the same reason as BuildLever's spin: the motor's positive sense runs opposite to a
        // positive turn about the axle. Speed 0 is a motor that holds still, as men standing in the wheel do.
        joint.SetParam(HingeJoint3D.Param.MotorTargetVelocity, -(float)(drive.Rpm * Math.Tau / 60));
        // #:drive-torque caps the motor: the most torque it can give in one physics tick is that torque times the tick's length.
        // the most impulse the walkers give in one physics step: torque × the step's length in sim time. A speed-up raises the
        // tick rate AND the time scale together (Main.SetSpeed), so a step stays 1/120 s of sim time; dividing by the tick
        // rate alone made every driven wheel 20x weaker at 20x (found by #157: the crane's stone never left the ground)
        joint.SetParam(HingeJoint3D.Param.MotorMaxImpulse, (float)(drive.Torque * Engine.TimeScale / Engine.PhysicsTicksPerSecond));
    }
}
