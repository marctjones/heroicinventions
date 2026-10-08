using System.Globalization;
using System.Text;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// Writes a <c>#lang heroic</c> source file for whatever a BuildSession
/// document contains — every part kind the palette places, plus the pipes,
/// connections, ropes, arbors, meshes, lifts, water inflows, channels and
/// cylinders — so a design made by pointing and clicking can be kept, edited
/// and rebuilt as Racket.
///
/// A catalogue part (wheel/screw/fixture) carries a generated shape's
/// numbers — volume, inertia — which can't be turned back into a generator
/// call. So the editor records which catalogue entry it came from (the
/// "catalogue" prop) and the exporter writes <c>#:shape (catalogue-shape
/// 'entry-id)</c>, which Racket regenerates exactly. A part with no such
/// record (loaded from a hand-written machine) is left as a commented
/// placeholder naming what it was.
/// </summary>
public static class RktExporter
{
    public static string Write(MachineDef m)
    {
        var sb = new StringBuilder();
        sb.Append("#lang heroic\n");
        sb.Append(";; Exported by the in-game editor's BuildSession (racket/build.rkt will\n");
        sb.Append(";; overwrite this .machine file the next time it runs from this source).\n");
        sb.Append($"(define-machine {m.Name}\n");
        if (m.Source is { } src) sb.Append($"  #:source {Quote(src)}\n");
        if (!m.Planet.IsEarth) sb.Append($"  #:planet {PlanetExpr(m.Planet)}\n");
        // unsaid, a scene takes its planet's air; a scene on the daily curve says none (saying one would hold it still)
        if (m.Ambient != m.Planet.Temperature && m.Weather is not { Daily: true }) sb.Append($"  #:ambient {F(m.Ambient)}\n");
        if (m.Weather is { } wx)
            sb.Append($"  #:weather (weather #:daily {(wx.Daily ? "#t" : "#f")} #:passes '({string.Join(' ', wx.Passes.Select(F))}) #:pass-minutes {F(wx.PassMinutes)}" +
                      (wx.Storms.Count > 0 ? $" #:storms (list {string.Join(' ', wx.Storms.Select(s => $"(storm #:sol {s.Sol} #:hour {F(s.Hour)} #:tau {F(s.Tau)} #:sols {F(s.Sols)} #:settle {F(s.Settle)})"))})" : "") + ")\n");
        if (m.Sun is { } sun) sb.Append($"  #:latitude {F(sun.Latitude)} #:day {sun.Day} #:time {F(sun.Time)}\n");

        foreach (var p in m.Parts) sb.Append(PartClause(p));
        foreach (var p in m.Pipes)
            sb.Append($"  (pipe {p.Id} {p.From.Part}.{p.From.Port} {p.To.Part}.{p.To.Port} #:conductance {F(p.Conductance)}{(p.Jet ? " #:jet #t" : "")})\n");
        foreach (var c in m.Connects)
            sb.Append($"  (connect {c.A.Part}.{c.A.Port} {c.B.Part}.{c.B.Port})\n");
        foreach (var a in m.SealedAir)
            sb.Append($"  (sealed-air ({string.Join(' ', a.Tanks)}) #:tube {F(a.TubeVolume)}" +
                      (a.HeatLoss > 0 ? $" #:heat-loss {F(a.HeatLoss)}" : "") +
                      (a.HeatCapacity > 0 ? $" #:heat-capacity {F(a.HeatCapacity)}" : "") + ")\n");
        foreach (var r in m.Ropes) sb.Append(RopeClause(r));
        foreach (var a in m.Arbors) sb.Append($"  (arbor {string.Join(' ', a.Parts)})\n");
        foreach (var x in m.Meshes) sb.Append(x.Efficiency == 1 ? $"  (mesh {x.A} {x.B})\n" : $"  (mesh {x.A} {x.B} #:efficiency {F(x.Efficiency)})\n");
        foreach (var l in m.Lifts)
            sb.Append($"  (lift {l.Id} #:by {l.By} #:from {l.From} #:to {l.To}" +
                      (l.Current is { } c ? $" #:current {F(c)}" : "") +
                      (l.CurrentFrom is { } cf ? $" #:current-from {cf}" : "") + ")\n");
        foreach (var s in m.Sources) sb.Append($"  (inflow {s.Id} #:into {s.Into} #:flow {F(s.Flow)})\n");
        foreach (var c in m.Channels)
            sb.Append($"  (channel {c.Id} #:from {c.From.Part}.{c.From.Port} " +
                      (c.To is { } to ? $"#:to {to.Part}.{to.Port}" : $"#:to off #:end {Vec(c.End!.Value)}") +
                      (c.Via is { Count: > 0 } via ? $" #:via ({string.Join(" ", via.Select(p => $"({F(p.X)} {F(p.Z)})"))})" : "") +
                      $" #:width {F(c.Width)}" + (c.Length is { } len ? $" #:length {F(len)}" : "") +
                      (c.Onto is { } onto ? $" #:onto {onto}" : "") +
                      (c.Dynamic ? " #:dynamic #t" + (c.Cells is { } cells ? $" #:cells {cells}" : "") : "") + ")\n");
        foreach (var c in m.Cylinders)
            sb.Append($"  ({(c.Kind == "steam" ? "steam-cylinder" : "atmospheric-cylinder")} {c.Id} #:piston {c.Piston} #:steam-from {c.Boiler}{(c.InjectionTemperature is { } inj ? $" #:injection-temperature {F(inj)}" : "")}{(c.Crank is { } crank ? $" #:crank {crank}" : "")})\n");

        foreach (var t in m.Triggers)
            sb.Append($"  (trigger {t.Id}" + (t.At is { } at ? $" #:at {Vec(at)}" : "") + (t.Size is { } sz ? $" #:size {Vec(sz)}" : "") +
                      (t.Body is { } body ? $" #:body {body}" : "") +
                      (t.WatchTarget is { } wt ? $" #:when ({wt} {t.WatchField} {(t.Rising ? "above" : "below")} {F(t.Threshold)})" : "") +
                      $" #:do ({string.Join(' ', t.Actions.Select(a => $"({a.Target} {a.Field} {F(a.Value)})"))}))\n");

        foreach (var w in m.Wakes)
        {
            string T(WakeTerm t) => $"({t.Target} {t.Field} {(t.Above ? "above" : "below")} {F(t.Value)})";
            sb.Append($"  (wake {w.Id} #:when ({string.Join(' ', w.Terms.Select(T))}) #:join {(w.All ? "and" : "or")} #:limit {F(w.Limit)}" +
                      (w.Events.Count > 0 ? $" #:events ({string.Join(' ', w.Events.Select(T))})" : "") + ")\n");
        }
        foreach (var b in m.Belts) sb.Append($"  (belt {b.Id} {b.A} {b.B} #:tension {F(b.Tension)} #:material {b.Material})\n");
        foreach (var f in m.Follows)
            sb.Append($"  (follow {f.Id} {(f.Lever is { } lv ? $"#:lever {lv}" : $"#:rope {f.Rope}")} #:from {F(f.From)} #:to {F(f.To)} " +
                      $"#:set ({f.Target} {f.Field}) #:low {F(f.Low)} #:high {F(f.High)})\n");

        foreach (var j in m.Joints) sb.Append($"  {JointCommand(j, F)}\n");
        if (m.Operator.Count > 0) sb.Append($"  (operator {string.Join(' ', m.Operator.Select(a => $"(at {F(a.At)} ({a.Target} {a.Field} {F(a.Value)}))"))})\n");

        sb.Append(")\n");
        return sb.ToString();
    }

    /// <summary>A joint clause, the same in a .rkt file and as an editor command.</summary>
    public static string JointCommand(JointSpec j, Func<double, string> number) =>
        $"(joint {j.Id} #:kind {j.Kind} #:a {j.A} #:b {j.B} #:at ({number(j.At.X)} {number(j.At.Y)} {number(j.At.Z)})" +
        (j.Axis is { } ax ? $" #:axis ({number(ax.X)} {number(ax.Y)} {number(ax.Z)})" : "") +
        (j.Free.Count > 0 ? $" #:free ({string.Join(' ', j.Free)})" : "") +
        (j.LimitDeg is { } d ? $" #:limit-deg {number(d)}" : "") + ")";

    /// <summary>mars, or (planet mars #:gravity 9.81 …) for a preset with numbers changed.</summary>
    private static string PlanetExpr(Planet p)
    {
        var preset = Planet.Named(p.Id);
        var changes = Planet.NumberKeys.Where(k => p.Number(k) != preset.Number(k)).Select(k => $" #:{k} {F(p.Number(k))}").ToList();
        if (p.Air != preset.Air)
            changes.Add($" #:air '({string.Join(' ', GasMix.Names.Select(g => $"({g} {F(p.Air[g])})"))})");
        return changes.Count == 0 ? p.Id : $"(planet {p.Id}{string.Concat(changes)})";
    }

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string Vec(Vec3 v) => $"({F(v.X)} {F(v.Y)} {F(v.Z)})";
    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string RopeClause(RopeSpec r)
    {
        string End(RopeEnd e) => $"({e.Part} {F(e.Local.X)} {F(e.Local.Y)} {F(e.Local.Z)})";
        var sb = new StringBuilder($"  (rope {r.Id} ");
        sb.Append(r.WindOn is { } drum ? $"#:wind-on {drum} " : $"#:from {End(r.From)} ");
        sb.Append($"#:to {End(r.To)} #:length {F(r.Length)}");
        if (r.Over.Count > 0) sb.Append($" #:over ({string.Join(' ', r.Over.Select(Vec))})");
        if (r.ReleaseDeg is { } rel) sb.Append($" #:release-deg {F(rel)}");
        sb.Append($" #:diameter {F(r.Diameter)} #:material {r.Material}");
        if (r.Nocked) sb.Append(" #:nocked #t");
        if (r.Turns is { } t) sb.Append($" #:turns {t}");
        if (r.Bar is { } bar) sb.Append($" #:bar {bar}");
        if (r.Mu is { } mu) sb.Append($" #:mu {F(mu)}");
        if (r.Links is { } links) sb.Append($" #:links {links}");
        return sb.Append(")\n").ToString();
    }

    private static string PartClause(PartSpec p)
    {
        double N(string key, double fallback = 0) => p.Props.TryGetValue(key, out var v) && v is SNumber n ? n.Value : fallback;
        // an optional number that may be #f (no value): omitted when absent
        string Opt(string kw, string key) => p.Props.TryGetValue(key, out var v) && v is SNumber n ? $" #:{kw} {F(n.Value)}" : "";
        string Sym(string key, string fallback) => p.Props.TryGetValue(key, out var v) && v is SSymbol s ? s.Name : fallback;
        // the four bearing clauses, once a #:bearing-radius is set (a pendulum's pin, a wheel's or lever's axle)
        string Bearing() => p.Props.GetValueOrDefault("bearing-radius") is SNumber pin
            ? $" #:bearing-radius {F(pin.Value)} #:bearing-mu {F(N("bearing-mu"))} #:bearing-drag {F(N("bearing-drag"))} #:bearing-wear {F(N("bearing-wear"))}"
            : "";
        // a part turned about the vertical (issue #83): its #:heading-deg, only once it has one
        string Heading() => N("heading-deg") != 0 ? $" #:heading-deg {F(N("heading-deg"))}" : "";
        string At() => $"#:at {Vec(p.At)}";
        string Mat() => $"#:material {p.Material}";

        switch (p.Kind)
        {
            case "tank":
                return $"  (tank {p.Id} {At()} #:area {F(N("area"))} #:height {F(N("height"))} #:water {F(N("water"))} {Mat()}\n" +
                       string.Concat(p.Ports.Select(pt => $"       (port {pt.Name} #:height {F(pt.Height)})\n")) + "  )\n";
            case "boiler":
                return $"  (boiler {p.Id} {At()} #:radius {F(N("radius"))} #:height {F(N("height"))} #:water {F(N("water"))} " +
                       $"#:fire {F(N("fire"))} #:temperature {F(N("temperature", 20))}" +
                       (N("burst") > 0 ? $" #:burst {F(N("burst"))}" : "") +
                       (N("wall") > 0 ? $" #:wall {F(N("wall"))}" : "") + $" {Mat()})\n";
            case "rotor":
                return $"  (rotor {p.Id} {At()} #:radius {F(N("radius"))} {Mat()} #:bore {F(N("bore"))} #:arm {F(N("arm"))} " +
                       $"#:wall {F(N("wall", 0.001))} #:nozzles {F(N("nozzles", 2))})\n";
            case "jetwheel":
                return $"  (jetwheel {p.Id} {At()} #:radius {F(N("radius"))} {Mat()} #:bore {F(N("bore"))} " +
                       $"#:paddles {F(N("paddles", 8))} #:width {F(N("width", 0.03))} #:mass {F(N("mass", 0.5))} #:load {F(N("load"))})\n";
            case "smokejack":
                return $"  (smokejack {p.Id} {At()} #:over {(p.Props.GetValueOrDefault("over") as SSymbol)?.Name ?? "?"} #:radius {F(N("radius"))} {Mat()} " +
                       $"#:vanes {F(N("vanes", 6))} #:width {F(N("width", 0.06))} #:mass {F(N("mass", 0.3))} #:load {F(N("load"))} " +
                       $"#:chimney-height {F(N("chimney-height", 2))} #:chimney-area {F(N("chimney-area", 0.05))})\n";
            case "block":
                return $"  (block {p.Id} {At()} #:size {F(N("size"))} #:tilt-deg {F(N("tilt-deg"))}" +
                       (p.Props.ContainsKey("dim-x") ? $" #:dimensions ({F(N("dim-x"))} {F(N("dim-y"))} {F(N("dim-z"))})" : "") +
                       (p.Props.GetValueOrDefault("fast") is SBool { Value: false } ? " #:fast #f" : "") + Opt("drag-coefficient", "drag-coefficient") + Heading() + $" {Mat()})\n";
            case "pendulum":
                return $"  (pendulum {p.Id} {At()} #:length {F(N("length"))} #:start-angle-deg {F(N("start-angle-deg"))} {Mat()}" +
                       Bearing() + Heading() + ")\n";
            case "lever":
                return $"  (lever {p.Id} {At()} #:length {F(N("length"))} {Mat()} #:axis {Sym("axis", "z")} " +
                       $"#:start-angle-deg {F(N("start-angle-deg"))} #:pivot-fraction {F(N("pivot-fraction", 0.5))} " +
                       $"#:limit-deg {F(N("limit-deg", 18))} #:damping {F(N("damping", 8))}" +
                       Opt("limit-lower-deg", "limit-lower-deg") + Opt("limit-upper-deg", "limit-upper-deg") +
                       $" #:spring-stiffness {F(N("spring-stiffness"))} #:spring-rest-deg {F(N("spring-rest-deg"))}" +
                       Opt("section", "section") + Bearing() + Heading() + ")\n";
            case "ramp":
                return $"  (ramp {p.Id} {At()} #:length {F(N("length"))} #:width {F(N("width"))} #:angle-deg {F(N("angle-deg"))} {Mat()}" + Heading() + ")\n";
            case "piston":
                return $"  (piston {p.Id} {At()} #:bore {F(N("bore"))} #:stroke {F(N("stroke"))} {Mat()} " +
                       $"#:start {F(N("start"))} #:rod-mass {F(N("rod-mass"))})\n";
            case "sluice-box":
                return $"  (sluice-box {p.Id} {At()} #:on {Sym("on", "?")} #:feed {F(N("feed"))} #:grain {F(N("grain"))} #:heavy-density {F(N("heavy-density"))}" +
                       $" #:heavy-fraction {F(N("heavy-fraction"))} #:light-density {F(N("light-density", 2650))} {Mat()})\n";
            case "float":
                return $"  (float {p.Id} {At()} #:in {Sym("in", "?")} #:mass {F(N("mass"))} #:area {F(N("area"))} #:height {F(N("height", 0.1))} {Mat()})\n";
            case "digger":
                return $"  (digger {p.Id} {At()} #:length {F(N("length"))} #:width {F(N("width"))} #:depth {F(N("depth"))}" +
                       $" #:power {F(N("power", 150))} #:spit {F(N("spit", 0.25))} #:spoil {F(N("spoil", 5))} {Mat()})\n";
            case "post":
                return $"  (post {p.Id} {At()} #:size ({F(N("size-x"))} {F(N("size-y"))} {F(N("size-z"))}) {Mat()}" +
                       (p.Props.TryGetValue("round", out var rd) && rd is SBool { Value: true } ? " #:round #t" : "") +
                       (p.Props.TryGetValue("breakable", out var bk) && bk is SBool { Value: true } ? " #:breakable #t" : "") + Heading() + ")\n";
            case "hearth":
                return $"  (hearth {p.Id} {At()} #:heats {Sym("heats", "?")} #:power {F(N("power"))} #:fuel {F(N("fuel"))} " +
                       $"#:fuel-kind {Sym("fuel-kind", "wood")} #:efficiency {F(N("efficiency", 0.5))})\n";
            case "mirror":
                return $"  (mirror {p.Id} {At()} #:area {F(N("area"))} #:onto {Sym("onto", "?")} #:reflectivity {F(N("reflectivity", 0.85))} {Mat()})\n";
            case "capstan":
                return $"  (capstan {p.Id} {At()} #:turns {F(N("turns"))} #:load {F(N("load"))} #:hold {F(N("hold"))} #:drop {F(N("drop", 1))} " +
                       $"#:radius {F(N("radius", 0.15))}" + Opt("mu", "mu") + $" #:rope {Sym("rope", "hemp")} {Mat()})\n";
            case "windmill":
                return $"  (windmill {p.Id} {At()} #:radius {F(N("radius"))} #:mass {F(N("mass"))} #:wind {F(N("wind"))}" +
                       (p.Props.GetValueOrDefault("wind-from-map") is SBool { Value: true } ? " #:wind-from-map #t" : "") + $" #:load {F(N("load"))} " +
                       $"#:cp {F(N("cp", 0.3))} #:tip-speed-ratio {F(N("tip-speed-ratio", 2.5))} {Mat()})\n";
            case "ball":
                return $"  (ball {p.Id} {At()} #:radius {F(N("radius", 0.05))}" + Opt("drag-coefficient", "drag-coefficient") + Heading() + $" {Mat()})\n";
            case "hopper":
                return $"  (hopper {p.Id} {At()} #:area {F(N("area", 0.01))} #:grain {F(N("grain", 5))} #:orifice {F(N("orifice", 0.01))} " +
                       $"#:grain-size {F(N("grain-size", 0.0003))} #:density {F(N("density", 1600))} {Mat()})\n";
            case "ratchet":
                return $"  (ratchet {p.Id} {At()} #:on {Sym("on", "?")} #:teeth {F(N("teeth", 12))}" +
                       (N("radius") > 0 ? $" #:radius {F(N("radius"))}" : "") +
                       (p.Props.GetValueOrDefault("reverse") is SBool { Value: true } ? " #:reverse #t" : "") + $" {Mat()})\n";
            case "cam":
                return $"  (cam {p.Id} {At()} #:on {Sym("on", "?")} #:pegs {F(N("pegs", 4))} #:lift {F(N("lift", 0.1))} #:rise {F(N("rise", 0.5))} #:mass {F(N("mass", 5))} {Mat()})\n";
            case "grip":
                return $"  (grip {p.Id} {At()} #:on {Sym("on", "world")} #:kind {Sym("kind", "tongs")} #:reach {F(N("reach", 0.15))} " +
                       $"#:force {F(N("force"))} #:strength {F(N("strength"))} #:closed {F(N("closed"))} {Mat()})\n";
            case "bellows":
                return $"  (bellows {p.Id} {At()} #:on {Sym("on", "?")} #:airflow {F(N("airflow"))} {Mat()})\n";
            case "sluice":
                return $"  (sluice {p.Id} {At()} #:on {Sym("on", "?")} #:height {F(N("height"))} #:opening {F(N("opening", 1))}" +
                       Opt("width", "width") + $" {Mat()})\n";
            case "float-valve":
                return $"  (float-valve {p.Id} {At()} #:on {Sym("on", "?")} #:shut {F(N("shut"))} #:travel {F(N("travel"))} {Mat()})\n";
            case "leak":
                return $"  (leak {p.Id} {At()} #:on {Sym("on", "?")} #:height {F(N("height"))} #:area {F(N("area"))} #:coefficient {F(N("coefficient", 0.6))}" +
                       (p.Props.GetValueOrDefault("into") is SSymbol into ? $" #:into {into.Name}" : "") +
                       (N("evaporation") > 0 ? $" #:evaporation {F(N("evaporation"))}" : "") +
                       (N("bore") > 0 ? $" #:bore {F(N("bore"))} #:lift {F(N("lift"))}" : "") + $" {Mat()})\n";
            case "safety-valve":
                return $"  (safety-valve {p.Id} {At()} #:on {Sym("on", "?")} #:lift {F(N("lift"))} #:bore {F(N("bore"))} " +
                       $"#:coefficient {F(N("coefficient", 0.8))} #:accumulation {F(N("accumulation", 0.1))} {Mat()})\n";
            case "pump":
                return $"  (pump {p.Id} {At()} #:from {Sym("from", "?")} #:to {Sym("to", "?")} #:bore {F(N("bore"))} #:stroke {F(N("stroke"))} " +
                       $"#:rpm {F(N("rpm"))} #:efficiency {F(N("efficiency", 0.8))}" + Opt("force", "force") +
                       Opt("temperature", "temperature") + $" {Mat()})\n";
            case "enclosure":
            {
                var gases = GasMix.Names.Where(g => p.Props.GetValueOrDefault(g) is SNumber).ToList();
                return $"  (enclosure {p.Id} {At()} #:size ({F(N("size-x"))} {F(N("size-y"))} {F(N("size-z"))})" +
                       Opt("pressure", "pressure") + Opt("temperature", "temperature") +
                       (gases.Count > 0 ? $" #:air '({string.Join(' ', gases.Select(g => $"({g} {F(N(g))})"))})" : "") +
                       $" #:insulation {F(N("insulation", 2))} #:heat-capacity {F(N("heat-capacity"))} #:heater {F(N("heater"))}" +
                       $" #:leak {F(N("leak"))} #:supply {F(N("supply"))} #:coefficient {F(N("coefficient", 0.6))}" +
                       (p.Props.GetValueOrDefault("wall") is SSymbol wall ? $" #:wall {wall.Name} #:wall-thickness {F(N("wall-thickness", 0.5))}" : "") +
                       Opt("ground", "ground") + Opt("emissivity", "emissivity") + $" {Mat()})\n";
            }
            case "door":
                return $"  (door {p.Id} {At()} #:from {Sym("from", "?")} #:to {Sym("to", "?")} #:area {F(N("area"))} #:open {F(N("open"))} " +
                       $"#:coefficient {F(N("coefficient", 0.6))} {Mat()})\n";
            case "air-pump":
                return $"  (air-pump {p.Id} {At()} #:from {Sym("from", "?")} #:to {Sym("to", "?")} #:speed {F(N("speed"))} #:until {F(N("until"))} {Mat()})\n";
            case "stirling":
                return $"  (stirling {p.Id} {At()} #:aperture {F(N("aperture"))} #:conductance {F(N("conductance"))} " +
                       $"#:carnot-fraction {F(N("carnot-fraction", 0.35))} #:emissivity {F(N("emissivity", 0.9))} " +
                       $"#:heat-capacity {F(N("heat-capacity", 10000))} #:inertia {F(N("inertia", 0.5))} #:load {F(N("load"))}" +
                       Opt("temperature", "temperature") + $" {Mat()})\n";
            case "envelope":
                return $"  (envelope {p.Id} {At()} #:volume {F(N("volume", 1))} #:envelope-mass {F(N("envelope-mass", 0.05))} #:burner-mass {F(N("burner-mass"))} " +
                       $"#:burner-power {F(N("burner-power"))} #:fuel {F(N("fuel"))} #:fuel-energy {F(N("fuel-energy", 40000000))} #:skin-conductance {F(N("skin-conductance"))}" +
                       Opt("temperature", "temperature") + Opt("height", "height") + $" #:drag-coefficient {F(N("drag-coefficient", 0.8))} {Mat()})\n";
            case "crucible":
                return $"  (crucible {p.Id} {At()} #:sand {Sym("sand", "basalt")} #:charge {F(N("charge"))} #:spot {F(N("spot"))} " +
                       $"#:emissivity {F(N("emissivity", 0.9))}" + Opt("temperature", "temperature") + $" {Mat()})\n";
            case "heat-store":
                return $"  (heat-store {p.Id} {At()} #:mass {F(N("mass", 10))} #:contents {Sym("contents", "basalt")}" + Opt("temperature", "temperature") + Opt("area", "area") +
                       $" #:emissivity {F(N("emissivity", 0.9))} #:conductance {F(N("conductance"))} {Mat()})\n";
            case "heat-bin":
                return $"  (heat-bin {p.Id} {At()} #:holds {Sym("holds", "?")} #:leak {F(N("leak", 0.1))} #:open {F(N("open"))}" +
                       (p.Props.GetValueOrDefault("sense") is SSymbol sense ? $" #:sense {sense.Name} #:open-below {F(N("open-below", 5))} #:close-above {F(N("close-above", 40))}" : "") +
                       $" {Mat()})\n";
            case "bimetal":
                return $"  (bimetal {p.Id} {At()} #:senses {Sym("senses", "?")} #:drives {Sym("drives", "?")} #:layers ({Sym("high", "brass")} {Sym("low", "steel")}) " +
                       $"#:length {F(N("length", 0.1))} #:thickness {F(N("thickness", 0.001))} #:width {F(N("width", 0.01))} #:high-share {F(N("high-share", 0.5))} " +
                       $"#:shut-at {F(N("shut-at", 40))} #:straight-at {F(N("straight-at", 20))} #:travel {F(N("travel", 0.0021))} #:contact {F(N("contact", 10))} {Mat()})\n";
            case "generator":
                return $"  (generator {p.Id} {At()} #:on {Sym("on", "?")} #:charges {Sym("charges", "?")} #:efficiency {F(N("efficiency", 0.8))} #:cut-in-rpm {F(N("cut-in-rpm", 1500))} " +
                       $"#:rated-rpm {F(N("rated-rpm", 2500))} #:rated-torque {F(N("rated-torque", 12))}" +
                       (p.Props.GetValueOrDefault("driven-by") is SSymbol drv ? $" #:driven-by {drv.Name}" : "") + $" {Mat()})\n";
            case "battery-bank":
                return $"  (battery-bank {p.Id} {At()} #:in {Sym("in", "?")} #:capacity {F(N("capacity", 4000))} #:charge {F(N("charge"))} #:volts {F(N("volts", 28))}" +
                       (p.Props.GetValueOrDefault("call-hour") is SNumber ch ? $" #:call-hour {F(ch.Value)}" : "") +
                       (p.Props.GetValueOrDefault("call-minutes") is SNumber cm ? $" #:call-minutes {F(cm.Value)}" : "") +
                       (p.Props.GetValueOrDefault("call-any-time") is SBool { Value: true } ? " #:call-any-time #t" : "") + $" {Mat()})\n";
            case "burning-mirror":
                return $"  (burning-mirror {p.Id} {At()} #:area {F(N("area"))} #:image {F(N("image"))} #:onto {Sym("onto", "?")} #:reflectivity {F(N("reflectivity", 0.85))} {Mat()})\n";
            case "pane":
                return $"  (pane {p.Id} {At()} #:on {Sym("on", "?")} #:side {F(N("side"))} #:thickness {F(N("thickness"))} #:count {F(N("count", 1))} " +
                       $"#:glass {Sym("glass", "silica")} #:facing {Sym("facing", "up")} #:strength {F(N("strength", 7e6))})\n";
            case "pond":
                return $"  (pond {p.Id} {At()} #:on {Sym("on", "?")} #:heater {F(N("heater"))}" + Opt("temperature", "temperature") +
                       $" #:coefficient {F(N("coefficient", 3.6e-8))})\n";
            case "drain":
                return $"  (drain {p.Id} {At()} #:into {Sym("into", "?")} #:perimeter {F(N("perimeter", 0.4))} {Mat()})\n";
            case "roof":
                return $"  (roof {p.Id} {At()} #:on {Sym("on", "?")} #:conductance {F(N("conductance"))}" +
                       (p.Props.GetValueOrDefault("gutter") is SSymbol gutter ? $" #:gutter {gutter.Name}" : "") + $" {Mat()})\n";
            case "plants":
                return $"  (plants {p.Id} {At()} #:area {F(N("area"))} #:water {Sym("water", "?")} #:efficiency {F(N("efficiency", 0.005))} " +
                       $"#:respiration {F(N("respiration"))} #:wood {F(N("wood"))}" +
                       (p.Props.GetValueOrDefault("store") is SSymbol store ? $" #:store {store.Name}" : "") + ")\n";
            case "melter":
                return $"  (melter {p.Id} {At()} #:into {Sym("into", "?")} #:power {F(N("power"))}" + Opt("ice-temperature", "ice-temperature") + $" {Mat()})\n";
            case "electrolyser":
                return $"  (electrolyser {p.Id} {At()} #:water {Sym("water", "?")} #:power {F(N("power"))} #:efficiency {F(N("efficiency", 0.7))} {Mat()})\n";
            case "galvanic-jar":
                return $"  (galvanic-jar {p.Id} {At()} #:cells {F(N("cells", 1))} #:volts {F(N("volts", 0.5))} #:milliamps {F(N("milliamps", 0.15))} " +
                       $"#:electrolyte {F(N("electrolyte", 4.5e-5))} #:on {(N("on", 1) != 0 ? "#t" : "#f")} {Mat()})\n";
            case "counterpoise":
                return $"  (counterpoise {p.Id} {At()} #:vessel {Sym("vessel", "?")} #:vessel-mass {F(N("vessel-mass"))} " +
                       $"#:counterweight {F(N("counterweight"))} #:radius {F(N("radius"))} #:turn-deg {F(N("turn-deg"))} " +
                       $"#:friction {F(N("friction"))} #:leaf-inertia {F(N("leaf-inertia"))} " +
                       $"#:leaf ({F(N("leaf-width", 1))} {F(N("leaf-height", 2))}) {Mat()})\n";
            case "waterwheel":
                return $"  (waterwheel {p.Id} {At()} #:radius {F(N("radius"))} #:width {F(N("width"))} #:mass {F(N("mass"))} #:load {F(N("load"))}" +
                       (N("buckets") > 0 ? $" #:buckets {F(N("buckets"))} #:bucket-volume {F(N("bucket-volume"))} #:spill-deg {F(N("spill-deg", 120))}" : "") +
                       (Sym("tail", "") is { Length: > 0 } tail ? $" #:tail {tail}" : "") +
                       (Sym("race", "") is { Length: > 0 } race ? $" #:race {race} #:paddle-depth {F(N("paddle-depth"))}" : "") +
                       Bearing() + $" {Mat()})\n";
            case "wheel":
            case "screw":
            case "fixture":
                if (Sym("catalogue", "") is not { Length: > 0 } entry) break;
                string extras = p.Kind switch
                {
                    "wheel" => $" #:axis {Sym("axis", "z")} #:angle-deg {F(N("angle-deg"))}" + Opt("tilt-deg", "tilt-deg") +
                                (p.Props.TryGetValue("on", out var on) && on is SSymbol chassis ? $" #:on {chassis.Name}" : "") +
                                Opt("rolling-resistance", "rolling-resistance") + Opt("grind-torque", "grind-torque") + Opt("yield", "yield") + $" #:drive-rpm {F(N("drive-rpm"))}" + Opt("drive-torque", "drive-torque") +
                                (N("start-rpm") != 0 ? $" #:start-rpm {F(N("start-rpm"))}" : "") + Bearing() + Heading(),
                    "screw" => $" #:tilt-deg {F(N("tilt-deg"))} #:drive-rpm {F(N("drive-rpm"))}" + Opt("drive-torque", "drive-torque") + Heading(),
                    _ => $" #:turn-deg {F(N("turn-deg"))}" + Heading(),
                };
                return $"  ({p.Kind} {p.Id} #:shape (catalogue-shape '{entry}) {At()} {Mat()}{extras})\n";
        }
        return $"  ;; {p.Id}: a {p.Kind} (shape {Sym("shape", "?")}) that wasn't placed from the catalogue can't be exported to Racket source —\n" +
               $"  ;; re-place it by hand: ({p.Kind} {p.Id} #:shape (...) {At()} {Mat()})\n";
    }
}
