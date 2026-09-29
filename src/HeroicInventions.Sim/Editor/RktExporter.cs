using System.Text;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// Writes a <c>#lang heroic</c> source file for the primitive part kinds
/// (tank, boiler, block, pendulum, lever, ramp, piston) a BuildSession
/// document contains, so a player's design can be handed to a person who
/// wants to keep editing it as Racket, or rebuilt by racket/build.rkt.
///
/// Wheel/screw/fixture parts (placed from the M4 catalogue) carry a
/// generated shape's numbers — volume, inertia, a gear's tooth count — that
/// only Racket's geometry code can produce from a `(spur-gear #:teeth ...)`
/// expression; reconstructing that expression from the numbers alone isn't
/// generally possible (many shapes can share a volume), so this exporter
/// leaves each one as a commented placeholder naming the catalogue entry it
/// came from, rather than guess at a shape call that might not reproduce it.
/// A design with no catalogue parts round-trips through Racket exactly.
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

        foreach (var p in m.Parts) sb.Append(PartClause(p));
        foreach (var p in m.Pipes)
            sb.Append($"  (pipe {p.Id} {p.From.Part}.{p.From.Port} {p.To.Part}.{p.To.Port} #:conductance {p.Conductance}{(p.Jet ? " #:jet #t" : "")})\n");
        foreach (var c in m.Connects)
            sb.Append($"  (connect {c.A.Part}.{c.A.Port} {c.B.Part}.{c.B.Port})\n");
        foreach (var a in m.SealedAir)
            sb.Append($"  (sealed-air ({string.Join(' ', a.Tanks)}) #:tube {a.TubeVolume})\n");

        sb.Append(")\n");
        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string PartClause(PartSpec p)
    {
        double N(string key, double fallback = 0) => p.Props.TryGetValue(key, out var v) && v is SNumber n ? n.Value : fallback;
        string At() => $"#:at ({p.At.X} {p.At.Y} {p.At.Z})";
        string Mat() => $"#:material {p.Material}";

        return p.Kind switch
        {
            "tank" => $"  (tank {p.Id} {At()} #:area {N("area")} #:height {N("height")} #:water {N("water")} {Mat()}\n" +
                      string.Concat(p.Ports.Select(pt => $"       (port {pt.Name} #:height {pt.Height})\n")) + "  )\n",
            "boiler" => $"  (boiler {p.Id} {At()} #:radius {N("radius")} #:height {N("height")} #:water {N("water")} " +
                        $"#:fire {N("fire")} #:temperature {N("temperature", 20)} {Mat()})\n",
            "block" => $"  (block {p.Id} {At()} #:size {N("size")} {Mat()})\n",
            "pendulum" => $"  (pendulum {p.Id} {At()} #:length {N("length")} #:start-angle-deg {N("start-angle-deg")} {Mat()})\n",
            "lever" => $"  (lever {p.Id} {At()} #:length {N("length")} #:start-angle-deg {N("start-angle-deg")} {Mat()})\n",
            "ramp" => $"  (ramp {p.Id} {At()} #:length {N("length")} #:width {N("width")} #:angle-deg {N("angle-deg")} {Mat()})\n",
            "piston" => $"  (piston {p.Id} {At()} #:bore {N("bore")} #:stroke {N("stroke")} {Mat()})\n",
            _ => $"  ;; {p.Id}: a {p.Kind} placed from the catalogue (shape {ShapeName(p)}) can't be exported to Racket source —\n" +
                 $"  ;; re-place it from the palette by hand: ({p.Kind} {p.Id} #:shape (...) {At()} {Mat()})\n",
        };
    }

    private static string ShapeName(PartSpec p) =>
        p.Props.TryGetValue("shape", out var v) && v is SSymbol s ? s.Name : "?";
}
