using Godot;

namespace HeroicInventions;

/// <summary>
/// The rover's numbers (issue #94). Sized after Opportunity (about 1.6 m long, 185 kg, 0.26 m wheels), so the crater's scale
/// stays honest; the three things a game has to choose are chosen here and stated, so they can be changed as numbers:
/// <list type="bullet">
/// <item>Speed: <see cref="GameSpeed"/>. Opportunity's own top speed was 5 cm/s (and about 1 cm/s in practice): across the
/// 850 m crater that is hours. 2 m/s crosses it in 7 minutes, and is the pace of a brisk walk (#60's tuning can scale it).</item>
/// <item>Grade: <see cref="GradeDeg"/>, the ~30 degrees of tilt Opportunity was rated for (docs/lonely-rover.html), set by the
/// tyres' grip and nothing else: nothing forbids the climb. On a slope θ the wheels can push up it with at most μ m g cos θ
/// and gravity pulls back with m g sin θ, so the steepest it climbs is tan θ = μ; <see cref="TyreFriction"/> is tan 30° = 0.577
/// for that (measured #198: it stalls at 29.9 degrees from rest on a box, a triangle mesh and a height map alike; the lightened
/// front wheels take it a little under 30). The motors are not the limit: six of <see cref="WheelTorque"/> push 6 T / r = 1680 N,
/// good for asin(1680 / 1815) = 68 degrees, and a single wheel on a hinge motor does give its cap (#198: 42 N·m stalls a wheel
/// carrying 185 kg at 8.5 to 9 degrees, asin(280 / 1815) = 8.9). Torque well above the grip is what skid steering needs (with
/// 22.7 N·m, enough for 30 degrees, it could not turn in place, nor on a 10 degree slope at all).
/// Each wheel hangs on a stiff spring (<see cref="SpringRate"/>), standing for Opportunity's rocker-bogie, which keeps all six
/// wheels loaded. Without it a rigid chassis on six rigid contacts is statically indeterminate: Jolt shared the weight
/// unevenly, light wheels spun and the rover lost its grip at an angle that depended on the ground's triangles and the order
/// the solver met the contacts (#198: with grip 1.0 it stalled at 29 degrees on a box, 30 to 31 on a 0.25 m mesh, 31 to 38 on
/// height maps, where the balance says 45).</item>
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
    public const double Gravity = 9.81;                  // m/s², what the game's physics runs under (Main sets Jolt's area gravity)
    public const double GradeDeg = 30;                   // degrees: the steepest slope the tyres' grip climbs (Opportunity's rated tilt; see the grade note above)
    public const double WheelTorque = 42;                // N·m each of the six wheels' motors, at most: 1680 N at the ground against 907 N of grip at 30 degrees (see the grade note)
    /// <summary>N/m of each wheel's spring, along the chassis's up: the chassis's share (95 kg x 9.81 / 6 = 155 N) sets it 1.5 cm.</summary>
    public const double SpringRate = 10000;
    /// <summary>Of the spring's critical damping, 2 sqrt(k m) with m the chassis's share.</summary>
    public const double SpringDampingRatio = 0.7;
    /// <summary>m each wheel may travel up or down on its spring.</summary>
    public const double SpringTravel = 0.1;

    // ---- the rover's hands (#163): what its arm and wheels can do to the world, as numbers ----
    /// <summary>
    /// N the wheels can pull or push with: the weight's share along the steepest slope it climbs, m g sin 30° = 185 x 9.81 x 0.5
    /// = 907 N, which is what its grip passes to the ground there (measured: it stalls at 29.9 degrees, #198).
    /// </summary>
    public static readonly double WheelPull = TotalMass * Gravity * Math.Sin(GradeDeg * Math.PI / 180);
    /// <summary>
    /// Friction of the tyres on the ground, tan <see cref="GradeDeg"/> = 0.577: the design's number, chosen so that Coulomb grip
    /// gives the 30 degrees (see the grade note), not a measured soil property. The tyre material is "rough", so its own friction
    /// is used, not the lower of tyre and ground (the game's ground has 0.6, above it). Grip caps the push at μ m g, which on Mars
    /// (3.71 m/s²) is 396 N, below <see cref="WheelPull"/>.
    /// </summary>
    public static readonly double TyreFriction = Math.Tan(GradeDeg * Math.PI / 180);
    /// <summary>N the rover can push or drag a load sideways with, under gravity <paramref name="g"/>: the lesser of the wheels' pull and the tyres' grip.</summary>
    public static double PushForce(double g) => Math.Min(WheelPull, TyreFriction * TotalMass * g);
    /// <summary>m above where a load was taken that its hand may go: the backhoe's own tolerance, not lifting (#72, Rover.Backhoe.cs dumps no more than 5 cm above where it dug).</summary>
    public const double LiftSlack = 0.05;
    /// <summary>The arm's pivot, in the chassis frame (the turntable of Rover.Backhoe.cs).</summary>
    public static readonly Vector3 ArmBaseLocal = Rover.TurntableLocal;   // the backhoe's own constant, so reach follows the arm if it moves

    /// <summary>The wheels' speed, rad/s, for the rover to roll at <paramref name="speed"/> m/s.</summary>
    public static double WheelOmega(double speed) => speed / WheelRadius;
}

/// <summary>
/// The player's rover (issue #94): a Jolt body in the world. A chassis and six wheels, each wheel on an axle with a motor (a
/// Generic6DofJoint3D: free to turn about the axle, sprung along the chassis's up, fixed otherwise). It is not a VehicleBody3D:
/// #34 measured that under Jolt a VehicleBody3D with no engine force stops dead and holds a slope, so it can't be asked how
/// steep it climbs or what it does with the motors at their limit; wheels driven by axle motors on real contacts can.
///
/// Forward is -z, the way Godot's cameras look. Drive it with <see cref="Drive"/>; the arm and bucket are
/// <see cref="Backhoe"/> (Rover.Backhoe.cs), the mesh Rover.Mesh.cs.
/// </summary>
public sealed partial class Rover : Node3D
{
    public RigidBody3D Chassis { get; private set; } = null!;
    public IReadOnlyList<RigidBody3D> Wheels => _wheels;

    /// <summary>m the arm reaches from its pivot with boom, stick and bucket straight out (Rover.Backhoe.cs's lengths).</summary>
    public const float ArmReach = BoomLength + StickLength + BucketLength;

    /// <summary>The arm's pivot in the world now.</summary>
    public Vector3 ArmBase => Chassis.GlobalTransform * RoverSpec.ArmBaseLocal;
    private readonly List<RigidBody3D> _wheels = [];
    private readonly List<Generic6DofJoint3D> _axles = [];
    private readonly List<bool> _left = [];

    /// <summary>Asked for each tick: the ground's height at x, z (to set the rover back on it if it ever falls through).</summary>
    public Func<double, double, double>? GroundHeight { get; set; }

    /// <summary>The commanded drive: forward -1 to 1 (1 is <see cref="RoverSpec.GameSpeed"/>), turn -1 to 1 (1 is a right turn at <see cref="RoverSpec.TurnRate"/>).</summary>
    public (double Forward, double Turn) Command { get; set; }
    /// <summary>Scales the top speed (a tuning knob, #60); 1 is <see cref="RoverSpec.GameSpeed"/>.</summary>
    public double SpeedScale { get; set; } = 1;
    /// <summary>N·m each wheel's motor may give: <see cref="RoverSpec.WheelTorque"/> (settable, so the grade can be measured against the torque).</summary>
    public double WheelTorque { get; set; } = RoverSpec.WheelTorque;

    private double _speedCmd, _turnCmd;
    private bool _frozen;

    /// <summary>Holds the whole rover still (the game is paused).</summary>
    public bool Frozen
    {
        get => _frozen;
        set
        {
            _frozen = value;
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

        var tyre = new PhysicsMaterial { Friction = (float)RoverSpec.TyreFriction, Bounce = 0f, Rough = true };   // rough: the tyre's friction is used, not the lower of the two surfaces' (see RoverSpec.TyreFriction)
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
            // the joint's axes are the chassis's: it turns about x (the axle, with the motor), rides on a spring along y, and
            // is held in z and the other two turns (Jolt's 6DOF constraint keeps a spring and a motor inside its step)
            var axle = new Generic6DofJoint3D { Name = $"Axle{k}", Transform = new Transform3D(Basis.Identity, WheelOffset(k)) };
            AddChild(axle);
            axle.NodeA = Chassis.GetPath();
            axle.NodeB = wheel.GetPath();
            // the spring holds the chassis's share of the weight with the wheel where it is drawn: its rest point is that share's
            // stretch below (the wheels' own weight doesn't go through it)
            double share = (RoverSpec.TotalMass - 6 * RoverSpec.WheelMass) / 6;
            axle.SetFlagY(Generic6DofJoint3D.Flag.EnableLinearSpring, true);
            axle.SetParamY(Generic6DofJoint3D.Param.LinearSpringStiffness, (float)RoverSpec.SpringRate);
            axle.SetParamY(Generic6DofJoint3D.Param.LinearSpringDamping, (float)(RoverSpec.SpringDampingRatio * 2 * Math.Sqrt(RoverSpec.SpringRate * share)));
            axle.SetParamY(Generic6DofJoint3D.Param.LinearSpringEquilibriumPoint, (float)(-share * RoverSpec.Gravity / RoverSpec.SpringRate));
            axle.SetParamY(Generic6DofJoint3D.Param.LinearLowerLimit, -(float)RoverSpec.SpringTravel);
            axle.SetParamY(Generic6DofJoint3D.Param.LinearUpperLimit, (float)RoverSpec.SpringTravel);
            axle.SetFlagX(Generic6DofJoint3D.Flag.EnableAngularLimit, false);
            axle.SetFlagX(Generic6DofJoint3D.Flag.EnableMotor, true);
            _wheels.Add(wheel);
            _axles.Add(axle);
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
        for (int k = 0; k < _axles.Count; k++)
        {
            // skid steering: a turn to the right runs the left wheels faster than the right
            double ground = _speedCmd + (_left[k] ? 1 : -1) * _turnCmd * RoverSpec.Track / 2;
            var axle = _axles[k];
            // with no command the motor holds the wheel still: the brake. The 6DOF motor's limit is a torque, not an impulse, so
            // it needs no step length
            axle.SetParamX(Generic6DofJoint3D.Param.AngularMotorTargetVelocity, (float)(MotorSign * RoverSpec.WheelOmega(ground)));
            axle.SetParamX(Generic6DofJoint3D.Param.AngularMotorForceLimit, (float)WheelTorque);
        }
        KeepOnGround();
    }

    /// <summary>+1 or -1: which way the axle motor's target must point for a positive speed to roll the rover forward (found by RoverEval).</summary>
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
