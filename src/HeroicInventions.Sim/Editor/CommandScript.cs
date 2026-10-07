using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// The inverse of running commands: the <see cref="BuildSession"/> command
/// lines that build a given <see cref="MachineDef"/> from an empty document.
/// It is the proof that the editor can make everything the engine can run —
/// a machine rebuilt from its script must equal the original — and it is what
/// a "show me the commands" panel or a saved edit script would print.
///
/// Parts come first, then the links between them, then one <c>set</c> per
/// prop that differs from its template's default and each part's ports — a
/// prop may name a channel or another part, so those must exist when it runs. A part the
/// templates can't reproduce — a Racket-generated gear no catalogue entry
/// describes — is placed with <c>raw-part</c>, its clause verbatim.
/// </summary>
public static class CommandScript
{
    public static IReadOnlyList<string> For(MachineDef m)
    {
        var lines = new List<string>();
        void Add(string s) => lines.Add(s);
        string N(double v) => SExprWriter.Number(v);
        string V(Vec3 v) => $"({N(v.X)} {N(v.Y)} {N(v.Z)})";
        string R(PortRef r) => $"{r.Part}.{r.Port}";

        if (m.Source is { } src) Add($"(source {SExprWriter.Print(new SString(src))})");
        if (!m.Planet.IsEarth) Add(PlanetCommand(m.Planet));
        if (m.Ambient != m.Planet.Temperature) Add($"(ambient {N(m.Ambient)})");   // a planet command sets the air to the planet's
        if (m.Weather is { } wx)
        {
            Add($"(weather #:daily {(wx.Daily ? "#t" : "#f")} #:passes ({string.Join(' ', wx.Passes.Select(N))}) #:pass-minutes {N(wx.PassMinutes)})");
            foreach (var s in wx.Storms)
                Add($"(storm #:sol {s.Sol} #:hour {N(s.Hour)} #:tau {N(s.Tau)} #:sols {N(s.Sols)} #:settle {N(s.Settle)})");
        }
        if (m.Sun is { } sun) Add($"(sun #:latitude {N(sun.Latitude)} #:day {sun.Day} #:time {N(sun.Time)})");

        var afterLinks = new List<string>();
        foreach (var p in m.Parts) Add(PartCommand(p, afterLinks));

        foreach (var p in m.Pipes)
            Add($"(pipe {p.Id} {R(p.From)} {R(p.To)} #:conductance {N(p.Conductance)}{(p.Jet ? " #:jet #t" : "")})");
        foreach (var c in m.Connects) Add($"(connect {R(c.A)} {R(c.B)})");
        foreach (var r in m.Ropes)
        {
            string End(RopeEnd e) => $"({e.Part} {N(e.Local.X)} {N(e.Local.Y)} {N(e.Local.Z)})";
            Add($"(rope {r.Id} #:from {End(r.From)} #:to {End(r.To)} #:length {N(r.Length)}" +
                (r.Over.Count > 0 ? $" #:over ({string.Join(' ', r.Over.Select(V))})" : "") +
                (r.WindOn is { } w ? $" #:wind-on {w}" : "") +
                (r.Turns is { } t ? $" #:turns {t}" : "") +
                (r.Bar is { } bar ? $" #:bar {bar}" : "") +
                (r.Mu is { } mu ? $" #:mu {N(mu)}" : "") +
                (r.ReleaseDeg is { } d ? $" #:release-deg {N(d)}" : "") +
                $" #:material {r.Material} #:diameter {N(r.Diameter)}" +
                (r.Nocked ? " #:nocked #t" : "") +
                (r.Links is { } links ? $" #:links {links}" : "") + ")");
        }
        foreach (var s in m.Sources) Add($"(inflow {s.Id} #:into {s.Into} #:flow {N(s.Flow)})");
        foreach (var c in m.Channels)
            Add($"(channel {c.Id} {R(c.From)} {(c.To is { } to ? R(to) : "off")}" +
                (c.End is { } e ? $" #:end {V(e)}" : "") +
                (c.Via is { Count: > 0 } via ? $" #:via ({string.Join(' ', via.Select(p => $"({N(p.X)} {N(p.Z)})"))})" : "") +
                $" #:width {N(c.Width)}" + (c.Length is { } l ? $" #:length {N(l)}" : "") +
                (c.Onto is { } onto ? $" #:onto {onto}" : "") +
                (c.Dynamic ? " #:dynamic #t" + (c.Cells is { } cells ? $" #:cells {cells}" : "") : "") + ")");
        foreach (var a in m.SealedAir)
            Add($"(sealed-air ({string.Join(' ', a.Tanks)}) #:tube {N(a.TubeVolume)}" +
                (a.HeatLoss != 0 ? $" #:heat-loss {N(a.HeatLoss)}" : "") +
                (a.HeatCapacity != 0 ? $" #:heat-capacity {N(a.HeatCapacity)}" : "") + ")");
        foreach (var a in m.Arbors) Add($"(arbor {string.Join(' ', a.Parts)})");
        foreach (var g in m.Meshes) Add(g.Efficiency == 1 ? $"(mesh {g.A} {g.B})" : $"(mesh {g.A} {g.B} #:efficiency {N(g.Efficiency)})");
        foreach (var c in m.Cylinders)
            Add($"({(c.Kind == "steam" ? "steam-cylinder" : "atmospheric-cylinder")} {c.Id} #:piston {c.Piston} #:steam-from {c.Boiler}" +
                (c.InjectionTemperature is { } inj ? $" #:injection-temperature {N(inj)}" : "") +
                (c.Crank is { } crank ? $" #:crank {crank}" : "") + ")");
        foreach (var l in m.Lifts)
            Add($"(lift {l.Id} #:by {l.By} #:from {l.From} #:to {l.To}" +
                (l.Current is { } cur ? $" #:current {N(cur)}" : "") +
                (l.CurrentFrom is { } cf ? $" #:current-from {cf}" : "") + ")");
        foreach (var t in m.Triggers)
            Add($"(trigger {t.Id}" + (t.At is { } at ? $" #:at {V(at)}" : "") + (t.Size is { } sz ? $" #:size {V(sz)}" : "") +
                (t.Body is { } body ? $" #:body {body}" : "") +
                (t.WatchTarget is { } wt ? $" #:when ({wt} {t.WatchField} {(t.Rising ? "above" : "below")} {N(t.Threshold)})" : "") +
                $" #:do ({string.Join(' ', t.Actions.Select(a => $"({a.Target} {a.Field} {N(a.Value)})"))}))");
        foreach (var w in m.Wakes)
        {
            string T(WakeTerm t) => $"({t.Target} {t.Field} {(t.Above ? "above" : "below")} {N(t.Value)})";
            Add($"(wake {w.Id} #:when ({string.Join(' ', w.Terms.Select(T))}) #:join {(w.All ? "and" : "or")} #:limit {N(w.Limit)} #:events ({string.Join(' ', w.Events.Select(T))}))");
        }
        foreach (var b in m.Belts) Add($"(belt {b.Id} {b.A} {b.B} #:tension {N(b.Tension)} #:material {b.Material})");
        foreach (var f in m.Follows)
            Add($"(follow {f.Id} {(f.Lever is { } lv ? $"#:lever {lv}" : $"#:rope {f.Rope}")} #:from {N(f.From)} #:to {N(f.To)} " +
                $"#:set ({f.Target} {f.Field}) #:low {N(f.Low)} #:high {N(f.High)})");
        foreach (var j in m.Joints) Add(RktExporter.JointCommand(j, N));
        lines.AddRange(afterLinks);
        return lines;
    }

    /// <summary>(planet id #:key value …): the preset and every number that differs from it.</summary>
    public static string PlanetCommand(Planet p)
    {
        var preset = Planet.Named(p.Id);
        var sb = new System.Text.StringBuilder($"(planet {p.Id}");
        foreach (var key in Planet.NumberKeys)
            if (p.Number(key) != preset.Number(key)) sb.Append($" #:{key} {SExprWriter.Number(p.Number(key))}");
        if (p.Air != preset.Air)
            sb.Append($" #:air ({string.Join(' ', GasMix.Names.Select(g => $"({g} {SExprWriter.Number(p.Air[g])})"))})");
        return sb.Append(')').ToString();
    }

    /// <summary>The command that places <paramref name="p"/>; the sets and port changes that finish it go to <paramref name="afterLinks"/>, once every part and link exists.</summary>
    private static string PartCommand(PartSpec p, List<string> afterLinks)
    {
        string Raw() => $"(raw-part {SExprWriter.Print(MachineWriter.PartClause(p))})";
        if (!PartTemplates.PrimitiveKinds.Contains(p.Kind)) return Raw();
        var template = PartTemplates.Create(p.Kind, p.Id, p.At, p.Material);
        // A prop the template has and this part lacks can't be un-set by a command: place the part verbatim.
        if (template.Props.Keys.Any(k => !p.Props.ContainsKey(k))) return Raw();

        foreach (var (key, value) in p.Props)
            if (!template.Props.TryGetValue(key, out var def) || def != value)
                afterLinks.Add($"(set {p.Id} #:{key} {SExprWriter.Print(value)})");
        foreach (var t in template.Ports.Where(t => p.Ports.All(x => x.Name != t.Name)))
            afterLinks.Add($"(remove-port {p.Id} {t.Name})");
        foreach (var port in p.Ports.Where(x => !template.Ports.Contains(x)))
            afterLinks.Add($"(port {p.Id} {port.Name} {port.Kind} {SExprWriter.Number(port.Height)})");
        string n(double v) => SExprWriter.Number(v);
        return $"({p.Kind} {p.Id} #:at ({n(p.At.X)} {n(p.At.Y)} {n(p.At.Z)}) #:material {p.Material})";
    }
}
