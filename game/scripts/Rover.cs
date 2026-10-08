using Godot;

namespace HeroicInventions;

/// <summary>
/// The rover's numbers (issue #94). Sized after Opportunity (about 1.6 m long, 185 kg, 0.26 m wheels), so the crater's scale
/// stays honest; the three things a game has to choose are chosen here and stated, so they can be changed as numbers:
/// <list type="bullet">
/// <item>Speed: <see cref="GameSpeed"/>. Opportunity's own top speed was 5 cm/s (and about 1 cm/s in practice): across the
/// 850 m crater that is hours. 2 m/s crosses it in 7 minutes, and is the pace of a brisk walk (#60's tuning can scale it).</item>
/// <item>Grade: the wheels' motors are torque-limited, and nothing forbids the climb. Each of the six hinge motors may give
/// <see cref="WheelTorque"/>; on a slope steeper than the rover can hold with that, it can't climb and rolls back. As an ideal,
/// 6 T / r would be the drive force at the ground and the steepest slope asin(6 T / (m g r)), but Jolt's hinge motor gives
/// well under what it is told (measured by RoverEval: T = 24 stalls at about 26 degrees where the ideal says 32, T = 42 at
/// 30 to 31 where the ideal says 67), so the cap is set by measurement: 42 N·m stalls at 30 to 31 degrees, the ~30 of tilt
/// Opportunity was rated for (docs/lonely-rover.html). The tyres are "rough" (their friction of 1.0 is used, not the lower of
/// tyre and ground: the ground's 0.6 alone slipped at about 25 degrees), so friction is not what stops it.</item>
/// <item>Turning: skid steering, <see cref="TurnRate"/> rad/s, in place or under way.</item>
/// </list>
/// </summary>
public static class RoverSpec
{
    public const double TotalMass = 185;                 // kg
    public const double WheelMass = 15;                  // kg each: the wheel with its motor and gearbox. 2.5 kg wheels on a 170 kg chassis sagged 3 cm into the ground above 1 m/s (a poor mass ratio for the solver)
    public const double WheelRadius = 0.15, WheelWidth = 0.12;   // m (Opportunity's are 0.13 / 0.16)
    public const double Track = 1.24;                    // m between the left and right wheels' centres
    public static readonly double[] WheelZ = [-0.6, 0.0, 0.6];   // front, middle, rear (forward is -z)
    public const double GameSpeed = 2.0;                 // m/s top speed driving
    public const double TurnRate = 0.9;                  // rad/s
    public const double Accel = 2.5;                     // m/s² the commanded speed changes by, and rad/s² x1.2 for turning
    public const double WheelTorque = 42;                // N·m each of the six wheels, at most (the cap on the hinge motors; see the grade note above)
    public const double Gravity = 9.81;                  // m/s², what the game's physics runs under (Main sets Jolt's area gravity)

    /// <summary>The wheels' speed, rad/s, for the rover to roll at <paramref name="speed"/> m/s.</summary>
    public static double WheelOmega(double speed) => speed / WheelRadius;
}

/// <summary>
/// The player's rover (issue #94): a Jolt body in the world. A chassis and six wheels, each wheel on a hinge with a motor, the
/// same way the game's own machines are driven (MachineView.Drives.cs). It is not a VehicleBody3D: #34 measured that under
/// Jolt a VehicleBody3D with no engine force stops dead and holds a slope, so it can't be asked how steep it climbs or what
/// it does with the motors at their limit; hinge motors with a torque cap do, and the cap is the grade limit.
///
/// Forward is -z, the way Godot's cameras look. Drive it with <see cref="Drive"/>; the arm and bucket are
/// <see cref="Backhoe"/> (Rover.Backhoe.cs), the mesh Rover.Mesh.cs.
/// </summary>
public sealed partial class Rover : Node3D
{
    public RigidBody3D Chassis { get; private set; } = null!;
    public IReadOnlyList<RigidBody3D> Wheels => _wheels;
    private readonly List<RigidBody3D> _wheels = [];
    private readonly List<HingeJoint3D> _hinges = [];
    private readonly List<bool> _left = [];

    /// <summary>Asked for each tick: the ground's height at x, z (to set the rover back on it if it ever falls through).</summary>
    public Func<double, double, double>? GroundHeight { get; set; }

    /// <summary>The commanded drive: forward -1 to 1 (1 is <see cref="RoverSpec.GameSpeed"/>), turn -1 to 1 (1 is a right turn at <see cref="RoverSpec.TurnRate"/>).</summary>
    public (double Forward, double Turn) Command { get; set; }
    /// <summary>Scales the top speed (a tuning knob, #60); 1 is <see cref="RoverSpec.GameSpeed"/>.</summary>
    public double SpeedScale { get; set; } = 1;

    private double _speedCmd, _turnCmd;

    /// <summary>Holds the whole rover still (the game is paused).</summary>
    public bool Frozen
    {
        set
        {
            Chassis.Freeze = value;
            foreach (var w in _wheels) w.Freeze = value;
        }
    }
    /// <summary>Times it was put back on the ground after falling through it (should stay 0).</summary>
    public int Rescues { get; private set; }

    public Rover()
    {
        Name = "Rover";
    }

    /// <summary>Stands the rover on the ground at (x, z) facing <paramref name="heading"/> degrees (Godot's yaw: 0 faces -z, 270 faces +x).</summary>
    public void Place(double x, double z, double heading, double groundY, double pitchDeg = 0)
    {
        _speedCmd = _turnCmd = 0;
        var basis = new Basis(Vector3.Up, Mathf.DegToRad((float)heading)) * new Basis(Vector3.Right, Mathf.DegToRad((float)pitchDeg));
        var origin = new Vector3((float)x, (float)groundY, (float)z) + basis.Y * (float)(RoverSpec.WheelRadius + 0.05);   // axle height, a hand's breadth to drop
        Chassis.GlobalTransform = new Transform3D(basis, origin);
        Chassis.LinearVelocity = Chassis.AngularVelocity = Vector3.Zero;
        for (int k = 0; k < _wheels.Count; k++)
        {
            var w = _wheels[k];
            w.GlobalTransform = new Transform3D(basis * WheelBasis, origin + basis * WheelOffset(k));
            w.LinearVelocity = w.AngularVelocity = Vector3.Zero;
        }
    }

    private static readonly Basis WheelBasis = Basis.FromEuler(new Vector3(0, 0, Mathf.Pi / 2));   // the cylinder's axis (y) turned onto x: the axle

    private static Vector3 WheelOffset(int k)
    {
        float side = k % 2 == 0 ? -1 : 1;
        return new Vector3(side * (float)(RoverSpec.Track / 2), 0, (float)RoverSpec.WheelZ[k / 2]);
    }

    public override void _Ready()
    {
        const uint layer = 1, mask = 1;   // with the crates, the boulders and the ground: whatever the machines' loose bodies meet
        double chassisMass = RoverSpec.TotalMass - 6 * RoverSpec.WheelMass;
        Chassis = new RigidBody3D
        {
            Name = "Chassis", Mass = (float)chassisMass, CollisionLayer = layer, CollisionMask = mask,
            CanSleep = false,
            LinearDampMode = RigidBody3D.DampMode.Replace, LinearDamp = 0,
            AngularDampMode = RigidBody3D.DampMode.Replace, AngularDamp = 0,
        };
        Chassis.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.0f, 0.18f, 1.5f) }, Position = new Vector3(0, 0.22f, 0) });
        AddChild(Chassis);

        var tyre = new PhysicsMaterial { Friction = 1f, Bounce = 0f, Rough = true };   // rough: the tyre's friction is used, not the lower of the two surfaces'. Cleated wheels bite into regolith; against the ground's 0.6 alone the rover slipped at about 25 degrees
        for (int k = 0; k < 6; k++)
        {
            var wheel = new RigidBody3D
            {
                Name = $"Wheel{k}", Mass = (float)RoverSpec.WheelMass, CollisionLayer = layer, CollisionMask = mask,
                PhysicsMaterialOverride = tyre, CanSleep = false,
                LinearDampMode = RigidBody3D.DampMode.Replace, LinearDamp = 0,
                AngularDampMode = RigidBody3D.DampMode.Replace, AngularDamp = 0,
                Transform = new Transform3D(WheelBasis, WheelOffset(k)),
            };
            // a sphere, not a cylinder: Jolt rounds a cylinder's edges and lets it sink 4 cm into the ground (measured: it rolled at 0.90 of omega r); a sphere rolls at exactly omega r
            wheel.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = (float)RoverSpec.WheelRadius } });
            AddChild(wheel);
            // the hinge's axis is the joint's own z; turned onto x
            var hinge = new HingeJoint3D { Name = $"Axle{k}", Transform = new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.Pi / 2, 0)), WheelOffset(k)) };
            AddChild(hinge);
            hinge.NodeA = Chassis.GetPath();
            hinge.NodeB = wheel.GetPath();
            hinge.SetFlag(HingeJoint3D.Flag.EnableMotor, true);
            _wheels.Add(wheel);
            _hinges.Add(hinge);
            _left.Add(k % 2 == 0);
        }
        BuildMesh();
        BuildBackhoe();
    }

    public override void _PhysicsProcess(double delta)
    {
        // the step's length in sim time: a speed-up raises the tick rate and the time scale together (Main.SetSpeed)
        double step = Engine.TimeScale / Engine.PhysicsTicksPerSecond;
        double v = Command.Forward * RoverSpec.GameSpeed * SpeedScale, w = Command.Turn * RoverSpec.TurnRate;
        _speedCmd = Slew(_speedCmd, v, RoverSpec.Accel * step);
        _turnCmd = Slew(_turnCmd, w, RoverSpec.Accel * 1.2 * step);
        for (int k = 0; k < _hinges.Count; k++)
        {
            // skid steering: a turn to the right runs the left wheels faster than the right
            double ground = _speedCmd + (_left[k] ? 1 : -1) * _turnCmd * RoverSpec.Track / 2;
            var hinge = _hinges[k];
            // the motor's sense is opposite to a turn about the axle (as MachineView.ApplyDrive notes); with no command it holds still: the brake
            hinge.SetParam(HingeJoint3D.Param.MotorTargetVelocity, (float)MotorSign * (float)RoverSpec.WheelOmega(ground));
            hinge.SetParam(HingeJoint3D.Param.MotorMaxImpulse, (float)(RoverSpec.WheelTorque * step));
        }
        KeepOnGround();
    }

    /// <summary>+1 or -1: which way the hinge motor's target must point for a positive speed to roll the rover forward (found by RoverEval).</summary>
    public const int MotorSign = 1;

    private static double Slew(double now, double target, double most) =>
        now + Math.Clamp(target - now, -most, most);

    private void KeepOnGround()
    {
        if (GroundHeight is not { } height) return;
        var at = Chassis.GlobalPosition;
        if (at.Y > height(at.X, at.Z) - 3) return;
        // fell through the ground (it was rebuilt under the rover, or a tunnelling step): stand it back on it, facing as it was
        Rescues++;
        Place(at.X, at.Z, Mathf.RadToDeg(Chassis.GlobalRotation.Y), height(at.X, at.Z));   // level, so a tipped rover is righted too
    }

    // ---- readings ----------------------------------------------------------------------------------------------

    /// <summary>Which way the rover faces, level.</summary>
    public Vector3 Forward { get { var f = -Chassis.GlobalBasis.Z; f.Y = 0; return f.LengthSquared() < 1e-6f ? Vector3.Forward : f.Normalized(); } }

    /// <summary>m/s along the rover's heading (negative backing up).</summary>
    public double Speed => Chassis.LinearVelocity.Dot(-Chassis.GlobalBasis.Z);

    /// <summary>Degrees the nose is above (+) or below (-) the level.</summary>
    public double PitchDeg => Mathf.RadToDeg(Math.Asin(Math.Clamp(-Chassis.GlobalBasis.Z.Y, -1, 1)));

    /// <summary>Degrees the body leans from upright, any way.</summary>
    public double TiltDeg => Mathf.RadToDeg(Chassis.GlobalBasis.Y.AngleTo(Vector3.Up));

    public double YawRate => Chassis.AngularVelocity.Y;

    /// <summary>The rover's world-space bounds, about its body (the wheels, deck and mast): for framing.</summary>
    public Aabb Bounds()
    {
        var c = Chassis.GlobalPosition;
        return new Aabb(c + new Vector3(-1.1f, -0.2f, -1.1f), new Vector3(2.2f, 1.5f, 2.2f));
    }

    // ---- the audit: every opaque mesh is tagged (HEROIC_AUDIT=1) -----------------------------------------------

    private static readonly bool Audit = OS.GetEnvironment("HEROIC_AUDIT") == "1";

    /// <summary>Visible opaque meshes with no part tag (the same test as MachineView.UntaggedMeshes).</summary>
    public List<MeshInstance3D> UntaggedMeshes()
    {
        var found = new List<MeshInstance3D>();
        void Walk(Node node)
        {
            if (node.HasMeta("part_id") || node.HasMeta("scenery")) return;
            if (node is MeshInstance3D mesh && mesh.IsVisibleInTree() && MachineView.IsOpaque(mesh)) found.Add(mesh);
            foreach (var child in node.GetChildren()) Walk(child);
        }
        foreach (var child in GetChildren()) Walk(child);
        return found;
    }

    public override void _ExitTree()
    {
        if (!Audit) return;
        int meshes = FindChildren("*", "MeshInstance3D", true, false).Count;
        GD.Print($"[audit] rover: {meshes} meshes, untagged opaque meshes: {UntaggedMeshes().Count}");
    }
}
