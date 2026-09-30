using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Heliostats (<see cref="Mirror"/>): a square bronze plate on a short
/// post and fork, turned each frame to face halfway between the sun and its
/// target, as the real thing must be to land the light there. While it is
/// catching the sun, a shaft of light runs from its face to the target,
/// brighter the more power it carries; at night the plates stand dark.
/// </summary>
public partial class MachineView
{
    private readonly List<(Mirror mirror, Node3D plate, MeshInstance3D beam, StandardMaterial3D beamMat, StandardMaterial3D faceMat)> _mirrorViews = [];

    private void BuildMirrors()
    {
        foreach (var (id, mirror) in Runtime.Mirrors)
        {
            var part = Runtime.Def.Part(id)!;
            var at = V(part.At);
            float side = Mathf.Sqrt((float)mirror.Area);
            var wood = Shapes.Mat(new Color(0.45f, 0.33f, 0.2f), roughness: 0.9f);
            AddChild(Shapes.Rod(new Vector3(at.X, 0, at.Z), at, 0.04f, wood));

            var plate = new Node3D { Position = at };
            AddChild(plate);
            var faceMat = Shapes.Mat(Shapes.Bronze, metallic: 0.9f, roughness: 0.15f);
            var face = Shapes.Box(new Vector3(side, side, 0.03f), faceMat);
            plate.AddChild(face);

            var beamMat = Shapes.Mat(new Color(1f, 0.93f, 0.6f), roughness: 1f, alpha: 0.35f);
            beamMat.EmissionEnabled = true;
            beamMat.Emission = new Color(1f, 0.85f, 0.45f);
            beamMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            var beam = Shapes.Rod(at, V(mirror.Target), side * 0.25f, beamMat);
            beam.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            AddChild(beam);
            _mirrorViews.Add((mirror, plate, beam, beamMat, faceMat));
            AddLabel(id, at + new Vector3(0, side / 2 + 0.25f, 0));
        }
    }

    private void DrawMirrors()
    {
        var s = V(Runtime.Sun.Direction);
        foreach (var (mirror, plate, beam, beamMat, faceMat) in _mirrorViews)
        {
            // dust dulls the plate (issue #69): matt and brown as it stops more of the light
            float dust = (float)mirror.Dust;
            faceMat.AlbedoColor = Shapes.Bronze.Lerp(new Color(0.55f, 0.38f, 0.25f), dust);
            faceMat.Metallic = 0.9f * (1 - dust);
            faceMat.Roughness = 0.15f + 0.8f * dust;
            var toTarget = (V(mirror.Target) - plate.Position).Normalized();
            // the face turns to the bisector of sun and target; at night it rests facing its target
            var normal = Runtime.Sun.Elevation > 0 ? (s + toTarget).Normalized() : toTarget;
            if (normal.LengthSquared() > 1e-6f)
            {
                var up = Mathf.Abs(normal.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
                plate.LookAt(plate.Position + normal, up);
            }
            double power = mirror.Power;
            beam.Visible = power > 1;
            beamMat.EmissionEnergyMultiplier = (float)Math.Clamp(power / 300, 0.2, 2.5);
        }
    }
}
