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

    private readonly List<(GalvanicJar Jar, StandardMaterial3D Rod, Color Fresh, Label3D Label, StandardMaterial3D Filament, StandardMaterial3D Halo)> _jarViews = [];

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
        var (filament, halo) = BuildLamp(at, jar.Cells);
        _jarViews.Add((jar, rod, fresh, label, filament, halo));
    }

    /// <summary>
    /// The load the current is seen in (#176): a glass bulb on a stand beside the row, wired across its first and last
    /// jar, its filament glowing by the power the jars give (<see cref="GalvanicJar.FilamentCelsius"/>, through
    /// <see cref="Skins.Glow"/>). Dull red for one jar, white-yellow for ten, black once the acid is spent.
    /// </summary>
    private (StandardMaterial3D Filament, StandardMaterial3D Halo) BuildLamp(Vector3 at, int cells)
    {
        // a lamp the size of a jar, so it reads at the distance the row does
        float half = (cells - 1) / 2f * JarSpacing;
        float side = cells > 1 ? 1 : -1;   // beside the row's end, clear of the labels above it: right of a stack, left of a single jar
        var lamp = at + new Vector3(side * (half + 0.24f), 0, 0);
        var copper = Shapes.Mat(Shapes.Copper, metallic: 0.7f, roughness: 0.4f);
        var stand = Shapes.Cylinder(0.03f, 0.10f, Shapes.Mat(Bitumen, roughness: 0.5f));
        stand.Position = lamp + new Vector3(0, 0.05f, 0);
        AddChild(stand);
        var filament = Shapes.Mat(new Color(0.12f, 0.1f, 0.1f), roughness: 0.6f, outline: false);
        var coil = Shapes.Rod(lamp + new Vector3(-0.035f, 0.20f, 0), lamp + new Vector3(0.035f, 0.20f, 0), 0.014f, filament);
        AddChild(coil);
        foreach (float x in new[] { -0.035f, 0.035f })   // the posts the filament is strung between
            AddChild(Shapes.Rod(lamp + new Vector3(x, 0.10f, 0), lamp + new Vector3(x, 0.20f, 0), 0.004f, copper));
        // the light the filament gives, a soft ball round it that brightens with its heat (drawn only)
        var halo = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                                            BlendMode = BaseMaterial3D.BlendModeEnum.Mix, AlbedoColor = new Color(1, 1, 1, 0), NoDepthTest = true };
        var glowBall = Shapes.Sphere(0.11f, halo);
        glowBall.Position = lamp + new Vector3(0, 0.20f, 0);
        AddChild(glowBall);
        var bulb = Shapes.Sphere(0.075f, Shapes.Glass(0.32f));
        bulb.Position = lamp + new Vector3(0, 0.20f, 0);
        AddChild(bulb);
        // leads: from the first jar's rod, and from the last jar's copper tube, down to the stand
        var rodTop = at + new Vector3(side * half, JarHeight + 0.05f, 0);
        var tube = at + new Vector3(-side * half, JarHeight * 0.4f, JarRadius);
        AddChild(Shapes.Rod(rodTop, lamp + new Vector3(-side * 0.035f, 0.10f, 0), 0.004f, copper));
        AddChild(Shapes.Rod(tube, lamp + new Vector3(side * 0.035f, 0.10f, 0), 0.004f, copper));
        return (filament, halo);
    }

    private void DrawGalvanicJars()
    {
        foreach (var (jar, rod, fresh, label, filament, halo) in _jarViews)
        {
            Skins.Glow(filament, jar.FilamentCelsius);
            var (light, energy) = Skins.Incandescence(jar.FilamentCelsius);
            halo.AlbedoColor = new Color(light.R, light.G, light.B, energy > 0 ? 0.25f + 0.5f * Mathf.Sqrt(Mathf.Clamp(energy / 4f, 0, 1)) : 0);
            rod.AlbedoColor = fresh.Lerp(Rust, (float)jar.SpentFraction);
            rod.Metallic = 0.6f * (1 - (float)jar.SpentFraction);
            string used = jar.SpentFraction > 0 ? $" · {jar.SpentFraction * 100:0.#}% spent" : "";
            label.Text = jar.Spent ? $"{jar.Name}\nspent"
                : jar.On ? $"{jar.Name}\n{jar.Voltage:0.##} V · {jar.Current * 1000:0.##} mA{used}\nlamp {jar.FilamentCelsius:0} °C"
                : $"{jar.Name}\noff ({jar.Voltage:0.##} V){used}";
        }
    }
}
