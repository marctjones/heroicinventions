using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>
/// Issue #94: the rover's body, measured under the game's own physics (Jolt at 120 Hz, under Earth's gravity 9.81, set by this eval). Run headless:
///   godot --headless --fixed-fps 120 --path game res://scenes/RoverEval.tscn
/// It prints one "EVAL name value" line per measurement; heroic/tests/rover-eval-test.rkt checks them against the numbers
/// worked out beforehand (RoverSpec): the top speed, the slope at which the tyres' grip stalls it (tan 30° = 0.577) on a box,
/// a triangle mesh and a height map alike (#198), the hold on a slope, the turn rate, the run over the crater's own height map,
/// and the backhoe's volumes.
/// </summary>
public partial class RoverEval : Node3D
{
    private sealed class Run(string name, int ticks, Action<Rover, int> drive, Action<RoverEval, Rover> setup)
    {
        public string Name = name; public int Ticks = ticks; public Action<Rover, int> Drive = drive; public Action<RoverEval, Rover> Setup = setup;
        public Func<Rover, string>? Report;
    }

    private readonly List<Run> _runs = [];
    private int _run = -1, _tick;
    private Node3D? _world;
    private Rover? _rover;
    private readonly List<Vector3> _positions = [];
    private readonly List<double> _speeds = [], _yaw = [];
    private Basis _start;
    private Terrain? _terrain;
    private double _volumeBefore;

    private static string F(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    public override void _Ready()
    {
        PhysicsServer3D.AreaSetParam(GetViewport().FindWorld3D().Space, PhysicsServer3D.AreaParameter.Gravity, (float)RoverSpec.EarthGravity);
        GD.Print($"EVAL engine {ProjectSettings.GetSetting("physics/3d/physics_engine")}");

        _runs.Add(new Run("flat", 120 * 10, (r, t) => r.Command = (1, 0), (e, r) => e.Slope(r, 0)));
        _runs.Add(new Run("idle", 240, (r, t) => r.Command = (0, 0), (e, r) => e.Slope(r, 0)));
        _runs.Add(new Run("reverse", 120 * 6, (r, t) => r.Command = (-1, 0), (e, r) => e.Slope(r, 0)));
        _runs.Add(new Run("turn", 120 * 6, (r, t) => r.Command = (0, 1), (e, r) => e.Slope(r, 0)));
        foreach (int deg in new[] { 20, 24, 28, 30, 31, 33, 36, 40 })
        {
            int d = deg;
            _runs.Add(new Run($"climb-{d}", 120 * 14, (r, t) => r.Command = (1, 0), (e, r) => e.Slope(r, d)));
            _runs.Add(new Run($"hold-{d}", 120 * 6, (r, t) => r.Command = (0, 0), (e, r) => e.Slope(r, d)));
        }
        // #198: the same slope as a box, a triangle mesh and a height map (the game's ground), from rest: the stall is the same on all
        foreach (var shape in new[] { "box", "mesh", "height" })
            foreach (double deg in new[] { 29.0, 31.0 })
            {
                string k = shape; double d = deg;
                _runs.Add(new Run($"grade-{k}-{d:0}", 120 * 10, (r, t) => r.Command = (1, 0), (e, r) => e.Ground(r, k, d)));
            }
        _runs.Add(new Run("crater", 120 * 25, (r, t) => r.Command = t < 120 * 17 ? (1, t > 120 * 8 && t < 120 * 11 ? 0.5 : 0) : (0, 0), (e, r) => e.Crater(r)));
        _runs.Add(new Run("dig", 120 * 14, (r, t) => { if (t == 60) r.StartCycle(); }, (e, r) => e.Synthetic(r, uphill: false)) { Report = r => Backhoe(r) });
        _runs.Add(new Run("dig-uphill", 120 * 14, (r, t) => { if (t == 60) r.StartCycle(); }, (e, r) => e.Synthetic(r, uphill: true)) { Report = r => Backhoe(r) });
        _runs.Add(new Run("dig-bedrock", 120 * 14, (r, t) => { if (t == 60) r.StartCycle(); }, (e, r) => e.Synthetic(r, uphill: false, bedrock: true)) { Report = r => Backhoe(r) });
        NextRun();
    }

    private string Backhoe(Rover r)
    {
        double after = _terrain!.Heights.Sum() * _terrain.Cell * _terrain.Cell + _terrain.Worked.Sum(w => w.Net());   // the fine ground the backhoe works (#63) is volume too
        return $"EVAL {_runs[_run].Name}.dug {F(r.Dug)}\nEVAL {_runs[_run].Name}.dumped {F(r.Dumped)}\nEVAL {_runs[_run].Name}.carried {F(r.Carried)}\n" +
               $"EVAL {_runs[_run].Name}.ground-volume-change {F(after - _volumeBefore)}\nEVAL {_runs[_run].Name}.cycles {r.Cycles}\nEVAL {_runs[_run].Name}.status {r.ArmStatus.Replace(' ', '_')}";
    }

    private void NextRun()
    {
        _world?.QueueFree();
        _run++;
        if (_run >= _runs.Count) { GetTree().Quit(); return; }
        _tick = 0;
        _positions.Clear(); _speeds.Clear(); _yaw.Clear();
        _terrain = null;
        _world = new Node3D();
        AddChild(_world);
        _rover = new Rover();
        _world.AddChild(_rover);
        _runs[_run].Setup(this, _rover);
        _start = _rover.Chassis.GlobalBasis;
    }

    /// <summary>A plane tilted <paramref name="deg"/> up ahead of the rover (forward is -z), which stands on it at the origin facing up it.</summary>
    private void Slope(Rover rover, double deg)
    {
        var tilt = new Basis(Vector3.Right, Mathf.DegToRad((float)deg));
        var up = tilt.Y; var ahead = -tilt.Z;
        var ground = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0, PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.6f, Bounce = 0f } };
        ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, 1, 300) } });
        ground.Transform = new Transform3D(tilt, ahead * 100 - up * 0.5f);
        _world!.AddChild(ground);
        rover.Place(0, 0, 0, 0, deg);
    }

    /// <summary>
    /// The slope of <see cref="Slope"/> as <paramref name="kind"/>: "box" (that same box), "mesh" (a ConcavePolygonShape3D of 1 m
    /// triangles) or "height" (a HeightMapShape3D of 0.25 m cells, as the worked ground's): the surface through the origin rising
    /// <paramref name="deg"/> toward -z, the rover standing on it facing up it.
    /// </summary>
    private void Ground(Rover rover, string kind, double deg)
    {
        if (kind == "box") { Slope(rover, deg); return; }
        double tan = Math.Tan(deg * Math.PI / 180), half = 40;
        var ground = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0, PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.6f, Bounce = 0f } };
        if (kind == "height")
        {
            const double cell = 0.25;
            int n = (int)Math.Round(2 * half / cell) + 1;
            var data = new float[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++) data[i + j * n] = (float)(-tan * (j - (n - 1) / 2.0) * cell);
            ground.AddChild(new CollisionShape3D
            {
                Shape = new HeightMapShape3D { MapWidth = n, MapDepth = n, MapData = data },
                Transform = new Transform3D(Basis.Identity.Scaled(new Vector3((float)cell, 1, (float)cell)), Vector3.Zero),
            });
        }
        else
        {
            var faces = new List<Vector3>();
            Vector3 P(double x, double z) => new((float)x, (float)(-tan * z), (float)z);
            for (double z = -half; z < half; z++)
                for (double x = -half; x < half; x++)
                    faces.AddRange([P(x, z), P(x + 1, z), P(x, z + 1), P(x + 1, z), P(x + 1, z + 1), P(x, z + 1)]);
            ground.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces.ToArray() } });
        }
        _world!.AddChild(ground);
        rover.Place(0, 0, 0, 0, deg);
    }

    private void Crater(Rover rover)
    {
        var terrain = Terrain.Parse(Godot.FileAccess.GetFileAsString("res://maps/victoria.map"), "victoria.map");
        var view = new TerrainView { Name = "Terrain" };
        _world!.AddChild(view);
        view.Show(terrain, new WorldGround(terrain).Water, MaterialLibrary.LoadDefault());
        rover.GroundHeight = terrain.HeightAt;
        rover.Place(190, 140, 270, terrain.HeightAt(190, 140));
        _terrain = terrain;
    }

    /// <summary>A flat 1 m-celled ground under a rover standing on a plain floor: the backhoe's arithmetic without Jolt's heights.</summary>
    private void Synthetic(Rover rover, bool uphill, bool bedrock = false)
    {
        const int n = 20;
        var soils = new List<SoilSpec> { new("regolith", 0, 0, 0.7, 1500), new("bedrock", 0, 5e7, 0.6, 2700) };
        var terrain = new Terrain
        {
            Name = "synthetic", Cell = 1, Nx = n, Nz = n, X0 = -10, Z0 = -10, Heights = new double[n * n],
            Soil = Enumerable.Repeat(bedrock ? 1 : 0, n * n).ToArray(), Soils = soils,
        };
        if (uphill)
            for (int j = 0; j < n; j++)
                for (int i = 11; i < n; i++) terrain.Heights[i + j * n] = 1;   // the ground on the rover's right (+x) is a metre higher than ahead
        _terrain = terrain;
        _volumeBefore = terrain.Heights.Sum() * terrain.Cell * terrain.Cell;
        var floor = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, 1, 100) }, Position = new Vector3(0, -0.5f, 0) });
        _world!.AddChild(floor);
        rover.Ground = terrain;
        rover.Place(0, 0, 0, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_run < 0 || _run >= _runs.Count || _rover is null) return;
        var run = _runs[_run];
        run.Drive(_rover, _tick);
        _tick++;
        _positions.Add(_rover.Chassis.GlobalPosition);
        _speeds.Add(_rover.Speed);
        _yaw.Add(_rover.YawRate);
        if (_tick < run.Ticks) return;
        Report(run);
        NextRun();
    }

    private void Report(Run run)
    {
        string n = run.Name;
        int hz = 120;
        double At(List<double> s, double t) => s[Math.Min(s.Count - 1, (int)(t * hz))];
        var ahead = _start * Vector3.Forward;
        double Along(int tick) => (_positions[tick] - _positions[0]).Dot(ahead);
        int last = _positions.Count - 1;
        GD.Print($"EVAL {n}.final-speed {F(_speeds[last])}");
        GD.Print($"EVAL {n}.distance {F(Along(last))}");
        GD.Print($"EVAL {n}.tilt-deg {F(_rover!.TiltDeg)}");
        if (n == "flat" || n == "reverse")
        {
            // distance in the last 4 s at the steady speed: the speed the game moves at
            GD.Print($"EVAL {n}.cruise {F((Along(last) - Along(last - 4 * hz)) / 4)}");
            GD.Print($"EVAL {n}.speed-at-1 {F(At(_speeds, 1))}");
            GD.Print($"EVAL {n}.stray {F((_positions[last] - _positions[0] - ahead * (float)Along(last)).Length())}");
        }
        else if (n == "turn")
            GD.Print($"EVAL {n}.yaw-rate {F(At(_yaw, 4))}\nEVAL {n}.drift {F((_positions[last] - _positions[0]).Length())}");
        else if (n.StartsWith("climb") || n.StartsWith("grade"))
            GD.Print($"EVAL {n}.cruise {F((Along(last) - Along(last - 4 * hz)) / 4)}");
        else if (n == "crater")
        {
            var end = _rover.Chassis.GlobalPosition;
            GD.Print($"EVAL crater.end {F(end.X)} {F(end.Y)} {F(end.Z)}");
            GD.Print($"EVAL crater.rescues {_rover.Rescues}");
            GD.Print($"EVAL crater.above-ground {F(end.Y - _terrain!.HeightAt(end.X, end.Z))}");
            GD.Print($"EVAL crater.cruise-8s {F((_positions[8 * hz] - _positions[4 * hz]).Length() / 4)}");
            GD.Print($"EVAL crater.yaw-deg {F(Mathf.RadToDeg(_rover.Chassis.GlobalRotation.Y))}");
            GD.Print($"EVAL crater.stopped-speed {F(_speeds[last])}");
        }
        if (run.Report is { } report) GD.Print(report(_rover));
    }
}
