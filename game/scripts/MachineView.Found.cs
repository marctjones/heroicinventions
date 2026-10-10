using Godot;
using HeroicInventions.Sim.Game;

namespace HeroicInventions;

/// <summary>
/// Whether the rover has found a buried block of this machine (owner decision on #240, Sim/Game/CargoFind.cs). Main sets
/// <see cref="RoughUntilFound"/> on the rover's cargo in a rover world; then the scene's burial tag (MachineView.Burial.cs) and build mode's
/// buried marker (BuildMode.Buried.cs) give no place or depth until the block is found, and the exact ones after. Main asks
/// <see cref="CheckFound"/> each frame once the ground has settled; a found block stays found (Main.Found.cs saves it). Anything else (a
/// machine run, a scene without the rover) is never rough: a person there may know where they put a thing.
/// </summary>
public partial class MachineView
{
    /// <summary>True for the rover's cargo: buried blocks show only their rough area until found.</summary>
    public bool RoughUntilFound { get; set; }

    private readonly HashSet<string> _foundBlocks = [];
    private readonly HashSet<string> _everHeld = [];

    /// <summary>Whether the block's place and depth may be shown: found, or not rough at all.</summary>
    public bool BlockFound(string id) => !RoughUntilFound || _foundBlocks.Contains(id);

    /// <summary>The blocks found so far (for the save).</summary>
    public IReadOnlyCollection<string> FoundBlocks => _foundBlocks;

    /// <summary>Marks a block found; logs the first time, with why.</summary>
    public void MarkFound(string id, string why)
    {
        if (_foundBlocks.Add(id)) GD.Print($"[found] {Name}.{id} at t {Runtime.Time:0.0}: {why}");
    }

    /// <summary>
    /// The found rule for each buried block not yet found: the least cover over its top, freed by the ground, struck by the teeth of a dig
    /// at <paramref name="digAt"/> (a dig new since the last call, else null), touched by a body of the rover (<paramref name="isRover"/>).
    /// </summary>
    /// <summary>A block's own labels (its name and mass) stand over the true spot: shown only once found (#240). Every frame, settled or not.</summary>
    public void ShowFoundLabels()
    {
        if (!RoughUntilFound) return;
        foreach (var (id, b) in _buried)
            if (IsInstanceValid(b.Body))
                foreach (var label in b.Body.GetChildren().OfType<Label3D>()) label.Visible = BlockFound(id);
    }

    public void CheckFound(Vector3? digAt, Func<GodotObject, bool>? isRover)
    {
        if (!RoughUntilFound || Ground is not { } g) return;
        foreach (var (id, b) in _buried)
        {
            if (_foundBlocks.Contains(id) || !IsInstanceValid(b.Body)) continue;
            if (b.Held) _everHeld.Add(id);
            var xf = PhysicsServer3D.BodyGetDirectState(b.Body.GetRid()).Transform;
            var at = xf.Origin;
            double top = at.Y + b.Size / 2;   // as CheckBurial measures the cover
            Vector3 ax = new Vector3(xf.Basis.Column0.X, 0, xf.Basis.Column0.Z).Normalized(), az = new Vector3(xf.Basis.Column2.X, 0, xf.Basis.Column2.Z).Normalized();
            if (!ax.IsFinite() || ax.LengthSquared() < 0.5f) ax = Vector3.Right;
            if (!az.IsFinite() || az.LengthSquared() < 0.5f) az = Vector3.Back;
            double cover = CargoFind.TopCover(top, CargoFind.TopSamples(b.Size).Select(s =>
            {
                var p = at + ax * (float)s.U + az * (float)s.V;
                return g.HeightAt(p.X, p.Z);
            }));
            bool freed = _everHeld.Contains(id) && !b.Held;
            bool struck = digAt is { } d && xf.AffineInverse() * d is var l && CargoFind.InBox(l.X, l.Y, l.Z, b.Size, CargoFind.StrikeReach);
            bool touched = isRover is not null && Touched(b, xf, isRover);
            if (CargoFind.Why(cover, freed, struck, touched) is { } why)
                MarkFound(id, struck && digAt is { } t && cover > CargoFind.ExposedCover && !freed
                    ? $"{why}: the teeth at ({t.X:0.00} {t.Y:0.00} {t.Z:0.00}), the crate from {at.Y - b.Size / 2:0.00} to {top:0.00} round ({at.X:0.00} {at.Z:0.00}), {cover:0.00} m of ground over its top"
                    : why);
        }
    }

    private bool Touched(Buried b, Transform3D xf, Func<GodotObject, bool> isRover)
    {
        if (GetWorld3D()?.DirectSpaceState is not { } space) return false;
        float side = b.Size + 2 * (float)CargoFind.TouchGap;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = new Vector3(side, side, side) }, Transform = xf, CollideWithBodies = true, Exclude = [b.Body.GetRid()],
        };
        foreach (var hit in space.IntersectShape(query, 16))
            if (hit["collider"].AsGodotObject() is { } o && isRover(o)) return true;
        return false;
    }

    /// <summary>The burial tag's text: exact once found; before, nothing (the cargo marker's rough area and name already say it is somewhere there, and the tag stands over the true place).</summary>
    private string BurialTagText(string id, Buried b) =>
        BlockFound(id) ? $"{id} buried {b.Cover:F2} m · {b.Pull / 1000:F1} kN to pull out" : "";
}
