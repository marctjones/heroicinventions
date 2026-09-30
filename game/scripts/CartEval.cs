using Godot;

namespace HeroicInventions;

/// <summary>
/// Issue #34: rigid-wheel carts against Godot's VehicleBody3D, under the
/// game's own physics engine (Jolt at 120 Hz). Run headless:
///   godot --headless --fixed-fps 120 --path game res://scenes/CartEval.tscn
/// It prints one "EVAL name value" line per measurement, and heroic/tests/cart-eval-test.rkt
/// checks them against the formulas:
///   a wheel cart rolls down a slope at g sin(theta) / (1 + sum(I/r^2) / M)
///   a coasting cart, with rolling resistance C_rr N per wheel, slows at C_rr g / (1 + sum(I/r^2) / M)
/// (the second is the issue's "C_rr g" with the wheels' own inertia counted; it is C_rr g for light wheels).
///
/// The rigid cart is a chassis with four disc wheels on hinges, all ordinary
/// Jolt bodies. Jolt has no rolling resistance, so it is added here the way
/// #25 would add it: a torque C_rr N r against each wheel's spin.
/// </summary>
public partial class CartEval : Node3D
{
    private const double G = 9.81;
    private const double SlopeDeg = 10;
    private const double ChassisMass = 20, WheelMass = 2, WheelRadius = 0.15, Crr = 0.02, CoastSpeed = 2;
    private const int Ticks = 120 * 6;   // six seconds a run

    private enum Kind { RigidSlope, RigidCoast, VehicleSlope, VehicleCoast }
    private static readonly Kind[] Runs = [Kind.RigidSlope, Kind.RigidCoast, Kind.VehicleSlope, Kind.VehicleCoast];
    private int _run = -1, _tick;
    private Node3D? _world;
    private RigidBody3D _chassis = null!;
    private readonly List<RigidBody3D> _wheels = [];
    private readonly List<(double T, double V)> _samples = [];
    private Vector3 _alongSlope;

    public override void _Ready()
    {
        PhysicsServer3D.AreaSetParam(GetViewport().FindWorld3D().Space, PhysicsServer3D.AreaParameter.Gravity, (float)G);
        GD.Print($"EVAL engine {ProjectSettings.GetSetting("physics/3d/physics_engine")}");
        NextRun();
    }

    private void NextRun()
    {
        _world?.QueueFree();
        _run++;
        if (_run >= Runs.Length) { GetTree().Quit(); return; }
        _tick = 0;
        _samples.Clear();
        _wheels.Clear();
        _world = new Node3D();
        AddChild(_world);

        var kind = Runs[_run];
        double theta = kind is Kind.RigidSlope or Kind.VehicleSlope ? Mathf.DegToRad(SlopeDeg) : 0;
        // one frame for the ground and the cart, tilted together: down the slope is +x in it
        var frame = new Node3D { Rotation = new Vector3(0, 0, (float)-theta) };
        _world.AddChild(frame);
        _alongSlope = new Vector3((float)Math.Cos(theta), (float)-Math.Sin(theta), 0);

        var ground = new StaticBody3D { PhysicsMaterialOverride = new PhysicsMaterial { Friction = 1f, Bounce = 0f } };
        ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(400, 1, 20) }, Position = new Vector3(0, -0.5f, 0) });
        frame.AddChild(ground);

        if (kind is Kind.RigidSlope or Kind.RigidCoast) BuildRigidCart(frame, kind == Kind.RigidCoast);
        else BuildVehicle(frame, kind == Kind.VehicleCoast);
    }

    /// <summary>Godot's default damping is 0.1 per second: a drag that would swamp a rolling-resistance measurement (#33 removes it game-wide).</summary>
    private static void Undamped(RigidBody3D b)
    {
        b.LinearDampMode = RigidBody3D.DampMode.Replace; b.LinearDamp = 0;
        b.AngularDampMode = RigidBody3D.DampMode.Replace; b.AngularDamp = 0;
    }

    private void BuildRigidCart(Node3D frame, bool coast)
    {
        double r = WheelRadius;
        _chassis = new RigidBody3D { Mass = (float)ChassisMass, Position = new Vector3(0, (float)r, 0) };
        _chassis.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.9f, 0.1f, 0.5f) } });
        Undamped(_chassis);
        frame.AddChild(_chassis);
        foreach (var (x, z) in new[] { (-0.4f, -0.35f), (0.4f, -0.35f), (-0.4f, 0.35f), (0.4f, 0.35f) })
        {
            // a disc wheel: a cylinder (its axis is Y) turned so the axle is along Z
            var wheel = new RigidBody3D
            {
                Mass = (float)WheelMass, Position = new Vector3(x, (float)r, z), Rotation = new Vector3(Mathf.Pi / 2, 0, 0),
                PhysicsMaterialOverride = new PhysicsMaterial { Friction = 1f, Bounce = 0f },
            };
            wheel.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = (float)r, Height = 0.06f } });
            Undamped(wheel);
            frame.AddChild(wheel);
            var hinge = new HingeJoint3D { Position = new Vector3(x, (float)r, z) };   // hinge axis is the joint's own Z
            frame.AddChild(hinge);
            hinge.NodeA = _chassis.GetPath();
            hinge.NodeB = wheel.GetPath();
            _wheels.Add(wheel);
            if (coast) wheel.AngularVelocity = new Vector3(0, 0, (float)(-CoastSpeed / r));
        }
        if (coast)
        {
            _chassis.LinearVelocity = new Vector3((float)CoastSpeed, 0, 0);
            foreach (var w in _wheels) w.LinearVelocity = new Vector3((float)CoastSpeed, 0, 0);
        }
    }

    private void BuildVehicle(Node3D frame, bool coast)
    {
        var body = new VehicleBody3D { Mass = (float)(ChassisMass + 4 * WheelMass), Position = new Vector3(0, 0.28f, 0) };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.9f, 0.1f, 0.5f) } });
        foreach (var (x, z) in new[] { (-0.4f, -0.35f), (0.4f, -0.35f), (-0.4f, 0.35f), (0.4f, 0.35f) })
            body.AddChild(new VehicleWheel3D
            {
                Position = new Vector3(x, -0.13f, z), WheelRadius = (float)WheelRadius, WheelRestLength = 0.1f,
                SuspensionTravel = 0.1f, SuspensionStiffness = 60, WheelFrictionSlip = 10f, EngineForce = 0, Brake = 0,
            });
        Undamped(body);
        frame.AddChild(body);
        _chassis = body;
        if (coast) body.LinearVelocity = new Vector3((float)CoastSpeed, 0, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_run < 0 || _run >= Runs.Length || _world is null) return;
        var kind = Runs[_run];
        if (kind == Kind.RigidCoast)
        {
            // rolling resistance: C_rr * (share of the weight) * r against each wheel's spin
            double n = (ChassisMass + 4 * WheelMass) * G / 4;
            foreach (var w in _wheels)
            {
                double spin = w.AngularVelocity.Z;
                if (Math.Abs(spin) > 1e-3)
                    w.ApplyTorque(new Vector3(0, 0, (float)(Math.Sign(spin) * -Crr * n * WheelRadius)));
            }
        }
        _tick++;
        double t = _tick / 120.0;
        _samples.Add((t, _chassis.LinearVelocity.Dot(_alongSlope)));
        if (_tick >= Ticks) { Report(kind); NextRun(); }
    }

    /// <summary>Acceleration (m/s^2, along the slope) from the least-squares slope of speed against time between 1 s and 4 s.</summary>
    private double Acceleration()
    {
        var pts = _samples.Where(s => s.T is >= 1 and <= 4).ToList();
        double mt = pts.Average(p => p.T), mv = pts.Average(p => p.V);
        return pts.Sum(p => (p.T - mt) * (p.V - mv)) / pts.Sum(p => (p.T - mt) * (p.T - mt));
    }

    private void Report(Kind kind)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("R", inv);
        GD.Print($"EVAL {kind}.acceleration {F(Acceleration())}");
        GD.Print($"EVAL {kind}.final-speed {F(_samples[^1].V)}");
        foreach (double t in new[] { 0.25, 0.5, 1.0, 2.0 })
            GD.Print($"EVAL {kind}.speed-at-{t:0.00} {F(_samples[(int)(t * 120) - 1].V)}");
        // where the chassis ended up in the tilted frame: a vehicle whose wheels do nothing sits on its belly
        var local = _chassis.GetParent<Node3D>().ToLocal(_chassis.GlobalPosition);
        GD.Print($"EVAL {kind}.chassis-height {F(local.Y)}");
        GD.Print($"EVAL {kind}.chassis-pitch-deg {F(Mathf.RadToDeg(_chassis.GlobalRotation.Z - _chassis.GetParent<Node3D>().GlobalRotation.Z))}");
        if (_wheels.Count > 0)
        {
            var state = PhysicsServer3D.BodyGetDirectState(_wheels[0].GetRid());
            var invI = state.InverseInertia;
            GD.Print($"EVAL {kind}.wheel-inertia {F(1 / invI.Z)}");
            GD.Print($"EVAL {kind}.wheel-inertia-ideal {F(_wheels[0].Mass * WheelRadius * WheelRadius / 2)}");
            GD.Print($"EVAL {kind}.wheel-spin-over-roll {F(-_wheels[0].AngularVelocity.Z * WheelRadius / _samples[^1].V)}");
        }
    }
}
