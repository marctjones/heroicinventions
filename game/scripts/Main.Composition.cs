using Godot;

namespace HeroicInventions;

/// <summary>
/// How a whole machine reads (issue #167), beyond its parts' looks:
/// <list type="bullet">
/// <item>A scale figure: a plain 1.7 m person, the ancient operator, standing beside every machine. Size is the
/// first thing that tells a crane from a model of one. On by default; P shows or hides it.</item>
/// <item>HEROIC_SILHOUETTE=1: every machine drawn solid black on white, no ground, sky or labels: the check that
/// two machines' shapes can be told apart at thumbnail size (a review tool, not a look).</item>
/// </list>
/// The figure is a child of Main, not of the machine, so the machine's own bounds (framing, the framing report)
/// don't include it.
/// </summary>
public partial class Main
{
    private Node3D? _figure;
    private bool _figureShown = true;
    private readonly bool _silhouette = OS.GetEnvironment("HEROIC_SILHOUETTE") == "1";

    public const float FigureHeight = 1.7f;

    /// <summary>Stands the figure beside the machine just built, on the side away from the panels' clutter.</summary>
    private void PlaceFigure(MachineView view)
    {
        _figure?.QueueFree();
        _figure = null;
        if (_world is not null || _silhouette) return;   // worlds have their own scale (the rover); silhouettes show the machine alone
        var box = BoundsOf(view);
        if (box.Size == Vector3.Zero) return;
        // A tabletop machine (an aeolipile) is framed closer than a person is tall: a figure would fill the frame or
        // stand in the camera's face, so it gets none.
        // The same for anything a person would tower over (Heron's fountain, 1.16 m): the figure would be the biggest
        // thing in the frame, which is the opposite of a ruler.
        if (FigureHeight > 1.3f * box.Size.Y && Mathf.Max(box.Size.X, box.Size.Z) < 1.5f) return;
        _figure = Figure();
        // Beside the machine as the camera sees it: off its screen-left side, at the machine's own depth, so it stands
        // in the same plane and never between the camera and the machine.
        var right = _camera.GlobalBasis.X with { Y = 0 };
        right = right.LengthSquared() > 1e-6f ? right.Normalized() : Vector3.Right;
        float halfAcross = 0;
        for (int i = 0; i < 8; i++) halfAcross = Mathf.Max(halfAcross, Mathf.Abs((box.GetEndpoint(i) - box.GetCenter()).Dot(right)));
        // where it stands: the first of a few places round the machine where the whole figure is in the clear part of
        // the screen; if none is, no figure (it must read as a ruler, not crowd the machine or be cut off)
        Vector3? spot = null;
        var toCamera = (_camera.GlobalPosition - box.GetCenter()) with { Y = 0 };
        toCamera = toCamera.LengthSquared() > 1e-6f ? toCamera.Normalized() : Vector3.Back;
        float halfDeep = 0;
        for (int i = 0; i < 8; i++) halfDeep = Mathf.Max(halfDeep, Mathf.Abs((box.GetEndpoint(i) - box.GetCenter()).Dot(toCamera)));
        // beside it on either side, then just in front of it near either end: the first that is wholly in the clear
        var candidates = new[]
        {
            box.GetCenter() - right * (halfAcross + 0.45f),
            box.GetCenter() + right * (halfAcross + 0.45f),
            box.GetCenter() - right * halfAcross * 0.75f + toCamera * (halfDeep + 0.4f),
            box.GetCenter() + right * halfAcross * 0.75f + toCamera * (halfDeep + 0.4f),
        };
        // a spot in front must not stand over the machine itself on screen (one in front of the drop test hid a block)
        var machineOnScreen = ScreenRect(box) is { } m ? m.Grow(-Mathf.Min(m.Size.X, m.Size.Y) * 0.08f) : (Rect2?)null;
        for (int k = 0; k < candidates.Length; k++)
        {
            var at = candidates[k] with { Y = 0 };
            if (!FitsOnScreen(at) || !FitsOnScreen(at + Vector3.Up * FigureHeight)) continue;
            // the spots beside it share its depth and can't hide it; only the two in front are checked
            if (k >= 2 && machineOnScreen is { } r && FigureOnScreen(at).Intersects(r)) continue;
            // nor stand so far forward that it looks bigger than it is beside the machine: a ruler nearer the lens than
            // the machine misreads its size (the post-and-lintel crane's figure stood in front of it, half again too tall)
            if (k >= 2 && FigureOnScreen(at).Size.Y > 1.15f * FigureOnScreen(box.GetCenter() with { Y = at.Y }).Size.Y) continue;
            spot = at;
            break;
        }
        if (spot is not { } place) { _figure.QueueFree(); _figure = null; return; }
        _figure.Position = place;
        _figure.Basis = Basis.LookingAt(-toCamera, Vector3.Up);   // facing the camera (its face is on +Z)
        _figure.Visible = _figureShown;
        AddChild(_figure);
    }

    /// <summary>True when <paramref name="p"/> lands inside the window region the panels leave clear.</summary>
    private bool FitsOnScreen(Vector3 p) =>
        !_camera.IsPositionBehind(p) && ClearArea().Grow(-8).HasPoint(_camera.UnprojectPosition(p));

    /// <summary>The figure's outline on screen standing at <paramref name="at"/>: its feet, head and shoulders.</summary>
    private Rect2 FigureOnScreen(Vector3 at)
    {
        var right = _camera.GlobalBasis.X * 0.2f;
        var rect = new Rect2(_camera.UnprojectPosition(at), Vector2.Zero);
        foreach (var p in new[] { at + right, at - right, at + Vector3.Up * FigureHeight + right, at + Vector3.Up * FigureHeight - right })
            rect = rect.Expand(_camera.UnprojectPosition(p));
        return rect;
    }

    private void ToggleFigure()
    {
        _figureShown = !_figureShown;
        if (_figure is not null) _figure.Visible = _figureShown;
    }

    /// <summary>
    /// A friendly person in a few bold shapes (art direction 12.19, owner 2026-10-10): legs, a tunic with a belt, arms hanging
    /// at the sides, a head with two dot eyes and a broad-brimmed sun hat, all with the outline. Still a ruler: 1.7 m from the
    /// soles to the top of the head (the hat's low crown adds 2 cm), 0.4 m across the shoulders, as the capsule was; muted
    /// colours that don't compete with the machine. It faces +Z; <see cref="PlaceFigure"/> turns it to the camera.
    /// </summary>
    private static Node3D Figure()
    {
        var figure = new Node3D { Name = "ScaleFigure" };
        var tunic = Shapes.Mat(Color.FromHtml("#62808F"), roughness: 0.9f);
        var legs = Shapes.Mat(Color.FromHtml("#47505A"), roughness: 0.9f);
        var leather = Shapes.Mat(Color.FromHtml("#3E3029"), roughness: 0.9f);
        var skin = Shapes.Mat(Color.FromHtml("#D6A27C"), roughness: 0.8f);
        var straw = Shapes.Mat(Color.FromHtml("#B48A52"), roughness: 0.9f);
        const float head = 0.115f;
        void Add(MeshInstance3D m, Vector3 at, Basis? turn = null)
        {
            m.Position = at;
            if (turn is { } b) m.Basis = b;
            m.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            figure.AddChild(m);
        }
        MeshInstance3D Capsule(float r, float h, StandardMaterial3D mat) => new() { Mesh = new CapsuleMesh { Radius = r, Height = h }, MaterialOverride = mat };
        foreach (float x in new[] { -0.085f, 0.085f })
        {
            Add(Capsule(0.07f, 0.86f, legs), new Vector3(x, 0.45f, 0));                            // legs, 0.02 to 0.88 m
            Add(Shapes.Box(new Vector3(0.11f, 0.06f, 0.2f), leather), new Vector3(x, 0.03f, 0.03f));   // shoes, toes forward
        }
        Add(Capsule(0.17f, 0.72f, tunic), new Vector3(0, 1.15f, 0));                                // tunic, 0.79 to 1.51 m
        Add(Shapes.Cylinder(0.172f, 0.05f, leather), new Vector3(0, 1.02f, 0));                      // belt
        foreach (float x in new[] { -1f, 1f })
        {
            var tilt = new Basis(Vector3.Back, x * 0.12f);
            Add(Capsule(0.05f, 0.6f, tunic), new Vector3(x * 0.215f, 1.17f, 0), tilt);                 // arms, hanging a little out
            Add(Shapes.Sphere(0.05f, skin), new Vector3(x * 0.25f, 0.86f, 0));                         // hands
        }
        Add(Shapes.Cylinder(0.045f, 0.08f, skin), new Vector3(0, 1.5f, 0));                           // neck
        Add(Shapes.Sphere(head, skin), new Vector3(0, FigureHeight - head, 0));                       // head, its top at 1.7 m
        var dark = Shapes.Mat(Color.FromHtml("#1E1A18"), roughness: 0.6f, outline: false);
        foreach (float x in new[] { -0.04f, 0.04f })
            Add(Shapes.Sphere(0.016f, dark), new Vector3(x, FigureHeight - head + 0.015f, head * 0.93f));   // eyes
        Add(Shapes.Cylinder(0.21f, 0.015f, straw), new Vector3(0, FigureHeight - 0.075f, 0));          // the hat's brim
        Add(Shapes.Cylinder(0.11f, 0.09f, straw), new Vector3(0, FigureHeight - 0.025f, 0));           // and its low crown
        return figure;
    }

    /// <summary>
    /// HEROIC_SILHOUETTE=1: the machine solid black, everything else white or hidden. Applied every frame to whatever
    /// machine is showing (from _PhysicsProcess). Each mesh gets its own black material, put back to black every frame:
    /// builders keep handles on the materials they change at run time (a tank's ice colour, a boiler's glow and rim),
    /// and with one shared black material the tank code repainted every mesh ice-white.
    /// </summary>
    private void ApplySilhouette(MachineView? view)
    {
        if (!_silhouette || view is null || !IsInstanceValid(view)) return;
        foreach (var node in view.FindChildren("*", "", true, false))
            switch (node)
            {
                case Label3D l: l.Visible = false; break;
                case GpuParticles3D or CpuParticles3D: ((Node3D)node).Visible = false; break;   // steam, spray, mist would veil the shape
                case MeshInstance3D m:
                    if (!m.HasMeta("silhouette"))
                    {
                        m.MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableFog = true };
                        m.SetMeta("silhouette", true);
                    }
                    if (m.MaterialOverride is StandardMaterial3D own)
                    {
                        own.AlbedoColor = Colors.Black;
                        own.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
                        own.EmissionEnabled = false;
                        own.NextPass = null;
                    }
                    break;
            }
        if (_floor is not null) _floor.Visible = false;
        _environment.BackgroundMode = Godot.Environment.BGMode.Color;
        _environment.BackgroundColor = Colors.White;
        _environment.FogEnabled = false;
        _environment.VolumetricFogEnabled = false;
        _environment.SsaoEnabled = false;
        _environment.SsilEnabled = false;
        _environment.SsrEnabled = false;
        _environment.SdfgiEnabled = false;
        _environment.GlowEnabled = false;
        _environment.AdjustmentEnabled = false;
        _environment.TonemapMode = Godot.Environment.ToneMapper.Linear;
        _sun.Visible = false;
    }
}
