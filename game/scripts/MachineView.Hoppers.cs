using Godot;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// Hoppers of grain (issue #50): a glass-sided box holding a column of sand that sinks at a steady
/// rate, however deep it is, a thin plate riding on the grain to show the steady speed a weight on
/// it would drive a rope at, a thin stream from the orifice while grain runs, and a heap on the
/// floor below that grows as it is drained (as a cone at the angle of repose, 34°). An arched
/// hopper draws a red band over its orifice; an empty one shows its bare floor.
/// </summary>
public partial class MachineView
{
    private const double AngleOfRepose = 34 * Math.PI / 180;

    private sealed class HopperView
    {
        public required Hopper Hopper;
        public required Vector3 Floor;              // the middle of the hopper's floor, the orifice
        public required float Side, StartLevel;
        public required MeshInstance3D Grain, Plate, Stream, Heap, Band;
        public required Node3D Glass;               // the walls and the collar that marks the end it was built with on top; turns over (#162)
        public float Turned;                        // radians the glass has swung so far; heads for pi per turn
        public float Half;                          // half the glass's height
    }

    private readonly List<HopperView> _hopperViews = [];

    private void BuildHoppers()
    {
        foreach (var (id, hopper) in Runtime.Hoppers)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var floor = V(part.At);
            float side = Mathf.Sqrt((float)hopper.Area), start = (float)hopper.Level;
            var glass = Shapes.Glass();
            glass.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            var walls = Shapes.Box(new Vector3(side + 0.01f, start * 1.15f + 0.05f, side + 0.01f), glass);
            float half = (start * 1.15f + 0.05f) / 2;
            // the glass turns over about its middle (#162): its walls, and a collar of oak at the end it was built with on top
            var glassNode = new Node3D { Position = floor + new Vector3(0, half, 0) };
            AddChild(glassNode);
            glassNode.AddChild(walls);
            var collar = Shapes.Box(new Vector3(side + 0.04f, 0.03f, side + 0.04f), Surface("oak"));
            collar.Position = new Vector3(0, half + 0.015f, 0);
            glassNode.AddChild(collar);
            Graduate(walls, id, side / 2, side / 2, -half, start, hopper.Area, grain: true);   // cm of depth up the glass (#174); children of the walls, so they turn over with the glass
            var sand = Shapes.Mat(new Color(0.82f, 0.7f, 0.42f), roughness: 1);
            var grain = Shapes.Box(Vector3.One, sand);
            var plate = Shapes.Box(new Vector3(side * 0.94f, 0.012f, side * 0.94f), Shapes.Mat(new Color(0.45f, 0.35f, 0.2f)));
            var stream = Shapes.Box(Vector3.One, sand);
            var heap = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 1, Height = 1, RadialSegments = 20 }, MaterialOverride = sand };
            var band = Shapes.Box(new Vector3(side + 0.03f, 0.012f, side + 0.03f), Shapes.Mat(new Color(0.9f, 0.15f, 0.1f)));
            band.Position = floor + new Vector3(0, -0.006f, 0);
            foreach (var n in new Node3D[] { grain, plate, stream, heap, band }) AddChild(n);
            AddLabel(id, floor + new Vector3(0, start * 1.15f + 0.15f, 0));
            _hopperViews.Add(new HopperView { Hopper = hopper, Floor = floor, Side = side, StartLevel = start, Grain = grain, Plate = plate, Stream = stream, Heap = heap, Band = band, Glass = glassNode, Half = half });
        }
    }

    private void DrawHoppers()
    {
        foreach (var v in _hopperViews)
        {
            var h = v.Hopper;
            // a glass turned over swings through half a turn about its middle and stays upside down; the sand (swapped by the
            // sim at the turn) is drawn where the sim has it
            float goal = Mathf.Pi * h.Turns;
            v.Turned = Mathf.Abs(goal - v.Turned) < 0.01f ? goal : Mathf.MoveToward(v.Turned, goal, 0.25f);
            v.Glass.Rotation = new Vector3(0, 0, v.Turned);
            float level = (float)h.Level;
            v.Grain.Visible = level > 0.001f;
            v.Grain.Scale = new Vector3(v.Side * 0.98f, Mathf.Max(level, 0.001f), v.Side * 0.98f);
            v.Grain.Position = v.Floor + new Vector3(0, level / 2, 0);
            v.Plate.Visible = level > 0.001f;
            v.Plate.Position = v.Floor + new Vector3(0, level + 0.006f, 0);
            // the stream, from the orifice to the heap's top (or the floor)
            double heapVolume = h.Drained / h.BulkDensity;
            float radius = (float)Math.Pow(3 * heapVolume / (Math.PI * Math.Tan(AngleOfRepose)), 1.0 / 3.0), height = radius * (float)Math.Tan(AngleOfRepose);
            float ground = 0;
            float fall = Mathf.Max(0.01f, v.Floor.Y - ground - (h.Flow > 0 ? height : 0));
            v.Stream.Visible = h.Flow > 0 && v.Floor.Y > ground + height;
            float width = Mathf.Clamp((float)h.Orifice * 0.5f, 0.002f, 0.02f);
            v.Stream.Scale = new Vector3(width, fall, width);
            v.Stream.Position = new Vector3(v.Floor.X, v.Floor.Y - fall / 2, v.Floor.Z);
            v.Heap.Visible = radius > 0.003f;
            v.Heap.Scale = new Vector3(Mathf.Max(radius, 0.001f), Mathf.Max(height, 0.001f), Mathf.Max(radius, 0.001f));
            v.Heap.Position = new Vector3(v.Floor.X, ground + height / 2, v.Floor.Z);
            v.Band.Visible = h.Arched;
        }
    }
}
