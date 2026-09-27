using Godot;
using HeroicInventions.Sim;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Builds the scene for any machine from its definition, and shows the
/// state of its <see cref="MachineRuntime"/> every tick. Nothing here is
/// specific to one machine: a new #lang heroic file needs no new C#.
/// </summary>
public partial class MachineView : Node3D
{
    public MachineRuntime Runtime { get; }
    public List<MaterialBlock> Blocks { get; } = [];

    private readonly MaterialLibrary _materials;
    private readonly List<(Tank tank, PartSpec spec, MeshInstance3D water)> _water = [];
    private readonly List<(Pipe pipe, Vector3 outlet, MeshInstance3D jet)> _jets = [];
    private readonly List<(Aeolipile rotor, Node3D node)> _rotors = [];
    private readonly List<(Boiler boiler, MeshInstance3D fire)> _fires = [];
    private readonly List<(Aeolipile rotor, GpuParticles3D puff)> _steamPuffs = []; // approximate — not a modelled steam flow, just where it exits
    private readonly List<RigidBody3D> _freezable = []; // every dynamic body: blocks, pendulums, levers
    // Local-space centre-of-mass offset for each body, for real potential
    // energy. Blocks are centred on their own origin (no entry needed —
    // GetValueOrDefault returns Vector3.Zero). Pendulums and levers place
    // their RigidBody3D's origin at the pivot instead, for the joint, so
    // their mass sits elsewhere in local space and needs the real offset.
    private readonly Dictionary<RigidBody3D, Vector3> _comOffset = [];
    private readonly Dictionary<string, RigidBody3D> _bodiesById = []; // for #:hang-from lookups
    private bool _manyIdenticalPendulums;
    private readonly List<(HingeJoint3D Joint, RigidBody3D Anchor, float ReleaseDeg, float StartDeg)> _releasable = [];
    private readonly List<(HingeJoint3D Joint, double DisableAt)> _motorTimeouts = [];
    private double? _initialMechanicalEnergy; // J, captured at rest — baseline for "energy retained"

    public MachineView(MachineRuntime runtime, MaterialLibrary materials)
    {
        Runtime = runtime;
        _materials = materials;
        Name = runtime.Def.Name;
    }

    // Godot needs a parameterless constructor to recreate scripts after a
    // C# assembly reload. Machine views are always rebuilt from files (R).
    public MachineView() : this(null!, null!) { }

    public override void _Ready()
    {
        if (Runtime is null) return;
        // Several identical, closely-spaced pendulums (Newton's cradle) —
        // labelling each one adds nothing (they're interchangeable) and
        // the text can't fit between them anyway. One machine title is enough.
        _manyIdenticalPendulums = Runtime.Def.Parts.Count(p => p.Kind == "pendulum") > 1;
        foreach (var part in Runtime.Def.Parts)
        {
            switch (part.Kind)
            {
                case "tank": BuildTank(part); break;
                case "boiler": BuildBoiler(part); break;
                case "rotor": BuildRotor(part); break;
                case "block": BuildBlock(part); break;
                case "pendulum": BuildPendulum(part); break;
                case "lever": BuildLever(part); break;
                case "ramp": BuildRamp(part); break;
            }
        }
        foreach (var pipe in Runtime.Def.Pipes) BuildPipe(pipe);
        Refresh();

        // Baseline for "energy retained": mechanical energy before anything
        // has moved (so, for a pendulum or trebuchet, whatever potential
        // energy its starting displacement already has).
        var e0 = Energy();
        _initialMechanicalEnergy = e0.KineticJ + e0.PotentialJ;
    }

    private static Vector3 V(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>
    /// Roughness driven by the material's real friction coefficient
    /// (0.30 bronze to 0.60 granite across the table) rather than a fixed
    /// per-category value — a low-friction surface reads as polished, a
    /// high-friction one as coarse, so the difference the μ label states
    /// is also something you can just look at. Metals additionally get
    /// metallic reflectance, since that's a real property of metal, not
    /// of friction.
    /// </summary>
    private StandardMaterial3D Surface(string materialId)
    {
        var mat = _materials[materialId];
        float roughness = Mathf.Clamp(0.12f + (float)(mat.Friction - 0.30) / 0.30f * 0.83f, 0.1f, 0.95f);
        return Shapes.Mat(Shapes.ColorFor(materialId),
                          metallic: mat.Category == MaterialCategory.Metal ? 0.8f : 0,
                          roughness: roughness);
    }

    /// <summary>
    /// A small floating name tag above a part — always faces the camera,
    /// so a scene of several similar boxes (Heron's fountain's three
    /// vessels, a row of material blocks) can be read at a glance instead
    /// of cross-referencing the Details panel.
    /// </summary>
    /// <summary>
    /// <paramref name="offset"/> is in <paramref name="parent"/>'s local
    /// space (default: this view, so effectively world space for static
    /// parts). For anything that moves — a block, a pendulum, a lever —
    /// pass the body itself as parent with a small local offset, so the
    /// label rides along automatically instead of staying behind at
    /// wherever the part started (which is what happened at first: a
    /// falling block's label stayed at its drop height, ending up
    /// off-camera once the block landed). Billboard mode keeps the text
    /// facing the camera regardless of the parent's own rotation.
    /// </summary>
    private void AddLabel(string text, Vector3 offset, Node3D? parent = null)
    {
        var label = new Label3D
        {
            Text = text,
            Position = offset,
            FontSize = 24,
            OutlineSize = 6,
            PixelSize = 0.0025f, // most of these parts are 10-30cm across; default pixel_size made text roughly life-sized
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true, // always readable, even behind glass or another part
        };
        (parent ?? this).AddChild(label);
    }

    private void BuildTank(PartSpec part)
    {
        float side = Mathf.Sqrt((float)part.Number("area"));
        float height = (float)part.Number("height");
        var shell = Shapes.Box(new Vector3(side, height, side),
                               Shapes.Mat(new Color(0.85f, 0.92f, 0.95f), roughness: 0.1f, alpha: 0.18f));
        shell.Position = V(part.At) + new Vector3(0, height / 2, 0);
        AddChild(shell);

        var water = Shapes.Box(new Vector3(side * 0.96f, 1, side * 0.96f),
                               Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.8f));
        AddChild(water);
        _water.Add((Runtime.Tanks[part.Id], part, water));
        AddLabel(part.Id, V(part.At) + new Vector3(0, height + 0.06f, 0));

        // A tank raised above the ground (Heron's fountain's three
        // vessels all are) otherwise just floats in mid-air with nothing
        // visibly holding it up — a support post grounds it, and reads
        // as part of one connected apparatus instead of a loose box.
        AddGroundedSupport(V(part.At), side * 0.12f, side * 0.5f);
    }

    /// <summary>
    /// A post from the ground up to a raised point, plus a small footing
    /// pad — anything held up in mid-air (a tank, a pendulum's pivot, a
    /// lever's fulcrum) otherwise just floats there with nothing visibly
    /// holding it up. Skipped near ground level, where it would be a
    /// stub too short to see.
    /// </summary>
    private void AddGroundedSupport(Vector3 point, float postRadius, float footingSize)
    {
        if (point.Y <= 0.05f) return;
        var wood = Surface("oak");
        AddChild(Shapes.Rod(new Vector3(point.X, 0, point.Z), point, postRadius, wood));
        var footing = Shapes.Box(new Vector3(footingSize, 0.03f, footingSize), Surface("granite"));
        footing.Position = new Vector3(point.X, 0.015f, point.Z);
        AddChild(footing);
    }

    /// <summary>
    /// A small particle puffer at a nozzle tip: not a modelled gas flow,
    /// just soft white puffs that spawn, drift upward, and fade — enough
    /// to read as "steam is venting here" at a glance, visible only while
    /// it actually is (toggled via GpuParticles3D.Emitting in Refresh).
    /// LocalCoords=false so spawned puffs drift in world space rather
    /// than being dragged around by the spinning rotor they came from.
    /// </summary>
    private GpuParticles3D BuildSteamPuffs(Aeolipile rotor, Vector3 localPosition)
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1, 1, 1, 0.55f));
        gradient.AddPoint(1f, new Color(1, 1, 1, 0f)); // fades to nothing over its lifetime

        var process = new ParticleProcessMaterial
        {
            Direction = new Vector3(0, 1, 0),
            Spread = 35f,
            InitialVelocityMin = 0.15f,
            InitialVelocityMax = 0.4f,
            Gravity = new Vector3(0, 0.35f, 0), // steam is buoyant, so it drifts up rather than falls
            ScaleMin = 0.6f,
            ScaleMax = 1.5f,
            ColorRamp = new GradientTexture1D { Gradient = gradient },
        };

        var particles = new GpuParticles3D
        {
            Position = localPosition,
            Amount = 14,
            Lifetime = 0.9,
            Emitting = false, // Refresh() turns this on only while steam is actually flowing
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = new SphereMesh { Radius = 0.012f, Height = 0.024f, Material = Shapes.Mat(Colors.White, roughness: 1f) },
        };
        _steamPuffs.Add((rotor, particles));
        return particles;
    }

    private Vector3 PortPosition(PortRef r)
    {
        var spec = Runtime.Def.Part(r.Part)!;
        return V(spec.At) + new Vector3(0, (float)spec.Port(r.Port, null).Height, 0);
    }

    private void BuildPipe(PipeSpec pipe)
    {
        var bronze = Surface("bronze");
        var from = PortPosition(pipe.From);
        var to = PortPosition(pipe.To);
        AddChild(Shapes.Rod(from, to, 0.006f, bronze));
        if (!pipe.Jet) return;

        // Thicker and brighter than the still water elsewhere, with a
        // slight glow — this is the one thing in the whole machine meant
        // to visibly catch the eye and change, so it needs to read as
        // "moving water", not blend in as another translucent surface.
        var jetMat = Shapes.Mat(new Color(0.35f, 0.75f, 1.0f), roughness: 0.15f, alpha: 0.9f);
        jetMat.EmissionEnabled = true;
        jetMat.Emission = new Color(0.3f, 0.7f, 1.0f);
        jetMat.EmissionEnergyMultiplier = 0.6f;
        var jet = Shapes.Cylinder(0.016f, 1, jetMat);
        AddChild(jet);
        _jets.Add((Runtime.Pipes[pipe.Id], to, jet));
    }

    private void BuildBoiler(PartSpec part)
    {
        float radius = (float)part.Number("radius");
        float height = (float)part.Number("height");
        var body = Shapes.Cylinder(radius, height, Surface(part.Material));
        body.Position = V(part.At) + new Vector3(0, height / 2, 0);
        AddChild(body);

        var fire = Shapes.Box(new Vector3(radius * 1.8f, 0.05f, radius * 1.8f), Shapes.Mat(new Color(1f, 0.45f, 0.1f)));
        fire.Position = V(part.At) + new Vector3(0, -0.03f, 0);
        AddChild(fire);
        _fires.Add((Runtime.Boilers[part.Id], fire));
        AddLabel(part.Id, V(part.At) + new Vector3(0, height + 0.06f, 0));
    }

    private void BuildRotor(PartSpec part)
    {
        var surface = Surface(part.Material);
        float radius = (float)part.Number("radius");
        float arm = (float)part.Number("arm");
        var axle = V(part.At);

        // Two hollow posts carry steam from the boiler's lid up to the axle ends.
        var boiler = Runtime.Def.Part(Runtime.BoilerFor(part.Id))!;
        float lid = (float)(boiler.At.Y + boiler.Number("height"));
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * (radius + 0.03f);
            AddChild(Shapes.Rod(new Vector3(axle.X + x, lid, axle.Z), axle + new Vector3(x, 0, 0), 0.008f, surface));
            AddChild(Shapes.Rod(axle + new Vector3(x, 0, 0), axle + new Vector3(side * radius, 0, 0), 0.006f, surface));
        }

        // The rotor spins about the X axle. Nozzle arms point along ±Y and
        // bend along ±Z, so both jets push the same way around.
        var node = new Node3D { Position = axle };
        AddChild(node);
        node.AddChild(Shapes.Sphere(radius, surface));
        var rotor = Runtime.Rotors[part.Id];
        foreach (int s in new[] { 1, -1 })
        {
            node.AddChild(Shapes.Rod(new Vector3(0, s * radius, 0), new Vector3(0, s * arm, 0), 0.006f, surface));
            node.AddChild(Shapes.Rod(new Vector3(0, s * arm, 0), new Vector3(0, s * arm, s * 0.025f), 0.006f, surface));
            node.AddChild(BuildSteamPuffs(rotor, new Vector3(0, s * arm, s * 0.05f)));
        }
        _rotors.Add((rotor, node));
        AddLabel(part.Id, axle + new Vector3(0, radius + arm + 0.05f, 0));
    }

    private void BuildBlock(PartSpec part)
    {
        float size = (float)part.Number("size");
        var block = new MaterialBlock(_materials[part.Material], size, Shapes.ColorFor(part.Material))
        {
            Name = part.Id,
            Position = V(part.At),
            Freeze = true,
        };
        AddChild(block);
        // Parented to the block, so the label follows it — otherwise a
        // block that falls (most of them do) leaves its label behind at
        // the drop height, off-camera once the block has landed.
        // The material name alone, not the full id — several blocks are
        // often placed close together (a row of material samples, a
        // ramp's cargo), and the longer text just overlapped its neighbours.
        // Friction coefficient alongside the name — on the inclined plane
        // in particular, "why did this one slide further?" otherwise has
        // no visible answer besides the end result.
        // Two lines, not one wide line — several blocks are often close
        // together (material samples, a ramp's cargo) and a single wide
        // label overlaps its neighbours long before the text gets small.
        string materialName = char.ToUpper(part.Material[0]) + part.Material[1..];
        AddLabel($"{materialName}\nμ{_materials[part.Material].Friction:F2}", new Vector3(0, size / 2 + 0.05f, 0), block);
        Blocks.Add(block);
        _freezable.Add(block);
        _bodiesById[part.Id] = block;

        // #:hang-from <part>: a free hinge to another body instead of
        // resting on it by friction, so this block stays hanging straight
        // down under gravity as that body rotates — a trebuchet's
        // counterweight, not cargo riding loose on a moving ramp.
        if (part.Props.GetValueOrDefault("hang-from") is SSymbol hangFrom
            && _bodiesById.TryGetValue(hangFrom.Name, out var anchor))
        {
            var joint = new HingeJoint3D { Position = V(part.At) };
            AddChild(joint);
            joint.NodeA = joint.GetPathTo(anchor);
            joint.NodeB = joint.GetPathTo(block);

            // #:release-past-deg: a stand-in for a sling's release hook —
            // once the anchor (the arm) has swung this far from where it
            // started, the hinge is freed and the block flies off on
            // whatever velocity it has, instead of staying attached
            // through the whole swing or (with no joint at all) just
            // sliding off early like loose cargo.
            double releaseDeg = part.Number("release-past-deg", double.PositiveInfinity);
            if (double.IsFinite(releaseDeg))
                _releasable.Add((joint, anchor, (float)releaseDeg, anchor.RotationDegrees.Z));
        }
    }

    /// <summary>
    /// A compound pendulum: a rod hanging from a fixed world anchor at
    /// <see cref="PartSpec.At"/>, with a bob at its far end. Mass comes
    /// from the material's real density over the rod+bob volume; Jolt
    /// derives the actual moment of inertia from those shapes, so this is
    /// genuine physical-pendulum dynamics, not an idealised point mass.
    /// </summary>
    private void BuildPendulum(PartSpec part)
    {
        float length = (float)part.Number("length");
        float startAngle = (float)part.Number("start-angle-deg");
        const float rodRadius = 0.01f;
        float bobRadius = Mathf.Max(0.03f, length * 0.08f);
        var mat = _materials[part.Material];
        var surface = Surface(part.Material);

        double rodVolume = Math.PI * rodRadius * rodRadius * length;
        double bobVolume = 4.0 / 3.0 * Math.PI * Math.Pow(bobRadius, 3);
        var body = new RigidBody3D
        {
            Name = part.Id,
            Position = V(part.At), // the pivot: body rotates about its own origin
            Mass = (float)mat.MassOf(rodVolume + bobVolume),
        };
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = rodRadius, Height = length }, Position = new Vector3(0, -length / 2, 0) });
        body.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = bobRadius }, Position = new Vector3(0, -length, 0) });
        var rod = Shapes.Cylinder(rodRadius, length, surface);
        rod.Position = new Vector3(0, -length / 2, 0);
        body.AddChild(rod);
        var bob = Shapes.Sphere(bobRadius, surface);
        bob.Position = new Vector3(0, -length, 0);
        body.AddChild(bob);
        AddChild(body);
        _freezable.Add(body);
        // Mass-weighted combination of the rod's centre (-L/2) and the
        // bob's centre (-L) — the real centre of mass, for real PE.
        _comOffset[body] = new Vector3(0, (float)((-length / 2 * rodVolume - length * bobVolume) / (rodVolume + bobVolume)), 0);

        // Released from start-angle-deg off vertical; a HingeJoint3D with
        // no NodeA pins the other end to the world at this joint's transform.
        body.RotationDegrees = new Vector3(0, 0, startAngle);
        var joint = new HingeJoint3D { Position = V(part.At) };
        AddChild(joint);
        joint.NodeB = joint.GetPathTo(body);
        // Parented to the swinging body itself (not the fixed pivot point),
        // near the bob — the pivot is often near the top of the camera's
        // frame or crowded (Newton's cradle has five side by side), while
        // the bob is the part actually worth pointing at.
        if (!_manyIdenticalPendulums)
            AddLabel(part.Id, new Vector3(0, -length + bobRadius + 0.06f, 0), body);
        AddGroundedSupport(V(part.At), 0.015f, 0.08f);
    }

    /// <summary>
    /// A lever/see-saw: a beam hinged at its centre. Rest `block` parts on
    /// the ends elsewhere in the machine file — the torque balance is
    /// Jolt's own contact physics acting on each material's real mass,
    /// not a hand-written lever equation.
    /// </summary>
    private void BuildLever(PartSpec part)
    {
        float length = (float)part.Number("length");
        float startAngle = (float)part.Number("start-angle-deg");
        float pivotFraction = (float)part.Number("pivot-fraction", 0.5);
        float limitDeg = (float)part.Number("limit-deg", 18);
        float damping = (float)part.Number("damping", 8.0);
        // A see-saw plank has to be wider than whatever rides on it, or a
        // block overhangs the edge and rolls off sideways once it tilts.
        // Kept thin: for an off-centre pivot (a trebuchet arm), a uniform
        // beam's own weight is biased toward its longer side by simple
        // geometry (more material there) — thick enough, its own self-
        // weight can out-torque the counterweight and swing the wrong way
        // entirely, which is exactly what happened before this was 0.04.
        const float thickness = 0.025f, depth = 0.22f;
        var mat = _materials[part.Material];
        var surface = Surface(part.Material);

        // pivot-fraction moves the hinge along the beam: 0.5 centres it
        // (a see-saw); nearer 0 or 1 gives a short arm and a long arm (a
        // trebuchet). The beam's own visual/collision centre sits offset
        // from the pivot (the body's origin) to match.
        float centerOffset = (0.5f - pivotFraction) * length;

        var body = new RigidBody3D
        {
            Name = part.Id,
            Position = V(part.At), // the pivot
            Mass = (float)mat.MassOf(length * thickness * depth),
            // A real pivot has bearing friction; without any damping a
            // see-saw snaps to its limit faster than a resting block can
            // settle onto the rising end, and it tumbles off instead.
            AngularDamp = damping,
        };
        var beamOffset = new Vector3(centerOffset, 0, 0);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(length, thickness, depth) }, Position = beamOffset });
        var beam = Shapes.Box(new Vector3(length, thickness, depth), surface);
        beam.Position = beamOffset;
        body.AddChild(beam);
        AddChild(body);
        _freezable.Add(body);
        _comOffset[body] = beamOffset; // the beam is uniform, so its own centroid is its centre of mass
        _bodiesById[part.Id] = body;

        body.RotationDegrees = new Vector3(0, 0, startAngle);
        var joint = new HingeJoint3D { Position = V(part.At) };
        AddChild(joint);
        joint.NodeB = joint.GetPathTo(body);

        // A real see-saw has mechanical stops (without one, the low end
        // just keeps rotating until it hits the floor); a trebuchet arm
        // instead wants to swing through most of its arc, so its .rkt
        // file passes a much larger #:limit-deg.
        joint.SetFlag(HingeJoint3D.Flag.UseLimit, true);
        joint.SetParam(HingeJoint3D.Param.LimitUpper, Mathf.DegToRad(limitDeg));
        joint.SetParam(HingeJoint3D.Param.LimitLower, Mathf.DegToRad(-limitDeg));

        // A torsion catapult's arm starts already moving — see the
        // #:initial-spin-deg-per-sec doc comment in machine.rkt. Directly
        // assigning RigidBody3D.AngularVelocity had no measurable effect
        // (the joint's own constraint solving seems to treat it as drift
        // and correct it away); driving it briefly through the hinge's
        // own motor — a real, supported feature for exactly this — works.
        float initialSpin = (float)part.Number("initial-spin-deg-per-sec", 0);
        if (initialSpin != 0)
        {
            joint.SetFlag(HingeJoint3D.Flag.EnableMotor, true);
            // Negated: the motor's positive direction runs opposite to
            // RotationDegrees.Z's own sign (confirmed against the
            // trebuchet, where positive rotZ is what raises the long/far
            // side — a positive #:initial-spin-deg-per-sec should mean
            // "throw the far end up", so it needs the opposite sign here.
            joint.SetParam(HingeJoint3D.Param.MotorTargetVelocity, -Mathf.DegToRad(initialSpin));
            joint.SetParam(HingeJoint3D.Param.MotorMaxImpulse, 500);
            _motorTimeouts.Add((joint, Runtime.Time + 0.08)); // a brief pulse, then it coasts on momentum

            // A visible stand-in for the twisted sinew skein the arm is
            // actually anchored to — without this, a torsion catapult's
            // pivot looks like a bare hinge with no hint of what powers
            // it, unlike the trebuchet where the counterweight itself
            // makes the mechanism obvious at a glance. Two washers
            // (the skein's real frames) with a wound rope-textured drum
            // between them, crossing the arm at the pivot.
            var rope = Surface("hemp");
            var skein = Shapes.Cylinder(0.05f, 0.16f, rope);
            skein.RotationDegrees = new Vector3(0, 0, 90);
            body.AddChild(skein); // child of the arm: twists with it, as the real skein does
            foreach (float side in new[] { -0.08f, 0.08f })
            {
                var washer = Shapes.Cylinder(0.065f, 0.015f, Surface("oak"));
                washer.RotationDegrees = new Vector3(0, 0, 90);
                washer.Position = new Vector3(side, 0, 0);
                body.AddChild(washer);
            }
        }
        // Parented to the beam so the label tilts with it. Local (0,…,0)
        // is the pivot — body's own origin — which stays a sensible label
        // spot regardless of #:pivot-fraction, unlike the beam's own
        // (possibly far off-centre) visual midpoint.
        AddLabel(part.Id, new Vector3(0, thickness + 0.08f, 0), body);
        // A wider footing than a pendulum's — a lever's fulcrum takes a
        // real sideways load (the beam pushes on it, unlike a pendulum
        // hanging straight down), and for a trebuchet's tall pivot this
        // also reads as the tower/frame a real one is mounted on.
        AddGroundedSupport(V(part.At), 0.03f, 0.18f);
    }

    /// <summary>
    /// A static, immovable ramp. Needs no new mechanics: whatever slides
    /// or sticks does so purely from the existing material friction table.
    /// </summary>
    private void BuildRamp(PartSpec part)
    {
        float length = (float)part.Number("length");
        float width = (float)part.Number("width");
        float angleDeg = (float)part.Number("angle-deg");
        float angle = Mathf.DegToRad(angleDeg);
        const float thickness = 0.05f;

        // part.At is the ramp's low edge at ground level; it rises going
        // toward -Z. Computing the slab's centre directly in world space
        // (rather than an offset inside a rotated local frame) keeps this
        // easy to check by hand: at length L and angle a, the centre sits
        // L/2 up and L/2·cos(a) back from the base.
        var center = V(part.At) + new Vector3(0, length / 2 * Mathf.Sin(angle), -length / 2 * Mathf.Cos(angle));
        var body = new StaticBody3D { Position = center, RotationDegrees = new Vector3(angleDeg, 0, 0) };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(width, thickness, length) } });
        body.AddChild(Shapes.Box(new Vector3(width, thickness, length), Surface(part.Material)));
        AddChild(body);
        AddLabel(part.Id, V(part.At) + new Vector3(0, 0.1f, 0));
    }

    public void Simulate(double dt)
    {
        Runtime.Step(dt);
        Refresh();
        CheckReleases();
    }

    private void CheckReleases()
    {
        for (int i = _releasable.Count - 1; i >= 0; i--)
        {
            var (joint, anchor, releaseDeg, startDeg) = _releasable[i];
            if (Mathf.Abs(anchor.RotationDegrees.Z - startDeg) < releaseDeg) continue;
            joint.QueueFree(); // frees the constraint; the block keeps its current velocity as a projectile
            _releasable.RemoveAt(i);
        }
        for (int i = _motorTimeouts.Count - 1; i >= 0; i--)
        {
            var (joint, disableAt) = _motorTimeouts[i];
            if (Runtime.Time < disableAt) continue;
            joint.SetFlag(HingeJoint3D.Flag.EnableMotor, false); // the spring's energy is spent; coast on momentum from here
            _motorTimeouts.RemoveAt(i);
        }
    }

    /// <summary>Rotation and height of every dynamic body — a quick way to confirm Jolt is actually moving them (see HEROIC_DEBUG_PHYSICS).</summary>
    public string DebugState() =>
        string.Join("  ", _freezable.Select(b => $"{b.Name} pos=({b.GlobalPosition.X:F2},{b.GlobalPosition.Y:F2},{b.GlobalPosition.Z:F2}) rotZ={b.RotationDegrees.Z:F1}°"));

    private void Refresh()
    {
        foreach (var (tank, spec, water) in _water)
        {
            float level = Mathf.Max((float)tank.Level, 0.001f);
            water.Scale = new Vector3(1, level, 1);
            water.Position = V(spec.At) + new Vector3(0, level / 2, 0);
        }
        foreach (var (pipe, outlet, jet) in _jets)
        {
            float height = pipe.Flow > 0 ? (float)pipe.JetHeight : 0;
            jet.Visible = height > 0.002f;
            jet.Scale = new Vector3(1, Mathf.Max(height, 0.001f), 1);
            jet.Position = outlet + new Vector3(0, height / 2, 0);
        }
        foreach (var (rotor, node) in _rotors)
            node.Rotation = new Vector3((float)rotor.Angle, 0, 0);
        foreach (var (boiler, fire) in _fires)
            fire.Visible = boiler.HeatInput > 0 && !boiler.IsDry;
        foreach (var (rotor, puff) in _steamPuffs)
            puff.Emitting = rotor.SteamFlow > 1e-6;
    }

    public void ToggleFire()
    {
        foreach (var (boiler, _) in _fires)
            boiler.HeatInput = boiler.HeatInput > 0 ? 0 : 3000;
    }

    public void SetFrozen(bool frozen)
    {
        foreach (var b in _freezable) b.Freeze = frozen;
    }

    /// <summary>The full raw-numbers readout — every tank, boiler, rotor and block. Verbose on purpose; see <see cref="EnergyHud"/> for the headline view.</summary>
    public string Details
    {
        get
        {
            var bits = new List<string>();
            foreach (var (id, t) in Runtime.Tanks) bits.Add($"{id} {t.WaterVolume * 1000:F1} L");
            foreach (var air in Runtime.AirPockets) bits.Add($"air {air.GaugePressure / 1000:F2} kPa");
            foreach (var (pipe, _, _) in _jets) bits.Add($"{pipe.Name} jet {(pipe.Flow > 0 ? pipe.JetHeight * 100 : 0):F1} cm");
            foreach (var (id, b) in Runtime.Boilers)
                bits.Add($"{id} {b.Temperature:F1} °C {b.GaugePressure / 1000:F1} kPa, fire {(b.HeatInput > 0 ? "on" : "off")}");
            foreach (var (id, r) in Runtime.Rotors) bits.Add($"{id} {r.Rpm:F0} rpm");
            foreach (var b in Blocks) bits.Add($"{b.Name} {b.Material.Name} {b.Mass:F1} kg");
            return string.Join(" · ", bits);
        }
    }

    // ------------------------------------------------------------ energy

    public readonly record struct EnergySummary(
        double KineticJ, double PotentialJ, double ThermalDeliveredJ,
        string SpeedLabel, double? EfficiencyPercent, double? RetainedPercent);

    /// <summary>
    /// A small, curated set of headline numbers — the point is "what kind
    /// of energy is in this machine and where did it go", not a complete
    /// state dump. Kinetic and thermal energy are exact where the sim core
    /// tracks them directly (the aeolipile's rotor, a boiler's cumulative
    /// heat); for plain Jolt bodies (blocks, pendulums, levers), kinetic
    /// energy is ½mv² from each body's centre-of-mass velocity only — a
    /// reasonable approximation that omits each body's own spin about its
    /// centre, so it understates the true total somewhat.
    /// </summary>
    public EnergySummary Energy()
    {
        double rotorKe = Runtime.Rotors.Values.Sum(r => r.KineticEnergy);
        double bodyKe = _freezable.Sum(b => 0.5 * b.Mass * b.LinearVelocity.LengthSquared());
        // The real centre of mass, not the body's own origin: a pendulum's
        // RigidBody3D origin sits fixed at the pivot for the joint, so its
        // Y never changes — using it directly would make PE constant and
        // silently ignore the entire swing.
        double pe = _freezable.Sum(b =>
            b.Mass * (float)Physics.Gravity * (b.GlobalTransform * _comOffset.GetValueOrDefault(b, Vector3.Zero)).Y);
        // Water has real gravitational PE too — without this, a fluid
        // machine like Heron's fountain (no rigid bodies, no rotor) shows
        // zero energy and a blank speed the whole time it's running.
        pe += Runtime.Tanks.Values.Sum(t => t.WaterVolume * Physics.WaterDensity * Physics.Gravity * (t.BaseElevation + t.Level / 2));
        double thermal = Runtime.Boilers.Values.Sum(b => b.HeatDelivered);

        string speed = Runtime.Rotors.Count > 0
            ? $"{Runtime.Rotors.Values.First().Rpm:F0} rpm"
            : _freezable.Count > 0
                ? $"{_freezable.Max(b => b.LinearVelocity.Length()):F2} m/s"
                : Runtime.Pipes.Count > 0
                    ? $"{Runtime.Pipes.Values.Max(p => Math.Abs(p.Flow)) * 1000:F2} L/s"
                    : "—";

        double? efficiency = Runtime.Boilers.Count > 0 && Runtime.Rotors.Count > 0 && thermal > 1e-6
            ? rotorKe / thermal * 100
            : null;

        double? retained = Runtime.Boilers.Count == 0 && _initialMechanicalEnergy is { } init && init > 1e-6
            ? (rotorKe + bodyKe + pe) / init * 100
            : null;

        return new EnergySummary(rotorKe + bodyKe, pe, thermal, speed, efficiency, retained);
    }

    public static string FormatJoules(double j)
    {
        double a = Math.Abs(j);
        return a switch
        {
            >= 1e6 => $"{j / 1e6:F2} MJ",
            >= 1e3 => $"{j / 1e3:F2} kJ",
            >= 1 => $"{j:F2} J",
            >= 1e-3 => $"{j * 1e3:F2} mJ",
            _ => $"{j * 1e6:F2} µJ",
        };
    }

    public static string FormatPercent(double p) =>
        Math.Abs(p) >= 0.01 ? $"{p:F2}%" : Math.Abs(p) >= 0.0001 ? $"{p:F4}%" : $"{p:E1}%";

    /// <summary>The energy dashboard as 3–4 lines: total mechanical energy and its kinetic/potential mix, heat delivered (if any), speed, and whichever of efficiency/retained energy applies.</summary>
    public string EnergyHud()
    {
        var e = Energy();
        double mech = e.KineticJ + e.PotentialJ;
        var lines = new List<string>
        {
            mech > 1e-9
                ? $"Energy: {FormatJoules(mech)} mechanical ({e.KineticJ / mech * 100:F0}% kinetic, {e.PotentialJ / mech * 100:F0}% potential)"
                : $"Energy: {FormatJoules(mech)} mechanical",
        };
        if (e.ThermalDeliveredJ > 0) lines.Add($"Heat delivered: {FormatJoules(e.ThermalDeliveredJ)}");
        lines.Add($"Speed: {e.SpeedLabel}");
        if (e.EfficiencyPercent is { } eff) lines.Add($"Efficiency: {FormatPercent(eff)} of heat became motion");
        if (e.RetainedPercent is { } ret) lines.Add($"Energy retained: {ret:F0}% of its starting mechanical energy");
        return string.Join("\n", lines);
    }
}
