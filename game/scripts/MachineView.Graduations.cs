using Godot;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// Graduations on vessels (#174): a scale on the wall of every vessel whose level the machine is about, so a slow
/// level (a receiver rising 1.8 cm a minute) and two levels a few centimetres apart can be read, not guessed.
/// <list type="bullet">
/// <item>Marks at round steps (1, 2 or 5 times a power of ten) of the vessel's own capacity, in litres for a vessel
/// a litre counts (area 0.1 to 1 m², or one a wake watches in litres; a thin one, like a receiver, in cm) and in centimetres of depth otherwise (or one
/// a trigger watches by level), chosen so no more than ten fit. A mark is a thin ring of dark ink round the wall;
/// the lowest and highest carry their number.</item>
/// <item>Marks in the trigger's amber where the machine watches the level: a wake term or event, or a field
/// trigger, on this vessel's <c>water</c> (litres) or <c>level</c> (cm). Thicker, and always numbered.</item>
/// </list>
/// The marks are children of the vessel's shell, so a hung vessel carries them, and one merged mesh per colour with
/// its own unlit material: no builder's material is shared or replaced.
/// </summary>
public partial class MachineView
{
    /// <summary>The depth (m) a vessel's marks stand at: the regular steps and the watched thresholds, with the numbers.</summary>
    public readonly record struct GraduationMark(float Depth, double Value, string Unit, bool Watched);

    /// <summary>
    /// The marks for a vessel of <paramref name="capacity"/> m of depth over <paramref name="area"/> m², given the fields the
    /// machine watches on it. <paramref name="grain"/>: a hopper, always measured in cm of depth.
    /// </summary>
    public static List<GraduationMark> GraduationMarks(float capacity, double area, IEnumerable<(string Field, double Value)> watches, bool grain = false)
    {
        var watched = watches.Where(w => w.Field is "water" or "level").ToList();
        bool litres = !grain && (watched.Any(w => w.Field == "water") || (!watched.Any(w => w.Field == "level") && area is >= 0.1 and <= 1.0));
        double perUnit = litres ? 1.0 / (1000 * area) : 0.01;                  // m of depth for one unit
        double range = capacity / perUnit;                                      // the capacity, in the unit
        string unit = litres ? "L" : "cm";
        var marks = new List<GraduationMark>();
        var amber = new List<GraduationMark>();
        foreach (var (field, value) in watched.Distinct())
        {
            // a "water" threshold is litres, a "level" one centimetres, whatever unit the plain marks are in
            double depth = field == "water" ? value / (1000 * area) : value / 100;
            if (depth > 1e-4 && depth <= capacity * 1.0001) amber.Add(new GraduationMark((float)depth, value, field == "water" ? "L" : "cm", true));
        }
        double step = Skins.NiceStep(range, 10);
        for (int k = 1; k * step < range * 0.999; k++)
        {
            float depth = (float)(k * step * perUnit);
            if (amber.Any(a => Math.Abs(a.Depth - depth) < capacity * 0.004)) continue;   // the amber one says it
            marks.Add(new GraduationMark(depth, k * step, unit, false));
        }
        marks.AddRange(amber);
        return marks;
    }

    /// <summary>The marks drawn on vessels, for the check that a drawn level crosses a mark when the trace says it does (HEROIC_GAUGE_TRACE).</summary>
    private readonly List<(string Tank, Node3D Vessel, float LocalY, string Label, bool Watched, bool[] Crossed)> _drawnMarks = [];

    private static readonly Color MarkNumber = new(0.97f, 0.95f, 0.88f);

    /// <summary>
    /// Draws the marks on <paramref name="vessel"/>, a box shell: half widths <paramref name="halfX"/> and <paramref name="halfZ"/>,
    /// its floor at local <paramref name="floorY"/>, <paramref name="capacity"/> m of depth to hold. A rule, not a per-machine drawing.
    /// </summary>
    private void Graduate(Node3D vessel, string id, float halfX, float halfZ, float floorY, float capacity, double area, bool grain = false)
    {
        if (capacity < 0.02f || area <= 0) return;
        var watches = new List<(string, double)>();
        foreach (var w in Runtime.Def.Wakes)
            foreach (var t in w.Terms.Concat(w.Events).Where(t => t.Target == id)) watches.Add((t.Field, t.Value));
        foreach (var t in Runtime.Def.Triggers.Where(t => t.WatchTarget == id && t.WatchField is not null)) watches.Add((t.WatchField!, t.Threshold));
        var marks = GraduationMarks(capacity, area, watches, grain);
        if (marks.Count == 0) return;

        float e = Mathf.Clamp(Mathf.Min(halfX * 2, capacity) * 0.012f, 0.003f, 0.006f);   // a ring's height; 1.2% of the smaller of width and depth
        var ink = new List<(Vector3, Vector3)>();
        var amber = new List<(Vector3, Vector3)>();
        void Ring(List<(Vector3, Vector3)> into, float y, float k)
        {
            float t = e * k, ex = halfX + t * 0.5f, ez = halfZ + t * 0.5f;
            into.Add((new Vector3(0, y, ez), new Vector3(halfX * 2 + 2 * t, t, t)));    // front and back
            into.Add((new Vector3(0, y, -ez), new Vector3(halfX * 2 + 2 * t, t, t)));
            into.Add((new Vector3(ex, y, 0), new Vector3(t, t, halfZ * 2)));            // the two sides
            into.Add((new Vector3(-ex, y, 0), new Vector3(t, t, halfZ * 2)));
        }
        var plain = marks.Where(m => !m.Watched).ToList();
        foreach (var m in marks)
        {
            Ring(m.Watched ? amber : ink, floorY + m.Depth, m.Watched ? 1.3f : 1f);
            _drawnMarks.Add((id, vessel, floorY + m.Depth, $"{m.Value:0.#}{m.Unit}", m.Watched, [false]));
        }
        if (ink.Count > 0) { var bars = Skins.Bars(ink, Skins.Ink); bars.Name = "graduations"; vessel.AddChild(bars); }
        if (amber.Count > 0) { var bars = Skins.Bars(amber, Skins.Watch); bars.Name = "thresholds"; vessel.AddChild(bars); }

        // the numbers: the lowest and highest plain marks, and every watched one
        foreach (var m in plain.Where(m => m == plain[0] || m == plain[^1]).Concat(marks.Where(m => m.Watched)))
        {
            var label = new Label3D
            {
                Text = $"{m.Value:0.#} {m.Unit}", Position = new Vector3(halfX + e * 3, floorY + m.Depth, halfZ),
                FontSize = 24, OutlineSize = 8, PixelSize = 0.0025f, NoDepthTest = true,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = m.Watched ? Skins.Watch : MarkNumber,
            };
            vessel.AddChild(label);
        }
    }

    // ── a capstan's slip line (#174) ────────────────────────────────────────────────────────────────────────

    private const float SlipScale = 1.2f;   // m: a hold scale at the hauler, as tall as the load's weight (or the hold, if that is more)
    private readonly List<(Capstan Capstan, MeshInstance3D Fill, StandardMaterial3D FillMat, MeshInstance3D Line, Label3D Label, float Foot)> _slipLines = [];

    /// <summary>
    /// Beside the hauler: a scale from nothing up to the load's weight, a bar for the hold (green while it holds, red once it
    /// slips) and an amber line where the rope lets go, at weight / e^(mu theta): what a turn more or less does to it shows at once.
    /// </summary>
    private void BuildSlipLines()
    {
        foreach (var (id, capstan) in Runtime.Capstans)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var top = V(part.At);
            float x = top.X - 0.8f - 0.35f, z = top.Z - 0.7f;
            var rail = Shapes.Box(new Vector3(0.025f, SlipScale, 0.025f), Shapes.Mat(Skins.Ink, outline: false));
            rail.Position = new Vector3(x, SlipScale / 2, z);
            AddChild(rail);
            var fillMat = Shapes.Mat(new Color(0.3f, 0.75f, 0.35f), outline: false);
            var fill = Shapes.Box(Vector3.One, fillMat);
            AddChild(fill);
            var line = Skins.Bars([(Vector3.Zero, new Vector3(0.22f, 0.012f, 0.05f))], Skins.Watch);
            AddChild(line);
            var label = new Label3D
            {
                FontSize = 24, OutlineSize = 8, PixelSize = 0.0025f, NoDepthTest = true, Modulate = Skins.Watch,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            };
            AddChild(label);
            _slipLines.Add((capstan, fill, fillMat, line, label, x));
        }
    }

    private void DrawSlipLines()
    {
        foreach (var (c, fill, mat, line, label, x) in _slipLines)
        {
            float z = (float)Runtime.Def.Part(c.Name)!.At.Z - 0.7f;
            double top = Math.Max(c.Weight, c.Hold);
            float held = (float)(c.Hold / top) * SlipScale, slip = (float)(c.LeastHold / top) * SlipScale;
            fill.Scale = new Vector3(0.05f, Mathf.Max(held, 0.004f), 0.05f);
            fill.Position = new Vector3(x, Mathf.Max(held, 0.004f) / 2, z);
            mat.AlbedoColor = c.Hold >= c.LeastHold ? new Color(0.3f, 0.75f, 0.35f) : new Color(0.85f, 0.2f, 0.15f);
            line.Position = new Vector3(x, slip, z);
            label.Position = new Vector3(x + 0.45f, slip, z);
            label.Text = $"slips below {c.LeastHold:0.#} N";
        }
    }
}
