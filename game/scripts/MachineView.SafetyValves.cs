using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Papin's safety valve on a boiler's lid: a seat with a disc on it, and a
/// lever pivoted on a stand at one end, pressing on the disc, with a weight
/// hung at the far end. The lever rises with the valve's opening and a
/// plume of steam blows out of the seat as hard as it vents. A boiler rated
/// to burst is drawn whole until it does; then its shell is gone, a few
/// torn staves lie round the fire and a cloud of flashed steam boils up
/// and thins away.
/// </summary>
public partial class MachineView
{
    private const float LeverLength = 0.24f, LeverLift = 0.35f; // m; rad at full lift, drawn larger than life so it shows
    private readonly Dictionary<string, MeshInstance3D> _boilerBodies = [];
    private readonly List<(SafetyValve valve, Node3D lever, GpuParticles3D plume)> _valveViews = [];
    private readonly List<(Boiler boiler, MeshInstance3D body, Node3D wreck, GpuParticles3D cloud)> _burstViews = [];

    private void BuildSafetyValves()
    {
        foreach (var (id, (valve, _)) in Runtime.SafetyValves)
        {
            var part = Runtime.Def.Part(id)!;
            var seat = V(part.At);
            var bronze = Surface(part.Material);
            float bore = (float)valve.Bore;
            var seatMesh = Shapes.Cylinder(bore * 1.4f, 0.03f, bronze);
            seatMesh.Position = seat + new Vector3(0, 0.015f, 0);
            AddChild(seatMesh);

            var pivot = seat + new Vector3(-0.06f, 0.05f, 0);
            AddChild(Shapes.Rod(seat + new Vector3(-0.06f, 0, 0), pivot, 0.006f, Surface("iron")));
            var lever = new Node3D { Position = pivot };
            AddChild(lever);
            var bar = Shapes.Box(new Vector3(LeverLength, 0.012f, 0.012f), Surface("iron"));
            bar.Position = new Vector3(LeverLength / 2, 0, 0);
            lever.AddChild(bar);
            var disc = Shapes.Cylinder(bore * 1.2f, 0.012f, bronze);
            disc.Position = new Vector3(0.06f, -0.03f, 0);
            lever.AddChild(disc);
            lever.AddChild(Shapes.Rod(new Vector3(0.06f, -0.024f, 0), new Vector3(0.06f, 0, 0), 0.004f, Surface("iron")));
            var weight = Shapes.Sphere(0.03f, Surface("iron"));
            weight.Position = new Vector3(LeverLength, -0.035f, 0);
            lever.AddChild(weight);

            var plume = SteamCloud(seat + new Vector3(0, 0.04f, 0), amount: 40, radius: 0.02f, lifetime: 1.2f);
            _valveViews.Add((valve, lever, plume));
            AddLabel($"{id} lifts at {valve.LiftPressure / 1000:0.#} kPa", seat + new Vector3(0.28f, 0.02f, 0));
        }
        foreach (var (id, boiler) in Runtime.Boilers)
        {
            if (boiler.BurstPressure <= 0 || !_boilerBodies.TryGetValue(id, out var body)) continue;
            var part = Runtime.Def.Part(id)!;
            float r = (float)part.Number("radius"), h = (float)part.Number("height");
            var wreck = new Node3D { Visible = false };
            AddChild(wreck);
            var shell = Surface(part.Material);
            for (int i = 0; i < 7; i++)
            {
                float a = i * Mathf.Tau / 7 + 0.3f, d = r * (1.6f + 0.5f * (i % 3));
                var stave = Shapes.Box(new Vector3(r * 0.9f, 0.01f, h * (0.4f + 0.1f * (i % 4))), shell);
                stave.Position = V(part.At) with { Y = 0.01f } + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
                stave.Rotation = new Vector3(0.25f * (i % 2 == 0 ? 1 : -1), a, 0.4f * ((i % 3) - 1));
                wreck.AddChild(stave);
            }
            var floor = Shapes.Cylinder(r, 0.02f, shell);
            floor.Position = V(part.At) + new Vector3(0, 0.01f, 0);
            wreck.AddChild(floor);
            var cloud = SteamCloud(V(part.At) + new Vector3(0, h / 2, 0), amount: 120, radius: r, lifetime: 4f);
            _burstViews.Add((boiler, body, wreck, cloud));
            AddLabel($"{id} rated to {boiler.BurstPressure / 1000:0.#} kPa", V(part.At) + new Vector3(0, h + 0.3f, 0));
        }
    }

    private GpuParticles3D SteamCloud(Vector3 at, int amount, float radius, float lifetime)
    {
        var particles = new GpuParticles3D
        {
            Position = at,
            Amount = amount,
            Lifetime = lifetime,
            Emitting = false,
            LocalCoords = false,
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = new Vector3(0, 1, 0),
                Spread = 12f,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = radius,
                Gravity = new Vector3(0, 0.3f, 0),
                ScaleMin = 1f,
                ScaleMax = 2f,
            },
            DrawPass1 = new SphereMesh
            {
                Radius = 0.03f,
                Height = 0.06f,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1, 1, 1, 0.25f),
                    Roughness = 1f,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                },
            },
        };
        AddChild(particles);
        return particles;
    }

    private void DrawSafetyValves()
    {
        foreach (var (valve, lever, plume) in _valveViews)
        {
            lever.Rotation = new Vector3(0, 0, (float)valve.Opening * LeverLift);
            plume.Emitting = valve.Flow > 1e-6;
            if (plume.ProcessMaterial is ParticleProcessMaterial m)
            {
                // exit speed scales with the flow, so a harder blow throws the plume higher
                float v = 0.4f + 2.5f * (float)valve.Opening;
                m.InitialVelocityMin = v * 0.8f;
                m.InitialVelocityMax = v * 1.2f;
            }
        }
        foreach (var (boiler, body, wreck, cloud) in _burstViews)
        {
            body.Visible = !boiler.Burst;
            wreck.Visible = boiler.Burst;
            double since = boiler.Time - boiler.BurstTime;
            cloud.Emitting = boiler.Burst && since < 3;
            if (cloud.ProcessMaterial is ParticleProcessMaterial m)
            {
                m.InitialVelocityMin = 1.5f;
                m.InitialVelocityMax = 4f;
                m.Spread = 70f;
            }
        }
    }
}
