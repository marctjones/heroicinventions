using System.Globalization;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

// One command per stdin line, one reply per stdout line, both plain
// S-expressions — the same vocabulary LiveLinkServer.cs speaks to a
// running game, but here there is no game: this process only ever
// touches HeroicInventions.Sim, so it can run in `raco test` or CI
// without Godot installed.
//
// Command:  (simulate "<path-to>.machine" <seconds> <step> <sample-dt>)
// Reply:    (run (<time> (<target.field> <value>) ...) (<time> ...) ...)
//        or (error "<message>")
var materials = MaterialLibrary.LoadDefault();

string? line;
while ((line = Console.In.ReadLine()) is not null)
{
    if (line.Trim().Length == 0) continue;
    string reply;
    try
    {
        reply = Handle(line, materials);
    }
    catch (Exception e)
    {
        reply = $"(error {Quote(e.Message)})";
    }
    Console.Out.Write(reply);
    Console.Out.Write('\n');
    Console.Out.Flush();
}

static string Handle(string line, MaterialLibrary materials)
{
    var forms = SExprReader.ReadAll(line);
    if (forms is not [SList form]) throw new FormatException("expected exactly one command form per line");
    if (form.Head != "simulate")
        throw new FormatException($"unknown command '{form.Head}'; only (simulate ...) is supported");

    var items = form.Items;
    if (items.Count != 5 || items[1] is not SString path || items[2] is not SNumber seconds
        || items[3] is not SNumber step || items[4] is not SNumber sampleDt)
        throw new FormatException("usage: (simulate \"<path>.machine\" <seconds> <step> <sample-dt>)");
    if (step.Value <= 0) throw new FormatException("step must be positive");
    if (sampleDt.Value <= 0) throw new FormatException("sample-dt must be positive");

    var def = MachineDef.Parse(File.ReadAllText(path.Value));
    var run = new MachineRuntime(def, materials);

    int totalSteps = (int)Math.Round(seconds.Value / step.Value);
    var frames = new List<string>(capacity: (int)(seconds.Value / sampleDt.Value) + 2);
    double nextSampleAt = 0;
    for (int i = 0; i <= totalSteps; i++)
    {
        if (run.Time + 1e-9 >= nextSampleAt)
        {
            frames.Add(FrameOf(run));
            nextSampleAt += sampleDt.Value;
        }
        if (i < totalSteps) run.Step(step.Value);
    }
    return $"(run {string.Join(' ', frames)})";
}

static string FrameOf(MachineRuntime run)
{
    var entries = run.FieldGetters
        .Select(kv => (kv.Key, Value: SafeRead(kv.Value)))
        .Where(e => e.Value is not null)
        .Select(e => $"({e.Key} {Num(e.Value!.Value)})");
    return $"({Num(run.Time)} {string.Join(' ', entries)})";
}

// A getter can throw or return NaN/Infinity in a transient state (e.g. a
// pipe with no flow yet); skip it for this frame rather than emit a token
// Racket's reader can't parse back.
static double? SafeRead(Func<double> get)
{
    try
    {
        double v = get();
        return double.IsFinite(v) ? v : null;
    }
    catch
    {
        return null;
    }
}

// "R"/"G17" round-trip exactly but may use an uppercase E exponent marker,
// which Racket's reader rejects — lowercase it.
static string Num(double v) => v.ToString("G17", CultureInfo.InvariantCulture).Replace("E", "e");
static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
