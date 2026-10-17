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
///   (check join (kind rope) (between heavy light) (over pulley) (hoist 0.55))   a rope tied between those two remembered parts; the lesson then
///                                                            threads it over the remembered pulley and hangs "heavy" 0.55 m up (#183)
///   (check join (kind pipe) (between upper lower))           a pipe between two remembered tanks (either way round)
///   (check join (kind rope) (between drum load) (wind-on drum) (hang 0.3))   a rope tied between them; the lesson then ties it again wound
///                                                            on the drum (#:wind-on), the load hung plumb from the drum's winding side
///                                                            0.3 m up; with (over pulley), over the remembered pulley's top first
///   (check join (kind arbor) (between wheel drum))           the two remembered wheels on one axle; (kind mesh) for two gears in mesh
///   (check place ... (then "(set {id} #:water 0)"))          commands run once it counts, {id} the part (a slim post, an emptied tank);
///                                                            on a join, the rope or pipe (an arbor's or a mesh's second part); {drum}
///                                                            any part the lesson remembers ((then "(set {drum} #:drive-rpm 3)"))
///   (check lifted (part light) (about 0.55) (within 0.08))   after a Test: the part ended that high, plus or minus (the Test's facts)
///   (check rolled (part ball) (far 1.0) (within 0.15))       after a Test: the ball went at the speed rolling from where it began gives,
///                                                            sqrt(10/7 g h) plus or minus 0.15 m/s, and ended at least 1 m from the start
///   (check level (part lower) (litres 10) (within 0.5))      after a Test: that tank ended with that much water, plus or minus
///   (check place (kind wheel) (teeth 72) (as crank))         a gear of that many teeth ((catalogue involute-gear-m5-72) names the exact
///                                                            catalogue part; (on-shaft drum) asks for it on the same axle as "drum")
///   (check mesh (with crank) (teeth 18) (as second))         a new gear meshed with the remembered "crank" (either way round), remembered as "second"
///   (check turned (part third) (rpm 60) (within 2))          after a Test: that wheel was turning at 60 rpm at the end, either way, plus or minus
///   (check stage (on sails) (as wheel-a pinion-b) (teeth 72 18))   a new wheel of 72 teeth on the sails' axle meshed with a new pinion of 18
///                                                            ("Add a stage": one edit), remembered as "wheel-a" and "pinion-b"
///   (check charging (part generator) (watts 440) (within 120))   after a Test: the bank was taking 440 W from that generator at the end
///   (check ratio (part wheel-a) (to pinion-e) (is 256) (within 2))   after a Test: pinion-e turned 256 times for each turn of wheel-a
///   A step may hold several outcome checks, (check turned ...) (check ratio ...): one Test must meet them all.
///
/// A step's (highlight ...) is a palette entry (post, gear), test, part:ROLE (that part lit in the scene), or a button:
/// button:gear_on, button:mesh_with or button:add_stage (the selected part's inspector button, "Put a gear on this shaft" (with
/// the step's (variant drum-30cm), the drum: "Put a drum on this shaft"), "Add a meshing gear", "Add a stage") and button:preset:LABEL (the part's card's size button, "button:preset:3 m" in quotes). A step's
/// (select crank) selects that remembered part as the step begins (and lights it while it isn't), so its inspector shows the button;
/// a lit button is the one for that part.
///
/// The lesson may say (view YAW PITCH DISTANCE X Y Z) where the camera starts: a tall rig is seen
/// from straight in front, so that nothing stands between the camera and the ground the blocks go on.
/// A step may also say (fail "..."): what to tell the player when the Test's
/// outcome check is not met. It may also say (variant pulley-10cm): the size the parts list's card
/// offers for this step; and (preset "3 m"): the size button its card has pressed (a 3 m post, not the usual 1 m). A place check may name (shape pulley) and (radius
/// 0.1) for a catalogue part of that shape and size.
///
/// A target is either a point, (at X Y Z), or a place on a remembered beam:
/// (on beam) (along -0.2) (up 0.0625) puts it 0.2 m along the beam from its
/// pivot toward its −x end, 0.0625 m above the pivot; (balance heavy light)
/// works out "along" from the parts' real masses, as torque = weight ×
/// distance, so that the beam balances. On a remembered ramp, (along 0.9) is
/// the distance up the slope from its foot and (up 0.076) how far the part's
/// centre stands off the slab's middle (half its 2.5 cm thickness and a 5 cm
/// ball's radius, and a millimetre).
///
/// Text may name numbers the lesson works out from the design: {heavy.mass}
/// (kg), {heavy.dist} (m from the pivot), {heavy.turn} (kg·m),
/// {heavy.material}, the same for any remembered part, {balance} (m), and
/// {tilt} (the last test's largest lean, degrees). Also: {hoist}, {rise} (how
/// far the part of the step's check rose), {accel} (m/s²), {drop}, {speed}, {slide}, {top}, {across} (a ball's
/// worked and measured numbers), {moved}, {reached}, {empties}, {upper.litres},
/// {lower.litres} and {lower.level} (water), {third.rpm} (how fast that wheel was turning at the end of the last Test, either way),
/// {generator.watts} (what the bank took from it at the end), {watts} (the same for the lesson's charging check's generator) and {ratio}
/// (the lesson's ratio check's, traced). A step may say (view YAW PITCH DISTANCE X Y Z): the camera goes there as the step begins.
/// (done "...") is what the lesson says once its last step is done.
/// </summary>
public sealed record Lesson(string Id, string Title, IReadOnlyList<LessonStep> Steps, string Done)
{
    /// <summary>
    /// Where the camera looks from when the lesson starts: (view YAW PITCH DISTANCE X Y Z), radians and metres, round the point X Y Z;
    /// null for the default (a little to the right, looking at the middle of a beam).
    /// </summary>
    public LessonView? View { get; init; }

    public static Lesson Parse(string text)
    {
        var root = SExprReader.ReadAll(text).OfType<SList>().FirstOrDefault(l => l.Head == "lesson")
                   ?? throw new FormatException("a lesson file holds one (lesson id ...) form");
        string id = root.Items.Count > 1 && root.Items[1] is SSymbol s ? s.Name : throw new FormatException("(lesson ID ...) needs an id");
        string title = Str(root.Field("title")) ?? id;
        var steps = root.Fields("step").Select(ParseStep).ToList();
        if (steps.Count == 0) throw new FormatException($"lesson {id} has no steps");
        var view = root.Field("view") is { } v && v.Items.Count >= 7 ? new LessonView(Num(v, 1), Num(v, 2), Num(v, 3), new Vec3(Num(v, 4), Num(v, 5), Num(v, 6))) : null;
        return new Lesson(id, title, steps, Str(root.Field("done")) ?? "Done.") { View = view };
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
        var checks = step.Fields("check").Select(ParseCheck).ToList();
        if (checks.Count == 0) throw new FormatException("a step needs a (check ...)");
        if (checks.Skip(1).Any(x => !x.IsOutcome) || checks.Count > 1 && !checks[0].IsOutcome)
            throw new FormatException("only a Test's outcome checks go several to a step");
        return new LessonStep(text, step.Field("highlight") is { } h ? Sym(h, 1) : null,
                              step.Field("material") is { } m ? Sym(m, 1) : null, target, checks[0])
        {
            Also = checks.Skip(1).ToList(),
            Variant = step.Field("variant") is { } v ? Sym(v, 1) : null,
            Preset = step.Field("preset") is { } pr ? Sym(pr, 1) : null,
            Fail = Str(step.Field("fail")),
            Select = step.Field("select") is { } sel ? Sym(sel, 1) : null,
            View = step.Field("view") is { } vw && vw.Items.Count >= 7 ? new LessonView(Num(vw, 1), Num(vw, 2), Num(vw, 3), new Vec3(Num(vw, 4), Num(vw, 5), Num(vw, 6))) : null,
        };
    }

    private static LessonCheck ParseCheck(SList c)
    {
        string Opt(string name) => c.Field(name) is { } f ? Sym(f, 1) : null!;
        double? Number(string name) => c.Field(name) is { } f ? Num(f, 1) : null;
        double? Second(string name) => c.Field(name) is { } f && f.Items.Count > 2 ? Num(f, 2) : null;
        return new LessonCheck(Sym(c, 1), Opt("kind"), Opt("category"), Opt("as"), Opt("part"), Opt("beam"),
                               c.Field("within-deg") is { } wd ? Num(wd, 1) : 3)
        {
            Shape = Opt("shape"),
            Radius = Number("radius"),
            Between = c.Field("between") is { } bt ? (Sym(bt, 1), Sym(bt, 2)) : null,
            Over = Opt("over"),
            WindOn = Opt("wind-on"),
            Hoist = Number("hoist") ?? Number("hang") ?? 0,
            About = Number("about"),
            Within = Number("within") ?? 0.1,
            Far = Number("far"),
            Litres = Number("litres"),
            Teeth = Number("teeth"),
            Catalogue = Opt("catalogue"),
            OnShaft = Opt("on-shaft"),
            With = Opt("with"),
            Rpm = Number("rpm"),
            On = Opt("on"),
            As2 = c.Field("as") is { } a2 && a2.Items.Count > 2 ? Sym(a2, 2) : null,
            Teeth2 = Second("teeth"),
            Watts = Number("watts"),
            To = Opt("to"),
            Is = Number("is"),
            Then = c.Fields("then").Select(t => Str(t) ?? "").Where(t => t != "").ToList(),
        };
    }

    private static string? Str(SList? l) => l is not null && l.Items.Count > 1 && l.Items[1] is SString s ? s.Value : null;
    private static string Sym(SList l, int i) => l.Items[i] switch { SSymbol s => s.Name, SString s => s.Value, var x => x.ToString()! };
    private static double Num(SList l, int i) => ((SNumber)l.Items[i]).Value;
}

/// <summary>A camera set-up: yaw and pitch (radians), distance, and the point looked at.</summary>
public sealed record LessonView(double Yaw, double Pitch, double Distance, Vec3 Pivot);

/// <summary>One instruction: what to read, what to light up, what to make new parts of, where the part goes, and what completes it.</summary>
public sealed record LessonStep(string Text, string? Highlight, string? Material, LessonTarget? Target, LessonCheck Check)
{
    /// <summary>The catalogue size the parts list's card should offer for this step (pulley-10cm), or null.</summary>
    public string? Variant { get; init; }

    /// <summary>The size button the lit entry's card should have pressed for this step ((preset "3 m"): a 3 m post), or null.</summary>
    public string? Preset { get; init; }

    /// <summary>What to say, in the lesson's own words, when a Test's outcome is not the worked one (its numbers filled in); null for a plain statement of the numbers.</summary>
    public string? Fail { get; init; }

    /// <summary>The remembered part the step selects as it begins (its inspector has the step's button), or null.</summary>
    public string? Select { get; init; }

    /// <summary>More outcome checks the same Test must meet (a step's second and later (check ...) forms): all of them, or the step isn't done.</summary>
    public IReadOnlyList<LessonCheck> Also { get; init; } = [];

    /// <summary>Every check of the step: its own, then <see cref="Also"/>.</summary>
    public IEnumerable<LessonCheck> Checks => Also.Prepend(Check);

    /// <summary>Where the camera goes as the step begins ((view YAW PITCH DISTANCE X Y Z), as the lesson's own), or null to leave it.</summary>
    public LessonView? View { get; init; }

    /// <summary>The inspector or card button the step lights ("mesh_with", "gear_on", "add_stage", "preset:3 m"), from (highlight button:...); null for none.</summary>
    public string? Button => Highlight is { } h && h.StartsWith("button:", StringComparison.Ordinal) ? h["button:".Length..] : null;
}

/// <summary>Where a step's part should go: a point, or a place along a remembered beam (see <see cref="Lesson"/>).</summary>
public sealed record LessonTarget(string Kind, Vec3? At, string? On, double Along, double Up, (string Heavy, string Light)? Balance, double Within);

/// <summary>
/// What completes a step: "place", "mesh", "stage", "join", "test", or one of the checks of a Test's outcome, "balanced", "lifted", "rolled",
/// "level", "turned", "charging" and "ratio"
/// (see <see cref="Lesson"/>).
/// </summary>
public sealed record LessonCheck(string Type, string? Kind, string? Category, string? As, string? Part, string? Beam, double WithinDeg)
{
    public string? Shape { get; init; }
    public double? Radius { get; init; }
    public (string A, string B)? Between { get; init; }
    public string? Over { get; init; }

    /// <summary>A rope join's remembered drum the rope is tied again wound on ((wind-on drum)), or null.</summary>
    public string? WindOn { get; init; }

    /// <summary>How far above where it stood the lesson hangs a rope's load, metres ((hoist 0.55), or (hang 0.3) on a wound rope).</summary>
    public double Hoist { get; init; }
    public double? About { get; init; }
    public double Within { get; init; } = 0.1;
    public double? Far { get; init; }
    public double? Litres { get; init; }

    /// <summary>A gear's tooth count the place or mesh check asks for (#:teeth), or null for any.</summary>
    public double? Teeth { get; init; }

    /// <summary>The catalogue part the place or mesh check asks for (#:catalogue involute-gear-m5-72), or null for any.</summary>
    public string? Catalogue { get; init; }

    /// <summary>The remembered part the placed part must share an axle with (on one arbor, or a chain of them: <see cref="ShaftSlots.AxleMates"/>).</summary>
    public string? OnShaft { get; init; }

    /// <summary>The remembered gear a "mesh" check's new gear meshes with.</summary>
    public string? With { get; init; }

    /// <summary>A "turned" check's speed at the end of the Test, rpm (either way round).</summary>
    public double? Rpm { get; init; }

    /// <summary>A "stage" check's remembered part whose axle the stage's wheel goes on (the sails, or the last stage's pinion).</summary>
    public string? On { get; init; }

    /// <summary>A "stage" check's second name, the pinion's ((as wheel-a pinion-b)): <see cref="LessonCheck.As"/> is the wheel's.</summary>
    public string? As2 { get; init; }

    /// <summary>A "stage" check's pinion teeth ((teeth 72 18)): <see cref="Teeth"/> is the wheel's.</summary>
    public double? Teeth2 { get; init; }

    /// <summary>A "charging" check's watts the bank takes at the end of the Test.</summary>
    public double? Watts { get; init; }

    /// <summary>A "ratio" check's other wheel ((part wheel-a) (to pinion-e)) and how many times it turned for each turn of the first ((is 256)).</summary>
    public string? To { get; init; }
    public double? Is { get; init; }

    /// <summary>
    /// Commands run once the step is done, "{id}" being the part that counted ((then "(set {id} #:water 0)") pours a tank out, for one;
    /// on a join, the rope or pipe, or an arbor's or a mesh's second part), "{drum}" any part the lesson remembers by that name.
    /// </summary>
    public IReadOnlyList<string> Then { get; init; } = [];

    /// <summary>A check on how a Test run came out: the lesson reads the run's facts, and the player presses Test (only "balanced" is run by the lesson itself).</summary>
    public bool IsOutcome => Type is "balanced" or "lifted" or "rolled" or "level" or "turned" or "charging" or "ratio";
}

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
        _baselineLinks = null;
        _baselineMeshes = null;
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
        {
            var c = Lesson.Steps[i].Check;
            bool gone = c is { Type: "place" or "mesh", As: { } role } && (!Roles.TryGetValue(role, out var id) || !doc.Parts.ContainsKey(id))
                     || c.Type == "join" && !JoinMade(doc, c)
                     || c.Type == "mesh" && !MeshMade(doc, c)
                     || c.Type == "stage" && !StageMade(doc, c)
                     || c is { Type: "place", OnShaft: not null, As: { } r2 } && Roles.TryGetValue(r2, out var placed) && !OnShaftOk(doc, c, placed);
            if (!gone) continue;
            Index = i;
            _baseline = null;
            _baselineLinks = null;
        _baselineMeshes = null;
            Nudge = null;
            foreach (var later in Lesson.Steps.Skip(i).SelectMany(s => new[] { s.Check.As, s.Check.As2 }).OfType<string>()) Roles.Remove(later);
            return;
        }
    }

    private void Advance()
    {
        Failure = null;
        Nudge = null;
        _baseline = null;
        _baselineLinks = null;
        _baselineMeshes = null;
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
        if (beam.Kind == "ramp")
        {
            // up the slope from the foot (it rises toward -z at heading 0), then off the slab along its normal
            double a = beam.Number("angle-deg", 15) * Math.PI / 180, hd = beam.Number("heading-deg", 0) * Math.PI / 180;
            double lx = 0, ly = along * Math.Sin(a) + t.Up * Math.Cos(a), lz = -along * Math.Cos(a) + t.Up * Math.Sin(a);
            return new Vec3(beam.At.X + lx * Math.Cos(hd) + lz * Math.Sin(hd), beam.At.Y + ly, beam.At.Z - lx * Math.Sin(hd) + lz * Math.Cos(hd));
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

    /// <summary>The ropes and pipes there were when the step was first looked at: only a link made since is nudged about.</summary>
    private HashSet<string>? _baselineLinks;

    /// <summary>The meshes there were when the step was first looked at ("a+b"): only a mesh made since is nudged about.</summary>
    private HashSet<string>? _baselineMeshes;

    private static string MeshKey(MeshSpec m) => $"{m.A}+{m.B}";

    /// <summary>A step has just been shown (after any settle command): what is on the bench now is not the player's doing, so it is not nudged about.</summary>
    public void Rebase(EditorDocument doc)
    {
        _baseline = doc.Parts.ToDictionary(kv => kv.Key, kv => Signature(kv.Value));
        _baselineLinks = Links(doc).Select(l => l.Id).ToHashSet();
        _baselineMeshes = doc.Meshes.Select(MeshKey).ToHashSet();
        Nudge = null;
    }

    private static string Signature(PartSpec p) => $"{p.Kind}|{N(p.At.X)} {N(p.At.Y)} {N(p.At.Z)}|{p.Material}|{N(p.Number("radius", 0))}";

    private static string KindName(string kind) => kind == "lever" ? "beam" : kind == "tank" ? "water tank" : kind.Contains('-') ? kind[..kind.IndexOf('-')] : kind;

    /// <summary>A part's kind in a person's words: "pulley", "block", "water tank".</summary>
    private static string PartName(PartSpec p) => p.Props.GetValueOrDefault("shape") is SSymbol shape ? shape.Name.Replace('-', ' ') : KindName(p.Kind);

    /// <summary>A part in a person's words: "pulley", "granite block", "water tank".</summary>
    private string Describe(PartSpec p) =>
        p.Kind is "block" or "ball" && materials.TryGet(p.Material, out var m) ? $"{m.Name.ToLowerInvariant()} {PartName(p)}" : PartName(p);

    /// <summary>
    /// Whether the design now does what the current "place" or "join" step asks. When
    /// it does, the step is done; the returned commands, if any, set the
    /// part exactly on the target (any close placement counts), or finish a
    /// join (thread the rope over the pulley). When it does
    /// not, and the player has just placed, moved or joined something, the reason says
    /// why that did not count: the wrong kind of part, the wrong
    /// material, the wrong size, off the target (how far, which way), or joining the wrong two.
    /// </summary>
    public (bool Done, IReadOnlyList<string> Settle, string? Reason) CheckDesign(EditorDocument doc)
    {
        Nudge = null;
        if (Finished || Current is not { } step) return (false, [], null);
        if (step.Check.Type == "join") return CheckJoin(doc, step);
        if (step.Check.Type == "mesh") return CheckMesh(doc, step);
        if (step.Check.Type == "stage") return CheckStage(doc, step);
        if (step.Check.Type != "place") return (false, [], null);
        var c = step.Check;
        var bound = Roles.Values.ToHashSet();
        _baseline ??= doc.Parts.ToDictionary(kv => kv.Key, kv => Signature(kv.Value));
        IEnumerable<PartSpec> pool = c.Part is { } role
            ? Roles.TryGetValue(role, out var known) && doc.Parts.TryGetValue(known, out var p) ? [p] : []
            : doc.Parts.Values.Where(x => !bound.Contains(x.Id));
        var changed = pool.Where(x => !_baseline.TryGetValue(x.Id, out var sig) || sig != Signature(x)).ToList();
        string? CategoryOf(PartSpec x) => materials.TryGet(x.Material, out var m) ? m.Category.ToString() : null;
        bool ShapeOk(PartSpec x) => c.Shape is null || x.Props.GetValueOrDefault("shape") is SSymbol sh && sh.Name == c.Shape;
        bool KindOk(PartSpec x) => (c.Kind is null || x.Kind == c.Kind) && ShapeOk(x);
        bool CategoryOk(PartSpec x) => c.Category is null || CategoryOf(x)?.Equals(c.Category, StringComparison.OrdinalIgnoreCase) == true;
        bool SizeOk(PartSpec x) => c.Radius is not { } want || Math.Abs(x.Number("radius", 0) - want) < 1e-6;
        bool GearOk(PartSpec x) => TeethOk(c, x) && CatalogueOk(c, x);
        bool ShaftOk(PartSpec x) => OnShaftOk(doc, c, x.Id);
        var candidates = pool.Where(x => KindOk(x) && CategoryOk(x) && SizeOk(x) && GearOk(x) && ShaftOk(x)).ToList();
        var target = TargetAt(doc);

        if (c.Part is { } r && (!Roles.TryGetValue(r, out var gone) || !doc.Parts.ContainsKey(gone)))
        {
            Nudge = $"The {r} part is not on the bench any more. Undo your last change, or press Start over.";
            return (false, [], Nudge);
        }

        if (step.Target is not null && target is null)
        {
            // what the part goes on is gone (a resumed lesson normally goes back a step; this is an edit made since)
            if (candidates.Count > 0 || changed.Count > 0)
                Nudge = $"This step puts the {KindName(step.Target.Kind)} {(step.Target.On is { } on ? $"on the {on}" : "in place")}, but there is no {step.Target.On ?? "target"} on the bench any more. Press Start over to begin again.";
            return (false, [], Nudge);
        }

        var best = candidates.Select(x => (Part: x, Off: target is { } t ? Distance(x.At, t) : 0)).OrderBy(x => x.Off).FirstOrDefault();
        if (best.Part is not null && !(target is { } && best.Off > step.Target!.Within))
        {
            if (c.As is { } name) Roles[name] = best.Part.Id;
            IReadOnlyList<string> settle = target is { } to && best.Off > 1e-4 ? [$"(move {best.Part.Id} ({N(to.X)} {N(to.Y)} {N(to.Z)}))"] : [];
            settle = [.. settle, .. ThenLines(c, best.Part.Id)];
            Advance();
            return (true, settle, null);
        }

        if (changed.Count == 0) return (false, [], null);

        // the part that came nearest to counting is the one to talk about: right kind and material but off, then the wrong size, then the wrong material, then the wrong part
        int Rank(PartSpec x) => !KindOk(x) ? 3 : !CategoryOk(x) ? 2 : !SizeOk(x) || !GearOk(x) || !ShaftOk(x) ? 1 : 0;
        var about = changed.OrderBy(Rank).ThenBy(x => target is { } t ? Distance(x.At, t) : 0).First();
        string used = PartName(about), asked = c.Shape ?? KindName(c.Kind ?? about.Kind);
        string matName = materials.TryGet(about.Material, out var mat) ? mat.Name.ToLowerInvariant() : about.Material;
        string Cap(string w) => char.ToUpperInvariant(w[0]) + w[1..];
        if (!KindOk(about))
            Nudge = $"That is a {used}, but this step needs a {asked}. Take the {used} away and pick {Cap(asked)} in the parts list.";
        else if (!CategoryOk(about))
            Nudge = $"That {used} is {matName}, {(CategoryOf(about) is { } cat ? $"a {cat.ToLowerInvariant()}" : "not the right stuff")}, but this step asks for a {c.Category!.ToLowerInvariant()} one"
                  + (step.Material is { } want ? $", such as {want}" : "")
                  + $". Take it away and pick {Cap(asked)} again: the lesson picks the material on its card.";
        else if (!SizeOk(about))
            Nudge = $"That {used} is {N(Math.Round(about.Number("radius", 0) * 100, 1))} cm in radius, but this lesson uses the {N(Math.Round(c.Radius!.Value * 100, 1))} cm one. "
                  + $"Take it away, pick the right size in the box under the parts list and place it again.";
        else if (!GearOk(about))
            Nudge = GearNudge(c, about) + " Take it away, pick the right size in the box under the parts list and place it again.";
        else if (!ShaftOk(about))
            Nudge = $"That {used} is not on the {c.OnShaft}'s axle. Take it away, select the {c.OnShaft} and press Put a {ShaftSlots.Noun(about.Symbol("shape", "gear"))} on this shaft.";
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
        return (false, [], Nudge);
    }

    /// <summary>
    /// The name the current step would remember a new part by, when that part would count (its kind, shape, size, teeth, catalogue part
    /// and material: not where it is), so the editor can give it that id as it is placed ("crank", not "gear_2"): what a person reads on
    /// its label and on the train's ratio ("crank -> third: 2:1"). Null when the step remembers nothing new, already knows that name, or
    /// the part would not count. Naming it so never changes what counts: the check still decides.
    /// </summary>
    public string? RoleFor(PartSpec part)
    {
        if (Finished || Current?.Check is not { Type: "place" or "mesh", As: { } role, Part: null } c || Roles.ContainsKey(role)) return null;
        bool shape = c.Shape is null || part.Props.GetValueOrDefault("shape") is SSymbol sh && sh.Name == c.Shape;
        bool category = c.Category is null || materials.TryGet(part.Material, out var m) && m.Category.ToString().Equals(c.Category, StringComparison.OrdinalIgnoreCase);
        bool size = c.Radius is not { } want || Math.Abs(part.Number("radius", 0) - want) < 1e-6;
        return (c.Kind is null || part.Kind == c.Kind) && shape && category && size && TeethOk(c, part) && CatalogueOk(c, part) ? role : null;
    }

    /// <summary>A done step's then-lines: {id} the part (or link) that counted, {ROLE} the part the lesson remembers by that name.</summary>
    private IEnumerable<string> ThenLines(LessonCheck c, string id) =>
        c.Then.Select(t => System.Text.RegularExpressions.Regex.Replace(t.Replace("{id}", id), @"\{([a-z][a-z0-9-]*)\}",
                                                                        m => Roles.TryGetValue(m.Groups[1].Value, out var part) ? part : m.Value));

    /// <summary>Whether a part has the teeth a check asks for (any, when it names none).</summary>
    private static bool TeethOk(LessonCheck c, PartSpec x) => c.Teeth is not { } want || Math.Abs(x.Number("teeth", 0) - want) < 1e-6;

    /// <summary>Whether a part is the catalogue part a check asks for (any, when it names none).</summary>
    private static bool CatalogueOk(LessonCheck c, PartSpec x) =>
        c.Catalogue is null || x.Props.GetValueOrDefault("catalogue") is SSymbol cat && cat.Name == c.Catalogue;

    /// <summary>Whether a part shares an axle with the part a check's (on-shaft ROLE) names (true when it names none).</summary>
    private bool OnShaftOk(EditorDocument doc, LessonCheck c, string id) =>
        c.OnShaft is not { } role || Roles.TryGetValue(role, out var on) && on != id && ShaftSlots.AxleMates(doc, on).Contains(id);

    /// <summary>Why a gear isn't the one asked for: its teeth, or which catalogue part it is.</summary>
    private static string GearNudge(LessonCheck c, PartSpec x)
    {
        double teeth = x.Number("teeth", 0);
        if (!TeethOk(c, x))
            return teeth > 0 ? $"That gear has {N(teeth)} teeth, but this step needs the one with {N(c.Teeth!.Value)}."
                             : $"That {PartName(x)} has no teeth, but this step needs a gear with {N(c.Teeth!.Value)}.";
        string what = x.Props.GetValueOrDefault("catalogue") is SSymbol cat ? cat.Name : PartName(x);
        return c.Catalogue is { } want ? $"That is the {what}, but this step needs the {want}." : $"That {PartName(x)} is not the part this step needs.";
    }

    // --------------------------------------------------------- meshes

    /// <summary>A gear in a person's words: "the crank gear" for one the lesson knows, else "a new gear".</summary>
    private string GearNamed(EditorDocument doc, string id) =>
        Roles.FirstOrDefault(kv => kv.Value == id).Key is { } role ? $"the {role} gear" : doc.Parts.TryGetValue(id, out var p) ? $"a new {PartName(p)}" : id;

    /// <summary>Whether the mesh a step asks for is in the design: its remembered gear meshed with the one it names (either way round).</summary>
    private bool MeshMade(EditorDocument doc, LessonCheck c) =>
        c.With is { } w && c.As is { } a && Roles.TryGetValue(w, out var wid) && Roles.TryGetValue(a, out var aid)
        && doc.Meshes.Any(m => m.A == wid && m.B == aid || m.A == aid && m.B == wid);

    /// <summary>
    /// A "mesh" step: a gear not yet known to the lesson, meshed with the remembered one (the inspector's "Add a meshing gear", or the
    /// Gear mesh tool), with the teeth the step asks for. When it is there the step is done and its then-lines run ({id} the new gear).
    /// Only a mesh made since the step began is nudged about (a gear placed but not meshed yet is the mouse tool's first command, not a
    /// mistake): meshed with the wrong gear, or the wrong size of gear.
    /// </summary>
    private (bool Done, IReadOnlyList<string> Settle, string? Reason) CheckMesh(EditorDocument doc, LessonStep step)
    {
        var c = step.Check;
        _baselineMeshes ??= doc.Meshes.Select(MeshKey).ToHashSet();
        if (c.With is not { } withRole || !Roles.TryGetValue(withRole, out var w) || !doc.Parts.ContainsKey(w))
        {
            Nudge = $"The {c.With} gear is not on the bench any more. Undo your last change, or press Start over.";
            return (false, [], Nudge);
        }
        var bound = Roles.Values.ToHashSet();
        bool Fits(PartSpec x) => (c.Kind is null || x.Kind == c.Kind) && TeethOk(c, x) && CatalogueOk(c, x)
                                 && (c.Category is null || materials.TryGet(x.Material, out var m) && m.Category.ToString().Equals(c.Category, StringComparison.OrdinalIgnoreCase));
        var partners = doc.Meshes.Where(m => m.A == w || m.B == w).Select(m => m.A == w ? m.B : m.A)
                                 .Where(id => !bound.Contains(id) && doc.Parts.ContainsKey(id)).Select(id => doc.Parts[id]).ToList();
        if (partners.FirstOrDefault(Fits) is { } got)
        {
            if (c.As is { } name) Roles[name] = got.Id;
            IReadOnlyList<string> settle = [.. ThenLines(c, got.Id)];
            Advance();
            return (true, settle, null);
        }
        var fresh = doc.Meshes.Where(m => !_baselineMeshes.Contains(MeshKey(m))).ToList();
        if (fresh.Count == 0) return (false, [], null);
        var made = fresh[^1];
        string again = $"Undo it (Ctrl+Z), select the {withRole} gear and press Add a meshing gear.";
        if (made.A != w && made.B != w)
            Nudge = $"That meshes {GearNamed(doc, made.A)} with {GearNamed(doc, made.B)}, but this step's gear meshes with the {withRole} gear. {again}";
        else if (doc.Parts.TryGetValue(made.A == w ? made.B : made.A, out var other) && !bound.Contains(other.Id))
            Nudge = GearNudge(c, other) + $" Undo it (Ctrl+Z), pick the right size on the gear's card and press Add a meshing gear again.";
        else
            Nudge = $"That meshes the {withRole} gear with one the lesson already has. {again}";
        return (false, [], Nudge);
    }

    // --------------------------------------------------------- stages

    /// <summary>Whether a teeth count is the one asked (any, when none is).</summary>
    private static bool TeethIs(PartSpec p, double? want) => want is not { } w || Math.Abs(p.Number("teeth", 0) - w) < 1e-6;

    /// <summary>Whether the stage a step asks for is in the design: its remembered wheel on the (on) part's axle, meshed with its remembered pinion.</summary>
    private bool StageMade(EditorDocument doc, LessonCheck c) =>
        c.On is { } on && Roles.TryGetValue(on, out var onId) && c.As is { } a && c.As2 is { } b && Roles.TryGetValue(a, out var w) && Roles.TryGetValue(b, out var p)
        && doc.Parts.ContainsKey(w) && doc.Parts.ContainsKey(p) && ShaftSlots.AxleMates(doc, onId).Contains(w)
        && doc.Meshes.Any(m => m.A == w && m.B == p || m.A == p && m.B == w);

    /// <summary>
    /// A "stage" step (tutorial plan, gap 2: (check stage (on sails) (as wheel-a pinion-b) (teeth 72 18))): a wheel not yet known to the
    /// lesson on the (on) part's axle, meshed with a pinion not yet known either, with the teeth asked: the inspector's "Add a stage", one
    /// (begin ...) edit (or "Put a gear on this shaft" and "Add a meshing gear" one after the other: the mesh completes it). Both are
    /// remembered at once, the wheel by the first name and the pinion by the second; the then-lines' {id} is the pinion. A stage made since
    /// the step began that doesn't count is said: off the axle, or the wrong sizes.
    /// </summary>
    private (bool Done, IReadOnlyList<string> Settle, string? Reason) CheckStage(EditorDocument doc, LessonStep step)
    {
        var c = step.Check;
        _baselineMeshes ??= doc.Meshes.Select(MeshKey).ToHashSet();
        if (c.On is not { } onRole || !Roles.TryGetValue(onRole, out var on) || !doc.Parts.ContainsKey(on))
        {
            Nudge = $"The {c.On} is not on the bench any more. Undo your last change, or press Start over.";
            return (false, [], Nudge);
        }
        var bound = Roles.Values.ToHashSet();
        var axle = ShaftSlots.AxleMates(doc, on);
        foreach (var m in doc.Meshes)
            foreach (var (w, p) in new[] { (m.A, m.B), (m.B, m.A) })
            {
                if (bound.Contains(w) || bound.Contains(p) || !axle.Contains(w) || !doc.Parts.TryGetValue(w, out var wheel) || !doc.Parts.TryGetValue(p, out var pinion)
                    || !TeethIs(wheel, c.Teeth) || !TeethIs(pinion, c.Teeth2)) continue;
                if (c.As is { } a) Roles[a] = w;
                if (c.As2 is { } b) Roles[b] = p;
                IReadOnlyList<string> settle = [.. ThenLines(c, p)];
                Advance();
                return (true, settle, null);
            }
        var fresh = doc.Meshes.Where(m => !_baselineMeshes.Contains(MeshKey(m))).ToList();
        if (fresh.Count == 0) return (false, [], null);
        var made = fresh[^1];
        string Sizes(PartSpec w, PartSpec p) => $"{N(w.Number("teeth", 0))} drives {N(p.Number("teeth", 0))}";
        string asked = c.Teeth is { } wt && c.Teeth2 is { } pt ? $"{N(wt)} drives {N(pt)}" : "";
        var (mw, mp) = axle.Contains(made.A) ? (made.A, made.B) : (made.B, made.A);
        if (!axle.Contains(mw))
            Nudge = $"That stage is not on the {onRole}'s axle. Undo it (Ctrl+Z), select the {onRole} and press Add a stage.";
        else if (bound.Contains(mw) || bound.Contains(mp))
            Nudge = $"That meshes a gear the lesson already has. Undo it (Ctrl+Z), select the {onRole} and press Add a stage.";
        else if (doc.Parts.TryGetValue(mw, out var gw) && doc.Parts.TryGetValue(mp, out var gp))
            Nudge = $"That stage is {Sizes(gw, gp)}, but this step's is {asked}. Undo it (Ctrl+Z), pick {asked.Replace(" drives ", " and ")} under Add a stage and press it again.";
        return (false, [], Nudge);
    }

    /// <summary>
    /// The names the current "stage" step would remember a stage by, when one of these sizes put on <paramref name="anchor"/>'s axle would
    /// count: the wheel's and the pinion's ("wheel-a", "pinion-b"), so "Add a stage" can give the two gears those ids as it builds them and
    /// the live ratio reads "sails -> pinion-e: 256:1". Null when the step isn't a stage on that axle, the sizes are not the step's, or the
    /// names are taken. Naming them so never changes what counts.
    /// </summary>
    public (string Wheel, string Pinion)? StageRolesFor(EditorDocument doc, string anchor, PartSpec wheel, PartSpec pinion)
    {
        if (Finished || Current?.Check is not { Type: "stage", On: { } on, As: { } a, As2: { } b } c || Roles.ContainsKey(a) || Roles.ContainsKey(b)) return null;
        if (!Roles.TryGetValue(on, out var onId) || !ShaftSlots.AxleMates(doc, onId).Contains(anchor)) return null;
        return TeethIs(wheel, c.Teeth) && TeethIs(pinion, c.Teeth2) ? (a, b) : null;
    }

    // --------------------------------------------------------- joins

    private readonly record struct Link(string Id, string Kind, string A, string B);

    /// <summary>The design's joins: ropes and pipes by their own ids; meshes ("a+b") and arbors (each wheel after an axle's first, "first+wheel").</summary>
    private static List<Link> Links(EditorDocument doc) =>
        [.. doc.Ropes.Select(r => new Link(r.Id, "rope", r.From.Part, r.To.Part)),
         .. doc.Pipes.Values.Select(p => new Link(p.Id, "pipe", p.From.Part, p.To.Part)),
         .. doc.Meshes.Select(m => new Link(MeshKey(m), "mesh", m.A, m.B)),
         .. doc.Arbors.SelectMany(a => a.Parts.Skip(1).Select(p => new Link($"{a.Parts[0]}+{p}", "arbor", a.Parts[0], p)))];

    /// <summary>Whether a join of a step's kind is between parts a and b, either way round; for an arbor, whether they turn on one axle at all.</summary>
    private static bool Joins(EditorDocument doc, Link l, string kind, string a, string b) =>
        l.Kind == kind && (l.A == a && l.B == b || l.A == b && l.B == a
                           || kind == "arbor" && (l.A == a || l.B == a) && ShaftSlots.AxleMates(doc, a).Contains(b));

    /// <summary>Whether the join a step asks for is in the design between the parts it names (either way round).</summary>
    private bool JoinMade(EditorDocument doc, LessonCheck c) =>
        c.Between is { } pair && Roles.TryGetValue(pair.A, out var a) && Roles.TryGetValue(pair.B, out var b)
        && Links(doc).Any(l => Joins(doc, l, c.Kind ?? "", a, b));

    private string Named(EditorDocument doc, string part) => doc.Parts.TryGetValue(part, out var p) ? $"the {Describe(p)}" : part;

    private (bool Done, IReadOnlyList<string> Settle, string? Reason) CheckJoin(EditorDocument doc, LessonStep step)
    {
        var c = step.Check;
        _baselineLinks ??= Links(doc).Select(l => l.Id).ToHashSet();
        if (c.Between is not { } pair || !Roles.TryGetValue(pair.A, out var a) || !Roles.TryGetValue(pair.B, out var b)
            || !doc.Parts.ContainsKey(a) || !doc.Parts.ContainsKey(b))
        {
            Nudge = "A part this step joins is not on the bench any more. Undo your last change, or press Start over.";
            return (false, [], Nudge);
        }
        var links = Links(doc);
        if (links.FirstOrDefault(l => Joins(doc, l, c.Kind ?? "", a, b)) is { Id: not null } made)
        {
            IReadOnlyList<string> settle = c.Kind != "rope" ? []
                : c.WindOn is not null ? WindRope(doc, made, pair, c)
                : c.Over is not null ? ThreadRope(doc, made, pair.A, pair.B, c) : [];
            settle = [.. settle, .. ThenLines(c, made.Kind is "rope" or "pipe" ? made.Id : b)];
            Advance();
            return (true, settle, null);
        }
        var fresh = links.Where(l => !_baselineLinks.Contains(l.Id)).ToList();
        if (fresh.Count == 0) return (false, [], null);
        var about = fresh.FirstOrDefault(l => l.Kind != c.Kind, fresh[^1]);
        string nameA = Named(doc, a), nameB = Named(doc, b);
        if (nameA == nameB) (nameA, nameB) = ($"the {pair.A} {PartName(doc.Parts[a])}", $"the {pair.B} {PartName(doc.Parts[b])}");   // two tanks: say which is which
        string wanted = $"between {nameA} and {nameB}";
        Nudge = about.Kind != c.Kind
            ? $"That is a {about.Kind}, but this step needs a {c.Kind}. Undo it (Ctrl+Z) and make a {c.Kind} {wanted}."
            : $"That {about.Kind} joins {Named(doc, about.A)} to {Named(doc, about.B)}, but this step needs it {wanted}. Undo it (Ctrl+Z) and join those two.";
        return (false, [], Nudge);
    }

    /// <summary>
    /// The rope the player tied straight between two blocks is taken off and tied again over the pulley, from the top of "heavy" up its
    /// side of the rim, round, and down to the top of "light", cut to the length of that path; and "heavy" is hung <c>hoist</c> above where
    /// it stood. The rim is the five points the engine's own pulley rig uses (180° to 0° in 45° steps), so the path is 4 chords of 2 r sin 22.5°
    /// round the top. The pulley turns about a horizontal axle across the rope (z).
    /// </summary>
    private IReadOnlyList<string> ThreadRope(EditorDocument doc, Link made, string heavyRole, string lightRole, LessonCheck c)
    {
        var pulley = doc.Parts[Roles[c.Over!]];
        var heavy = doc.Parts[Roles[heavyRole]];
        var light = doc.Parts[Roles[lightRole]];
        double r = pulley.Number("radius", 0.1), cx = pulley.At.X, cy = pulley.At.Y, cz = pulley.At.Z;
        double hs = heavy.Number("size", 0.1) / 2, ls = light.Number("size", 0.1) / 2;
        var hangs = new Vec3(cx - r, hs + c.Hoist, cz);
        var stands = new Vec3(cx + r, ls, cz);
        double length = (cy - (hangs.Y + hs)) + 4 * 2 * r * Math.Sin(Math.PI / 8) + (cy - (stands.Y + ls));
        var rim = new[] { 180, 135, 90, 45, 0 }.Select(deg => $"({N(cx + r * Math.Cos(deg * Math.PI / 180))} {N(cy + r * Math.Sin(deg * Math.PI / 180))} {N(cz)})");
        return
        [
            $"(remove {made.Id})",
            $"(move {heavy.Id} ({N(hangs.X)} {N(hangs.Y)} {N(hangs.Z)}))",
            $"(move {light.Id} ({N(stands.X)} {N(stands.Y)} {N(stands.Z)}))",
            $"(rope {made.Id} #:from ({heavy.Id} 0 {N(hs)} 0) #:to ({light.Id} 0 {N(ls)} 0) #:length {N(length)} #:over ({string.Join(' ', rim)}) #:turns {pulley.Id} #:material hemp #:diameter 0.01)",
        ];
    }

    /// <summary>
    /// The rope the player tied between a drum and its load is taken off and tied again wound on the drum, (rope … #:wind-on drum), as the
    /// engine's own windlasses and cranes are: it leaves the drum's rim on the side that turning the drum forward (+ about its axle, the
    /// way a positive #:drive-rpm turns it) winds it on, and runs to the top of the load, hung <c>hang</c> above where it stood on the
    /// ground. Without a pulley the load hangs plumb under that side of the rim, x = drum + r across the axle (the rope's path is then
    /// straight down from the rim). With (over pulley) the rope goes from the drum to the pulley's near side, over its top (the five rim
    /// points <see cref="ThreadRope"/> uses, here from the side facing the drum round to the far one) and down its far side to the load,
    /// hung plumb under it; the pulley turns with the rope (#:turns). Cut to the length of that path: the tangent off the drum
    /// (sqrt(d² − r²) across the axle, d from the drum's middle to the pulley's near side, and any run along the axle), 4 chords of
    /// 2 r sin 22.5° round the pulley, and the drop. So the rope starts just taut. It keeps the material and thickness the player tied.
    /// </summary>
    private IReadOnlyList<string> WindRope(EditorDocument doc, Link made, (string A, string B) pair, LessonCheck c)
    {
        var drum = doc.Parts[Roles[c.WindOn!]];
        var load = doc.Parts[Roles[pair.A == c.WindOn ? pair.B : pair.A]];
        var tied = doc.Ropes.First(r => r.Id == made.Id);
        double r = drum.Number("radius", 0.1);
        double half = load.Kind == "ball" ? load.Number("radius", 0.05) : load.Number("size", 0.1) / 2;
        double hangY = half + c.Hoist;
        var axle = Unit(Neg(ShaftSlots.BehindWheel(drum)));
        string over = "";
        Vec3 hangs;
        double length;
        if (c.Over is { } pulleyRole && Roles.TryGetValue(pulleyRole, out var pid) && doc.Parts.TryGetValue(pid, out var pulley))
        {
            double rp = pulley.Number("radius", 0.1);
            var p = pulley.At;
            var across = Across(Unit(Neg(ShaftSlots.BehindWheel(pulley))));
            var far = Dot(Minus(p, drum.At), across) >= 0 ? across : Neg(across);   // away from the drum
            Vec3 Rim(double deg) => new(p.X + rp * Math.Cos(deg * Math.PI / 180) * far.X, p.Y + rp * Math.Sin(deg * Math.PI / 180), p.Z + rp * Math.Cos(deg * Math.PI / 180) * far.Z);
            var rim = new double[] { 180, 135, 90, 45, 0 }.Select(Rim).ToList();
            var d = Minus(rim[0], drum.At);
            double along = Dot(d, axle), inPlane2 = Dot(d, d) - along * along;
            double tangent = Math.Sqrt(Math.Max(0, inPlane2 - r * r) + along * along);
            hangs = new Vec3(rim[^1].X, hangY, rim[^1].Z);
            length = tangent + 4 * 2 * rp * Math.Sin(Math.PI / 8) + (p.Y - (hangY + half));
            over = $" #:over ({string.Join(' ', rim.Select(q => $"({N(q.X)} {N(q.Y)} {N(q.Z)})"))}) #:turns {pulley.Id}";
        }
        else
        {
            var side = Across(axle);   // turning forward about the axle carries this side of the rim up
            hangs = new Vec3(drum.At.X + r * side.X, hangY, drum.At.Z + r * side.Z);
            length = drum.At.Y - (hangY + half);
        }
        return
        [
            $"(remove {made.Id})",
            $"(move {load.Id} ({N(hangs.X)} {N(hangs.Y)} {N(hangs.Z)}))",
            $"(rope {made.Id} #:from ({drum.Id} 0 0 0) #:to ({load.Id} 0 {N(half)} 0) #:length {N(length)} #:wind-on {drum.Id}{over} #:material {tied.Material} #:diameter {N(tied.Diameter)})",
        ];
    }

    /// <summary>The level way across an axle that turning forward about it carries up: up × axle (an axle along +z: +x), or +x for an upright axle.</summary>
    private static Vec3 Across(Vec3 axle)
    {
        var s = new Vec3(axle.Z, 0, -axle.X);
        double n = Math.Sqrt(s.X * s.X + s.Z * s.Z);
        return n < 1e-9 ? new Vec3(1, 0, 0) : new Vec3(s.X / n, 0, s.Z / n);
    }

    private static Vec3 Neg(Vec3 v) => new(-v.X, -v.Y, -v.Z);
    private static Vec3 Minus(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static Vec3 Unit(Vec3 v) { double n = Math.Sqrt(Dot(v, v)); return n < 1e-12 ? v : new Vec3(v.X / n, v.Y / n, v.Z / n); }

    // --------------------------------------------------------- outcomes

    /// <summary>What the last Test run saw (its facts), or null before any; once the lesson is finished, the run that finished it.</summary>
    public TestFacts? LastFacts { get; private set; }

    /// <summary>
    /// A test run ended: a "test" step is done; a "balanced" step is done
    /// when the beam's traced tilt never left ±within-deg; a "lifted", "rolled" or "level" step is done when the run's facts (what rose, how fast
    /// a ball went, where the water ended) match the worked number; otherwise it says what
    /// happened. Each beam's tilts are degrees, + when its +x end is up.
    /// </summary>
    public bool OnTest(IReadOnlyDictionary<string, List<double>> tilts, IReadOnlyDictionary<string, double> masses, TestFacts? facts = null, EditorDocument? doc = null)
    {
        foreach (var (id, m) in masses) SimMasses[id] = m;
        if (facts is not null && !Finished) LastFacts = facts;   // a finished lesson's words keep the numbers of the Test that finished it (the player tries one walker after)
        var beamRole = Current?.Check.Beam ?? Roles.Keys.FirstOrDefault(r => r == "beam");
        if (beamRole is not null && Roles.TryGetValue(beamRole, out var beamId) && tilts.TryGetValue(beamId, out var trace))
            LastTilt = trace.Select(Math.Abs).DefaultIfEmpty(0).Max();
        if (Finished || Current is not { } step) return false;
        var c = step.Check;
        switch (c.Type)
        {
            case "test":
                Advance();
                return true;
            case "balanced":
                if (LastTilt is { } most && most <= c.WithinDeg) { Advance(); return true; }
                Failure = LastTilt is { } t ? $"Not balanced yet: the beam leaned {t:0.#}° (it has to stay within {c.WithinDeg:0.#}°)."
                                            : "The test didn't see the beam.";
                return false;
            case "lifted" or "rolled" or "level" or "turned" or "charging" or "ratio":
                return OutcomeOk(step, doc);
        }
        return false;
    }

    /// <summary>A Test's outcome against every check of the step: all met, the step is done; else the step's fail text, or the first unmet check in plain words.</summary>
    private bool OutcomeOk(LessonStep step, EditorDocument? doc)
    {
        string? first = null;
        foreach (var c in step.Checks)
            if (!OutcomeMet(c, doc, out var plain)) first ??= plain;
        if (first is null) { Advance(); return true; }
        Failure = step.Fail is { } fail && doc is not null ? Fill(fail, doc) : first;
        return false;
    }

    private bool OutcomeMet(LessonCheck c, EditorDocument? doc, out string plain)
    {
        if (LastFacts is not { } facts || c.Part is not { } role || !Roles.TryGetValue(role, out var id)) { plain = "The test didn't see the part."; return false; }
        bool ok;
        switch (c.Type)
        {
            case "lifted":
                ok = facts.RiseOf(id) is { } rise && Math.Abs(rise.Metres - (c.About ?? 0)) <= c.Within;
                plain = $"The {role} part rose {N(Math.Round(facts.RiseOf(id)?.Metres ?? 0, 2))} m, and it should have gone up {N(c.About ?? 0)} m.";
                break;
            case "rolled":
            {
                var move = facts.RiseOf(id);
                double want = doc is not null ? RollingSpeed(doc, id) : 0;
                ok = move is not null && Math.Abs(move.TopSpeed - want) <= c.Within && move.Across >= (c.Far ?? 0);
                plain = $"The {role} went at most {N(Math.Round(move?.TopSpeed ?? 0, 2))} m/s and ended {N(Math.Round(move?.Across ?? 0, 1))} m from where it began; rolling down from there should give {N(Math.Round(want, 2))} m/s (within {N(c.Within)}) and at least {N(c.Far ?? 0)} m.";
                break;
            }
            case "turned":
            {
                double rpm = Math.Abs(facts.TurnOf(id)?.EndRpm ?? 0);
                ok = facts.TurnOf(id) is not null && Math.Abs(rpm - (c.Rpm ?? 0)) <= c.Within;
                plain = $"The {role} gear was turning at {N(Math.Round(rpm, 1))} rpm at the end, and it should have been {N(c.Rpm ?? 0)} rpm.";
                break;
            }
            case "charging":
            {
                var gen = facts.GeneratorOf(id);
                ok = gen is not null && Math.Abs(gen.EndDelivered - (c.Watts ?? 0)) <= c.Within;
                plain = gen is null ? $"The test didn't see the {role} charge anything."
                      : $"The {role} was charging the bank at {N(Math.Round(gen.EndDelivered))} W at the end, its rotor at {N(Math.Round(gen.EndRpm))} rpm, and it should have been {N(c.Watts ?? 0)} W.";
                break;
            }
            case "ratio":
            {
                double? ratio = RatioOf(facts, c);
                ok = ratio is { } r && Math.Abs(r - (c.Is ?? 0)) <= c.Within;
                plain = $"The {c.To} turned {N(Math.Round(ratio ?? 0, 1))} times for each turn of the {role}, and it should have been {N(c.Is ?? 0)}.";
                break;
            }
            default:
                ok = facts.TankOf(id) is { } tank && Math.Abs(tank.EndLitres - (c.Litres ?? 0)) <= c.Within;
                plain = $"The {role} tank ended with {N(Math.Round(facts.TankOf(id)?.EndLitres ?? 0, 1))} litres, and it should have {N(c.Litres ?? 0)}.";
                break;
        }
        return ok;
    }

    /// <summary>How many times a "ratio" check's (to) wheel turned for each turn of its (part), from the Test's traced turns (either way round), or null.</summary>
    private double? RatioOf(TestFacts facts, LessonCheck c) =>
        c.Part is { } a && c.To is { } b && Roles.TryGetValue(a, out var aid) && Roles.TryGetValue(b, out var bid)
        && facts.TurnOf(aid) is { Turns: var ta } && Math.Abs(ta) > 1e-9 && facts.TurnOf(bid) is { Turns: var tb } ? Math.Abs(tb / ta) : null;

    // --------------------------------------------------------- numbers worked from the design

    /// <summary>
    /// How fast a solid ball is going at the foot of the slope it starts on, m/s: it gives up the height its centre falls, h, and a share 2/7
    /// of that goes into turning (I = 2/5 m r²), so v = sqrt(10/7 g h) where h is how far its centre is above where it comes to rest on flat ground (one radius).
    /// </summary>
    public static double RollingSpeed(EditorDocument doc, string ball)
    {
        var part = doc.Parts[ball];
        return RollingSpeed(part.At.Y - part.Number("radius", 0.05));
    }

    public static double RollingSpeed(double drop) => Math.Sqrt(10.0 / 7 * Physics.Gravity * Math.Max(0, drop));

    /// <summary>
    /// How fast a rope over a pulley speeds up its two blocks: a = g (m1 − m2) / (m1 + m2). The engine's pulley turns to keep pace with the
    /// rope and does not slow it (measured in a trace: 5.68 m/s², and the rope's pull 11.152 N = 2 m1 m2 g / (m1 + m2) to the digit).
    /// </summary>
    public double Acceleration(EditorDocument doc)
    {
        if (!Roles.TryGetValue("heavy", out var h) || !Roles.TryGetValue("light", out var l) || !doc.Parts.ContainsKey(h) || !doc.Parts.ContainsKey(l)) return 0;
        double m1 = MassOf(doc, h), m2 = MassOf(doc, l);
        return Physics.Gravity * (m1 - m2) / (m1 + m2);
    }

    /// <summary>
    /// How long the upper of two equal tanks, joined by a pipe of conductance k (m³/s per metre of head), takes to run dry, s:
    /// with a, b their water depths and D how much higher the upper stands, da/dt = −(k/A)(D + 2a − (a0 + b0)), so a falls to nothing at
    /// t = (A / 2k) ln((a0 − c) / −c) with c = (a0 + b0 − D)/2 (below zero when it will empty); 0 when it never does.
    /// </summary>
    public double TimeToEmpty(EditorDocument doc)
    {
        if (!Roles.TryGetValue("upper", out var u) || !Roles.TryGetValue("lower", out var l) || !doc.Parts.TryGetValue(u, out var up) || !doc.Parts.TryGetValue(l, out var low)) return 0;
        double area = up.Number("area", 0.05), a0 = up.Number("water", 0) / area, b0 = low.Number("water", 0) / area;
        double k = doc.Pipes.Values.FirstOrDefault(p => p.From.Part == u && p.To.Part == l || p.From.Part == l && p.To.Part == u)?.Conductance ?? 0.001;
        double c = (a0 + b0 - (up.At.Y - low.At.Y)) / 2;
        return c < 0 && a0 > 0 ? area / (2 * k) * Math.Log((a0 - c) / -c) : 0;
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
            if (field == "" && Number(name, doc) is { } text2) return text2;
            if (!Roles.TryGetValue(name, out var id) || !doc.Parts.ContainsKey(id)) return "?";
            return field switch
            {
                "mass" => $"{N(MassOf(doc, id))} kg",
                "dist" => beam != "" ? $"{N(Math.Abs(Along(doc, beam, id)))} m" : "?",
                "turn" => beam != "" ? N(MassOf(doc, id) * Math.Abs(Along(doc, beam, id))) : "?",
                "material" => materials.TryGet(doc.Parts[id].Material, out var mat) ? mat.Name.ToLowerInvariant() : doc.Parts[id].Material,
                "litres" => $"{N(Math.Round(doc.Parts[id].Number("water", 0) * 1000, 1))} litres",
                "level" => $"{N(Math.Round(doc.Parts[id].Number("water", 0) / doc.Parts[id].Number("area", 0.05) * 100, 1))} cm",
                "rpm" => LastFacts?.TurnOf(id) is { } turn ? $"{N(Math.Round(Math.Abs(turn.EndRpm)))} rpm" : "?",
                "watts" => LastFacts?.GeneratorOf(id) is { } gen ? $"{N(Math.Round(gen.EndDelivered))} W" : "?",
                _ => m.Value,
            };
        });
    }

    /// <summary>The worked and measured numbers a lesson's words can quote, or null when the name is not one of them (or not known yet).</summary>
    private string? Number(string name, EditorDocument doc)
    {
        string Metres(double v) => $"{N(Math.Round(v, 2))} m";
        var check = Current?.Check ?? Lesson.Steps[^1].Check;
        var outcome = Lesson.Steps.Select(s => s.Check).LastOrDefault(c => c.Type is "lifted" or "rolled" or "level" or "turned" or "charging" or "ratio") ?? check;
        string? partId = outcome.Part is { } role && Roles.TryGetValue(role, out var pid) ? pid : null;
        switch (name)
        {
            case "hoist": return Lesson.Steps.Select(s => s.Check).FirstOrDefault(c => c.Type == "join" && c.Hoist > 0) is { } j ? Metres(j.Hoist) : "?";
            case "expect": return outcome.Type switch
            {
                "turned" => $"{N(outcome.Rpm ?? 0)} rpm",
                "charging" => $"{N(outcome.Watts ?? 0)} W",
                "ratio" => N(outcome.Is ?? 0),
                _ => Metres(outcome.About ?? 0),
            };
            case "ratio":
                return Lesson.Steps.SelectMany(s => s.Checks).FirstOrDefault(c => c.Type == "ratio") is { } rc && LastFacts is { } rf && RatioOf(rf, rc) is { } ratio
                    ? N(Math.Round(ratio, ratio < 10 ? 1 : 0)) : "?";
            case "watts":
                return Lesson.Steps.SelectMany(s => s.Checks).FirstOrDefault(c => c.Type == "charging") is { Part: { } gr } && Roles.TryGetValue(gr, out var gid)
                       && LastFacts?.GeneratorOf(gid) is { } g ? $"{N(Math.Round(g.EndDelivered))} W" : "?";
            case "rise": return LastFacts?.RiseOf(partId ?? "") is { } rise ? Metres(rise.Metres) : "?";
            case "accel": return $"{Acceleration(doc).ToString("0.00", CultureInfo.InvariantCulture)} m/s²";
            case "drop" when Roles.TryGetValue("ball", out var ball) && doc.Parts.TryGetValue(ball, out var bp): return Metres(bp.At.Y - bp.Number("radius", 0.05));
            case "speed" when Roles.TryGetValue("ball", out var ball2) && doc.Parts.ContainsKey(ball2): return $"{N(Math.Round(RollingSpeed(doc, ball2), 2))} m/s";
            case "slide" when Roles.TryGetValue("ball", out var ball3) && doc.Parts.TryGetValue(ball3, out var sp):
                return $"{N(Math.Round(Math.Sqrt(2 * Physics.Gravity * (sp.At.Y - sp.Number("radius", 0.05))), 2))} m/s";
            case "top": return LastFacts?.RiseOf(partId ?? "") is { } fast ? $"{N(Math.Round(fast.TopSpeed, 2))} m/s" : "?";
            case "across": return LastFacts?.RiseOf(partId ?? "") is { } far ? Metres(far.Across) : "?";
            case "moved": return LastFacts?.TankOf(partId ?? "") is { } tank ? $"{N(Math.Round(tank.Litres, 1))} litres" : "?";
            case "reached" when partId is not null && doc.Parts.TryGetValue(partId, out var vessel):
                return LastFacts?.TankOf(partId) is { } held ? $"{N(Math.Round(held.EndLitres / 1000 / vessel.Number("area", 0.05) * 100, 1))} cm" : "?";
            case "empties": return TimeToEmpty(doc) is > 0 and var secs ? $"{N(Math.Round(secs))} seconds" : "?";
        }
        return null;
    }

    private static double Distance(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
    private static string N(double v) => Math.Round(v, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);
}
