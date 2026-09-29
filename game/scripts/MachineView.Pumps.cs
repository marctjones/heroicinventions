using Godot;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// A lift pump on its staging: a see-through barrel with the bucket working
/// in it, driven through a Scotch yoke by a crank wheel over the top (so
/// the bucket rides exactly x = S/2·(1 − cos θ)), an iron suction pipe dropping
/// into the well, and a spout over the cistern that gushes on each
/// upstroke. The water in the pipe stands as high as the atmosphere can
/// push it: up into the barrel for a pump within the limit, broken off at
/// h_max over the well for one past it, with the empty pipe above drawn
/// pale. A red tick on the pipe marks h_max over the well's surface, and
/// rides down with it as the well is drawn.
/// </summary>
public partial class MachineView
{
    private const float PipeRadius = 0.035f, StagingThickness = 0.08f;

    private sealed record PumpView(LiftPump Pump, float X, float Z, float Floor, MeshInstance3D Bucket, Node3D Wheel,
                                   MeshInstance3D Yoke, MeshInstance3D Rod, MeshInstance3D PipeWater, MeshInstance3D Void,
                                   MeshInstance3D BarrelWater, MeshInstance3D LimitTick, MeshInstance3D Stream, Label3D Label);

    private readonly List<PumpView> _pumpViews = [];

    private void BuildPumps()
    {
        var water = Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.8f);
        foreach (var (id, pump) in Runtime.Pumps)
        {
            var part = Runtime.Def.Part(id)!;
            var at = V(part.At);
            var wood = Surface(part.Material);
            var iron = Surface("iron");
            float r = (float)pump.Bore / 2, s = (float)pump.StrokeLength, foot = at.Y, spout = (float)pump.Spout;
            float floor = (float)Runtime.Def.Part(pump.From.Name)!.At.Y + 0.1f;

            // staging: a deck under the barrel on two posts
            var deck = Shapes.Box(new Vector3(0.9f, StagingThickness, 0.7f), wood);
            deck.Position = new Vector3(at.X, foot - 0.12f - StagingThickness / 2, at.Z);
            AddChild(deck);
            foreach (float dz in new[] { -0.3f, 0.3f })
                AddChild(Shapes.Rod(new Vector3(at.X - 0.4f, 0, at.Z + dz), new Vector3(at.X - 0.4f, foot - 0.12f, at.Z + dz), 0.05f, wood));

            float barrelTop = spout + 0.12f;
            var barrel = Shapes.Cylinder(r + 0.02f, barrelTop - (foot - 0.1f), Shapes.Mat(new Color(0.85f, 0.92f, 0.95f), roughness: 0.1f, alpha: 0.25f));
            barrel.Position = new Vector3(at.X, (barrelTop + foot - 0.1f) / 2, at.Z);
            AddChild(barrel);
            var barrelWater = Shapes.Cylinder(r * 0.95f, 1, water);
            AddChild(barrelWater);
            AddChild(Shapes.Rod(new Vector3(at.X, spout, at.Z), new Vector3(at.X + 0.42f, spout - 0.04f, at.Z), 0.04f, Surface("iron")));

            AddChild(Shapes.Rod(new Vector3(at.X, foot - 0.1f, at.Z), new Vector3(at.X, floor, at.Z), PipeRadius, Surface("iron")));
            var pipeWater = Shapes.Cylinder(PipeRadius * 1.05f, 1, water);
            AddChild(pipeWater);
            var gap = Shapes.Cylinder(PipeRadius * 1.1f, 1, Shapes.Mat(new Color(0.95f, 0.95f, 0.9f), roughness: 1, alpha: 0.45f));
            AddChild(gap);
            var tick = Shapes.Box(new Vector3(0.3f, 0.03f, 0.03f), Shapes.Mat(new Color(0.85f, 0.1f, 0.1f)));
            AddChild(tick);

            var bucket = Shapes.Cylinder(r * 0.97f, 0.05f, Surface("hemp"));
            AddChild(bucket);
            var rod = Shapes.Cylinder(0.012f, 1, iron);
            AddChild(rod);

            // crank wheel on a gallows over the barrel; its pin drives a horizontal yoke on the rod
            var hub = new Vector3(at.X, spout + 0.45f + s / 2, at.Z + 0.08f);
            AddChild(Shapes.Rod(new Vector3(at.X + 0.4f, foot - 0.12f, at.Z + 0.12f), new Vector3(at.X + 0.4f, hub.Y + 0.1f, at.Z + 0.12f), 0.04f, wood));
            AddChild(Shapes.Rod(new Vector3(at.X + 0.4f, hub.Y, at.Z + 0.12f), hub + new Vector3(0, 0, 0.04f), 0.02f, iron));
            var wheel = new Node3D { Position = hub };
            AddChild(wheel);
            var disc = Shapes.Cylinder(s / 2 + 0.06f, 0.03f, wood);
            disc.RotationDegrees = new Vector3(90, 0, 0);
            wheel.AddChild(disc);
            var pin = Shapes.Cylinder(0.018f, 0.1f, iron);
            pin.RotationDegrees = new Vector3(90, 0, 0);
            pin.Position = new Vector3(0, -s / 2, -0.04f);
            wheel.AddChild(pin);
            var yoke = Shapes.Box(new Vector3(s + 0.12f, 0.03f, 0.03f), iron);
            AddChild(yoke);

            var stream = Shapes.Cylinder(1, 1, water);
            stream.Visible = false;
            AddChild(stream);

            var label = new Label3D
            {
                Position = new Vector3(at.X - 0.7f, foot - 0.75f, at.Z + 0.3f),
                FontSize = 24, OutlineSize = 6, PixelSize = 0.009f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            };
            AddChild(label);
            _pumpViews.Add(new PumpView(pump, at.X, at.Z, floor, bucket, wheel, yoke, rod, pipeWater, gap, barrelWater, tick, stream, label));
        }
    }

    private void DrawPumps()
    {
        foreach (var v in _pumpViews)
        {
            var p = v.Pump;
            float foot = (float)p.Barrel, s = (float)p.StrokeLength, spout = (float)p.Spout;
            float surface = (float)p.From.SurfaceElevation, limit = surface + (float)p.Limit;
            float bucketY = foot + (float)p.Bucket;
            var hub = v.Wheel.Position;
            float yokeY = hub.Y - s / 2 * Mathf.Cos((float)p.Angle);

            v.Wheel.Rotation = new Vector3(0, 0, (float)p.Angle);
            v.Bucket.Position = new Vector3(v.X, bucketY, v.Z);
            v.Yoke.Position = new Vector3(v.X, yokeY, v.Z + 0.04f);
            Span(v.Rod, v.X, v.Z, bucketY, yokeY);

            // the column: to the barrel if the atmosphere can push it there, else broken off at h_max
            float top = Mathf.Min(limit, foot);
            Span(v.PipeWater, v.X, v.Z, surface, top);
            Span(v.Void, v.X, v.Z, top, foot - 0.1f);
            v.LimitTick.Position = new Vector3(v.X + 0.12f, limit, v.Z);
            float filled = p.Primed ? Mathf.Clamp(limit - foot, 0, s) : 0;
            Span(v.BarrelWater, v.X, v.Z, foot, filled >= s ? spout : foot + filled);

            bool gushing = p.Flow > 1e-6;
            v.Stream.Visible = gushing;
            if (gushing)
            {
                float land = (float)p.To.SurfaceElevation, x = v.X + 0.44f;
                float radius = Mathf.Clamp(Mathf.Sqrt((float)p.Flow / Mathf.Pi / 1.5f), 0.01f, 0.05f);
                v.Stream.Scale = new Vector3(radius, Mathf.Max(spout - 0.04f - land, 0.01f), radius);
                v.Stream.Position = new Vector3(x, (spout - 0.04f + land) / 2, v.Z);
            }
            v.Label.Text = $"{p.Name}: lift {p.SuctionLift:F2} m, limit {p.Limit:F2} m\n" +
                           (p.Broken && p.Delivered < 1e-9 ? "column broken: nothing lifted"
                               : $"{p.Delivered * 1000:F0} L in {p.Strokes} strokes");
        }
    }

    /// <summary>Stretches a unit-high cylinder upright at (x, z) between two heights, hiding it when they meet.</summary>
    private static void Span(MeshInstance3D mesh, float x, float z, float from, float to)
    {
        mesh.Visible = to - from > 0.005f;
        if (!mesh.Visible) return;
        mesh.Scale = new Vector3(1, to - from, 1);
        mesh.Position = new Vector3(x, (from + to) / 2, z);
    }
}
