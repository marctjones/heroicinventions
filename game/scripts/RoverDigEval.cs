using System.Diagnostics;
using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// Issue #63: the rover's earthworks, measured under the game's own physics (Jolt at 120 Hz). Run headless:
///   godot --headless --fixed-fps 120 --path game res://scenes/RoverDigEval.tscn
/// It prints "EVAL name value" lines; heroic/tests/rover-dig-test.rkt checks them against numbers worked out beforehand: the trench
/// and the heap a few bucketfuls make on the fine ground, their volumes, the rover refusing a 35 degree bank and climbing the
/// ramp it then builds, and what each step of the work costs a physics tick (8.33 ms). From rest on the fine ground's slope it
/// climbs 29 degrees and not 31, as on a box: its grip, tan 30°, is the limit on any ground (#198).
/// </summary>
public partial class RoverDigEval : Node3D
{
    private const double Hz = 120, Tan35 = 0.7, Tan45 = 1.0;
    private static readonly SoilSpec Regolith = new("regolith", 0, Cohesion: 0, Friction: Tan35, Density: 1500);
    private static readonly SoilSpec Cemented = new("ice-cemented-regolith", 0, Cohesion: 40000, Friction: Tan35, Density: 1500);
    private static readonly SoilSpec Rock = new("bedrock", 0, Cohesion: 5e7, Friction: 0.6, Density: 2700);

    private Node3D? _world;
    private Rover _rover = null!;
    private Terrain _terrain = null!;
    private TerrainView _view = null!;
    private IEnumerator<int>? _script;
    private int _wait, _run = -1;
    private string _name = "";
    private readonly List<(string Name, Func<IEnumerable<int>> Script)> _runs = [];
    private readonly List<double> _refreshMs = [];

    private static string F(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    private void Say(string key, string value) => GD.Print($"EVAL {_name}.{key} {value}");
    private void Say(string key, double value) => Say(key, F(value));

    public override void _Ready()
    {
        PhysicsServer3D.AreaSetParam(GetViewport().FindWorld3D().Space, PhysicsServer3D.AreaParameter.Gravity, (float)RoverSpec.EarthGravity);
        string only = OS.GetEnvironment("HEROIC_ONLY");
        if (only != "ramp" && only != "gaps")
        foreach (int deg in new[] { 30, 31, 35, 41, 43, 45 })
        {
            int d = deg;
            _runs.Add(($"slope-{d}", () => Slope(d)));
            if (d is 30 or 35) _runs.Add(($"plane-{d}", () => Plane(d)));
        }
        if (only is not ("ramp" or "gaps"))
            foreach (int deg in new[] { 29, 31 })
            {
                int d = deg;
                _runs.Add(($"rest-{d}", () => FromRest(d)));
            }
        if (only is "ramp" or "gaps") _runs.Clear();
        else
        {
            _runs.Add(("trench", Trench));
            _runs.Add(("bank", Bank));
        }
        if (only != "ramp") _runs.AddRange([("cap", Cap), ("between-wheels", BetweenWheels), ("between-nodes", BetweenNodes)]);   // #257
        if (only != "gaps") _runs.Add(("ramp", Ramp));
        NextRun();
    }

    private void NextRun()
    {
        _world?.QueueFree();
        _run++;
        if (_run >= _runs.Count) { GetTree().Quit(); return; }
        _name = _runs[_run].Name;
        _world = new Node3D();
        AddChild(_world);
        _terrain = new Terrain
        {
            Name = "dig-eval", Cell = 5, Nx = 24, Nz = 24, X0 = -60, Z0 = -60, Heights = new double[24 * 24],
            Soils = [Regolith, Rock, Cemented], Soil = Enumerable.Repeat(_name is "bank" or "ramp" ? 2 : 0, 24 * 24).ToArray(), OpenEdges = false,
        };
        _view = new TerrainView { Name = "Terrain" };
        _world.AddChild(_view);
        _view.Show(_terrain, new WorldGround(_terrain).Water, MaterialLibrary.LoadDefault());
        _rover = new Rover { Ground = _terrain, GroundHeight = _terrain.HeightAt, GroundGravity = RoverSpec.EarthGravity };
        _world.AddChild(_rover);
        _refreshMs.Clear();
        _script = _runs[_run].Script().GetEnumerator();
        _wait = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_script is null) return;
        long t = Stopwatch.GetTimestamp();
        _view.Refresh(1 / Hz);
        _refreshMs.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        if (_wait > 0) { _wait--; return; }
        if (_script.MoveNext()) _wait = _script.Current; else { _script = null; NextRun(); }
    }

    // ---- script helpers ---------------------------------------------------------------------------------------

    private static int Secs(double s) => (int)(s * Hz);

    /// <summary>Runs one dig-and-dump cycle of the arm to its end.</summary>
    private IEnumerable<int> Cycle()
    {
        _rover.StartCycle();
        yield return 1;
        for (int n = 0; _rover.ArmBusy && n < Secs(30); n++) yield return 1;
    }

    private IEnumerable<int> Settle(double seconds = 1.5) { yield return Secs(seconds); }

    private void Stand(double x, double z, double heading) => _rover.Place(x, z, heading, _terrain.HeightAt(x, z));

    private string Local(Vector3 world)
    {
        var d = world - _rover.Chassis.GlobalPosition;
        var b = _rover.Chassis.GlobalBasis;
        return $"{F(d.Dot(-b.Z))} {F(d.Dot(b.X))} {F(d.Y)}";   // ahead, right, up
    }

    // ---- runs -------------------------------------------------------------------------------------------------

    private IEnumerable<int> Probe()
    {
        Stand(0, 0, 270);
        yield return Secs(1);
        foreach (var s in Cycle()) yield return s;
        Say("dig-at-local", Local(_rover.LastDigAt));
        Say("dump-at-local", Local(_rover.LastDumpAt));
        Say("status", _rover.ArmStatus.Replace(' ', '_'));
        Say("dug", _rover.Dug); Say("dumped", _rover.Dumped);
        Say("min-height", _terrain.Worked[0].Fine.Heights.Min()); Say("max-height", _terrain.Worked[0].Fine.Heights.Max());
    }

    // where the teeth and the tipped bucket are, measured from the rover's centre by the probe: metres ahead and to the right
    private const double DigAhead = 1.9855, DigRight = 0.0022, DumpAhead = 1.0310, DumpRight = 1.2177;

    /// <summary>Where to stand, and facing, so that the teeth dig at <paramref name="d"/> and the bucket tips at <paramref name="p"/> (x, z; they must be about 1.55 m apart).</summary>
    private static (double X, double Z, double Heading) PoseFor((double X, double Z) d, (double X, double Z) p)
    {
        double best = 1e9, bestTheta = 0;
        for (double theta = 0; theta < 360; theta += 0.05)
        {
            double r = theta * Math.PI / 180, fx = -Math.Sin(r), fz = -Math.Cos(r), rx = Math.Cos(r), rz = -Math.Sin(r);
            double ex = d.X + (DumpAhead - DigAhead) * fx + (DumpRight - DigRight) * rx - p.X, ez = d.Z + (DumpAhead - DigAhead) * fz + (DumpRight - DigRight) * rz - p.Z;
            // (the vector from the teeth to the bucket, turned to this heading, against the one wanted)
            double e = ex * ex + ez * ez;
            if (e < best) (best, bestTheta) = (e, theta);
        }
        double t = bestTheta * Math.PI / 180;
        double ffx = -Math.Sin(t), ffz = -Math.Cos(t), rrx = Math.Cos(t), rrz = -Math.Sin(t);
        return (d.X - DigAhead * ffx - DigRight * rrx, d.Z - DigAhead * ffz - DigRight * rrz, bestTheta);
    }

    private const double Reach = 1.5455;   // m between the teeth and the tipped bucket

    private void Profile(string what)
    {
        _refreshMs.Sort();
        if (_refreshMs.Count == 0) return;
        Say($"{what}.refresh-median-ms", _refreshMs[_refreshMs.Count / 2]);
        Say($"{what}.refresh-p99-ms", _refreshMs[(int)(_refreshMs.Count * 0.99)]);
        Say($"{what}.refresh-max-ms", _refreshMs[^1]);
        _refreshMs.Clear();
    }

    private IEnumerable<int> Trench()
    {
        // six buckets in a row along +x, each dug where the last one stopped and tipped to the side
        for (int k = 0; k < 6; k++)
        {
            double side = k % 2 == 0 ? 1 : -1;
            (double X, double Z) d = (k * 1.0, 0), p = (d.X, d.Z + side * Reach);
            var (x, z, h) = PoseFor(d, p);
            Stand(x, z, h);
            yield return Secs(0.7);
            foreach (var s in Cycle()) yield return s;
            Say($"cycle-{k}.status", _rover.ArmStatus.Replace(' ', '_'));
        }
        var w = _terrain.Worked[0];
        Say("worked-patches", _terrain.Worked.Count);
        Say("patch-nodes", w.Fine.Heights.Length);
        Say("dug", _rover.Dug); Say("dumped", _rover.Dumped); Say("net-volume", w.Net());
        Say("deepest", -w.Fine.Heights.Min()); Say("heap-height", w.Fine.Heights.Max());
        Say("coarse-bucket-depth", 0.2 / 25);
        // the trench's width where it is deepest, and the steepest step anywhere (it stands by repose)
        double deep = w.Fine.Heights.Min(); int count = 0;
        for (int k = 0; k < w.Fine.Heights.Length; k++) if (w.Fine.Heights[k] < deep / 2) count++;
        Say("trench-nodes-below-half-depth", count);
        // the steepest step anywhere but against the rover's wheels, and there: a heap tipped beside a wheel stops at it (the wheel
        // holds it as a crate does, BodyAt, road plan 2026-10-10), so the soil can stand steeper than repose against the tyre
        double steep = 0, atWheels = 0;
        bool ByWheel(int i, int j) => _rover.Wheels.Any(wh => Math.Abs(wh.GlobalPosition.X - w.NodeX(i)) < 0.35 && Math.Abs(wh.GlobalPosition.Z - w.NodeZ(j)) < 0.35);
        for (int j = 1; j < w.Nz - 1; j++)
            for (int i = 1; i < w.Nx - 2; i++)
            {
                double g = Math.Abs(w.Fine.Heights[i + 1 + j * w.Nx] - w.Fine.Heights[i + j * w.Nx]) / w.Fine.Cell;
                if (ByWheel(i, j) || ByWheel(i + 1, j)) atWheels = Math.Max(atWheels, g);
                else steep = Math.Max(steep, g);
            }
        Say("steepest-step-grade", steep);
        Say("steepest-step-grade-at-wheels", atWheels);
        Say("rover-above-ground", _rover.Chassis.GlobalPosition.Y - _terrain.HeightAt(_rover.Chassis.GlobalPosition.X, _rover.Chassis.GlobalPosition.Z));
        Profile("trench");
    }

    /// <summary>A bank of ice-cemented soil rising toward +x at 45 degrees (it stands: a cohesive cut is good to tens of metres) to <paramref name="height"/> m, level beyond.</summary>
    private WorkedGround MakeBank(double height)
    {
        var work = _terrain.WorkAt(0, 0)!;
        work.Shape((x, z) => Math.Clamp(x * Tan45, 0, height));
        return work;
    }

    private IEnumerable<int> Drive(double fromX, string key, double seconds, double stopAtX = double.PositiveInfinity)
    {
        Stand(fromX, 0, 270);
        yield return Secs(1);
        _rover.Command = (1, 0);
        double best = fromX;
        for (int n = 0; n < Secs(seconds); n++)
        {
            best = Math.Max(best, _rover.Chassis.GlobalPosition.X);
            if (best > stopAtX) break;
            if (n % Secs(1) == 0 && OS.GetEnvironment("HEROIC_EVAL_TRACE") == "1") { var c = _rover.Chassis.GlobalPosition; GD.Print($"TRACE {key} t={n / Hz:F0} x={c.X:F2} y={c.Y:F2} ground={_terrain.HeightAt(c.X, c.Z):F2} v={_rover.Speed:F2} rescues={_rover.Rescues}"); }
            yield return 1;
        }
        _rover.Command = (0, 0);
        yield return Secs(1);
        Say($"{key}.final-x", _rover.Chassis.GlobalPosition.X);
        Say($"{key}.furthest-x", best);
        Say($"{key}.final-height", _rover.Chassis.GlobalPosition.Y);
        Say($"{key}.rescues", _rover.Rescues);
        Say($"{key}.on-top", (best > 2 && _rover.Chassis.GlobalPosition.Y > 1.0) ? 1 : 0);
    }

    private IEnumerable<int> Slope(int deg)
    {
        var work = _terrain.WorkAt(0, 0)!;
        double tan = Math.Tan(deg * Math.PI / 180);
        work.Shape((x, z) => Math.Clamp(x * tan, 0, 8));
        foreach (var s in Drive(-0.8, "climb", 12)) yield return s;
        Say("slope-deg", deg);
        Say("height-reached", _rover.Chassis.GlobalPosition.Y);
    }

    /// <summary>The same slope as one tilted box, as RoverEval does it, to compare the fine ground with.</summary>
    private IEnumerable<int> Plane(int deg)
    {
        var tilt = new Basis(Vector3.Right, Mathf.DegToRad(deg));
        var up = tilt.Y; var ahead = -tilt.Z;
        var ground = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0, PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.6f, Bounce = 0f } };
        ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, 1, 300) } });
        ground.Transform = new Transform3D(tilt, ahead * 100 - up * 0.5f);
        _world!.AddChild(ground);
        _rover.Place(0, 0, 0, 0, deg);
        yield return Secs(1);
        var start = _rover.Chassis.GlobalPosition;
        _rover.Command = (1, 0);
        yield return Secs(8);
        Say("travel", (_rover.Chassis.GlobalPosition - start).Dot(ahead));   // up the slope (+), or rolled back down it (-)
    }

    /// <summary>
    /// #198: the rover standing still on the fine ground's slope of <paramref name="deg"/> (a height map), told to drive up it: the
    /// stall from rest, with no run-up. Its grip gives tan θ = 0.577, 30 degrees.
    /// </summary>
    private IEnumerable<int> FromRest(int deg)
    {
        var work = _terrain.WorkAt(0, 0)!;
        double tan = Math.Tan(deg * Math.PI / 180);
        work.Shape((x, z) => Math.Clamp(x * tan, 0, 8));
        yield return 2;   // the patch's body is rebuilt on the next refresh
        const double x0 = 4;
        _rover.Place(x0, 0, 270, x0 * tan, deg);
        yield return Secs(0.5);
        _rover.Command = (1, 0);
        double best = x0;
        for (int n = 0; n < Secs(8); n++)
        {
            best = Math.Max(best, _rover.Chassis.GlobalPosition.X);
            if (n % Secs(1) == 0 && OS.GetEnvironment("HEROIC_EVAL_TRACE") == "1") { var c = _rover.Chassis.GlobalPosition; GD.Print($"TRACE rest-{deg} t={n / Hz:F0} x={c.X:F2} y={c.Y:F2} ground={_terrain.HeightAt(c.X, c.Z):F2} v={_rover.Speed:F2}"); }
            yield return 1;
        }
        Say("travel", _rover.Chassis.GlobalPosition.X - x0);
        Say("furthest", best - x0);
        Say("final-speed", _rover.Speed);
        Say("rescues", _rover.Rescues);
    }

    private IEnumerable<int> Bank()
    {
        MakeBank(1.0);
        foreach (var s in Drive(-0.8, "attempt", 15)) yield return s;
    }

    private IEnumerable<int> Ramp()
    {
        double height = 1.0;
        var work = MakeBank(height);
        foreach (var s in Drive(-0.8, "before", 15)) yield return s;
        // cut the bank's top and tip the spoil at its foot, along the way up: every dump lands lower than its dig
        // Fill the bank's foot from the toe up, a bucketful on each side of the path at each of four places, each cut from the bank beside the
        // path (so no trench lies across it): every dump lands lower than the dig it came from.
        var spots = new List<((double X, double Z) D, (double X, double Z) P)>();
        var plan = (OS.GetEnvironment("HEROIC_RAMP") is { Length: > 0 } env ? env : "0.45;-1.0,-0.4,0.2,0.8").Split(';');
        double side = double.Parse(plan[0], System.Globalization.CultureInfo.InvariantCulture);
        foreach (double px in plan[1].Split(',').Select(t => double.Parse(t, System.Globalization.CultureInfo.InvariantCulture)))
            foreach (double pz in new[] { -side, side })
            {
                double dz = Math.Sign(pz) * 1.55 - pz, dx = Math.Sqrt(Reach * Reach - dz * dz);
                spots.Add(((px + dx, Math.Sign(pz) * 1.55), (px, pz)));
            }
        int cycle = 0;
        foreach (var (d, p) in spots)
        {
            var (x, z, h) = PoseFor(d, p);
            Stand(x, z, h);
            Say($"cycle-{cycle}.pose", $"{x + 29.5:0.000} {z + 30:0.000} {h:0.00}");   // as rover-dig-bank.world has the bank (x + 29.5, z + 30)
            yield return Secs(0.7);
            foreach (var s in Cycle()) yield return s;
            Say($"cycle-{cycle++}.status", _rover.ArmStatus.Replace(' ', '_'));
        }
        for (double tz = -0.62; tz < 0.7; tz += 0.62)
        {
            double steep = 0;
            for (double x = -2.5; x < 1.4; x += 0.25) steep = Math.Max(steep, Math.Atan2(work.HeightAt(x + 0.5, tz) - work.HeightAt(x, tz), 0.5) * 180 / Math.PI);
            Say($"track-{tz:0.00}-steepest-deg", steep);
        }
        Say("cycles", cycle);
        Say("dug", _rover.Dug); Say("dumped", _rover.Dumped); Say("net-volume", work.Net());
        // the centre line, every half metre
        var line = new List<string>();
        for (double x = -3; x <= 2.01; x += 0.5) line.Add($"{x:0.0}:{work.HeightAt(x, 0):0.00}");
        Say("centre-line", string.Join(",", line));
        double foot = -1.5, crest = 1.0;
        Say("ramp-mean-grade-deg", Math.Atan2(work.HeightAt(crest, 0) - work.HeightAt(foot, 0), crest - foot) * 180 / Math.PI);
        double steepest = 0;
        for (double x = -2.5; x < 1.4; x += 0.25) steepest = Math.Max(steepest, Math.Atan2(work.HeightAt(x + 0.5, 0) - work.HeightAt(x, 0), 0.5) * 180 / Math.PI);
        Say("ramp-steepest-half-metre-deg", steepest);
        Say("bank-grade-deg", Math.Atan(Tan45) * 180 / Math.PI);
        Say("bank-height", height);
        if (OS.GetEnvironment("HEROIC_EVAL_TRACE") == "1")
            for (double z = -2; z <= 2.01; z += 0.25)
            {
                var row = new List<string>();
                for (double x = -2.5; x <= 2.01; x += 0.25) row.Add($"{work.HeightAt(x, z) * 100,4:0}");
                GD.Print($"MAP z={z,5:0.00} " + string.Join(" ", row));
            }
        foreach (var s in Drive(-3.5, "after", 15, stopAtX: 3.0)) yield return s;
        Profile("ramp");
    }

    // ---- #257: what BodyAt sees ---------------------------------------------------------------------------------

    /// <summary>
    /// The 32-result cap. A height map answers once per triangle, so a query that meets a lot of ground can fill its 32 places with
    /// ground and never reach the rover's own wheel. Here the column over a wheel's node is crowded with 300 more static pieces of
    /// ground (a wheel's sphere is one body among them); the old single query, cut at 32, finds no rigid body, BodyAt names the wheel.
    /// </summary>
    private IEnumerable<int> Cap()
    {
        Stand(0, 0, 270);
        yield return Secs(1.5);
        var work = _terrain.WorkAt(0, 0)!;
        var wheel = _rover.Wheels[0];
        var p = wheel.GlobalPosition;
        float ground = (float)work.HeightAt(p.X, p.Z);
        for (int n = 0; n < 300; n++)
        {
            var slab = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
            slab.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.02f, 0.02f, 0.02f) } });
            slab.Position = new Vector3(p.X + (n % 6 - 2.5f) * 0.03f, ground + 0.01f + (n / 36) * 0.01f, p.Z + (n / 6 % 6 - 2.5f) * 0.03f);
            _world!.AddChild(slab);
        }
        yield return 3;
        var node = work.Fine.Heights.Length > 0 ? (int)Math.Round((p.X - work.MinX) / WorkedGround.FineCell) + (int)Math.Round((p.Z - work.MinZ) / WorkedGround.FineCell) * work.Nx : 0;
        var r = new WorkedGround.Raised(node, work.NodeX(node % work.Nx), work.NodeZ(node / work.Nx), ground, ground + 0.1);
        // the old query, as BodyAt asked it before #257
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.25f, 0.18f, 0.25f) }, CollideWithAreas = false, CollideWithBodies = true,
            Transform = new Transform3D(Basis.Identity, new Vector3((float)r.X, ground + 0.035f, (float)r.Z)),
        };
        var hits = GetWorld3D().DirectSpaceState.IntersectShape(query, 32);
        Say("old-query-results", hits.Count);
        Say("old-query-rigid-bodies", hits.Count(h => h["collider"].AsGodotObject() is RigidBody3D));
        Say("wheel-in-column", Math.Abs(p.X - r.X) < 0.125 && Math.Abs(p.Z - r.Z) < 0.125 ? 1 : 0);
        Say("body-at", _rover.BodyAt(work, r) ?? "none");
        Say("wheel-name", wheel.Name.ToString());
    }

    /// <summary>
    /// Soil rising between the wheels. A bucketful (0.2 m3) is tipped at each of a grid of places round the rover's six wheels
    /// (0.1 m apart over 2.8 m x 2.4 m), with the soil going round the bodies as in the game (<see cref="WorkedGround.Around"/>, BodyAt);
    /// each is measured and then put back. The bump is how far the ground came up into a wheel: the highest the ground stands above a sphere's underside
    /// (over a 1 cm grid across each wheel), less what it did at rest. A wheel resting on the ground is lifted by exactly that.
    /// </summary>
    private static readonly int Buckets = int.TryParse(OS.GetEnvironment("HEROIC_BUCKETS"), out var nb) ? nb : 1;
    private static readonly double Step = double.TryParse(OS.GetEnvironment("HEROIC_STEP"), out var st) ? st : 0.1;
    private IEnumerable<int> BetweenWheels()
    {
        Stand(0, 0, 270);
        yield return Secs(1.5);
        var work = _terrain.WorkAt(0, 0)!;
        // the wheels are spheres of RoverSpec.WheelRadius: ground higher than a sphere's underside at a point is ground inside the wheel
        var wheels = _rover.Wheels.Select(w => w.GlobalPosition).ToList();
        double Inside(double[]? _ = null)
        {
            double worstIn = double.NegativeInfinity, r = RoverSpec.WheelRadius;
            foreach (var c in wheels)
                for (double x = c.X - r; x <= c.X + r + 1e-9; x += 0.01)
                    for (double z = c.Z - r; z <= c.Z + r + 1e-9; z += 0.01)
                    {
                        double d2 = (x - c.X) * (x - c.X) + (z - c.Z) * (z - c.Z);
                        if (d2 >= r * r) continue;
                        worstIn = Math.Max(worstIn, work.HeightAt(x, z) - (c.Y - Math.Sqrt(r * r - d2)));
                    }
            return worstIn;
        }
        double resting = Inside();
        Say("resting-penetration", resting);
        var (h0, l0, s0) = ((double[])work.Fine.Heights.Clone(), (bool[])work.Fine.Loose.Clone(), (int[])work.Fine.Soil.Clone());
        double worst = 0, worstX = 0, worstZ = 0; int pours = 0, refused = 0, bumped = 0;
        for (double dx = -1.4; dx <= 1.401; dx += Step)
            for (double dz = -1.2; dz <= 1.201; dz += Step)
            {
                pours++;
                for (int b = 0; b < Buckets; b++)
                    if (!work.Around(() => { if (work.Pour(dx, dz, 0.2, 0) is not null) work.Settle(RoverSpec.EarthGravity); }, r => _rover.BodyAt(work, r) is not null)) refused++;
                double bump = Inside() - resting;
                if (bump > 0.0005) bumped++;
                if (bump > worst) (worst, worstX, worstZ) = (bump, dx, dz);
                Array.Copy(h0, work.Fine.Heights, h0.Length); Array.Copy(l0, work.Fine.Loose, l0.Length); Array.Copy(s0, work.Fine.Soil, s0.Length);
                work.Fine.Touch();
            }
        Say("pours", pours); Say("refused", refused); Say("pours-with-bump-over-half-mm", bumped);
        Say("worst-bump", worst); Say("worst-bump-at", $"{F(worstX)} {F(worstZ)}");
        Say("wheel-positions", string.Join(";", _rover.Wheels.Select(w => $"{w.GlobalPosition.X:0.000},{w.GlobalPosition.Z:0.000}")));
    }

    /// <summary>
    /// Soil rising in the gap between two nodes. A column is a fine cell (0.25 m) across, centred on a node that rose, so ground that
    /// rises over the next node's own column (which did not rise and was not asked) can reach a body there: a surface is a triangle
    /// between nodes. A 6 cm cube (frozen, a loose body like a crate) stands on flat ground a quarter of a cell short of the next node:
    /// one at the axis gap (0.75 cell along), one at the corner gap (0.75, 0.75) and one 0.625 along and 0.3 across. A bucketful
    /// (0.2 m3) is tipped at each of a grid of places 5 cm apart over 1.8 m x 1.8 m round it, the soil going round the bodies as in
    /// the game; the bump is how far the ground came up into the cube (the highest the ground stands over its footprint, its
    /// underside being on ground at 0), and each tip is put back.
    /// </summary>
    private IEnumerable<int> BetweenNodes()
    {
        Stand(8, 8, 270);
        var work = _terrain.WorkAt(0, 0)!;
        int i0 = (int)Math.Round((-3 - work.MinX) / WorkedGround.FineCell), j0 = (int)Math.Round((-3 - work.MinZ) / WorkedGround.FineCell);
        var crates = new List<(string Name, double X, double Z)>();
        foreach (var (name, fx, fz, ox, oz) in new[] { ("axis", 0.75, 0.0, 0, 0), ("corner", 0.75, 0.75, 12, 0), ("oblique", 0.625, 0.3, 0, 12) })
            crates.Add((name, work.NodeX(i0 + ox) + fx * WorkedGround.FineCell, work.NodeZ(j0 + oz) + fz * WorkedGround.FineCell));
        const float Side = 0.06f;
        foreach (var c in crates)
        {
            var crate = new RigidBody3D { Name = "Crate-" + c.Name, Freeze = true, FreezeMode = RigidBody3D.FreezeModeEnum.Static, Mass = 1, CollisionLayer = 1, CollisionMask = 1 };
            crate.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Side, Side, Side) } });
            crate.Position = new Vector3((float)c.X, Side / 2, (float)c.Z);
            _world!.AddChild(crate);
        }
        yield return Secs(1);
        var (h0, l0, s0) = ((double[])work.Fine.Heights.Clone(), (bool[])work.Fine.Loose.Clone(), (int[])work.Fine.Soil.Clone());
        foreach (var (name, cx, cz) in crates)
        {
            double Rise()
            {
                double top = 0;
                for (double x = cx - Side / 2; x <= cx + Side / 2 + 1e-9; x += 0.01)
                    for (double z = cz - Side / 2; z <= cz + Side / 2 + 1e-9; z += 0.01)
                        top = Math.Max(top, work.HeightAt(x, z));
                return top;
            }
            double worst = 0, wx = 0, wz = 0; int pours = 0, bumped = 0;
            for (double dx = -0.9; dx <= 0.901; dx += 0.05)
                for (double dz = -0.9; dz <= 0.901; dz += 0.05)
                {
                    pours++;
                    for (int b = 0; b < Buckets; b++)
                        work.Around(() => { if (work.Pour(cx + dx, cz + dz, 0.2, 0) is not null) work.Settle(RoverSpec.EarthGravity); }, r => _rover.BodyAt(work, r) is not null);
                    double bump = Rise();
                    if (bump > 0.0005) bumped++;
                    if (bump > worst) (worst, wx, wz) = (bump, dx, dz);
                    Array.Copy(h0, work.Fine.Heights, h0.Length); Array.Copy(l0, work.Fine.Loose, l0.Length); Array.Copy(s0, work.Fine.Soil, s0.Length);
                    work.Fine.Touch();
                }
            Say($"{name}.pours", pours); Say($"{name}.pours-with-bump-over-half-mm", bumped);
            Say($"{name}.worst-bump", worst); Say($"{name}.worst-bump-at", $"{F(wx)} {F(wz)}");
        }
    }
}
