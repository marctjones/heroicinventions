using Godot;
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
        foreach (var part in Runtime.Def.Parts)
        {
            switch (part.Kind)
            {
                case "tank": BuildTank(part); break;
                case "boiler": BuildBoiler(part); break;
                case "rotor": BuildRotor(part); break;
                case "block": BuildBlock(part); break;
            }
        }
        foreach (var pipe in Runtime.Def.Pipes) BuildPipe(pipe);
        Refresh();
    }

    private static Vector3 V(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private StandardMaterial3D Surface(string materialId) =>
        Shapes.Mat(Shapes.ColorFor(materialId),
                   metallic: _materials[materialId].Category == MaterialCategory.Metal ? 0.8f : 0,
                   roughness: _materials[materialId].Category == MaterialCategory.Metal ? 0.35f : 0.8f);

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

        var jet = Shapes.Cylinder(0.006f, 1, Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.8f));
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
        foreach (int s in new[] { 1, -1 })
        {
            node.AddChild(Shapes.Rod(new Vector3(0, s * radius, 0), new Vector3(0, s * arm, 0), 0.006f, surface));
            node.AddChild(Shapes.Rod(new Vector3(0, s * arm, 0), new Vector3(0, s * arm, s * 0.025f), 0.006f, surface));
        }
        _rotors.Add((Runtime.Rotors[part.Id], node));
    }

    private void BuildBlock(PartSpec part)
    {
        var block = new MaterialBlock(_materials[part.Material], (float)part.Number("size"), Shapes.ColorFor(part.Material))
        {
            Name = part.Id,
            Position = V(part.At),
            Freeze = true,
        };
        AddChild(block);
        Blocks.Add(block);
    }

    public void Simulate(double dt)
    {
        Runtime.Step(dt);
        Refresh();
    }

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
    }

    public void ToggleFire()
    {
        foreach (var (boiler, _) in _fires)
            boiler.HeatInput = boiler.HeatInput > 0 ? 0 : 3000;
    }

    public void SetFrozen(bool frozen)
    {
        foreach (var b in Blocks) b.Freeze = frozen;
    }

    public string Status
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
            return $"{Runtime.Def.Name}: " + string.Join(" · ", bits);
        }
    }
}
