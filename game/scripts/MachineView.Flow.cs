using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Water you can see moving (#171). Three rules, each drawn from a number the sim already holds:
///
/// 1. <b>Flow along a pipe.</b> A pipe is drawn with a bore (the sim has none, so every pipe is drawn
///    <see cref="PipeBore"/> in radius) and a sleeve of bright dashes rides it. The dashes travel at the speed
///    the water would: flow ÷ the bore's cross-section, signed by the flow's direction, in dashes of
///    <see cref="DashPitch"/>. The phase advances by the sim's own clock, so it pauses with the sim and runs at the
///    speed it runs. No flow, no sleeve. Above about 15 dashes a second a dash would alias, so they fade into an even
///    band (as the turn marks do). Channels already ride foam flecks at the water's own speed (Channels).
/// 2. <b>The pour where lifted water arrives.</b> A noria's buckets tip one after another near the top, so the
///    stream into what receives them comes once per bucket, sized by what a bucket holds: at rpm × buckets ÷ 60
///    a second, the same rate the sim lifts. (The sim moves lifted water continuously; the pulses are the same
///    water cut into bucketfuls.) A screw's or pump's pour is continuous, as its water is (Lifts, Pumps).
/// 3. <b>Water held in a bucket</b> is drawn at its level: each of a noria's pockets fills as it passes through
///    the river, holds its water up the rising side, and empties as it tips.
/// </summary>
public partial class MachineView
{
    /// <summary>The bore every pipe is drawn with, m of radius. The sim's pipes have a conductance, not a diameter.</summary>
    private const float PipeBore = 0.03f;
    /// <summary>Length of one dash and its gap, m.</summary>
    private const float DashPitch = 0.15f;
    /// <summary>The dashes a second where they start to blur into a band, and where the band is complete.</summary>
    private const float DashBlurFrom = 15f, DashBlurTo = 30f;

    private static readonly bool FlowReport = OS.GetEnvironment("HEROIC_FLOW_REPORT") == "1";

    private sealed class PipeFlow
    {
        public required Pipe Pipe;
        public required string Id;
        public required ShaderMaterial Material;
        public required MeshInstance3D Sleeve;
        public double Phase, Last = -1, NextReport;
    }

    private readonly List<PipeFlow> _pipeFlows = [];

    /// <summary>The dashes on one pipe: a sleeve a little wider than the pipe, its own material (it holds that pipe's phase).</summary>
    private void BuildPipeFlow(PipeSpec spec, Vector3 from, Vector3 to)
    {
        float length = (to - from).Length();
        if (length < 0.02f) return;
        var material = Skins.FlowDashes(length, DashPitch);
        var sleeve = Shapes.Rod(from, to, PipeBore * 1.12f, Shapes.Mat(Colors.White, alpha: 0.5f));
        sleeve.Mesh = new CylinderMesh { TopRadius = PipeBore * 1.12f, BottomRadius = PipeBore * 1.12f, Height = length, CapTop = false, CapBottom = false };
        sleeve.MaterialOverride = material;
        sleeve.Visible = false;
        AddChild(sleeve);
        _pipeFlows.Add(new PipeFlow { Pipe = Runtime.Pipes[spec.Id], Id = spec.Id, Material = material, Sleeve = sleeve });
    }

    private void DrawPipeFlow()
    {
        const double area = Math.PI * PipeBore * PipeBore;
        foreach (var f in _pipeFlows)
        {
            double now = Runtime.Time, dt = f.Last < 0 ? 0 : now - f.Last;
            f.Last = now;
            if (dt < 0 || dt > 1) dt = 0;   // a restart or a jump in the clock
            double q = f.Pipe.Flow;                 // m³/s, positive from the pipe's first port to its second
            bool moving = Math.Abs(q) > 1e-7;
            f.Sleeve.Visible = moving;
            if (!moving) continue;
            double speed = q / area;                // m/s
            double dashes = speed / DashPitch;      // dashes a second, signed
            f.Phase = ((f.Phase + dashes * dt) % 1 + 1) % 1;
            f.Material.SetShaderParameter("phase", (float)f.Phase);
            f.Material.SetShaderParameter("blur", Mathf.Clamp((Mathf.Abs((float)dashes) - DashBlurFrom) / (DashBlurTo - DashBlurFrom), 0, 1));
            if (FlowReport && now >= f.NextReport)
            {
                f.NextReport = Math.Floor(now / 5) * 5 + 5;
                GD.Print($"[flow] t={now:F2} pipe {f.Id}: {q * 1000:F3} L/s through a {PipeBore * 2000:F0} mm bore = {speed:F3} m/s = {dashes:F2} dashes/s (pitch {DashPitch * 100:F0} cm)");
            }
        }
    }

    // ---- a noria: water in its buckets, and the pour once per bucket ---------------------------------------------

    private sealed class NoriaView
    {
        public required LiftDrive Drive;
        public required int Buckets;
        public required float Radius, Reach;     // wheel radius; the radius a bucket's mouth sits at
        public required float Back, Deep, Wide, Y0;  // a bucket's pocket: its length from the back wall, its depth along the radius, its width across, where the back wall's inside face is
        public required float[] Slot;            // each bucket's angle on the wheel
        public required MeshInstance3D[] Water;
        public required Vector3 Ref1, Ref2;      // the plane the wheel turns in, counter-clockwise about its axle
        public bool Pouring;
        public int Pours;
        public double First = -1, NextDump;
    }

    private readonly Dictionary<LiftDrive, NoriaView> _norias = [];

    /// <summary>Buckets' water for every lift worked by a noria (a wheel with buckets built into its rim).</summary>
    private void BuildNorias()
    {
        foreach (var d in _liftDrives)
        {
            if (d.By.Kind != "wheel" || d.By.Number("buckets", 0) < 1 || !d.By.Props.ContainsKey("bucket-depth")) continue;
            _building = d.By.Id;
            int n = (int)d.By.Number("buckets");
            float r = (float)d.By.Number("radius"), w = (float)d.By.Number("width"), bd = (float)d.By.Number("bucket-depth"), len = (float)d.By.Number("bucket-length");
            float plate = Mathf.Min(0.012f * r, 0.03f), inner = w - 2 * 0.05f * w;   // as the geometry builds them (racket/heroic/geometry/wheels.rkt)
            var slot = new float[n];
            var water = new MeshInstance3D[n];
            for (int i = 0; i < n; i++)
            {
                slot[i] = Mathf.Tau * i / n;
                water[i] = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, MaterialOverride = Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.92f), Visible = false };   // a material of its own each
                AddChild(water[i]);   // the view's, not the wheel's: a mesh under a body would change its buoyancy box (BoxSize) and so its physics
            }
            var up = Vector3.Up - d.Axis * Vector3.Up.Dot(d.Axis);
            if (up.LengthSquared() < 1e-6f) up = Vector3.Forward;
            var ref2 = up.Normalized();
            _norias[d] = new NoriaView
            {
                Drive = d, Buckets = n, Radius = r, Reach = r - bd / 2, Slot = slot, Water = water,
                Y0 = -len / 2 + plate, Back = len - plate, Deep = bd * 1.06f, Wide = w * 1.02f,   // a hair proud of the bucket's walls, so the level reads from the side too (readable over realistic)
                Ref1 = ref2.Cross(d.Axis.Normalized()).Normalized(), Ref2 = ref2,
            };
            _building = null;
        }
    }

    private static float Polar(NoriaView n, Vector3 v) => Mathf.Atan2(v.Dot(n.Ref2), v.Dot(n.Ref1));
    private static float Wrap(float a) => ((a % Mathf.Tau) + Mathf.Tau) % Mathf.Tau;

    /// <summary>The share of each pour window, of the time a bucket takes to come to the same place, that a bucket spends tipping.</summary>
    private const float PourDuty = 0.35f;

    private void DrawNoria(LiftDrive d, NoriaView n)
    {
        var hub = d.Body.GlobalPosition;
        float turn = Polar(n, d.Body.GlobalBasis.X);                      // where bucket 0 is
        float tip = Polar(n, d.Spout - hub);                              // where a bucket reaches the trough and tips
        float step = Mathf.Tau / n.Buckets, window = PourDuty * step;
        // where the river's surface cuts the circle the buckets ride: they dip in on the way down, come out on the way up
        float arg = ((float)d.Lift.From.SurfaceElevation - hub.Y) / n.Reach;
        float exit = Mathf.Asin(Mathf.Clamp(arg, -1, 1));
        float sEnter = Wrap(Mathf.Pi - exit - tip), sExit = Wrap(exit - tip);
        float fill = (float)d.Lift.Fill;
        int pouringBucket = -1;
        for (int i = 0; i < n.Buckets; i++)
        {
            float psi = turn + n.Slot[i];
            float s = Wrap(psi - tip);
            float f = s < window ? 1 - s / window
                : arg < -1 ? 0
                : arg > 1 ? 1
                : s < sEnter ? 0
                : s < sExit && sExit > sEnter ? (s - sEnter) / (sExit - sEnter)
                : 1;
            if (s < window && d.Lift.Flow > 1e-5) pouringBucket = i;
            f *= fill;
            var water = n.Water[i];
            water.Visible = f > 0.02f;
            if (!water.Visible) continue;
            // against the back wall (the trailing end), as much of the pocket as there is water
            float lenWater = f * n.Back;
            var turnBy = new Basis(Vector3.Back, n.Slot[i]);
            water.GlobalTransform = d.Body.GlobalTransform * new Transform3D(turnBy * Basis.FromScale(new Vector3(n.Deep, lenWater, n.Wide)),
                                                                              turnBy * new Vector3(n.Reach, n.Y0 + lenWater / 2, 0));
        }

        if (FlowReport && Runtime.Time >= n.NextDump)
        {
            n.NextDump = Runtime.Time + 10;
            GD.Print($"[flow] t={Runtime.Time:F2} noria buckets: turn {Mathf.RadToDeg(turn):F1} deg, tip {Mathf.RadToDeg(tip):F1} deg, river exit {Mathf.RadToDeg(exit):F1} deg (arg {arg:F3}), fill {fill:F2}, full {n.Water.Count(w => w.Visible)} of {n.Buckets}");
        }
        // the pour: only while a bucket tips, from its lip, sized by the bucketful (flow over the share of time it runs)
        bool pour = pouringBucket >= 0;
        d.Stream.Visible = pour;
        if (pour)
        {
            float psi = turn + n.Slot[pouringBucket];
            var lip = hub + n.Ref1 * (n.Reach * Mathf.Cos(psi)) + n.Ref2 * (n.Reach * Mathf.Sin(psi));
            float bottom = (float)d.Lift.To.SurfaceElevation;
            float height = Mathf.Max(lip.Y - bottom, 0.02f);
            float thickness = Mathf.Clamp(Mathf.Sqrt((float)d.Lift.Flow / PourDuty) * 0.6f, 0.01f, 0.12f);
            d.Stream.Scale = new Vector3(thickness, height, thickness);
            d.Stream.Position = new Vector3(lip.X, bottom + height / 2, lip.Z);
        }
        if (pour && !n.Pouring)
        {
            n.Pours++;
            double now = Runtime.Time;
            if (n.First < 0) n.First = now;
            if (FlowReport)
                GD.Print($"[flow] t={now:F2} {d.Spec.Id}: pour #{n.Pours} from bucket {pouringBucket}" + (n.Pours > 1
                    ? $"; {(n.Pours - 1) / (now - n.First):F4} pours/s so far; sim rpm {d.Lift.Rpm:F3} x {n.Buckets} buckets / 60 = {d.Lift.Rpm * n.Buckets / 60:F4}" : ""));
        }
        n.Pouring = pour;
    }
}
