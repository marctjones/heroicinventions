using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// A bimetal strip you can see bend (issue #97): two bonded layers drawn side by side along the arc the strip really takes
/// (Timoshenko's curvature, <see cref="BimetalStrip.NowCurvature"/>), the first layer's metal outside, the second's inside, with a
/// clamp at the fixed end. The real tip moves only about 60 micrometres per kelvin on a 100 mm strip, which would not show, so
/// the bend is drawn <see cref="BendExaggeration"/> times too strong (the curvature is multiplied; the length is true) and the layers
/// are drawn <see cref="ThicknessExaggeration"/> times as thick as they are (legibility over realism; the physics is the real one).
/// Two pins mark the tip where the lid it works is shut (red) and wide open (blue), with the same exaggeration, so the strip is seen to
/// travel between them; a label gives the strip's temperature, the real tip deflection and the lid.
/// </summary>
public partial class MachineView
{
    /// <summary>How many times too strong a strip's bend is drawn.</summary>
    public const float BendExaggeration = 10f;
    /// <summary>How many times too thick its layers are drawn.</summary>
    public const float ThicknessExaggeration = 4f;
    private const int StripSegments = 16;

    private sealed record StripView(BimetalStrip Strip, Node3D Root, MeshInstance3D[] HighLayer, MeshInstance3D[] LowLayer, Node3D ShutPin, Node3D OpenPin, Label3D Label, float Thickness);
    private readonly List<StripView> _stripViews = [];

    private void BuildBimetals()
    {
        foreach (var (id, strip) in Runtime.Bimetals)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            float th = Mathf.Max((float)strip.Thickness * ThicknessExaggeration, 0.006f) / 2, width = Mathf.Max((float)strip.Width, 0.02f);
            var root = new Node3D { Position = V(part.At) };
            AddChild(root);
            var highMat = Surface(strip.High.Id);
            var lowMat = Surface(strip.Low.Id);
            float seg = (float)strip.Length / StripSegments;
            var high = new MeshInstance3D[StripSegments];
            var low = new MeshInstance3D[StripSegments];
            for (int i = 0; i < StripSegments; i++)
            {
                high[i] = Shapes.Box(new Vector3(seg * 1.04f, th, width), highMat); root.AddChild(high[i]);
                low[i] = Shapes.Box(new Vector3(seg * 1.04f, th, width), lowMat); root.AddChild(low[i]);
            }
            // the clamp, a dark block at the fixed end
            var clamp = Shapes.Box(new Vector3(0.03f, th * 2 + 0.03f, width + 0.01f), Shapes.Mat(new Color(0.18f, 0.18f, 0.2f)));
            clamp.Position = new Vector3(-0.015f, 0, 0);
            root.AddChild(clamp);
            Node3D Pin(Color c)
            {
                var pin = Shapes.Box(new Vector3(0.012f, 0.012f, width + 0.02f), Shapes.Mat(c, outline: false));
                root.AddChild(pin);
                return pin;
            }
            var shutPin = Pin(new Color(0.85f, 0.2f, 0.15f)); var openPin = Pin(new Color(0.2f, 0.45f, 0.9f));
            var label = new Label3D
            {
                FontSize = 22, OutlineSize = 6, PixelSize = 0.0035f, NoDepthTest = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Position = new Vector3((float)strip.Length / 2, 0.14f, 0),
            };
            root.AddChild(label);
            _stripViews.Add(new StripView(strip, root, high, low, shutPin, openPin, label, th));
        }
        _building = null;
    }

    /// <summary>The point at arclength s along an arc of curvature k from the origin, heading +x, curling toward +y.</summary>
    private static (float X, float Y, float Angle) OnArc(float k, float s) =>
        Mathf.Abs(k) < 1e-6f ? (s, 0, 0) : (Mathf.Sin(k * s) / k, (1 - Mathf.Cos(k * s)) / k, k * s);

    private void DrawBimetals()
    {
        foreach (var v in _stripViews)
        {
            var b = v.Strip;
            float seg = (float)b.Length / StripSegments;
            // the second layer (the one that expands less) is inside the bend: on the +y side
            float k = (float)b.NowCurvature * BendExaggeration;
            for (int i = 0; i < StripSegments; i++)
            {
                var (x, y, a) = OnArc(k, seg * (i + 0.5f));
                var normal = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0);
                var centre = new Vector3(x, y, 0);
                v.HighLayer[i].Position = centre - normal * v.Thickness / 2;
                v.LowLayer[i].Position = centre + normal * v.Thickness / 2;
                v.HighLayer[i].Rotation = v.LowLayer[i].Rotation = new Vector3(0, 0, a);
            }
            void Place(Node3D pin, double tempC)
            {
                var (x, y, _) = OnArc((float)b.CurvatureAt(tempC) * BendExaggeration, (float)b.Length);
                pin.Position = new Vector3(x, y, 0);
            }
            Place(v.ShutPin, b.ShutAt);
            Place(v.OpenPin, b.OpenAt);
            v.Label.Text = $"{b.Name}: {b.Temperature:0.0} °C, tip {b.Deflection * 1000:+0.00;-0.00} mm (bend drawn x{BendExaggeration:0})\nlid {b.Opening * 100:0}% open";
        }
    }
}
