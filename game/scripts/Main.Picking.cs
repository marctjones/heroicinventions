using Godot;

namespace HeroicInventions;

/// <summary>
/// Which part is under the cursor (#151). Every node a machine's builders add carries a <c>part_id</c> (or is marked
/// <c>scenery</c>, see <see cref="MachineView.MarkScenery"/>), so a point on the screen can be traced back to the part
/// it shows. Click-to-operate (#152) and dragging (#159) build on this; nothing here is bound to input yet.
/// </summary>
public partial class Main
{
    /// <summary>
    /// The part drawn at a viewport point. A ray through the point meets the physics bodies first (they are what a
    /// hand would touch); if it meets none of a machine's, the visible meshes' boxes along the same ray are tried,
    /// nearest first, opaque ones before translucent (a click should go through water and glass to the thing behind
    /// when there is one, and still find a pane when there is nothing else). The ground and the scale figure belong
    /// to no part, so the ray passes through them.
    /// </summary>
    public (MachineView View, string PartId, Node3D Node)? PartAt(Vector2 screen)
    {
        var from = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        var space = _camera.GetWorld3D().DirectSpaceState;
        var skip = new Godot.Collections.Array<Rid>();
        for (int tries = 0; tries < 32; tries++)
        {
            var hit = space.IntersectRay(new PhysicsRayQueryParameters3D { From = from, To = from + dir * 2000f, Exclude = skip });
            if (hit.Count == 0) break;
            var body = (CollisionObject3D)hit["collider"].AsGodotObject();
            if (PartOf(body) is { } found) return found;
            skip.Add(body.GetRid());   // the ground, or something that isn't a part: look past it
        }

        IEnumerable<MachineView> views = _views.Count > 0 ? _views : _current is not null ? [_current] : [];
        (MachineView View, string PartId, Node3D Node)? best = null, bestSeeThrough = null;
        float bestT = float.MaxValue, bestSeeThroughT = float.MaxValue;
        foreach (var view in views)
            foreach (var mesh in view.Meshes())
            {
                if (!mesh.IsVisibleInTree() || mesh.Mesh is null || PartOf(mesh) is not { } found) continue;
                // in the mesh's own frame its box is axis-aligned, so a turned beam is tested as the beam, not as the larger box around it
                var toLocal = mesh.GlobalTransform.AffineInverse();
                // (the direction is not renormalised in the mesh's frame, so t means the same distance along the ray in both)
                if (RayEntersBox(mesh.Mesh.GetAabb(), toLocal * from, toLocal.Basis * dir) is not { } t) continue;
                if (MachineView.IsOpaque(mesh)) { if (t < bestT) { bestT = t; best = found; } }
                else if (t < bestSeeThroughT) { bestSeeThroughT = t; bestSeeThrough = found; }
            }
        return best ?? bestSeeThrough;
    }

    /// <summary>Where along a ray (in units of its direction) it first meets a box: the slab test; null if it misses or the box is behind.</summary>
    private static float? RayEntersBox(Aabb box, Vector3 origin, Vector3 dir)
    {
        float near = 0, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float lo = box.Position[axis], hi = box.End[axis];
            if (Mathf.Abs(dir[axis]) < 1e-9f)
            {
                if (origin[axis] < lo || origin[axis] > hi) return null;   // parallel to this pair of faces and outside them
                continue;
            }
            float t1 = (lo - origin[axis]) / dir[axis], t2 = (hi - origin[axis]) / dir[axis];
            near = Mathf.Max(near, Mathf.Min(t1, t2));
            far = Mathf.Min(far, Mathf.Max(t1, t2));
            if (near > far) return null;
        }
        return near;
    }

    /// <summary>The part a node belongs to: its own <c>part_id</c> or its nearest tagged ancestor's, null for scenery and for nodes no machine owns.</summary>
    private static (MachineView View, string PartId, Node3D Node)? PartOf(Node3D node)
    {
        for (Node? n = node; n is not null; n = n.GetParent())
        {
            if (n.HasMeta("scenery")) return null;
            if (!n.HasMeta("part_id")) continue;
            for (Node? v = n; v is not null; v = v.GetParent())
                if (v is MachineView view) return (view, n.GetMeta("part_id").AsString(), node);
            return null;
        }
        return null;
    }

    /// <summary>Scripted step "pick X Y": prints the part under that viewport pixel (the viewport is scaled to the window: the centre of a 1280x800 window is 800 500) (HEROIC_INPUT, see ScriptedInput.cs).</summary>
    private void PrintPick(Vector2 screen)
    {
        var picked = PartAt(screen);
        GD.Print($"[pick] {screen.X:0} {screen.Y:0} -> {(picked is { } p ? $"{p.PartId} ({p.View.Name}, {p.Node.GetType().Name} {p.Node.Name})" : "nothing")}");
    }
}
