using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The rover in a world save (issue #201): <c>(rover 1 (chassis …) (wheel K …) … (drive …) (bucket …) (arm …))</c>. Each body is its
/// transform (the basis's three columns, then the origin), its linear velocity and its spin, all as the doubles the floats widen to,
/// so reading them back gives the very same floats. The axle joints keep no state of their own: the spring's stretch, the wheel's
/// turn and the motor's target all follow from the two bodies' poses and the drive state, so putting the bodies back puts the
/// joints back, and the rover lands sprung. (What Jolt cannot hand over is a constraint's warm-start impulse, which it works up
/// again within a tick or two.) The bucket is its load and its running totals (a save from before #72's 2026-10-08 rule also holds the
/// load's carry ceiling, which is read past: soil now goes wherever the arm reaches); the arm is the cycle's
/// step and how far into it, with the poses it is moving between, so a cycle resumes where it was.
/// </summary>
public sealed partial class Rover
{
    public const int SaveVersion = 1;

    private static SList Form(string name, params double[] numbers) => new([new SSymbol(name), .. numbers.Select(n => (SExpr)new SNumber(n))]);
    private static double[] Nums(SList l, string name) => l.Field(name)?.Items.Skip(1).OfType<SNumber>().Select(n => n.Value).ToArray() ?? throw new FormatException($"the rover's save has no ({name} …)");
    private static double Num(SList l, string name) => Nums(l, name) is [var v, ..] ? v : throw new FormatException($"({name}) has no number");

    private static double[] V(Vector3 v) => [v.X, v.Y, v.Z];
    private static Vector3 V3(double[] n, int at = 0) => new((float)n[at], (float)n[at + 1], (float)n[at + 2]);
    private static double[] PoseNums(Pose p) => [p.Swing, p.A1, p.A2, p.A3];

    /// <summary>The state of one body as the physics server holds it now (the node shows the tick before).</summary>
    private static SList BodyForm(string head, RigidBody3D body, params double[] first)
    {
        var rid = body.GetRid();
        var t = (Transform3D)PhysicsServer3D.BodyGetState(rid, PhysicsServer3D.BodyState.Transform);
        var v = (Vector3)PhysicsServer3D.BodyGetState(rid, PhysicsServer3D.BodyState.LinearVelocity);
        var w = (Vector3)PhysicsServer3D.BodyGetState(rid, PhysicsServer3D.BodyState.AngularVelocity);
        return new SList([new SSymbol(head), .. first.Select(n => (SExpr)new SNumber(n)),
            Form("xf", [.. V(t.Basis.Column0), .. V(t.Basis.Column1), .. V(t.Basis.Column2), .. V(t.Origin)]), Form("vel", V(v)), Form("spin", V(w))]);
    }

    private static void SetBody(RigidBody3D body, SList form)
    {
        var x = Nums(form, "xf");
        if (x.Length != 12) throw new FormatException("a body's (xf …) has twelve numbers");
        body.GlobalTransform = new Transform3D(new Basis(V3(x, 0), V3(x, 3), V3(x, 6)), V3(x, 9));
        body.LinearVelocity = V3(Nums(form, "vel"));
        body.AngularVelocity = V3(Nums(form, "spin"));
    }

    public SList SaveState()
    {
        var items = new List<SExpr> { new SSymbol("rover"), new SNumber(SaveVersion), BodyForm("chassis", Chassis) };
        for (int k = 0; k < _wheels.Count; k++) items.Add(BodyForm("wheel", _wheels[k], k));
        items.Add(new SList([new SSymbol("drive"), Form("command", Command.Forward, Command.Turn), Form("speed", _speedCmd), Form("turn", _turnCmd), Form("rescues", Rescues)]));
        items.Add(new SList([new SSymbol("bucket"), Form("carried", Carried),
            Form("soil", _carriedSoil), Form("dug", Dug), Form("dumped", Dumped), Form("cycles", Cycles),
            Form("dig-at", V(_lastDigAt)), Form("dump-at", V(_lastDumpAt))]));
        items.Add(new SList([new SSymbol("arm"), Form("step", _step), Form("time", _stepTime), new SList([new SSymbol("refused"), new SBool(_refused)]),
            new SList([new SSymbol("status"), new SString(ArmStatus)]),
            Form("pose", PoseNums(_pose)), Form("from", PoseNums(_from)), Form("dig-start", PoseNums(_digStart)), Form("dig-end", PoseNums(_digEnd)),
            // the road's carrying (2026-10-10): the cycle's kind, its side and swing, and a load kept on the deck (absent in older saves: a full cycle to the right)
            Form("cycle", Cycle == DigOnlyCycle ? 1 : Cycle == DumpOnlyCycle ? 2 : 0), Form("side", (int)_side), Form("dig-swing", _digSwing),
            new SList([new SSymbol("keep"), new SBool(_keep)]), new SList([new SSymbol("carrying"), new SBool(CarryingOn)])]));
        return new SList(items);
    }

    /// <summary>Puts the rover back as <see cref="SaveState"/> left it. Throws FormatException on a save it cannot read, leaving the rover as it was.</summary>
    public void LoadState(SList saved)
    {
        if (saved.Items.ElementAtOrDefault(1) is not SNumber v || v.Value != SaveVersion)
            throw new FormatException($"the save's rover is version {(saved.Items.ElementAtOrDefault(1) as SNumber)?.Value}; this game reads version {SaveVersion}");
        var wheels = saved.Fields("wheel").Select(w => (K: w.Items.ElementAtOrDefault(1) is SNumber n ? (int)n.Value : -1, Form: w)).ToList();
        if (saved.Field("chassis") is not { } chassis || wheels.Count != _wheels.Count || wheels.Any(w => w.K < 0 || w.K >= _wheels.Count))
            throw new FormatException("the rover's save needs a chassis and its six wheels");
        foreach (var (_, f) in wheels) if (Nums(f, "xf").Length != 12) throw new FormatException("a body's (xf …) has twelve numbers");
        if (Nums(chassis, "xf").Length != 12) throw new FormatException("a body's (xf …) has twelve numbers");
        var drive = saved.Field("drive") ?? throw new FormatException("the rover's save has no (drive …)");
        var bucket = saved.Field("bucket") ?? throw new FormatException("the rover's save has no (bucket …)");
        var arm = saved.Field("arm") ?? throw new FormatException("the rover's save has no (arm …)");
        Pose P(string n) => Nums(arm, n) is [var s, var a1, var a2, var a3] ? new Pose(s, a1, a2, a3) : throw new FormatException($"({n}) is four angles");
        var cmd = Nums(drive, "command");
        var (pose, from, digStart, digEnd) = (P("pose"), P("from"), P("dig-start"), P("dig-end"));
        int step = (int)Num(arm, "step");
        var cycle = arm.Field("cycle") is null ? FullCycle : Num(arm, "cycle") switch { 1 => DigOnlyCycle, 2 => DumpOnlyCycle, _ => FullCycle };
        if (step < -1 || step >= cycle.Length) throw new FormatException("the arm's step is not one of the cycle's");

        // all read: now set
        SetBody(Chassis, chassis);
        foreach (var (k, f) in wheels) SetBody(_wheels[k], f);
        Command = (cmd[0], cmd[1]);
        _speedCmd = Num(drive, "speed"); _turnCmd = Num(drive, "turn"); Rescues = (int)Num(drive, "rescues");
        Carried = Num(bucket, "carried");
        _carriedSoil = (int)Num(bucket, "soil"); Dug = Num(bucket, "dug"); Dumped = Num(bucket, "dumped"); Cycles = (int)Num(bucket, "cycles");
        _lastDigAt = V3(Nums(bucket, "dig-at")); _lastDumpAt = V3(Nums(bucket, "dump-at"));
        _step = step; _stepTime = Num(arm, "time");
        _refused = arm.Field("refused")?.Items.ElementAtOrDefault(1) is SBool { Value: true };
        ArmStatus = arm.Field("status")?.Items.ElementAtOrDefault(1) is SString s ? s.Value : ArmStatus;
        _from = from; _digStart = digStart; _digEnd = digEnd;
        Cycle = cycle;
        _side = arm.Field("side") is null || Num(arm, "side") < 0 ? ArmSide.Right : ArmSide.Left;
        _digSwing = arm.Field("dig-swing") is null ? 0 : Num(arm, "dig-swing");
        _keep = arm.Field("keep")?.Items.ElementAtOrDefault(1) is SBool { Value: true };
        CarryingOn = arm.Field("carrying")?.Items.ElementAtOrDefault(1) is SBool { Value: true };
        Apply(pose);
    }
}
