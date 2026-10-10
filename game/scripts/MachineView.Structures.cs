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
    private readonly List<(Hearth hearth, double initialFuel, Node3D logs, StandardMaterial3D glow, Node3D flames)> _hearthViews = [];

    private void BuildPost(PartSpec part)
    {
        float w = (float)part.Number("size-x"), h = (float)part.Number("size-y"), d = (float)part.Number("size-z");
        bool round = part.Props.TryGetValue("round", out var r) && r is SBool { Value: true };
        var body = new StaticBody3D
        {
            Transform = new Transform3D(YawOf(part), V(part.At) + new Vector3(0, h / 2, 0)),
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = (float)_materials[part.Material].Friction },
        };
        Shape3D shape = round ? new CylinderShape3D { Radius = w / 2, Height = h } : new BoxShape3D { Size = new Vector3(w, h, d) };
        body.AddChild(new CollisionShape3D { Shape = shape });
        body.AddChild(round ? Shapes.Cylinder(w / 2, h, Surface(part.Material)) : Shapes.Box(new Vector3(w, h, d), Surface(part.Material)));
        AddChild(body);
        // a post standing on the ground gets a stone footing a little wider than itself: a made thing set into the
        // ground, and a colour of its own whatever it's made of (#165). Only drawn.
        if (Mathf.Abs((float)part.At.Y) < 0.01f)
        {
            var footing = Shapes.Box(new Vector3(w * 1.6f, Mathf.Min(0.06f, h * 0.08f), d * 1.6f), Surface("granite"));
            footing.Transform = new Transform3D(YawOf(part), V(part.At) + new Vector3(0, Mathf.Min(0.03f, h * 0.04f), 0));
            AddChild(footing);
        }
        _surfaceMaterials[body.GetInstanceId()] = part.Material;
        RegisterBreakable(part, body);
        AddLabel(part.Id, V(part.At) + new Vector3(0, h + 0.08f, 0));
    }

    private void BuildHearth(PartSpec part)
    {
        var hearth = Runtime.Hearths[part.Id];
        var heated = Runtime.Def.Part(part.Symbol("heats", ""))!;
        float radius = heated.Kind switch
        {
            "boiler" => (float)heated.Number("radius"),
            "enclosure" => 0.25f,                     // a stove standing in the room it heats
            _ => Mathf.Sqrt((float)heated.Number("area")) / 3,
        };
        var centre = V(part.At);
        var stone = Surface("granite");
        const int stones = 10;
        for (int i = 0; i < stones; i++)
        {
            float a = i * Mathf.Tau / stones;
            var size = new Vector3(radius * 0.45f, 0.1f, radius * 0.3f);
            var s = new MeshInstance3D { Mesh = BlockLooks.Truncated(size, c => Mathf.Min(size.Y, size.Z) * (0.25f + 0.04f * ((i + c) % 4))), MaterialOverride = stone };   // fieldstones, not bricks (12.20)
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
        // the fire (12.20; it was one glowing box): a bed of embers, split logs crossed on it in two layers (lumps for charcoal or coal), and flames over them while
        // it burns. The logs shrink as the fuel goes; the embers glow and the flames stand taller with the draught.
        var bed = Shapes.Cylinder(radius * 0.75f, 0.02f, glow);
        bed.Scale = new Vector3(1, 1, 0.8f);
        bed.Position = centre + new Vector3(0, 0.01f, 0);
        AddChild(bed);
        var logs = new Node3D { Position = centre + new Vector3(0, 0.02f, 0) };
        AddChild(logs);
        var bark = Shapes.Mat(Color.FromHtml("#5A3E2B"), roughness: 1f);
        var cut = Shapes.Mat(Color.FromHtml("#C89A62"), roughness: 0.9f, outline: false);
        float d = Mathf.Clamp(radius * 0.2f, 0.035f, 0.08f), len = radius * 1.35f;
        if (hearth.FuelKind != "wood")
        {
            // charcoal or coal: a heap of black lumps (faceted, as the rock is), the middle one on top
            var lump = Shapes.Mat(Color.FromHtml(hearth.FuelKind == "coal" ? "#22201F" : "#2F2A27"), roughness: 0.7f);
            int k = 0;
            foreach (var (x, y, z) in new[] { (-0.3f, 0f, -0.2f), (0.3f, 0f, -0.25f), (-0.25f, 0f, 0.28f), (0.28f, 0f, 0.22f), (0f, 0f, 0f), (-0.05f, 0.8f, 0.05f), (0.12f, 0.7f, -0.1f) })
            {
                var piece = new MeshInstance3D { Mesh = BlockLooks.Truncated(Vector3.One * d * 1.3f, c => d * (0.2f + 0.05f * ((k + c) % 3))), MaterialOverride = lump };
                piece.Position = new Vector3(x * len, d * 0.65f + y * d, z * len);
                piece.Rotation = new Vector3(0.4f * k, 0.9f * k, 0.2f * k);
                logs.AddChild(piece);
                k++;
            }
        }
        else for (int layer = 0; layer < 2; layer++)
            foreach (float off in new[] { -0.3f, 0.3f })
            {
                // the lower pair runs along X, the upper across it, as a fire is laid
                var log = new Node3D { Position = new Vector3(0, d / 2 + layer * d * 0.9f, 0) + (layer == 0 ? new Vector3(0, 0, off * len) : new Vector3(off * len, 0, 0)) };
                log.Basis = layer == 0 ? new Basis(Vector3.Back, Mathf.Pi / 2) : new Basis(Vector3.Right, Mathf.Pi / 2);
                log.AddChild(Shapes.Cylinder(d / 2, len, bark));
                foreach (float end in new[] { -1f, 1f })
                {
                    var face = Shapes.Cylinder(d / 2 * 0.8f, 0.004f, cut);
                    face.Position = new Vector3(0, end * len / 2, 0);
                    log.AddChild(face);
                }
                logs.AddChild(log);
            }
        // flames: three tongues, yellow inside orange, unshaded so they read as light in any weather
        var flames = new Node3D { Position = centre + new Vector3(0, 0.02f + d, 0), Visible = false };
        AddChild(flames);
        var orange = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.45f, 0.08f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var yellow = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.3f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        float tall = Mathf.Clamp(radius * 0.9f, 0.12f, 0.35f);
        foreach (var (x, z, k) in new[] { (0f, 0f, 1f), (-0.3f, 0.15f, 0.7f), (0.32f, -0.1f, 0.75f) })
        {
            var tongue = new Node3D { Position = new Vector3(x * radius, 0, z * radius) };
            var outer = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = radius * 0.28f * k, Height = tall * k, RadialSegments = 8 }, MaterialOverride = orange, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            outer.Position = new Vector3(0, tall * k / 2, 0);
            var inner = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = radius * 0.15f * k, Height = tall * k * 0.6f, RadialSegments = 8 }, MaterialOverride = yellow, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            inner.Position = new Vector3(0, tall * k * 0.3f, 0);
            tongue.AddChild(outer);
            tongue.AddChild(inner);
            flames.AddChild(tongue);
        }
        _hearthViews.Add((hearth, Math.Max(hearth.Fuel, 1e-9), logs, glow, flames));
        AddLabel(part.Id, centre + new Vector3(radius * 1.6f, 0.2f, 0));
    }

    private void DrawHearths()
    {
        foreach (var (hearth, initial, logs, glow, flames) in _hearthViews)
        {
            float left = (float)Math.Clamp(hearth.Fuel / initial, 0, 1);
            logs.Scale = Vector3.One * (0.25f + 0.75f * left);   // burning down, never quite to nothing (the ash and the last brands)
            float draught = (float)hearth.Draught;
            float flicker = 1.2f + 0.5f * (float)Math.Sin(Runtime.Time * 9 * draught);
            glow.EmissionEnergyMultiplier = hearth.Lit ? draught * flicker : 0;
            flames.Visible = hearth.Lit && draught > 0.01f;
            if (flames.Visible)
                for (int i = 0; i < flames.GetChildCount(); i++)
                {
                    var tongue = (Node3D)flames.GetChild(i);
                    float f = Mathf.Clamp(draught, 0.3f, 1.5f) * (0.85f + 0.15f * (float)Math.Sin(Runtime.Time * 11 + i * 2.1));
                    tongue.Scale = new Vector3(1, f, 1) * (0.4f + 0.6f * left);
                }
        }
    }
}
