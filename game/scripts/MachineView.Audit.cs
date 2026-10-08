using Godot;

namespace HeroicInventions;

/// <summary>
/// HEROIC_AUDIT=1: checks a machine does what its parts are there for.
/// At the first tick it lists every pair of physical parts that start
/// overlapping (jammed into each other — the jib legs inside a treadwheel
/// would have shown up here); over the run it records how far every
/// moving part actually moved and turned, what each rope carried, and what
/// each lift delivered, so a part that's there but never takes part —
/// a pulley that never turns, a rope that never pulls — stands out.
/// </summary>
public partial class MachineView
{
    private Dictionary<RigidBody3D, (Transform3D Start, float Moved, float Turned)>? _audit;
    private readonly Dictionary<string, float> _auditRopeMax = [];
    private readonly Dictionary<string, double> _auditLifted = [];
    private readonly List<string> _auditOverlaps = [];

    public void AuditTick(double dt)
    {
        if (_audit is null)
        {
            _audit = _freezable.ToDictionary(b => b, b => (b.GlobalTransform, 0f, 0f));
            FindOverlaps();
        }
        foreach (var b in _freezable)
        {
            // pieces a fracture makes (#43) are new bodies: start tracking them where they appear
            if (!_audit.TryGetValue(b, out var seen)) _audit[b] = seen = (b.GlobalTransform, 0f, 0f);
            var (start, moved, turned) = seen;
            float m = b.GlobalPosition.DistanceTo(start.Origin);
            float t = Mathf.RadToDeg((start.Basis.Inverse() * b.GlobalTransform.Basis).GetRotationQuaternion().GetAngle());
            _audit[b] = (start, Mathf.Max(moved, m), Mathf.Max(turned, t));
        }
        foreach (var r in _ropes)
            _auditRopeMax[r.Spec.Id] = Mathf.Max(_auditRopeMax.GetValueOrDefault(r.Spec.Id), r.Tension);
        foreach (var (id, lift) in Runtime.Lifts)
            _auditLifted[id] = _auditLifted.GetValueOrDefault(id) + lift.Flow * dt;
    }

    /// <summary>Pairs of parts that would collide and start more than 5 mm inside each other.</summary>
    private void FindOverlaps()
    {
        var space = GetWorld3D().DirectSpaceState;
        var jointed = new HashSet<(ulong, ulong)>();
        foreach (var j in GetChildren().OfType<Joint3D>())
        {
            var a = j.GetNodeOrNull<PhysicsBody3D>(j.NodeA);
            var b = j.GetNodeOrNull<PhysicsBody3D>(j.NodeB);
            if (a is not null && b is not null) { jointed.Add((a.GetInstanceId(), b.GetInstanceId())); jointed.Add((b.GetInstanceId(), a.GetInstanceId())); }
        }
        var bodies = GetChildren().OfType<CollisionObject3D>().ToList();
        var seen = new HashSet<string>();
        foreach (var body in bodies)
            foreach (var shapeNode in body.GetChildren().OfType<CollisionShape3D>())
            {
                var query = new PhysicsShapeQueryParameters3D
                {
                    Shape = shapeNode.Shape,
                    Transform = body.GlobalTransform * shapeNode.Transform,
                    CollisionMask = uint.MaxValue,
                    Exclude = [body.GetRid()],
                };
                var points = space.CollideShape(query, 64);
                // points come in pairs: on the query shape, on the other body
                var hits = space.IntersectShape(query, 32);
                foreach (var hit in hits)
                {
                    if (hit["collider"].AsGodotObject() is not CollisionObject3D other || other == body) continue;
                    if (jointed.Contains((body.GetInstanceId(), other.GetInstanceId()))) continue;
                    bool collide = (body.CollisionLayer & other.CollisionMask) != 0 || (other.CollisionLayer & body.CollisionMask) != 0;
                    if (!collide) continue;
                    if (body is StaticBody3D && other is StaticBody3D) continue; // fixed to fixed: can't block anything
                    float depth = 0;
                    for (int i = 0; i + 1 < points.Count; i += 2) depth = Mathf.Max(depth, points[i].DistanceTo(points[i + 1]));
                    if (depth < 0.005f) continue; // resting contact, not a jam
                    string key = string.Join(" <-> ", new[] { body.Name.ToString(), other.Name.ToString() }.Order());
                    if (seen.Add(key)) _auditOverlaps.Add($"{key} start {depth * 1000:F0} mm inside each other");
                }
            }
    }

    /// <summary>
    /// Visible opaque meshes that neither belong to a part nor are marked scenery: a click on one would find
    /// nothing (#151). Translucent shapes (water, glass, steam) don't count; they are never what is clicked.
    /// </summary>
    public List<MeshInstance3D> UntaggedMeshes()
    {
        var found = new List<MeshInstance3D>();
        void Walk(Node node)
        {
            if (node.HasMeta("part_id") || node.HasMeta("scenery")) return;
            if (node is MeshInstance3D mesh && mesh.IsVisibleInTree() && IsOpaque(mesh)) found.Add(mesh);
            foreach (var child in node.GetChildren()) Walk(child);
        }
        foreach (var child in GetChildren()) Walk(child);
        return found;
    }

    /// <summary>Which build pass made the view's child that holds this node (see TagBuilt).</summary>
    private string BuiltBy(Node node)
    {
        while (node.GetParent() is { } up && up != this) node = up;
        return node.HasMeta("built_by") ? node.GetMeta("built_by").AsString() : "later";
    }

    public static bool IsOpaque(MeshInstance3D mesh)
    {
        var material = mesh.MaterialOverride ?? mesh.GetActiveMaterial(0);
        return material is not BaseMaterial3D m || (m.Transparency == BaseMaterial3D.TransparencyEnum.Disabled && m.AlbedoColor.A > 0.99f);
    }

    public string AuditReport()
    {
        var lines = new List<string> { $"AUDIT {Name} at {Runtime.Time:F1}s" };
        lines.AddRange(_auditOverlaps.Select(o => "  OVERLAP " + o));
        if (_audit is not null)
            foreach (var (b, (_, moved, turned)) in _audit)
                lines.Add($"  part {b.Name}: moved up to {moved:F2} m, turned up to {turned:F0}°{(moved < 0.005f && turned < 0.5f ? "   <-- NEVER MOVED" : "")}");
        foreach (var (id, t) in _auditRopeMax)
            lines.Add($"  rope {id}: max tension {t:F0} N{(t < 1 ? "   <-- NEVER PULLED" : "")}");
        foreach (var (id, v) in _auditLifted)
            lines.Add($"  lift {id}: delivered {v * 1000:F0} L{(v < 1e-4 ? "   <-- NOTHING LIFTED" : "")}");
        var untagged = UntaggedMeshes();
        lines.Add($"  untagged opaque meshes: {untagged.Count}");
        lines.AddRange(untagged.GroupBy(BuiltBy).Select(g => $"    UNTAGGED from {g.Key}: {g.Count()} e.g. {GetPathTo(g.First())}"));
        return string.Join("\n", lines);
    }
}
