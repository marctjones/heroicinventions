using Godot;
using HeroicInventions.Sim.Fluids;

namespace HeroicInventions;

/// <summary>
/// Where water comes from and where it goes, marked so the eye can tell an
/// endless supply from a pool that can run dry. An inflow is a spring: a
/// ring of stones round a little pool with water welling up in it, at the
/// head of the runnel that carries it in, labelled as never running dry.
/// A tank is only ever what's in it. A channel that runs off the edge of
/// the scene ends at a marker saying so: that water is gone for good.
/// </summary>
public partial class MachineView
{
    private readonly List<(WaterSource source, GpuParticles3D welling)> _springHeads = [];

    private void BuildSpringHead(string id, WaterSource source, Vector3 at, Vector3 outward, float width)
    {
        float r = width * 0.9f + 0.2f;
        var centre = at + outward * r;
        var stone = Shapes.Mat(new Color(0.5f, 0.52f, 0.48f), roughness: 0.95f);
        var moss = Shapes.Mat(new Color(0.33f, 0.45f, 0.27f), roughness: 1f);
        const int stones = 9;
        for (int i = 0; i < stones; i++)
        {
            float a = i * Mathf.Tau / stones + 0.3f;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            if (dir.Dot(-outward) > 0.8f) continue;   // leave a gap where the runnel leaves
            float s = r * (0.35f + 0.08f * (i % 3));
            var rock = Shapes.Box(new Vector3(s, s * 0.7f, s * 0.8f), i % 3 == 0 ? moss : stone);
            rock.Position = centre + dir * r + Vector3.Up * (s * 0.2f);
            rock.Rotation = new Vector3(0.2f * (i % 2), a, 0.15f * ((i % 3) - 1));
            AddChild(rock);
        }
        // a spring above the ground (an aqueduct's head) stands on a masonry pier
        if (centre.Y > 0.3f)
        {
            var pier = Shapes.Cylinder(r * 1.05f, centre.Y, Shapes.Mat(Shapes.Stone, roughness: 0.9f));
            pier.Position = new Vector3(centre.X, centre.Y / 2 - 0.02f, centre.Z);
            AddChild(pier);
        }
        var pool = Shapes.Cylinder(r * 0.85f, 0.04f, Shapes.Mat(Shapes.Water, roughness: 0.1f, alpha: 0.85f));
        pool.Position = centre + Vector3.Up * 0.01f;
        AddChild(pool);

        // water welling up in the middle of the pool: a low dome of bubbles, as much as the spring gives
        var welling = new GpuParticles3D
        {
            Position = centre + Vector3.Up * 0.04f,
            Amount = 30,
            Lifetime = 0.8f,
            LocalCoords = false,
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up,
                Spread = 25f,
                InitialVelocityMin = 0.15f,
                InitialVelocityMax = 0.35f,
                Gravity = new Vector3(0, -0.6f, 0),
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = r * 0.25f,
            },
            DrawPass1 = new SphereMesh { Radius = 0.02f, Height = 0.04f, Material = Shapes.Mat(new Color(0.85f, 0.93f, 1f), roughness: 0.2f, alpha: 0.8f) },
        };
        AddChild(welling);
        _springHeads.Add((source, welling));
        AddLabel($"{id}: a spring, {source.Rate * 1000:0.##} L/s — never runs dry", centre + Vector3.Up * (r * 0.6f + 0.25f));
    }

    /// <summary>A channel running off the edge of the scene: a marker post at its end.</summary>
    private void BuildOutfallMarker(string id, Vector3 end, Vector3 along)
    {
        var post = Shapes.Box(new Vector3(0.08f, 0.5f, 0.08f), Shapes.Mat(new Color(0.45f, 0.33f, 0.2f), roughness: 0.9f));
        var side = along.Cross(Vector3.Up).Normalized();
        post.Position = new Vector3(end.X, 0.25f, end.Z) + side * 0.4f;
        AddChild(post);
        AddLabel($"{id} runs off the scene — gone for good", post.Position + Vector3.Up * 0.45f);
    }

    private void DrawSprings()
    {
        foreach (var (source, welling) in _springHeads)
        {
            bool flowing = source.Flow > 1e-7;
            welling.Emitting = flowing;
            if (flowing) welling.AmountRatio = (float)Math.Clamp(source.Flow / Math.Max(source.Rate, 1e-9), 0.2, 1);
        }
    }
}
