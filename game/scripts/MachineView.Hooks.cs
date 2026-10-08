using Godot;
using HeroicInventions.Sim;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Hooks (#160): a rope's load end can be let go and taken up again at run time. Unhooking leaves the rope's end on a
/// small iron hook of its own, a free body that hangs from the rope like any other load, so a hand can drag it (#159).
/// Let go near a body it is hooked to it: the rope's To end is rebound to that body, at the nearest point of its
/// surface, the hook is taken away and a lifting eye is drawn there. The rope's tension, strength and breaking rules
/// are the ropes' own (MachineView.Ropes.cs); this only moves which body the end is tied to.
/// </summary>
public partial class MachineView
{
    /// <summary>A person did something to a rope's load: (view, rope, "hook" or "unhook", the load's name, the point, in the world).</summary>
    public static event Action<MachineView, string, string, string?, Vector3>? HookChanged;

    private const float HookRadius = 0.04f, HookMass = 2f;
    /// <summary>How near a hook must be let go to a body's surface to be hooked to it, m.</summary>
    public const float HookReach = 0.15f;

    private readonly Dictionary<Rope, RigidBody3D> _hookOf = [];              // a rope that has let its load go, and the hook it hangs on
    private readonly Dictionary<string, RigidBody3D> _hookBodies = [];        // the same, by the name a hand grabs it by: "ROPE.hook"
    private readonly Dictionary<Rope, MeshInstance3D> _eyes = [];

    /// <summary>The body a hand can take hold of under this name: a part's, or a rope's free hook ("ROPE.hook").</summary>
    public RigidBody3D? HandBody(string id) => _bodiesById.GetValueOrDefault(id) ?? _hookBodies.GetValueOrDefault(id);

    /// <summary>
    /// Fields only the view has: <c>ROPE hook 0</c> lets the load go, <c>ROPE hook 1</c> hooks the hook to what is within its
    /// reach. (A driven wheel's <c>drive-rpm</c> is the runtime's, #154, applied to the motor every tick.)
    /// False if this isn't one of those, so the caller goes on to the runtime's fields.
    /// </summary>
    public bool TrySetViewField(string target, string field, double value)
    {
        if (field == "hook" && _ropes.FirstOrDefault(r => r.Spec.Id == target) is { } rope)
        {
            if (value == 0) Unhook(rope);
            else if (_hookOf.TryGetValue(rope, out var hook) && !DropHook(hook))
                throw new MachineFormatException($"{target} has nothing within {HookReach * 100:0} cm of its hook to hook onto");
            return true;
        }
        return false;
    }

    private bool Hookable(Rope r) => r.Active && r.Spec.ReleaseDeg is null && !r.Spec.Nocked;

    private void Unhook(Rope rope)
    {
        if (_hookOf.ContainsKey(rope)) return;   // already let go
        if (!Hookable(rope)) throw new MachineFormatException($"{rope.Spec.Id} cannot be unhooked: it has " + (rope.Active ? "a sling or a nock for an end" : "parted or been let go"));
        if (rope.B is not { } load) throw new MachineFormatException($"{rope.Spec.Id} has no load to unhook: its end is tied to the ground");
        var eye = load.GlobalTransform * rope.BLocal;
        var hook = MakeHook(rope.Spec.Id, eye + Vector3.Up * HookRadius, PointVelocity(load, eye));
        _hookOf[rope] = hook;
        _hookBodies[$"{rope.Spec.Id}.hook"] = hook;   // the name a hand grabs it by
        if (_eyes.Remove(rope, out var ring)) ring.QueueFree();
        rope.B = hook;
        rope.BLocal = Vector3.Zero;
        HookChanged?.Invoke(this, rope.Spec.Id, "unhook", load.Name, eye);
    }

    private RigidBody3D MakeHook(string ropeId, Vector3 at, Vector3 velocity)
    {
        var hook = new RigidBody3D
        {
            Name = $"{ropeId}-hook", Mass = HookMass, CanSleep = false, GravityScale = (float)(_shownGravity / Physics.Gravity),
            PhysicsMaterialOverride = ContactFor("iron"),
        };
        hook.SetMeta("part_id", ropeId);
        hook.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = HookRadius } });
        var iron = Shapes.Mat(Shapes.ColorFor("iron"), metallic: 0.6f);
        hook.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = HookRadius * 0.8f, Height = HookRadius * 1.6f, RadialSegments = 12, Rings = 6 }, MaterialOverride = iron });
        hook.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.008f, Height = 0.07f, RadialSegments = 6 }, MaterialOverride = iron, Position = new Vector3(0, HookRadius + 0.02f, 0) });
        AddChild(hook);
        hook.GlobalPosition = at;
        hook.LinearVelocity = velocity;
        _freezable.Add(hook);
        return hook;
    }

    /// <summary>
    /// A body has just been let go of by a hand: if it is a rope's free hook with something solid within reach, tie the
    /// rope to that, at the nearest point of its surface. True if it was hooked.
    /// </summary>
    public bool DropHook(RigidBody3D body)
    {
        var (rope, _) = _hookOf.FirstOrDefault(kv => kv.Value == body);
        if (rope is null) return false;
        var at = body.GlobalPosition;
        RigidBody3D? best = null;
        var bestLocal = Vector3.Zero;
        float nearest = HookReach;
        foreach (var candidate in _bodiesById.Values)
        {
            if (candidate == rope.A || candidate == body || !IsInstanceValid(candidate) || candidate.Name == (rope.Spec.Turns ?? "")) continue;
            if (BoundsOf(candidate) is not { } box) continue;
            var local = candidate.ToLocal(at);
            var onSurface = local.Clamp(box.Position, box.End);
            float d = local.DistanceTo(onSurface);
            if (d <= nearest) { nearest = d; best = candidate; bestLocal = onSurface; }
        }
        if (best is null) return false;

        rope.B = best;
        rope.BLocal = bestLocal;
        _hookOf.Remove(rope);
        _hookBodies.Remove($"{rope.Spec.Id}.hook");
        _freezable.Remove(body);
        if (Hand?.Body == body) Hand = null;
        body.QueueFree();
        AddEye(rope);
        HookChanged?.Invoke(this, rope.Spec.Id, "hook", best.Name, best.GlobalTransform * bestLocal);
        return true;
    }

    /// <summary>A body's collision shapes' box, in its own frame; null if it has none.</summary>
    private static Aabb? BoundsOf(RigidBody3D body)
    {
        Aabb? all = null;
        foreach (var cs in body.GetChildren().OfType<CollisionShape3D>())
        {
            if (cs.Shape is null) continue;
            Aabb box = cs.Shape switch
            {
                BoxShape3D b => new Aabb(-b.Size / 2, b.Size),
                SphereShape3D s => new Aabb(-Vector3.One * s.Radius, Vector3.One * s.Radius * 2),
                CylinderShape3D c => new Aabb(new Vector3(-c.Radius, -c.Height / 2, -c.Radius), new Vector3(2 * c.Radius, c.Height, 2 * c.Radius)),
                CapsuleShape3D c => new Aabb(new Vector3(-c.Radius, -c.Height / 2, -c.Radius), new Vector3(2 * c.Radius, c.Height, 2 * c.Radius)),
                var other => other.GetDebugMesh().GetAabb(),
            };
            var placed = cs.Transform * box;
            all = all is { } a ? a.Merge(placed) : placed;
        }
        return all;
    }

    /// <summary>
    /// The lifting eye a hoisting rope hangs from: a small iron ring standing on the face of the load where the rope is
    /// tied, in the load's own frame so it goes where the load goes. Drawn for ropes wound on a drum (the cranes).
    /// </summary>
    private void AddEye(Rope rope)
    {
        if (rope.Drum is null || rope.B is not { } load || !Hookable(rope)) return;
        var box = BoundsOf(load);
        var normal = Vector3.Up;
        if (box is { } b)
        {
            // the face the point is on: the axis along which it sits furthest toward the edge
            var rel = (rope.BLocal - (b.Position + b.Size / 2)) / (b.Size / 2 + Vector3.One * 1e-6f);
            int axis = Mathf.Abs(rel.X) >= Mathf.Abs(rel.Y) && Mathf.Abs(rel.X) >= Mathf.Abs(rel.Z) ? 0 : Mathf.Abs(rel.Y) >= Mathf.Abs(rel.Z) ? 1 : 2;
            normal = Vector3.Zero;
            normal[axis] = Mathf.Sign(rel[axis]) == 0 ? 1 : Mathf.Sign(rel[axis]);
        }
        var spin = normal.Cross(Vector3.Right);
        if (spin.LengthSquared() < 1e-6f) spin = normal.Cross(Vector3.Forward);   // the ring's axis lies along the face, so the ring stands on it
        var ring = new MeshInstance3D
        {
            Name = $"{rope.Spec.Id}-eye",
            Mesh = new TorusMesh { InnerRadius = 0.02f, OuterRadius = 0.065f, Rings = 16, RingSegments = 8 },
            MaterialOverride = Shapes.Mat(Shapes.ColorFor("iron"), metallic: 0.6f),
            Position = rope.BLocal + normal * 0.065f,
            Basis = new Basis(new Quaternion(Vector3.Up, spin.Normalized())),
        };
        ring.SetMeta("part_id", rope.Spec.Id);
        load.AddChild(ring);
        _eyes[rope] = ring;
    }
}
