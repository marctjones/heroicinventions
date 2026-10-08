using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// "Test it" (#165): runs the design where it stands for a few seconds and
/// comes back to building, saying in a sentence what happened ("the beam
/// tipped 18° down on the left, the granite block's side"). The frozen
/// preview is hidden and a running copy of the same machine takes its
/// place, stepped here (Main steps only a machine being watched); at the
/// end the copy goes and the preview comes back, the design untouched.
/// </summary>
public partial class BuildMode
{
    public const double TestSeconds = 4;

    /// <summary>What one test run saw: the sentence, each beam's tilt (degrees, + = its +x end up) over the run, and each loose part's mass.</summary>
    public sealed record TestResult(string Sentence, IReadOnlyDictionary<string, List<double>> Tilts,
                                    IReadOnlyDictionary<string, double> Masses, double Seconds);

    /// <summary>Raised when a test run ends.</summary>
    public event Action<TestResult>? Tested;

    public TestResult? LastTest { get; private set; }
    public bool Testing => _testView is not null;

    private MachineView? _testView;
    private double _testTime;
    private readonly Dictionary<string, (Vector3 Position, Basis Basis)> _testStart = [];
    private readonly Dictionary<string, List<double>> _testTilts = [];

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
        _testStart.Clear();
        _testTilts.Clear();
        foreach (var id in _session.Document.Parts.Keys)
            if (view.BodyNamed(id) is { } body)
            {
                _testStart[id] = (body.GlobalPosition, body.GlobalBasis);
                if (_session.Document.Parts[id].Kind == "lever") _testTilts[id] = [];
            }
        _testButton.Disabled = true;
        _status.Text = $"Testing: watch what happens ({TestSeconds:0} s, Esc stops early)";
        GD.Print("[BuildMode] test: started");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_testView is not { } view) return;
        view.Simulate(delta, trace: false);
        _testTime += delta;
        foreach (var (id, tilts) in _testTilts)
            if (view.BodyNamed(id) is { } b) tilts.Add(TiltOf(b));
        if (_testTime >= TestSeconds) FinishTest();
    }

    /// <summary>How far a beam's length leans from level, in degrees: positive when its +x end is up.</summary>
    private static double TiltOf(RigidBody3D beam) =>
        Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(beam.GlobalBasis.X.Normalized().Y, -1f, 1f)));

    private void FinishTest()
    {
        if (_testView is not { } view) return;
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
                if (most < 2) { sentences.Add($"The beam stayed level (it never leaned more than {most:0.#}°)."); continue; }
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
                if (now.Y < 0.12f && start.Y > 0.25f) sentences.Add($"The {LooseName(id)} fell to the ground.");
                else if (across > 0.1f) sentences.Add($"The {LooseName(id)} {(part.Kind == "ball" ? "rolled" : "slid")} {across:0.0} m.");
                else if (drop > 0.1f && !riding) sentences.Add($"The {LooseName(id)} dropped {drop:0.0} m.");
            }
        }
        string sentence = sentences.Count > 0 ? string.Join(" ", sentences) : "Nothing moved: everything stayed where it was built.";
        var result = new TestResult(sentence, _testTilts.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()), masses, _testTime);

        view.QueueFree();
        _testView = null;
        _testButton.Disabled = false;
        _keepStatus = true;
        Redraw();
        _status.Text = $"Test: {sentence}";
        Log($"test: {sentence}");
        GD.Print($"[BuildMode] test: {sentence} | " + string.Join(" ", result.Tilts.Select(kv =>
            $"{kv.Key} tilt end {F(kv.Value.LastOrDefault())} most {F(kv.Value.Select(Math.Abs).DefaultIfEmpty(0).Max())}")));
        LastTest = result;
        Tested?.Invoke(result);
    }

    /// <summary>A loose part as a person would name it: "granite block", "iron ball".</summary>
    private string LooseName(string id)
    {
        var part = _session.Document.Parts[id];
        string material = _materials.TryGet(part.Material, out var m) ? m.Name.ToLowerInvariant() : part.Material;
        return $"{material} {part.Kind}";
    }
}
