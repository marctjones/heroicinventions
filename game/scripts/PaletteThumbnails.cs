using Godot;

namespace HeroicInventions;

/// <summary>
/// The build palette's thumbnails (#106): each part drawn by the game itself
/// (<see cref="BuildMode.BuildPartModel"/>, the same model the placing ghost
/// uses), in an offscreen viewport with its own world and light, one part a
/// couple of frames apart so opening build mode never stalls. Nothing is
/// cached on disk: they are remade each time, so they follow any change to a
/// part's look.
/// </summary>
public partial class PaletteThumbnails(BuildMode builder, List<(int Index, string PaletteId)> queue, Action<int, Texture2D> done) : Node
{
    public const int Size = 40;   // px shown in the list
    private const int Render = 96; // px rendered, scaled down for a clean edge

    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private Node3D? _model;
    private int _current = -1, _wait;

    public override void _Ready()
    {
        _viewport = new SubViewport
        {
            Size = new Vector2I(Render, Render),
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(_viewport);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.75f, 0.75f, 0.8f),
            AmbientLightEnergy = 0.6f,
        };
        _viewport.AddChild(new WorldEnvironment { Environment = env });
        var sun = new DirectionalLight3D { LightEnergy = 1.1f };
        _viewport.AddChild(sun);
        sun.LookAtFromPosition(new Vector3(2, 4, 3), Vector3.Zero, Vector3.Up);
        _camera = new Camera3D { Fov = 35 };
        _viewport.AddChild(_camera);
        _camera.MakeCurrent();
    }

    public override void _Process(double delta)
    {
        if (_wait-- > 0) return;
        if (_model is not null)
        {
            // drawn last frame: keep it
            var image = _viewport.GetTexture().GetImage();
            // HEROIC_THUMBS_DIR=<dir>: also save each full-size render, to look them all over (or diff them, #107)
            if (OS.GetEnvironment("HEROIC_THUMBS_DIR") is { Length: > 0 } dir)
                image.SavePng(System.IO.Path.Combine(dir, queue[_current].PaletteId + ".png"));
            image.Resize(Size, Size, Image.Interpolation.Lanczos);
            done(queue[_current].Index, ImageTexture.CreateFromImage(image));
            _model.QueueFree();
            _model = null;
        }
        if (++_current >= queue.Count) { QueueFree(); return; }

        try { _model = builder.BuildPartModel(queue[_current].PaletteId, hosted: true); }
        catch (Exception e) { GD.Print($"[thumbnails] {queue[_current].PaletteId}: {e.Message}"); _model = null; return; }
        _viewport.AddChild(_model);
        if (_model is MachineView view) view.SetFrozen(true);
        foreach (var label in Descendants(_model).OfType<Label3D>()) label.Visible = false;
        Frame(_model);
        _wait = 1;   // let it draw before reading it back
    }

    /// <summary>Points the camera at the model from up and to one side, far enough back that all of it shows.</summary>
    private void Frame(Node3D model)
    {
        Aabb? box = null;
        foreach (var vi in Descendants(model).OfType<VisualInstance3D>())
        {
            if (vi is GpuParticles3D or Label3D) continue;
            var local = vi.GetAabb();
            if (local.Size == Vector3.Zero) continue;
            var world = vi.GlobalTransform * local;
            box = box is { } b ? b.Merge(world) : world;
        }
        var bounds = box ?? new Aabb(new Vector3(-0.1f, 0, -0.1f), new Vector3(0.2f, 0.2f, 0.2f));
        float radius = Mathf.Max(0.05f, bounds.Size.Length() / 2);
        float distance = radius / Mathf.Sin(Mathf.DegToRad(_camera.Fov / 2)) * 1.05f;
        var centre = bounds.GetCenter();
        _camera.LookAtFromPosition(centre + new Vector3(0.55f, 0.5f, 0.85f).Normalized() * distance, centre, Vector3.Up);
        _camera.Near = Mathf.Max(0.01f, distance - radius * 2);
        _camera.Far = distance + radius * 2;
    }

    private static IEnumerable<Node> Descendants(Node n)
    {
        yield return n;
        foreach (var c in n.GetChildren()) foreach (var d in Descendants(c)) yield return d;
    }
}
