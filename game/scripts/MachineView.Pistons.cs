using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// Pistons: bodies that slide straight up and down over their stroke (a
/// slider joint, no turning). An atmospheric cylinder's force — the
/// atmosphere on the piston's top against the steam or vacuum under it —
/// is applied to its piston every tick, and the piston's height is fed
/// back so the cylinder knows its volume and when the tappets switch the
/// valves. A pump's piston delivers water on each upstroke and carries
/// the weight of the water column while it rises.
/// </summary>
public partial class MachineView
{
    private readonly Dictionary<string, (RigidBody3D Body, float Bottom, PartSpec Part)> _pistons = [];
    private readonly List<(AtmosphericCylinder Cylinder, RigidBody3D Piston, float Bottom, StandardMaterial3D Casing)> _cylinderDrives = [];
    private readonly List<(WaterLift Lift, RigidBody3D Piston, float LastY)> _pumpDrives = [];

    private void BuildPiston(PartSpec part)
    {
        float bore = (float)part.Number("bore"), stroke = (float)part.Number("stroke");
        float start = (float)part.Number("start", 0);
        const float thickness = 0.1f;
        var mat = _materials[part.Material];
        var bottom = V(part.At);
        var body = new RigidBody3D
        {
            Name = part.Id,
            Position = bottom + Vector3.Up * start * stroke,
            Mass = (float)(mat.MassOf(Math.PI * bore * bore / 4 * thickness) + part.Number("rod-mass", 0)),
            PhysicsMaterialOverride = ContactFor(part.Material),
            CollisionLayer = AxleLayer, // it slides in its own bore; nothing else reaches it
            CollisionMask = 0,
            CanSleep = false,
        };
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = bore / 2, Height = thickness } });
        var look = PartSurface(part, bore);
        body.AddChild(Shapes.Cylinder(bore / 2 * 0.98f, thickness, look));
        var rod = Shapes.Cylinder(Mathf.Max(bore * 0.08f, 0.02f), 0.6f, look);
        rod.Position = new Vector3(0, 0.3f + thickness / 2, 0);
        body.AddChild(rod);
        AddChild(body);
        _freezable.Add(body);
        _bodiesById[part.Id] = body;
        _pistons[part.Id] = (body, bottom.Y, part);

        // a slider joint moves along its own X: turn X to point up
        var slide = new SliderJoint3D { Transform = new Transform3D(new Basis(new Vector3(0, 0, 1), Mathf.Pi / 2), body.Position) };
        AddChild(slide);
        slide.NodeB = slide.GetPathTo(body);
        slide.SetParam(SliderJoint3D.Param.LinearLimitLower, -start * stroke);
        slide.SetParam(SliderJoint3D.Param.LinearLimitUpper, (1 - start) * stroke);
        slide.SetParam(SliderJoint3D.Param.AngularLimitLower, 0);
        slide.SetParam(SliderJoint3D.Param.AngularLimitUpper, 0);

        // the cylinder (or pump barrel) it slides in, see-through
        var casingMat = Shapes.Mat(new Color(0.75f, 0.78f, 0.8f), metallic: 0.3f, roughness: 0.4f, alpha: 0.35f);
        var casing = Shapes.Cylinder(bore / 2 * 1.06f, stroke + 2 * thickness, casingMat);
        casing.Position = bottom + Vector3.Up * stroke / 2;
        AddChild(casing);
        AddLabel(part.Id, bottom + Vector3.Up * (stroke + 0.3f), pixelSize: LabelSizeFor(Mathf.Max(stroke, 1)));
        AddGroundedSupport(bottom - Vector3.Up * thickness, bore * 0.2f, bore * 1.4f);
        _casings[part.Id] = casingMat;
    }

    private readonly Dictionary<string, StandardMaterial3D> _casings = [];

    private void BuildPistonDrives()
    {
        foreach (var spec in Runtime.Def.Cylinders)
        {
            var (body, bottom, _) = _pistons[spec.Piston];
            _cylinderDrives.Add((Runtime.Cylinders[spec.Id], body, bottom, _casings[spec.Piston]));
        }
        foreach (var spec in Runtime.Def.Lifts.Where(l => _pistons.ContainsKey(l.By)))
        {
            var body = _pistons[spec.By].Body;
            _pumpDrives.Add((Runtime.Lifts[spec.Id], body, body.GlobalPosition.Y));
        }
    }

    private void DrivePistons()
    {
        foreach (var (cylinder, piston, bottom, _) in _cylinderDrives)
        {
            cylinder.PistonHeight = piston.GlobalPosition.Y - bottom;
            piston.ApplyCentralForce(Vector3.Down * (float)cylinder.Force);
        }
        for (int i = 0; i < _pumpDrives.Count; i++)
        {
            var (lift, piston, lastY) = _pumpDrives[i];
            float y = piston.GlobalPosition.Y;
            lift.Stroke(y - lastY);
            if (piston.LinearVelocity.Y > 0) piston.ApplyCentralForce(Vector3.Down * (float)lift.LoadForce);
            _pumpDrives[i] = (lift, piston, y);
        }
    }

    /// <summary>Steam under the piston shows white; the vacuum after injection, cold blue.</summary>
    private void DrawCylinders()
    {
        foreach (var (cylinder, _, _, casing) in _cylinderDrives)
            casing.AlbedoColor = cylinder.Injecting ? new Color(0.35f, 0.55f, 0.95f, 0.45f) : new Color(0.95f, 0.95f, 0.95f, 0.45f);
    }
}
