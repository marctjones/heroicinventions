using Godot;
using HeroicInventions.Sim.Fluids;

namespace HeroicInventions;

/// <summary>
/// The backhoe (issue #94): a boom, a stick and a bucket on a turntable at the rover's front, played as one dig-and-dump cycle
/// when the player asks (key B). It stows over the deck, reaches out, lowers the bucket to the ground ahead (the boom angle is
/// solved so the teeth meet the ground wherever it is, on a slope too), draws it in curling the bucket, lifts, swings to the
/// side, lowers and tips it out, swings back and folds away.
///
/// The earthworks are real where the ground can be edited: the ground here is 5 m cells, so where the teeth first dig the map
/// gets a 30 m patch of 0.25 m cells (<see cref="WorkedGround"/>) and the bucket scrapes those: at the end of the dig stroke the
/// bucket's volume is taken from the nodes within 0.45 m of the teeth (never from bedrock), a third of a metre deep, and where
/// the bucket tips it is put back on the nodes there. The two volumes are the same, so what is dug is dumped, and the heap and
/// the hole then settle by Mohr–Coulomb as the rest of the ground does. It digs and dumps wherever the arm reaches, a hole down to
/// bedrock and a heap on a heap (#72, owner decision 2026-10-08: soil is not a load, and nothing is refused). Soil goes round the
/// bodies on the ground, as gravel poured against a crate piles round it: a tip lands on the open ground of its footprint, and a
/// slide's deposit stops at a body's base, the body a retaining wall (<see cref="WorkedGround.Around"/>), so piling soil never lifts
/// or shoves a load. Digging away what a body rests on is allowed: it falls as gravity takes it. Only a bucket held right over a body,
/// its whole footprint covered, keeps its load.
/// </summary>
public sealed partial class Rover
{
    /// <summary>m³ one bucket holds (a small backhoe's is 0.1 to 0.3; Opportunity's scoop is a few cm³, but the game's arm is the player's tool).</summary>
    public const double BucketVolume = 0.2;

    /// <summary>m, the boom, stick and bucket (their sum is the arm's reach from the turntable); public so Rover.cs can read them instead of copying the numbers.</summary>
    public const float BoomLength = 0.8f, StickLength = 0.7f, BucketLength = 0.3f;
    /// <summary>The turntable's place on the chassis (x, y, z): at the front, 0.7 m ahead of the centre.</summary>
    public static readonly Vector3 TurntableLocal = new(0, 0.34f, -0.7f);

    private readonly record struct Pose(double Swing, double A1, double A2, double A3);

    // degrees: the turntable's swing (+ to the left), then the boom's angle above the horizontal ahead, the stick's and the
    // bucket's each relative to the part before (so the stick points at A1 + A2 and the bucket at A1 + A2 + A3)
    private static readonly Pose Stowed = new(0, 172, -170, -50);
    private static readonly Pose Reach = new(0, 40, -62, -25);
    private static readonly Pose DigStart = new(0, 20, -55, -35);   // A1 is solved so the teeth meet the ground
    private static readonly Pose DigEnd = new(0, 20, -92, 55);      // drawn in, the bucket curled up: level and full
    private static readonly Pose Carry = new(0, 50, -75, 30);
    private static readonly Pose Dumping = new(-75, 28, -50, -10);   // to the right; a dump to the left swings the other way (ArmSide)
    private static readonly Pose Tipped = new(-75, 28, -50, -85);

    private enum Phase { Stowed, Reaching, Lowering, Digging, Lifting, Swinging, Placing, Dumping, SwingingBack, Stowing }
    private static readonly (Phase Phase, double Seconds)[] FullCycle =
    [
        (Phase.Reaching, 1.4), (Phase.Lowering, 1.0), (Phase.Digging, 1.8), (Phase.Lifting, 1.0), (Phase.Swinging, 1.4),
        (Phase.Placing, 0.9), (Phase.Dumping, 1.1), (Phase.SwingingBack, 1.4), (Phase.Stowing, 1.6),
    ];
    // carrying (the road's haul, #road-plan): a dig that keeps its load folds away with it, and the dump-only cycle that follows
    // lifts it from the deck, swings, tips and folds away
    private static readonly (Phase Phase, double Seconds)[] DigOnlyCycle = [.. FullCycle.Take(4), FullCycle[^1]];
    private static readonly (Phase Phase, double Seconds)[] DumpOnlyCycle = [(Phase.Lifting, 1.0), .. FullCycle.Skip(4)];
    private (Phase Phase, double Seconds)[] Cycle = FullCycle;

    private Node3D _swing = null!, _boom = null!, _stick = null!, _bucket = null!, _tip = null!;
    private MeshInstance3D _fill = null!;
    private Pose _pose = Stowed, _from = Stowed;
    private int _step = -1;
    private double _stepTime;

    /// <summary>The ground the arm digs, or null (the arm then only moves). Set by the world; <see cref="Gravity"/> is the ground's.</summary>
    public Terrain? Ground { get; set; }
    public double GroundGravity { get; set; } = RoverSpec.Gravity;

    /// <summary>m³ in the bucket now.</summary>
    public double Carried { get; private set; }
    /// <summary>m³ taken out of the ground, and put back, since the start: equal once a cycle has finished with a dump.</summary>
    public double Dug { get; private set; }
    public double Dumped { get; private set; }
    /// <summary>Cycles that dug and dumped without being refused.</summary>
    public int Cycles { get; private set; }
    /// <summary>What the arm last said or refused, for the HUD.</summary>
    public string ArmStatus { get; private set; } = "Backhoe stowed";

    public bool ArmBusy => _step >= 0;

    /// <summary>Which part of the cycle the arm is in: Stowed, Reaching, Lowering, Digging, Lifting, Swinging, Placing, Dumping, SwingingBack, Stowing.</summary>
    public string PhaseName => _step < 0 ? nameof(Phase.Stowed) : Cycle[_step].Phase.ToString();

    private Vector3 _lastDigAt, _lastDumpAt;
    public Vector3 LastDigAt => _lastDigAt;
    public Vector3 LastDumpAt => _lastDumpAt;

    private void BuildBackhoe()
    {
        var arm = Arm;
        _swing = new Node3D { Name = "ArmTurntable", Position = TurntableLocal };
        Chassis.AddChild(_swing);
        Part(_swing, "arm-turntable", Shapes.Cylinder(0.1f, 0.1f, Shapes.Mat(arm, metallic: 0.3f, roughness: 0.5f)), new Vector3(0, 0.03f, 0));
        _boom = new Node3D { Name = "Boom", Position = new Vector3(0, 0.1f, 0) };
        _swing.AddChild(_boom);
        ArmPart(_boom, "arm-boom", new Vector3(0.08f, 0.1f, BoomLength + 0.08f), arm, new Vector3(0, 0, -BoomLength / 2));
        _stick = new Node3D { Name = "Stick", Position = new Vector3(0, 0, -BoomLength) };
        _boom.AddChild(_stick);
        ArmPart(_stick, "arm-stick", new Vector3(0.07f, 0.08f, StickLength + 0.08f), arm, new Vector3(0, 0, -StickLength / 2));
        _bucket = new Node3D { Name = "Bucket", Position = new Vector3(0, 0, -StickLength) };
        _stick.AddChild(_bucket);
        // the bucket: a floor, a back, two sides, three teeth; its open side is up when the arm holds it level
        var steel = Color.FromHtml("#8E8A84");
        ArmPart(_bucket, "bucket-floor", new Vector3(0.3f, 0.025f, BucketLength), steel, new Vector3(0, -0.0125f, -BucketLength / 2));
        ArmPart(_bucket, "bucket-back", new Vector3(0.3f, 0.18f, 0.025f), steel, new Vector3(0, 0.08f, 0.0125f));
        foreach (var (x, i) in new[] { (-0.1375f, 0), (0.1375f, 1) })
            ArmPart(_bucket, $"bucket-side-{i}", new Vector3(0.025f, 0.16f, BucketLength), steel, new Vector3(x, 0.07f, -BucketLength / 2));
        foreach (var (x, i) in new[] { (-0.1f, 0), (0f, 1), (0.1f, 2) })
            ArmPart(_bucket, $"bucket-tooth-{i}", new Vector3(0.04f, 0.03f, 0.06f), Color.FromHtml("#E6DEC8"), new Vector3(x, -0.015f, -BucketLength - 0.02f));
        // the soil in it: a mound that grows with what is carried
        _fill = Part(_bucket, "bucket-soil", Shapes.Box(new Vector3(0.25f, 0.12f, BucketLength - 0.04f), Shapes.Mat(Soil, roughness: 1)), new Vector3(0, 0.02f, -BucketLength / 2));
        _fill.Visible = false;
        _tip = new Node3D { Name = "Teeth", Position = new Vector3(0, 0, -BucketLength - 0.04f) };
        _bucket.AddChild(_tip);
        Apply(Stowed);
    }

    /// <summary>Which side the bucket swings to to tip: the turntable's swing is + to the left.</summary>
    public enum ArmSide { Right = -1, Left = 1 }

    private const double DumpSwingDeg = 75;   // the dump's swing either side, degrees (Dumping.Swing is the right's)
    private ArmSide _side = ArmSide.Right;
    private double _digSwing;
    private bool _keep;
    /// <summary>A load kept by a dig-only cycle is in the bucket: the next cycle only tips it.</summary>
    public bool CarryingOn { get; private set; }

    /// <summary>
    /// Starts the dig-and-dump cycle; ignored while one is under way. <paramref name="dump"/> is the side it tips to (B: right,
    /// Shift+B: left); <paramref name="digSwingDeg"/> swings the dig that many degrees left (+) or right of straight ahead; with
    /// <paramref name="keep"/> it digs and folds away with the load, carrying it (the road's haul) and the next cycle, whatever its
    /// arguments say of digging, only lifts the load from the deck and tips it (unless it keeps again with the bucket less than
    /// 0.85 full: then it digs again and tops the load up).
    /// </summary>
    public bool StartCycle(ArmSide dump = ArmSide.Right, double digSwingDeg = 0, bool keep = false)
    {
        if (_step >= 0) return false;
        _side = dump;
        _digSwing = Math.Clamp(digSwingDeg, -45, 45);
        bool dumpOnly = CarryingOn && Carried > 1e-9 && !(keep && Carried < 0.85 * BucketVolume);   // a dig that keeps its load tops a part-load up
        _keep = keep && !dumpOnly;
        Cycle = dumpOnly ? DumpOnlyCycle : _keep ? DigOnlyCycle : FullCycle;
        CarryingOn = false;
        _step = 0;
        _refused = false;
        _stepTime = 0;
        _from = _pose;
        ArmStatus = dumpOnly ? "Backhoe lifting the load it carried" : "Backhoe reaching out";
        if (dumpOnly) BeginOf(Cycle[0].Phase);
        return true;
    }

    /// <summary>Where the bucket would tip to the given side, in the world, the rover as it stands now (the arm put there and back in the same frame).</summary>
    public Vector3 DumpPoint(ArmSide side) => TipAt(Tipped with { Swing = DumpSwingDeg * (int)side });

    /// <summary>Where the teeth would end a dig swung <paramref name="digSwingDeg"/> to the left, the rover as it stands now: the dig's own boom solve.</summary>
    public Vector3 DigPoint(double digSwingDeg)
    {
        var pose = DigEnd with { Swing = digSwingDeg };
        return TipAt(pose with { A1 = SolveBoomToGround(pose) });
    }

    private Vector3 TipAt(Pose p)
    {
        var was = _pose;
        Apply(p);
        var tip = _tip.GlobalPosition;
        Apply(was);
        return tip;
    }

    private void Apply(Pose p)
    {
        _pose = p;
        _swing.RotationDegrees = new Vector3(0, (float)p.Swing, 0);
        _boom.RotationDegrees = new Vector3((float)p.A1, 0, 0);
        _stick.RotationDegrees = new Vector3((float)p.A2, 0, 0);
        _bucket.RotationDegrees = new Vector3((float)p.A3, 0, 0);
        double fill = Carried / BucketVolume;
        _fill.Visible = fill > 1e-6;
        _fill.Scale = new Vector3(1, (float)Math.Max(0.05, fill), 1);
    }

    private static Pose Lerp(Pose a, Pose b, double t)
    {
        double s = t * t * (3 - 2 * t);   // smoothstep: a hydraulic arm starts and stops gently
        return new Pose(a.Swing + (b.Swing - a.Swing) * s, a.A1 + (b.A1 - a.A1) * s, a.A2 + (b.A2 - a.A2) * s, a.A3 + (b.A3 - a.A3) * s);
    }

    private Pose _digStart, _digEnd;

    public override void _Process(double delta)
    {
        if (_step < 0 || Frozen) return;   // paused: the arm holds still too
        _stepTime += delta;
        var (phase, seconds) = Cycle[_step];
        double t = Math.Min(1, _stepTime / seconds);
        Apply(Lerp(_from, Target(phase), t));
        if (t < 1) return;
        EndOf(phase);
        _step++;
        _stepTime = 0;
        _from = _pose;
        if (_step >= Cycle.Length)
        {
            _step = -1;
            Apply(Stowed);
            if (ArmStatus.StartsWith("Backhoe")) ArmStatus = Carried > 0 ? $"Backhoe stowed, {Carried:0.00} m³ in the bucket" : "Backhoe stowed";   // else it keeps what it last said: Dumped, Dug nothing, Kept
        }
        else BeginOf(Cycle[_step].Phase);
    }

    private Pose Target(Phase phase) => phase switch
    {
        Phase.Reaching => Reach,
        Phase.Lowering => _digStart,
        Phase.Digging => _digEnd,
        Phase.Lifting => Carry,
        Phase.Swinging => Carry with { Swing = DumpSwingDeg * (int)_side },
        Phase.Placing => Dumping with { Swing = DumpSwingDeg * (int)_side },
        Phase.Dumping => Tipped with { Swing = DumpSwingDeg * (int)_side },
        Phase.SwingingBack => Carry with { A3 = Tipped.A3 + 30 },
        _ => Stowed,
    };

    private void BeginOf(Phase phase)
    {
        if (_refused && phase != Phase.Lowering) return;   // keep the reason
        switch (phase)
        {
            case Phase.Lowering:
                _digStart = DigStart with { Swing = _digSwing, A1 = SolveBoomToGround(DigStart with { Swing = _digSwing }) };
                _digEnd = DigEnd with { Swing = _digSwing, A1 = SolveBoomToGround(DigEnd with { Swing = _digSwing }) };
                break;
            case Phase.Digging: ArmStatus = "Backhoe digging"; break;
            case Phase.Lifting when _keep: ArmStatus = Carried > 0 ? "Backhoe folding away with the load" : ArmStatus; break;
            case Phase.Swinging: ArmStatus = Carried > 0 ? "Backhoe swinging the load round" : "Backhoe swinging round (nothing in the bucket)"; break;
            case Phase.Placing: if (Carried > 0) ArmStatus = "Backhoe lowering the load to the ground"; break;
            case Phase.Dumping: if (Carried > 0) ArmStatus = "Backhoe tipping the bucket out"; break;
            case Phase.SwingingBack: break;
        }
    }

    private void EndOf(Phase phase)
    {
        switch (phase)
        {
            case Phase.Lowering: _lastDigAt = _tip.GlobalPosition; break;
            case Phase.Digging: DigHere(); break;
            case Phase.Dumping: DumpHere(); break;
            case Phase.Stowing when _keep || Cycle == DumpOnlyCycle: CarryingOn = Carried > 1e-9; break;   // the load rides on the deck to where it is tipped (or back, if it was refused)
        }
    }

    /// <summary>The boom angle that puts the teeth on the ground ahead, whatever its height: the highest angle at which the teeth are at or below it.</summary>
    private double SolveBoomToGround(Pose pose)
    {
        if (Ground is not { } ground) return pose.A1;
        var was = _pose;
        double best = -30;
        for (double a1 = 60; a1 >= -30; a1 -= 0.5)
        {
            Apply(pose with { A1 = a1 });
            var tip = _tip.GlobalPosition;
            if (tip.Y <= ground.HeightAt(tip.X, tip.Z) + 0.02) { best = a1; break; }
        }
        Apply(was);
        return best;
    }

    private void DigHere()
    {
        var at = _tip.GlobalPosition;
        _lastDigAt = at;
        if (Ground is not { } ground) { Refuse("Dug nothing: no ground to dig (visual only)"); return; }
        if (ground.CellAt(at.X, at.Z) is not { } cell) { Refuse("Dug nothing: off the map"); return; }
        double room = BucketVolume - Carried;
        if (room < 1e-6) { Refuse("Dug nothing: the bucket is full"); return; }
        if (at.Y > ground.HeightAt(at.X, at.Z) + 0.15) { Refuse($"Dug nothing: the teeth didn't reach the ground ({at.Y - ground.HeightAt(at.X, at.Z):0.00} m above it)"); return; }
        var soil = ground.SoilOf(cell);   // (bedrock is never cut: the patch's nodes of rock give nothing, and spoil tipped on rock is soil again)
        long t = TickProfile.Start();
        if (ground.WorkAt(at.X, at.Z) is not { } work) { Refuse("Dug nothing: too near the edge of the map or of the ground that can be worked here"); return; }
        TickProfile.Stop("backhoe-patch", t);
        t = TickProfile.Start();
        // the bucket scrapes the fine cells under the teeth, and a bank dug steeper than the soil stands slumps into the hole (#44);
        // a slump that would rise under or against a body is refused, and the ground is left as it was (#72)
        WorkedGround.Scooped? took = null;
        string? inTheWay = null;
        bool done = work.Around(() => { took = work.Scoop(at.X, at.Z, room); if (took is not null) work.Settle(GroundGravity); },
                                raised => BodyAt(raised) is { } name && (inTheWay = name) is not null);
        TickProfile.Stop("backhoe-scoop", t);
        if (!done) { Refuse($"Dug nothing: the soil found no way to settle round the {inTheWay}"); return; }
        if (took is not { } scooped) { Refuse($"Dug nothing: {soil.Material} under the teeth is too hard for the backhoe"); return; }
        _carriedSoil = scooped.Soil;
        Carried += scooped.Volume;
        Dug += scooped.Volume;
        ArmStatus = $"Dug {scooped.Volume:0.00} m³ of {soil.Material}";
    }

    private void DumpHere()
    {
        var at = _tip.GlobalPosition;
        _lastDumpAt = at;
        if (Carried < 1e-9) return;
        if (Ground is not { } ground) { Carried = 0; return; }
        if (ground.CellAt(at.X, at.Z) is null) { Refuse("Kept the load: off the map"); return; }
        if (ground.WorkAt(at.X, at.Z) is not { } work) { Refuse("Kept the load: too near the edge of the ground that can be worked here"); return; }
        // soil goes wherever the arm reaches, up or down (#72); loose soil heaped steeper than it stands slides (#44). A heap, or its
        // slide, that would rise under or against a body is refused: raising a body on soil is lifting a load by the back door
        double m3 = Carried;
        long t = TickProfile.Start();
        double? landed = null;
        string? inTheWay = null;
        bool done = work.Around(() => { landed = work.Pour(at.X, at.Z, m3, _carriedSoil); if (landed is not null) work.Settle(GroundGravity); },
                                raised => BodyAt(raised) is { } name && (inTheWay = name) is not null);
        TickProfile.Stop("backhoe-pour", t);
        if (!done) { Refuse($"Kept {Carried:0.00} m³: the soil found no way to settle round the {inTheWay}"); return; }
        if (landed is null)
        {
            Refuse(inTheWay is null ? "Kept the load: no ground to tip it on" : $"Kept {Carried:0.00} m³: the bucket is over the {inTheWay}");
            return;
        }
        Dumped += m3;
        Carried = 0;
        Cycles++;
        ArmStatus = $"Dumped {m3:0.00} m³";
    }

    /// <summary>m: how far below a node's old height a body's underside may reach and still be found (Jolt lets a resting body sink
    /// into the ground by up to its penetration slop, 0.02 m), and how far above the new height the ground must stay clear of one.</summary>
    private const float BodySlack = 0.03f, BodyClearance = 0.05f;

    /// <summary>
    /// The name of a loose body (a rigid body, frozen or not, the rover's own chassis and wheels too) that the ground, risen at
    /// this node, would reach: a column a fine cell across over it, from just under its old height to a little over its new one.
    /// Null if there is none. (#72: the rover may move soil anywhere, but not lift or push a load with it.) The rover's own wheels
    /// count (road plan, 2026-10-10): a heap tipped beside a wheel stops at it, as against a crate, so spoil never rises through a
    /// wheel and buries it (a wheel's centre under a height map never comes back out).
    /// </summary>
    private string? BodyAt(WorkedGround.Raised r)
    {
        if (!IsInsideTree()) return null;
        var space = GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid>();
        float cell = (float)WorkedGround.FineCell;
        float lo = (float)r.Before - BodySlack, hi = (float)r.After + BodyClearance;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = new Vector3(cell, hi - lo, cell) }, CollideWithAreas = false, CollideWithBodies = true, Exclude = exclude,
            Transform = new Transform3D(Basis.Identity, new Vector3((float)r.X, (lo + hi) / 2, (float)r.Z)),
        };
        foreach (var hit in space.IntersectShape(query, 32))
            if (hit["collider"].AsGodotObject() is RigidBody3D body) return body.Name;   // frozen too: it would be shoved when let go
        return null;
    }

    private bool _refused;   // this cycle's dig or dump was refused: its reason is what the status keeps saying until the arm is stowed

    private void Refuse(string why) { ArmStatus = why; _refused = true; }

    private int _carriedSoil;
}
