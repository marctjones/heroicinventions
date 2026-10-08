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
    private readonly Dictionary<Tank, MeshInstance3D> _ice = [];
    private readonly Dictionary<Tank, MeshInstance3D> _tankShells = [];
    private readonly List<(Pipe pipe, Vector3 outlet, MeshInstance3D jet)> _jets = [];
    private readonly List<(Aeolipile rotor, Node3D node)> _rotors = [];
    private readonly List<(Boiler boiler, MeshInstance3D fire, StandardMaterial3D glow, GpuParticles3D flame)> _fires = [];
    private readonly List<(Func<double> steamFlow, GpuParticles3D puff)> _steamPuffs = []; // approximate — not a modelled steam flow, just where it exits
    private readonly List<RigidBody3D> _freezable = []; // every dynamic body: blocks, pendulums, levers
    // Local-space centre-of-mass offset for each body, for real potential
    // energy. Blocks are centred on their own origin (no entry needed —
    // GetValueOrDefault returns Vector3.Zero). Pendulums and levers place
    // their RigidBody3D's origin at the pivot instead, for the joint, so
    // their mass sits elsewhere in local space and needs the real offset.
    private readonly Dictionary<RigidBody3D, Vector3> _comOffset = [];
    private readonly Dictionary<string, RigidBody3D> _bodiesById = []; // parts by name, for ropes, gears and lifts
    private bool _manyIdenticalPendulums;
    // Pendulum bobs strike each other through ResolveBobImpacts, not Jolt.
    private readonly List<(RigidBody3D Body, float Length, float BobRadius, float Inertia, float Restitution)> _pendulums = [];
    private const uint PendulumLayer = 4;
    // Wheels and screws: every body turning on a fixed axle, with the
    // axle's world direction and extent — for supports, rpm and spin energy.
    private readonly List<(RigidBody3D Body, Vector3 Axis, float HalfLength, float Radius, bool Driven, string Label)> _axles = [];
    private double? _initialMechanicalEnergy; // J, captured at rest — baseline for "energy retained"

    /// <summary>
    /// The nodes each part built, by part id: what the build-mode editor
    /// picks, highlights and drags, so it works on exactly what runs.
    /// Supports, pipes, ropes and frames added after the parts belong to
    /// no single part and aren't listed.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Node3D>> PartNodes => _partNodes;

    /// <summary>A part's moving body, by id (a thrown stone, a bolt), for the camera to follow.</summary>
    public RigidBody3D? BodyNamed(string id) => _bodiesById.GetValueOrDefault(id);
    private readonly Dictionary<string, IReadOnlyList<Node3D>> _partNodes = [];

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
        _manyIdenticalPendulums = Runtime.Def.Parts.Count(p => p.Kind == "pendulum" && !Runtime.Pendulums.ContainsKey(p.Id)) > 1;
        ChildEnteredTree += TagBuilt;
        foreach (var part in Runtime.Def.Parts)
        {
            int before = GetChildCount();
            _building = part.Id;
            switch (part.Kind)
            {
                case "tank": BuildTank(part); break;
                case "boiler": BuildBoiler(part); break;
                case "rotor": BuildRotor(part); break;
                case "jetwheel": BuildJetWheel(part); break;
                case "smokejack": BuildSmokeJack(part); break;
                case "block": BuildBlock(part); break;
                case "ball": BuildBall(part); break;
                case "pendulum": BuildPendulum(part); break;
                case "lever": BuildLever(part); break;
                case "ramp": BuildRamp(part); break;
                case "wheel": BuildWheel(part); break;
                case "screw": BuildScrew(part); break;
                case "fixture": BuildFixture(part); break;
                case "piston": BuildPiston(part); break;
                case "post": BuildPost(part); break;
                case "hearth": BuildHearth(part); break;
                case "waterwheel": BuildWaterWheel(part); break;
                case "windmill": BuildWindmill(part); break;
                case "capstan": BuildCapstan(part); break;
                case "counterpoise": BuildCounterpoise(part); break;
                case "digger": BuildDigger(part); break;
                case "float": BuildFloat(part); break;
                case "sluice-box": BuildSluiceBox(part); break;
                case "galvanic-jar": BuildGalvanicJar(part); break;
            }
            _building = null;
            _partNodes[part.Id] = Enumerable.Range(before, GetChildCount() - before)
                .Select(i => GetChild(i)).OfType<Node3D>().ToList();
        }
        _pass = "BuildPipe";
        foreach (var pipe in Runtime.Def.Pipes) { _building = pipe.Id; BuildPipe(pipe); }
        _building = null;
        Pass(BuildPendulumFrames);
        Pass(BuildArbors);
        Pass(BuildGearTrains);
        Pass(BuildBelts);
        Pass(BuildGrips);
        Pass(BuildCams);
        Pass(BuildRatchets);
        Pass(BuildHoppers);
        Pass(BuildAxleSupports);
        _pass = "BuildRope";
        foreach (var rope in Runtime.Def.Ropes) { _building = rope.Id; BuildRope(rope); }
        _building = null;
        Pass(BuildLifts);
        BuildNorias();   // water in a noria's buckets (#171)
        Pass(BuildChannels);
        Pass(BuildFloatValves);
        Pass(BuildLeaks);
        Pass(BuildDrains);
        Pass(BuildTriggers);
        Pass(BuildSafetyValves);
        Pass(BuildBellows);
        Pass(BuildWarmth);
        Pass(BuildEnclosures);
        Pass(BuildDoors);
        Pass(BuildCrucibles);
        Pass(BuildHeatStores);
        Pass(BuildBimetals);
        Pass(BuildElectrics);
        Pass(BuildEnvelopes);
        Pass(BuildPanes);
        Pass(BuildRainHouse);
        Pass(BuildStirlings);
        Pass(BuildGreenhouse);
        BuildProducts();   // oxygen from plants (#173)
        Pass(BuildMirrors);
        Pass(BuildPumps);
        Pass(BuildPistonDrives);
        Pass(BuildGauges);   // after the boilers, cylinders, tanks and rooms it sits on (#170)
        Pass(BuildCarriedWheels);
        Pass(BuildMillstones);
        Pass(BuildAxleFriction);
        Pass(BuildJoints);
        Pass(BuildImpacts);
        Pass(BuildFracture);
        Pass(LetLooseBodiesMeetFixtures);
        Skins.FitJoints(this, Surface);   // a collar or ball wherever the physics joins two parts
        Skins.OrientGrain(this);
        Skins.PlankSeams(this);        // dark seams on wide wooden boxes (#102)
        Skins.RecedeStructure(this);
        BuildTurnMarks();   // a stripe on everything that turns, a blur ring above 15 rev/s (#172)
        Skins.ScreenLabels(this);      // every label the same size on screen, near or far (#148)
        // and every label added later (an impact's readout, a fracture's tag) the same way
        if (!_watchingNodes)
        {
            _watchingNodes = true;
            GetTree().NodeAdded += OnNodeAdded;
            TreeExiting += StopWatchingNodes;
        }
        Refresh();

        // Baseline for "energy retained": mechanical energy before anything
        // has moved (so, for a pendulum or trebuchet, whatever potential
        // energy its starting displacement already has).
        var e0 = Energy();
        _initialMechanicalEnergy = e0.KineticJ + e0.PotentialJ;
    }

    /// <summary>
    /// The part whose builder is running right now. Every node added to the view while this is set is tagged
    /// with it, so no builder has to remember to (#151).
    /// </summary>
    private string? _building;

    private string _pass = "parts";

    private void Pass(Action build) { _building = null; _pass = build.Method.Name; build(); _building = null; _pass = "after"; }

    private void TagBuilt(Node node)
    {
        if (_building is { } id && !node.HasMeta("part_id") && !node.HasMeta("scenery")) node.SetMeta("part_id", id);
        else node.SetMeta("built_by", _pass);   // for the audit: which pass made a node that no part owns
    }

    /// <summary>Marks a node as belonging to no part (ground, light, a scale figure): the audit skips it, a click passes through it.</summary>
    public static void MarkScenery(Node node) => node.SetMeta("scenery", true);

    /// <summary>
    /// Everything drawn or built for a part, for highlighting: the nodes tagged with its id (a body carries its own
    /// meshes below it). A rope, pipe, channel or joint is a part of the machine too, under its own id.
    /// </summary>
    public IEnumerable<Node3D> NodesOf(string partId) =>
        GetChildren().OfType<Node3D>().Where(n => n.HasMeta("part_id") && n.GetMeta("part_id").AsString() == partId);

    /// <summary>Every mesh in the view, however deep, for the picker.</summary>
    public IEnumerable<MeshInstance3D> Meshes()
    {
        var stack = new Stack<Node>(GetChildren());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is MeshInstance3D mesh) yield return mesh;
            foreach (var child in node.GetChildren()) stack.Push(child);
        }
    }

    private static Vector3 V(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>
    /// A part's heading as a turn about the vertical (issue #83): the frame it is built in is the machine's own
    /// turned by this, so its body, axle and slope stand at the heading and its pivot stays where it was put.
    /// </summary>
    private bool _watchingNodes;

    /// <summary>Once only: a view built again, or taken out of the tree twice (the editor does both), would otherwise unhook a hook it no longer has.</summary>
    private void StopWatchingNodes()
    {
        if (!_watchingNodes) return;
        _watchingNodes = false;
        GetTree().NodeAdded -= OnNodeAdded;
        TreeExiting -= StopWatchingNodes;
    }

    private void OnNodeAdded(Node node)
    {
        if (node is Label3D label && IsAncestorOf(label)) Skins.ScreenLabel(label);
    }

    private static Basis YawOf(PartSpec part) => new(Vector3.Up, Mathf.DegToRad((float)MachineDef.HeadingOf(part)));

    /// <summary>The axis a wheel or screw turns on, before any heading: #:axis, raised #:tilt-deg.</summary>
    private static Vector3 AxleOf(PartSpec part)
    {
        float tilt = Mathf.DegToRad((float)part.Number("tilt-deg", 0));
        return part.Kind == "screw" ? new Vector3(Mathf.Cos(tilt), Mathf.Sin(tilt), 0)
            : part.Symbol("axis", "z") switch
            {
                "x" => new Vector3(Mathf.Cos(tilt), Mathf.Sin(tilt), 0),
                "y" => new Vector3(Mathf.Sin(tilt), Mathf.Cos(tilt), 0),
                _ => new Vector3(0, Mathf.Sin(tilt), Mathf.Cos(tilt)),
            };
    }

    /// <summary>The surface of anything made of <paramref name="materialId"/>: see <see cref="Skins.For"/>.</summary>
    private StandardMaterial3D Surface(string materialId) => Skins.For(_materials[materialId]);

    /// <summary>How a part's material behaves in contact: its friction and how much a collision gives back.</summary>
    private PhysicsMaterial ContactFor(string materialId) =>
        new() { Friction = (float)_materials[materialId].Friction, Bounce = (float)_materials[materialId].Restitution };

    /// <summary>
    /// A part's own surface: <see cref="Surface"/> for its material, its brightness nudged by its name
    /// (<see cref="Skins.Vary"/>) so neighbouring parts of one material don't merge into one shape.
    /// <paramref name="size"/> is no longer needed now the outline sizes itself on screen.
    /// </summary>
    private StandardMaterial3D PartSurface(PartSpec part, float size)
    {
        var mat = Surface(part.Material);
        Skins.Vary(mat, part.Id);
        return mat;
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
    private void AddLabel(string text, Vector3 offset, Node3D? parent = null, float pixelSize = 0.0025f)
    {
        var label = new Label3D
        {
            Text = text,
            Position = offset,
            FontSize = 24,
            OutlineSize = 6,
            PixelSize = pixelSize, // default suits 10-30cm parts; generated parts pass one scaled to their own size
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
                               Shapes.Glass());
        shell.Position = V(part.At) + new Vector3(0, height / 2, 0);
        AddChild(shell);
        // an iron frame on its twelve edges: a glass box reads as a vessel, not as a ghost of one, at any size and
        // whatever is behind it (#165). Children of the shell, so they move with a hung one.
        var frame = Surface("iron");
        float e = Mathf.Clamp(side * 0.04f, 0.004f, 0.03f), hs = side / 2, hh = height / 2;
        foreach (float a in new[] { -1f, 1f })
            foreach (float b in new[] { -1f, 1f })
            {
                shell.AddChild(Shapes.Rod(new Vector3(a * hs, -hh, b * hs), new Vector3(a * hs, hh, b * hs), e, frame));   // uprights
                shell.AddChild(Shapes.Rod(new Vector3(-hs, a * hh, b * hs), new Vector3(hs, a * hh, b * hs), e, frame));   // along X, top and bottom
                shell.AddChild(Shapes.Rod(new Vector3(b * hs, a * hh, -hs), new Vector3(b * hs, a * hh, hs), e, frame));   // along Z, top and bottom
            }

        Skins.HoopTank(shell, side, height, Surface("iron"));   // iron hoops, a few by its height (#102)
        Graduate(shell, part.Id, side / 2, side / 2, -height / 2, height, side * side);   // a scale on the wall, amber where the machine watches the level (#174)

        // nearly opaque: water held in a vessel must read against a pale sky through its glass (readable first)
        var water = Shapes.Box(new Vector3(side * 0.96f, 1, side * 0.96f),
                               Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.95f));
        AddChild(water);
        _water.Add((Runtime.Tanks[part.Id], part, water));
        // Ice is opaque and matt where water is clear and glossy: the eye reads the difference as solid
        var ice = Shapes.Box(new Vector3(side * 0.96f, 1, side * 0.96f), Shapes.Mat(new Color(0.96f, 0.98f, 1f), roughness: 0.75f));
        ice.Visible = false;
        AddChild(ice);
        _ice[Runtime.Tanks[part.Id]] = ice;
        _tankShells[Runtime.Tanks[part.Id]] = shell;
        bool hung = Runtime.Counterpoises.Values.Any(c => c.Vessel == Runtime.Tanks[part.Id]);
        if (hung)
        {
            AddLabel(part.Id, new Vector3(0, height / 2 + 0.06f, 0), shell);
            return;   // it hangs on its rope, not on a post
        }
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
    private GpuParticles3D BuildSteamPuffs(Func<double> steamFlow, Vector3 localPosition)
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
            DrawPass1 = new SphereMesh
            {
                Radius = 0.012f,
                Height = 0.024f,
                // alpha < 1 turns transparency on at all; VertexColorUseAsAlbedo
                // is what actually lets the per-particle ColorRamp fade (set
                // above) modulate it — without both, the puffs render fully
                // solid no matter what alpha the gradient specifies.
                Material = new StandardMaterial3D
                {
                    // A fixed low alpha, not relying on the per-particle
                    // ColorRamp to carry the transparency (that combined
                    // with VertexColorUseAsAlbedo still read as solid —
                    // this is the guaranteed-to-work version).
                    AlbedoColor = new Color(1, 1, 1, 0.3f),
                    Roughness = 1f,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                },
            },
        };
        _steamPuffs.Add((steamFlow, particles));
        return particles;
    }

    /// <summary>
    /// Small flickering embers rising off the fire — additive blending
    /// (each particle brightens what's behind it instead of just
    /// covering it) is what actually reads as "glowing", the way plain
    /// alpha transparency does for steam but wouldn't for fire.
    /// Visibility/emitting is tied to the boiler's HeatInput in Refresh.
    /// </summary>
    private GpuParticles3D BuildFlameParticles(Vector3 localPosition, float boilerRadius)
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1f, 0.9f, 0.3f)); // pale yellow at the base
        gradient.AddPoint(0.5f, new Color(1f, 0.45f, 0.05f)); // orange mid-rise
        gradient.AddPoint(1f, new Color(0.6f, 0.1f, 0.05f, 0f)); // fades out red

        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = boilerRadius * 0.7f,
            Direction = new Vector3(0, 1, 0),
            Spread = 20f,
            InitialVelocityMin = 0.2f,
            InitialVelocityMax = 0.5f,
            Gravity = new Vector3(0, 0.6f, 0), // rises faster than steam — flames, not vapour
            ScaleMin = 0.4f,
            ScaleMax = 1.0f,
            ColorRamp = new GradientTexture1D { Gradient = gradient },
        };

        return new GpuParticles3D
        {
            Position = localPosition,
            Amount = 18,
            Lifetime = 0.5,
            Emitting = false, // Refresh() turns this on only while the fire is lit
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = new SphereMesh
            {
                Radius = 0.015f,
                Height = 0.03f,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = Colors.White,
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.5f, 0.1f),
                    EmissionEnergyMultiplier = 1.5f,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                },
            },
        };
    }

    private Vector3 PortPosition(PortRef r)
    {
        var spec = Runtime.Def.Part(r.Part)!;
        return V(spec.At) + new Vector3(0, (float)spec.Port(r.Port, null).Height, 0);
    }

    private void BuildPipe(PipeSpec pipe)
    {
        var bronze = Surface("iron");   // dark, so the water dashes riding it read and the pipe stands off a pale ground (#171)
        var from = PortPosition(pipe.From);
        var to = PortPosition(pipe.To);
        AddChild(Shapes.Rod(from, to, PipeBore, bronze));
        BuildPipeFlow(pipe, from, to);   // dashes that ride it with the flow (#171)
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
        _boilerBodies[part.Id] = body;
        Skins.BandBoiler(body, radius, height, Surface(part.Material == "iron" || part.Material == "steel" ? "bronze" : "iron"));   // bands (#102)

        var firePos = V(part.At) + new Vector3(0, -0.03f, 0);
        var emberMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.35f, 0.05f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.4f, 0.05f),
            EmissionEnergyMultiplier = 2.5f,
        };
        var fire = Shapes.Box(new Vector3(radius * 1.8f, 0.05f, radius * 1.8f), emberMat);
        fire.Position = firePos;
        AddChild(fire);
        var flame = BuildFlameParticles(firePos, radius);
        AddChild(flame);
        _fires.Add((Runtime.Boilers[part.Id], fire, emberMat, flame));
        AddLabel(part.Id, V(part.At) + new Vector3(0, height + 0.06f, 0));
        // A boiler set up over its fire stands on stone piers round the
        // firebox, so the fire under it can be seen.
        if (part.At.Y > 0.05)
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Pi / 4 + i * Mathf.Pi / 2;
                var rim = V(part.At) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius * 0.85f;
                var pier = Shapes.Box(new Vector3(radius * 0.3f, (float)part.At.Y, radius * 0.3f), Surface("granite"));
                pier.Position = new Vector3(rim.X, (float)part.At.Y / 2, rim.Z);
                AddChild(pier);
            }
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
        node.AddChild(Shapes.Sphere(radius, Surface(part.Material)));   // its own surface: the turning mark rides on it (#172)
        var rotor = Runtime.Rotors[part.Id];
        foreach (int s in new[] { 1, -1 })
        {
            node.AddChild(Shapes.Rod(new Vector3(0, s * radius, 0), new Vector3(0, s * arm, 0), 0.006f, surface));
            node.AddChild(Shapes.Rod(new Vector3(0, s * arm, 0), new Vector3(0, s * arm, s * 0.025f), 0.006f, surface));
            node.AddChild(BuildSteamPuffs(() => rotor.SteamFlow, new Vector3(0, s * arm, s * 0.05f)));
        }
        _rotors.Add((rotor, node));
        AddLabel(part.Id, axle + new Vector3(0, radius + arm + 0.05f, 0));
    }

    private readonly List<(JetWheel wheel, Node3D node, MeshInstance3D jet)> _jetWheelViews = [];

    /// <summary>
    /// Branca's wheel: a hub on an axle along Z with flat paddles round its
    /// rim, and a spout piped from its boiler's lid that ends just short of
    /// the lowest paddle, aimed along +X so the jet drives the rim forward.
    /// The jet is drawn as a translucent tapering stream while steam flows.
    /// </summary>
    private void BuildJetWheel(PartSpec part)
    {
        var surface = Surface(part.Material);
        float radius = (float)part.Number("radius");
        float width = (float)part.Number("width", 0.03);
        int paddles = (int)part.Number("paddles", 8);
        var axle = V(part.At);
        var wheel = Runtime.JetWheels[part.Id];

        // the axle on two posts
        foreach (float side in new[] { -1f, 1f })
        {
            var postTop = axle + new Vector3(0, 0, side * (width + 0.03f));
            AddChild(Shapes.Rod(postTop with { Y = 0 }, postTop, 0.012f, Surface("oak")));
        }
        AddChild(Shapes.Rod(axle + new Vector3(0, 0, -(width + 0.03f)), axle + new Vector3(0, 0, width + 0.03f), 0.006f, surface));

        var node = new Node3D { Position = axle };
        AddChild(node);
        var hub = Shapes.Cylinder(radius * 0.18f, width * 0.8f, Surface(part.Material));   // its own surface: the turning mark rides on it (#172)
        hub.RotationDegrees = new Vector3(90, 0, 0);
        node.AddChild(hub);
        for (int i = 0; i < paddles; i++)
        {
            float a = Mathf.Tau * i / paddles;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
            node.AddChild(Shapes.Rod(dir * radius * 0.15f, dir * (radius - width / 2), 0.004f, surface));
            var paddle = Shapes.Box(new Vector3(width, 0.004f, width), surface);
            paddle.Position = dir * (radius - width / 2);
            paddle.Rotation = new Vector3(0, 0, a);
            node.AddChild(paddle);
        }

        // the spout: up from the boiler's lid, then across to just short of the lowest paddle
        var nozzle = axle + new Vector3(-0.07f, -radius + width / 2, 0);
        if (Runtime.Def.Part(Runtime.BoilerFor(part.Id)) is { } boiler)
        {
            var lid = V(boiler.At) + new Vector3(0, (float)boiler.Number("height"), 0);
            var bend = new Vector3(lid.X, nozzle.Y, lid.Z);
            AddChild(Shapes.Rod(lid, bend, 0.008f, Surface(boiler.Material)));
            AddChild(Shapes.Rod(bend, nozzle, 0.008f, Surface(boiler.Material)));
        }
        var jetMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.97f, 1f, 0.6f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var jet = Shapes.Rod(nozzle, nozzle + new Vector3(0.07f + width, 0, 0), 0.006f, jetMat);
        jet.Visible = false;
        AddChild(jet);
        AddChild(BuildSteamPuffs(() => wheel.SteamFlow, nozzle + new Vector3(0.07f + width, 0, 0)));
        _jetWheelViews.Add((wheel, node, jet));
        AddLabel(part.Id, axle + new Vector3(0, radius + 0.06f, 0));
    }

    private readonly List<(JetWheel jack, Node3D node)> _smokeJackViews = [];

    /// <summary>
    /// A smoke jack: an open-fronted brick chimney rising from its fire, and
    /// inside it a wheel of angled vanes on an upright axle, turned by the
    /// warm air going up (drawn as a faint shimmer while the fire burns).
    /// </summary>
    private void BuildSmokeJack(PartSpec part)
    {
        var jack = Runtime.JetWheels[part.Id];
        float radius = (float)part.Number("radius");
        float width = (float)part.Number("width", 0.06);
        int vanes = (int)part.Number("vanes", 6);
        float height = (float)part.Number("chimney-height", 2);
        var at = V(part.At);
        var fire = Runtime.Def.Part(part.Symbol("over", ""));
        var fireAt = fire is null ? at with { Y = 0 } : V(fire.At);

        // chimney: two brick walls round the flue (back and left), cut away on
        // the two sides the camera looks from, so the vanes inside show
        float half = radius + 0.06f;
        var brick = Surface("limestone");
        float bottom = fireAt.Y + 0.35f, top = fireAt.Y + height;
        foreach (var (offset, size) in new[]
        {
            (new Vector3(0, 0, -half), new Vector3(half * 2, top - bottom, 0.04f)),
            (new Vector3(-half, 0, 0), new Vector3(0.04f, top - bottom, half * 2)),
        })
        {
            var wall = Shapes.Box(size, brick);
            wall.Position = new Vector3(fireAt.X, (bottom + top) / 2, fireAt.Z) + offset;
            AddChild(wall);
        }

        // the vane wheel on an upright axle
        AddChild(Shapes.Rod(at + new Vector3(0, -0.1f, 0), at + new Vector3(0, 0.25f, 0), 0.006f, Surface("iron")));
        var node = new Node3D { Position = at };
        AddChild(node);
        var surface = Surface(part.Material);
        node.AddChild(Shapes.Cylinder(radius * 0.15f, 0.03f, surface));
        for (int i = 0; i < vanes; i++)
        {
            float a = Mathf.Tau * i / vanes;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var vane = Shapes.Box(new Vector3(radius * 0.85f, 0.004f, width), surface);
            vane.Position = dir * (radius * 0.55f);
            vane.Rotation = new Vector3(0, -a, 0);
            vane.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(35)); // pitched, so rising air turns it
            node.AddChild(vane);
        }
        _smokeJackViews.Add((jack, node));
        BuildSmokeJackWork(jack, fireAt, radius, top);   // the draught and the spit it turns (MachineView.Draughts.cs)
        AddChild(BuildSteamPuffs(() => jack.JetVelocity > 0.05 ? 1 : 0, at + new Vector3(0, -0.25f, 0)));
        AddLabel(part.Id, at + new Vector3(0, 0.3f, 0));
    }

    private void BuildBlock(PartSpec part)
    {
        float size = (float)part.Number("size");
        var dims = part.Props.ContainsKey("dim-x")
            ? new Vector3((float)part.Number("dim-x"), (float)part.Number("dim-y"), (float)part.Number("dim-z"))
            : Vector3.One * size;
        var block = new MaterialBlock(_materials[part.Material], dims, Shapes.ColorFor(part.Material))
        {
            Name = part.Id,
            Transform = new Transform3D(YawOf(part) * new Basis(Vector3.Right, Mathf.DegToRad((float)part.Number("tilt-deg", 0))), V(part.At)),
            Freeze = true,
        };
        foreach (var visual in block.GetChildren().OfType<MeshInstance3D>())
            visual.MaterialOverride = PartSurface(part, size);
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
        // A shaped block (a catapult bolt) is a part with a job, named for it;
        // a plain cube is a material sample, labelled with its friction.
        // A plain cube is labelled with what matters where it is: its friction
        // on a ramp (it decides whether it slides), its mass anywhere else (on
        // a lever it decides which side sinks; μ there meant nothing).
        if (part.Props.ContainsKey("dim-x"))
            AddLabel(part.Id, new Vector3(0, dims.Y / 2 + 0.05f, 0), block);
        else if (Runtime.Def.Parts.Any(p => p.Kind == "ramp"))
            AddLabel($"{materialName}\nμ{_materials[part.Material].Friction:F2}", new Vector3(0, size / 2 + 0.05f, 0), block);
        else
            AddLabel($"{materialName}\n{block.Mass:0.##} kg", new Vector3(0, size / 2 + 0.05f, 0), block);
        // swept along its path each step unless the machine says #:fast #f (MaterialBlock turns it on)
        block.ContinuousCd = part.Props.GetValueOrDefault("fast") is not SBool { Value: false };
        Blocks.Add(block);
        _freezable.Add(block);
        RegisterDrag(part, block, dims, false);
        _bodiesById[part.Id] = block;
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
        if (Runtime.Pendulums.TryGetValue(part.Id, out var onBearing))
        {
            BuildBearingPendulum(part, onBearing);
            return;
        }
        float length = (float)part.Number("length");
        float startAngle = (float)part.Number("start-angle-deg");
        var yaw = YawOf(part);
        const float rodRadius = 0.01f;
        float bobRadius = Mathf.Max(0.03f, length * 0.08f);
        var mat = _materials[part.Material];
        var surface = PartSurface(part, bobRadius * 2);

        double rodVolume = Math.PI * rodRadius * rodRadius * length;
        double bobVolume = 4.0 / 3.0 * Math.PI * Math.Pow(bobRadius, 3);
        var body = new RigidBody3D
        {
            Name = part.Id,
            Position = V(part.At), // the pivot: body rotates about its own origin
            Mass = (float)mat.MassOf(rodVolume + bobVolume),
            PhysicsMaterialOverride = ContactFor(part.Material),
            ContinuousCd = true, // the bob is fast at the bottom of the swing
            // Bobs meet each other through ResolveBobImpacts; everything
            // else (blocks, the floor) through Jolt as usual.
            CollisionLayer = PendulumLayer,
            CollisionMask = 1,
            CanSleep = false, // a slow swing near the bottom would otherwise freeze mid-arc
        };
        // About the pivot: a rod swinging from its end (mL²/3) plus a ball
        // at distance L (its own 2/5·mr² plus the parallel-axis mL²).
        double rodMass = mat.MassOf(rodVolume), bobMass = mat.MassOf(bobVolume);
        float inertia = (float)(rodMass * length * length / 3
                                + bobMass * (length * length + 0.4 * bobRadius * bobRadius));
        _pendulums.Add((body, length, bobRadius, inertia, (float)mat.Restitution));
        _bodiesById[part.Id] = body;
        _hinges[body] = (V(part.At), yaw * new Vector3(0, 0, 1));
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
        body.Basis = yaw * new Basis(new Vector3(0, 0, 1), Mathf.DegToRad(startAngle));
        var joint = new HingeJoint3D { Transform = new Transform3D(yaw, V(part.At)) };
        AddChild(joint);
        joint.NodeB = joint.GetPathTo(body);
        // Parented to the swinging body itself (not the fixed pivot point),
        // near the bob — the pivot is often near the top of the camera's
        // frame or crowded (Newton's cradle has five side by side), while
        // the bob is the part actually worth pointing at.
        if (!_manyIdenticalPendulums)
            AddLabel(part.Id, new Vector3(0, -length + bobRadius + 0.06f, 0), body);
        _pendulumMounts.Add((V(part.At), bobRadius, 0.06f, yaw, part.Id)); // hung from a frame beside the swing, built once all are placed
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
        float thickness = 0.025f, depth = 0.22f;
        if (part.Props.GetValueOrDefault("section") is SNumber section)
            thickness = depth = (float)section.Value; // a square beam, e.g. a catapult arm
        var mat = _materials[part.Material];
        var surface = PartSurface(part, depth);

        // pivot-fraction moves the hinge along the beam: 0.5 centres it
        // (a see-saw); nearer 0 or 1 gives a short arm and a long arm (a
        // trebuchet). The beam's own visual/collision centre sits offset
        // from the pivot (the body's origin) to match.
        float centerOffset = (0.5f - pivotFraction) * length;

        var yaw = YawOf(part);
        var axis0 = part.Symbol("axis", "z") switch { "x" => Vector3.Right, "y" => Vector3.Up, _ => new Vector3(0, 0, 1) };
        var axis = yaw * axis0;
        var toAxis = yaw * AxleBasis(axis0); // the beam is built turning about local Z; this turns that onto #:axis, at the heading
        float stiffness = (float)part.Number("spring-stiffness", 0);
        var body = new RigidBody3D
        {
            Name = part.Id,
            Transform = new Transform3D(toAxis, V(part.At)), // the pivot, level
            Mass = (float)mat.MassOf(length * thickness * depth),
            PhysicsMaterialOverride = ContactFor(part.Material),
            ContinuousCd = true, // a trebuchet arm's tip moves at tens of metres a second
            // A real pivot has bearing friction; without any damping a
            // see-saw snaps to its limit faster than a resting block can
            // settle onto the rising end, and it tumbles off instead.
            AngularDamp = damping,
        };
        if (stiffness != 0)
        {
            // An arm in a torsion spring runs out through openings in its
            // frame that aren't modelled; keep it clear of fixtures (it
            // still meets the floor and loose bodies).
            body.CollisionLayer = SprungArmLayer;
            body.CollisionMask = 1;
        }
        var beamOffset = new Vector3(centerOffset, 0, 0);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(length, thickness, depth) }, Position = beamOffset });
        var beam = Shapes.Box(new Vector3(length, thickness, depth), surface);
        beam.Position = beamOffset;
        body.AddChild(beam);
        AddChild(body);
        _freezable.Add(body);
        _comOffset[body] = beamOffset; // the beam is uniform, so its own centroid is its centre of mass
        _bodiesById[part.Id] = body;
        _hinges[body] = (V(part.At), axis);

        // The hinge takes its zero from the arm's pose when it's attached,
        // and #:limit-deg is meant from level — so attach it level, then
        // turn the arm to its starting angle. (Attached after turning, a
        // trebuchet cocked at -50° had its +75° stop at +25°, short of
        // vertical, and flung its stone backwards.)
        var joint = new HingeJoint3D { Transform = new Transform3D(toAxis, V(part.At)) };
        AddChild(joint);
        joint.NodeB = joint.GetPathTo(body);
        body.Transform = new Transform3D(toAxis * new Basis(new Vector3(0, 0, 1), Mathf.DegToRad(startAngle)), V(part.At));

        // A real see-saw has mechanical stops (without one, the low end
        // just keeps rotating until it hits the floor); a trebuchet arm
        // instead wants to swing through most of its arc, so its .rkt
        // file passes a much larger #:limit-deg. A catapult arm's stops are
        // uneven: far back when drawn, a little forward of square at rest.
        float lower = part.Props.GetValueOrDefault("limit-lower-deg") is SNumber lo ? (float)lo.Value : -limitDeg;
        float upper = part.Props.GetValueOrDefault("limit-upper-deg") is SNumber hi ? (float)hi.Value : limitDeg;
        // The hinge measures its angle the opposite way round to the arm's
        // own rotation (the same reason its motor speeds are negated), so
        // the arm's stops at [lower, upper] are the hinge's [−upper, −lower].
        // Even stops (±limit) never showed it; a catapult arm's uneven ones did.
        joint.SetFlag(HingeJoint3D.Flag.UseLimit, true);
        joint.SetParam(HingeJoint3D.Param.LimitUpper, Mathf.DegToRad(-lower));
        joint.SetParam(HingeJoint3D.Param.LimitLower, Mathf.DegToRad(-upper));
        BuildCatch(part, body, joint, toAxis, axis, lower, upper, depth);   // #:catch-deg (#155): holds the arm until let go

        if (stiffness != 0)
            _springs.Add(new TorsionSpring
            {
                Body = body, Axis = axis, Stiffness = stiffness,
                Rest = Mathf.DegToRad((float)part.Number("spring-rest-deg", 0)),
                Angle = Mathf.DegToRad(startAngle), LastRaw = RawAngle(body, axis),
            });

        // A torsion spring's skein, drawn where it holds an arm on a level
        // axle (an onager): a wound drum of twisted rope crossing the arm at
        // its pivot between two washers, turning with the arm as the real
        // skein twists. (A catapulta's upright springs are part of its frame.)
        if (stiffness != 0 && Mathf.Abs(axis.Y) < 0.5f)
        {
            var skein = Shapes.Cylinder(0.07f, 0.24f, Surface("hemp"));
            skein.RotationDegrees = new Vector3(90, 0, 0); // along the axle (local Z)
            body.AddChild(skein);
            foreach (float side in new[] { -0.12f, 0.12f })
            {
                var washer = Shapes.Cylinder(0.09f, 0.02f, Surface("bronze"));
                washer.RotationDegrees = new Vector3(90, 0, 0);
                washer.Position = new Vector3(0, 0, side);
                body.AddChild(washer);
            }
        }
        // Parented to the beam so the label tilts with it. Local (0,…,0)
        // is the pivot — body's own origin — which stays a sensible label
        // spot regardless of #:pivot-fraction, unlike the beam's own
        // (possibly far off-centre) visual midpoint.
        if (stiffness == 0) // a sprung arm sits inside its frame, which carries the label
            AddLabel(part.Id, new Vector3(0, thickness + 0.08f, 0), body);
        // A wider footing than a pendulum's — a lever's fulcrum takes a
        // real sideways load (the beam pushes on it, unlike a pendulum
        // hanging straight down), and for a trebuchet's tall pivot this
        // also reads as the tower/frame a real one is mounted on. A sprung
        // arm is held by its spring's frame instead.
        // An upright each side of the beam, with the axle between them: a
        // post straight under the pivot would stand in the way of whatever
        // swings below it (a trebuchet's counterweight passes right there).
        // An arm turning about a vertical axis is held by its own frame.
        if (Mathf.Abs(axis.Y) < 0.5f)
        {
            float beside = depth / 2 + 0.12f;
            var near = V(part.At) - axis * beside;
            var far = V(part.At) + axis * beside;
            AddGroundedSupport(near, 0.03f, 0.18f);
            AddGroundedSupport(far, 0.03f, 0.18f);
            AddChild(Shapes.Rod(near, far, 0.02f, Surface("iron")));
        }
    }

    private const uint FixtureLayer = 8, SprungArmLayer = 16;
    private readonly List<(Vector3 Pivot, float BobRadius, float Reach, Basis Yaw, string Id)> _pendulumMounts = []; // Reach: clearance past the end pivots

    /// <summary>
    /// Pendulums hang from a frame beside their swing, never from a post
    /// beneath the pivot (which is exactly where the bob hangs). Pendulums
    /// in a row (a Newton's cradle) share one frame: a post at each end on
    /// each side, a rail along each side at pivot height, and an axle
    /// across the rails at each pivot.
    /// </summary>
    private void BuildPendulumFrames()
    {
        var wood = Surface("oak");
        var iron = Surface("iron");
        // a row is pendulums at one height, one heading and one line across their swing; it is laid out in the
        // row's own frame (x along the row, z across the swing) and turned into the world by its heading
        foreach (var row in _pendulumMounts.GroupBy(m =>
                 {
                     var local = m.Yaw.Inverse() * m.Pivot;
                     return (Mathf.Snapped(m.Pivot.Y, 0.01f), Mathf.Snapped(local.Z, 0.01f), Mathf.Snapped(m.Yaw.GetEuler().Y, 0.001f));
                 }))
        {
            var yaw = row.First().Yaw;
            _building = row.First().Id;   // a row's frame is the first pendulum's to click
            Vector3 At(float x, float y, float z) => yaw * new Vector3(x, y, z);
            float y = row.First().Pivot.Y, z = (yaw.Inverse() * row.First().Pivot).Z;
            float side = row.Max(m => m.BobRadius) + 0.04f;
            float left = row.Min(m => (yaw.Inverse() * m.Pivot).X - m.Reach), right = row.Max(m => (yaw.Inverse() * m.Pivot).X + m.Reach);
            foreach (float dz in new[] { -side, side })
            {
                AddGroundedSupport(At(left, y, z + dz), 0.012f, 0.06f);
                AddGroundedSupport(At(right, y, z + dz), 0.012f, 0.06f);
                AddChild(Shapes.Rod(At(left, y, z + dz), At(right, y, z + dz), 0.01f, wood));
            }
            foreach (var (pivot, _, _, _, _) in row)
                AddChild(Shapes.Rod(pivot - yaw * new Vector3(0, 0, side), pivot + yaw * new Vector3(0, 0, side), 0.005f, iron));
        }
    }
    private readonly List<TorsionSpring> _springs = [];

    /// <summary>A lever held by a torsion spring (twisted sinew): torque −k·(θ − rest) about its hinge.</summary>
    private sealed class TorsionSpring
    {
        public required RigidBody3D Body;
        public required Vector3 Axis;
        public required float Stiffness, Rest;
        public double Angle, LastRaw;
    }

    private void DriveSprings()
    {
        foreach (var s in _springs)
        {
            double raw = RawAngle(s.Body, s.Axis);
            s.Angle += Unwrap(raw - s.LastRaw);
            s.LastRaw = raw;
            s.Body.ApplyTorque(s.Axis * (float)(-s.Stiffness * (s.Angle - s.Rest)));
        }
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
        var yaw = YawOf(part);
        var center = V(part.At) + yaw * new Vector3(0, length / 2 * Mathf.Sin(angle), -length / 2 * Mathf.Cos(angle));
        var body = new StaticBody3D
        {
            Transform = new Transform3D(yaw * new Basis(Vector3.Right, angle), center),
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = (float)_materials[part.Material].Friction },
        };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(width, thickness, length) } });
        body.AddChild(Shapes.Box(new Vector3(width, thickness, length), Surface(part.Material)));
        AddChild(body);
        // Drawn as the solid wedge a ramp is, down to its own base height, so it reads as a slope and not a plank
        // laid flat (two such planks, a lever and a ramp, looked alike in the build palette; #165). Only drawn: the
        // slab above is still what bodies slide on.
        float rise = length * Mathf.Sin(angle), run = length * Mathf.Cos(angle);
        if (rise > 0.01f)
        {
            var wedge = new MeshInstance3D
            {
                // PrismMesh's ridge is at its top; left_to_right 1 puts it over the +X edge, a right-angled wedge
                Mesh = new PrismMesh { LeftToRight = 1, Size = new Vector3(run, rise, width) },
                MaterialOverride = Surface(part.Material),
                // its +X (the tall side) must point back up the slope, the ramp's −Z: a quarter turn about Y, then the heading
                Transform = new Transform3D(yaw * new Basis(Vector3.Up, Mathf.Pi / 2), V(part.At) + yaw * new Vector3(0, rise / 2 - thickness / 2, -run / 2)),
            };
            AddChild(wedge);
        }
        _surfaceMaterials[body.GetInstanceId()] = part.Material;
        AddLabel(part.Id, V(part.At) + new Vector3(0, 0.1f, 0));
    }

    // ------------------------------------------------ generated geometry

    // Parts on axles collide with ordinary bodies (layer 1) but never with
    // each other: a meshing pair's outlines overlap by design, and contact
    // between teeth would just shove them apart. Turning one gear to drive
    // the next is a gear-coupling constraint, not tooth collisions.
    private const uint AxleLayer = 2;

    private static readonly Dictionary<string, ArrayMesh> GeneratedMeshes = [];

    /// <summary>
    /// A part's generated mesh — racket/heroic/geometry, written to
    /// game/meshes/&lt;stem&gt;.glb by racket/build.rkt — read with Godot's
    /// runtime glTF loader rather than the editor's importer, so it works
    /// headless with no import step. Cached per file: every part built from
    /// the same numbers shares one mesh.
    /// </summary>
    private static ArrayMesh GeneratedMesh(PartSpec part)
    {
        // A machine's own parts are written to meshes/; the build-mode
        // palette's standard parts (a 15 cm drum, an 18-tooth gear) to
        // meshes/catalogue/. Either can appear in a machine built in the editor.
        string path = $"res://meshes/{part.Text("mesh")}.glb";
        if (!Godot.FileAccess.FileExists(path) && Godot.FileAccess.FileExists($"res://meshes/catalogue/{part.Text("mesh")}.glb"))
            path = $"res://meshes/catalogue/{part.Text("mesh")}.glb";
        if (GeneratedMeshes.TryGetValue(path, out var cached)) return cached;
        var doc = new GltfDocument();
        var state = new GltfState();
        // read through Godot's own file access, which also reads inside an exported game's pack, where res:// is no folder (#99)
        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        var err = bytes.Length > 0 ? doc.AppendFromBuffer(bytes, path.GetBaseDir(), state) : Error.FileNotFound;
        if (err != Error.Ok || state.GetMeshes().Count == 0)
            throw new MachineFormatException(
                $"{part.Kind} {part.Id}: couldn't load its mesh {path} ({err}); run `racket racket/build.rkt`", part.Location);
        var mesh = state.GetMeshes()[0].Mesh.GetMesh();
        GeneratedMeshes[path] = mesh;
        return mesh;
    }

    /// <summary>Text sized to the part: millimetre gears and a 4.5 m treadwheel can't share one label size.</summary>
    private static float LabelSizeFor(float size) => Mathf.Clamp(size * 0.002f, 0.00012f, 0.01f);
    private static float LabelSizeFor(Aabb box) => LabelSizeFor(Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z)));

    /// <summary>The rotation taking a generated part's local +Z (its axle) onto <paramref name="axis"/>.</summary>
    private static Basis AxleBasis(Vector3 axis)
    {
        var z = new Vector3(0, 0, 1);
        var cross = z.Cross(axis);
        if (cross.LengthSquared() < 1e-10f) return axis.Z >= 0 ? Basis.Identity : new Basis(Vector3.Up, Mathf.Pi);
        return new Basis(cross.Normalized(), z.AngleTo(axis));
    }

    /// <summary>
    /// A body turning on a fixed axle through part.At along
    /// <paramref name="axis"/>. #:drive-rpm turns it steadily through the
    /// hinge's motor, like a man at a crank; without it, it turns only if
    /// pushed. Mass and moments of inertia are the material's density times
    /// the shape's exact volume and second moments, computed from the mesh
    /// in Racket. Left to derive them itself from the (simplified,
    /// slightly lopsided) collision hull, Jolt put a gear's centre of mass
    /// off its axle, and an undriven gear swung round like a pendulum.
    /// </summary>
    private RigidBody3D BuildOnAxle(PartSpec part, Vector3 axis, float startAngleDeg, string labelText)
    {
        var mesh = GeneratedMesh(part);
        // at its heading: the axle turns with the machine, and so does the roll the mesh is stood at about it
        var yaw = YawOf(part);
        var toAxis = yaw * AxleBasis(axis);
        axis = yaw * axis;
        double density = _materials[part.Material].Density;
        var body = new RigidBody3D
        {
            Name = part.Id,
            Mass = (float)(density * part.Number("volume")),
            PhysicsMaterialOverride = ContactFor(part.Material),
            CenterOfMassMode = RigidBody3D.CenterOfMassModeEnum.Custom,
            CenterOfMass = Vector3.Zero, // every generated wheel and screw is balanced on its axle
            Inertia = new Vector3((float)(density * part.Number("inertia-x")),
                                  (float)(density * part.Number("inertia-y")),
                                  (float)(density * part.Number("inertia-z"))),
            CollisionLayer = AxleLayer,
            CollisionMask = 1,
            AngularDamp = 0.2f, // a little bearing friction, so an undriven wheel nudged by something eventually stops
            // A small slow wheel — a clock gear cranked at 2 rpm, its rim
            // moving 3 mm/s — is below the engine's "come to rest"
            // threshold and would be put to sleep mid-turn.
            CanSleep = false,
        };
        body.Transform = new Transform3D(toAxis * new Basis(new Vector3(0, 0, 1), Mathf.DegToRad(startAngleDeg)), V(part.At));
        // #:start-rpm lets it go already spinning, a flywheel spun up by hand and released
        if (part.Props.GetValueOrDefault("start-rpm") is SNumber spin)
            body.AngularVelocity = axis.Normalized() * (float)(spin.Value * Math.Tau / 60);
        if (part.Symbol("shape", "") is "disc-wheel" or "cart-wheel")
            // a wheel that rolls on the ground needs a round rim: a hull of the
            // mesh's 64 facets bumps from flat to flat and loses to every bump
            // (carts slowed at twice their rolling resistance)
            body.AddChild(new CollisionShape3D
            {
                Shape = new CylinderShape3D { Radius = (float)part.Number("radius"), Height = (float)part.Number("width") },
                Rotation = new Vector3(Mathf.Pi / 2, 0, 0),   // the cylinder's axis (its Y) along the axle (local Z)
            });
        else if (part.Symbol("shape", "") == "drum" && part.Props.ContainsKey("on"))
        {
            // a flanged wheel (a drum, carried as a wagon's wheel): its barrel is
            // the tread and its flanges straddle a rail. A hull would fill the
            // groove and roll it on its flanges, so: three true cylinders.
            float len = (float)part.Number("length"), t = 0.08f * len;   // the drum's own flange thickness
            float tread = (float)part.Number("radius"), flange = (float)part.Number("flange-radius");
            foreach (var (radius, height, z) in new[] { (tread, len - 2 * t, 0f), (flange, t, len / 2 - t / 2), (flange, t, -(len / 2 - t / 2)) })
                body.AddChild(new CollisionShape3D
                {
                    Shape = new CylinderShape3D { Radius = radius, Height = height },
                    Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), new Vector3(0, 0, z)),
                });
        }
        else
            body.AddChild(new CollisionShape3D { Shape = mesh.CreateConvexShape() });
        var extent = mesh.GetAabb().Size;
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = PartSurface(part, Mathf.Max(extent.X, extent.Y)) });
        // A round, evenly toothed wheel looks the same at every angle, so a gear turning at 27 rpm read as
        // frozen: it is marked with a stripe, painted on, by BuildTurnMarks (#172).
        // a disc wheel stays a solid disc (that is the point of it) but gets a rim and a hub (#102)
        // (not a millstone: stone and a grind torque make it a runner, which is a solid slab)
        if (part.Symbol("shape", "") == "disc-wheel" && !part.Props.ContainsKey("grind-torque") && _materials[part.Material].Category != HeroicInventions.Sim.Materials.MaterialCategory.Stone)
            Skins.RimWheel(body, Mathf.Min(extent.X, extent.Y) / 2, extent.Z, Surface(part.Material));
        AddChild(body);
        _freezable.Add(body);
        _bodiesById[part.Id] = body;
        if (part.Props.GetValueOrDefault("on") is SSymbol)
        {
            // a cart's wheel: its axle rides on the chassis, hinged there once
            // every body is built (BuildCarriedWheels)
            _toCarry.Add((part, body, axis));
            AddLabel(part.Id, Vector3.Up * (float)(part.Number("radius", 0.1) + 0.1), body, LabelSizeFor((float)part.Number("radius", 0.1)));
            return body;
        }
        _hinges[body] = (V(part.At), axis);

        // A wheel riding on another's arbor is locked to it (BuildArbors) and
        // turns freely in its own bearing; any drive belongs to the arbor's first.
        // (a water wheel, windmill or jet wheel on an arbor is turned by the sim: the first Jolt wheel leads)
        bool rides = Runtime.Def.Arbors.Any(a => a.Parts.Contains(part.Id) && ArborLead(a) != part.Id);
        double rpm = rides ? 0 : part.Number("drive-rpm", 0);
        if (!rides)
        {
            // A HingeJoint3D turns about its own local Z, so it gets the same basis.
            var joint = new HingeJoint3D { Transform = new Transform3D(toAxis, V(part.At)) };
            AddChild(joint);
            joint.NodeB = joint.GetPathTo(body);
            _axleJoints[part.Id] = joint;
            // the motor is set from the part's drive (speed, torque), and again every tick (ApplyDrives), so a person can change it
            if (Runtime.Drives.TryGetValue(part.Id, out var drive) && drive.Driven)
            {
                _driveJoints.Add((joint, drive));
                ApplyDrive(joint, drive);
            }
        }
        else
        {
            // its own bearing on the axle line, free and undriven: the shaft is carried in bearings, so a heavy
            // wheel on a light lead (the Antikythera d2, 56 g, on a 3 g pinion) doesn't hang its weight through
            // the arbor's lock and the lead's one hinge — Jolt let that chain sag 1.3 mm and a millimetre gear's
            // teeth ride over its partner's (#85). The lock (BuildArbors) still makes them turn as one.
            var bearing = new HingeJoint3D { Transform = new Transform3D(toAxis, V(part.At)) };
            AddChild(bearing);
            bearing.NodeB = bearing.GetPathTo(body);
        }

        var box = mesh.GetAabb();
        _axles.Add((body, axis, box.Size.Z / 2, Mathf.Max(box.Size.X, box.Size.Y) / 2, rpm != 0, labelText));
        return body; // labelled per axle in BuildAxleSupports, so parts sharing one don't print on top of each other
    }

    private void BuildWheel(PartSpec part)
    {
        // #:tilt-deg raises an x or z axle toward y, or leans a y axle toward x
        float tilt = Mathf.DegToRad((float)part.Number("tilt-deg", 0));
        var axis = part.Symbol("axis", "z") switch
        {
            "x" => new Vector3(Mathf.Cos(tilt), Mathf.Sin(tilt), 0),
            "y" => new Vector3(Mathf.Sin(tilt), Mathf.Cos(tilt), 0),
            _ => new Vector3(0, Mathf.Sin(tilt), Mathf.Cos(tilt)),
        };
        string label = part.Symbol("shape", "") == "gear" ? $"{part.Id} · {part.Number("teeth"):F0} teeth" : part.Id;
        BuildOnAxle(part, axis, (float)part.Number("angle-deg", 0), label);
    }

    /// <summary>An Archimedes' screw: its axle runs along X, raised #:tilt-deg from level.</summary>
    private void BuildScrew(PartSpec part)
    {
        float tilt = Mathf.DegToRad((float)part.Number("tilt-deg", 0));
        BuildOnAxle(part, new Vector3(Mathf.Cos(tilt), Mathf.Sin(tilt), 0), 0, part.Id);
    }

    /// <summary>
    /// Wheels on one arbor turn as one piece: each after the first is
    /// locked to the first by a hinge allowed no rotation at all. Each
    /// remembers its arbor-mates, so a rope pulling on one knows it has
    /// the whole shaft's inertia to turn.
    /// </summary>
    private void BuildArbors()
    {
        foreach (var arbor in Runtime.Def.Arbors)
        {
            // a water wheel, windmill or jet wheel on it is turned by the sim, and coupled to the first wheel in BuildGearTrains
            var bodies = arbor.Parts.Where(p => !IsSimTurned(p)).Select(p => _bodiesById[p]).ToList();
            var lead = bodies[0];
            foreach (var rider in bodies.Skip(1))
            {
                var lock_ = new HingeJoint3D { Transform = new Transform3D(AxleBasis(_hinges[lead].Axis), rider.GlobalPosition) };
                AddChild(lock_);
                lock_.NodeA = lock_.GetPathTo(lead);
                lock_.NodeB = lock_.GetPathTo(rider);
                lock_.SetFlag(HingeJoint3D.Flag.UseLimit, true);
                lock_.SetParam(HingeJoint3D.Param.LimitUpper, 0);
                lock_.SetParam(HingeJoint3D.Param.LimitLower, 0);
            }
            foreach (var b in bodies) _arborMates[b] = bodies.Where(o => o != b).ToList();
        }
    }

    /// <summary>Generated geometry that stays put, standing on the ground at part.At.</summary>
    private void BuildFixture(PartSpec part)
    {
        var mesh = GeneratedMesh(part);
        var body = new StaticBody3D
        {
            Name = part.Id, Position = V(part.At), RotationDegrees = new Vector3(0, (float)(part.Number("turn-deg", 0) + MachineDef.HeadingOf(part)), 0),
            CollisionLayer = FixtureLayer, CollisionMask = 1, // meets loose bodies, not sprung arms
            PhysicsMaterialOverride = ContactFor(part.Material),
        };
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
        // sized by the fixture's thinnest direction, so a long frame doesn't get a heavy rim
        var extent = mesh.GetAabb().Size;
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = PartSurface(part, Mathf.Min(extent.X, Mathf.Min(extent.Y, extent.Z))) });
        AddChild(body);
        var box = mesh.GetAabb();
        // Springs and arms sit inside the frame; one label for the whole engine reads better than four.
        if (part.Symbol("shape", "") == "catapult-frame")
            AddLabel(part.Id, V(part.At) + new Vector3(0, box.End.Y + 0.1f, 0), pixelSize: LabelSizeFor(box));
    }

    /// <summary>
    /// A fixture meets the loose bodies on layer 1 (issue #189). Jolt pushes a body off another only
    /// when the other's layer is in the body's own mask, not when either mask holds the other's layer
    /// as Godot's own physics did: the frame scanning the bolt saw the contact, but the bolt, scanning
    /// only layer 1, felt none of it and fell through the catapulta's channel. So every body on layer 1
    /// scans the fixtures' layer too; sprung arms, pendulums and axle bodies stay clear of them.
    /// </summary>
    private void LetLooseBodiesMeetFixtures()
    {
        foreach (var body in GetChildren().OfType<RigidBody3D>())
            if (body.CollisionLayer == 1 && (body.CollisionMask & 1) != 0) body.CollisionMask |= FixtureLayer;
    }

    /// <summary>
    /// What holds each axle up, and its label. Parts sharing one axle line
    /// (a treadwheel and its drum, two gears fixed on one arbor) are
    /// treated as one axle. Clockwork-scale axles (under 10 cm) run from a
    /// dark wooden backboard — the Antikythera mechanism sat in a wooden
    /// case, and a bronze plate behind bronze gears hid them completely;
    /// larger ones get an axle rod and a post at each end.
    /// </summary>
    private void BuildAxleSupports()
    {
        var groups = _axles
            .GroupBy(a =>
            {
                var c = a.Body.Position;
                var across = c - a.Axis * c.Dot(a.Axis); // where the axle line pierces the plane square to it
                return (Mathf.Snapped(a.Axis.X, 0.001f), Mathf.Snapped(a.Axis.Y, 0.001f), Mathf.Snapped(a.Axis.Z, 0.001f),
                        Mathf.Snapped(across.X, 0.001f), Mathf.Snapped(across.Y, 0.001f), Mathf.Snapped(across.Z, 0.001f));
            })
            .Select(g =>
            {
                var axis = g.First().Axis;
                float lo = g.Min(a => a.Body.Position.Dot(axis) - a.HalfLength);
                float hi = g.Max(a => a.Body.Position.Dot(axis) + a.HalfLength);
                var c = g.First().Body.Position;
                var across = c - axis * c.Dot(axis);
                return (Axis: axis, Back: across + axis * lo, Front: across + axis * hi, Radius: g.Max(a => a.Radius),
                        Label: string.Join("\n", g.Select(a => a.Label)), Id: g.First().Body.Name.ToString(), Driven: g.Any(a => _trainOf.ContainsKey(a.Body)));
            })
            .ToList();

        foreach (var g in groups)
        {
            _building = g.Id;   // an axle's rod, posts and label belong to its first wheel
            // one line per part on the axle, above its largest wheel
            var middle = (g.Back + g.Front) / 2;
            AddLabel(g.Label, middle + Vector3.Up * g.Radius * 1.25f, pixelSize: LabelSizeFor(2 * g.Radius));
        }

        // small wheels on z axles stand on a clockmaker's plate, unless they are a driven
        // train's (#113), working machinery that stands on posts like any other axle
        var small = groups.Where(g => g.Radius < 0.1f && Mathf.Abs(g.Axis.Z) > 0.999f && !g.Driven).ToList();
        if (small.Count > 0)
        {
            _building = small[0].Id;   // the plate is shared: the first small wheel owns it
            float margin = small.Max(g => g.Radius) * 0.3f;
            float left = small.Min(g => g.Back.X - g.Radius) - margin;
            float right = small.Max(g => g.Back.X + g.Radius) + margin;
            float top = small.Max(g => g.Back.Y + g.Radius) + margin;
            float plateZ = small.Min(g => g.Back.Z) - 0.003f;
            const float thick = 0.002f;
            var plate = Shapes.Box(new Vector3(right - left, top, thick), Shapes.Mat(new Color(0.22f, 0.15f, 0.10f), roughness: 0.7f));
            plate.Position = new Vector3((left + right) / 2, top / 2, plateZ - thick / 2);
            AddChild(plate);
            foreach (var g in small)
            {
                _building = g.Id;
                float rod = Mathf.Max(0.0006f, g.Radius * 0.04f);
                AddChild(Shapes.Rod(new Vector3(g.Back.X, g.Back.Y, plateZ), g.Front + g.Axis * rod * 2, rod, Surface("bronze"))); // the arbor
            }
        }
        foreach (var g in groups.Except(small))
        {
            _building = g.Id;
            float gap = Mathf.Clamp(g.Radius * 0.1f, 0.01f, 0.15f);
            var back = g.Back - g.Axis * gap;
            var front = g.Front + g.Axis * gap;
            float rod = Mathf.Clamp(g.Radius * 0.04f, 0.005f, 0.08f);
            AddChild(Shapes.Rod(back, front, rod, Surface("iron")));
            AddGroundedSupport(back, rod * 1.6f, rod * 8);
            AddGroundedSupport(front, rod * 1.6f, rod * 8);
        }
    }

    /// <summary>Signed turning speed of an axle body about its own axle, in rpm.</summary>
    private static double AxleRpm(RigidBody3D body, Vector3 axis) => body.AngularVelocity.Dot(axis) * 60 / Math.Tau;

    /// <summary>½ωᵀIω, with the world-space inertia from the physics server.</summary>
    private static double SpinEnergy(RigidBody3D body)
    {
        var inv = PhysicsServer3D.BodyGetDirectState(body.GetRid())?.InverseInertiaTensor;
        if (inv is not { } invI || Mathf.Abs(invI.Determinant()) < 1e-20f) return 0;
        var w = body.AngularVelocity;
        return 0.5 * w.Dot(invI.Inverse() * w);
    }

    /// <summary>
    /// Collisions between pendulum bobs, as a sequence of two-ball impacts —
    /// the textbook account of a Newton's cradle. A rigid-body solver
    /// treats a row of touching balls as one simultaneous contact and
    /// shares the blow out among all of them, so the whole row swings off
    /// together; resolved pair by pair, the momentum passes down the row
    /// and leaves by the far ball, as it does on a real cradle.
    ///
    /// Each impact is an impulse J along the line of centres, sized so the
    /// balls' closing speed is reversed and scaled by the restitution e:
    ///   J = (1 + e)·v / (kᵢ²/Iᵢ + kⱼ²/Iⱼ)
    /// where k is each ball's lever arm about its pivot for a push along
    /// that line and I its moment of inertia about the pivot. Runs before
    /// each physics step, so a pair about to close its gap within the
    /// step is caught before the balls overlap.
    /// </summary>
    private void ResolveBobImpacts()
    {
        if (_pendulums.Count < 2) return;
        float dt = (float)GetPhysicsProcessDeltaTime();
        var bobs = _pendulums.Select(p =>
        {
            var centre = p.Body.GlobalTransform * new Vector3(0, -p.Length, 0);
            return (p.Body, Arm: centre - p.Body.GlobalPosition, Centre: centre, p.BobRadius, p.Inertia, p.Restitution, Axis: _hinges[p.Body].Axis.Normalized());
        }).ToList();
        // Impulses given to a body only take effect at the next physics
        // step, so the chain of impacts is worked out on a copy of the
        // balls' spins; each impulse is then handed to Jolt once.
        var spin = bobs.Select(b => b.Body.AngularVelocity.Dot(b.Axis)).ToArray();
        var impulses = new List<(RigidBody3D Body, Vector3 Impulse, Vector3 At)>();
        Vector3 Velocity(int i) => (bobs[i].Axis * spin[i]).Cross(bobs[i].Arm);

        // Broad phase (issue #73): only bobs that could touch within this step
        // are ever compared. Sorted along X, a pair is a candidate if the gap
        // between their centres is within both radii plus the distance they
        // could close in one step. Impacts pass speed along a row, and an
        // elastic knock can send a light ball off at up to twice the
        // striker's speed, so the reach allows twice the fastest bob's speed.
        // Comparing every pair every pass grew as N²: 800 pendulums ran
        // 3.2 times slower than real time, 87% of it in this check.
        float fastest = 0;
        for (int i = 0; i < bobs.Count; i++) fastest = Mathf.Max(fastest, Velocity(i).Length());
        float reach = 2 * fastest * dt + 0.001f;
        float widest = bobs.Max(b => b.BobRadius);
        var order = Enumerable.Range(0, bobs.Count).OrderBy(i => bobs[i].Centre.X).ToArray();
        var pairs = new List<(int I, int J)>();
        for (int oi = 0; oi < order.Length; oi++)
        {
            int i = order[oi];
            for (int oj = oi + 1; oj < order.Length; oj++)
            {
                int j = order[oj];
                if (bobs[j].Centre.X - bobs[i].Centre.X > 2 * widest + reach) break;
                float limit = bobs[i].BobRadius + bobs[j].BobRadius + reach;
                if (bobs[i].Centre.DistanceSquaredTo(bobs[j].Centre) <= limit * limit)
                    pairs.Add(i < j ? (i, j) : (j, i));
            }
        }
        if (pairs.Count == 0) return;
        pairs.Sort();   // the same order the all-pairs loop used, so a cradle's chain resolves the same way

        for (int pass = 0; pass < 4 * bobs.Count; pass++)
        {
            bool struck = false;
            foreach (var (i, j) in pairs)
            {
                var a = bobs[i];
                var b = bobs[j];
                var line = b.Centre - a.Centre;
                float dist = line.Length();
                var n = line / dist;
                float closing = (Velocity(i) - Velocity(j)).Dot(n);
                if (closing <= 1e-4f || dist - a.BobRadius - b.BobRadius > closing * dt + 0.0005f) continue;
                float ka = a.Arm.Cross(n).Dot(a.Axis), kb = b.Arm.Cross(n).Dot(b.Axis);
                float e = Mathf.Min(a.Restitution, b.Restitution);
                float impulse = (1 + e) * closing / (ka * ka / a.Inertia + kb * kb / b.Inertia);
                spin[i] -= impulse * ka / a.Inertia;
                spin[j] += impulse * kb / b.Inertia;
                var contact = a.Centre + n * a.BobRadius;
                impulses.Add((a.Body, -n * impulse, contact - a.Body.GlobalPosition));
                impulses.Add((b.Body, n * impulse, contact - b.Body.GlobalPosition));
                struck = true;
                // the strike, for the impact record: the energy it takes is
                // the pair's closing energy, (1 - e²) of it
                if (closing >= LeastImpactSpeed)
                    RecordImpact(new Impact(Runtime.Time, a.Body, b.Body, contact, n, closing, impulse,
                                            0.5f * (1 - e * e) * closing * closing / (ka * ka / a.Inertia + kb * kb / b.Inertia)));
            }
            if (!struck) break;
        }
        foreach (var (body, impulse, at) in impulses) body.ApplyImpulse(impulse, at);
    }

    public void Simulate(double dt, bool trace = true)
    {
        ApplyPlanetGravity();
        DetectImpacts();
        ResolveBobImpacts();
        ResolveRopes();
        ApplyDrives();
        DriveGearTrains();
        DriveLifts();
        DrivePistons();
        DriveSprings();
        RollCarriedWheels();
        GrindMillstones(dt);
        LoadGenerators(dt);
        FrictionAxles(dt);
        DriveFollows();
        DriveBelts(dt);
        DriveGrips(dt);
        DriveCams(dt);
        DriveRatchets(dt);
        DriveCatches();
        TestTriggers();
        ApplyBuoyancy();
        ApplyLift();
        ApplyDrag();
        ApplyHand();   // a hand holding something (#159)
        ConstrainChains();
        CheckBurial();
        CheckSpinLimit();   // the bodies have stepped: any at Jolt's spin limit are reported (MachineView.SpinLimit.cs, #202)
        Runtime.Step(dt);
        CoupleDrivenTrains(dt);   // after the sim's turning parts have stepped, before Jolt's bodies do (#113)
        Refresh();
        if (trace) TraceTick(dt);
        KeepVelocitiesIntoStep();
    }

    private double _shownGravity = Physics.Gravity;
    private bool _gravityApplied;

    /// <summary>
    /// The world falls at Earth's 9.81 m/s² (Main sets it once); this
    /// machine's bodies fall at its planet's gravity (issue #38), each scaled
    /// by g / 9.81, so a Mars machine and an Earth one can stand in one world
    /// and each swing at its own rate. Set again whenever the planet's
    /// gravity changes (scene.gravity, live).
    /// </summary>
    private void ApplyPlanetGravity()
    {
        double g = Runtime.Outside.Gravity;
        if (_gravityApplied && g == _shownGravity) return;
        _gravityApplied = true;
        _shownGravity = g;
        float scale = (float)(g / Physics.Gravity);
        foreach (var b in FindChildren("*", nameof(RigidBody3D), true, false).OfType<RigidBody3D>())
            b.GravityScale = scale;
    }

    /// <summary>Rotation and height of every dynamic body — a quick way to confirm Jolt is actually moving them (see HEROIC_DEBUG_PHYSICS).</summary>
    public string DebugState() =>
        string.Join("  ", _freezable.Select(b => $"{b.Name} pos=({b.GlobalPosition.X:F2},{b.GlobalPosition.Y:F2},{b.GlobalPosition.Z:F2}) rotZ={b.RotationDegrees.Z:F1}°")
                          .Concat(_ropes.Select(r => r.Describe()))
                          .Concat(GearReport())
                          .Concat(Runtime.Channels.Values.Select(c => $"{c.Name} {c.Flow * 1000:0.#}L/s {c.Depth * 100:F1}cm {c.Velocity:F2}m/s"))
                          .Concat(_liftDrives.Select(d => $"{d.Spec.Id} {d.Lift.Rpm:F1}rpm {d.Lift.Flow * 1000:F2}L/s {d.Spec.From}={d.Lift.From.WaterVolume * 1000:F0}L {d.Spec.To}={d.Lift.To.WaterVolume * 1000:F0}L"))
                          .Concat(_cylinderDrives.Select(c => $"{c.Cylinder.Name} P={c.Cylinder.Pressure / 1000:F0}kPa F={c.Cylinder.Force / 1000:F1}kN {(c.Cylinder.Injecting ? "INJECT" : "steam")} strokes={c.Cylinder.Strokes} boiler={c.Cylinder.Boiler.Temperature:F1}C steam-used={c.Cylinder.SteamUsed:F1}kg"))
                          .Concat(_pumpDrives.Select(p => $"{p.Lift.Name} {p.Lift.To.Name}={p.Lift.To.WaterVolume * 1000:F0}L"))
                          .Concat(_springs.Select(sp => $"{sp.Body.Name} θ={Mathf.RadToDeg((float)sp.Angle):F0}°")));

    /// <summary>Redraws the scene from the simulation as it stands now: what a sleep leaves on screen (issue #59).</summary>
    public void ShowState() => Refresh();

    private void Refresh()
    {
        foreach (var (tank, spec, water) in _water)
        {
            float level = ShownLevel(tank);   // the true level, or a 4 mm film once it holds any (#173)
            water.Visible = tank.WaterVolume >= 5e-6 || tank.Level > 0.001;
            water.Scale = new Vector3(1, level, 1);
            // a hanging vessel rides up and down on its rope
            water.Position = new Vector3((float)spec.At.X, (float)tank.BaseElevation + level / 2, (float)spec.At.Z);
            if (_ice.TryGetValue(tank, out var ice))
            {
                // the sheet floats on what is still water; drawn thicker than life so a few mm shows
                float thick = (float)tank.Ice;
                ice.Visible = thick > 1e-5;
                // frozen solid it is the whole block, at its true thickness, and whiter (air trapped as it froze)
                float shown = tank.FrozenSolid ? thick : Mathf.Max(thick * 3, 0.01f);
                ((StandardMaterial3D)ice.MaterialOverride).AlbedoColor = tank.FrozenSolid ? new Color(1f, 1f, 1f) : new Color(0.96f, 0.98f, 1f);
                ice.Scale = new Vector3(1, shown, 1);
                ice.Position = new Vector3((float)spec.At.X, (float)tank.BaseElevation + (float)tank.Level + shown / 2, (float)spec.At.Z);
            }
            if (_tankShells.TryGetValue(tank, out var shell))
                shell.Position = new Vector3(shell.Position.X, (float)tank.BaseElevation + (float)tank.Height / 2, shell.Position.Z);
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
        foreach (var (boiler, fire, glow, flame) in _fires)
        {
            bool lit = boiler.HeatInput > 0 && !boiler.IsDry;
            fire.Visible = lit;
            flame.Emitting = lit;
            // A cheap flicker: emission energy wobbling around its base
            // value. Two sine waves at different (irrational-ish) rates
            // so it doesn't read as a metronomic pulse.
            if (lit)
            {
                double t = Runtime.Time;
                glow.EmissionEnergyMultiplier = 2.5f + 0.7f * (float)(Math.Sin(t * 11.3) + Math.Sin(t * 5.1)) / 2f;
            }
        }
        foreach (var (steamFlow, puff) in _steamPuffs)
            puff.Emitting = steamFlow() > 1e-6;
        foreach (var (jack, node) in _smokeJackViews)
            node.Rotation = new Vector3(0, (float)jack.Angle, 0);
        foreach (var (wheel, node, jet) in _jetWheelViews)
        {
            node.Rotation = new Vector3(0, 0, (float)wheel.Angle);
            // the jet shows while steam flows, fuller the faster it comes
            jet.Visible = wheel.SteamFlow > 1e-6;
            jet.Transparency = Mathf.Clamp(1f - (float)(wheel.JetVelocity / 300.0), 0.3f, 0.85f);
        }
        foreach (var rope in _ropes) DrawRope(rope);
        DrawLiftStreams();
        DrawChannels();
        DrawPipeFlow();
        DrawCylinders();
        DrawHearths();
        DrawWaterWheels();
        DrawWindmills();
        DrawCapstans();
        DrawCounterpoises();
        DrawBearingPendulums();
        DrawFloatValves();
        DrawLeaks();
        DrawDrains();
        DrawTriggers();
        DrawImpacts();
        DrawBelts();
        DrawGearTrains();
        DrawJoints();
        DrawMillstones();
        DrawAxleFriction();
        DrawFracture();
        DrawGrips();
        DrawCams();
        DrawRatchets();
        DrawHoppers();
        DrawSafetyValves();
        DrawBellows();
        DrawWarmth();
        DrawEnclosures();
        DrawDoors();
        DrawCrucibles();
        DrawHeatStores();
        DrawBimetals();
        DrawElectrics();
        DrawEnvelopes();
        DrawGalvanicJars();
        DrawPanes();
        DrawRainHouse();
        DrawStirlings();
        DrawGreenhouse();
        DrawSprings();
        DrawMirrors();
        DrawPumps();
        DrawDiggers();
        DrawFloats();
        DrawSluiceBoxes();
        DrawProducts();
        DrawGauges();   // last: a room's dial follows its walls
    }

    public void ToggleFire()
    {
        foreach (var (boiler, _, _, _) in _fires)
            boiler.HeatInput = boiler.HeatInput > 0 ? 0 : 3000;
    }

    /// <summary>
    /// After a live edit (issue #75), takes over the previous build's moving
    /// bodies: each body whose part stands where it stood (same id, kind and
    /// position in both definitions) gets the old body's pose and velocities,
    /// so a swinging pendulum or a stone in flight carries on; a part the
    /// edit moved starts at its new place. Ropes that are unchanged keep what
    /// they had wound, released or broken. The simulation-side state is
    /// carried separately, by MachineRuntime.TakeStateFrom, before this view
    /// is built.
    /// </summary>
    public void TakeBodiesFrom(MachineView old)
    {
        foreach (var (id, body) in _bodiesById)
        {
            if (!old._bodiesById.TryGetValue(id, out var was) || !IsInstanceValid(was)) continue;
            var before = old.Runtime.Def.Part(id);
            var now = Runtime.Def.Part(id);
            if (before is null || now is null || before.Kind != now.Kind || before.At != now.At) continue;
            // Read from the physics server, not the node: a body's node is only
            // brought up to date at the start of the next physics step, so
            // between steps it still shows the previous tick, and copying it
            // left the rebuilt machine one tick behind its own clock.
            var state = PhysicsServer3D.BodyGetDirectState(was.GetRid());
            body.GlobalTransform = state.Transform;
            body.LinearVelocity = state.LinearVelocity;
            body.AngularVelocity = state.AngularVelocity;
        }
        foreach (var rope in _ropes)
            if (old._ropes.FirstOrDefault(r => r.Spec.Id == rope.Spec.Id) is { } was
                && was.Spec.From == rope.Spec.From && was.Spec.To == rope.Spec.To && was.Spec.Length == rope.Spec.Length)
            {
                rope.Wound = was.Wound;
                rope.Released = was.Released;
                rope.Broken = was.Broken;
                rope.ArmTurned = was.ArmTurned;
                foreach (var seg in rope.Segments) seg.Visible = !rope.Released && !rope.Broken;
            }
        _initialMechanicalEnergy = old._initialMechanicalEnergy;
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
            foreach (var (id, src) in Runtime.Sources) bits.Add($"{id} brings {src.Flow * 1000:0.#} L/s");
            foreach (var (id, (v, flow)) in Runtime.FloatValves)
                bits.Add($"{id} {v.Opening * 100:F0}% open, {flow() * 1000:F2} L/s, holding {v.Tank.Name} at {v.Tank.Level * 100:F2} cm (shuts at {v.ShutLevel * 100:0.#})");
            foreach (var (id, l) in Runtime.Leaks)
                bits.Add($"{id} {l.Flow * 1000:F2} L/s, {l.Head * 100:F1} cm over the hole, {l.Lost * 1000:F1} L out" + (l.Evaporation > 0 ? $", {l.Evaporated * 1000:F1} L seeped" : ""));
            foreach (var (id, c) in Runtime.Channels) bits.Add($"{id} {c.Flow * 1000:0.#} L/s, {c.Depth * 100:0.#} cm deep at {c.Velocity:F2} m/s");
            foreach (var air in Runtime.AirPockets) bits.Add($"air {air.GaugePressure / 1000:F2} kPa");
            foreach (var (pipe, _, _) in _jets) bits.Add($"{pipe.Name} jet {(pipe.Flow > 0 ? pipe.JetHeight * 100 : 0):F1} cm");
            foreach (var (id, b) in Runtime.Boilers)
                bits.Add(b.Burst
                    ? $"{id} BURST at {b.BurstGauge / 1000:F1} kPa, {b.BurstTime:F1} s: {b.Flashed:F3} kg flashed to steam"
                    : $"{id} {b.Temperature:F1} °C {b.GaugePressure / 1000:F1} kPa, fire {(b.HeatInput > 0 ? "on" : "off")}" +
                      (b.Rating > 0 ? $", rated {b.Rating / 1000:0.#} kPa" + (b.BurstLimit < b.Rating ? $", holds {b.BurstLimit / 1000:0.#} kPa at this heat" : "") : ""));
            foreach (var (id, (v, _)) in Runtime.SafetyValves)
                bits.Add($"{id} {v.Opening * 100:F0}% open, venting {v.Flow * 1000:F2} g/s, {v.Vented:F3} kg out (lifts at {v.LiftPressure / 1000:0.#} kPa)");
            foreach (var (id, p) in Runtime.Pumps)
                bits.Add($"{id} {p.Strokes} strokes, {p.Delivered * 1000:F1} L lifted, lift {p.SuctionLift:F2} m of {p.Limit:F2} m" +
                         (p.Broken ? ", column broken" : "") + $", {p.MaxPull:F0} N on the rod at most" + (p.Stalled ? ", STALLED" : ""));
            foreach (var (id, r) in Runtime.Rotors) bits.Add($"{id} {r.Rpm:F0} rpm");
            foreach (var (id, w) in Runtime.WaterWheels) bits.Add($"{id} {w.Rpm:F1} rpm, {w.Power:F0} W, {w.Water:F1} kg aboard");
            if (!Runtime.Planet.IsEarth)
                bits.Add($"on {Runtime.Planet.Name}: g {Runtime.Outside.Gravity:0.##} m/s², air {Runtime.Outside.Pressure:0.#} Pa, " +
                         $"{Runtime.Outside.AirDensity:0.####} kg/m³, {Runtime.Outside.OxygenFraction * 100:0.##}% O₂, water boils at {Runtime.Outside.BoilingPoint:0.#} °C");
            if (Runtime.Ambient != 20 || Runtime.Tanks.Values.Any(t => t.Ice > 0)) bits.Add($"air {Runtime.Ambient:0.#} °C");
            foreach (var (id, t) in Runtime.Tanks.Where(kv => kv.Value.Ice > 0))
                bits.Add($"{id} iced {t.Ice * 1000:F1} mm" + (t.FrozenSolid ? ", frozen solid" : $", {t.WaterVolume * 1000:F0} L still water"));
            if (Runtime.Weather is { } wx)
                bits.Add($"sol {Runtime.Sun.SolNumber}, {(int)Runtime.Sun.Time:00}:{(int)(Runtime.Sun.Time % 1 * 60):00} local, air {Runtime.Ambient:0} °C" +
                         (wx.Storm is { } st ? $", dust storm (τ {st.Tau:0.#})" : $", clear (τ {wx.Dust:0.##})") +
                         (wx.Relay ? ", RELAY PASS" : $", next pass in {wx.NextPass:0.#} h"));
            if (Runtime.SunShown)
            {
                var sun = Runtime.Sun;
                string[] compass = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
                bits.Add(sun.Elevation > 0
                    ? $"sun {sun.Elevation:F1}° up in the {compass[(int)Math.Round(sun.Azimuth / 45) % 8]}, {sun.DirectNormal:F0} W/m², {(int)sun.Time:00}:{(int)(sun.Time % 1 * 60):00} on day {sun.Day}"
                    : $"night, {(int)sun.Time:00}:{(int)(sun.Time % 1 * 60):00} on day {sun.Day}");
            }
            foreach (var (id, m) in Runtime.Mirrors)
                bits.Add($"{id} throws {m.Power:F0} W (cos {m.Cosine:F3}), {m.Collected / 1000:F1} kJ so far");
            foreach (var (id, c) in Runtime.Capstans)
                bits.Add($"{id} {c.Turns:0.##} turns (x{c.Ratio:0.#}): " + (c.Held ? $"held by {c.Hold:0.#} N (needs {c.LeastHold:0.#} N)"
                    : c.Grounded ? "load on the ground" : $"{(c.Velocity < 0 ? "running out" : "coming in")} at {Math.Abs(c.Velocity):F2} m/s, {c.LoadTension:0} N at the load"));
            foreach (var (id, m) in Runtime.Windmills)
                bits.Add($"{id} {m.Rpm:F1} rpm in {m.Wind:0.#} m/s, {m.Power / 1000:F2} kW of the wind's {m.WindPower / 1000:F1} kW (Cp {m.PowerCoefficient:F3}, Betz 0.593)");
            foreach (var (id, p) in Runtime.Pendulums)
                bits.Add($"{id} {p.Angle * 180 / Math.PI:F1}° (last turned at {p.Amplitude * 180 / Math.PI:F2}°, {p.Swings} swings), bearing {p.Bearing.Heat:F2} J heat, {p.Bearing.Wear:E2} mm³ worn");
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
    /// energy is ½mv² of each body's centre of mass plus ½ω·Iω of its spin
    /// about it (#80).
    /// </summary>
    public EnergySummary Energy()
    {
        double rotorKe = Runtime.Rotors.Values.Sum(r => r.KineticEnergy) + Runtime.WaterWheels.Values.Sum(w => w.KineticEnergy)
                         + Runtime.Windmills.Values.Sum(m => m.KineticEnergy);
        // each body's centre of mass moving, and its spin about it (a wheel's is all spin). Without the spin a
        // counterweight tumbling on its chain, slowed by the chain's pull, showed its spin turned into height
        // as a 45 J gain the trebuchet never made (#80).
        double bodyKe = _freezable.Sum(b => 0.5 * b.Mass * b.LinearVelocity.LengthSquared() + SpinEnergy(b))
                        + BearingPendulumEnergy(out double bearingPe);
        // The real centre of mass, not the body's own origin: a pendulum's
        // RigidBody3D origin sits fixed at the pivot for the joint, so its
        // Y never changes — using it directly would make PE constant and
        // silently ignore the entire swing.
        double pe = _freezable.Sum(b =>
            b.Mass * (float)Runtime.Outside.Gravity * (b.GlobalTransform * _comOffset.GetValueOrDefault(b, Vector3.Zero)).Y);
        // Water has real gravitational PE too — without this, a fluid
        // machine like Heron's fountain (no rigid bodies, no rotor) shows
        // zero energy and a blank speed the whole time it's running.
        pe += bearingPe;
        pe += Runtime.Tanks.Values.Sum(t => t.WaterVolume * Physics.WaterDensity * Runtime.Outside.Gravity * (t.BaseElevation + t.Level / 2));
        // A twisted torsion spring stores ½·k·(θ − rest)²: counted, or a
        // catapult would seem to make energy from nothing when loosed.
        pe += _springs.Sum(sp => 0.5 * sp.Stiffness * Math.Pow(sp.Angle - sp.Rest, 2));
        double thermal = Runtime.Boilers.Values.Sum(b => b.HeatDelivered);

        string speed = Runtime.Rotors.Count > 0
            ? $"{Runtime.Rotors.Values.First().Rpm:F0} rpm"
            : Runtime.WaterWheels.Count > 0
                ? string.Join(", ", Runtime.WaterWheels.Values.Select(w => $"{w.Name} {w.Rpm:F1} rpm"))
            : Runtime.Capstans.Count > 0
                ? $"{Runtime.Capstans.Values.Max(c => Math.Abs(c.Velocity)):F2} m/s"
            : Runtime.Windmills.Count > 0
                ? string.Join(", ", Runtime.Windmills.Values.Select(m => $"{m.Name} {m.Rpm:F1} rpm"))
            : _axles.Count > 0
                ? $"{_axles.Max(a => Math.Abs(AxleRpm(a.Body, a.Axis))):F1} rpm"
            : _freezable.Count > 0
                ? $"{_freezable.Max(b => b.LinearVelocity.Length()):F2} m/s"
            : Runtime.Pumps.Count > 0
                ? $"{Runtime.Pumps.Values.Max(p => p.Rpm):F0} strokes/min"
            : Runtime.Pendulums.Count > 0
                ? $"{Runtime.Pendulums.Values.Max(p => Math.Abs(p.AngularVelocity) * p.Length):F2} m/s"
                : Runtime.Pipes.Count > 0
                    ? $"{Runtime.Pipes.Values.Max(p => Math.Abs(p.Flow)) * 1000:F2} L/s"
                    : "—";

        double? efficiency = Runtime.Boilers.Count > 0 && Runtime.Rotors.Count > 0 && thermal > 1e-6
            ? rotorKe / thermal * 100
            : null;

        // Not for a driven machine: whoever turns the crank (or the river
        // under a noria, a spring filling a tank, a pump's crank) keeps adding energy, so "retained" means nothing.
        double? retained = Runtime.Boilers.Count == 0 && !_axles.Any(a => a.Driven) && _liftDrives.Count == 0 && Runtime.Pumps.Count == 0
                           && Runtime.Sources.Count == 0 && Runtime.WaterWheels.Count == 0 && Runtime.Windmills.Count == 0 && Runtime.Hearths.Count == 0
                           && _initialMechanicalEnergy is { } init && init > 1e-6
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
