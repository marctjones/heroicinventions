namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// The limits a rover's road is planned to (docs/plans/road-plan.md §1). Grades are degrees, lengths metres. The defaults are the
/// Lonely Rover's: it climbs 29.5 degrees straight from rest (#198), but a turn barely bites above ~24, so a cut road is graded to
/// <see cref="RunDeg"/> and only ground that cannot be cut (rock) may be driven steeper, straight, up to <see cref="StraightDeg"/>
/// over a metre (the run the rover's panel measures its grade over, <see cref="GroundGrade.Run"/>). The plan said 24 there; the
/// bare rock the rover already climbs to the bank in the opening is 25.2 degrees over its steepest metre (x 249.3 to 250.3), so
/// it is 27: 2.5 degrees inside what the tyres hold.
/// </summary>
public sealed record RoadLimits
{
    /// <summary>The grade a cut road climbs at most (turns still bite at 20 degrees: 90 degrees in 6 s).</summary>
    public double RunDeg { get; init; } = 20;
    /// <summary>The steepest metre of road anywhere: a hump of rock left in it, or the natural ground it is driven on, straight.</summary>
    public double StraightDeg { get; init; } = 27;
    /// <summary>The steepest a landing (where the road turns, or the rover stands to work) may be, along and across.</summary>
    public double LandingDeg { get; init; } = 5;
    /// <summary>
    /// The steepest ground the rover can stop on, and start from, at will: a haul spot is no steeper. Up the 25 to 27 degree rock
    /// below the opening's shelf its tyres hold it with almost nothing to spare (0.577 cos 27 = 0.51 of its weight against sin 27
    /// = 0.45): it slides a few tenths of a metre down the fall line at every stop and stalls from rest; at 23 it has three times the margin.
    /// </summary>
    public double StopDeg { get; init; } = 23;
    /// <summary>The steepest natural ground the road may cross without benching it, across its line.</summary>
    public double NaturalCrossDeg { get; init; } = 16;
    /// <summary>The road's width: two dig columns 0.39 m either side of its line, each 0.45 m round (the rover's track is 1.24 m, 1.36 m over the tyres).</summary>
    public double Width { get; init; } = 1.7;
    /// <summary>The search lattice's spacing.</summary>
    public double Lattice { get; init; } = 0.5;
    /// <summary>How far the teeth move on from one station to the next: two 0.45 m columns 0.39 m off the line leave no ridge under a wheel (0.62 m off it) at this spacing.</summary>
    public double Advance { get; init; } = 0.75;
    /// <summary>How far ahead of the rover's centre the teeth end a dig on level ground (they land shorter uphill: 1.83 m on 25 degrees).</summary>
    public double Reach { get; init; } = 2.1;
    /// <summary>The dig columns' offset either side of the road's line, and the ground a bucket scrapes round its teeth.</summary>
    public double ColumnOffset { get; init; } = 0.39;
    public double DigRadius { get; init; } = WorkedGround.DigRadius;
    /// <summary>Where a bucket tipped to the left lands, ahead of and to the left of the rover's centre.</summary>
    public double DumpAhead { get; init; } = 0.8;
    public double DumpSide { get; init; } = 1.4;
    /// <summary>A side-cast heap must land this far below the road's floor beside it, or it spills back in.</summary>
    public double SpillDrop { get; init; } = 0.3;
    /// <summary>A haul spot's heap, beside road that is driven but not cut (the way the rover came), must land this far below it.</summary>
    public double HaulDrop { get; init; } = 0.1;
    /// <summary>Buckets one haul spot takes (a heap of 0.6 m³, r ≈ 0.9 m).</summary>
    public int HaulSpotBuckets { get; init; } = 3;
    /// <summary>
    /// m a heap reaches from where it is tipped, and the run the rover takes up ground it cannot start on (long enough to be back on
    /// its line at speed, where a turn bites, before the steep ground: at a crawl it slides off it there): no heap may lie beside road
    /// the rover drives again (it is as wide as the road, and a wheel over a heap's flank slews it off its line: measured, 0.5 m in one
    /// run), so haul spots lie behind the run's start, and a heap is never cast beside the road it came up.
    /// </summary>
    public double HeapReach { get; init; } = 0.9;
    public double RunUp { get; init; } = 7.0;
    public double Bucket { get; init; } = 0.2;
    /// <summary>s: a dig-and-dump cycle, and a haul's drive back to its spot and up again.</summary>
    public double CycleSeconds { get; init; } = 11.6;
    public double HaulSeconds { get; init; } = 15;
    /// <summary>How far the search may stray from the box round the start and the target.</summary>
    public double Margin { get; init; } = 6;
}

/// <summary>A straight stretch of road: from (X0, Z0) to (X1, Z1), facing <see cref="HeadingDeg"/> (the rover's yaw: 0 faces -z, 270 faces +x), climbing <see cref="GradeDeg"/>; a landing at its start where it turns.</summary>
public sealed record RoadSegment(double X0, double Z0, double X1, double Z1, double HeadingDeg, double GradeDeg, double Level0, double Level1, bool TurnsAtStart);

/// <summary>Where a station's spoil goes: side-cast to the left where it stands, or carried to a haul spot (the rover stands at (X, Z) to tip it, landing at (LandX, LandZ)).</summary>
public sealed record RoadSpoil(bool Haul, double X, double Z, double LandX, double LandZ, string Why = "")
{
    public static RoadSpoil Left(double landX, double landZ) => new(false, double.NaN, double.NaN, landX, landZ);
    public override string ToString() => (Haul ? $"haul to ({LandX:0.0} {LandZ:0.0})" : $"left at ({LandX:0.0} {LandZ:0.0})") + (Why.Length > 0 ? $" ({Why})" : "");
}

/// <summary>
/// A place the rover stands to dig: its centre (X, Z) and heading, the teeth's point on the road's line, the two columns either side
/// of it, the level they are cut to, the m³ of its slab of road and the buckets that takes, and where the spoil goes.
/// </summary>
public sealed record RoadStation(int Index, double X, double Z, double HeadingDeg, double TeethX, double TeethZ,
                                 IReadOnlyList<(double X, double Z)> Columns, double CutTo, double Volume, int Buckets, RoadSpoil Spoil);

/// <summary>A point of a road's line: where, facing which way, its level there, and how far along it is (a turn is two points in one place).</summary>
public sealed record RoadPoint(double X, double Z, double YawDeg, double Level, double S);

/// <summary>Where spoil is hauled to: the rover stands at (X, Z) facing along the road and tips to the left, at (LandX, LandZ).</summary>
public sealed record RoadSpot(double X, double Z, double LandX, double LandZ);

/// <summary>A road's plan: its stretches, the stations that cut it, the buckets and seconds that takes, and why (or why not); its line, and the haul spots behind its cut.</summary>
public sealed record RoadPlan(bool Found, IReadOnlyList<RoadSegment> Segments, IReadOnlyList<RoadStation> Stations, int Buckets, double Seconds, string Why)
{
    public IReadOnlyList<RoadPoint> Path { get; init; } = [];
    public IReadOnlyList<RoadSpot> Spots { get; init; } = [];

    /// <summary>m³ the road's cut takes out in all.</summary>
    public double Volume => Stations.Sum(s => s.Volume);
}

/// <summary>
/// Plans a road for the rover to drive up to a place it could not reach (docs/plans/road-plan.md): a search over a lattice of
/// <see cref="RoadLimits.Lattice"/> m and sixteen headings (the lattice's own steps: across, diagonal and a knight's move, so a leg
/// can cross a slope at 67 degrees to its fall line, where a 32 degree slope climbs 13) from the rover to a point <see cref="RoadLimits.Reach"/> short of the target,
/// facing it. Each step along the road sets the road's level there (its design), level across its width:
/// <list type="bullet">
/// <item>where the ground is rock under its line (nothing to cut), the road is the ground as it lies;</item>
/// <item>else it climbs at most <see cref="RoadLimits.RunDeg"/> from the step before (min of the surface and that), or holds
/// level (a landing), and where rock lies under the cut within that grade of the step before, it is cut down to the rock: a bench on
/// the rock is a floor the backhoe's teeth stop at by themselves ("too hard"), so the road comes out exactly as planned;</item>
/// <item>never below the rock across its width (rock left in the road is a hump, driven over as it lies).</item>
/// </list>
/// No metre of it may climb more than <see cref="RoadLimits.StraightDeg"/>; it turns only on a landing (the rover turns in place
/// there; on a slope a turn slides it); it never passes over a crate. The cost is its length, 3 a m³ cut, 6 a m³ filled and 2 a
/// landing (with the landing's own cut), so it keeps to the ground where it can, and switches back across a slope too steep to cut
/// straight up. The road is then cut by stations <see cref="RoadLimits.Advance"/> apart, the rover facing along it; spoil is cast to the
/// left where it lands well below the road and its fall line meets neither a crate nor the road, else it is carried back to the nearest
/// haul spot behind the cut (<see cref="RoadLimits.HaulSpotBuckets"/> buckets a heap). A pure function of the ground it is given:
/// the executor (game/scripts/Main.Road.cs) plans again from the real ground after every station.
/// </summary>
public static class RoadPlanner
{
    private const double SampleStep = 0.25;   // m: across the road, along it and in a landing's square (the worked ground's fine cell)
    private const double CutCost = 3, FillCost = 6, LandingCost = 2, LandingSide = 2.4;

    private sealed class Node
    {
        public int I, J, H, Mode;   // Mode: 0 graded (or the ground as it lies), 1 held level
        public double Level, G, S, Cut, Fill;
        public int Parent = -1;
        public bool Turn;
    }

    /// <summary>Plans the road from <paramref name="start"/> toward <paramref name="target"/>. <paramref name="floorAt"/> is the lowest the backhoe can cut (minus infinity over soil all the way down; the surface itself on rock); <paramref name="crates"/> are bodies the road keeps off and spoil keeps away from (centre, radius).</summary>
    public static RoadPlan Plan(Func<double, double, double> heightAt, Func<double, double, double> floorAt,
                                IReadOnlyList<(double X, double Z, double R)> crates, (double X, double Z) start, (double X, double Z) target,
                                RoadLimits limits)
    {
        var L = limits;
        double tanRun = Math.Tan(L.RunDeg * Math.PI / 180), tanStraight = Math.Tan(L.StraightDeg * Math.PI / 180);
        double tanLanding = Math.Tan(L.LandingDeg * Math.PI / 180), tanCross = Math.Tan(L.NaturalCrossDeg * Math.PI / 180);
        double x0 = Math.Min(start.X, target.X) - L.Margin, z0 = Math.Min(start.Z, target.Z) - L.Margin;
        int ni = (int)Math.Ceiling((Math.Max(start.X, target.X) + L.Margin - x0) / L.Lattice) + 1;
        int nj = (int)Math.Ceiling((Math.Max(start.Z, target.Z) + L.Margin - z0) / L.Lattice) + 1;
        // the lattice is laid through the start
        x0 = start.X - Math.Round((start.X - x0) / L.Lattice) * L.Lattice;
        z0 = start.Z - Math.Round((start.Z - z0) / L.Lattice) * L.Lattice;
        double X(int i) => x0 + i * L.Lattice;
        double Z(int j) => z0 + j * L.Lattice;
        int si = (int)Math.Round((start.X - x0) / L.Lattice), sj = (int)Math.Round((start.Z - z0) / L.Lattice);
        if (Math.Abs(target.X - start.X) + Math.Abs(target.Z - start.Z) < 1e-9) return NotFound("the target is where the rover is");
        double bearing = Yaw(target.X - start.X, target.Z - start.Z);

        var nodes = new List<Node>();
        var best = new Dictionary<long, double>();
        var open = new PriorityQueue<int, double>();
        long Key(Node n) => (((long)n.I * nj + n.J) * Headings + n.H) * 2 + n.Mode;
        double Heuristic(Node n) => Math.Max(0, Math.Sqrt(Sq(X(n.I) - target.X) + Sq(Z(n.J) - target.Z)) - L.Reach - 0.5);
        void Push(Node n)
        {
            long k = Key(n);
            if (best.TryGetValue(k, out double g) && g <= n.G + 1e-9) return;
            best[k] = n.G;
            nodes.Add(n);
            open.Enqueue(nodes.Count - 1, n.G + Heuristic(n));
        }
        double startLevel = heightAt(start.X, start.Z);
        for (int h = 0; h < Headings; h++)   // the rover turns where it stands, if it must, before the road starts: the start is its own landing
            Push(new Node { I = si, J = sj, H = h, Mode = 0, Level = startLevel, G = Math.Abs(AngleDiff(YawOf(h), bearing)) < Facing ? 0 : LandingCost });

        // the level 1 m back along the path, for the grade over a metre; before the start, the ground the rover came up on, straight back
        (double Level, double Run) Back(int idx, double s)
        {
            int at = idx;
            while (nodes[at].Parent >= 0 && s - nodes[at].S < GroundGrade.Run - 1e-9) at = nodes[at].Parent;
            double run = s - nodes[at].S;
            if (run >= GroundGrade.Run - 1e-9 || nodes[at].Parent >= 0) return (nodes[at].Level, run);
            var (fx, fz) = Forward(YawOf(nodes[at].H));
            double behind = GroundGrade.Run - run;
            return (heightAt(start.X - fx * behind, start.Z - fz * behind), GroundGrade.Run);
        }

        int goal = -1, expanded = 0;
        string why = "";
        while (open.TryDequeue(out int idx, out _))
        {
            var n = nodes[idx];
            if (best.TryGetValue(Key(n), out double bg) && bg < n.G - 1e-9) continue;
            if (++expanded > 400_000) { why = "searched 400,000 places and found no road"; break; }
            double nx = X(n.I), nz = Z(n.J);
            double toTarget = Math.Sqrt(Sq(nx - target.X) + Sq(nz - target.Z));
            if (Math.Abs(toTarget - L.Reach) <= L.Lattice && Math.Abs(AngleDiff(YawOf(n.H), Yaw(target.X - nx, target.Z - nz))) < Facing)
            {
                goal = idx;
                break;
            }
            // turn on a landing: the last metre level enough, and the square round it cut level (rock in it no higher than the landing allows)
            {
                var (back, run) = Back(idx, n.S);
                bool level = n.Parent < 0 || run < 1e-9 || Math.Abs(n.Level - back) <= tanLanding * run + 1e-9;
                if (level)
                {
                    double? landing = LandingCut(heightAt, floorAt, nx, nz, n.Level, tanLanding);
                    if (landing is { } cut)
                        foreach (int dh in new[] { 1, Headings - 1 })   // a sixteenth of a turn at a time; the landing is paid for once
                            Push(new Node { I = n.I, J = n.J, H = (n.H + dh) % Headings, Mode = 1, Level = n.Level, S = n.S, Cut = cut, Turn = true,
                                            G = n.G + (n.Turn ? 0.25 : LandingCost + CutCost * cut), Parent = idx });
                }
            }
            var (di, dj) = Step(n.H);
            int i2 = n.I + di, j2 = n.J + dj;
            if (i2 < 0 || j2 < 0 || i2 >= ni || j2 >= nj) continue;
            double x2 = X(i2), z2 = Z(j2), len = L.Lattice * Math.Sqrt(di * di + dj * dj);
            if (crates.Any(c => Sq(x2 - c.X) + Sq(z2 - c.Z) < Sq(c.R + L.Width / 2))) continue;   // never over a crate
            var across = Across(heightAt, floorAt, x2, z2, n.H, L.Width);
            for (int mode = 0; mode < 2; mode++)
            {
                if (mode == 0 && n.Mode == 1 && !n.Turn) continue;   // held level is the run-in to a landing: it turns there, or stays level (no stairs)
                if (Design(across, n.Level, len, mode, tanRun, tanCross, L.Width) is not { } d) continue;
                double s2 = n.S + len;
                var (back, run) = Back(idx, s2);
                if (Math.Abs(d.Level - back) > tanStraight * Math.Max(run, len) + 1e-9) continue;   // no metre steeper than the rover drives
                Push(new Node { I = i2, J = j2, H = n.H, Mode = d.Natural ? 0 : mode, Level = d.Level, S = s2, Cut = d.Cut * len, Fill = d.Fill * len,
                                G = n.G + len + CutCost * d.Cut * len + FillCost * d.Fill * len, Parent = idx });
            }
        }
        if (goal < 0) return NotFound(why.Length > 0 ? why : $"no road from ({start.X:0.0} {start.Z:0.0}) to within {L.Reach:0.0} m of ({target.X:0.0} {target.Z:0.0}) climbs under {L.StraightDeg:0} degrees a metre");

        // the path, start first
        var path = new List<Node>();
        for (int at = goal; at >= 0; at = nodes[at].Parent) path.Add(nodes[at]);
        path.Reverse();
        var segments = Segments(path, X, Z);
        var line = path.Select(n => new RoadPoint(X(n.I), Z(n.J), YawOf(n.H), n.Level, n.S)).ToList();
        return Cut(line, segments, heightAt, floorAt, crates, L);
    }

    /// <summary>
    /// The stations that cut a planned road's line as the ground now is (the executor's plan after each station): the same line and
    /// levels, the cut that is left. Ground already at the road's level needs no station.
    /// </summary>
    public static RoadPlan Recut(RoadPlan plan, Func<double, double, double> heightAt, Func<double, double, double> floorAt,
                                 IReadOnlyList<(double X, double Z, double R)> crates, RoadLimits limits) =>
        plan.Found ? Cut(plan.Path, plan.Segments, heightAt, floorAt, crates, limits) : plan;

    private static RoadPlan Cut(IReadOnlyList<RoadPoint> line, IReadOnlyList<RoadSegment> segments, Func<double, double, double> heightAt,
                                Func<double, double, double> floorAt, IReadOnlyList<(double X, double Z, double R)> crates, RoadLimits L)
    {
        var stations = Stations(line, heightAt, floorAt, crates, L, out var spots, out string spoilWhy);
        int buckets = stations.Sum(s => s.Buckets);
        int hauled = stations.Where(s => s.Spoil.Haul).Sum(s => s.Buckets);
        double length = line[^1].S;
        double seconds = buckets * L.CycleSeconds + hauled * L.HaulSeconds + length / 1.0;
        string text = $"{segments.Count} stretch{(segments.Count == 1 ? "" : "es")} over {length:0.0} m ("
                      + string.Join("; ", segments.Select(g => $"{g.HeadingDeg:0} deg, {Math.Sqrt(Sq(g.X1 - g.X0) + Sq(g.Z1 - g.Z0)):0.0} m at {g.GradeDeg:0.0} deg{(g.TurnsAtStart ? " from a landing" : "")}"))
                      + $"); {stations.Count} station{(stations.Count == 1 ? "" : "s")} cut {stations.Sum(s => s.Volume):0.00} m3 in {buckets} buckets, {hauled} hauled{spoilWhy}";
        return new RoadPlan(true, segments, stations, buckets, seconds, text) { Path = line, Spots = spots };
    }

    private static RoadPlan NotFound(string why) => new(false, [], [], 0, 0, why);

    private static double Sq(double v) => v * v;

    /// <summary>The rover's yaw (degrees, 0 faces -z, 270 faces +x) that faces along (dx, dz).</summary>
    public static double Yaw(double dx, double dz) => ((Math.Atan2(-dx, -dz) * 180 / Math.PI) % 360 + 360) % 360;

    private const int Headings = 16;
    private const double Facing = 12;   // degrees: the road's last stretch faces the target this nearly (half a sixteenth, and a knight's move's 4)

    /// <summary>The lattice step for heading index h, in the rover's yaw order (forward is (−sin yaw, −cos yaw): 0 is −z, 4 is −x, 12 is +x).</summary>
    private static readonly (int Di, int Dj)[] Steps =
    [
        (0, -1), (-1, -2), (-1, -1), (-2, -1), (-1, 0), (-2, 1), (-1, 1), (-1, 2),
        (0, 1), (1, 2), (1, 1), (2, 1), (1, 0), (2, -1), (1, -1), (1, -2),
    ];

    private static (int Di, int Dj) Step(int h) => Steps[h];

    /// <summary>The yaw of heading index h: its step's own direction (a knight's move is 26.6 degrees off the axis, not 22.5).</summary>
    private static double YawOf(int h) => Yaw(Steps[h].Di, Steps[h].Dj);

    private static (double Fx, double Fz) Forward(double yawDeg) { double r = yawDeg * Math.PI / 180; return (-Math.Sin(r), -Math.Cos(r)); }

    /// <summary>The surface and the floor across the road at a point, every <see cref="SampleStep"/> from its left edge to its right.</summary>
    private static (double Surface, double Floor)[] Across(Func<double, double, double> heightAt, Func<double, double, double> floorAt, double x, double z, int h, double width)
    {
        var (fx, fz) = Forward(YawOf(h));
        double rx = -fz, rz = fx;   // the rover's right: forward turned clockwise seen from above (heading 270, forward +x: right is +z)
        int n = (int)Math.Round(width / SampleStep);
        var a = new (double, double)[n + 1];
        for (int k = 0; k <= n; k++)
        {
            double off = -width / 2 + k * SampleStep;
            a[k] = (heightAt(x + rx * off, z + rz * off), floorAt(x + rx * off, z + rz * off));
        }
        return a;
    }

    private readonly record struct Designed(double Level, double Cut, double Fill, bool Natural);

    /// <summary>The road's level at a step (see the class summary), with the m² cut and filled across it; null where it cannot be.</summary>
    private static Designed? Design((double Surface, double Floor)[] across, double prev, double len, int mode, double tanRun, double tanCross, double width)
    {
        int mid = across.Length / 2;
        var (surface, floor) = across[mid];
        if (surface <= floor + 0.05)
        {
            // rock under the line: driven as it lies, if it is not too steep across
            if (mode == 1) return null;
            double cross = Math.Abs(across[^1].Surface - across[0].Surface) / width;
            return cross > tanCross ? null : new Designed(surface, 0, 0, true);
        }
        double graded = Math.Min(surface, prev + tanRun * len);
        double level = mode == 1 ? prev : graded;
        if (mode == 0 && graded < surface - 1e-9 && !double.IsNegativeInfinity(floor) && floor >= prev - tanRun * len && floor < level) level = floor;   // a bench on the rock
        if (mode == 1 && level > surface + 0.5) return null;   // a landing on more than half a metre of fill: not a road the arm builds
        // rock in the road is ridden over as it lies: under the line it sets the level; to the side it may stand as high as the
        // natural ground's cross slope allows there, and lifts the level where it stands higher
        level = Math.Max(level, floor);
        for (int k = 0; k < across.Length; k++)
            level = Math.Max(level, across[k].Floor - Math.Abs(k - mid) * SampleStep * tanCross);
        double cut = 0, fill = 0;
        foreach (var (s, f) in across)
        {
            cut += Math.Max(0, s - Math.Max(level, f)) * SampleStep;
            fill += Math.Max(0, level - s) * SampleStep;
        }
        return new Designed(level, cut, fill, false);
    }

    /// <summary>m³ to cut a landing's square level at <paramref name="level"/>, or null if rock in it stands higher than a landing allows.</summary>
    private static double? LandingCut(Func<double, double, double> heightAt, Func<double, double, double> floorAt, double x, double z, double level, double tanLanding)
    {
        double cut = 0, half = LandingSide / 2;
        for (double u = -half; u <= half + 1e-9; u += SampleStep)
            for (double v = -half; v <= half + 1e-9; v += SampleStep)
            {
                double s = heightAt(x + u, z + v), f = floorAt(x + u, z + v);
                if (f > level + tanLanding * half) return null;
                cut += Math.Max(0, s - Math.Max(level, f)) * SampleStep * SampleStep;
            }
        return cut;
    }

    private static List<RoadSegment> Segments(List<Node> path, Func<int, double> X, Func<int, double> Z)
    {
        // a stretch runs from where the road starts, or turns, to where it next turns or ends (a turn is a node in place)
        var segments = new List<RoadSegment>();
        int from = 0;
        for (int k = 1; k <= path.Count; k++)
        {
            bool turn = k < path.Count && path[k].I == path[k - 1].I && path[k].J == path[k - 1].J;
            if (k < path.Count && !turn) continue;
            var a = path[from]; var b = path[k - 1];
            double len = b.S - a.S;
            if (len > 1e-9)
                segments.Add(new RoadSegment(X(a.I), Z(a.J), X(b.I), Z(b.J), YawOf(a.H), Math.Atan2(b.Level - a.Level, len) * 180 / Math.PI, a.Level, b.Level, from > 0));
            from = k;
        }
        return segments;
    }

    /// <summary>A point along the road and its level and cut (m² of cross-section), the road extended straight back before its start and straight on after its end.</summary>
    private readonly record struct Along(double S, double X, double Z, double Yaw, double Level, double CutArea);

    private static List<RoadStation> Stations(IReadOnlyList<RoadPoint> path, Func<double, double, double> heightAt, Func<double, double, double> floorAt,
                                              IReadOnlyList<(double X, double Z, double R)> crates, RoadLimits L, out List<RoadSpot> spotList, out string why)
    {
        why = "";
        spotList = [];
        // the road every quarter metre, its last node's level carried on as the pad the rover stands on to work (its wheels 0.75 m
        // ahead of its centre, and it may stand up to half a metre on from the road's end to reach: the pad runs on 1.5 m), but not
        // into a crate's ground (that is the dig at the target, not the road)
        var along = new List<Along>();
        void Add(double s, double x, double z, double yaw, double level)
        {
            var (fx, fz) = Forward(yaw);
            double rx = -fz, rz = fx, cut = 0;
            for (double off = -L.Width / 2; off <= L.Width / 2 + 1e-9; off += SampleStep)
            {
                double px = x + rx * off, pz = z + rz * off;
                cut += Math.Max(0, heightAt(px, pz) - Math.Max(level, floorAt(px, pz))) * SampleStep;
            }
            along.Add(new Along(s, x, z, yaw, level, cut));
        }
        for (int k = 0; k + 1 < path.Count; k++)
        {
            var a = path[k]; var b = path[k + 1];
            if (b.S - a.S < 1e-9) continue;   // a turn
            double len = b.S - a.S;
            for (double t = 0; t < len - 1e-9; t += SampleStep)
            {
                double u = t / len;
                // the level between nodes: the node ahead's (a step is cut to its design all along)
                Add(a.S + t, a.X + (b.X - a.X) * u, a.Z + (b.Z - a.Z) * u, b.YawDeg, t == 0 ? a.Level : b.Level);
            }
        }
        var end = path[^1];
        double endYaw = end.YawDeg;
        var (efx, efz) = Forward(endYaw);
        for (double t = 0; t <= 1.5 + 1e-9; t += SampleStep)
        {
            double x = end.X + efx * t, z = end.Z + efz * t;
            if (crates.Any(c => Sq(x - c.X) + Sq(z - c.Z) < Sq(c.R + L.DigRadius))) break;
            Add(end.S + t, x, z, endYaw, end.Level);
        }
        // the cut worth a station: a tenth of a metre over a third of the road's width (less is a scrape the wheels ride over)
        const double worth = 0.05;
        int first = along.FindIndex(a => a.CutArea > worth), last = along.FindLastIndex(a => a.CutArea > worth);
        var stations = new List<RoadStation>();
        if (first < 0) return stations;
        double sFirst = along[first].S, sLast = along[last].S;
        var teeth = new List<double>();
        for (double t = sFirst + L.DigRadius * 0.6; t < sLast + L.Advance / 2; t += L.Advance) teeth.Add(Math.Min(t, sLast));
        Along At(double s)
        {
            if (s <= along[0].S)
            {
                // behind the start: the ground the rover came up on, as it lies
                var a0 = along[0]; var (fx, fz) = Forward(a0.Yaw);
                double x = a0.X + fx * (s - a0.S), z = a0.Z + fz * (s - a0.S);
                return a0 with { S = s, X = x, Z = z, Level = heightAt(x, z), CutArea = 0 };
            }
            int k = along.FindLastIndex(a => a.S <= s + 1e-9);
            var a = along[k]; var (gx, gz) = Forward(a.Yaw);
            return a with { S = s, X = a.X + gx * (s - a.S), Z = a.Z + gz * (s - a.S) };
        }
        // haul spots: behind the cut (on the road where it needs no cutting, or on the way the rover came, straight back from the
        // start), every metre, nearest first, where the rover can stop and start, a heap tipped to the left lands below the road, and
        // its fall line is clear
        var spots = new List<(double X, double Z, double LandX, double LandZ, int Room)>();
        // (behind the start, and behind a run's start from there: a heap beside the road is driven past on every haul)
        double behindRun = Math.Min(sFirst - L.Reach, 0) - L.RunUp - L.DumpAhead - L.HeapReach;
        for (double s = behindRun; s > behindRun - 14 && spots.Count < 8; s -= 1.0)
        {
            var p = At(s);
            if (s >= along[0].S && along[Math.Max(0, along.FindLastIndex(a => a.S <= s + 1e-9))].CutArea > worth) continue;
            if (Math.Abs(At(s + 0.5).Level - At(s - 0.5).Level) > Math.Tan(L.StopDeg * Math.PI / 180)) continue;   // it must stop there, and start again
            var land = Land(p, L);
            double road = At(s + L.DumpAhead).Level;   // the road beside where it lands
            if (heightAt(land.X, land.Z) > road - L.HaulDrop || FallLineMeets(heightAt, land.X, land.Z, crates, along, p.S, L) is not null) continue;
            spots.Add((p.X, p.Z, land.X, land.Z, L.HaulSpotBuckets));
        }
        spotList = spots.Select(p => new RoadSpot(p.X, p.Z, p.LandX, p.LandZ)).ToList();
        int spot = 0;
        for (int n = 0; n < teeth.Count; n++)
        {
            double t = teeth[n];
            var at = At(t);
            var chassis = At(t - L.Reach);
            var (fx, fz) = Forward(at.Yaw);
            double rx = -fz, rz = fx;
            double from = n == 0 ? sFirst : (teeth[n - 1] + t) / 2, to = n == teeth.Count - 1 ? sLast + SampleStep : (t + teeth[n + 1]) / 2;
            double volume = along.Where(a => a.S >= from - 1e-9 && a.S < to - 1e-9).Sum(a => a.CutArea * SampleStep);
            int buckets = volume < 0.01 ? 0 : (int)Math.Ceiling(volume / L.Bucket - 1e-9);
            var land = Land(chassis, L);
            RoadSpoil spoil;
            double drop = At(chassis.S + L.DumpAhead).Level - heightAt(land.X, land.Z);
            string? meets = FallLineMeets(heightAt, land.X, land.Z, crates, along, t, L);
            // a heap beside road the rover drives again (behind it, and the way it came) must keep clear of its wheels
            double beside = L.DumpSide - L.HeapReach - L.Width / 2;
            if (meets is null && beside < 0.15) meets = $"a heap there would reach {(-beside + 0.15):0.00} m into the road it drives back down";
            if (drop >= L.SpillDrop && meets is null)
                spoil = RoadSpoil.Left(land.X, land.Z);
            else
            {
                string because = meets ?? $"a left heap lands {drop:0.00} m below the road: it would spill back";
                int need = buckets;
                while (spot < spots.Count && spots[spot].Room <= 0) spot++;
                if (spot >= spots.Count) { why = $"; station {n + 1} has nowhere to put its spoil"; spoil = new RoadSpoil(true, double.NaN, double.NaN, double.NaN, double.NaN, because); }
                else
                {
                    var sp = spots[spot];
                    spoil = new RoadSpoil(true, sp.X, sp.Z, sp.LandX, sp.LandZ, because);
                    // the station's buckets fill this spot, and spill on to the next (the executor takes the next one when a spot is full)
                    for (int k = spot; k < spots.Count && need > 0; k++)
                    {
                        int take = Math.Min(need, spots[k].Room);
                        spots[k] = spots[k] with { Room = spots[k].Room - take };
                        need -= take;
                    }
                }
            }
            stations.Add(new RoadStation(n + 1, chassis.X, chassis.Z, at.Yaw, at.X, at.Z,
                [(at.X - rx * L.ColumnOffset, at.Z - rz * L.ColumnOffset), (at.X + rx * L.ColumnOffset, at.Z + rz * L.ColumnOffset)],
                at.Level, volume, buckets, spoil));
        }
        return stations;
    }

    /// <summary>Where a bucket tipped to the left lands, the rover at this point of the road facing along it.</summary>
    private static (double X, double Z) Land(Along p, RoadLimits L)
    {
        var (fx, fz) = Forward(p.Yaw);
        double lx = fz, lz = -fx;   // the rover's left
        return (p.X + fx * L.DumpAhead + lx * L.DumpSide, p.Z + fz * L.DumpAhead + lz * L.DumpSide);
    }

    /// <summary>
    /// What spoil heaped at (x, z) would run down onto, or null if it runs clear: the steepest way down from it, 3 m of it (a heap of a few buckets spreads a metre; the rest is margin), passes
    /// no crate (within its radius and a metre) and does not cross the road ahead of <paramref name="behind"/> (a heap that slides
    /// onto road already cut, or still to be driven, undoes it).
    /// </summary>
    private static string? FallLineMeets(Func<double, double, double> heightAt, double x, double z, IReadOnlyList<(double X, double Z, double R)> crates,
                                      List<Along> road, double behind, RoadLimits L)
    {
        double px = x, pz = z;
        for (int k = 0; k < 12; k++)
        {
            if (crates.Any(c => Sq(px - c.X) + Sq(pz - c.Z) < Sq(c.R + 1.0))) return $"its fall line meets a crate {k * SampleStep:0.0} m down";
            if (road.Any(a => a.S >= behind - L.Advance && Sq(px - a.X) + Sq(pz - a.Z) < Sq(L.Width / 2 + 0.3))) return $"its fall line crosses the road {k * SampleStep:0.0} m down";
            double gx = heightAt(px + 0.1, pz) - heightAt(px - 0.1, pz), gz = heightAt(px, pz + 0.1) - heightAt(px, pz - 0.1);
            double g = Math.Sqrt(gx * gx + gz * gz);
            if (g < 1e-6) break;   // level: it stops here
            px -= gx / g * SampleStep; pz -= gz / g * SampleStep;
        }
        return null;
    }

    /// <summary>The signed difference b − a between two angles in degrees, in [−180, 180).</summary>
    public static double AngleDiff(double a, double b) => ((b - a) % 360 + 540) % 360 - 180;
}
