using Godot;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>A node that turns about an axis by an angle read each frame (a spit driven by a smoke jack's vanes).</summary>
public partial class TurnedBy : Node3D
{
    public Func<double> Angle { get; set; } = () => 0;
    public Vector3 Axis { get; set; } = Vector3.Right;
    public override void _Process(double delta) => Basis = new Basis(Axis, (float)Angle());
}

/// <summary>
/// What a smoke jack's work looks like (#176): the hot air rising through its chimney drawn as streaks climbing at the
/// draught's own speed, and the roasting spit the jack turns, with a joint of meat on it, set in front of the fire.
/// Only drawn: the spit's gearing (the vanes' 39 rpm, geared down 40 to 1 to a turn a minute and a half) is not part of the
/// jack's load.
/// </summary>
public partial class MachineView
{
    /// <summary>The vanes turn this many times for each turn of the spit.</summary>
    private const double SpitGearing = 40;

    private void BuildSmokeJackWork(JetWheel jack, Vector3 fireAt, float flueRadius, float chimneyTop)
    {
        // the draught: warm streaks rising through the flue at the draught's speed, which is the vanes' wind
        float rise = chimneyTop - (fireAt.Y + 0.35f);
        float speed = 1.18f;                                   // m/s: the header's draught
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1f, 0.62f, 0.25f, 0.0f));
        ramp.AddPoint(0.15f, new Color(1f, 0.62f, 0.25f, 0.55f));
        ramp.AddPoint(0.85f, new Color(1f, 0.8f, 0.5f, 0.45f));
        ramp.SetColor(ramp.GetPointCount() - 1, new Color(1f, 0.8f, 0.5f, 0f));
        var process = new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 0, InitialVelocityMin = speed, InitialVelocityMax = speed, Gravity = Vector3.Zero,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(flueRadius * 0.8f, 0.01f, flueRadius * 0.8f),
            ColorRamp = new GradientTexture1D { Gradient = ramp },
        };
        var streaks = new GpuParticles3D
        {
            Position = new Vector3(fireAt.X, fireAt.Y + 0.35f, fireAt.Z), Amount = 40, Lifetime = rise / speed, Emitting = false, LocalCoords = false,
            ProcessMaterial = process, VisibilityAabb = new Aabb(new Vector3(-1, -0.5f, -1), new Vector3(2, rise + 1, 2)),
            DrawPass1 = new BoxMesh
            {
                Size = new Vector3(0.012f, 0.22f, 0.012f),
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(1, 1, 1, 1),
                },
            },
        };
        AddChild(streaks);
        _steamPuffs.Add((() => jack.JetVelocity > 0.05 ? 1 : 0, streaks));   // switched on and off with the draught, as the steam puffs are

        // the spit: an iron rod on two forked stands in front of the fire, a joint on it, geared to the vanes
        float y = fireAt.Y + 0.30f, z = fireAt.Z + 0.42f, len = 0.9f;
        var iron = Surface("iron");
        foreach (float sx in new[] { -1f, 1f })
        {
            var foot = new Vector3(fireAt.X + sx * len / 2, 0, z);
            AddChild(Shapes.Rod(foot, foot + new Vector3(0, y - 0.04f, 0), 0.012f, iron));
            AddChild(Shapes.Rod(foot + new Vector3(0, y - 0.04f, -0.03f), foot + new Vector3(0, y - 0.04f, 0.03f), 0.008f, iron));
        }
        var spit = new TurnedBy { Position = new Vector3(fireAt.X, y, z), Axis = Vector3.Right, Angle = () => jack.Angle / SpitGearing };
        AddChild(spit);
        spit.AddChild(Shapes.Rod(new Vector3(-len / 2 - 0.05f, 0, 0), new Vector3(len / 2 + 0.05f, 0, 0), 0.007f, iron));
        var meat = Shapes.Mat(new Color(0.55f, 0.27f, 0.14f), roughness: 0.8f);
        var roast = Shapes.Cylinder(0.085f, 0.4f, meat);
        roast.RotationDegrees = new Vector3(0, 0, 90);
        spit.AddChild(roast);
        var bone = Shapes.Box(new Vector3(0.10f, 0.03f, 0.03f), Shapes.Mat(new Color(0.92f, 0.9f, 0.82f), roughness: 0.7f));
        bone.Position = new Vector3(0.22f, 0.09f, 0);   // a knuckle of bone, so the turning shows
        spit.AddChild(bone);
        AddLabel("spit", new Vector3(fireAt.X, y + 0.22f, z));
    }
}
