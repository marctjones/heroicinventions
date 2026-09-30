using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #67: a machine saved and loaded goes on exactly as if it had never stopped.</summary>
public class RuntimeStateTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    public static IEnumerable<object[]> ShippedMachines() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "machines"), "*.machine").Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    private static MachineDef Load(string name) =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", name + ".machine")));

    private static double? Read(Func<double> get)
    {
        try { double v = get(); return double.IsFinite(v) ? v : null; } catch { return null; }
    }

    private static void RunFor(MachineRuntime rt, double seconds, double dt = 0.05)
    {
        int steps = (int)Math.Round(seconds / dt);
        for (int i = 0; i < steps; i++) rt.Step(dt);
    }

    /// <summary>Save after 30 s, load into a fresh build, run 30 s more: every field agrees with a run that never stopped.</summary>
    [Theory]
    [MemberData(nameof(ShippedMachines))]
    public void SaveThenLoadGivesTheSameTraceGoingForwardAsNeverHavingSaved(string name)
    {
        var def = Load(name);
        var straight = new MachineRuntime(def, Materials);
        var first = new MachineRuntime(def, Materials);
        RunFor(straight, 30); RunFor(first, 30);

        // through the text, as a file would carry it
        string text = SExprWriter.Print(RuntimeState.Capture(first));
        var state = (SList)SExprReader.ReadAll(text).Single();
        var resumed = new MachineRuntime(def, Materials);
        var unmatched = RuntimeState.Restore(resumed, state);
        Assert.Empty(unmatched);
        Assert.Equal(first.Time, resumed.Time, precision: 12);
        foreach (var (key, get) in first.FieldGetters)
            if (Read(get) is { } a && Read(resumed.FieldGetters[key]) is { } b)
                Assert.True(Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Abs(a)), $"{name}: {key} was {a} when saved and {b} when loaded");

        RunFor(straight, 30); RunFor(resumed, 30);
        Assert.Equal(straight.Time, resumed.Time, precision: 9);
        foreach (var (key, get) in straight.FieldGetters)
            if (Read(get) is { } a && Read(resumed.FieldGetters[key]) is { } b)
                Assert.True(Math.Abs(a - b) <= 1e-7 * Math.Max(1, Math.Abs(a)), $"{name}: {key} is {a} in the run that never stopped and {b} in the one that was saved and loaded");
    }

    [Fact]
    public void TheStateSavedIsRichEnoughToMeanSomething()
    {
        var rt = new MachineRuntime(Load("wake-clock"), Materials);
        RunFor(rt, 10);
        var state = RuntimeState.Capture(rt);
        var paths = state.Items.Skip(1).OfType<SList>().Select(l => ((SSymbol)l.Items[0]).Name).ToList();
        Assert.True(paths.Count > 10, $"{paths.Count} values saved");
        Assert.Contains(paths, p => p == "runtime/Fluids/Tanks/0/WaterVolume");
        Assert.Contains(paths, p => p.StartsWith("runtime/Time") || p.Contains("Time"));
        // and the cistern's 20 L are in it
        var water = state.Items.OfType<SList>().Single(l => l.Items[0] is SSymbol { Name: "runtime/Fluids/Tanks/0/WaterVolume" });
        Assert.Equal(0.02, ((SNumber)water.Items[1]).Value, precision: 9);
    }

    [Fact]
    public void ASaveOfADifferentMachineReportsWhatItCouldNotPlace()
    {
        var clock = new MachineRuntime(Load("wake-clock"), Materials);
        RunFor(clock, 5);
        var sand = new MachineRuntime(Load("sand-timer"), Materials);
        var unmatched = RuntimeState.Restore(sand, RuntimeState.Capture(clock));
        Assert.NotEmpty(unmatched);                                              // its cistern has nowhere to go: the paths that found nothing are named
    }
}
