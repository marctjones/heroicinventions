using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Open water you can watch move. A channel is drawn as a stone trough
/// from its tank's wall down to the next tank's wall (or away out of the
/// scene), with water standing in it at the depth the sim works out and
/// flecks of foam riding the surface at the speed it works out — so a
/// race that runs fast and shallow looks it, and one choked off drains
/// and stills. Where a channel ends above the water it runs into, it
/// spills down as a falling sheet.
///
/// An inflow — water arriving from beyond the scene — is drawn the same
/// way: a short trough coming in from outside (away from the machine's
/// middle), spilling into its tank, fed by a spring-head (see Springs).
///
/// A sluice gate stands across the head of its channel: an oak plate in
/// two grooved uprights, raised as far as its opening says, so a shut gate
/// sits on the sill and a drawn one hangs clear above the water.
/// </summary>
public partial class MachineView
{
    private readonly List<(Channel? channel, WaterSource? source, Trough trough)> _troughs = [];
    private readonly List<(SluiceGate gate, MeshInstance3D plate)> _gateViews = [];

    private sealed class Trough
    {
        public required Node3D Frame;          // x along the flow, y up, z across; origin at the start, on the bed
        public required float Length, Width;
        public required MeshInstance3D Water;
        public required MeshInstance3D Fall;
        public required float FallTop;         // world height the water leaves the end at
        public required Tank? Into;            // what it spills into, if anything in the scene
        public float? Onto;                    // or the world height of the fire or boiler it pours onto
        public required Vector3 FallAt;        // world point just inside that tank's wall
        public required Vector3 Along;         // world direction of flow, level
        public required List<(MeshInstance3D node, float lateral)> Flecks;
        public required float[] Phase;         // distance each fleck has travelled
        public double LastTime;
    }

    private void BuildChannels()
    {
        foreach (var spec in Runtime.Def.Channels)
        {
            var channel = Runtime.Channels[spec.Id];
            var fromPart = Runtime.Def.Part(spec.From.Part)!;
            PartSpec? toPart = spec.To is { } t ? Runtime.Def.Part(t.Part) : null;
            float fromHalf = Half(fromPart), farHalf = toPart is null ? 0 : Half(toPart);

            // the course: tank centre, any waypoints, then the far tank's centre (or the end point)
            var course = new List<Vector3> { new((float)fromPart.At.X, 0, (float)fromPart.At.Z) };
            foreach (var (x, z) in spec.Via ?? []) course.Add(new Vector3((float)x, 0, (float)z));
            course.Add(toPart is not null
                ? new Vector3((float)toPart.At.X, 0, (float)toPart.At.Z)
                : new Vector3((float)spec.End!.Value.X, 0, (float)spec.End.Value.Z));

            // trim the tanks' half-walls off the two ends
            var first = (course[1] - course[0]).Normalized();
            var last = (course[^1] - course[^2]).Normalized();
            course[0] += first * fromHalf;
            course[^1] -= last * farHalf;

            // elevation falls evenly with distance along the course, lip to end
            var cumulative = new List<float> { 0 };
            for (int i = 1; i < course.Count; i++) cumulative.Add(cumulative[^1] + (course[i] - course[i - 1]).Length());
            float lip = (float)channel.LipElevation, endY = (float)channel.EndElevation;
            Vector3 At(int i) => course[i] + Vector3.Up * (lip + (endY - lip) * cumulative[i] / cumulative[^1]);

            for (int i = 0; i + 1 < course.Count; i++)
            {
                var dir = (course[i + 1] - course[i]).Normalized();
                float reach = (float)channel.Width / 2;   // overlap at the bends so the walls meet
                var start = At(i) - (i > 0 ? dir * reach : Vector3.Zero);
                var end = At(i + 1) + (i + 2 < course.Count ? dir * reach : Vector3.Zero);
                bool final = i + 2 == course.Count;
                var trough = MakeTrough(start, end, (float)channel.Width, final ? channel.To : null, end + dir * 0.05f);
                if (final && spec.Onto is { } onto && Runtime.Def.Part(onto) is { } target)
                    trough.Onto = (float)(target.At.Y + target.Kind switch { "boiler" => target.Number("height"), "waterwheel" => target.Number("radius"), _ => 0.1 });
                _troughs.Add((channel, null, trough));
                if (final && spec.To is null && spec.Onto is null) BuildOutfallMarker(spec.Id, end, dir);
                if (i == 0) AddLabel(spec.Id, (start + end) / 2 + Vector3.Up * (trough.Width / 3 + 0.15f));
                if (i == 0 && channel.Gate is { } gate) BuildGate(gate, trough);
            }
        }

        foreach (var spec in Runtime.Def.Sources)
        {
            var source = Runtime.Sources[spec.Id];
            var part = Runtime.Def.Part(spec.Into)!;
            var centre = new Vector3((float)part.At.X, 0, (float)part.At.Z);
            // upstream is away from the middle of the machine
            var mid = Runtime.Def.Parts.Aggregate(Vector3.Zero, (acc, p) => acc + new Vector3((float)p.At.X, 0, (float)p.At.Z)) / Runtime.Def.Parts.Count;
            var outward = centre - mid;
            outward = outward.LengthSquared() > 1e-4f ? outward.Normalized() : Vector3.Right;
            float half = Half(part), top = (float)(part.At.Y + part.Number("height"));
            float width = Mathf.Min(half * 1.2f, 0.25f + 1.5f * Mathf.Sqrt((float)source.Rate)); // a spring's runnel, a river's breadth
            var end = centre + outward * half + Vector3.Up * (top + 0.02f);
            var start = end + outward * 0.8f + Vector3.Up * 0.03f;   // short: the spring-head sits close enough to stay in view
            var trough = MakeTrough(start, end, width, source.Into, end - outward * 0.05f);
            _troughs.Add((null, source, trough));
            BuildSpringHead(spec.Id, source, start, outward, width);
        }

        static float Half(PartSpec tank) => Mathf.Sqrt((float)tank.Number("area")) / 2;
    }

    /// <summary>The gate's plate and its two uprights, a hand's breadth down the trough from the tank's wall.</summary>
    private void BuildGate(SluiceGate gate, Trough trough)
    {
        var id = Runtime.Gates.First(g => g.Value == gate).Key;
        var part = Runtime.Def.Part(id)!;
        float w = (float)gate.Width, h = (float)gate.Height;
        const float x = 0.08f, thick = 0.05f, post = 0.08f;
        var wood = Surface(part.Material);
        foreach (float s in new[] { -1f, 1f })
        {
            var upright = Shapes.Box(new Vector3(post, 2 * h + 0.1f, post), wood);
            upright.Position = new Vector3(x, h + 0.05f, s * (w / 2 + post / 2));
            trough.Frame.AddChild(upright);
        }
        var beam = Shapes.Box(new Vector3(post, post, w + 2 * post), wood);
        beam.Position = new Vector3(x, 2 * h + 0.1f + post / 2, 0);
        trough.Frame.AddChild(beam);
        var plate = Shapes.Box(new Vector3(thick, h, w), wood);
        trough.Frame.AddChild(plate);
        _gateViews.Add((gate, plate));
        AddLabel(id, trough.Frame.Transform * new Vector3(x, 2 * h + 0.3f, 0));
    }

    private void DrawGates()
    {
        const float x = 0.08f;
        foreach (var (gate, plate) in _gateViews)
        {
            float h = (float)gate.Height;
            plate.Position = new Vector3(x, (float)gate.Opening * h + h / 2, 0);
        }
    }

    private Trough MakeTrough(Vector3 start, Vector3 end, float width, Tank? into, Vector3 fallAt)
    {
        var run = end - start;
        float length = run.Length();
        var dir = run / length;
        var side = dir.Cross(Vector3.Up).Normalized();
        var up = side.Cross(dir);
        var frame = new Node3D { Transform = new Transform3D(new Basis(dir, up, side), start) };
        AddChild(frame);

        const float wall = 0.06f;
        float height = Mathf.Max(0.1f, width / 3);
        var stone = Shapes.Mat(Shapes.Stone, roughness: 0.9f);
        var bed = Shapes.Box(new Vector3(length, wall, width + 2 * wall), stone);
        bed.Position = new Vector3(length / 2, -wall / 2, 0);
        frame.AddChild(bed);
        foreach (float s in new[] { -1f, 1f })
        {
            var w = Shapes.Box(new Vector3(length, height, wall), stone);
            w.Position = new Vector3(length / 2, height / 2, s * (width + wall) / 2);
            frame.AddChild(w);
        }

        var water = Shapes.Box(new Vector3(length, 1, width * 0.98f), Shapes.Mat(Shapes.Water, roughness: 0.15f, alpha: 0.8f));
        water.Visible = false;
        frame.AddChild(water);

        var foam = Shapes.Mat(new Color(0.9f, 0.95f, 1f), roughness: 0.4f, alpha: 0.85f);
        int count = Math.Clamp((int)(length * width * 8), 6, 60);
        var rng = new Random(start.GetHashCode());
        var flecks = new List<(MeshInstance3D, float)>();
        var phase = new float[count];
        for (int i = 0; i < count; i++)
        {
            var f = Shapes.Box(new Vector3(0.14f, 0.006f, 0.035f), foam);
            f.Visible = false;
            frame.AddChild(f);
            flecks.Add((f, (float)(rng.NextDouble() - 0.5) * width * 0.85f));
            phase[i] = (float)rng.NextDouble() * length;
        }

        var fall = Shapes.Box(new Vector3(0.05f, 1, width * 0.9f), Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.7f));
        fall.Visible = false;
        var level = (dir with { Y = 0 }).Normalized();
        fall.Basis = new Basis(level, Vector3.Up, level.Cross(Vector3.Up));
        AddChild(fall);

        return new Trough
        {
            Frame = frame, Length = length, Width = width, Water = water, Fall = fall, FallTop = end.Y,
            Into = into, FallAt = fallAt, Along = (dir with { Y = 0 }).Normalized(), Flecks = flecks, Phase = phase,
            LastTime = Runtime.Time,
        };
    }

    private void DrawChannels()
    {
        DrawGates();
        foreach (var (channel, source, t) in _troughs)
        {
            double flow, depth, velocity;
            if (channel is not null) (flow, depth, velocity) = (channel.Flow, channel.Depth, channel.Velocity);
            else
            {
                // an inflow's runnel: however deep and fast its flow runs down a gentle (1%) slope
                flow = source!.Flow;
                depth = Channel.NormalDepth(flow, t.Width, 0.01);
                velocity = depth > 0 ? flow / (t.Width * depth) : 0;
            }
            bool running = flow > 1e-6;
            float d = Mathf.Max((float)depth, 0.004f);
            t.Water.Visible = running;
            t.Water.Scale = new Vector3(1, d, 1);
            t.Water.Position = new Vector3(t.Length / 2, d / 2, 0);

            // flecks ride the surface at the water's speed, wrapping round to the start
            float moved = (float)(velocity * (Runtime.Time - t.LastTime));
            t.LastTime = Runtime.Time;
            for (int i = 0; i < t.Flecks.Count; i++)
            {
                var (node, lateral) = t.Flecks[i];
                t.Phase[i] = (t.Phase[i] + moved) % t.Length;
                node.Visible = running;
                node.Position = new Vector3(t.Phase[i], d + 0.004f, lateral);
            }

            // spilling off the end into a lower pool
            float bottom = t.Into is { } into ? (float)into.SurfaceElevation : t.Onto ?? float.NaN;
            bool falls = running && !float.IsNaN(bottom) && t.FallTop - bottom > 0.01f;
            t.Fall.Visible = falls;
            if (falls)
            {
                float top = t.FallTop + d, h = top - bottom;
                t.Fall.Scale = new Vector3(Mathf.Clamp(d, 0.02f, 0.3f) / 0.05f, h, 1);
                t.Fall.Position = new Vector3(t.FallAt.X, bottom + h / 2, t.FallAt.Z);
            }
        }
    }
}
