using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Joints between moving parts (issue #30), built once every body is placed:
/// Godot takes each joint's anchors from where its bodies stand when it is
/// made, so a machine is assembled closed — a connecting rod exactly as long
/// as the gap it spans — and the joints start with nothing to correct.
///
///   pin        a HingeJoint3D between the two, about the given axis
///   ball       a PinJoint3D (point to point), or a ConeTwistJoint3D when its
///              swing is limited, the cone about the line to the second part
///   universal  a Cardan cross: a small iron spider hinged to each shaft,
///              its first arm square to both shafts, its second square to the
///              first and to the driven shaft. Not a 6-DOF joint with one
///              turn locked: Jolt's swing-twist limits make that a constant-
///              velocity joint, and the driven shaft would never speed up
///              and slow down the way a real Hooke's joint makes it.
///   6dof       a Generic6DofJoint3D along the world's axes, each motion
///              locked (a zero-width limit) unless it is named free
///
/// A joint's two bodies don't collide with each other (Godot's default).
/// </summary>
public partial class MachineView
{
    private readonly List<(JointSpec Spec, RigidBody3D A, RigidBody3D B, Label3D Label)> _crosses = [];

    private void BuildJoints()
    {
        BuildTethers();   // tether ropes (#155): the ropes are built by now
        foreach (var spec in Runtime.Def.Joints)
        {
            _building = spec.Id;
            RigidBody3D? Body(string id) => id == "world" ? null : _bodiesById.GetValueOrDefault(id);
            var a = Body(spec.A);
            var b = Body(spec.B);
            if ((spec.A != "world" && a is null) || (spec.B != "world" && b is null))
            {
                GD.PrintErr($"joint {spec.Id}: {spec.A} or {spec.B} has no body to hold");
                continue;
            }
            var at = V(spec.At);
            switch (spec.Kind)
            {
                case "pin":
                {
                    var axis = V(spec.Axis!.Value).Normalized();
                    Attach(new HingeJoint3D { Name = spec.Id, Transform = new Transform3D(AxleBasis(axis), at) }, a, b);
                    break;
                }
                case "ball" when spec.LimitDeg is { } limit:
                {
                    // the cone's axis (the joint's own X) along the line to the second part
                    var toward = ((b ?? a)!.GlobalPosition - at) is var d && d.LengthSquared() > 1e-8f ? d.Normalized() : Vector3.Down;
                    var cone = new ConeTwistJoint3D { Name = spec.Id, Transform = new Transform3D(BasisWithX(toward), at) };
                    cone.SetParam(ConeTwistJoint3D.Param.SwingSpan, Mathf.DegToRad((float)limit));
                    cone.SetParam(ConeTwistJoint3D.Param.TwistSpan, Mathf.Pi);
                    Attach(cone, a, b);
                    break;
                }
                case "ball":
                    Attach(new PinJoint3D { Name = spec.Id, Position = at }, a, b);
                    break;
                case "universal":
                    BuildCross(spec, a!, b!, at);
                    break;
                case "6dof":
                {
                    var joint = new Generic6DofJoint3D { Name = spec.Id, Position = at };
                    var setters = new (string Motion, Action<Generic6DofJoint3D.Flag, bool> Flag, Action<Generic6DofJoint3D.Param, float> Param)[]
                    {
                        ("x", joint.SetFlagX, joint.SetParamX), ("y", joint.SetFlagY, joint.SetParamY), ("z", joint.SetFlagZ, joint.SetParamZ),
                    };
                    foreach (var (motion, flag, param) in setters)
                    {
                        flag(Generic6DofJoint3D.Flag.EnableLinearLimit, !spec.Free.Contains(motion));
                        param(Generic6DofJoint3D.Param.LinearLowerLimit, 0);
                        param(Generic6DofJoint3D.Param.LinearUpperLimit, 0);
                        flag(Generic6DofJoint3D.Flag.EnableAngularLimit, !spec.Free.Contains("r" + motion));
                        param(Generic6DofJoint3D.Param.AngularLowerLimit, 0);
                        param(Generic6DofJoint3D.Param.AngularUpperLimit, 0);
                    }
                    Attach(joint, a, b);
                    break;
                }
            }
        }
    }

    /// <summary>Adds a joint between two bodies, or one body and the world (the joint's NodeB, as for every world hinge here).</summary>
    private void Attach(Joint3D joint, RigidBody3D? a, RigidBody3D? b)
    {
        AddChild(joint);
        if (a is not null && b is not null)
        {
            joint.NodeA = joint.GetPathTo(a);
            joint.NodeB = joint.GetPathTo(b);
        }
        else joint.NodeB = joint.GetPathTo((a ?? b)!);
    }

    private static Basis BasisWithX(Vector3 x)
    {
        var helper = Mathf.Abs(x.Dot(Vector3.Up)) < 0.9f ? Vector3.Up : Vector3.Right;
        var z = x.Cross(helper).Normalized();
        return new Basis(x, z.Cross(x), z);
    }

    /// <summary>A Hooke's joint: a spider hinged to shaft A about one arm and to shaft B about the other.</summary>
    private void BuildCross(JointSpec spec, RigidBody3D a, RigidBody3D b, Vector3 at)
    {
        var axisA = _hinges[a].Axis.Normalized();
        var axisB = _hinges[b].Axis.Normalized();
        var arm1 = axisA.Cross(axisB);
        if (arm1.LengthSquared() < 1e-8f)
            arm1 = axisA.Cross(Mathf.Abs(axisA.Dot(Vector3.Up)) < 0.9f ? Vector3.Up : Vector3.Right);   // shafts in line: any arm square to them
        arm1 = arm1.Normalized();
        var arm2 = axisB.Cross(arm1).Normalized();

        const float size = 0.05f;
        var iron = _materials["iron"];
        var spider = new RigidBody3D
        {
            Name = $"{spec.Id}-cross",
            Mass = (float)iron.MassOf(size * size * size),
            Position = at,
            CollisionLayer = 0,   // exists for Jolt, touches nothing
            CollisionMask = 0,
            CanSleep = false,
        };
        spider.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * size } });
        var look = Surface("iron");
        foreach (var arm in new[] { arm1, arm2 })
        {
            var bar = Shapes.Rod(-arm * size * 1.4f, arm * size * 1.4f, size * 0.22f, look);
            spider.AddChild(bar);
        }
        AddChild(spider);
        _freezable.Add(spider);
        _bodiesById[spider.Name] = spider;

        var first = new HingeJoint3D { Name = $"{spec.Id}-arm-1", Transform = new Transform3D(AxleBasis(arm1), at) };
        Attach(first, a, spider);
        var second = new HingeJoint3D { Name = $"{spec.Id}-arm-2", Transform = new Transform3D(AxleBasis(arm2), at) };
        Attach(second, spider, b);

        var label = new Label3D
        {
            Position = at + new Vector3(0, 0.18f, 0),
            FontSize = 24, OutlineSize = 6, PixelSize = 0.003f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
        };
        AddChild(label);
        _crosses.Add((spec, a, b, label));
    }

    /// <summary>Over each Hooke's joint, the driven shaft's speed as a share of the driving one's.</summary>
    private void DrawJoints()
    {
        foreach (var (spec, a, b, label) in _crosses)
        {
            float wa = a.AngularVelocity.Dot(_hinges[a].Axis), wb = b.AngularVelocity.Dot(_hinges[b].Axis);
            label.Text = Mathf.Abs(wa) > 1e-3f ? $"{spec.Id}: driven shaft at {wb / wa:0.000} x" : spec.Id;
        }
    }
}
