using System.Globalization;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// Workshop lessons (#166): a lesson is data, one file a lesson under
/// game/lessons/, read here; the editor (BuildMode.Lessons.cs) shows it and
/// feeds it what the player does. A step is an instruction, the palette
/// entry or button to light up, an optional target (where the part should
/// go: any close placement counts, and is then set exactly on it), and a
/// check that completes the step by itself:
///
///   (check place (kind block) (category stone) (as heavy))   a new part of that kind near the target, remembered as "heavy"
///   (check place (part light))                               the part remembered as "light" moved near the target
///   (check test)                                             the player pressed Test
///   (check balanced (beam beam) (within-deg 3))              the lesson runs the machine itself; the beam's traced tilt stays within 3°
///
/// A target is either a point, (at X Y Z), or a place on a remembered beam:
/// (on beam) (along -0.2) (up 0.0625) puts it 0.2 m along the beam from its
/// pivot toward its −x end, 0.0625 m above the pivot; (balance heavy light)
/// works out "along" from the parts' real masses, as torque = weight ×
/// distance, so that the beam balances.
///
/// Text may name numbers the lesson works out from the design: {heavy.mass}
/// (kg), {heavy.dist} (m from the pivot), {heavy.turn} (kg·m),
/// {heavy.material}, the same for any remembered part, {balance} (m), and
/// {tilt} (the last test's largest lean, degrees). (done "...") is what the
/// lesson says once its last step is done.
/// </summary>
public sealed record Lesson(string Id, string Title, IReadOnlyList<LessonStep> Steps, string Done)
{
    public static Lesson Parse(string text)
    {
        var root = SExprReader.ReadAll(text).OfType<SList>().FirstOrDefault(l => l.Head == "lesson")
                   ?? throw new FormatException("a lesson file holds one (lesson id ...) form");
        string id = root.Items.Count > 1 && root.Items[1] is SSymbol s ? s.Name : throw new FormatException("(lesson ID ...) needs an id");
        string title = Str(root.Field("title")) ?? id;
        var steps = root.Fields("step").Select(ParseStep).ToList();
        if (steps.Count == 0) throw new FormatException($"lesson {id} has no steps");
        return new Lesson(id, title, steps, Str(root.Field("done")) ?? "Done.");
    }

    private static LessonStep ParseStep(SList step)
    {
        string text = Str(step.Field("text")) ?? throw new FormatException("a step needs (text \"...\")");
        LessonTarget? target = null;
        if (step.Field("target") is { } t)
        {
            Vec3? at = t.Field("at") is { } a ? new Vec3(Num(a, 1), Num(a, 2), Num(a, 3)) : null;
            (string, string)? balance = t.Field("balance") is { } b ? (Sym(b, 1), Sym(b, 2)) : null;
            target = new LessonTarget(Sym(t.Field("kind")!, 1), at, t.Field("on") is { } on ? Sym(on, 1) : null,
                                      t.Field("along") is { } al ? Num(al, 1) : 0, t.Field("up") is { } up ? Num(up, 1) : 0,
                                      balance, t.Field("within") is { } w ? Num(w, 1) : 0.15);
        }
        var c = step.Field("check") ?? throw new FormatException("a step needs a (check ...)");
        string Opt(string name) => c.Field(name) is { } f ? Sym(f, 1) : null!;
        var check = new LessonCheck(Sym(c, 1), Opt("kind"), Opt("category"), Opt("as"), Opt("part"), Opt("beam"),
                                    c.Field("within-deg") is { } wd ? Num(wd, 1) : 3);
        return new LessonStep(text, step.Field("highlight") is { } h ? Sym(h, 1) : null,
                              step.Field("material") is { } m ? Sym(m, 1) : null, target, check);
    }

    private static string? Str(SList? l) => l is not null && l.Items.Count > 1 && l.Items[1] is SString s ? s.Value : null;
    private static string Sym(SList l, int i) => l.Items[i] switch { SSymbol s => s.Name, SString s => s.Value, var x => x.ToString()! };
    private static double Num(SList l, int i) => ((SNumber)l.Items[i]).Value;
}

/// <summary>One instruction: what to read, what to light up, what to make new parts of, where the part goes, and what completes it.</summary>
public sealed record LessonStep(string Text, string? Highlight, string? Material, LessonTarget? Target, LessonCheck Check);

/// <summary>Where a step's part should go: a point, or a place along a remembered beam (see <see cref="Lesson"/>).</summary>
public sealed record LessonTarget(string Kind, Vec3? At, string? On, double Along, double Up, (string Heavy, string Light)? Balance, double Within);

/// <summary>What completes a step: "place", "test" or "balanced" (see <see cref="Lesson"/>).</summary>
public sealed record LessonCheck(string Type, string? Kind, string? Category, string? As, string? Part, string? Beam, double WithinDeg);

/// <summary>
/// A lesson being played: which step, and which parts in the design the
/// lesson has come to know by name ("beam", "heavy", "light"). Everything
/// that decides whether a step is done lives here, out of the engine, so
/// it is tested without Godot (LessonTests.cs); the "balanced" check's run
/// itself is the editor's Test, whose traced tilts come in through
/// <see cref="OnTest"/>.
/// </summary>
public sealed class LessonRunner(Lesson lesson, MaterialLibrary materials)
{
    public Lesson Lesson { get; } = lesson;
    public int Index { get; private set; }
    public Dictionary<string, string> Roles { get; } = [];
    public LessonStep? Current => Index < Lesson.Steps.Count ? Lesson.Steps[Index] : null;
    public bool Finished { get; private set; }

    /// <summary>The masses the last test run reported for each part, kg; until a test runs, density × volume (the same sum the run makes).</summary>
    public Dictionary<string, double> SimMasses { get; } = [];

    /// <summary>The largest lean of the lesson's beam in the last test run, degrees.</summary>
    public double? LastTilt { get; set; }

    /// <summary>Why the last "balanced" check failed, in plain words, or null.</summary>
    public string? Failure { get; private set; }

    public void Restore(int index, IReadOnlyDictionary<string, string> roles)
    {
        Index = Math.Clamp(index, 0, Lesson.Steps.Count - 1);
        _baseline = null;
        Nudge = null;
        Roles.Clear();
        foreach (var (k, v) in roles) Roles[k] = v;
    }

    /// <summary>
    /// Goes back to the earliest step whose remembered part is no longer in
    /// the design (a lesson resumed after the design was lost or changed).
    /// </summary>
    public void Revalidate(EditorDocument doc)
    {
        for (int i = 0; i < Index; i++)
            if (Lesson.Steps[i].Check is { Type: "place", As: { } role } && (!Roles.TryGetValue(role, out var id) || !doc.Parts.ContainsKey(id)))
            {
                Index = i;
                _baseline = null;
                Nudge = null;
                foreach (var later in Lesson.Steps.Skip(i).Select(s => s.Check.As).OfType<string>()) Roles.Remove(later);
                return;
            }
    }

    private void Advance()
    {
        Failure = null;
        Nudge = null;
        _baseline = null;
        Index++;
        if (Index >= Lesson.Steps.Count) { Index = Lesson.Steps.Count - 1; Finished = true; }
    }

    // --------------------------------------------------------- numbers

    /// <summary>A part's mass in kg: what the last run reported, else its material's density × its volume.</summary>
    public double MassOf(EditorDocument doc, string id)
    {
        if (SimMasses.TryGetValue(id, out var m)) return m;
        var part = doc.Parts[id];
        double volume = part.Kind == "ball" ? 4.0 / 3 * Math.PI * Math.Pow(part.Number("radius", 0.05), 3)
                      : Math.Pow(part.Number("size", 0.1), 3);
        return materials[part.Material].MassOf(volume);
    }

    /// <summary>The direction a beam runs along (its +x end), turned by its heading.</summary>
    private static Vec3 AxisOf(PartSpec beam)
    {
        double h = beam.Number("heading-deg", 0) * Math.PI / 180;
        return new Vec3(Math.Cos(h), 0, -Math.Sin(h));
    }

    /// <summary>How far along a remembered beam a part is from its pivot, metres: + toward the beam's +x end.</summary>
    public double Along(EditorDocument doc, string beamId, string id)
    {
        var beam = doc.Parts[beamId];
        var axis = AxisOf(beam);
        var p = doc.Parts[id].At;
        return (p.X - beam.At.X) * axis.X + (p.Z - beam.At.Z) * axis.Z;
    }

    /// <summary>Where along the beam the light part balances the heavy one: mass × distance the same on each side.</summary>
    public double? BalanceAlong(EditorDocument doc, string beamRole, string heavyRole, string lightRole)
    {
        if (!Roles.TryGetValue(beamRole, out var beam) || !Roles.TryGetValue(heavyRole, out var heavy) || !Roles.TryGetValue(lightRole, out var light)
            || !doc.Parts.ContainsKey(beam) || !doc.Parts.ContainsKey(heavy) || !doc.Parts.ContainsKey(light)) return null;
        return -MassOf(doc, heavy) * Along(doc, beam, heavy) / MassOf(doc, light);
    }

    /// <summary>The current step's target in the world, or null when it has none (or what it is placed on isn't there yet).</summary>
    public Vec3? TargetAt(EditorDocument doc)
    {
        if (Current?.Target is not { } t) return null;
        if (t.At is { } at) return at;
        if (t.On is not { } onRole || !Roles.TryGetValue(onRole, out var beamId) || !doc.Parts.TryGetValue(beamId, out var beam)) return null;
        double along = t.Along;
        if (t.Balance is { } b)
        {
            if (BalanceAlong(doc, onRole, b.Heavy, b.Light) is not { } bal) return null;
            along = bal;
        }
        var axis = AxisOf(beam);
        return new Vec3(beam.At.X + axis.X * along, beam.At.Y + t.Up, beam.At.Z + axis.Z * along);
    }

    // --------------------------------------------------------- checks

    /// <summary>
    /// Why the last placement did not count, in plain words (null when there is
    /// nothing to say: nothing has been placed or moved since the step began, or
    /// the step is done). Cleared when the step is done or the design changes
    /// back.
    /// </summary>
    public string? Nudge { get; private set; }

    /// <summary>The design as it was when the step was first looked at: only a part placed or moved since then is nudged about.</summary>
    private Dictionary<string, string>? _baseline;

    /// <summary>A step has just been shown (after any settle command): what is on the bench now is not the player's doing, so it is not nudged about.</summary>
    public void Rebase(EditorDocument doc)
    {
        _baseline = doc.Parts.ToDictionary(kv => kv.Key, kv => Signature(kv.Value));
        Nudge = null;
    }

    private static string Signature(PartSpec p) => $"{p.Kind}|{N(p.At.X)} {N(p.At.Y)} {N(p.At.Z)}|{p.Material}";

    private static string KindName(string kind) => kind == "lever" ? "beam" : kind;

    /// <summary>
    /// Whether the design now does what the current "place" step asks. When
    /// it does, the step is done; the returned command, if any, sets the
    /// part exactly on the target (any close placement counts). When it does
    /// not, and the player has just placed or moved a part, the reason says
    /// why that part did not count: the wrong kind of part, the wrong
    /// material, or off the target (how far, which way).
    /// </summary>
    public (bool Done, string? Settle, string? Reason) CheckDesign(EditorDocument doc)
    {
        Nudge = null;
        if (Finished || Current is not { Check.Type: "place" } step) return (false, null, null);
        var c = step.Check;
        var bound = Roles.Values.ToHashSet();
        _baseline ??= doc.Parts.ToDictionary(kv => kv.Key, kv => Signature(kv.Value));
        IEnumerable<PartSpec> pool = c.Part is { } role
            ? Roles.TryGetValue(role, out var known) && doc.Parts.TryGetValue(known, out var p) ? [p] : []
            : doc.Parts.Values.Where(x => !bound.Contains(x.Id));
        var changed = pool.Where(x => !_baseline.TryGetValue(x.Id, out var sig) || sig != Signature(x)).ToList();
        string? CategoryOf(PartSpec x) => materials.TryGet(x.Material, out var m) ? m.Category.ToString() : null;
        bool KindOk(PartSpec x) => c.Kind is null || x.Kind == c.Kind;
        bool CategoryOk(PartSpec x) => c.Category is null || CategoryOf(x)?.Equals(c.Category, StringComparison.OrdinalIgnoreCase) == true;
        var candidates = pool.Where(x => KindOk(x) && CategoryOk(x)).ToList();
        var target = TargetAt(doc);

        if (c.Part is { } r && (!Roles.TryGetValue(r, out var gone) || !doc.Parts.ContainsKey(gone)))
        {
            Nudge = $"The {r} part is not on the bench any more. Undo your last change, or press Start over.";
            return (false, null, Nudge);
        }

        if (step.Target is not null && target is null)
        {
            // what the part goes on is gone (a resumed lesson normally goes back a step; this is an edit made since)
            if (candidates.Count > 0 || changed.Count > 0)
                Nudge = $"This step puts the {KindName(step.Target.Kind)} {(step.Target.On is { } on ? $"on the {on}" : "in place")}, but there is no {step.Target.On ?? "target"} on the bench any more. Press Start over to begin again.";
            return (false, null, Nudge);
        }

        var best = candidates.Select(x => (Part: x, Off: target is { } t ? Distance(x.At, t) : 0)).OrderBy(x => x.Off).FirstOrDefault();
        if (best.Part is not null && !(target is { } && best.Off > step.Target!.Within))
        {
            if (c.As is { } name) Roles[name] = best.Part.Id;
            string? settle = target is { } to && best.Off > 1e-4 ? $"(move {best.Part.Id} ({N(to.X)} {N(to.Y)} {N(to.Z)}))" : null;
            Advance();
            return (true, settle, null);
        }

        if (changed.Count == 0) return (false, null, null);

        // the part that came nearest to counting is the one to talk about: right kind and material but off, then the wrong material, then the wrong part
        int Rank(PartSpec x) => KindOk(x) ? (CategoryOk(x) ? 0 : 1) : 2;
        var about = changed.OrderBy(Rank).ThenBy(x => target is { } t ? Distance(x.At, t) : 0).First();
        string used = KindName(about.Kind), asked = KindName(c.Kind ?? about.Kind);
        string matName = materials.TryGet(about.Material, out var mat) ? mat.Name.ToLowerInvariant() : about.Material;
        if (!KindOk(about))
            Nudge = $"That is a {used}, but this step needs a {asked}. Take the {used} away and pick {char.ToUpperInvariant(asked[0])}{asked[1..]} in the parts list.";
        else if (!CategoryOk(about))
            Nudge = $"That {used} is {matName}, {(CategoryOf(about) is { } cat ? $"a {cat.ToLowerInvariant()}" : "not the right stuff")}, but this step asks for a {c.Category!.ToLowerInvariant()} one"
                  + (step.Material is { } want ? $", such as {want}" : "")
                  + $". Take it away and pick {char.ToUpperInvariant(asked[0])}{asked[1..]} again: the lesson picks the material on its card.";
        else if (target is { } tg)
        {
            var d = new Vec3(tg.X - about.At.X, tg.Y - about.At.Y, tg.Z - about.At.Z);
            var moves = new List<string>();
            if (Math.Abs(d.X) >= 0.02) moves.Add($"{N(Math.Round(Math.Abs(d.X), 2))} m to the {(d.X > 0 ? "right" : "left")}");
            if (Math.Abs(d.Y) >= 0.02) moves.Add($"{N(Math.Round(Math.Abs(d.Y), 2))} m {(d.Y > 0 ? "up" : "down")}");
            if (Math.Abs(d.Z) >= 0.02) moves.Add($"{N(Math.Round(Math.Abs(d.Z), 2))} m {(d.Z > 0 ? "nearer to you" : "farther from you")}");
            Nudge = $"Not on the green {KindName(step.Target!.Kind)} yet: that {used} is {N(Math.Round(Distance(about.At, tg), 2))} m from it, and it has to be within {N(step.Target.Within)} m. "
                  + (moves.Count > 0 ? $"Move it {string.Join(" and ", moves)}." : "Move it a little closer.");
        }
        return (false, null, Nudge);
    }

    /// <summary>
    /// A test run ended: a "test" step is done; a "balanced" step is done
    /// when the beam's traced tilt never left ±within-deg, else it says what
    /// happened. Each beam's tilts are degrees, + when its +x end is up.
    /// </summary>
    public bool OnTest(IReadOnlyDictionary<string, List<double>> tilts, IReadOnlyDictionary<string, double> masses)
    {
        foreach (var (id, m) in masses) SimMasses[id] = m;
        var beamRole = Current?.Check.Beam ?? Roles.Keys.FirstOrDefault(r => r == "beam");
        if (beamRole is not null && Roles.TryGetValue(beamRole, out var beamId) && tilts.TryGetValue(beamId, out var trace))
            LastTilt = trace.Select(Math.Abs).DefaultIfEmpty(0).Max();
        if (Finished || Current is not { } step) return false;
        switch (step.Check.Type)
        {
            case "test":
                Advance();
                return true;
            case "balanced":
                if (LastTilt is { } most && most <= step.Check.WithinDeg) { Advance(); return true; }
                Failure = LastTilt is { } t ? $"Not balanced yet: the beam leaned {t:0.#}° (it has to stay within {step.Check.WithinDeg:0.#}°)."
                                            : "The test didn't see the beam.";
                return false;
        }
        return false;
    }

    /// <summary>The step's text with its numbers filled in from the design (see <see cref="Lesson"/>).</summary>
    public string Fill(string text, EditorDocument doc)
    {
        string beam = Roles.GetValueOrDefault("beam") is { } b && doc.Parts.ContainsKey(b) ? b : "";
        return System.Text.RegularExpressions.Regex.Replace(text, @"\{([a-z-]+)(?:\.([a-z]+))?\}", m =>
        {
            string name = m.Groups[1].Value, field = m.Groups[2].Value;
            if (name == "tilt") return LastTilt is { } t ? (t < 0.1 ? "a tenth of a degree" : $"{t:0.#}°") : "?";
            if (name == "balance")
                return BalanceAlong(doc, "beam", "heavy", "light") is { } bal ? $"{N(Math.Abs(bal))} m" : "?";
            if (!Roles.TryGetValue(name, out var id) || !doc.Parts.ContainsKey(id)) return "?";
            return field switch
            {
                "mass" => $"{N(MassOf(doc, id))} kg",
                "dist" => beam != "" ? $"{N(Math.Abs(Along(doc, beam, id)))} m" : "?",
                "turn" => beam != "" ? N(MassOf(doc, id) * Math.Abs(Along(doc, beam, id))) : "?",
                "material" => materials.TryGet(doc.Parts[id].Material, out var mat) ? mat.Name.ToLowerInvariant() : doc.Parts[id].Material,
                _ => m.Value,
            };
        });
    }

    private static double Distance(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
    private static string N(double v) => Math.Round(v, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);
}
