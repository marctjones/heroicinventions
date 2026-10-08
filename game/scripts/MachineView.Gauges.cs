using System.Globalization;
using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Mechanics;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Pressure you can see (#170). One dial for every pressurised vessel, so a pressure reads the same on a boiler, a
/// cylinder, a sealed air vessel and a room:
/// <list type="bullet">
/// <item>The needle sweeps 270 degrees (<see cref="Skins.GaugeAngle"/>) from zero over the top. A rigid vessel with a
/// limit (a rated boiler, a cylinder on one) is scaled so full is its burst limit, with an amber mark at 60%, where
/// <see cref="Skins.Rim"/>'s outline starts to warm, and a red one at 100%: the needle crosses the amber mark on the
/// tick the rim turns amber, because both read <see cref="BoilerShare"/>. A vessel with no limit (an unrated boiler,
/// a sealed air vessel, a room) has a plain scale: 200 kPa gauge for a boiler, 100 for a cylinder or room, 25 for
/// a sealed air vessel, no amber or red.</item>
/// <item>Below the air outside, the needle swings the other way from zero and turns blue, a quarter turn at a full
/// vacuum: a Newcomen cylinder after the cold jet, a chamber pumped down.</item>
/// <item>A room's dial reads gauge pressure (its pressure over the air outside), so on Mars a chamber at 5 kPa reads 4.39
/// kPa on a 100 kPa scale: the needle's angle is 135 - 2.7 x gauge (kPa) degrees.</item>
/// </list>
/// Optionally <c>HEROIC_GAUGE_TRACE=&lt;path&gt;</c> (and <c>HEROIC_GAUGE_DT</c>, default 1 s; 0 is every drawn frame) writes one line per dial per
/// sample: sim time, id, share, needle angle, the rim's colour or "plain" on a boiler, the gauge in kPa and the wall clock in ms; a check
/// compares it with the trace's pressure at the same time.
/// </summary>
public partial class MachineView
{
    private const double AmbientDialKPa = 100;                              // a room or a cylinder: full scale, kPa gauge
    private static readonly double[] PlainScale = [200_000, 100_000, 25_000];   // Pa: unrated boiler, cylinder or room, sealed air

    /// <summary>A dial: a face with its marks, and a needle that <see cref="Set"/> turns to a share of the scale.</summary>
    private sealed class Dial
    {
        public required Node3D Root, Needle;
        public required StandardMaterial3D NeedleMat;
        public float Angle { get; private set; }
        public void Set(double share)
        {
            Angle = Skins.GaugeAngle(share);
            Needle.RotationDegrees = new Vector3(0, 0, Angle);
            NeedleMat.AlbedoColor = share < 0 ? Skins.Vacuum : new Color(0.65f, 0.08f, 0.05f);
        }
    }

    private sealed record GaugeView(string Id, Dial Dial, Func<double> Share, Func<bool>? Hidden = null, Action<Dial>? Anchor = null, Func<double>? Kpa = null, Func<string>? Rim = null);
    private readonly List<GaugeView> _gauges = [];

    /// <summary>The share of a boiler's burst limit its pressure has reached: the dial's needle and the shell's strain rim both read this.</summary>
    internal static double BoilerShare(Boiler b) => b.GaugePressure / Math.Max(1.0, b.BurstLimit);

    private static Dial NewDial(Node3D parent, Vector3 at, float radius, bool limit)
    {
        var root = new Node3D { Position = at };
        parent.AddChild(root);
        var face = Shapes.Cylinder(radius, 0.012f, Shapes.Mat(new Color(0.95f, 0.94f, 0.9f)));
        face.RotationDegrees = new Vector3(90, 0, 0);
        root.AddChild(face);
        void Tick(double share, Color color, float length, float width)
        {
            var pivot = new Node3D { RotationDegrees = new Vector3(0, 0, Skins.GaugeAngle(share)), Position = new Vector3(0, 0, 0.0085f) };
            var bar = Shapes.Box(new Vector3(radius * width, radius * length, 0.004f), Shapes.Mat(color, outline: false));
            bar.Position = new Vector3(0, radius * (0.98f - length / 2), 0);
            pivot.AddChild(bar);
            root.AddChild(pivot);
        }
        Tick(0, Skins.Ink, 0.22f, 0.07f);
        Tick(-1, Skins.Vacuum, 0.22f, 0.07f);
        if (limit)
        {
            Tick(Skins.RimFrom, Skins.Watch, 0.34f, 0.12f);                    // where the shell's outline starts to warm
            Tick(1, new Color(0.85f, 0.1f, 0.08f), 0.34f, 0.12f);              // the limit
        }
        else Tick(1, Skins.Ink, 0.22f, 0.07f);
        var needleMat = Shapes.Mat(new Color(0.65f, 0.08f, 0.05f), outline: false);
        var needle = new Node3D { Position = new Vector3(0, 0, 0.0125f) };
        var bar2 = Shapes.Box(new Vector3(radius * 0.09f, radius * 0.86f, 0.006f), needleMat);
        bar2.Position = new Vector3(0, radius * 0.43f, 0);
        needle.AddChild(bar2);
        root.AddChild(needle);
        var hub = Shapes.Cylinder(radius * 0.1f, 0.014f, Shapes.Mat(Skins.Ink, outline: false));
        hub.RotationDegrees = new Vector3(90, 0, 0);
        hub.Position = new Vector3(0, 0, 0.0125f);
        root.AddChild(hub);
        return new Dial { Root = root, Needle = needle, NeedleMat = needleMat };
    }

    private static double SignedShare(double gauge, double ambient, double scale) => gauge >= 0 ? gauge / scale : gauge / Math.Max(1.0, ambient);

    private void BuildGauges()
    {
        // a boiler: on the front of its shell, beside the bands
        foreach (var (id, boiler) in Runtime.Boilers)
        {
            if (!_boilerBodies.TryGetValue(id, out var body)) continue;
            _building = id;
            var part = Runtime.Def.Part(id)!;
            float r = (float)part.Number("radius"), h = (float)part.Number("height");
            bool rated = boiler.Rating > 0;
            var dial = NewDial(this, V(part.At) + new Vector3(0, h * 0.62f, r + Mathf.Clamp(r * 0.05f, 0.006f, 0.03f) + 0.012f), Mathf.Clamp(r * 0.5f, 0.05f, 0.16f), rated);   // proud of the bands (Skins.BandBoiler)
            _gauges.Add(new GaugeView(id, dial,
                () => boiler.AbsolutePressure >= boiler.Zone.Pressure
                    ? (rated ? BoilerShare(boiler) : boiler.GaugePressure / PlainScale[0])
                    : (boiler.AbsolutePressure - boiler.Zone.Pressure) / Math.Max(1.0, boiler.Zone.Pressure),
                () => boiler.Burst,
                Kpa: () => boiler.GaugePressure / 1000,
                Rim: () => body.MaterialOverride is StandardMaterial3D m && m.NextPass is ShaderMaterial s && s != Skins.Outline
                    ? ((Color)s.GetShaderParameter("color")).ToHtml(false) : "plain"));
        }
        // a cylinder: on its casing, low on the front. Rated by the boiler that feeds it, if that has a rating
        foreach (var spec in Runtime.Def.Cylinders)
        {
            if (!_pistons.TryGetValue(spec.Piston, out var pis)) continue;
            _building = spec.Piston;
            var cylinder = Runtime.Cylinders[spec.Id];
            float bore = (float)pis.Part.Number("bore"), stroke = (float)pis.Part.Number("stroke");
            bool rated = cylinder.Boiler.Rating > 0;
            var dial = NewDial(this, new Vector3(pis.Body.Position.X, pis.Bottom + Mathf.Min(0.25f * stroke, 0.5f), pis.Body.Position.Z + bore / 2 * 1.06f + 0.012f),
                               Mathf.Clamp(bore * 0.2f, 0.06f, 0.16f), rated);
            _gauges.Add(new GaugeView(spec.Id, dial,
                () => SignedShare(cylinder.Pressure - cylinder.Zone.Pressure, cylinder.Zone.Pressure, rated ? Math.Max(1.0, cylinder.Boiler.BurstLimit) : PlainScale[1]),
                Kpa: () => (cylinder.Pressure - cylinder.Zone.Pressure) / 1000));
        }
        // the air shut in over water (Heron's fountain): on the front of the first sealed vessel, riding with it
        foreach (var air in Runtime.AirPockets)
        {
            var pair = Runtime.Tanks.FirstOrDefault(t => t.Value.Air == air);
            if (pair.Value is null || !_tankShells.TryGetValue(pair.Value, out var shell)) continue;
            float side = Mathf.Sqrt((float)pair.Value.Area), height = (float)pair.Value.Height;
            var dial = NewDial(shell, new Vector3(0, height * 0.28f, side / 2 + 0.01f), Mathf.Clamp(side * 0.32f, 0.04f, 0.2f), false);
            _gauges.Add(new GaugeView(pair.Key, dial, () => SignedShare(air.GaugePressure, air.Zone.Pressure, PlainScale[2]), Kpa: () => air.GaugePressure / 1000));
        }
        // a room: on its front wall, which billows out with the room (see DrawEnclosures); the dial stays at its height when the room sags, so it still reads
        foreach (var (id, room) in Runtime.Enclosures)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            float h = (float)part.Number("size-y"), d = (float)part.Number("size-z");
            var at = V(part.At);
            float y = Mathf.Min(1.5f, h * 0.6f), radius = Mathf.Clamp(h * 0.1f, 0.08f, 0.4f);
            var dial = NewDial(this, at + new Vector3(0, y, d / 2 + 0.02f), radius, false);
            var view = _enclosureViews.FirstOrDefault(v => v.Room == room);
            _gauges.Add(new GaugeView(id, dial,
                () => SignedShare(room.GaugePressure, room.Outside.Pressure, AmbientDialKPa * 1000),
                Anchor: dl => { if (view is not null) dl.Root.Position = at + new Vector3(0, y, d / 2 * view.Walls.Scale.Z + 0.02f); },
                Kpa: () => room.GaugePressure / 1000));
        }
        BuildSlipLines();   // the capstans' slip lines (Graduations.cs)
        _gaugeTrace = OS.GetEnvironment("HEROIC_GAUGE_TRACE") is { Length: > 0 } path ? new StreamWriter(path, false) { AutoFlush = true } : null;
        if (double.TryParse(OS.GetEnvironment("HEROIC_GAUGE_DT"), CultureInfo.InvariantCulture, out var dt) && dt >= 0) _gaugeDt = dt;
    }

    private StreamWriter? _gaugeTrace;
    private double _gaugeDt = 1, _gaugeNext;

    private void DrawGauges()
    {
        DrawSlipLines();
        bool sample = _gaugeTrace is not null && (_gaugeDt == 0 || Runtime.Time + 1e-9 >= _gaugeNext);   // a step of 0: every frame, paused ones too
        foreach (var g in _gauges)
        {
            double share = g.Share();
            g.Dial.Set(share);
            g.Dial.Root.Visible = !(g.Hidden?.Invoke() ?? false);
            g.Anchor?.Invoke(g.Dial);
            if (sample)
                _gaugeTrace!.WriteLine(string.Join(' ', Runtime.Time.ToString("F3", CultureInfo.InvariantCulture), g.Id, share.ToString("F5", CultureInfo.InvariantCulture),
                    g.Dial.Angle.ToString("F3", CultureInfo.InvariantCulture), g.Rim?.Invoke() ?? "-", (g.Kpa?.Invoke() ?? 0).ToString("F4", CultureInfo.InvariantCulture), Time.GetTicksMsec().ToString(CultureInfo.InvariantCulture)));   // the wall clock last, to tell a pause from a step
        }
        if (sample)
            foreach (var pv in _pumpViews)   // where the pale P/(rho g) line and the red limit tick are drawn, in metres over the well's surface
                _gaugeTrace!.WriteLine(string.Join(' ', Runtime.Time.ToString("F3", CultureInfo.InvariantCulture), "pump:" + pv.Pump.Name,
                    (pv.Ideal.Position.Y - pv.Pump.From.SurfaceElevation).ToString("F4", CultureInfo.InvariantCulture),
                    (pv.LimitTick.Position.Y - pv.Pump.From.SurfaceElevation).ToString("F4", CultureInfo.InvariantCulture)));
        if (_gaugeTrace is not null)
            foreach (var m in _drawnMarks.Where(m => !m.Crossed[0]))   // the first frame a drawn level's top reaches a mark: every frame, not only on a sample
                if (_water.FirstOrDefault(w => w.spec.Id == m.Tank) is { water: { Visible: true } water }
                    && water.Position.Y + water.Scale.Y / 2 >= m.Vessel.GlobalPosition.Y + m.LocalY)
                {
                    m.Crossed[0] = true;
                    _gaugeTrace.WriteLine($"{Runtime.Time.ToString("F3", CultureInfo.InvariantCulture)} cross:{m.Tank} {m.Label} {(m.Watched ? "watched" : "mark")}");
                }
        if (sample) _gaugeNext = Runtime.Time + _gaugeDt;
    }
}
