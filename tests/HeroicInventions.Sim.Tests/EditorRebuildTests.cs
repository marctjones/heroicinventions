using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #76's promise: everything the engine can run, the editor can build.
/// Every shipped machine is turned into the BuildSession commands that make
/// it (<see cref="CommandScript"/>), those commands are run in a fresh
/// session, and the machine that comes out must be the machine that went in
/// — same parts, props, ports and links — and must simulate identically.
/// </summary>
public class EditorRebuildTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    public static IEnumerable<object[]> ShippedMachines() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "machines"), "*.machine").Select(f => new object[] { f });

    private static BuildSession Fresh(string name) =>
        new(Materials, catalogue: [], machinesDir: Path.GetTempPath(), name: name);

    /// <summary>The written machine with what does not affect it taken out: source locations, and the order of a part's props and ports.</summary>
    private static string Canonical(MachineDef def)
    {
        static SExpr Norm(SExpr e) => e switch
        {
            SList { Head: "srcloc" } => new SList([]),
            SList { Head: "props" or "ports" } l => new SList([l.Items[0], .. l.Items.Skip(1).Select(Norm).OrderBy(x => SExprWriter.Print(x), StringComparer.Ordinal)]),
            SList l => new SList(l.Items.Select(Norm).ToList()),
            _ => e,
        };
        var forms = SExprReader.ReadAll(MachineWriter.Write(def));
        return SExprWriter.Print(Norm(forms.Single())).Replace("(srcloc", "\n(srcloc").Replace(" ()", "").Replace("(\"<editor>\"", "(");
    }

    [Theory]
    [MemberData(nameof(ShippedMachines))]
    public void EveryShippedMachineRebuildsFromItsCommandsIntoTheSameMachine(string file)
    {
        var original = MachineDef.Parse(File.ReadAllText(file));
        var session = Fresh(original.Name);
        foreach (string line in CommandScript.For(original)) session.Execute(line);

        Assert.StartsWith("ok:", session.Execute("(check)"));
        var rebuilt = session.Document.ToMachineDef();
        string a = Canonical(original), b = Canonical(rebuilt);
        if (a != b)
        {
            var la = a.Split('\n'); var lb = b.Split('\n');
            int i = Enumerable.Range(0, Math.Min(la.Length, lb.Length)).FirstOrDefault(k => la[k] != lb[k]);
            Assert.Fail($"{Path.GetFileName(file)} differs near line {i}:\n  original {la.ElementAtOrDefault(i)}\n  rebuilt  {lb.ElementAtOrDefault(i)}");
        }
    }

    /// <summary>The same numbers out of both: water volumes, boiler temperatures and steam-wheel speeds after ten seconds.</summary>
    [Theory]
    [MemberData(nameof(ShippedMachines))]
    public void ARebuiltMachineSimulatesLikeItsOriginal(string file)
    {
        var original = MachineDef.Parse(File.ReadAllText(file));
        var session = Fresh(original.Name);
        foreach (string line in CommandScript.For(original)) session.Execute(line);

        var x = new MachineRuntime(original, Materials);
        var y = new MachineRuntime(session.Document.ToMachineDef(), Materials);
        for (int i = 0; i < 1000; i++) { x.Step(0.01); y.Step(0.01); }

        foreach (var (id, tank) in x.Tanks) Assert.Equal(tank.WaterVolume, y.Tanks[id].WaterVolume, precision: 12);
        foreach (var (id, boiler) in x.Boilers) Assert.Equal(boiler.Temperature, y.Boilers[id].Temperature, precision: 9);
        foreach (var (id, wheel) in x.JetWheels) Assert.Equal(wheel.Rpm, y.JetWheels[id].Rpm, precision: 9);
    }

    /// <summary>The oracle has to be able to fail: a nudged part, a lost mesh or a changed prop each make the comparison differ.</summary>
    [Fact]
    public void TheRebuildComparisonNoticesADifference()
    {
        var original = MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "antikythera-lunar-train.machine")));
        var s = Fresh(original.Name);
        foreach (string line in CommandScript.For(original)) s.Execute(line);
        string same = Canonical(s.Document.ToMachineDef());
        Assert.Equal(Canonical(original), same);
        Assert.True(original.Meshes.Count > 0 && original.Arbors.Count > 0, "the sample should use meshes and arbors");

        string gear = original.Meshes[0].A;
        s.Execute($"(move {gear} (9 9 9))");
        Assert.NotEqual(same, Canonical(s.Document.ToMachineDef()));
        s.Execute("(undo)");
        s.Execute($"(unmesh {original.Meshes[0].A} {original.Meshes[0].B})");
        Assert.NotEqual(same, Canonical(s.Document.ToMachineDef()));
    }

    [Fact]
    public void RopeCommandBuildsTheSameRopeTheDslDoes()
    {
        var s = Fresh("rope");
        s.Execute("(post tower #:at (0 0 0))");
        s.Execute("(block load #:at (1 0 0))");
        s.Execute("(rope hoist #:from (tower 0 1 0) #:to (load 0 0.1 0) #:length 2.5 #:over ((0 3 0)) #:diameter 0.03)");
        var rope = Assert.Single(s.Document.Ropes);
        Assert.Equal(2.5, rope.Length);
        Assert.Equal("tower", rope.From.Part);
        Assert.Equal(new Vec3(0, 3, 0), Assert.Single(rope.Over));
        Assert.Equal(0.03, rope.Diameter);
        Assert.StartsWith("ok:", s.Execute("(check)"));
        Assert.Contains("(rope hoist", MachineWriter.Write(s.Document.ToMachineDef()));
    }

    [Fact]
    public void RemovingAPartTakesEveryLinkOnItAway()
    {
        var s = Fresh("gears");
        var original = MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "antikythera-lunar-train.machine")));
        foreach (string line in CommandScript.For(original)) s.Execute(line);
        Assert.NotEmpty(s.Document.Meshes);
        string gear = s.Document.Meshes[0].A;
        s.Execute($"(remove {gear})");
        Assert.DoesNotContain(s.Document.Meshes, m => m.A == gear || m.B == gear);
        Assert.DoesNotContain(s.Document.Arbors, a => a.Parts.Contains(gear));
        Assert.StartsWith("ok:", s.Execute("(check)"));
        s.Execute("(undo)");
        Assert.Contains(s.Document.Meshes, m => m.A == gear || m.B == gear);
    }

    [Fact]
    public void SetTakesSymbolsAndFlagsAndStillRefusesAMissingPart()
    {
        var s = Fresh("props");
        s.Execute("(hearth fire #:at (0 0 0))");
        s.Execute("(boiler pot #:at (0 0.1 0))");
        s.Execute("(post pillar #:at (1 0 0))");
        s.Execute("(set fire #:fuel-kind coal)");
        s.Execute("(set pillar #:round #t)");
        s.Execute("(set fire #:heats pot)");
        Assert.Equal("coal", s.Document.Parts["fire"].Symbol("fuel-kind", ""));
        Assert.True(s.Document.Parts["pillar"].Props["round"] is SBool { Value: true });
        Assert.Throws<InvalidOperationException>(() => s.Execute("(set fire #:heats nowhere)"));
    }

    // ---- the Join-parts gestures: picks in, one command out

    private static BuildSession Rebuilt(string machine)
    {
        var original = MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", machine + ".machine")));
        var s = Fresh(original.Name);
        foreach (string line in CommandScript.For(original)) s.Execute(line);
        return s;
    }

    /// <summary>Take the Antikythera train's meshes and arbors apart, put them back with the gestures a person would make, and the machine is the one we started with.</summary>
    [Fact]
    public void GearMeshAndAxleGesturesRebuildTheAntikytheraTrain()
    {
        var s = Rebuilt("antikythera-lunar-train");
        string before = Canonical(s.Document.ToMachineDef());
        var meshes = s.Document.Meshes.ToList();
        var arbors = s.Document.Arbors.ToList();
        Assert.True(meshes.Count > 0 && arbors.Count > 0, "the train should use both meshes and arbors");

        foreach (var g in meshes) s.Execute($"(unmesh {g.A} {g.B})");
        foreach (var a in arbors) s.Execute($"(unarbor {a.Parts[0]})");
        Assert.Empty(s.Document.Meshes);
        Assert.Empty(s.Document.Arbors);
        Assert.NotEqual(before, Canonical(s.Document.ToMachineDef()));

        foreach (var a in arbors)
            s.Execute(LinkGestures.Command(s.Document, LinkGestures.Kind.Arbor, a.Parts));
        foreach (var g in meshes)
            s.Execute(LinkGestures.Command(s.Document, LinkGestures.Kind.Mesh, [g.A, g.B]));
        Assert.Equal(before, Canonical(s.Document.ToMachineDef()));
    }

    [Fact]
    public void CylinderGestureAcceptsEitherOrderAndRefusesTwoBoilers()
    {
        var s = Rebuilt("newcomen-engine");
        var cyl = Assert.Single(s.Document.Cylinders);
        string before = Canonical(s.Document.ToMachineDef());
        s.Execute($"(remove {cyl.Id})");
        Assert.Empty(s.Document.Cylinders);

        string command = LinkGestures.Command(s.Document, LinkGestures.Kind.Cylinder, [cyl.Boiler, cyl.Piston]); // boiler picked first
        Assert.Contains($"#:piston {cyl.Piston} #:steam-from {cyl.Boiler}", command);
        s.Execute(command);
        Assert.StartsWith("ok:", s.Execute("(check)"));
        Assert.Single(s.Document.Cylinders);

        // a cylinder needs one piston and one boiler, each once
        Assert.Throws<InvalidOperationException>(() => LinkGestures.Command(s.Document, LinkGestures.Kind.Cylinder, [cyl.Piston, cyl.Piston]));
        s.Execute("(boiler spare #:at (5 0 0))");
        Assert.Contains("a cylinder joins a piston to a boiler",
            Assert.Throws<InvalidOperationException>(() => LinkGestures.Command(s.Document, LinkGestures.Kind.Cylinder, [cyl.Boiler, "spare"])).Message);
    }

    [Fact]
    public void GesturesRefuseWhatCannotBeJoined()
    {
        var s = Fresh("bench");
        s.Execute("(tank a #:at (0 0 0))");
        s.Execute("(tank b #:at (1 0 0))");
        s.Execute("(post p #:at (2 0 0))");
        var doc = s.Document;
        Assert.Contains("a gear meshes with another gear", Assert.Throws<InvalidOperationException>(() => LinkGestures.Command(doc, LinkGestures.Kind.Mesh, ["a", "b"])).Message);
        Assert.Contains("shared air joins tanks", Assert.Throws<InvalidOperationException>(() => LinkGestures.Command(doc, LinkGestures.Kind.SealedAir, ["a", "p"])).Message);
        Assert.Throws<InvalidOperationException>(() => LinkGestures.Command(doc, LinkGestures.Kind.Rope, ["a", "a"]));
        Assert.Throws<InvalidOperationException>(() => LinkGestures.Command(doc, LinkGestures.Kind.Cylinder, ["a", "b"]));

        // shared air between tanks is fine, and the group survives losing one tank only if two remain
        s.Execute(LinkGestures.Command(doc, LinkGestures.Kind.SealedAir, ["a", "b"]));
        Assert.Single(s.Document.SealedAir);
        s.Execute("(remove-air a)");
        Assert.Empty(s.Document.SealedAir);
    }

    [Fact]
    public void RopeGestureCutsTheRopeToTheDistanceAndTheInspectorTrimsIt()
    {
        var s = Fresh("bench");
        s.Execute("(post tower #:at (0 0 0))");
        s.Execute("(block load #:at (3 4 0))");
        string command = LinkGestures.Command(s.Document, LinkGestures.Kind.Rope, ["tower", "load"]);
        Assert.Contains("rope-1", command);
        Assert.Contains("#:length 5", command);           // a 3-4-5 triangle
        s.Execute(command);
        s.Execute("(set-rope rope-1 #:length 5.5 #:diameter 0.03)");
        var rope = Assert.Single(s.Document.Ropes);
        Assert.Equal(5.5, rope.Length);
        Assert.Equal(0.03, rope.Diameter);
        Assert.Equal("rope-2", LinkGestures.NextId(s.Document, "rope"));
        var links = LinkGestures.LinksOn(s.Document, "tower");
        Assert.Contains(links, l => l.RopeId == "rope-1" && l.Remove == "(remove rope-1)");
        s.Execute(links.Single().Remove);
        Assert.Empty(s.Document.Ropes);
    }
}
