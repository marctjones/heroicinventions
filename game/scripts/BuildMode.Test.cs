using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// "Test it" (#165): runs the design where it stands for a few seconds and
/// comes back to building, saying in a sentence what happened ("the beam
/// tipped 18° down on the left, the granite block's side"). The frozen
/// preview is hidden and a running copy of the same machine takes its
/// place, stepped here (Main steps only a machine being watched); at the
/// end the copy goes and the preview comes back, the design untouched.
///
/// How long it runs (#182): at least <see cref="TestSeconds"/>, the 4 s the
/// first lesson's sentences were made for; then until everything has been
/// quiet for a second (no body moving or spinning, no tank's level changing,
/// no rope breaking: <see cref="TestRecorder"/>); but never more than
/// <see cref="TestLimit"/>, 30 s, since a pendulum, a spinning wheel or a
/// stream from an inflow never stops. A tank that drains through a hole in
/// T = (A/(Cd a)) sqrt(2h/g) is quiet about a second after T. What the run
/// saw is in <see cref="TestResult.Facts"/>, as numbers; the sentence is made
/// from the same facts.
/// </summary>
public partial class BuildMode
{
    /// <summary>The shortest a test runs, seconds.</summary>
    public const double TestSeconds = TestRecorder.MinSeconds;

    /// <summary>The longest a test runs, seconds: HEROIC_TEST_SECONDS in a script, or 30.</summary>
    public double TestLimit { get; set; } = double.TryParse(OS.GetEnvironment("HEROIC_TEST_SECONDS"), System.Globalization.CultureInfo.InvariantCulture, out var limit) && limit >= TestSeconds
        ? limit : TestRecorder.DefaultMaxSeconds;

    /// <summary>
    /// What one test run saw: the sentence, each beam's tilt (degrees, + = its +x end up) over the run, each loose
    /// part's mass, how long it ran, and <see cref="Facts"/>: what rose, what turned, where the water went, which ropes
    /// broke, as numbers a lesson can check (#182).
    /// </summary>
    public sealed record TestResult(string Sentence, IReadOnlyDictionary<string, List<double>> Tilts,
                                    IReadOnlyDictionary<string, double> Masses, double Seconds)
    {
        public TestFacts Facts { get; init; } = TestFacts.Empty;
    }

    /// <summary>Raised when a test run ends.</summary>
    public event Action<TestResult>? Tested;

    public TestResult? LastTest { get; private set; }
    public bool Testing => _testView is not null;

    private MachineView? _testView;
    private double _testTime;
    private readonly Dictionary<string, (Vector3 Position, Basis Basis)> _testStart = [];
    private readonly Dictionary<string, List<double>> _testTilts = [];
    private TestRecorder? _recorder;
    private Dictionary<string, string> _testKinds = [];
    private double _testStatusAt;

    private void StartTest()
    {
        if (Live || Testing) return;
        if (_session.Document.Parts.Count == 0) { _status.Text = "Nothing to test yet: put a part in the scene first"; return; }
        CancelPlacing();
        CancelLink();
        CancelConnect();
        MachineView view;
        try { view = new MachineView(new MachineRuntime(_session.Document.ToMachineDef(), _materials), _materials) { Name = "test" }; }
        catch (Exception e) { _status.Text = $"It can't run yet: {Plain(e.Message)}"; return; }
        if (_preview is not null) _preview.Visible = false;
        foreach (var nodes in _fallbackVisuals.Values) foreach (var n in nodes) ((Node3D)n).Visible = false;
        foreach (var p in _ports) p.Node.Visible = false;
        AddChild(view);
        view.SetFrozen(false);
        _testView = view;
        _testTime = 0;
        _testStatusAt = -1;
        _testStart.Clear();
        _testTilts.Clear();
        foreach (var id in _session.Document.Parts.Keys)
            if (view.BodyNamed(id) is { } body)
            {
                _testStart[id] = (body.GlobalPosition, body.GlobalBasis);
                if (_session.Document.Parts[id].Kind == "lever") _testTilts[id] = [];
            }
        _testKinds = _testStart.Keys.ToDictionary(id => id, id => _session.Document.Parts[id].Kind);
        var (heights, tanks) = view.TestStart(_testStart.Keys);
        _recorder = new TestRecorder(TestLimit);
        _recorder.Begin(heights, tanks);
        _testButton.Disabled = true;
        _status.Text = $"Testing: watch what happens (Esc stops early)";
        GD.Print("[BuildMode] test: started");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_testView is not { } view) return;
        view.Simulate(delta, trace: false);
        _testTime += delta;
        foreach (var (id, tilts) in _testTilts)
            if (view.BodyNamed(id) is { } b) tilts.Add(TiltOf(b));
        _recorder!.Tick(view.TestTick(delta, _testKinds));
        if (_testTime >= _testStatusAt)
        {
            _testStatusAt = _testTime + 0.25;
            _status.Text = $"Testing: {_testTime:0.0} s (Esc stops early; at most {TestLimit:0} s)";
        }
        if (_recorder.Done) FinishTest();
    }

    /// <summary>How far a beam's length leans from level, in degrees: positive when its +x end is up.</summary>
    private static double TiltOf(RigidBody3D beam) =>
        Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(beam.GlobalBasis.X.Normalized().Y, -1f, 1f)));

    private void FinishTest()
    {
        if (_testView is not { } view) return;
        var facts = _recorder!.Facts();
        var sentences = new List<string>();
        var masses = new Dictionary<string, double>();
        var doc = _session.Document;
        foreach (var (id, (start, startBasis)) in _testStart)
        {
            if (view.BodyNamed(id) is not { } body) continue;
            var part = doc.Parts[id];
            masses[id] = body.Mass;
            if (part.Kind == "lever")
            {
                double end = TiltOf(body), most = _testTilts[id].Select(Math.Abs).DefaultIfEmpty(0).Max();
                if (most < 2)
                {
                    sentences.Add(most < 0.1 ? "The beam stayed level (it never leaned even a tenth of a degree)."
                                             : $"The beam stayed level (it never leaned more than {most:0.#}°).");
                    continue;
                }
                // the end that went down, along the beam as it was built, and where that is on screen
                var along = startBasis.X.Normalized() * (end < 0 ? 1 : -1);
                var screenDown = _camera.UnprojectPosition(start + along) - _camera.UnprojectPosition(start);
                string side = screenDown.X < 0 ? "left" : "right";
                var onThatSide = _testStart.Where(kv => kv.Key != id && doc.Parts[kv.Key].Kind is "block" or "ball"
                                                        && (kv.Value.Position - start).Dot(along) > 0.02f)
                                           .Select(kv => kv.Key).ToList();
                string whose = onThatSide.Count > 0 ? $", on the {string.Join(" and ", onThatSide.Select(LooseName))}'s side" : "";
                sentences.Add($"The beam tipped {Math.Abs(end):0}° down on the {side}{whose}.");
            }
            else if (part.Kind is "block" or "ball")
            {
                var now = body.GlobalPosition;
                float across = new Vector2(now.X - start.X, now.Z - start.Z).Length(), drop = start.Y - now.Y;
                bool riding = _testStart.Keys.Any(k => doc.Parts[k].Kind == "lever") && across < 0.1f;
                if (now.Y < 0.12f && start.Y > 0.25f && across < 0.5f) sentences.Add($"The {LooseName(id)} fell to the ground.");   // mostly straight down; a ball that rolled off a ramp and on across the floor rolled (#184)
                else if (across > 0.1f) sentences.Add($"The {LooseName(id)} {(part.Kind == "ball" ? "rolled" : "slid")} {across:0.0} m.");
                else if (drop > 0.1f && !riding) sentences.Add($"The {LooseName(id)} dropped {drop:0.0} m.");
                if (!riding && facts.RiseOf(id) is { } rise && TestReport.RiseSentence(LooseName(id), rise) is { } rose) sentences.Add(rose);
            }
        }
        sentences.AddRange(TurnSentences(facts));
        sentences.AddRange(TestReport.WaterSentences(facts));
        sentences.AddRange(TestReport.RopeSentences(facts));
        string sentence = sentences.Count > 0 ? string.Join(" ", sentences) : "Nothing moved: everything stayed where it was built.";
        var result = new TestResult(sentence, _testTilts.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()), masses, _testTime) { Facts = facts };
        _recorder = null;

        view.QueueFree();
        _testView = null;
        _testButton.Disabled = false;
        _keepStatus = true;
        Redraw();
        _status.Text = $"Test: {sentence}" + (facts.EndedBy == "limit" ? $" (Stopped at {_testTime:0} s with things still moving.)" : "");
        Log($"test: {sentence}");
        GD.Print($"[BuildMode] test: {sentence} | " + string.Join(" ", result.Tilts.Select(kv =>
            $"{kv.Key} tilt end {F(kv.Value.LastOrDefault())} most {F(kv.Value.Select(Math.Abs).DefaultIfEmpty(0).Max())}")));
        GD.Print($"[BuildMode] facts: {TestReport.DataLine(facts)}");
        LastTest = result;
        Tested?.Invoke(result);
    }

    /// <summary>"The bronze pulley turned 0.88 of a turn." for each wheel that turned; wheels fixed on one axle turn together and share a sentence.</summary>
    private IEnumerable<string> TurnSentences(TestFacts facts)
    {
        var doc = _session.Document;
        var def = doc.ToMachineDef();
        var done = new HashSet<string>();
        foreach (var turn in facts.Turns)
        {
            if (!done.Add(turn.Id) || !doc.Parts.TryGetValue(turn.Id, out var part)) continue;
            var mates = def.Arbors.FirstOrDefault(a => a.Parts.Contains(turn.Id))?.Parts
                .Where(m => m != turn.Id && doc.Parts.ContainsKey(m) && facts.TurnOf(m) is { } t && Math.Abs(t.Turns - turn.Turns) < 0.01 * Math.Max(1, Math.Abs(turn.Turns)))
                .ToList() ?? [];
            foreach (var m in mates) done.Add(m);
            var names = mates.Prepend(turn.Id).Select(WheelName).ToList();
            string who = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and the " + names[^1];
            if (TestReport.TurnSentence(who, turn) is { } s) yield return s;
        }
    }

    /// <summary>A wheel as a person would name it: "bronze pulley", "oak drum", "waterwheel".</summary>
    private string WheelName(string id)
    {
        var part = _session.Document.Parts[id];
        string shape = part.Props.GetValueOrDefault("catalogue") is SSymbol c ? System.Text.RegularExpressions.Regex.Replace(c.Name, @"-\d.*$", "") : part.Kind;
        string material = _materials.TryGet(part.Material, out var m) ? m.Name.ToLowerInvariant() : part.Material;
        return part.Kind == "wheel" ? $"{material} {shape}" : shape;
    }

    /// <summary>A loose part as a person would name it: "granite block", "iron ball".</summary>
    private string LooseName(string id)
    {
        var part = _session.Document.Parts[id];
        string material = _materials.TryGet(part.Material, out var m) ? m.Name.ToLowerInvariant() : part.Material;
        return $"{material} {part.Kind}";
    }
}
