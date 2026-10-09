using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Vessels stacked in a column, and the air shut in them (Heron's fountain, redrawn as Hero drew it). Four rules, each
/// for any machine whose blueprint has the shape, none naming a machine:
///
/// 1. <b>A stack is cut away.</b> A vessel that holds sealed air, or has another tank directly above or below it, is
///    drawn with its near walls removed (the glass is back-face only, no hoop across the front), so its water level and
///    its air show from the front. A tank that rests on another stands on four corner posts in the neck between them,
///    not on a post to the ground, which would run through the vessels below.
/// 2. <b>A pipe up a stack goes round.</b> The sim's ports are at a tank's middle, so two pipes in one column would lie
///    on each other inside the water. A pipe between tanks in one column that has another tank between its ends is drawn
///    up the outside of the column, one side to each pipe in turn (the first on the left), entering each vessel through
///    its wall; with the bore thin enough for the vessels (<see cref="ColumnBore"/>). The nozzle with nothing in its way
///    stays straight up the middle. The sim's pipe has no route; this is only where it is drawn.
/// 3. <b>Air is drawn as air.</b> The air shut in a sealed vessel is a tinted box from the water's surface to the lid
///    (<see cref="Skins.Air"/>), thicker as the gas is squeezed (<see cref="Skins.AirTint"/>), so a receiver filling
///    with water shows its air shrinking and darkening. Vessels of a stack sharing one air pocket are joined by a tube of
///    the same tint up the side opposite the first pipe.
/// 4. <b>The head rule on the scene.</b> A jet out of a sealed vessel (a jet pipe from a tank on a sealed air pocket) into
///    an open tank that drains by a pipe into another tank of the pocket is Heron's fountain: water that falls from that
///    tank's surface to the receiver's surface lifts the jet about as far above the supply's surface. Two matched
///    brackets in <see cref="Skins.Head"/> show it, "falls h" at the left and "lifts about h" at the right, on the live
///    surfaces: the fall from the open tank's to the receiver's, the lift from the supply's surface up by the air's
///    pressure head P/(rho g) (the top of the jet when it runs). A tick marks the nozzle's tip, so a lift that cannot reach it
///    is a dead jet. When the drain has stopped (receiver full, basin empty, or the levels met) the fall's bracket dims
///    and says why.
/// </summary>
public partial class MachineView
{
    // ---- 1. a stack

    private IEnumerable<PartSpec> TankParts => Runtime.Def.Parts.Where(p => p.Kind == "tank");
    private static float SideOf(PartSpec tank) => Mathf.Sqrt((float)tank.Number("area"));
    private static float HeightOf(PartSpec tank) => (float)tank.Number("height");
    private static bool Overlaps(PartSpec a, PartSpec b) =>
        Math.Abs(a.At.X - b.At.X) < (SideOf(a) + SideOf(b)) / 2 && Math.Abs(a.At.Z - b.At.Z) < (SideOf(a) + SideOf(b)) / 2;

    /// <summary>The widest neck between two vessels of a stack, m: farther apart than this they are separate machines' parts, not one vessel.</summary>
    private const float MaxNeck = 0.2f;

    /// <summary>The tank this one rests on: the nearest under it in its column, its top no more than a neck below this floor.</summary>
    private PartSpec? TankBelow(PartSpec part) => TankParts
        .Where(o => o.Id != part.Id && Overlaps(o, part) && o.At.Y + HeightOf(o) <= part.At.Y + 1e-6 && part.At.Y - (o.At.Y + HeightOf(o)) <= MaxNeck)
        .OrderByDescending(o => o.At.Y).FirstOrDefault();

    private PartSpec? TankAbove(PartSpec part) => TankParts
        .Where(o => o.Id != part.Id && Overlaps(o, part) && part.At.Y + HeightOf(part) <= o.At.Y + 1e-6 && o.At.Y - (part.At.Y + HeightOf(part)) <= MaxNeck)
        .OrderBy(o => o.At.Y).FirstOrDefault();

    private bool InStack(PartSpec part) => TankBelow(part) is not null || TankAbove(part) is not null;

    /// <summary>Drawn cut away: it holds sealed air over its water, or it is part of a stack.</summary>
    private bool IsCutAway(PartSpec part) => Runtime.Tanks[part.Id].Air is not null || InStack(part);

    /// <summary>Glass seen from inside: only the far faces are drawn, so the near walls are gone and the contents show.</summary>
    private static StandardMaterial3D CutAwayGlass()
    {
        var glass = Shapes.Glass();
        glass.CullMode = BaseMaterial3D.CullModeEnum.Front;
        return glass;
    }

    /// <summary>Four corner posts through the neck between a vessel and the one it rests on.</summary>
    private void AddNeckPosts(PartSpec part)
    {
        var below = TankBelow(part)!;
        float r = Mathf.Clamp(Mathf.Min(SideOf(part), SideOf(below)) * 0.05f, 0.005f, 0.012f);
        float half = Mathf.Min(SideOf(part), SideOf(below)) / 2 - r - 0.004f;
        float y0 = (float)below.At.Y + HeightOf(below), y1 = (float)part.At.Y;
        var oak = Surface("oak");
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                AddChild(Shapes.Rod(new Vector3((float)part.At.X + sx * half, y0, (float)part.At.Z + sz * half),
                                    new Vector3((float)part.At.X + sx * half, y1, (float)part.At.Z + sz * half), r, oak));
    }

    // ---- 2. a pipe up a stack

    /// <summary>The bore a pipe is drawn with between two vessels of a stack: thin beside a small vessel (the default is 30 mm).</summary>
    private static float ColumnBore(float narrowestSide) => Mathf.Clamp(narrowestSide * 0.07f, 0.006f, PipeBore);

    private int _lanes;   // pipes and tubes drawn up the outside of a column so far: even ones on the left, odd on the right

    private static bool IsColumn(Vector3 a, Vector3 b) =>
        Mathf.Abs(a.X - b.X) < 1e-4f && Mathf.Abs(a.Z - b.Z) < 1e-4f && Mathf.Abs(a.Y - b.Y) > 0.02f;

    /// <summary>
    /// How a pipe between two tanks of one column is drawn, with its bore: straight when nothing is in its way, otherwise up
    /// the outside of the column. Null for any other pipe (the caller draws it straight, at the default bore).
    /// </summary>
    private (List<Vector3> Route, float Bore)? ColumnRoute(PipeSpec pipe, Vector3 from, Vector3 to)
    {
        if (!IsColumn(from, to)) return null;
        var a = Runtime.Def.Part(pipe.From.Part)!; var b = Runtime.Def.Part(pipe.To.Part)!;
        if (a.Kind != "tank" || b.Kind != "tank") return null;
        float bore = ColumnBore(Mathf.Min(SideOf(a), SideOf(b)));
        float lo = Mathf.Min(from.Y, to.Y), hi = Mathf.Max(from.Y, to.Y);
        var between = TankParts.Where(t => t.Id != a.Id && t.Id != b.Id && Overlaps(a, t)
                                           && t.At.Y >= lo - 1e-4 && t.At.Y + HeightOf(t) <= hi + 1e-4).ToList();
        if (between.Count == 0) return ([from, to], bore);
        // a port on a floor is drawn a little above it, so the rod does not lie half in the glass
        float yA = a.Port(pipe.From.Port, null).Height < bore ? from.Y + bore * 1.3f : from.Y;
        float yB = b.Port(pipe.To.Port, null).Height < bore ? to.Y + bore * 1.3f : to.Y;
        return (UpTheSide(a, b, yA, yB, between.Max(SideOf) / 2, bore, from, enterFrom: false, out _), bore);
    }

    /// <summary>The route up one side of a column from vessel A at <paramref name="yA"/> to vessel B at <paramref name="yB"/>, past vessels half <paramref name="passing"/> wide.</summary>
    private List<Vector3> UpTheSide(PartSpec a, PartSpec b, float yA, float yB, float passing, float bore, Vector3 axis, bool enterFrom, out float laneX)
    {
        int k = _lanes++;
        float dir = k % 2 == 0 ? -1 : 1;
        float reach = passing + bore * 2.4f + 0.01f + (k / 2) * bore * 3.2f;   // a bore and a half clear of the wall, so the pipe stands off the vessel's dark frame
        float x = laneX = axis.X + dir * reach;
        var route = new List<Vector3>();
        if (enterFrom && reach > SideOf(a) / 2 + 1e-3f) route.Add(new Vector3(axis.X + dir * (SideOf(a) / 2 - 0.02f), yA, axis.Z));   // in through A's wall
        route.Add(new Vector3(x, yA, axis.Z));
        route.Add(new Vector3(x, yB, axis.Z));
        if (reach > SideOf(b) / 2 + 1e-3f) route.Add(new Vector3(axis.X + dir * (SideOf(b) / 2 - 0.02f), yB, axis.Z));   // in through B's wall
        return route;
    }

    // ---- 3. air you can see

    private readonly List<(Tank Tank, PartSpec Spec, MeshInstance3D Box, StandardMaterial3D Mat, float Side)> _airViews = [];
    /// <summary>Where the dial of a pocket's gauge hangs when a tube joins its vessels: beside the tube, level with its middle.</summary>
    private readonly Dictionary<AirPocket, Vector3> _airTubeDials = [];

    private void BuildSealedAir()
    {
        foreach (var part in TankParts)
        {
            var tank = Runtime.Tanks[part.Id];
            if (tank.Air is null) continue;
            _building = part.Id;
            float side = SideOf(part);
            var mat = Shapes.Mat(Skins.Air, roughness: 1f, alpha: 0.3f, outline: false);
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            var box = Shapes.Box(new Vector3(side * 0.96f, 1, side * 0.96f), mat);
            box.Name = "air";
            box.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            AddChild(box);
            _airViews.Add((tank, part, box, mat, side));
        }
        _building = null;
        // the tube between the vessels of a stack that share a pocket: up the side the first pipe did not take
        foreach (var air in Runtime.Def.SealedAir)
        {
            var first = Runtime.Def.Part(air.Tanks[0])!;
            foreach (var other in air.Tanks.Skip(1).Select(t => Runtime.Def.Part(t)!))
            {
                if (Math.Abs(first.At.X - other.At.X) > 1e-4 || Math.Abs(first.At.Z - other.At.Z) > 1e-4 || Math.Abs(first.At.Y - other.At.Y) < 0.02) continue;
                var (lower, upper) = first.At.Y < other.At.Y ? (first, other) : (other, first);
                float bore = ColumnBore(Mathf.Min(SideOf(lower), SideOf(upper)));
                float yLo = (float)lower.At.Y + HeightOf(lower) - bore * 1.8f, yHi = (float)upper.At.Y + HeightOf(upper) - bore * 1.8f;
                float passing = TankParts.Where(t => t.Id != lower.Id && t.Id != upper.Id && Overlaps(lower, t)
                                                     && t.At.Y >= lower.At.Y - 1e-4 && t.At.Y + HeightOf(t) <= upper.At.Y + HeightOf(upper) + 1e-4)
                                         .Select(t => SideOf(t) / 2).Append(SideOf(lower) / 2).Append(SideOf(upper) / 2).Max();
                var route = UpTheSide(lower, upper, yLo, yHi, passing, bore, V(lower.At), enterFrom: true, out float laneX);
                var tube = Shapes.Mat(Skins.Air, roughness: 0.5f);
                _building = lower.Id;
                float dialRadius = Mathf.Clamp(SideOf(first) * 0.32f, 0.04f, 0.2f);
                _airTubeDials[Runtime.Tanks[first.Id].Air!] = new Vector3(laneX + Mathf.Sign(laneX - (float)lower.At.X) * (bore + 0.006f + dialRadius), (yLo + yHi) / 2, (float)lower.At.Z);
                for (int i = 0; i + 1 < route.Count; i++)
                {
                    var rod = Shapes.Rod(route[i], route[i + 1], bore, tube);
                    rod.Name = "air-tube";
                    AddChild(rod);
                    if (i > 0) { var elbow = Shapes.Sphere(bore * 1.1f, tube); elbow.Position = route[i]; AddChild(elbow); }
                }
                _building = null;
            }
        }
    }

    private void DrawSealedAir()
    {
        foreach (var (tank, spec, box, mat, _) in _airViews)
        {
            float top = (float)(tank.BaseElevation + tank.Height), surface = (float)tank.BaseElevation + ShownLevel(tank);
            float h = top - surface;
            box.Visible = h > 0.002f;
            box.Scale = new Vector3(1, Mathf.Max(h, 0.001f), 1);
            box.Position = new Vector3((float)spec.At.X, surface + h / 2, (float)spec.At.Z);
            mat.AlbedoColor = new Color(Skins.Air, Skins.AirTint(tank.Air!.GaugePressure));
        }
    }

    // ---- 4. the head rule

    private sealed class HeadView
    {
        public required Tank Basin, Receiver, Supply;
        public required Pipe Jet, Drain;
        public required float AxisX, Z, LeftX, RightX, BasinHalf, ReceiverHalf, SupplyHalf;
        public required StandardMaterial3D FallMat, LiftMat, GuideMat;
        public required MeshInstance3D FallBar, FallLow, FallHigh, FallGuideLow, FallGuideHigh;
        public required MeshInstance3D LiftBar, LiftLow, LiftHigh, LiftGuideLow, LiftGuideHigh, TipTick;
        public required Label3D FallLabel, LiftLabel, TipLabel;
    }

    private readonly List<HeadView> _heads = [];

    private const float BarWidth = 0.012f;

    private static MeshInstance3D Unlit(Node parent, StandardMaterial3D mat, string name)
    {
        var box = Shapes.Box(Vector3.One, mat);
        box.Name = name;
        box.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        parent.AddChild(box);
        return box;
    }

    private static StandardMaterial3D Ink(Color color, float alpha = 1)
    {
        var m = Shapes.Mat(color, roughness: 1, alpha: alpha, outline: false);
        m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        return m;
    }

    private Label3D HeadLabel(Color color)
    {
        var label = new Label3D
        {
            FontSize = 24, OutlineSize = 8, PixelSize = 0.0022f, NoDepthTest = true, Modulate = color,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        AddChild(label);
        return label;
    }

    private void BuildHeadMarks()
    {
        foreach (var jetSpec in Runtime.Def.Pipes.Where(p => p.Jet))
        {
            var jet = Runtime.Pipes[jetSpec.Id];
            var supply = jet.From; var basin = jet.To;
            if (supply.Air is not { } air || basin.Air is not null) continue;
            foreach (var drainSpec in Runtime.Def.Pipes.Where(p => !p.Jet))
            {
                var drain = Runtime.Pipes[drainSpec.Id];
                if (drain.From != basin || drain.To == supply || drain.To.Air != air) continue;
                var receiver = drain.To;
                var supplyPart = Runtime.Def.Part(supply.Name)!; var basinPart = Runtime.Def.Part(basin.Name)!; var receiverPart = Runtime.Def.Part(receiver.Name)!;
                _building = jetSpec.Id;
                float reach = SideOf(basinPart) / 2 + 0.13f;
                var fallMat = Ink(Skins.Head, 0.999f); var liftMat = Ink(Skins.Head); var guideMat = Ink(Skins.Head, 0.6f); var tipMat = Ink(Skins.Watch);
                var view = new HeadView
                {
                    Basin = basin, Receiver = receiver, Supply = supply, Jet = jet, Drain = drain,
                    AxisX = (float)supplyPart.At.X, Z = (float)supplyPart.At.Z,
                    LeftX = (float)supplyPart.At.X - reach, RightX = (float)supplyPart.At.X + reach,
                    BasinHalf = SideOf(basinPart) / 2, ReceiverHalf = SideOf(receiverPart) / 2, SupplyHalf = SideOf(supplyPart) / 2,
                    FallMat = fallMat, LiftMat = liftMat, GuideMat = guideMat,
                    FallBar = Unlit(this, fallMat, "falls"), FallLow = Unlit(this, fallMat, "falls-low"), FallHigh = Unlit(this, fallMat, "falls-high"),
                    FallGuideLow = Unlit(this, guideMat, "falls-guide-low"), FallGuideHigh = Unlit(this, guideMat, "falls-guide-high"),
                    LiftBar = Unlit(this, liftMat, "lifts"), LiftLow = Unlit(this, liftMat, "lifts-low"), LiftHigh = Unlit(this, liftMat, "lifts-high"),
                    LiftGuideLow = Unlit(this, guideMat, "lifts-guide-low"), LiftGuideHigh = Unlit(this, guideMat, "lifts-guide-high"),
                    TipTick = Unlit(this, tipMat, "nozzle-tip"),
                    FallLabel = HeadLabel(Skins.Head), LiftLabel = HeadLabel(Skins.Head), TipLabel = HeadLabel(Skins.Watch),
                };
                view.TipLabel.Text = "nozzle";
                _heads.Add(view);
                _building = null;
                break;
            }
        }
    }

    /// <summary>A box from <paramref name="a"/> to <paramref name="b"/>, axis-aligned (a bar or a level line), <paramref name="thick"/> across.</summary>
    private static void Span(MeshInstance3D box, Vector3 a, Vector3 b, float thick)
    {
        var d = b - a;
        box.Position = (a + b) / 2;
        box.Scale = new Vector3(Mathf.Max(Mathf.Abs(d.X), thick), Mathf.Max(Mathf.Abs(d.Y), thick), thick);
    }

    private void DrawHeadMarks()
    {
        foreach (var h in _heads)
        {
            float z = h.Z;
            float basinTop = (float)h.Basin.BaseElevation + ShownLevel(h.Basin);
            float receiverTop = (float)h.Receiver.BaseElevation + ShownLevel(h.Receiver);
            float supplyTop = (float)h.Supply.BaseElevation + ShownLevel(h.Supply);
            float fall = Mathf.Max(0, basinTop - receiverTop);
            float lift = (float)h.Supply.Zone.PressureToHead(h.Supply.SurfaceGaugePressure + h.Supply.ZoneOverpressure);   // the air's pressure head: how far above the supply's surface it can hold water
            float tip = (float)h.Jet.ToPortElevation;

            // falls: the open tank's surface down to the receiver's, drawn beside the column on the left
            Span(h.FallBar, new Vector3(h.LeftX, receiverTop, z), new Vector3(h.LeftX, basinTop, z), BarWidth);
            Span(h.FallLow, new Vector3(h.LeftX - 0.035f, receiverTop, z), new Vector3(h.LeftX + 0.035f, receiverTop, z), BarWidth);
            Span(h.FallHigh, new Vector3(h.LeftX - 0.035f, basinTop, z), new Vector3(h.LeftX + 0.035f, basinTop, z), BarWidth);
            Span(h.FallGuideLow, new Vector3(h.LeftX, receiverTop, z), new Vector3(h.AxisX - h.ReceiverHalf, receiverTop, z), 0.004f);
            Span(h.FallGuideHigh, new Vector3(h.LeftX, basinTop, z), new Vector3(h.AxisX - h.BasinHalf, basinTop, z), 0.004f);
            // lifts: from the supply's surface up by the pressure head, on the right; its top is the jet's top
            Span(h.LiftBar, new Vector3(h.RightX, supplyTop, z), new Vector3(h.RightX, supplyTop + lift, z), BarWidth);
            Span(h.LiftLow, new Vector3(h.RightX - 0.035f, supplyTop, z), new Vector3(h.RightX + 0.035f, supplyTop, z), BarWidth);
            Span(h.LiftHigh, new Vector3(h.RightX - 0.035f, supplyTop + lift, z), new Vector3(h.RightX + 0.035f, supplyTop + lift, z), BarWidth);
            Span(h.LiftGuideLow, new Vector3(h.RightX, supplyTop, z), new Vector3(h.AxisX + h.SupplyHalf, supplyTop, z), 0.004f);
            Span(h.LiftGuideHigh, new Vector3(h.RightX, supplyTop + lift, z), new Vector3(h.AxisX, supplyTop + lift, z), 0.004f);
            Span(h.TipTick, new Vector3(h.RightX - 0.045f, tip, z), new Vector3(h.RightX + 0.045f, tip, z), BarWidth * 0.8f);

            bool started = Runtime.Time > 0;
            bool drains = h.Drain.Flow > 1e-9 && !Blocked(h.Drain);
            string why = !started || drains ? "" : h.Receiver.WaterVolume >= h.Receiver.Capacity - 1e-9 ? " (receiver full)" : h.Basin.WaterVolume <= 1e-9 ? " (basin empty)" : " (levels met)";
            h.FallMat.AlbedoColor = new Color(Skins.Head, why == "" ? 1f : 0.4f);
            h.FallLabel.Modulate = new Color(Skins.Head, why == "" ? 1f : 0.55f);
            h.FallLabel.Text = $"falls {fall * 100:0} cm{why}";
            h.FallLabel.Position = new Vector3(h.LeftX, (receiverTop + basinTop) / 2, z);
            h.LiftLabel.Text = $"lifts ≈ {lift * 100:0} cm";
            h.LiftLabel.Position = new Vector3(h.RightX, supplyTop + lift / 2, z);
            h.TipLabel.Position = new Vector3(h.RightX + 0.1f, tip, z);
            h.TipLabel.Visible = lift < tip - supplyTop + 0.01f || h.Jet.Flow <= 0;   // named only when it matters: the lift is short of it
        }
    }
}
