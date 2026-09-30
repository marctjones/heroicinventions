using Godot;

namespace HeroicInventions;

/// <summary>
/// Impacts (issue #27): every time a moving body comes into contact with
/// something — the floor, a post, another body — the strike is recorded:
/// how fast the two surfaces were closing along the contact normal, the
/// impulse that stopped them, and the kinetic energy the collision took
/// (as heat, sound and damage: what #47's drums and #43's fractures work
/// from). Each strike flashes where it landed, bigger for more energy, with
/// a label that fades.
///
/// Jolt only says a contact exists after the step that made it, by which
/// time the bodies have already bounced. So each tick keeps the bodies'
/// velocities as they go into the step, and a contact that is new this
/// tick is measured against those, damped as the step damps them
/// (v (1 - c dt)): the velocities the collision met. Jolt works a bounce
/// from the speed before the step's gravity, so gravity is left out here
/// too — a block dropped on the floor leaves at exactly e times the speed
/// recorded (drop-test). The energy taken is what the pair had then less
/// what they have now, spin included.
///
/// Jolt combines two surfaces' restitution as the larger of the two (the
/// floor gives none of its own, so a block bounces with its own), and
/// gives none below 1 m/s of closing speed.
/// </summary>
public partial class MachineView
{
    /// <summary>One strike. <c>Other</c> is null for the ground or anything that doesn't move.</summary>
    public sealed record Impact(double Time, RigidBody3D Body, Node3D? Other, Vector3 Point, Vector3 Normal,
                                float Speed, float Impulse, float EnergyLost);

    /// <summary>Each monitored body's strikes so far: how many, the last one, the hardest, the energy taken in all.</summary>
    public sealed class ImpactRecord
    {
        public int Count;
        public Impact? Last;
        public float MaxSpeed, TotalEnergy;
    }

    /// <summary>Raised for every strike on a monitored body (a pair of monitored bodies raises it once).</summary>
    public event Action<Impact>? Struck;

    public IReadOnlyDictionary<RigidBody3D, ImpactRecord> ImpactRecords => _impactRecords;

    // Below this closing speed a new contact is a settling touch, not a strike.
    private const float LeastImpactSpeed = 0.05f;

    private readonly Dictionary<RigidBody3D, ImpactRecord> _impactRecords = [];
    private readonly Dictionary<RigidBody3D, (Vector3 V, Vector3 W)> _intoStep = [];
    private readonly Dictionary<RigidBody3D, HashSet<ulong>> _touching = [];
    private readonly List<(MeshInstance3D Ring, StandardMaterial3D Look, Label3D Label, double Born, float Size)> _flashes = [];

    /// <summary>Every moving body reports its contacts.</summary>
    private void BuildImpacts()
    {
        foreach (var b in _freezable)
        {
            b.ContactMonitor = true;
            b.MaxContactsReported = Math.Max(b.MaxContactsReported, 8);
            _impactRecords[b] = new ImpactRecord();
            _touching[b] = [];
        }
    }

    /// <summary>The velocities a body met the step's collisions with: last tick's, damped.</summary>
    private (Vector3 V, Vector3 W) IntoCollision(RigidBody3D body)
    {
        if (body.Freeze) return (Vector3.Zero, Vector3.Zero);
        var (v, w) = _intoStep.TryGetValue(body, out var kept) ? kept : (body.LinearVelocity, body.AngularVelocity);
        float dt = (float)GetPhysicsProcessDeltaTime();
        float linearDamp = body.LinearDamp + (body.LinearDampMode == RigidBody3D.DampMode.Combine ? DefaultDamp("linear") : 0);
        float angularDamp = body.AngularDamp + (body.AngularDampMode == RigidBody3D.DampMode.Combine ? DefaultDamp("angular") : 0);
        return (v * (1 - linearDamp * dt), w * (1 - angularDamp * dt));
    }

    private static float DefaultDamp(string which) =>
        (float)ProjectSettings.GetSetting($"physics/3d/default_{which}_damp", 0.1).AsDouble();

    private static float KineticEnergy(RigidBody3D body, Vector3 v, Vector3 w)
    {
        if (body.Freeze) return 0;
        var inverse = PhysicsServer3D.BodyGetDirectState(body.GetRid()).InverseInertiaTensor;
        float spin = inverse.Determinant() > 1e-12f ? w.Dot(inverse.Inverse() * w) : 0;
        return 0.5f * body.Mass * v.LengthSquared() + 0.5f * spin;
    }

    /// <summary>Finds the contacts that began in the last step and records each as a strike. Runs first each tick.</summary>
    private void DetectImpacts()
    {
        var seenPairs = new HashSet<(ulong, ulong)>();
        foreach (var (body, touching) in _touching)
        {
            if (!IsInstanceValid(body)) continue;
            var state = PhysicsServer3D.BodyGetDirectState(body.GetRid());
            var now = new Dictionary<ulong, List<int>>();
            for (int i = 0; i < state.GetContactCount(); i++)
            {
                ulong other = state.GetContactColliderId(i);
                if (!now.TryGetValue(other, out var list)) now[other] = list = [];
                list.Add(i);
            }
            foreach (var (otherId, contacts) in now)
            {
                if (touching.Contains(otherId)) continue;
                ulong self = body.GetInstanceId();
                if (!seenPairs.Add((Math.Min(self, otherId), Math.Max(self, otherId)))) continue;
                var other = InstanceFromId(otherId) as Node3D;
                var otherBody = other as RigidBody3D;

                var point = Vector3.Zero;
                var normal = Vector3.Zero;
                var impulse = Vector3.Zero;
                foreach (int i in contacts)
                {
                    point += state.GetContactLocalPosition(i);   // global, despite the name
                    normal += state.GetContactLocalNormal(i);
                    impulse += state.GetContactImpulse(i);
                }
                point /= contacts.Count;
                normal = normal.LengthSquared() > 1e-12f ? normal.Normalized() : Vector3.Up;

                var (v, w) = IntoCollision(body);
                var mine = v + w.Cross(point - CentreOfMass(body));
                var (ov, ow) = otherBody is null ? (Vector3.Zero, Vector3.Zero) : IntoCollision(otherBody);
                var theirs = otherBody is null ? Vector3.Zero : ov + ow.Cross(point - CentreOfMass(otherBody));
                float closing = Mathf.Abs((mine - theirs).Dot(normal));
                if (closing < LeastImpactSpeed) continue;

                float before = KineticEnergy(body, v, w) + (otherBody is null ? 0 : KineticEnergy(otherBody, ov, ow));
                float after = KineticEnergy(body, body.LinearVelocity, body.AngularVelocity)
                              + (otherBody is null ? 0 : KineticEnergy(otherBody, otherBody.LinearVelocity, otherBody.AngularVelocity));
                RecordImpact(new Impact(Runtime.Time, body, other, point, normal, closing,
                                        Mathf.Abs(impulse.Dot(normal)), Mathf.Max(0, before - after)));
            }
            touching.Clear();
            touching.UnionWith(now.Keys);
        }
    }

    /// <summary>Keeps each body's velocities as they go into the step. Runs last each tick.</summary>
    private void KeepVelocitiesIntoStep()
    {
        foreach (var body in _touching.Keys)
            if (IsInstanceValid(body)) _intoStep[body] = (body.LinearVelocity, body.AngularVelocity);
    }

    /// <summary>Counts a strike against both bodies (if the other is monitored), raises Struck, and flashes it.</summary>
    private void RecordImpact(Impact impact)
    {
        void Count(RigidBody3D body)
        {
            if (!_impactRecords.TryGetValue(body, out var rec)) return;
            rec.Count++;
            rec.Last = impact;
            rec.MaxSpeed = Mathf.Max(rec.MaxSpeed, impact.Speed);
            rec.TotalEnergy += impact.EnergyLost;
        }
        Count(impact.Body);
        if (impact.Other is RigidBody3D other) Count(other);
        Struck?.Invoke(impact);
        if (impact.EnergyLost >= 0.5f) Flash(impact);
    }

    /// <summary>A ring that spreads from the strike and fades, its size growing with the cube root of the energy taken.</summary>
    private void Flash(Impact impact)
    {
        float size = 0.12f * Mathf.Pow(impact.EnergyLost, 1f / 3f);
        var look = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.75f, 0.2f, 1f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.7f, OuterRadius = 1f, Rings = 32 }, MaterialOverride = look };
        AddChild(ring);
        // lying in the plane of the contact: the torus's axis along the normal
        var tilt = Vector3.Up.Cross(impact.Normal);
        var basis = tilt.LengthSquared() > 1e-8f ? new Basis(tilt.Normalized(), Vector3.Up.AngleTo(impact.Normal)) : Basis.Identity;
        ring.Transform = new Transform3D(basis.Scaled(Vector3.One * 0.01f), impact.Point);
        var label = new Label3D
        {
            Text = $"{impact.EnergyLost:0.#} J at {impact.Speed:0.00} m/s",
            Position = impact.Point + new Vector3(0, 0.15f + size, 0),
            FontSize = 24, OutlineSize = 6, PixelSize = 0.004f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
        };
        AddChild(label);
        _flashes.Add((ring, look, label, Runtime.Time, size));
    }

    private void DrawImpacts()
    {
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var (ring, look, label, born, size) = _flashes[i];
            float age = (float)(Runtime.Time - born);
            if (age > 2.5f || age < 0)
            {
                ring.QueueFree();
                label.QueueFree();
                _flashes.RemoveAt(i);
                continue;
            }
            float spread = Mathf.Clamp(age / 0.5f, 0, 1);
            ring.Visible = spread < 1;
            ring.Basis = ring.Basis.Orthonormalized().Scaled(Vector3.One * Mathf.Max(0.01f, size * spread));
            look.AlbedoColor = look.AlbedoColor with { A = 1 - spread };
            label.Modulate = new Color(1, 1, 1, Mathf.Clamp(1.5f - age / 1.5f, 0, 1));
            label.Position += new Vector3(0, 0.002f, 0);
        }
    }
}
