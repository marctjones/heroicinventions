using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// Issue #72's energy audit of the rover's dirt, under the game's own physics (Jolt at 120 Hz). Run headless:
///   godot --headless --fixed-fps 120 --path game res://scenes/DirtEnergyEval.tscn
/// It prints "EVAL name value" lines; heroic/tests/dirt-energy-test.rkt checks them. The rover may move soil anywhere (owner
/// decision 2026-10-08), but dirt must not hand energy to a body through a quirk of the model. Each run puts granite blocks (0.5 m,
/// 337.5 kg, restitution 0.6: Jolt takes the larger of two surfaces', so a kick bounces) at rest on the ground and changes the ground
/// round them, then reads each block's energy, ½ m v² + ½ I ω² + m g y (g = 9.81 m/s², Earth's, RoverSpec.EarthGravity, which this eval sets Jolt to),
/// against what it had at rest:
///  - rest: nothing changes for 100 quarter-seconds: the floor any reading has (Jolt's resting jitter);
///  - reshape: 100 digs and dumps 4 to 6 m from a block on the level and one on a 16.7 degree slope, the patch's body replaced each
///    time (TerrainView.Worked, as #188 found the map's is): what replacing the ground under a resting body gives it;
///  - patch-made: a block resting on lumpy map ground when the rover's first dig, 8 m off, lays the fine patch under it;
///  - heap-under, heap-beside, slide-under: the ground model alone (no check), soil tipped under a block, tipped 0.75 m from it so
///    the heap's slumping skirt runs under its edge, and a pit's wall dug at the rim so the slump raises the pit's floor under a block;
///  - around-dump, around-dig: the same tips and dig done by the rover's backhoe, whose soil goes round the blocks (#72, owner
///    decision 2026-10-08: nothing is refused but a bucket right over a body);
///  - undermine: the backhoe digs a bank out from under a block, which falls as gravity takes it.
/// </summary>
public partial class DirtEnergyEval : Node3D
{
    private const double Hz = 120, Tan35 = 0.7;
    private static readonly SoilSpec Regolith = new("regolith", 0, Cohesion: 0, Friction: Tan35, Density: 1500);

    private Node3D? _world;
    private Terrain _terrain = null!;
    private TerrainView _view = null!;
    private Rover? _rover;
    private IEnumerator<int>? _script;
    private int _wait, _run = -1;
    private string _name = "";
    private readonly List<(string Name, Func<IEnumerable<int>> Script)> _runs = [];
    private readonly List<Watched> _blocks = [];
    private MaterialLibrary _materials = null!;

    private sealed class Watched(string name, MaterialBlock body, double size)
    {
        public readonly string Name = name;
        public readonly MaterialBlock Body = body;
        public readonly double Size = size;
        public double Rest;            // J at rest, before the run's changes
        public double PeakKinetic;     // J, the most kinetic energy seen since the last reset
        public double PeakGain;        // J, the most total energy above rest seen since the last reset
        public double StartY;
    }

    private static string F(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    private void Say(string key, string value) => GD.Print($"EVAL {_name}.{key} {value}");
    private void Say(string key, double value) => Say(key, F(value));

    public override void _Ready()
    {
        PhysicsServer3D.AreaSetParam(GetViewport().FindWorld3D().Space, PhysicsServer3D.AreaParameter.Gravity, (float)RoverSpec.EarthGravity);
        _materials = MaterialLibrary.LoadDefault();
        string only = OS.GetEnvironment("HEROIC_ONLY");
        foreach (var (name, script) in new (string, Func<IEnumerable<int>>)[]
                 { ("rest", Rest), ("reshape", Reshape), ("patch-made", PatchMade), ("heap-under", HeapUnder), ("heap-beside", HeapBeside),
                   ("slide-under", SlideUnder), ("around-dump", AroundDump), ("around-dig", AroundDig), ("undermine", Undermine) })
            if (only.Length == 0 || only.Split(',').Contains(name)) _runs.Add((name, script));
        NextRun();
    }

    private void NextRun()
    {
        _world?.QueueFree();
        _blocks.Clear();
        _rover = null;
        _run++;
        if (_run >= _runs.Count) { GetTree().Quit(); return; }
        _name = _runs[_run].Name;
        _world = new Node3D();
        AddChild(_world);
        _terrain = new Terrain
        {
            Name = "dirt-eval", Cell = 5, Nx = 24, Nz = 24, X0 = -60, Z0 = -60, Heights = new double[24 * 24],
            Soils = [Regolith], Soil = new int[24 * 24], OpenEdges = false,
        };
        if (_name == "patch-made")
            for (int j = 0; j < 24; j++)   // gentle lumps, 4 degrees at most: a block rests on them
                for (int i = 0; i < 24; i++) _terrain.Heights[i + j * 24] = 0.3 * Math.Sin(i * 1.3) * Math.Cos(j * 0.9);
        _view = new TerrainView { Name = "Terrain" };
        _world.AddChild(_view);
        _view.Show(_terrain, new WorldGround(_terrain).Water, _materials);
        _script = _runs[_run].Script().GetEnumerator();
        _wait = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_script is null) return;
        _view.Refresh(1 / Hz);
        foreach (var b in _blocks) Watch(b);
        if (OS.GetEnvironment("HEROIC_EVAL_TRACE") == "1" && Engine.GetPhysicsFrames() % 30 == 0)
            foreach (var b in _blocks) { var (k, e, y) = Energy(b); GD.Print($"TRACE t={Engine.GetPhysicsFrames() / Hz:F2} {b.Name} y={y:F4} k={k:G3} arm={_rover?.PhaseName} {_rover?.ArmStatus}"); }
        if (_wait > 0) { _wait--; return; }
        if (_script.MoveNext()) _wait = _script.Current; else { _script = null; NextRun(); }
    }

    // ---- energy ---------------------------------------------------------------------------------------------

    private static (double Kinetic, double Total, double Y) Energy(Watched w)
    {
        var rid = w.Body.GetRid();
        var t = (Transform3D)PhysicsServer3D.BodyGetState(rid, PhysicsServer3D.BodyState.Transform);
        var v = (Vector3)PhysicsServer3D.BodyGetState(rid, PhysicsServer3D.BodyState.LinearVelocity);
        var spin = (Vector3)PhysicsServer3D.BodyGetState(rid, PhysicsServer3D.BodyState.AngularVelocity);
        double m = w.Body.Mass, inertia = m * w.Size * w.Size / 6;   // a cube's, about any axis through its centre
        double kinetic = 0.5 * m * v.LengthSquared() + 0.5 * inertia * spin.LengthSquared();
        return (kinetic, kinetic + m * RoverSpec.EarthGravity * t.Origin.Y, t.Origin.Y);
    }

    private static void Watch(Watched w)
    {
        var (k, e, _) = Energy(w);
        w.PeakKinetic = Math.Max(w.PeakKinetic, k);
        w.PeakGain = Math.Max(w.PeakGain, e - w.Rest);
    }

    private Watched Block(string name, double x, double z, double size = 0.5)
    {
        var body = new MaterialBlock(_materials["granite"], Vector3.One * (float)size, Colors.Gray) { Name = name };
        double y = _terrain.HeightAt(x, z) + size / 2 + 0.01;
        body.Position = new Vector3((float)x, (float)y, (float)z);
        _world!.AddChild(body);
        var w = new Watched(name, body, size);
        _blocks.Add(w);
        return w;
    }

    /// <summary>Notes each block's energy now as its rest, and clears the peaks.</summary>
    private void AtRest()
    {
        foreach (var b in _blocks)
        {
            var (_, e, y) = Energy(b);
            (b.Rest, b.StartY, b.PeakKinetic, b.PeakGain) = (e, y, 0, double.NegativeInfinity);
        }
    }

    private void Report(string key)
    {
        foreach (var b in _blocks)
        {
            var (k, e, y) = Energy(b);
            Say($"{key}.{b.Name}.peak-kinetic-j", b.PeakKinetic);
            Say($"{key}.{b.Name}.peak-gain-j", b.PeakGain);
            Say($"{key}.{b.Name}.gain-j", e - b.Rest);
            Say($"{key}.{b.Name}.rise-m", y - b.StartY);
            Say($"{key}.{b.Name}.kinetic-j", k);
            Say($"{key}.{b.Name}.mass-kg", b.Body.Mass);
            if (OS.GetEnvironment("HEROIC_EVAL_TRACE") == "1") GD.Print($"TRACE {key} {b.Name} y0={b.StartY} y={y} rest={b.Rest} e={e} k={k}");
        }
    }

    private static int Secs(double s) => (int)(s * Hz);

    // ---- runs -------------------------------------------------------------------------------------------------

    private WorkedGround Level()
    {
        var w = _terrain.WorkAt(0, 0)!;
        return w;
    }

    private IEnumerable<int> Rest()
    {
        Level();
        yield return 2;
        Block("flat", 2, 0);
        yield return Secs(3);
        AtRest();
        yield return Secs(25);
        Report("after-25s");
    }

    private IEnumerable<int> Reshape()
    {
        var w = Level();
        w.Shape((x, z) => Math.Clamp((x - 4) * 0.3, 0, 2.4));   // level to x = 4, then a 16.7 degree slope (0.3), level again past 12
        yield return 2;
        Block("flat", 2, 0);
        Block("slope", 7, 0);
        yield return Secs(3);
        AtRest();
        for (int n = 0; n < 100; n++)
        {
            // dig 4 m and more from either block and tip it beside the hole: the slide stays well clear of both
            double x = -2 - (n % 3) * 0.6, z = -3 + (n % 5) * 0.4;
            if (w.Scoop(x, z, 0.2) is { } s) { w.Settle(RoverSpec.EarthGravity); w.Pour(x - 1.5, z, s.Volume, s.Soil); w.Settle(RoverSpec.EarthGravity); }
            yield return Secs(0.25);
            if (n == 9) Report("after-10");
        }
        Say("cycles", 100);   // each a dig and a dump, each a new body for the patch (TerrainView.RedrawPatch)
        Report("after-100");
    }

    private IEnumerable<int> PatchMade()
    {
        var b = Block("lumpy", 1.3, 2.1);
        yield return Secs(3);
        AtRest();
        yield return Secs(2);
        Report("before");      // two seconds at rest on the map's own ground: the floor
        AtRest();
        var w = _terrain.WorkAt(8, 8)!;   // the rover's first dig, 8 m off: the patch it lays reaches under the block
        Say("patch-covers-block", w.Inside(1.3, 2.1, 0.5) ? 1 : 0);
        Say("surface-difference-m", w.HeightAt(1.3, 2.1) - _terrain.CoarseSurfaceAt(1.3, 2.1));
        yield return Secs(2);
        Report("after");
    }

    private IEnumerable<int> HeapUnder()
    {
        var w = Level();
        yield return 2;
        var b = Block("block", 0, 0);
        yield return Secs(3);
        AtRest();
        // the ground model alone, no check: a bucketful tipped under the block, three times
        for (int n = 0; n < 3; n++)
        {
            var s = w.Scoop(-4, n * 0.8, 0.2)!.Value;
            w.Pour(0, 0, s.Volume, s.Soil);
            w.Settle(RoverSpec.EarthGravity);
            yield return Secs(2);
        }
        Say("ground-rise-m", w.HeightAt(0, 0));
        Report("after-3");
    }

    private IEnumerable<int> HeapBeside()
    {
        var w = Level();
        yield return 2;
        var b = Block("block", 0.75, 0);   // its near face 0.5 m from where the bucket tips; the heap at repose reaches 0.65 m
        yield return Secs(3);
        AtRest();
        for (int n = 0; n < 3; n++)
        {
            var s = w.Scoop(-4, n * 0.8, 0.2)!.Value;
            w.Pour(0, 0, s.Volume, s.Soil);
            w.Settle(RoverSpec.EarthGravity);
            yield return Secs(2);
        }
        Say("ground-rise-under-face-m", w.HeightAt(0.55, 0));
        Report("after-3");
    }

    private IEnumerable<int> SlideUnder()
    {
        var w = Level();
        // a pit 1 m deep with a flat floor 3 m across and walls cut at 45 degrees, steeper than the loose regolith's 35: a
        // cohesionless wall stands only while nothing settles it
        w.Shape((x, z) => { double r = Math.Sqrt(x * x + z * z); return -Math.Clamp(2.5 - r, 0, 1); });
        yield return 2;
        var b = Block("block", 1.2, 0);   // on the floor, its far face 0.05 m from the wall's foot at r = 1.5
        yield return Secs(3);
        AtRest();
        // a bucket taken from the rim on the block's side: the wall slumps into the pit, its toe running across the floor
        var s = w.Scoop(2.7, 0, 0.2);
        w.Settle(RoverSpec.EarthGravity);
        Say("floor-rise-under-block-m", w.HeightAt(1.2, 0) + 1);
        yield return Secs(3);
        Report("after");
    }

    // where the teeth and the tipped bucket are, from the rover's centre (metres ahead, right): RoverDigEval's probe
    private const double DigAhead = 1.9855, DigRight = 0.0022, DumpAhead = 1.0310, DumpRight = 1.2177;

    /// <summary>A pose that tips the bucket at <paramref name="p"/> with the rover facing <paramref name="heading"/> degrees.</summary>
    private void StandToDump((double X, double Z) p, double heading)
    {
        double t = heading * Math.PI / 180, fx = -Math.Sin(t), fz = -Math.Cos(t), rx = Math.Cos(t), rz = -Math.Sin(t);
        double x = p.X - DumpAhead * fx - DumpRight * rx, z = p.Z - DumpAhead * fz - DumpRight * rz;
        _rover!.Place(x, z, heading, _terrain.HeightAt(x, z));
    }

    private IEnumerable<int> Cycle()
    {
        _rover!.StartCycle();
        yield return 1;
        for (int n = 0; _rover.ArmBusy && n < Secs(30); n++) yield return 1;
    }

    private void AddRover()
    {
        _rover = new Rover { Ground = _terrain, GroundHeight = _terrain.HeightAt, GroundGravity = RoverSpec.EarthGravity };
        _world!.AddChild(_rover);
        _rover.Place(-12, -12, 0, _terrain.HeightAt(-12, -12));   // out of the way while the blocks come to rest
    }

    private IEnumerable<int> Tip((double X, double Z) at, string key)
    {
        StandToDump(at, 270);
        yield return Secs(1);
        foreach (var s in Cycle()) yield return s;
        Say($"{key}.status", _rover!.ArmStatus.Replace(' ', '_'));
        Say($"{key}.carried", _rover.Carried);
    }

    /// <summary>The backhoe tips right over a block (it keeps its load: the one case), half over it, and beside a second one.</summary>
    private IEnumerable<int> AroundDump()
    {
        var w = Level();
        yield return 2;
        AddRover();
        Block("under", 0, 0);
        Block("beside", 4.75, 0);
        yield return Secs(3);
        AtRest();
        foreach (var s in Tip((0, 0), "over")) yield return s;                 // the whole footprint under the block: kept
        for (int n = 0; n < 3; n++)
            foreach (var s in Tip((0.3, 0), $"half-over-{n}")) yield return s;  // the open half of the footprint takes it
        for (int n = 0; n < 3; n++)
            foreach (var s in Tip((4, 0), $"beside-{n}")) yield return s;       // 0.75 m off: the skirt piles against it
        Say("dumped", _rover!.Dumped);
        Say("net-volume", w.Net() + _rover.Carried);
        Say("heap-by-under-m", w.HeightAt(0.5, 0));
        Say("heap-by-beside-m", w.HeightAt(4.4, 0));
        yield return Secs(1);
        Report("after");
    }

    /// <summary>The backhoe digs a pit's rim: the wall slumps across the floor and stops at a block's base.</summary>
    private IEnumerable<int> AroundDig()
    {
        var w = Level();
        w.Shape((x, z) => { double r = Math.Sqrt(x * x + z * z); return -Math.Clamp(2.5 - r, 0, 1); });   // SlideUnder's pit
        yield return 2;
        AddRover();
        Block("block", 1.2, 0);
        yield return Secs(3);
        AtRest();
        StandToDig((2.7, 0), 90);   // the teeth on the rim, the rover facing -x from outside the pit
        yield return Secs(1);
        foreach (var s in Cycle()) yield return s;
        Say("status", _rover!.ArmStatus.Replace(' ', '_'));
        Say("dug", _rover.Dug);
        Say("floor-beside-block-m", w.HeightAt(1.6, 0));
        Say("floor-across-pit-m", w.HeightAt(-1.6, 0));
        yield return Secs(1);
        Report("after");
    }

    /// <summary>
    /// A block on the edge of a bank 0.6 m high, the backhoe digging the bank out from under it: it falls as gravity takes it. Its
    /// energy goes down by m g Δh (less, while it still moves), never up.
    /// </summary>
    private IEnumerable<int> Undermine()
    {
        var w = Level();
        w.Shape((x, z) => x >= 0 ? 0.6 : Math.Max(0, 0.6 + x * 0.7));   // a level top, its face at repose
        yield return 2;
        AddRover();
        Block("block", 0.3, 0);   // spanning 0.05 to 0.55, its near face 5 cm from the edge
        yield return Secs(3);
        AtRest();
        for (int n = 0; n < 3; n++)
        {
            StandToDig((0.25, 0), 90);
            yield return Secs(1);
            foreach (var s in Cycle()) yield return s;
            Say($"cycle-{n}.status", _rover!.ArmStatus.Replace(' ', '_'));
            yield return Secs(2);
        }
        Say("ground-under-block-m", w.HeightAt(0.3, 0));
        Report("after");
    }

    private void StandToDig((double X, double Z) d, double heading)
    {
        double t = heading * Math.PI / 180, fx = -Math.Sin(t), fz = -Math.Cos(t), rx = Math.Cos(t), rz = -Math.Sin(t);
        double x = d.X - DigAhead * fx - DigRight * rx, z = d.Z - DigAhead * fz - DigRight * rz;
        _rover!.Place(x, z, heading, _terrain.HeightAt(x, z));
    }
}
