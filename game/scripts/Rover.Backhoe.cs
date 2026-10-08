using Godot;
using HeroicInventions.Sim.Fluids;

namespace HeroicInventions;

/// <summary>
/// The backhoe (issue #94): a boom, a stick and a bucket on a turntable at the rover's front, played as one dig-and-dump cycle
/// when the player asks (key B). It stows over the deck, reaches out, lowers the bucket to the ground ahead (the boom angle is
/// solved so the teeth meet the ground wherever it is, on a slope too), draws it in curling the bucket, lifts, swings to the
/// side, lowers and tips it out, swings back and folds away.
///
/// The earthworks are real where the ground can be edited: at the end of the dig stroke the bucket's volume is taken out of
/// the ground's cell under the teeth with <see cref="Terrain.Dig"/> (never from bedrock), and where the bucket tips it is put
/// back with <see cref="Terrain.Heap"/>. The two volumes are the same, so what is dug is dumped. It dumps only at or below the
/// elevation it dug from (#72: free effort must not be able to store energy by lifting soil up a hill), and a bucket that
/// can't dump keeps its load. The ground here is 5 m cells, so a 0.2 m³ bucket is 8 mm of one cell: the bucket shows its load
/// (a mound of soil in it), because the heap on the ground is too small to see until the ground has finer cells near the rover.
/// </summary>
public sealed partial class Rover
{
    /// <summary>m³ one bucket holds (a small backhoe's is 0.1 to 0.3; Opportunity's scoop is a few cm³, but the game's arm is the player's tool).</summary>
    public const double BucketVolume = 0.2;

    private const float BoomLength = 0.8f, StickLength = 0.7f, BucketLength = 0.3f;

    private readonly record struct Pose(double Swing, double A1, double A2, double A3);

    // degrees: the turntable's swing (+ to the left), then the boom's angle above the horizontal ahead, the stick's and the
    // bucket's each relative to the part before (so the stick points at A1 + A2 and the bucket at A1 + A2 + A3)
    private static readonly Pose Stowed = new(0, 172, -170, -50);
    private static readonly Pose Reach = new(0, 40, -62, -25);
    private static readonly Pose DigStart = new(0, 20, -55, -35);   // A1 is solved so the teeth meet the ground
    private static readonly Pose DigEnd = new(0, 20, -92, 55);      // drawn in, the bucket curled up: level and full
    private static readonly Pose Carry = new(0, 50, -75, 30);
    private static readonly Pose Dumping = new(-75, 28, -50, -10);
    private static readonly Pose Tipped = new(-75, 28, -50, -85);

    private enum Phase { Stowed, Reaching, Lowering, Digging, Lifting, Swinging, Placing, Dumping, SwingingBack, Stowing }
    private static readonly (Phase Phase, double Seconds)[] Cycle =
    [
        (Phase.Reaching, 1.4), (Phase.Lowering, 1.0), (Phase.Digging, 1.8), (Phase.Lifting, 1.0), (Phase.Swinging, 1.4),
        (Phase.Placing, 0.9), (Phase.Dumping, 1.1), (Phase.SwingingBack, 1.4), (Phase.Stowing, 1.6),
    ];

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

    private double _digElevation;     // m, the ground's height at the cell dug from
    private Vector3 _lastDigAt, _lastDumpAt;
    public Vector3 LastDigAt => _lastDigAt;
    public Vector3 LastDumpAt => _lastDumpAt;

    private void BuildBackhoe()
    {
        var arm = Arm;
        _swing = new Node3D { Name = "ArmTurntable", Position = new Vector3(0, 0.34f, -0.7f) };
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

    /// <summary>Starts the dig-and-dump cycle; ignored while one is under way.</summary>
    public bool StartCycle()
    {
        if (_step >= 0) return false;
        _step = 0;
        _stepTime = 0;
        _from = _pose;
        ArmStatus = "Backhoe reaching out";
        return true;
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
        Phase.Swinging => Carry with { Swing = Dumping.Swing },
        Phase.Placing => Dumping,
        Phase.Dumping => Tipped,
        Phase.SwingingBack => Carry with { A3 = Tipped.A3 + 30 },
        _ => Stowed,
    };

    private void BeginOf(Phase phase)
    {
        switch (phase)
        {
            case Phase.Lowering: _digStart = DigStart with { A1 = SolveBoomToGround() }; _digEnd = DigEnd with { A1 = _digStart.A1 + 4 }; break;
            case Phase.Digging: ArmStatus = "Backhoe digging"; break;
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
        }
    }

    /// <summary>The boom angle that puts the teeth on the ground ahead, whatever its height: the highest angle at which the teeth are at or below it.</summary>
    private double SolveBoomToGround()
    {
        if (Ground is not { } ground) return DigStart.A1;
        double best = -30;
        for (double a1 = 60; a1 >= -30; a1 -= 1)
        {
            Apply(DigStart with { A1 = a1 });
            var tip = _tip.GlobalPosition;
            if (tip.Y <= ground.HeightAt(tip.X, tip.Z) + 0.02) { best = a1; break; }
        }
        Apply(_from);
        return best;
    }

    private void DigHere()
    {
        var at = _tip.GlobalPosition;
        _lastDigAt = at;
        if (Ground is not { } ground) { ArmStatus = "Dug nothing: no ground to dig (visual only)"; return; }
        if (ground.CellAt(at.X, at.Z) is not { } cell) { ArmStatus = "Dug nothing: off the map"; return; }
        double room = BucketVolume - Carried;
        if (room < 1e-6) { ArmStatus = "Dug nothing: the bucket is full"; return; }
        if (at.Y > ground.HeightAt(at.X, at.Z) + 0.15) { ArmStatus = "Dug nothing: the teeth didn't reach the ground"; return; }
        var soil = ground.SoilOf(cell);
        if (soil.Cohesion >= 1e7) { ArmStatus = $"Dug nothing: {soil.Material} is too hard for the backhoe"; return; }   // bedrock: never
        _digElevation = ground.HeightAt(at.X, at.Z);   // the ground at the teeth, not the cell's centre: the cells are 5 m
        double taken = ground.Dig(cell, room / (ground.Cell * ground.Cell));
        Carried += taken;
        Dug += taken;
        ArmStatus = $"Dug {taken:0.00} m³ of {soil.Material}";
        Relax(ground, cell);
    }

    private void DumpHere()
    {
        var at = _tip.GlobalPosition;
        _lastDumpAt = at;
        if (Carried < 1e-9) return;
        if (Ground is not { } ground) { Carried = 0; return; }
        if (ground.CellAt(at.X, at.Z) is not { } cell) { ArmStatus = "Kept the load: off the map"; return; }
        // free effort must not store energy (#72): soil goes down or sideways, never up
        double here = ground.HeightAt(at.X, at.Z);
        if (here > _digElevation + 0.05) { ArmStatus = $"Kept {Carried:0.00} m³: won't dump {here - _digElevation:0.0} m above where it dug"; return; }
        double m3 = Carried;
        ground.Heap(cell, m3);
        Dumped += m3;
        Carried = 0;
        Cycles++;
        ArmStatus = $"Dumped {m3:0.00} m³";
        Relax(ground, cell);
    }

    private void Relax(Terrain ground, int cell)
    {
        int i = cell % ground.Nx, j = cell / ground.Nx;
        ground.Relax(i - 2, j - 2, i + 2, j + 2, GroundGravity);   // loose soil heaped steeper than it stands slides (#44)
    }
}
