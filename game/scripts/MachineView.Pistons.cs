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
    private readonly List<(ICylinder Cylinder, RigidBody3D Piston, float Bottom, StandardMaterial3D Casing)> _cylinderDrives = [];
    // What shows the steam and the cold water: puffs at the steam valve while
    // steam flows into the cylinder; spray inside it while the jet runs.
    private readonly List<(ICylinder Cylinder, GpuParticles3D Steam, GpuParticles3D Spray)> _cylinderPlumbing = [];
    private readonly List<(WaterLift Lift, RigidBody3D Piston, float LastY)> _pumpDrives = [];
    // steam cylinders whose valve an eccentric on a crank works: the crank, and its pin in the crank's own frame
    private readonly List<(SteamCylinder Cylinder, RigidBody3D Crank, Vector3 Pin, RigidBody3D Piston)> _valveGear = [];

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
            var (body, bottom, piston) = _pistons[spec.Piston];
            var cylinder = Runtime.Cylinders[spec.Id];
            _cylinderDrives.Add((cylinder, body, bottom, _casings[spec.Piston]));
            // a high-pressure cylinder has no injection cistern: its steam goes out to the air
            _cylinderPlumbing.Add((cylinder, BuildSteamMain(spec, piston), cylinder is SteamCylinder ? new GpuParticles3D { Emitting = false } : BuildInjection(piston)));
            // the valve's eccentric: the crank pin is where a joint holds something to the crank
            if (cylinder is SteamCylinder steam && spec.Crank is { } crankId && _bodiesById.TryGetValue(crankId, out var crank)
                && Runtime.Def.Joints.FirstOrDefault(j => j.A == crankId || j.B == crankId) is { } pinJoint)
                _valveGear.Add((steam, crank, crank.GlobalTransform.AffineInverse() * V(pinJoint.At), body));
        }
        foreach (var spec in Runtime.Def.Lifts.Where(l => _pistons.ContainsKey(l.By)))
        {
            var body = _pistons[spec.By].Body;
            _pumpDrives.Add((Runtime.Lifts[spec.Id], body, body.GlobalPosition.Y));
        }
    }

    private void DrivePistons()
    {
        // an eccentric on the crankshaft: steam under the piston while the
        // crank's forward turn raises it. The rod keeps its length, so the
        // piston rises at (v_pin · u) / u_y, u along the rod from pin to piston:
        // the valve turns at the piston's own dead centres, even with the
        // cylinder set off the crank's line.
        foreach (var (steam, crank, pin, piston) in _valveGear)
        {
            if (!_hinges.TryGetValue(crank, out var hinge)) continue;
            var at = crank.GlobalTransform * pin;
            var along = (piston.GlobalPosition - at).Normalized();
            float rise = hinge.Axis.Cross(at - hinge.Pivot).Dot(along) / along.Y;
            steam.Valve = rise >= 0 ? 1 : -1;
        }
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
        foreach (var (cylinder, steam, spray) in _cylinderPlumbing)
        {
            steam.Emitting = cylinder.SteamDraw > 1e-4; // only while steam is really flowing in
            spray.Emitting = cylinder.Injecting;
        }
    }

    /// <summary>
    /// The steam main: a pipe from the boiler's dome up to the bottom of the
    /// cylinder, with the steam valve (the "regulator") in it. Newcomen's
    /// cylinder stood directly over its boiler, so the pipe is short.
    /// </summary>
    private GpuParticles3D BuildSteamMain(CylinderSpec spec, PartSpec piston)
    {
        var boiler = Runtime.Def.Part(spec.Boiler)!;
        var boilerTop = V(boiler.At) + Vector3.Up * (float)boiler.Number("height");
        var cylinderBase = V(piston.At) - Vector3.Up * 0.1f;
        var iron = Surface("iron");
        var from = new Vector3(cylinderBase.X, boilerTop.Y, cylinderBase.Z);
        AddChild(Shapes.Rod(from, cylinderBase, 0.08f, iron));
        var valve = Shapes.Box(new Vector3(0.3f, 0.12f, 0.3f), Surface("bronze"));
        valve.Position = (from + cylinderBase) / 2;
        AddChild(valve);
        AddLabel("steam valve", valve.Position + new Vector3(0.45f, 0, 0), pixelSize: 0.004f);
        var puffs = Particles(valve.Position + new Vector3(0.18f, 0, 0), new Color(1, 1, 1, 0.3f), 0.05f,
                              Vector3.Up, rise: 0.4f, speed: 0.5f);
        AddChild(puffs);
        return puffs;
    }

    /// <summary>
    /// The injection: a cistern of cold water up beside the cylinder (kept
    /// full by the engine's own pump in a real engine house) and a pipe
    /// down into the cylinder's base, where the jet sprays up into the
    /// steam to condense it.
    /// </summary>
    private GpuParticles3D BuildInjection(PartSpec piston)
    {
        float bore = (float)piston.Number("bore"), stroke = (float)piston.Number("stroke");
        var bottom = V(piston.At);
        var cistern = bottom + new Vector3(-(bore + 0.5f), stroke + 0.4f, 0);
        AddChild(Place(Shapes.Box(new Vector3(0.6f, 0.4f, 0.6f), Shapes.Mat(new Color(0.85f, 0.92f, 0.95f), roughness: 0.1f, alpha: 0.25f)), cistern));
        AddChild(Place(Shapes.Box(new Vector3(0.56f, 0.28f, 0.56f), Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.8f)), cistern - new Vector3(0, 0.05f, 0)));
        AddGroundedSupport(cistern - new Vector3(0, 0.2f, 0), 0.04f, 0.3f);
        AddLabel("injection cistern", cistern + new Vector3(0, 0.4f, 0), pixelSize: 0.004f);
        var iron = Surface("iron");
        var elbow = new Vector3(cistern.X, bottom.Y - 0.05f, cistern.Z);
        AddChild(Shapes.Rod(cistern - new Vector3(0, 0.2f, 0), elbow, 0.03f, iron));
        AddChild(Shapes.Rod(elbow, bottom - new Vector3(0, 0.05f, 0), 0.03f, iron));
        var spray = Particles(bottom + new Vector3(0, 0.02f, 0), new Color(0.4f, 0.6f, 1f, 0.6f), 0.02f,
                              Vector3.Up, rise: -9.8f, speed: 3.5f);
        AddChild(spray);
        return spray;
    }

    private static MeshInstance3D Place(MeshInstance3D mesh, Vector3 at) { mesh.Position = at; return mesh; }

    /// <summary>A plain particle stream: see-through balls of one colour, fired along dir, rising (or falling) as they go.</summary>
    private static GpuParticles3D Particles(Vector3 at, Color color, float size, Vector3 dir, float rise, float speed) => new()
    {
        Position = at,
        Amount = 24,
        Lifetime = 0.8,
        Emitting = false,
        LocalCoords = false,
        ProcessMaterial = new ParticleProcessMaterial
        {
            Direction = dir, Spread = 30f,
            InitialVelocityMin = speed * 0.6f, InitialVelocityMax = speed,
            Gravity = new Vector3(0, rise, 0),
            ScaleMin = 0.6f, ScaleMax = 1.5f,
        },
        DrawPass1 = new SphereMesh
        {
            Radius = size, Height = size * 2,
            Material = new StandardMaterial3D { AlbedoColor = color, Roughness = 1f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
        },
    };
}
