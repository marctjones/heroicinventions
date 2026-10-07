using Godot;
using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Galvanic jars, the "Baghdad battery": a row of clay pots, one per cell,
/// each sealed with bitumen round the iron rod that stands out of its neck.
/// The rod is where the work shows: it starts wrought-iron grey and rusts
/// toward brown as its vinegar is spent, since the iron is what the acid
/// eats. A spent jar's rod is fully rusted and its label says so.
/// </summary>
public partial class MachineView
{
    private const float JarHeight = 0.14f, JarRadius = 0.045f, JarSpacing = 0.12f;
    private static readonly Color Terracotta = new(0.72f, 0.42f, 0.28f);
    private static readonly Color Bitumen = new(0.06f, 0.05f, 0.05f);
    private static readonly Color Rust = new(0.62f, 0.30f, 0.12f);

    private readonly List<(GalvanicJar Jar, StandardMaterial3D Rod, Color Fresh, Label3D Label)> _jarViews = [];

    private void BuildGalvanicJar(PartSpec part)
    {
        if (!Runtime.GalvanicJars.TryGetValue(part.Id, out var jar)) return;
        var at = V(part.At);
        var clay = part.Material == "clay" ? Shapes.Mat(Terracotta, roughness: 0.95f) : Surface(part.Material);
        var seal = Shapes.Mat(Bitumen, roughness: 0.4f);
        var fresh = Shapes.ColorFor("iron");
        var rod = Shapes.Mat(fresh, metallic: 0.6f, roughness: 0.6f);
        // a row of jars along x, centred on #:at, standing on it
        for (int i = 0; i < jar.Cells; i++)
        {
            var foot = at + new Vector3((i - (jar.Cells - 1) / 2f) * JarSpacing, 0, 0);
            var body = Shapes.Cylinder(JarRadius, JarHeight * 0.8f, clay);
            body.Position = foot + new Vector3(0, JarHeight * 0.4f, 0);
            AddChild(body);
            var neck = Shapes.Cylinder(JarRadius * 0.55f, JarHeight * 0.2f, clay);
            neck.Position = foot + new Vector3(0, JarHeight * 0.9f, 0);
            AddChild(neck);
            var plug = Shapes.Cylinder(JarRadius * 0.45f, 0.01f, seal);
            plug.Position = foot + new Vector3(0, JarHeight + 0.005f, 0);
            AddChild(plug);
            var iron = Shapes.Cylinder(0.006f, 0.05f, rod);
            iron.Position = foot + new Vector3(0, JarHeight + 0.025f, 0);
            AddChild(iron);
        }
        var label = new Label3D
        {
            Text = part.Id, FontSize = 24, PixelSize = 0.0012f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            OutlineSize = 6, Position = at + new Vector3(0, JarHeight + 0.09f, 0),
        };
        AddChild(label);
        _jarViews.Add((jar, rod, fresh, label));
    }

    private void DrawGalvanicJars()
    {
        foreach (var (jar, rod, fresh, label) in _jarViews)
        {
            rod.AlbedoColor = fresh.Lerp(Rust, (float)jar.SpentFraction);
            rod.Metallic = 0.6f * (1 - (float)jar.SpentFraction);
            string used = jar.SpentFraction > 0 ? $" · {jar.SpentFraction * 100:0.#}% spent" : "";
            label.Text = jar.Spent ? $"{jar.Name}\nspent"
                : jar.On ? $"{jar.Name}\n{jar.Voltage:0.##} V · {jar.Current * 1000:0.##} mA{used}"
                : $"{jar.Name}\noff ({jar.Voltage:0.##} V){used}";
        }
    }
}
