namespace HeroicInventions.Sim.Machines;

/// <summary>
/// The inverse of <see cref="MachineDef.Parse"/>: turns a <see cref="MachineDef"/>
/// back into .machine text. This is racket/heroic/emit.rkt's job done in C#, for
/// the in-game editor — it has no Racket to shell out to, and the round trip
/// (load a machine, edit it, save it, reparse) has to work without one.
///
/// The clause order and every null encoding here (an absent channel target
/// written as <c>(to off)</c>, a #f rope release as <c>(release-deg #f)</c>, …)
/// match emit.rkt exactly, so a file this writer produces and a file
/// racket/build.rkt produces from the same machine are interchangeable: either
/// one round-trips through <see cref="MachineDef.Parse"/> the same way.
/// </summary>
public static class MachineWriter
{
    public static void WriteFile(MachineDef m, string path) => File.WriteAllText(path, Write(m));

    /// <summary>The full .machine file text for <paramref name="m"/>, one clause per line.</summary>
    public static string Write(MachineDef m)
    {
        var clauses = new List<SExpr>();
        if (m.Source is { } src) clauses.Add(Tagged("source", new SString(src)));
        if (m.Ambient != 20) clauses.Add(Tagged("ambient", new SNumber(m.Ambient)));
        if (!m.Planet.IsEarth) clauses.Add(m.Planet.ToClause());
        if (m.Weather is { } wx) clauses.Add(WeatherClause(wx));
        if (m.Sun is { } sun)
            clauses.Add(Tagged("sun", Tagged("latitude", Num(sun.Latitude)), Tagged("day", Num(sun.Day)), Tagged("time", Num(sun.Time))));
        foreach (var p in m.Parts) clauses.Add(PartClause(p));
        foreach (var p in m.Pipes) clauses.Add(PipeClause(p));
        foreach (var c in m.Connects) clauses.Add(ConnectClause(c));
        foreach (var r in m.Ropes) clauses.Add(RopeClause(r));
        foreach (var s in m.Sources) clauses.Add(InflowClause(s));
        foreach (var c in m.Channels) clauses.Add(ChannelClause(c));
        foreach (var c in m.Cylinders) clauses.Add(CylinderClause(c));
        foreach (var l in m.Lifts) clauses.Add(LiftClause(l));
        foreach (var g in m.Meshes) clauses.Add(MeshClause(g));
        foreach (var a in m.Arbors) clauses.Add(ArborClause(a));
        foreach (var a in m.SealedAir) clauses.Add(SealedAirClause(a));
        foreach (var t in m.Triggers) clauses.Add(TriggerClause(t));
        foreach (var f in m.Follows) clauses.Add(FollowClause(f));
        foreach (var b in m.Belts) clauses.Add(Tagged("belt", Sym(b.Id), Sym(b.A), Sym(b.B), Tagged("tension", Num(b.Tension)), Tagged("material", Sym(b.Material)), SrcLoc(b.Location)));
        foreach (var j in m.Joints) clauses.Add(JointClause(j));

        var w = new System.Text.StringBuilder();
        w.Append(";; Saved by the in-game editor. Building from Racket will replace this file.\n");
        w.Append($"(machine {m.Name}");
        foreach (var c in clauses) w.Append($"\n  {SExprWriter.Print(c)}");
        w.Append(")\n");
        return w.ToString();
    }

    public static SExpr WeatherClause(WeatherSpec w) =>
        Tagged("weather", [Tagged("daily", new SBool(w.Daily)),
            Tagged("passes", [.. w.Passes.Select(h => (SExpr)Num(h))]),
            Tagged("pass-minutes", Num(w.PassMinutes)),
            .. w.Storms.Select(s => (SExpr)Tagged("storm", Tagged("sol", Num(s.Sol)), Tagged("hour", Num(s.Hour)),
                Tagged("tau", Num(s.Tau)), Tagged("sols", Num(s.Sols)), Tagged("settle", Num(s.Settle))))]);

    private static SSymbol Sym(string s) => new(s);
    private static SNumber Num(double v) => new(v);
    private static SExpr NumOrFalse(double? v) => v is { } d ? Num(d) : new SBool(false);
    private static SExpr SymOrFalse(string? s) => s is { } n ? Sym(n) : new SBool(false);
    private static SList List(params SExpr[] items) => new(items);
    private static SList Tagged(string head, params SExpr[] items) => new([Sym(head), .. items]);

    private static SExpr Vec3(Vec3 v) => List(Num(v.X), Num(v.Y), Num(v.Z));

    private static SExpr SrcLoc(SourceLocation? loc) =>
        loc is null
            ? Tagged("srcloc", new SString("<editor>"), Num(0), Num(0))
            : Tagged("srcloc", new SString(loc.File), Num(loc.Line), Num(loc.Column));

    public static SExpr PartClause(PartSpec p) =>
        Tagged("part", Sym(p.Id), Sym(p.Kind),
            Tagged("material", Sym(p.Material)),
            new SList([Sym("at"), .. ((SList)Vec3(p.At)).Items]),
            new SList([Sym("props"), .. p.Props.Select(kv => (SExpr)List(Sym(kv.Key), kv.Value))]),
            new SList([Sym("ports"), .. p.Ports.Select(pt => (SExpr)List(Sym(pt.Name), Sym(pt.Kind), Num(pt.Height)))]),
            SrcLoc(p.Location));

    /// <summary>A (name part port) reference clause, such as (from tank1 outlet).</summary>
    private static SExpr RefClause(string head, PortRef r) => Tagged(head, Sym(r.Part), Sym(r.Port));

    /// <summary>A bare (part port) reference, with no head symbol — as used in a connect clause.</summary>
    private static SExpr Ref(PortRef r) => List(Sym(r.Part), Sym(r.Port));

    private static SExpr PipeClause(PipeSpec p) =>
        Tagged("pipe", Sym(p.Id),
            RefClause("from", p.From), RefClause("to", p.To),
            Tagged("conductance", Num(p.Conductance)),
            Tagged("jet", new SBool(p.Jet)),
            SrcLoc(p.Location));

    private static SExpr ConnectClause(ConnectSpec c) =>
        Tagged("connect", Ref(c.A), Ref(c.B), SrcLoc(c.Location));

    private static SExpr RopeEndClause(string head, RopeEnd e) =>
        Tagged(head, Sym(e.Part), Num(e.Local.X), Num(e.Local.Y), Num(e.Local.Z));

    private static SExpr RopeClause(RopeSpec r) =>
        Tagged("rope", [Sym(r.Id),
            RopeEndClause("from", r.From), RopeEndClause("to", r.To),
            Tagged("length", Num(r.Length)),
            new SList([Sym("over"), .. r.Over.Select(o => (SExpr)Vec3(o))]),
            Tagged("wind-on", SymOrFalse(r.WindOn)),
            Tagged("release-deg", NumOrFalse(r.ReleaseDeg)),
            Tagged("material", Sym(r.Material)),
            Tagged("diameter", Num(r.Diameter)),
            Tagged("nocked", new SBool(r.Nocked)),
            Tagged("turns", SymOrFalse(r.Turns)),
            Tagged("bar", SymOrFalse(r.Bar)),
            Tagged("mu", NumOrFalse(r.Mu)),
            .. r.Links is { } n ? [Tagged("links", Num(n))] : Array.Empty<SExpr>(),   // a chain (#31) only
            SrcLoc(r.Location)]);

    private static SExpr InflowClause(SourceSpec s) =>
        Tagged("inflow", Sym(s.Id), Tagged("into", Sym(s.Into)), Tagged("flow", Num(s.Flow)), SrcLoc(s.Location));

    private static SExpr ChannelClause(ChannelSpec c) =>
        Tagged("channel", [Sym(c.Id),
            RefClause("from", c.From),
            c.To is { } to ? RefClause("to", to) : Tagged("to", Sym("off")),
            c.End is { } end ? Tagged("end", Num(end.X), Num(end.Y), Num(end.Z)) : Tagged("end"),
            Tagged("via", (c.Via ?? []).Select(p => (SExpr)List(Num(p.X), Num(p.Z))).ToArray()),
            Tagged("width", Num(c.Width)),
            Tagged("length", NumOrFalse(c.Length)),
            Tagged("onto", SymOrFalse(c.Onto)),
            // a dynamic reach (issue #36) only: steady channels' clauses stay as they were
            .. c.Dynamic ? [Tagged("dynamic", new SBool(true)), Tagged("cells", c.Cells is { } n ? Num(n) : new SBool(false))] : Array.Empty<SExpr>(),
            SrcLoc(c.Location)]);

    private static SExpr CylinderClause(CylinderSpec c) =>
        Tagged("atmospheric-cylinder", [Sym(c.Id),
            Tagged("piston", Sym(c.Piston)), Tagged("steam-from", Sym(c.Boiler)),
            .. c.InjectionTemperature is { } inj ? [Tagged("injection-temperature", Num(inj))] : Array.Empty<SExpr>(),
            SrcLoc(c.Location)]);

    private static SExpr LiftClause(LiftSpec l) =>
        Tagged("lift", Sym(l.Id),
            Tagged("by", Sym(l.By)), Tagged("from", Sym(l.From)), Tagged("to", Sym(l.To)),
            Tagged("current", NumOrFalse(l.Current)),
            Tagged("current-from", SymOrFalse(l.CurrentFrom)),
            SrcLoc(l.Location));

    private static SExpr JointClause(JointSpec j) =>
        Tagged("joint", Sym(j.Id),
            Tagged("kind", Sym(j.Kind)), Tagged("a", Sym(j.A)), Tagged("b", Sym(j.B)),
            Tagged("at", Num(j.At.X), Num(j.At.Y), Num(j.At.Z)),
            j.Axis is { } ax ? Tagged("axis", Num(ax.X), Num(ax.Y), Num(ax.Z)) : Tagged("axis"),
            new SList([Sym("free"), .. j.Free.Select(f => (SExpr)Sym(f))]),
            Tagged("limit-deg", NumOrFalse(j.LimitDeg)),
            SrcLoc(j.Location));

    private static SExpr FollowClause(FollowSpec f) =>
        Tagged("follow", Sym(f.Id),
            Tagged("lever", SymOrFalse(f.Lever)), Tagged("rope", SymOrFalse(f.Rope)),
            Tagged("from", Num(f.From)), Tagged("to", Num(f.To)),
            Tagged("set", Sym(f.Target), Sym(f.Field)),
            Tagged("low", Num(f.Low)), Tagged("high", Num(f.High)),
            SrcLoc(f.Location));

    private static SExpr TriggerClause(TriggerSpec t) =>
        Tagged("trigger", Sym(t.Id),
            t.At is { } at ? Tagged("at", Num(at.X), Num(at.Y), Num(at.Z)) : Tagged("at"),
            t.Size is { } sz ? Tagged("size", Num(sz.X), Num(sz.Y), Num(sz.Z)) : Tagged("size"),
            Tagged("body", SymOrFalse(t.Body)),
            t.WatchTarget is { } wt
                ? Tagged("when", Sym(wt), Sym(t.WatchField!), Sym(t.Rising ? "above" : "below"), Num(t.Threshold))
                : Tagged("when"),
            new SList([Sym("do"), .. t.Actions.Select(a => (SExpr)List(Sym(a.Target), Sym(a.Field), Num(a.Value)))]),
            SrcLoc(t.Location));

    private static SExpr MeshClause(MeshSpec g) => Tagged("mesh", Sym(g.A), Sym(g.B), SrcLoc(g.Location));

    private static SExpr ArborClause(ArborSpec a) =>
        Tagged("arbor", new SList([Sym("parts"), .. a.Parts.Select(p => (SExpr)Sym(p))]), SrcLoc(a.Location));

    private static SExpr SealedAirClause(SealedAirSpec a) =>
        Tagged("sealed-air",
            new SList([Sym("tanks"), .. a.Tanks.Select(t => (SExpr)Sym(t))]),
            Tagged("tube-volume", Num(a.TubeVolume)),
            Tagged("heat-loss", Num(a.HeatLoss)),
            Tagged("heat-capacity", Num(a.HeatCapacity)),
            SrcLoc(a.Location));
}
