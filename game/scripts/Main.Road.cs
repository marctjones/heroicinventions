using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using HeroicInventions.Sim.Fluids;

namespace HeroicInventions;

/// <summary>
/// The director's road builder (docs/plans/road-plan.md): <c>build-road-to X Z [G]</c> plans a road from where the rover stands toward
/// a place it cannot reach (<see cref="RoadPlanner"/>, graded to G degrees, 20 by default) and cuts it with the backhoe, as a player
/// would with B and Shift+B: it drives to each station on the rover's real pose, swings each dig onto the road's two columns, digs
/// each column until the teeth meet the rock ("too hard") or the ground is at the road's level, and puts the spoil where the plan
/// says: tipped to the left, or carried (a dig that keeps its load), backed down the road to a haul spot, tipped there and driven
/// back. After every station it plans again from the ground as it now is, so the next station is where the cut really ends. Nothing
/// is conjured: every m³ is a bucket the arm dug, and the rover's wheels, tyres and grip are the physics' own.
/// The run is logged as <c>[road] …</c> lines; captions read <c>{road:station}</c>, <c>{road:cycles}</c>, <c>{road:dug}</c>,
/// <c>{road:hauled}</c> and <c>{road:done}</c>.
/// </summary>
public partial class Main
{
    private readonly Dictionary<string, double> _road = new() { ["station"] = 0, ["stations"] = 0, ["cycles"] = 0, ["dug"] = 0, ["hauled"] = 0, ["done"] = 0, ["rescues"] = 0 };
    private double _roadDelta;   // the frame's delta, for the sub-goals the road's steps run
    private double _roadYaw;     // the road's first stretch's yaw: the haul spots lie back along it
    private int _roadSpotBuckets = 3;   // buckets a haul spot takes (RoadLimits.HaulSpotBuckets)
    private readonly HashSet<(int, int)> _roadSkip = [];   // places (to the decimetre) the arm could not reach from the road
    private double _runShort;    // m the runs have been ending short of where they were let go for (learnt run by run)
    private (double X, double Z)? _roadRunUp;   // where a run up the steep rock starts: on the gentle ground behind the first haul spot

    /// <summary>A road reading for captions and checks: station, stations, cycles, dug (m³), hauled (buckets), done (0/1), rescues.</summary>
    private double RoadField(string name) => _road.TryGetValue(name, out double v) ? v : throw new InvalidOperationException($"the road has no field {name}");

    /// <summary>build-road-to X Z [G]: the goal that builds the road, one rendered frame at a time.</summary>
    private Func<double, bool> RoadGoal(double x, double z, double gradeDeg)
    {
        var steps = RoadSteps(x, z, gradeDeg).GetEnumerator();
        return delta =>
        {
            _roadDelta = delta;
            return !steps.MoveNext();
        };
    }

    /// <summary>Runs a director goal (drive-to, turn-to) to its end inside the road's steps.</summary>
    private IEnumerable<int> Until(Func<double, bool> goal)
    {
        while (!goal(_roadDelta)) yield return 0;
    }

    private IEnumerable<int> RoadSteps(double tx, double tz, double gradeDeg)
    {
        var rover = _rover!;
        var ground = _groundSim!.Ground;
        var limits = new RoadLimits { RunDeg = gradeDeg };
        var start = (X: (double)rover.Chassis.GlobalPosition.X, Z: (double)rover.Chassis.GlobalPosition.Z);
        int rescues = rover.Rescues, cycles = 0, hauled = 0, stationsDone = 0, idle = 0;
        double dug0 = rover.Dug;
        foreach (var k in _road.Keys.ToList()) _road[k] = 0;
        _runShort = 0;
        _roadSkip.Clear();
        _roadSpotBuckets = limits.HaulSpotBuckets;
        var crates = RoadCrates(tx, tz);
        double Floor(double fx, double fz) => ground.FloorAt(fx, fz) ?? double.NegativeInfinity;

        var plan = RoadPlanner.Plan(ground.HeightAt, Floor, crates, start, (tx, tz), limits);
        GD.Print($"[road] plan to ({tx:0.0} {tz:0.0}) at {gradeDeg:0} deg: {plan.Why}");
        if (!plan.Found) throw new InvalidOperationException($"build-road-to: {plan.Why}");
        foreach (var s in plan.Stations)
            GD.Print($"[road]   station {s.Index} at ({s.X:0.00} {s.Z:0.00}) heading {s.HeadingDeg:0}, teeth ({s.TeethX:0.00} {s.TeethZ:0.00}), cut to {s.CutTo:0.00}: {s.Volume:0.00} m3, {s.Buckets} buckets, {s.Spoil}");
        _road["stations"] = plan.Stations.Count;
        _roadYaw = plan.Segments[0].HeadingDeg;
        var pad = plan.Segments[^1];
        // the haul spots behind the cut, nearest first, and the buckets each has taken (a heap of four is about 0.8 m3, r 1 m)
        var spots = plan.Spots.ToList();
        var used = new int[spots.Count];
        foreach (var sp in spots) GD.Print($"[road]   haul spot: stand at ({sp.X:0.00} {sp.Z:0.00}), tip at ({sp.LandX:0.00} {sp.LandZ:0.00})");
        // a run up the rock starts just ahead of the nearest spot's heap (they lie behind it: no run passes one)
        _roadRunUp = spots.Count > 0 ? new RoadLine(spots[0].X, spots[0].Z, plan.Segments[0].HeadingDeg).At(limits.DumpAhead + limits.HeapReach + 0.3) : null;

        while (true)
        {
            // where the cut is not done yet: the first place along the road where a wheel's track, or its line, stands above the road's
            // level (the planner's stations say where to start; where the teeth really land, ±0.3 m, is where the next one starts)
            var station = NextCut(plan, crates, limits);
            if (station is null) break;
            if (++stationsDone > 10 * Math.Max(4, (int)_road["stations"])) throw new InvalidOperationException($"build-road-to: {stationsDone - 1} stations and the cut is not done");
            _road["station"] = stationsDone;
            int stationCycles = 0, stationHauled = 0;
            double stationDug = rover.Dug;
            bool floor = false;
            GD.Print($"[road] station {stationsDone}: to ({station.X:0.00} {station.Z:0.00}) for teeth at ({station.TeethX:0.00} {station.TeethZ:0.00}), cut to {station.CutTo:0.00}, {station.Volume:0.00} m3 left ({station.Spoil})");
            foreach (var s in Stand(station)) yield return s;

            foreach (var (cx, cz) in station.Columns)
            {
                int columnCycles = 0, misses = 0;
                while (true)
                {
                    double surface = ground.HeightAt(cx, cz);
                    if (surface <= station.CutTo + 0.05) { GD.Print($"[road]   column ({cx:0.00} {cz:0.00}): the ground is at the road's level ({surface:0.00})"); break; }
                    if (columnCycles >= 8) { GD.Print($"[road]   column ({cx:0.00} {cz:0.00}): 8 buckets, the ground still at {surface:0.00}; on to the next"); break; }
                    double swing = AimSwing(cx, cz);
                    bool keep = station.Spoil.Haul;   // carried: a part-load is topped up by the next dig, and hauled once the bucket is full
                    double before = rover.Dug;
                    foreach (var s in ArmCycle(Rover.ArmSide.Left, swing, keep)) yield return s;
                    columnCycles++; stationCycles++; cycles++;
                    _road["cycles"] = cycles;
                    _road["dug"] = rover.Dug - dug0;
                    double took = rover.Dug - before;
                    var teeth = rover.LastDigAt;
                    GD.Print($"[road]   dig {stationCycles}: swing {swing:0} deg, teeth at ({teeth.X:0.00} {teeth.Z:0.00}), {rover.ArmStatus}; ground there {ground.HeightAt(teeth.X, teeth.Z):0.00}, floor {Floor(teeth.X, teeth.Z):0.00}");
                    double miss = Math.Sqrt((teeth.X - cx) * (teeth.X - cx) + (teeth.Z - cz) * (teeth.Z - cz));
                    if (miss > 0.2 && took < 0.03)
                    {
                        // the teeth came down off the column (the rover has slid, or the arm cannot swing that far): stand again
                        if (++misses >= 2)
                        {
                            GD.Print($"[road]   the teeth missed the column by {miss:0.00} m again: the arm does not reach it from the road; left as it is");
                            _roadSkip.Add(((int)Math.Round(cx * 10), (int)Math.Round(cz * 10)));
                            break;
                        }
                        GD.Print($"[road]   the teeth missed the column by {miss:0.00} m: standing again");
                        foreach (var s in Stand(station)) yield return s;
                        continue;
                    }
                    if (rover.ArmStatus.Contains("too hard")) { floor = true; break; }
                    if (rover.ArmStatus.StartsWith("Dug nothing")) break;
                    if (keep && rover.Carried >= 0.85 * Rover.BucketVolume)
                    {
                        foreach (var s in Haul(spots, used)) yield return s;
                        hauled++; stationHauled++;
                        _road["hauled"] = hauled;
                        foreach (var s in Stand(station)) yield return s;
                    }
                    if (took < 0.03) break;   // a scrape: the column is as deep as the bucket gets it
                }
            }
            CheckRoad(rover, start, rescues);
            idle = rover.Dug - stationDug < 0.01 ? idle + 1 : 0;
            if (idle >= 3) throw new InvalidOperationException($"build-road-to: three stations running dug nothing (the rover at ({rover.Chassis.GlobalPosition.X:0.00} {rover.Chassis.GlobalPosition.Z:0.00}), {plan.Stations[0].Volume:0.00} m3 left at the first)");
            GD.Print($"[road] station {stationsDone} at ({rover.Chassis.GlobalPosition.X:0.00} {rover.Chassis.GlobalPosition.Z:0.00}): {stationCycles} cycles, "
                     + $"{(floor ? "floor reached" : "the ground at the road's level")}, dug {rover.Dug - stationDug:0.00} m3, hauled {stationHauled}; tilt {rover.TiltDeg:0}, rescues {rover.Rescues - rescues}");
            plan = RoadPlanner.Recut(plan, ground.HeightAt, Floor, crates, limits);   // the same line, the cut that is left
        }
        if (rover.Carried > 1e-9)   // the last part-load
        {
            foreach (var s in Haul(spots, used)) yield return s;
            hauled++;
            _road["hauled"] = hauled;
        }

        // onto the pad, where the road ends facing the target
        // the pad: where the teeth land on the target (the road's last stretch, held level on, takes the rover's wheels there)
        GD.Print($"[road] the cut is done: onto the pad, the teeth over ({tx:0.00} {tz:0.00})");
        var last = plan.Path[^1];
        foreach (var s in Stand(new RoadStation(0, pad.X1, pad.Z1, last.YawDeg, tx, tz, [], last.Level, 0, 0, RoadSpoil.Left(tx, tz)))) yield return s;
        CheckRoad(rover, start, rescues);
        var p = rover.Chassis.GlobalPosition;
        _road["done"] = 1;
        _road["rescues"] = rover.Rescues - rescues;
        GD.Print($"[road] done: {stationsDone} stations, {cycles} cycles, dug {rover.Dug - dug0:0.00} m3, hauled {hauled}; the rover on the pad at ({p.X:0.00} {p.Z:0.00}) tilt {rover.TiltDeg:0} deg, "
                 + $"grade along the road {GradeAlong(start.X, start.Z, p.X, p.Z):0.0} deg, rescues {rover.Rescues - rescues}; the floor along its line: "
                 + string.Join(" ", Enumerable.Range(0, 8).Select(n => { double f = n / 7.0; double x = start.X + (p.X + (tx - p.X) * 0.6 - start.X) * f, z = start.Z + (p.Z + (tz - p.Z) * 0.6 - start.Z) * f; return $"{x:0.0}:{ground.HeightAt(x, z):0.00}"; })));
    }

    /// <summary>
    /// The next place to dig, from the ground as it is: walking the road's line (and its pad) every 0.1 m, the first point where
    /// the ground under either wheel's track or the line stands more than 7 cm above the road's level there, and can be cut (not
    /// rock); the station puts the teeth there, on that track, and takes its
    /// spoil rule from the planner's station nearest it. Null when the road is cut.
    /// </summary>
    private RoadStation? NextCut(RoadPlan plan, List<(double X, double Z, double R)> crates, RoadLimits limits)
    {
        var g = _groundSim!.Ground;
        var path = plan.Path;
        var samples = new List<(double X, double Z, double Yaw, double Level)>();
        for (int k = 0; k + 1 < path.Count; k++)
        {
            var a = path[k]; var b = path[k + 1];
            double len = b.S - a.S;
            if (len < 1e-9) continue;
            for (double t = 0; t < len - 1e-9; t += 0.1)
                samples.Add((a.X + (b.X - a.X) * t / len, a.Z + (b.Z - a.Z) * t / len, b.YawDeg, t == 0 ? a.Level : b.Level));
        }
        var end = path[^1];
        var endLine = new RoadLine(end.X, end.Z, end.YawDeg);
        for (double t = 0; t <= 1.5 + 1e-9; t += 0.1)
        {
            var (x, z) = endLine.At(t);
            if (crates.Any(c => (x - c.X) * (x - c.X) + (z - c.Z) * (z - c.Z) < (c.R + limits.DigRadius) * (c.R + limits.DigRadius))) break;
            samples.Add((x, z, end.YawDeg, end.Level));
        }
        for (int n = 0; n < samples.Count; n++)
        {
            var (x, z, yaw, level) = samples[n];
            var line = new RoadLine(x, z, yaw);
            var (fx, fz) = line.F;
            foreach (double side in new[] { -RoverSpec.Track / 2, 0, RoverSpec.Track / 2 })
            {
                double px = x - fz * side, pz = z + fx * side, h = g.HeightAt(px, pz);
                if (h <= level + 0.07 || h <= (g.FloorAt(px, pz) ?? double.NegativeInfinity) + 0.05) continue;
                if (_roadSkip.Any(k => Math.Abs(k.Item1 - px * 10) <= 3 && Math.Abs(k.Item2 - pz * 10) <= 3)) continue;   // out of the arm's reach from the road
                var (tx, tz) = (x, z);
                double cx = px, cz = pz;
                var nearest = plan.Stations.OrderBy(st => (st.TeethX - tx) * (st.TeethX - tx) + (st.TeethZ - tz) * (st.TeethZ - tz)).FirstOrDefault();
                var spoil = nearest?.Spoil ?? new RoadSpoil(true, double.NaN, double.NaN, double.NaN, double.NaN, "no station planned here");
                var (cfx, cfz) = (fx, fz);
                return new RoadStation(0, tx - cfx * limits.Reach, tz - cfz * limits.Reach, yaw, tx, tz, [(cx, cz)], level, nearest?.Volume ?? 0, 1, spoil);
            }
        }
        return null;
    }

    /// <summary>The bodies a road keeps off and its spoil away from: every machine's parts as a circle round them (the rover's not among them), the target's crate included.</summary>
    private List<(double X, double Z, double R)> RoadCrates(double tx, double tz)
    {
        var crates = new List<(double X, double Z, double R)>();
        foreach (var v in _views)
        {
            if (PartsBox(v) is not { } box) continue;
            var c = box.GetCenter();
            double r = Math.Sqrt(box.Size.X * box.Size.X + box.Size.Z * box.Size.Z) / 2;
            if (r > 6) continue;   // a machine as big as a mill is not a crate in the way (its place is its own business)
            // the target's own machine (the buried bank): its crate, 0.5 m a side, at the target, not the box round all its parts (its
            // bank and cells are drawn over it): the road ends at the crate, and the dig at the target is the crate's own business
            if (Math.Abs(tx - c.X) <= box.Size.X / 2 + 0.3 && Math.Abs(tz - c.Z) <= box.Size.Z / 2 + 0.3) { crates.Add((tx, tz, 0.35)); continue; }
            crates.Add((c.X, c.Z, Math.Max(0.3, r)));
        }
        GD.Print($"[road] bodies kept clear of: {string.Join(", ", crates.Select(c => $"({c.X:0.0} {c.Z:0.0}) r {c.R:0.0}"))}");
        return crates;
    }

    /// <summary>A straight stretch of the road's line: a point of it and the yaw along it (degrees, the rover's: 270 faces +x).</summary>
    private readonly record struct RoadLine(double X, double Z, double Yaw)
    {
        public (double Fx, double Fz) F => (-Math.Sin(Yaw * Math.PI / 180), -Math.Cos(Yaw * Math.PI / 180));
        /// <summary>How far along the line a point is, and how far to its right (the rover's right facing along it).</summary>
        public (double Along, double Right) Of(double x, double z) { var (fx, fz) = F; return ((x - X) * fx + (z - Z) * fz, (x - X) * -fz + (z - Z) * fx); }
        public (double X, double Z) At(double along) { var (fx, fz) = F; return (X + fx * along, Z + fz * along); }
    }

    /// <summary>
    /// Brings the rover to stand on the road's line where its teeth land on a station's point: the teeth land shorter uphill (1.83 m
    /// on 25 degrees, 2.1 on the level), so it is placed by where the arm's own solve says they would land from here. On ground it can
    /// stop and start on (the level shelf, the gentler rock below x 248) it eases there along the line. Up the 25 to 27 degree rock
    /// it cannot: the tyres hold it with almost nothing to spare, it stalls from rest, and every stop slides it a few tenths down the
    /// fall line (0.5 m sideways for 0.8 m backed, measured), so short-and-back corrections only drift it off the line. There it
    /// backs down to the gentle ground behind the haul spots, steers onto the line there, takes one run at it, and digs from where
    /// the run ends: the dig's swing takes up a lateral miss (±35 degrees, ±0.8 m), and the cut is planned again from where it is.
    /// A station off its heading (a landing's turn) is turned to first, in place.
    /// </summary>
    private IEnumerable<int> Stand(RoadStation station)
    {
        var rover = _rover!;
        var line = new RoadLine(station.TeethX, station.TeethZ, station.HeadingDeg);
        if (Math.Abs(Mathf.RadToDeg(Mathf.AngleDifference(RoverHeading(), Mathf.DegToRad((float)station.HeadingDeg)))) > 45 && rover.TiltDeg < 10)
            foreach (var s in Until(TurnGoal(station.HeadingDeg))) yield return s;   // a landing's turn (on a slope it would slide)
        for (int round = 0; round < 3; round++)
        {
            var at = rover.Chassis.GlobalPosition;
            var (along, right) = line.Of(at.X, at.Z);
            if (Math.Abs(right) > 2.0) throw new InvalidOperationException($"build-road-to: the rover is {right:+0.00;-0.00} m off the road's line at ({at.X:0.00} {at.Z:0.00}) and cannot get back onto it");
            var dig = rover.DigPoint(0);
            double short_ = -line.Of(dig.X, dig.Z).Along;   // + : the teeth land short of the station's point (it is at 0 along the line)
            if (Math.Abs(short_) <= 0.12 && Math.Abs(right) <= 0.45) yield break;
            // never so far on that the front wheels meet the face the cut has not reached yet
            double most = MostAlong(line, along, station.CutTo);
            double want = Math.Min(along + short_, most);
            bool steep = rover.PitchDeg >= 12 && short_ > 0;
            GD.Print($"[road]   the teeth land {(short_ > 0 ? "short" : "long")} by {Math.Abs(short_):0.00} m, {right:+0.00;-0.00} m off the line (pitch {rover.PitchDeg:0}): {(steep ? "back to the gentle rock and a run at it" : "easing")}");
            if (!steep)
            {
                foreach (var s in Ease(line, want)) yield return s;
                continue;
            }
            // the run: from the gentle ground behind the haul spots, onto the line there, then up in one go
            var (runFrom, _) = _roadRunUp is { } r ? line.Of(r.X, r.Z) : (along - 3, 0);
            // (up from a haul spot, past the heap it has just tipped ahead-left of it, 1.1 m out: its toe reaches 0.45 m from the line,
            // under the left wheels, so it eases up 0.35 m to the right of the line, the uphill side)
            var (lfx, lfz) = line.F;
            var easeLine = along < runFrom - 0.5 ? line with { X = line.X - lfz * 0.35, Z = line.Z + lfx * 0.35 } : line;
            foreach (var s in Ease(easeLine, runFrom, 0.3)) yield return s;
            var p = rover.Chassis.GlobalPosition;
            GD.Print($"[road]   a run from ({p.X:0.00} {p.Z:0.00}), {line.Of(p.X, p.Z).Right:+0.00;-0.00} m off the line");
            foreach (var s in Charge(line, Math.Min(want + _runShort, most))) yield return s;
            p = rover.Chassis.GlobalPosition;
            var ended = line.Of(p.X, p.Z);
            dig = rover.DigPoint(0);
            _runShort = Math.Clamp(_runShort + 0.7 * (want - ended.Along), -0.5, 1.0);   // the runs end short, about the same each time: learnt
            GD.Print($"[road]   the run ended at ({p.X:0.00} {p.Z:0.00}), {ended.Right:+0.00;-0.00} m off the line, the teeth {-line.Of(dig.X, dig.Z).Along:+0.00;-0.00} m short");
            if (Math.Abs(ended.Right) <= 0.6 || round >= 1) yield break;   // one more run only if it is off by more than the swing takes up
        }
    }

    /// <summary>The farthest along the line, from <paramref name="from"/> on, the rover's centre may stand with its front wheels short of the uncut face (ground 0.15 m above the road's level under either wheel's track), with 0.15 m to spare.</summary>
    private double MostAlong(RoadLine line, double from, double level)
    {
        var g = _groundSim!.Ground;
        var (fx, fz) = line.F;
        double lead = RoverSpec.WheelZ[2] + RoverSpec.WheelRadius;   // the front wheels' leading edge, ahead of the centre
        for (double a = from; a < from + 25; a += 0.05)
        {
            var (x, z) = line.At(a + lead);
            foreach (double side in new[] { -RoverSpec.Track / 2, RoverSpec.Track / 2 })
                if (g.HeightAt(x - fz * side, z + fx * side) > level + 0.15) return Math.Max(from, a - 0.15);
        }
        return from + 25;
    }

    /// <summary>
    /// The turn command that steers the rover onto the line, heading along it: aimed at the line 2 m on (or, backing, 2 m back), as a
    /// driver steers. On a slope steeper than 15 degrees it aims uphill of the line, as a driver crabs across a side slope: there the
    /// rover slid 0.2 to 0.5 m down the fall line in a run up the rock, and the turn that would take it back barely bites (measured).
    /// </summary>
    private double SteerOnto(RoadLine line, bool backing)
    {
        var rover = _rover!;
        var p = rover.Chassis.GlobalPosition;
        var (_, right) = line.Of(p.X, p.Z);
        if (rover.PitchDeg is > 15 or < -15)
        {
            // the fall line's side: which way across the line the ground falls (the ground a metre either side of the rover)
            var (fx, fz) = line.F;
            var g = _groundSim!.Ground;
            double fallsRight = g.HeightAt(p.X + fz, p.Z - fx) - g.HeightAt(p.X - fz, p.Z + fx);   // left minus right: + when the ground falls to the right
            right -= Math.Sign(fallsRight) * -0.2;   // aim 0.2 m uphill of the line
        }
        double correction = Math.Clamp(Math.Atan2(right, 2.0) * 180 / Math.PI, -20, 20);   // right of the line: turn left (the yaw up) going on, right going back
        double want = line.Yaw + (backing ? -correction : correction);
        double e = Mathf.RadToDeg(Mathf.AngleDifference(RoverHeading(), Mathf.DegToRad((float)want)));   // > 0: turn left (Left raises the heading)
        return Math.Clamp(-e / 15, -1, 1);   // the turn command: +1 is Right
    }

    /// <summary>Drives on at full speed along the line, steering onto it, and lets go where it will coast to a stop <paramref name="along"/> it (about 1.8 m/s² up the 25 degree rock).</summary>
    private IEnumerable<int> Charge(RoadLine line, double along)
    {
        var rover = _rover!;
        double elapsed = 0;
        bool letGo = false;
        while (true)
        {
            elapsed += _roadDelta;
            var p = rover.Chassis.GlobalPosition;
            double ahead = along - line.Of(p.X, p.Z).Along;
            double v = Math.Max(0, rover.Speed);
            letGo |= ahead <= v * v / (2 * 1.8) + 0.05;
            _directorCommand = (letGo ? 0 : 1, v > 0.6 ? SteerOnto(line, false) : 0);   // (from rest it goes straight first: a turn takes the grip the climb needs)
            if (letGo && Math.Abs(rover.Speed) < 0.05) { _directorCommand = (0, 0); yield break; }
            if (!letGo && elapsed > 3 && v < 0.05) { _directorCommand = (0, 0); GD.Print($"[road]   the run stalled at ({p.X:0.00} {p.Z:0.00}), {ahead:0.00} m short"); yield break; }
            if (rover.TiltDeg > 33) throw new InvalidOperationException($"build-road-to: tilting {rover.TiltDeg:0} deg at ({p.X:0.00} {p.Z:0.00})");
            if (elapsed > 30) throw new InvalidOperationException("build-road-to: a run took more than 30 s");
            yield return 0;
        }
    }

    /// <summary>The turntable's swing (degrees, + left) that puts the dig's teeth on the line through (x, z) along the rover's heading: a few rounds of the arm's own solve.</summary>
    private double AimSwing(double x, double z)
    {
        var rover = _rover!;
        var f = rover.Forward;
        var right = new Vector3(-f.Z, 0, f.X);
        var b = rover.ArmBase;
        double swing = 0;
        for (int round = 0; round < 4; round++)
        {
            var p = rover.DigPoint(swing);
            double off = (x - p.X) * right.X + (z - p.Z) * right.Z;   // + : the column is to the right of where the teeth land
            double reach = Math.Max(0.5, Math.Sqrt((p.X - b.X) * (p.X - b.X) + (p.Z - b.Z) * (p.Z - b.Z)));
            swing = Math.Clamp(swing - off / reach * 180 / Math.PI, -45, 45);   // the swing is + to the left (the backhoe swings a dig 45 at most)
            if (Math.Abs(off) < 0.02) break;
        }
        return swing;
    }

    /// <summary>One backhoe cycle, run to its end with the rover held still.</summary>
    private IEnumerable<int> ArmCycle(Rover.ArmSide side, double swing, bool keep)
    {
        var rover = _rover!;
        _directorCommand = (0, 0);
        double waited = 0;
        while (Math.Abs(rover.Speed) > 0.05) { waited += _roadDelta; if (waited > 10) break; yield return 0; }
        if (!rover.StartCycle(side, swing, keep)) throw new InvalidOperationException($"build-road-to: the backhoe would not start ({rover.ArmStatus})");
        while (rover.ArmBusy) { _directorCommand = (0, 0); yield return 0; }
    }

    /// <summary>
    /// Backs down the road with the load to the farthest haul spot with room, tips it to the left there, and leaves the rover there:
    /// farthest first, so it never backs past a heap it has made, nor runs past one on its way up (the spots lie behind the run).
    /// </summary>
    private IEnumerable<int> Haul(List<RoadSpot> spots, int[] used)
    {
        var rover = _rover!;
        var ground = _groundSim!.Ground;
        for (int k = spots.Count - 1; k >= 0; k--)
        {
            if (used[k] >= _roadSpotBuckets) continue;
            var sp = spots[k];
            var line = new RoadLine(sp.X, sp.Z, _roadYaw);
            foreach (var s in Ease(line, 0, 0.3)) yield return s;   // (where it tips is not critical: it backs through the steep rock in one go, sliding as it will)
            foreach (var s in ArmCycle(Rover.ArmSide.Left, 0, false)) yield return s;
            var at = rover.LastDumpAt;
            GD.Print($"[road]   haul {used.Sum() + 1}: tipped at ({at.X:0.00} {at.Z:0.00}) from ({rover.Chassis.GlobalPosition.X:0.00} {rover.Chassis.GlobalPosition.Z:0.00}): {rover.ArmStatus}; the heap's top {ground.HeightAt(at.X, at.Z):0.00}");
            if (rover.Carried < 1e-9) { used[k]++; yield break; }
            used[k] = 99;   // kept: the heap there has reached a wheel or a body; the next spot down
        }
        throw new InvalidOperationException($"build-road-to: no haul spot took the load ({rover.ArmStatus})");
    }

    /// <summary>
    /// Eases the rover on or back along the road's line, steering onto it, until it is <paramref name="along"/> it to
    /// <paramref name="tol"/>: half a metre a second, a quarter for the last half metre. On the 25 to 27 degree rock the tyres hold
    /// the rover with little to spare (0.577 cos 27 = 0.51 of its weight against sin 27 = 0.45), so it brakes, and climbs, at about
    /// 0.6 m/s²: backing down any faster it slides on past where it stopped.
    /// </summary>
    private IEnumerable<int> Ease(RoadLine line, double along, double tol = 0.06)
    {
        var rover = _rover!;
        double elapsed = 0, best = double.MaxValue, bestAt = 0;
        while (true)
        {
            elapsed += _roadDelta;
            var p = rover.Chassis.GlobalPosition;
            double ahead = along - line.Of(p.X, p.Z).Along;   // + : the place is ahead
            if (Math.Abs(ahead) <= tol)
            {
                _directorCommand = (0, 0);
                if (Math.Abs(rover.Speed) < 0.05) yield break;
                yield return 0;
                continue;
            }
            double speed = Math.Abs(ahead) <= 0.5 ? 0.125 : Math.Abs(ahead) > 1.5 && Math.Abs(rover.PitchDeg) < 23 ? 0.5 : 0.25;   // of the top speed, 2 m/s
            _directorCommand = (Math.Sign(ahead) * speed, Math.Abs(rover.Speed) > 0.2 ? SteerOnto(line, ahead < 0) * 0.6 : 0);
            if (Math.Abs(ahead) < best - 0.02) (best, bestAt) = (Math.Abs(ahead), elapsed);
            if (elapsed - bestAt > 6) throw new InvalidOperationException($"build-road-to: stalled easing {(ahead > 0 ? "on" : "back")} at ({p.X:0.00} {p.Z:0.00}), {Math.Abs(ahead):0.00} m short");
            if (rover.TiltDeg > 33) throw new InvalidOperationException($"build-road-to: tilting {rover.TiltDeg:0} deg at ({p.X:0.00} {p.Z:0.00})");
            if (elapsed > 90) throw new InvalidOperationException("build-road-to: easing took more than 90 s");
            yield return 0;
        }
    }

    /// <summary>The steepest metre (degrees, either way) of ground along the straight line between two points, read every half metre.</summary>
    private double GradeAlong(double x0, double z0, double x1, double z1)
    {
        var g = _groundSim!.Ground;
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        if (len < GroundGrade.Run) return 0;
        double fx = (x1 - x0) / len, fz = (z1 - z0) / len, worst = 0;
        for (double d = 0; d + GroundGrade.Run <= len + 1e-9; d += GroundGrade.Step)
        {
            double rise = g.HeightAt(x0 + fx * (d + GroundGrade.Run), z0 + fz * (d + GroundGrade.Run)) - g.HeightAt(x0 + fx * d, z0 + fz * d);
            worst = Math.Max(worst, Math.Abs(Math.Atan2(rise, GroundGrade.Run) * 180 / Math.PI));
        }
        return worst;
    }

    /// <summary>The wheels' gaps above the ground (m; well below 0 is a wheel under it).</summary>
    private List<double> WheelGaps()
    {
        var height = _groundSim!.Ground;
        return _rover!.Wheels.Select(w => (double)w.GlobalPosition.Y - height.HeightAt(w.GlobalPosition.X, w.GlobalPosition.Z) - RoverSpec.WheelRadius).ToList();
    }

    /// <summary>Between stations: the rover was never stood back up, and no wheel is under the ground.</summary>
    private void CheckRoad(Rover rover, (double X, double Z) start, int rescues)
    {
        if (rover.Rescues != rescues) throw new InvalidOperationException($"build-road-to: the rover was stood back on the ground {rover.Rescues - rescues} times");
        if (WheelGaps().Min() < -0.1) throw new InvalidOperationException($"build-road-to: a wheel is {-WheelGaps().Min() * 100:0} cm under the ground");
    }
}
