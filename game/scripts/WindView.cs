using Godot;
using HeroicInventions.Sim.Fluids;

namespace HeroicInventions;

/// <summary>
/// The crater's wind, made visible (owner ruling 2026-10-09): pale streaks of drifting dust around the player, each carried along
/// the way the wind blows at the place it is (the map's heading, veered by the hour) at that wind's own speed, so the corridor
/// below the rim's notch shows as a stronger stream: more streaks, longer and quicker, than the thin air off it. Legibility over
/// realism (docs/art-direction.md, "The wind"): the streaks are a cue, not a simulation. Their number visible, their length and
/// their pace follow the wind's speed there; they fade with distance from the focus so the edge of the field never shows.
/// The speed is the same <see cref="WindField.SpeedAt"/> a windmill reads, at the same solar hour, so what is drawn is what the
/// sails get. It moves only while the sim runs.
/// </summary>
public partial class WindView : Node3D
{
    /// <summary>The wind field, the focus point (the rover or the camera's pivot), the sim's (solar hour, seconds into the run), and whether it is running.</summary>
    public required Terrain Ground { get; init; }
    public required Func<Vector3> Focus { get; init; }
    public required Func<(double Hour, double Seconds)> Clock { get; init; }
    public required Func<bool> Running { get; init; }

    private const int Count = 360;
    private const float Reach = 40f;            // m: the streaks fill a disc this wide around the focus, fading out towards its edge
    private const float FullSpeed = 8f;         // m/s: the wind at which every streak shows (the corridor's night peak is 8.1)
    private const float MaxAlpha = 0.35f;

    private readonly Vector2[] _pos = new Vector2[Count];   // world x, z
    private readonly float[] _height = new float[Count];    // m above the ground
    private readonly float[] _threshold = new float[Count]; // a streak shows once the wind there passes this share of FullSpeed
    private MultiMesh _mesh = null!;
    private readonly RandomNumberGenerator _rng = new() { Seed = 61 };
    private bool _placed;

    public override void _Ready()
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _mesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = Vector3.One, Material = material }, InstanceCount = Count };
        AddChild(new MultiMeshInstance3D { Multimesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 200 });
        for (int i = 0; i < Count; i++) _threshold[i] = _rng.Randf();
    }

    /// <summary>The wind (m/s) at a world point now, as the streaks and the rover's readout have it.</summary>
    public double SpeedAt(Vector3 p)
    {
        var (hour, seconds) = Clock();
        return Ground.Wind!.SpeedAt(p.X, p.Z, hour, seconds);
    }

    public override void _Process(double delta)
    {
        if (Ground.Wind is not { } wind || _mesh is null) return;
        var focus = Focus();
        var (hour, seconds) = Clock();
        bool moving = Running();
        float dt = moving ? (float)Math.Min(delta, 0.1) : 0f;
        // wind blows from azimuth a (from +x toward +z), so towards a + 180
        double from = wind.HeadingAt(seconds) * Math.PI / 180;
        var toward = new Vector2(-(float)Math.Cos(from), -(float)Math.Sin(from));
        for (int i = 0; i < Count; i++)
        {
            if (!_placed) Respawn(i, focus);
            double v = wind.SpeedAt(_pos[i].X, _pos[i].Y, hour, seconds);
            _pos[i] += toward * (float)v * dt;
            if (_pos[i].DistanceTo(new Vector2(focus.X, focus.Z)) > Reach) Respawn(i, focus);
            // denser where stronger: a streak shows once the wind there is past its own threshold, and eases in over a little more
            float show = Mathf.Clamp(((float)v / FullSpeed - _threshold[i]) / 0.12f, 0, 1);
            float fade = 1f - Mathf.SmoothStep(0.45f, 1f, _pos[i].DistanceTo(new Vector2(focus.X, focus.Z)) / Reach);
            float length = 0.4f + 0.3f * (float)v;                  // m: longer in a stronger wind
            float y = (float)Ground.HeightAt(_pos[i].X, _pos[i].Y) + _height[i];
            var fwd = new Vector3(toward.X, 0, toward.Y);
            var right = new Vector3(-fwd.Z, 0, fwd.X);
            var basis = new Basis(right * 0.035f, Vector3.Up * 0.035f, fwd * length);
            _mesh.SetInstanceTransform(i, new Transform3D(basis, new Vector3(_pos[i].X, y, _pos[i].Y)));
            _mesh.SetInstanceColor(i, new Color(0.93f, 0.84f, 0.70f, MaxAlpha * show * fade));
        }
        _placed = true;
    }

    private void Respawn(int i, Vector3 focus)
    {
        // anywhere in the disc, uniformly; a streak that has blown out of it reappears somewhere else in it
        float a = _rng.Randf() * Mathf.Tau, r = Reach * Mathf.Sqrt(_rng.Randf());
        _pos[i] = new Vector2(focus.X + r * Mathf.Cos(a), focus.Z + r * Mathf.Sin(a));
        _height[i] = 0.25f + _rng.Randf() * 2.5f;
    }
}
