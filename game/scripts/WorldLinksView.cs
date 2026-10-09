using System.Globalization;
using System.Text;
using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Draws a world's links between machines (issue #78). A pipe is a bronze
/// pipe from one tank's port to the other's with the water in it shown
/// running (a blue core, thicker and brighter the faster it flows, and
/// gone when nothing moves); a shaft is an iron line shaft from one
/// turning part to the other, spinning at the driving end's speed with a
/// dark stripe so the turning shows. A link whose part or port has gone is
/// drawn red, with the reason on its tag, as unfinished parts are in build
/// mode. A wire (issue #208) is a copper line from the generator up over to the
/// bank, glowing while it carries charge. Each link carries a tag with its live numbers.
/// </summary>
public partial class WorldLinksView : Node3D
{
    private sealed class Drawn
    {
        public required WorldLinks.Link Link;
        public MeshInstance3D? Water;
        public List<StandardMaterial3D> Copper = [];
        public Node3D? Spinner;
        public double Angle;
        public Label3D? Tag;
    }

    private readonly List<Drawn> _drawn = [];
    private WorldLinks? _links;
    private StreamWriter? _trace;
    private double _traceDt, _time;
    private long _traceFrames;

    private static readonly Color Unfinished = new(0.9f, 0.15f, 0.1f);

    /// <summary>Draws <paramref name="links"/>, placed by <paramref name="point"/>: where a (label, part, port) end stands, or null if it has gone.</summary>
    public void Show(WorldLinks links, Func<LinkEnd, Vector3?> point)
    {
        foreach (var c in GetChildren()) c.QueueFree();
        _drawn.Clear();
        _links = links;
        foreach (var link in links.All)
        {
            var a = point(link.Spec.From);
            var b = point(link.Spec.To);
            var d = new Drawn { Link = link };
            _drawn.Add(d);
            if (a is not { } from || b is not { } to)
            {
                // an end has gone: a red stub at whichever end is left
                if ((a ?? b) is { } left)
                {
                    AddChild(Shapes.Sphere(0.12f, Shapes.Mat(Unfinished)));
                    GetChild<Node3D>(GetChildCount() - 1).Position = left;
                    d.Tag = Tag(left + Vector3.Up * 0.3f, $"{link.Spec.Id}: unfinished", Unfinished);
                }
                continue;
            }
            bool broken = link.Unfinished is not null;
            var mid = (from + to) / 2;
            if (link.Spec.Kind == "pipe")
            {
                AddChild(Shapes.Rod(from, to, 0.035f, broken ? Shapes.Mat(Unfinished) : Shapes.Mat(Shapes.Bronze, metallic: 0.8f, roughness: 0.35f, alpha: 0.45f)));
                if (!broken)
                {
                    var water = Shapes.Rod(from, to, 0.025f, Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.85f));
                    var mat = (StandardMaterial3D)water.MaterialOverride;
                    mat.EmissionEnabled = true;
                    mat.Emission = Shapes.Water;
                    AddChild(water);
                    d.Water = water;
                }
            }
            else if (link.Spec.Kind == "wire")
            {
                // a copper wire from the generator up, across at a height clear of both and down to the bank (lossless: it is only drawn)
                float top = Mathf.Max(from.Y, to.Y) + 1.2f;
                Vector3 up = @from with { Y = top }, over = to with { Y = top };
                foreach (var (p, q) in new[] { (@from, up), (up, over), (over, to) })
                {
                    var mat = broken ? Shapes.Mat(Unfinished) : Shapes.Mat(Shapes.Copper, metallic: 0.7f, roughness: 0.4f);
                    AddChild(Shapes.Rod(p, q, 0.02f, mat));
                    if (!broken) d.Copper.Add(mat);
                }
                mid = (up + over) / 2;
                foreach (var end in new[] { @from, to })
                {
                    var lug = Shapes.Box(new Vector3(0.14f, 0.14f, 0.14f), Shapes.Mat(new Color(0.25f, 0.25f, 0.27f), metallic: 0.6f, roughness: 0.5f));
                    lug.Position = end;
                    AddChild(lug);
                }
            }
            else
            {
                // a line shaft, spinning about its own length
                var holder = new Node3D { Position = mid };
                var dir = (to - from).Normalized();
                var axis = Vector3.Up.Cross(dir);
                if (axis.LengthSquared() > 1e-8f) holder.Basis = new Basis(axis.Normalized(), Vector3.Up.AngleTo(dir));
                AddChild(holder);
                var spinner = new Node3D();
                holder.AddChild(spinner);
                float len = (to - from).Length();
                spinner.AddChild(Shapes.Cylinder(0.06f, len, broken ? Shapes.Mat(Unfinished) : Shapes.Mat(new Color(0.35f, 0.33f, 0.32f), metallic: 0.8f, roughness: 0.4f)));
                var stripe = Shapes.Box(new Vector3(0.02f, len, 0.02f), Shapes.Mat(new Color(0.9f, 0.8f, 0.3f)));
                stripe.Position = new Vector3(0.06f, 0, 0);
                spinner.AddChild(stripe);
                // a bearing block at each end
                foreach (var end in new[] { from, to })
                {
                    var block = Shapes.Box(new Vector3(0.22f, 0.22f, 0.22f), Shapes.Mat(new Color(0.45f, 0.3f, 0.18f)));
                    block.Position = end;
                    AddChild(block);
                }
                d.Spinner = spinner;
            }
            d.Tag = Tag(mid + Vector3.Up * 0.35f, link.Spec.Id, broken ? Unfinished : Colors.White);
            d.Tag.PixelSize = 0.004f * Mathf.Max(1, (to - from).Length() / 5);   // readable from as far as the link is long
        }
        Refresh(0);
    }

    private Label3D Tag(Vector3 at, string text, Color color)
    {
        var label = new Label3D
        {
            Text = text, Position = at, FontSize = 28, OutlineSize = 8, PixelSize = 0.004f, Modulate = color,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
        };
        AddChild(label);
        return label;
    }

    /// <summary>Brings the drawing up to the links' state after a tick of dt.</summary>
    public void Refresh(double dt)
    {
        foreach (var d in _drawn)
        {
            var l = d.Link;
            if (l.Unfinished is not null)
            {
                if (d.Tag is not null) d.Tag.Text = $"{l.Spec.Id}: unfinished\n{l.Unfinished}";
                continue;
            }
            if (l.Pipe is { } pipe && d.Water is { } water)
            {
                double q = Math.Abs(pipe.Flow) * 1000;   // L/s
                water.Visible = q > 0.001;
                float thick = Mathf.Clamp((float)Math.Sqrt(q / 4), 0.25f, 1f);
                water.Scale = new Vector3(thick, 1, thick);
                ((StandardMaterial3D)water.MaterialOverride).EmissionEnergyMultiplier = Mathf.Clamp((float)q / 4, 0.1f, 1.2f);
                if (d.Tag is not null) d.Tag.Text = $"{l.Spec.Id} · {pipe.Flow * 1000:F2} L/s";
            }
            if (l is { Generator: { } gen, Bank: { } bank })
            {
                // the copper glows while it carries charge, in step with the share of the rated power
                double watts = gen.Delivered;
                float glow = watts > 0 ? Mathf.Clamp((float)(watts / Math.Max(1, gen.Efficiency * gen.RatedTorque * gen.RatedOmega)) * 3f, 0.3f, 1.5f) : 0f;
                foreach (var m in d.Copper)
                {
                    m.EmissionEnabled = glow > 0;
                    m.Emission = new Color(1f, 0.6f, 0.25f);
                    m.EmissionEnergyMultiplier = glow;
                }
                if (d.Tag is not null)
                    d.Tag.Text = $"{l.Spec.Id} · {watts:F0} W · {watts / bank.Volts:F1} A · {bank.ChargeWh:F1}/{bank.CapacityWh:F0} Wh" + (bank.Accepting ? "" : " (open circuit)");
            }
            if (l.Shaft is { } shaft && d.Spinner is { } spinner)
            {
                d.Angle = (d.Angle + shaft.AngularVelocity * dt) % Math.Tau;
                spinner.Rotation = new Vector3(0, (float)d.Angle, 0);
                if (d.Tag is not null) d.Tag.Text = $"{l.Spec.Id} · {shaft.Rpm:F1} rpm · {shaft.Torque:F0} N·m · {shaft.Power / 1000:F2} kW";
            }
        }
        if (dt > 0) TraceTick(dt);
    }

    // A trace of the links' fields, <path>.links, in the frame shape of a machine's trace (see MachineView.Trace.cs).
    public void StartTrace(string path, double sampleDt, double time)
    {
        _trace = new StreamWriter(path, append: false, new UTF8Encoding(false));
        _traceDt = sampleDt;
        _time = time;
        _traceFrames = (long)Math.Floor(time / sampleDt + 1e-9);   // started mid-run (the first link made then): on the run's own sampling grid
        WriteFrame();
    }

    public bool Tracing => _trace is not null;
    /// <summary>More world-level readings to trace with the links' (the ground's, issue #37).</summary>
    public IReadOnlyDictionary<string, Func<double>> ExtraFields { get; set; } = new Dictionary<string, Func<double>>();

    public void StopTrace()
    {
        _trace?.Flush();
        _trace?.Dispose();
        _trace = null;
    }

    /// <summary>After a sleep that ran the machines ahead without stepping this view: the clock it stamps its frames with catches up (#207).</summary>
    public void SkipTo(double time) { if (time > _time) _time = time; }

    private void TraceTick(double dt)
    {
        if (_trace is null) return;
        _time += dt;
        if (_time + dt / 2 >= _traceFrames * _traceDt)
        {
            WriteFrame();
            _traceFrames = Math.Max(_traceFrames, (long)Math.Floor((_time + dt / 2) / _traceDt) + 1);   // as a machine's trace: not caught up a frame a tick (#207)
        }
    }

    private void WriteFrame()
    {
        _traceFrames++;
        var sb = new StringBuilder();
        sb.Append('(').Append(_time.ToString("R", CultureInfo.InvariantCulture));
        if (_links is not null)
            foreach (var (key, get) in _links.FieldGetters.Concat(ExtraFields))
            {
                double v = get();
                if (double.IsFinite(v)) sb.Append(" (").Append(key).Append(' ').Append(v.ToString("R", CultureInfo.InvariantCulture)).Append(')');
            }
        sb.Append(')');
        _trace!.WriteLine(sb.ToString());
    }
}
