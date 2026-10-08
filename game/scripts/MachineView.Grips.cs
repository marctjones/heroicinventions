using Godot;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// Grips (issue #55): tongs and hooks that pick up a loose block and let it go on cue.
/// While a grip is closed and empty it looks, every tick, for the nearest loose block whose
/// centre is within its reach and takes hold of it with a fully locked joint, hung on the
/// body it is fixed to (or on the world). The grip keeps hold while the load — the block's
/// mass times how hard gravity and the grip's own motion pull it, m·|a − g| — stays within
/// what the grip can carry (Grip.Capacity), and lets go when it is opened, or when it is
/// overloaded (and stays open to that load until it is opened and closed again).
/// It is drawn as two jaws at its point and a faint sphere the size of its reach: grey open,
/// amber closed and empty, green holding, red once it has dropped a load.
/// </summary>
public partial class MachineView
{
    private sealed class GripView
    {
        public required Grip Grip;
        public RigidBody3D? Host;                    // what it hangs from, or null for the world
        public required Vector3 Local;               // its point, in the host's frame (or the world's)
        public required MeshInstance3D[] Jaws;
        public required MeshInstance3D Reach;
        public required StandardMaterial3D Material, ReachMaterial;
        public Generic6DofJoint3D? Joint;
        public MaterialBlock? Body;
        public Vector3 LastVelocity;
        public Vector3 Acceleration;                 // the held body's, smoothed
    }

    private readonly List<GripView> _gripViews = [];

    private void BuildGrips()
    {
        foreach (var (id, grip) in Runtime.Grips)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            string on = part.Symbol("on", "world");
            RigidBody3D? host = on == "world" ? null : _bodiesById.GetValueOrDefault(on)
                ?? throw new Sim.Machines.MachineFormatException($"grip {id} hangs on {on}, which is not a body in this machine", part.Location);
            var world = V(part.At);
            var mat = Shapes.Mat(Shapes.ColorFor(part.Material), metallic: 0.6f);
            var reachMat = Shapes.Mat(new Color(0.7f, 0.7f, 0.7f), alpha: 0.10f);
            reachMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            var jaws = new[]
            {
                Shapes.Box(new Vector3(0.012f, 0.06f, 0.03f), mat),
                Shapes.Box(new Vector3(0.012f, 0.06f, 0.03f), mat),
            };
            var reach = Shapes.Sphere((float)grip.Reach, reachMat);
            var view = new GripView
            {
                Grip = grip, Host = host, Jaws = jaws, Reach = reach, Material = mat, ReachMaterial = reachMat,
                Local = host is null ? world : host.Transform.AffineInverse() * world,
            };
            foreach (var n in new Node3D[] { jaws[0], jaws[1], reach })
            {
                if (host is not null) host.AddChild(n); else AddChild(n);
                n.Position = view.Local;
            }
            AddLabel(id, world + new Vector3(0, 0.1f, 0));
            _gripViews.Add(view);
        }
    }

    private Vector3 GripPoint(GripView v) => v.Host is null ? v.Local : v.Host.GlobalTransform * v.Local;

    /// <summary>Looks after every grip for one physics tick: lets go, checks the load, or takes hold.</summary>
    private void DriveGrips(double dt)
    {
        if (_gripViews.Count == 0) return;
        var gravity = new Vector3(0, -(float)Runtime.Outside.Gravity, 0);
        foreach (var v in _gripViews)
        {
            var g = v.Grip;
            if (!g.Closed)
            {
                if (g.Held) Release(v, overloaded: false);
                continue;
            }
            if (g.Held)
            {
                var body = v.Body!;
                if (!IsInstanceValid(body)) { Release(v, overloaded: false); continue; }
                var a = (body.LinearVelocity - v.LastVelocity) / (float)dt;
                v.LastVelocity = body.LinearVelocity;
                v.Acceleration = v.Acceleration.Lerp(a, 0.2f);                  // smoothed: contact jitter is not a load
                g.Load = body.Mass * (v.Acceleration - gravity).Length();
                if (g.Load > g.Capacity) Release(v, overloaded: true);
            }
            else if (!g.Overloaded)
            {
                var point = GripPoint(v);
                MaterialBlock? best = null;
                float nearest = (float)g.Reach;
                foreach (var block in Blocks)
                {
                    if (block == v.Host || _gripHeld.Contains(block) || !IsInstanceValid(block)) continue;
                    float d = block.GlobalPosition.DistanceTo(point);
                    if (d <= nearest) { nearest = d; best = block; }
                }
                if (best is not null) TakeHold(v, best);
            }
        }
    }

    private readonly HashSet<MaterialBlock> _gripHeld = [];

    private void TakeHold(GripView v, MaterialBlock body)
    {
        var joint = new Generic6DofJoint3D { GlobalTransform = new Transform3D(Basis.Identity, body.GlobalPosition) };
        AddChild(joint);
        // a fixed joint: nothing may slide or turn, the way tongs keep a load rigid
        void Lock(Action<Generic6DofJoint3D.Flag, bool> flag, Action<Generic6DofJoint3D.Param, float> param)
        {
            flag(Generic6DofJoint3D.Flag.EnableLinearLimit, true);
            param(Generic6DofJoint3D.Param.LinearLowerLimit, 0f);
            param(Generic6DofJoint3D.Param.LinearUpperLimit, 0f);
            flag(Generic6DofJoint3D.Flag.EnableAngularLimit, true);
            param(Generic6DofJoint3D.Param.AngularLowerLimit, 0f);
            param(Generic6DofJoint3D.Param.AngularUpperLimit, 0f);
        }
        Lock(joint.SetFlagX, joint.SetParamX);
        Lock(joint.SetFlagY, joint.SetParamY);
        Lock(joint.SetFlagZ, joint.SetParamZ);
        if (v.Host is not null) joint.NodeA = joint.GetPathTo(v.Host);
        joint.NodeB = joint.GetPathTo(body);
        v.Joint = joint;
        v.Body = body;
        v.LastVelocity = body.LinearVelocity;
        v.Acceleration = Vector3.Zero;
        _gripHeld.Add(body);
        var g = v.Grip;
        g.HeldBody = body.Name;
        g.HeldFor = 0;
        g.LoadMu = _materials[Runtime.Def.Part(body.Name)!.Material].Friction;
        g.Load = body.Mass * (float)Runtime.Outside.Gravity;
    }

    private void Release(GripView v, bool overloaded)
    {
        v.Joint?.QueueFree();
        v.Joint = null;
        if (v.Body is { } b) _gripHeld.Remove(b);
        v.Body = null;
        var g = v.Grip;
        g.HeldBody = null;
        g.HeldFor = 0;
        g.Load = 0;
        if (overloaded) g.Overloaded = true;
    }

    private static float HalfWidth(MaterialBlock b) =>
        b.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape is BoxShape3D box ? box.Size.X / 2 : 0.05f;

    private void DrawGrips()
    {
        foreach (var v in _gripViews)
        {
            var g = v.Grip;
            var colour = g.Overloaded ? new Color(0.9f, 0.2f, 0.15f) : g.Held ? new Color(0.3f, 0.85f, 0.4f) : g.Closed ? new Color(0.95f, 0.7f, 0.2f) : new Color(0.6f, 0.6f, 0.6f);
            v.Material.AlbedoColor = colour;
            // open: the jaws stand apart at the reach; closed on a load: at its face; closed and empty: nearly together
            float gap = !g.Closed ? (float)g.Reach * 0.9f : g.Held && v.Body is { } b ? Mathf.Max(0.02f, HalfWidth(b)) : 0.01f;
            v.Jaws[0].Position = v.Local + new Vector3(-gap, 0, 0);
            v.Jaws[1].Position = v.Local + new Vector3(gap, 0, 0);
            v.ReachMaterial.AlbedoColor = new Color(colour.R, colour.G, colour.B, g.Closed && !g.Held ? 0.16f : 0.08f);
        }
    }
}
