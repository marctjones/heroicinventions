using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Blocks buried in a world's ground (issue #54). Each tick a block's cover
/// is read from the map: the ground's height over it less the top of the
/// block. Covered by more than a quarter of its height it is held where it
/// lies (frozen), and the force it would take to pull it straight out is
/// worked out (Burial.PullOut: its weight, the soil on its lid, the soil's
/// grip on its sides). Uncovered past that (dug, or slid away), it is let go
/// where it lies (FreeInPlace): the thin crust left over it crumbles off to
/// its sides, the worked ground under it is its base, and it becomes an
/// ordinary body at rest in its pit, not lifted or shoved. A slide that
/// buries a block that was standing free holds it the same way. A buried
/// block shows as a tag on the ground above it: how deep, and the pull (for the rover's cargo only once found, #240: MachineView.Found.cs).
/// </summary>
public partial class MachineView
{
    /// <summary>The ground of the world this machine stands in, if it has a map (set by Main).</summary>
    public Terrain? Ground { get; set; }

    private sealed class Buried
    {
        public required RigidBody3D Body;
        public required float Size;
        public double Cover, Pull;
        public bool Held;
        /// <summary>Ticks since the ground was bared round it, while it waits, still frozen, for the ground's new body to be there; -1 when not waiting.</summary>
        public int Freeing = -1;
        /// <summary>The cover at which the ground last could not be bared round it (it stays held), so it is not tried every tick: NaN if none.</summary>
        public double Refused = double.NaN;
        public Label3D? Tag;
    }
    private readonly Dictionary<string, Buried> _buried = [];

    /// <summary>Before each physics step: which blocks the ground holds, and which it has let go.</summary>
    private void CheckBurial()
    {
        if (Ground is not { } g) return;
        if (_buried.Count == 0)
            foreach (var part in Runtime.Def.Parts.Where(p => p.Kind == "block"))
                if (_bodiesById.TryGetValue(part.Id, out var body))
                    _buried[part.Id] = new Buried { Body = body, Size = (float)part.Number("size") };
        double gravity = Runtime.Outside.Gravity;
        foreach (var (id, b) in _buried)
        {
            if (!IsInstanceValid(b.Body)) continue;
            var at = PhysicsServer3D.BodyGetDirectState(b.Body.GetRid()).Transform.Origin;
            double surface = g.HeightAt(at.X, at.Z);
            b.Cover = surface - (at.Y + b.Size / 2);
            var cell = g.CellAt(at.X, at.Z);
            var soil = cell is { } c ? g.SoilOf(c) : g.Soils[0];
            bool loose = cell is { } lc && g.Loose[lc];
            b.Pull = b.Cover > 0 ? Burial.PullOut(b.Cover, b.Size, b.Body.Mass, soil, gravity, loose) : b.Body.Mass * gravity;
            bool held = Burial.Held(b.Cover, b.Size);
            if (!held && b.Held && !(Math.Abs(b.Cover - b.Refused) < 0.01) && !FreeInPlace(id, b, g))
            {
                b.Refused = b.Cover;
                held = true;   // the ground could not be bared round it: it stays held where it is (never let go inside the ground)
            }
            else if (!held && b.Held && Math.Abs(b.Cover - b.Refused) < 0.01) held = true;
            if (held)
            {
                b.Body.Freeze = true;   // held by the ground over it (again, if a pause and run unfroze it)
                b.Tag ??= NewBurialTag();
                b.Freeing = -1;
            }
            else if (b.Freeing >= 0)
                LetGo(id, b);
            b.Held = held;
            if (b.Tag is { } tag)
            {
                tag.Visible = held;
                tag.Position = new Vector3(at.X, (float)surface + 0.4f, at.Z);
                tag.Text = BurialTagText(id, b);   // no depth or pull until found (#240, MachineView.Found.cs)
            }
        }
    }

    /// <summary>Ticks a bared block waits, frozen, for the ground's collision to match the ground before it is let go regardless.</summary>
    private const int FreeingTicks = 30;
    /// <summary>m each half-size of the box is shrunk by when it is checked against the ground: what touching (not overlapping) allows.</summary>
    private const float FreeingSlack = 0.005f;

    /// <summary>
    /// A block whose cover has fallen under the hold (Burial.Held) is freed where it lies (#54, owner question 2026-10-10: it used to be
    /// lifted to stand on the ground over it, a 0.6 m jump out of its pit). The ground round it is bared (Terrain.FreeBlock: the crust
    /// over it crumbles off round it as loose spoil, the soil beside it that only the grid takes is spread as a thin skin over the
    /// ground above, volume kept, and the ground under it is its base), and it stays frozen until the ground's new body is there
    /// under it and clear of it (LetGo), the next tick or so: the terrain view rebuilds a patch's body after the patch changes.
    /// False if the ground could not be bared: it then stays held.
    /// </summary>
    private bool FreeInPlace(string id, Buried b, Terrain g)
    {
        var state = PhysicsServer3D.BodyGetDirectState(b.Body.GetRid()).Transform;
        var at = state.Origin;
        var basis = state.Basis;
        // the box's extent along each world axis (a block lies square, but a tilted one is covered too)
        float h = b.Size / 2;
        var half = new Vector3(
            h * (Mathf.Abs(basis.X.X) + Mathf.Abs(basis.Y.X) + Mathf.Abs(basis.Z.X)),
            h * (Mathf.Abs(basis.X.Y) + Mathf.Abs(basis.Y.Y) + Mathf.Abs(basis.Z.Y)),
            h * (Mathf.Abs(basis.X.Z) + Mathf.Abs(basis.Y.Z) + Mathf.Abs(basis.Z.Z)));
        double before = g.HeightAt(at.X, at.Z);
        string where = $"{Name}.{id}";
        b.Body.Freeze = true;
        // the ground it lies in: a worked patch over it, else the map's own cells if they are fine enough, else a patch made for it
        string? inTheWay = null;
        bool InTheWay(WorkedGround.Raised r) => GroundRisesInto(r, b.Body) is { } name && (inTheWay = name) is not null;
        double gravity = Runtime.Outside.Gravity, bottom = at.Y - half.Y, top = at.Y + half.Y;
        FreedBlock? freed;
        double cellSize = WorkedGround.FineCell;
        var patch = g.Worked.FirstOrDefault(w => w.Inside(at.X, at.Z, Math.Max(half.X, half.Z) + 1));
        if (patch is null && g.FreesOnMap)
        {
            cellSize = g.Cell;
            freed = g.FreeBlockOnMap(at.X, at.Z, half.X, half.Z, bottom, top, gravity, InTheWay, (x, z) => ClearOfBodies(g, x, z, b.Body));
        }
        else if ((patch ?? g.WorkAt(at.X, at.Z)) is { } work)
            freed = work.FreeBlock(at.X, at.Z, half.X, half.Z, bottom, top, gravity, InTheWay, (x, z) => ClearOfBodies(g, x, z, b.Body));
        else
        {
            GD.Print($"[burial] {where} under {b.Cover:0.000} m stays held: no workable ground round it (the map's edge) to bare it in");
            return false;
        }
        if (freed is not { } f)
        {
            GD.Print($"[burial] {where} under {b.Cover:0.000} m stays held: the soil over and beside it found no way to fall clear of {inTheWay ?? "what stands by"}");
            return false;
        }
        GD.Print($"[burial] {where} freed in place: crust {f.Crust:0.000} m³ {(f.Skin > f.Soil - f.Crust + 1e-9 ? "(nowhere to fall clear of a body: in the skin)" : "to its sides")}; "
               + $"{f.Soil - f.Crust:0.000} m³ beside it (the grid's: a height map's wall slopes over a whole {cellSize} m cell, so the pit is that much wider than its {2 * half.X:0.00} x {2 * half.Z:0.00} m footprint) "
               + $"spread {f.SkinRise * 100:0.0} cm thick over the ground above it; soil kept: {f.Spoil:0.000} of {f.Soil:0.000} m³{(Math.Abs(f.Spoil - f.Soil) < 1e-9 ? "" : " SHORT")}. "
               + $"The ground over it {before:0.000} -> {g.HeightAt(at.X, at.Z):0.000}, its base {bottom:0.000}; its centre stays at y {at.Y:0.000}; {f.Nodes} nodes laid at its base, {f.Block:0.000} m³ of the surface was the block itself");
        b.Freeing = 0;
        b.Refused = double.NaN;
        return true;
    }

    /// <summary>
    /// A bared block is let go once the ground's body is there under it (a slab just under its base meets the ground) and clear of it
    /// (its box, shrunk by FreeingSlack, meets none): it then starts at rest with nothing to push it, no overlap for Jolt to resolve.
    /// If after FreeingTicks the two still disagree, it is let go anyway and the reason printed (never moved to make them agree).
    /// </summary>
    private void LetGo(string id, Buried b)
    {
        var t = PhysicsServer3D.BodyGetDirectState(b.Body.GetRid()).Transform;
        float h = b.Size / 2;
        bool clear = !TouchesGround(b.Body, t, new Vector3(h - FreeingSlack, h - FreeingSlack, h - FreeingSlack), Vector3.Zero);
        bool under = TouchesGround(b.Body, t, new Vector3(h - FreeingSlack, 0.02f, h - FreeingSlack), new Vector3(0, -h, 0));
        if (!(clear && under) && ++b.Freeing < FreeingTicks) return;
        b.Body.Freeze = false;
        b.Body.LinearVelocity = b.Body.AngularVelocity = Vector3.Zero;
        b.Freeing = -1;
        GD.Print($"[burial] {Name}.{id} let go at y {t.Origin.Y:0.000}"
               + (clear && under ? ", at rest on its base, clear of the ground round it" : $" after {FreeingTicks} ticks: {(clear ? "" : "the ground's collision still crosses its box; ")}{(under ? "" : "no ground under its base")}"));
    }

    /// <summary>Whether a box (half-extents <paramref name="half"/>, in the body's frame, offset by <paramref name="offset"/> in it) meets the ground's bodies.</summary>
    private bool TouchesGround(RigidBody3D self, Transform3D t, Vector3 half, Vector3 offset)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = half * 2 }, CollideWithAreas = false, CollideWithBodies = true,
            CollisionMask = TerrainView.GroundLayer, Exclude = new Godot.Collections.Array<Rid> { self.GetRid() },
            Transform = new Transform3D(t.Basis, t * offset),
        };
        foreach (var hit in GetWorld3D().DirectSpaceState.IntersectShape(query, 16))
            if (hit["collider"].AsGodotObject() is StaticBody3D sb && sb.GetParent() is TerrainView) return true;
        return false;
    }

    /// <summary>m round a node within which a body counts as reached by the crumbled crust rising there.</summary>
    private const float CrustClearance = 0.25f;

    /// <summary>
    /// The name of a body, not this machine's own, that ground risen at this node would reach or come within CrustClearance of, or
    /// null (Terrain.FreeBlock then tips its spoil well away from it). Spoil tipped in one go beside the rover that dug the block out
    /// once settled into the gap between two of its wheels, where a cell's own column misses both, and wedged them.
    /// </summary>
    /// <summary>m: a freed block's crumbled crust is tipped no nearer than this to any other body (a rover standing by, the backhoe that
    /// dug it out): on rubble at its repose loose soil runs on downhill, and soil that comes to rest against a body's wheels is soil
    /// the next settling (the backhoe's, which looks only at each node's own column) runs under or between them.</summary>
    private const float SpoilKeepClear = 2.0f;

    /// <summary>Whether no rigid body but this machine's own stands within SpoilKeepClear of (x, z) at the ground there.</summary>
    private bool ClearOfBodies(Terrain g, double x, double z, RigidBody3D self)
    {
        float y = (float)g.HeightAt(x, z);
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = new Vector3(2 * SpoilKeepClear, 4, 2 * SpoilKeepClear) }, CollideWithAreas = false, CollideWithBodies = true,
            Transform = new Transform3D(Basis.Identity, new Vector3((float)x, y + 1, (float)z)),
        };
        return FirstBodyHit(query, self) is null;
    }

    /// <summary>The first rigid body, not this machine's own, a query meets; the ground's answers (a height map answers once a
    /// triangle, so it alone can fill a page of results) are left out and the query asked again until a page holds no ground.</summary>
    private RigidBody3D? FirstBodyHit(PhysicsShapeQueryParameters3D query, RigidBody3D self)
    {
        var exclude = new Godot.Collections.Array<Rid> { self.GetRid() };
        var space = GetWorld3D().DirectSpaceState;
        for (int round = 0; round < 8; round++)
        {
            query.Exclude = exclude;
            var hits = space.IntersectShape(query, 32);
            bool more = false;
            foreach (var hit in hits)
            {
                var other = hit["collider"].AsGodotObject();
                if (other is RigidBody3D body && !IsAncestorOf(body)) return body;
                if (other is CollisionObject3D o && !exclude.Contains(o.GetRid())) { exclude.Add(o.GetRid()); more = true; }
            }
            if (hits.Count < 32 || !more) return null;
        }
        return null;
    }

    private string? GroundRisesInto(WorkedGround.Raised r, RigidBody3D self)
    {
        float cell = (float)WorkedGround.FineCell;
        float lo = (float)r.Before - 0.03f, hi = (float)r.After + CrustClearance;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = new Vector3(cell + 2 * CrustClearance, hi - lo, cell + 2 * CrustClearance) }, CollideWithAreas = false, CollideWithBodies = true,
            Transform = new Transform3D(Basis.Identity, new Vector3((float)r.X, (lo + hi) / 2, (float)r.Z)),
        };
        return FirstBodyHit(query, self)?.Name;
    }

    private Label3D NewBurialTag()
    {
        var tag = new Label3D { FontSize = 26, OutlineSize = 8, PixelSize = 0.008f, Modulate = new Color(1, 0.85f, 0.5f), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
        AddChild(tag);
        return tag;
    }

    /// <summary>The burial readings, for the trace: cover (m), buried (0/1), pull-out (N).</summary>
    private IEnumerable<(string Key, double Value)> BurialFields() =>
        _buried.SelectMany(kv => new[]
        {
            ($"{kv.Key}.cover", kv.Value.Cover), ($"{kv.Key}.buried", kv.Value.Held ? 1.0 : 0.0), ($"{kv.Key}.pull-out", kv.Value.Pull),
        });
}
