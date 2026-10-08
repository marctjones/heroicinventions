using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Weather indoors (issue #58). A warm pond breathes vapour, as hard as it
/// is evaporating. A cold roof below the air's dew point fogs over, a pale
/// film under its ceiling, and its condensate falls as drops into the gutter
/// (or to the floor) as fast as it forms.
/// </summary>
public partial class MachineView
{
    private readonly List<(Pond Pond, GpuParticles3D Vapour)> _pondViews = [];
    private readonly List<(Roof Roof, MeshInstance3D Fog, StandardMaterial3D FogMat, GpuParticles3D Drops)> _roofViews = [];

    private void BuildRainHouse()
    {
        foreach (var (id, pond) in Runtime.Ponds)
        {
            _building = id;
            var tank = Runtime.Def.Part(pond.Tank.Name)!;
            float side = Mathf.Sqrt((float)tank.Number("area"));
            var vapour = SteamCloud(V(tank.At) + new Vector3(0, (float)pond.Tank.Level + 0.05f, 0), amount: 40, radius: side * 0.4f, lifetime: 2f);
            _pondViews.Add((pond, vapour));
            AddLabel(id, V(tank.At) + new Vector3(0, (float)tank.Number("height") + 0.2f, 0));
        }
        foreach (var (id, roof) in Runtime.Roofs)
        {
            _building = id;
            var room = Runtime.Def.Part(roof.Room.Name)!;
            float w = (float)room.Number("size-x"), h = (float)room.Number("size-y"), d = (float)room.Number("size-z");
            var fogMat = Shapes.Mat(new Color(0.93f, 0.96f, 1f), roughness: 1, alpha: 0);
            fogMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            var fog = Shapes.Box(new Vector3(w * 0.98f, 0.03f, d * 0.98f), fogMat);
            fog.Position = V(room.At) + new Vector3(0, h - 0.03f, 0);
            AddChild(fog);
            // drops fall from the ceiling over the gutter, or the middle of the floor
            var over = roof.Gutter is { } g ? V(Runtime.Def.Part(g.Name)!.At) : V(room.At);
            var drops = Drops(new Vector3(over.X, (float)room.At.Y + h - 0.05f, over.Z));
            _roofViews.Add((roof, fog, fogMat, drops));
        }
    }

    private GpuParticles3D Drops(Vector3 at)
    {
        var particles = new GpuParticles3D
        {
            Position = at, Amount = 30, Lifetime = 0.6f, Emitting = false, LocalCoords = false,
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = new Vector3(0, -1, 0), Spread = 3, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.4f,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.3f, 0.01f, 0.3f),
                Gravity = new Vector3(0, -(float)Runtime.Outside.Gravity, 0),
            },
            DrawPass1 = new SphereMesh { Radius = 0.012f, Height = 0.03f, Material = Shapes.Mat(Shapes.Water, roughness: 0.1f, alpha: 0.8f) },
        };
        AddChild(particles);
        return particles;
    }

    private void DrawRainHouse()
    {
        foreach (var (pond, vapour) in _pondViews)
        {
            vapour.Emitting = pond.Evaporation > 1e-6;
            if (vapour.Emitting) vapour.AmountRatio = (float)Math.Clamp(pond.Evaporation * 3600 / 3, 0.15, 1);   // full at 3 kg/h
        }
        foreach (var (roof, fog, fogMat, drops) in _roofViews)
        {
            fogMat.AlbedoColor = fogMat.AlbedoColor with { A = roof.Fogged ? 0.55f : 0 };
            drops.Emitting = roof.Rain > 0;
            if (drops.Emitting) drops.AmountRatio = (float)Math.Clamp(roof.Rain * 3600 / 3, 0.15, 1);
        }
    }
}
