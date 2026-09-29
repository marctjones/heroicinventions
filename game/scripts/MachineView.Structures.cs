using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Things that stand still: posts, piers and walls (static bodies other parts
/// rest on), and the hearth — a ring of stones with a heap of logs that
/// shrinks as the fire burns them, glowing while it is lit.
/// </summary>
public partial class MachineView
{
    private readonly List<(Hearth hearth, double initialFuel, MeshInstance3D logs, StandardMaterial3D glow)> _hearthViews = [];

    private void BuildPost(PartSpec part)
    {
        float w = (float)part.Number("size-x"), h = (float)part.Number("size-y"), d = (float)part.Number("size-z");
        bool round = part.Props.TryGetValue("round", out var r) && r is SBool { Value: true };
        var body = new StaticBody3D
        {
            Position = V(part.At) + new Vector3(0, h / 2, 0),
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = (float)_materials[part.Material].Friction },
        };
        Shape3D shape = round ? new CylinderShape3D { Radius = w / 2, Height = h } : new BoxShape3D { Size = new Vector3(w, h, d) };
        body.AddChild(new CollisionShape3D { Shape = shape });
        body.AddChild(round ? Shapes.Cylinder(w / 2, h, Surface(part.Material)) : Shapes.Box(new Vector3(w, h, d), Surface(part.Material)));
        AddChild(body);
        AddLabel(part.Id, V(part.At) + new Vector3(0, h + 0.08f, 0));
    }

    private void BuildHearth(PartSpec part)
    {
        var hearth = Runtime.Hearths[part.Id];
        var heated = Runtime.Def.Part(part.Symbol("heats", ""))!;
        float radius = heated.Kind == "boiler" ? (float)heated.Number("radius") : Mathf.Sqrt((float)heated.Number("area")) / 3;
        var centre = V(part.At);
        var stone = Surface("granite");
        const int stones = 10;
        for (int i = 0; i < stones; i++)
        {
            float a = i * Mathf.Tau / stones;
            var s = Shapes.Box(new Vector3(radius * 0.45f, 0.1f, radius * 0.3f), stone);
            s.Position = centre + new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)) * radius * 1.05f;
            s.Rotation = new Vector3(0, -a, 0);
            AddChild(s);
        }
        var glow = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.25f, 0.13f, 0.06f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.3f, 0.05f),
            EmissionEnergyMultiplier = 0,
        };
        var logs = Shapes.Box(new Vector3(radius * 1.4f, 1, radius * 1.1f), glow);
        AddChild(logs);
        logs.Position = centre;
        _hearthViews.Add((hearth, Math.Max(hearth.Fuel, 1e-9), logs, glow));
        AddLabel(part.Id, centre + new Vector3(radius * 1.6f, 0.2f, 0));
    }

    private void DrawHearths()
    {
        foreach (var (hearth, initial, logs, glow) in _hearthViews)
        {
            float heap = 0.02f + 0.16f * (float)Math.Clamp(hearth.Fuel / initial, 0, 1);
            logs.Scale = new Vector3(1, heap, 1);
            logs.Position = new Vector3(logs.Position.X, heap / 2, logs.Position.Z);
            glow.EmissionEnergyMultiplier = hearth.Lit ? 1.2f + 0.5f * (float)Math.Sin(Runtime.Time * 9) : 0;
        }
    }
}
