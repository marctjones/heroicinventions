using Godot;

namespace HeroicInventions;

/// <summary>
/// The rover's cargo crates you can tell apart (art direction 12.19). A crate of the Lonely Rover's cargo (cargo-crate, found-bank,
/// found-motor) is one oak block to the physics; the view dresses it as a shipping crate and says what it holds, so the player knows a
/// crate at a glance, by colour from far off and by its lid close up, and a lid the backhoe uncovers is seen at once against the rubble:
/// <list type="bullet">
/// <item>dark battens along its twelve edges (a crate, not a block of wood or a boulder);</item>
/// <item>a band round the top of its sides in the colour of what it holds (<see cref="CargoLooks"/>, by the placement's label);</item>
/// <item>a sign on the lid of what is in it: a solar panel's cells, gas cylinders, crossed tools, a battery's terminals (the motors'
/// crate carries its motor on the lid already).</item>
/// </list>
/// Look only: every piece rides on the crate's body as scenery (no collision, no mass, never picked, never in the reach box: the
/// rover's reach is still measured to the 50 cm block), and nothing is drawn while the ground holds the crate under it (#240: the
/// rover knows roughly where a crate lies, never how deep).
/// </summary>
public partial class MachineView
{
    /// <summary>What a crate holds, as the view draws it: the band's colour and the sign on the lid. Keyed by the world's placement label.</summary>
    private enum LidSign { None, Panel, Cylinders, Tools, Terminals }
    private static readonly Dictionary<string, (Color Band, LidSign Sign)> CargoLooks = new()
    {
        ["battery-bank"] = (new Color(0.55f, 0.36f, 0.85f), LidSign.Terminals),   // violet: none of the bank's own state colours
        ["solar-panels"] = (new Color(0.16f, 0.42f, 0.86f), LidSign.Panel),       // a panel's blue
        ["gas-cylinders"] = (new Color(0.93f, 0.93f, 0.90f), LidSign.Cylinders),  // white, as gas cylinders are painted
        ["hand-tools"] = (new Color(0.20f, 0.66f, 0.30f), LidSign.Tools),         // green
        ["motors"] = (new Color(0.96f, 0.82f, 0.10f), LidSign.None),              // yellow; the motor stands on its lid
    };
    private static readonly string[] CargoMachineNames = ["cargo-crate", "found-bank", "found-motor"];

    private void BuildCargoMarkings()
    {
        if (!CargoMachineNames.Contains(Runtime.Def.Name) || _bodiesById.GetValueOrDefault("crate") is not { } crate) return;
        float s = (float)(Runtime.Def.Part("crate")?.Number("size", 0.5) ?? 0.5), h = s / 2;
        // by the placement's label in a world; a found bank or motor shown on its own (its view named for the machine) as in the world
        string holds = CargoLooks.ContainsKey(Name) ? Name : Runtime.Def.Name switch { "found-bank" => "battery-bank", "found-motor" => "motors", _ => Name };
        var (band, sign) = CargoLooks.GetValueOrDefault(holds, (new Color(0.35f, 0.24f, 0.14f), LidSign.None));
        // battens: square strips along the twelve edges, a darker oak, standing a little proud of the faces
        float b = s * 0.09f, e = h - b * 0.3f;
        var battens = new List<(Vector3, Vector3)>();
        foreach (float x in new[] { -e, e })
            foreach (float z in new[] { -e, e })
                battens.Add((new Vector3(x, 0, z), new Vector3(b, s * 1.01f, b)));
        foreach (float y in new[] { -e, e })
            foreach (float o in new[] { -e, e })
            {
                battens.Add((new Vector3(0, y, o), new Vector3(s * 1.01f, b, b)));
                battens.Add((new Vector3(o, y, 0), new Vector3(b, b, s * 1.01f)));
            }
        Decorate(crate, Merged(battens, Shapes.Mat(Skins.ColorOf("oak").Darkened(0.45f), roughness: 0.9f)));
        // the band round the sides under the top battens: a box a hair wider than the crate, its top and bottom hidden inside it
        var ring = Shapes.Box(new Vector3(s * 1.012f, s * 0.14f, s * 1.012f), Shapes.Mat(band, roughness: 0.7f, outline: false));
        ring.Position = new Vector3(0, h - b - s * 0.08f, 0);
        Decorate(crate, ring);
        // the sign on the lid, between the top battens
        float top = h + 0.004f;
        switch (sign)
        {
            case LidSign.Panel:   // a dark blue panel, its cells ruled in pale lines
                var panel = Shapes.Box(new Vector3(s * 0.66f, 0.012f, s * 0.5f), Shapes.Mat(new Color(0.10f, 0.20f, 0.45f), metallic: 0.3f, roughness: 0.3f, outline: false));
                panel.Position = new Vector3(0, top, 0);
                Decorate(crate, panel);
                var lines = new List<(Vector3, Vector3)>();
                for (int i = -1; i <= 1; i++) lines.Add((new Vector3(i * s * 0.165f, top + 0.007f, 0), new Vector3(0.006f, 0.002f, s * 0.5f)));
                lines.Add((new Vector3(0, top + 0.007f, 0), new Vector3(s * 0.66f, 0.002f, 0.006f)));
                Decorate(crate, Merged(lines, Shapes.Mat(new Color(0.70f, 0.80f, 0.95f), outline: false)));
                break;
            case LidSign.Cylinders:   // three cylinders lying side by side, grey with a white shoulder
                for (int i = -1; i <= 1; i++)
                {
                    float r = s * 0.07f;
                    var bottle = Shapes.Cylinder(r, s * 0.62f, Shapes.Mat(new Color(0.55f, 0.58f, 0.62f), metallic: 0.4f, roughness: 0.4f));
                    bottle.Basis = new Basis(Vector3.Back, Mathf.Pi / 2);
                    bottle.Position = new Vector3(0, top + r, i * s * 0.17f);
                    Decorate(crate, bottle);
                    var shoulder = Shapes.Cylinder(r * 1.05f, s * 0.1f, Shapes.Mat(band, outline: false));
                    shoulder.Basis = bottle.Basis;
                    shoulder.Position = bottle.Position + new Vector3(s * 0.25f, 0, 0);
                    Decorate(crate, shoulder);
                }
                break;
            case LidSign.Tools:   // a hammer and a spanner, crossed
                var steel = Shapes.Mat(new Color(0.70f, 0.72f, 0.75f), metallic: 0.5f, roughness: 0.35f);
                foreach (float turn in new[] { -1f, 1f })
                {
                    var tool = new Node3D { Position = new Vector3(0, top + 0.012f, 0), Basis = new Basis(Vector3.Up, turn * Mathf.Pi / 4) };
                    var shaft = Shapes.Box(new Vector3(s * 0.62f, 0.02f, s * 0.06f), steel);
                    tool.AddChild(shaft);
                    var head = Shapes.Box(new Vector3(s * 0.08f, 0.03f, s * 0.2f), turn < 0 ? steel : Shapes.Mat(new Color(0.25f, 0.25f, 0.28f), metallic: 0.5f, roughness: 0.4f));
                    head.Position = new Vector3(s * 0.3f, 0, 0);
                    tool.AddChild(head);
                    Decorate(crate, tool);
                }
                break;
            case LidSign.Terminals:   // a battery's two posts, red and black, either side of the bank's lamp
                foreach (var (x, c) in new[] { (-s * 0.28f, new Color(0.85f, 0.15f, 0.12f)), (s * 0.28f, new Color(0.08f, 0.08f, 0.09f)) })
                {
                    var post = Shapes.Cylinder(s * 0.06f, s * 0.1f, Shapes.Mat(c, metallic: 0.3f, roughness: 0.4f));
                    post.Position = new Vector3(x, top + s * 0.05f, s * 0.22f);
                    Decorate(crate, post);
                }
                break;
        }
    }

    /// <summary>A piece of look on a body: it rides with it and is scenery (never picked, never in a part's reach box).</summary>
    private static void Decorate(Node3D body, Node3D piece)
    {
        MarkScenery(piece);
        body.AddChild(piece);
    }

    /// <summary>Boxes merged into one mesh in one material (one outline round the lot).</summary>
    private static MeshInstance3D Merged(IEnumerable<(Vector3 Centre, Vector3 Size)> boxes, StandardMaterial3D mat)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (c, size) in boxes) st.AppendFrom(new BoxMesh { Size = size }, 0, new Transform3D(Basis.Identity, c));
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = mat };
    }
}
