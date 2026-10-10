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

    /// <summary>The spot each mirror's light lands on (#162): a person drags it to aim the mirror by hand. Pale on its receiver, red when aimed off it.</summary>
    private readonly Dictionary<Mirror, (MeshInstance3D Spot, StandardMaterial3D Mat)> _mirrorSpots = [];

    /// <summary>The mirrors' spots as the aim points they sit at, by mirror id, for dragging them (Main.Aim.cs).</summary>
    public IEnumerable<(string Id, Mirror Mirror, Vector3 Spot)> MirrorSpots() =>
        Runtime.Mirrors.Select(kv => (kv.Key, kv.Value, V(kv.Value.Target)));

    // A clean mirror drawn bright, pale polished bronze, not the reflection of whatever sky is behind the viewer:
    // at metallic 0.9 a face showed Mars's pale sky and read as tan on a tan ground (owner rule: readable first).
    private static readonly Color MirrorFace = Color.FromHtml("#F2CF85");

    private readonly Dictionary<Mirror, Vector3> _spotHeld = [];

    /// <summary>While a person drags a mirror's spot, the spot is drawn under the hand; null lets it go back to the mirror's aim.</summary>
    public void ShowSpotAt(Mirror mirror, Vector3? at)
    {
        if (at is { } p) _spotHeld[mirror] = p; else _spotHeld.Remove(mirror);
        if (_mirrorSpots.TryGetValue(mirror, out var sp)) sp.Spot.Position = at ?? V(mirror.Target);
    }

    private void BuildMirrors()
    {
        foreach (var (id, mirror) in Runtime.Mirrors)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var at = V(part.At);
            float side = Mathf.Sqrt((float)mirror.Area);
            var wood = Surface("oak");
            AddChild(Shapes.Rod(new Vector3(at.X, 0, at.Z), at, 0.04f, wood));

            var plate = new Node3D { Position = at };
            AddChild(plate);
            var faceMat = Shapes.Mat(MirrorFace, metallic: 0.5f, roughness: 0.2f);
            var face = Shapes.Box(new Vector3(side, side, 0.03f), faceMat);
            plate.AddChild(face);

            var beamMat = Shapes.Mat(new Color(1f, 0.93f, 0.6f), roughness: 1f, alpha: 0.35f);
            beamMat.EmissionEnabled = true;
            beamMat.Emission = new Color(1f, 0.85f, 0.45f);
            beamMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            var beam = Shapes.Rod(at, V(mirror.Target), mirror.Focusing ? 0.06f : side * 0.25f, beamMat);   // a burning mirror's light narrows to its focus
            beam.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            AddChild(beam);
            var spotMat = Shapes.Mat(new Color(1f, 0.93f, 0.6f), roughness: 1f, alpha: 0.6f);
            spotMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            spotMat.NoDepthTest = true;   // always there to take hold of, even inside its receiver
            // a ring, not a disc (12.21): the receiver it lands on (a rock's glowing cracks) shows through the middle
            float ring = Mathf.Max(0.12f, side * 0.35f);
            var spot = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = ring * 0.6f, OuterRadius = ring, Rings = 48, RingSegments = 6 }, MaterialOverride = spotMat };
            spot.Scale = new Vector3(1, 0.2f, 1);
            spot.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            spot.Position = V(mirror.Target);
            AddChild(spot);
            _mirrorSpots[mirror] = (spot, spotMat);
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
            faceMat.AlbedoColor = MirrorFace.Lerp(new Color(0.55f, 0.38f, 0.25f), dust);
            faceMat.Metallic = 0.5f * (1 - dust);
            faceMat.Roughness = 0.15f + 0.8f * dust;
            var aim = V(mirror.Target);
            var toTarget = (aim - plate.Position).Normalized();
            // the beam and the spot follow the aim: a person can turn it (#162)
            var span = aim - plate.Position;
            beam.Position = plate.Position + span / 2;
            ((CylinderMesh)beam.Mesh!).Height = Mathf.Max(span.Length(), 0.001f);
            var up0 = span.Normalized();
            var axis0 = Vector3.Up.Cross(up0);
            beam.Basis = axis0.LengthSquared() > 1e-8f ? new Basis(axis0.Normalized(), Vector3.Up.AngleTo(up0)) : up0.Y < 0 ? new Basis(Vector3.Right, Mathf.Pi) : Basis.Identity;
            if (_mirrorSpots.TryGetValue(mirror, out var sp))
            {
                sp.Spot.Position = _spotHeld.TryGetValue(mirror, out var held) ? held : aim;
                sp.Mat.AlbedoColor = mirror.OnReceiver ? new Color(1f, 0.93f, 0.6f, 0.6f) : new Color(1f, 0.3f, 0.2f, 0.7f);
            }
            // the face turns to the bisector of sun and target; at night it rests facing its target
            // a burning mirror faces the sun square, its target at its focus
            var normal = !mirror.Track ? V(mirror.Normal)   // tracking off: it stays as it was left
                : Runtime.Sun.Elevation <= 0 ? toTarget : mirror.Focusing ? s : (s + toTarget).Normalized();
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
