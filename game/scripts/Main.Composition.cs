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
        foreach (var c in candidates)
        {
            var at = c with { Y = 0 };
            if (FitsOnScreen(at) && FitsOnScreen(at + Vector3.Up * FigureHeight)) { spot = at; break; }
        }
        if (spot is not { } place) { _figure.QueueFree(); _figure = null; return; }
        _figure.Position = place;
        _figure.Visible = _figureShown;
        AddChild(_figure);
    }

    /// <summary>True when <paramref name="p"/> lands inside the window region the panels leave clear.</summary>
    private bool FitsOnScreen(Vector3 p) =>
        !_camera.IsPositionBehind(p) && ClearArea().Grow(-8).HasPoint(_camera.UnprojectPosition(p));

    private void ToggleFigure()
    {
        _figureShown = !_figureShown;
        if (_figure is not null) _figure.Visible = _figureShown;
    }

    /// <summary>
    /// A person reduced to two shapes, a body and a head, 1.7 m tall, in a muted slate that doesn't compete with the
    /// machine (it's a ruler, not a character).
    /// </summary>
    private static Node3D Figure()
    {
        var figure = new Node3D { Name = "ScaleFigure" };
        var cloth = Shapes.Mat(Color.FromHtml("#56606B"), roughness: 0.9f);
        const float head = 0.115f, bodyHeight = FigureHeight - 2 * head - 0.02f;
        var body = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.17f, Height = bodyHeight },
            MaterialOverride = cloth,
            Position = new Vector3(0, bodyHeight / 2, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        figure.AddChild(body);
        var skull = Shapes.Sphere(head, cloth);
        skull.Position = new Vector3(0, FigureHeight - head, 0);
        figure.AddChild(skull);
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
