using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// A rope or chain built of rigid links pinned end to end (issue #31), for
/// a rope with #:links n: n rods, each 1/n of its length and weighing what
/// that much of it weighs (the material's density over its cross-section),
/// joined by pin joints to each other and to its two ends (a fixed point,
/// or a part that moves). Unlike the lumped rope, which only ever pulls its
/// ends together, it has weight all along it: it sags into a catenary
/// between two supports and swings as a real chain does. The links don't
/// collide with anything; they hang in the air as drawn.
/// </summary>
public partial class MachineView
{
    private readonly List<(RopeSpec Spec, List<RigidBody3D> Links)> _chains = [];

    private void BuildChain(RopeSpec spec)
    {
        int n = spec.Links!.Value;
        var (aBody, aLocal) = ResolveEnd(spec.From);
        var (bBody, bLocal) = ResolveEnd(spec.To);
        var a = aBody is null ? aLocal : aBody.GlobalTransform * aLocal;
        var b = bBody is null ? bLocal : bBody.GlobalTransform * bLocal;
        float length = (float)spec.Length, link = length / n, radius = (float)spec.Diameter / 2;
        var mat = _materials[spec.Material];
        float mass = (float)(mat.Density * Math.PI * radius * radius * link);

        // laid out slack as a V hanging below the line between the ends, its two arms together the chain's length
        var mid = (a + b) / 2;
        float halfSpan = (b - a).Length() / 2;
        float drop = Mathf.Sqrt(Mathf.Max(0, length * length / 4 - halfSpan * halfSpan));
        var bottom = mid + Vector3.Down * drop;
        Vector3 Along(float s) => s <= length / 2 ? a.Lerp(bottom, s / (length / 2)) : bottom.Lerp(b, (s - length / 2) / (length / 2));

        var look = Surface(spec.Material);
        var links = new List<RigidBody3D>();
        for (int k = 0; k < n; k++)
        {
            var p0 = Along(k * link);
            var p1 = Along((k + 1) * link);
            var body = new RigidBody3D
            {
                Name = $"{spec.Id}-{k}", Mass = mass, CollisionLayer = 0, CollisionMask = 0,
                LinearDamp = 0.3f, AngularDamp = 0.3f, CanSleep = false,
            };
            // a rod along the body's own Y, centred on it
            body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = radius, Height = link } });
            body.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = link * 0.96f, RadialSegments = 6 },
                MaterialOverride = look,
            });
            var dir = (p1 - p0).Normalized();
            var axis = Vector3.Up.Cross(dir);
            var basis = axis.LengthSquared() > 1e-8f ? new Basis(axis.Normalized(), Vector3.Up.AngleTo(dir)) : Basis.Identity;
            body.Transform = new Transform3D(basis, (p0 + p1) / 2);
            AddChild(body);
            _freezable.Add(body);
            _bodiesById[body.Name] = body;
            links.Add(body);
        }
        // pins: the first link to the From end, each to the next, the last to the To end
        Pin(links[0], aBody, a);
        for (int k = 0; k + 1 < n; k++) Pin(links[k + 1], links[k], Along((k + 1) * link));
        Pin(links[^1], bBody, b);
        _chains.Add((spec, links));
    }

    /// <summary>
    /// Each tick, before the physics step: closes the gaps the pin joints
    /// have let open. Forty joints in a row under a chain's weight give a
    /// little each (about 1% of a link, measured), so the chain would hang
    /// longer than it is; a few passes of moving each pair of pinned ends
    /// together (the ends at a fixed hook don't move) keeps it its length.
    /// Positions only; Jolt keeps the velocities.
    /// </summary>
    private void ConstrainChains()
    {
        foreach (var (spec, links) in _chains)
        {
            float half = (float)spec.Length / links.Count / 2;
            var (aBody, aLocal) = ResolveEnd(spec.From);
            var (bBody, bLocal) = ResolveEnd(spec.To);
            var states = links.Select(l => PhysicsServer3D.BodyGetDirectState(l.GetRid())).ToList();
            var xf = states.Select(s => s.Transform).ToList();
            Vector3 Low(int k) => xf[k] * new Vector3(0, -half, 0);    // the end towards From
            Vector3 High(int k) => xf[k] * new Vector3(0, half, 0);   // the end towards To
            Vector3 Hook(RigidBody3D? body, Vector3 local) =>
                body is null ? local : PhysicsServer3D.BodyGetDirectState(body.GetRid()).Transform * local;
            for (int pass = 0; pass < 4; pass++)
            {
                // the From hook holds the first link; pinned neighbours share each gap; the To hook holds the last
                var gap = Hook(aBody, aLocal) - Low(0);
                xf[0] = xf[0].Translated(aBody is null ? gap : gap / 2);
                for (int k = 0; k + 1 < links.Count; k++)
                {
                    var d = High(k) - Low(k + 1);
                    xf[k] = xf[k].Translated(-d / 2);
                    xf[k + 1] = xf[k + 1].Translated(d / 2);
                }
                gap = Hook(bBody, bLocal) - High(links.Count - 1);
                xf[^1] = xf[^1].Translated(bBody is null ? gap : gap / 2);
            }
            for (int k = 0; k < links.Count; k++) states[k].Transform = xf[k];
        }
    }

    private void Pin(RigidBody3D body, RigidBody3D? other, Vector3 at)
    {
        var pin = new PinJoint3D { Position = at };
        AddChild(pin);
        pin.NodeA = pin.GetPathTo(body);
        if (other is not null) pin.NodeB = pin.GetPathTo(other);
    }
}
