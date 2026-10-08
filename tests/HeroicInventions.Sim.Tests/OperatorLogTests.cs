using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #153: the operator log's text form, its place in a save file, and the demo operator a blueprint carries.</summary>
public class OperatorLogTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static MachineDef Airlock() =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "airlock.machine")));

    private static readonly OperatorAction[] Cycle =
    [
        new(400.1583333331337, "bleed", "open", 1), new(600, "bleed", "open", 0), new(600, "outer", "open", 1), new(1e-5, "pump", "speed", 0.05),
    ];

    [Fact]
    public void ALogWritesAsOneAtFormPerLineAndReadsBackExactly()
    {
        string text = OperatorLog.ToText(Cycle);
        Assert.StartsWith("(at 400.1583333331337 (bleed open 1))\n(at 600 (bleed open 0))", text);
        Assert.Equal(Cycle, OperatorLog.Parse(text));            // record equality: every digit of every time and value
    }

    [Fact]
    public void ALogThatIsNotAtFormsIsAnErrorNotASkippedLine()
    {
        Assert.Throws<FormatException>(() => OperatorLog.Parse("(at 5 (bleed open))"));
        Assert.Throws<FormatException>(() => OperatorLog.Parse("(at 5 (bleed open 1)) (bleed open 1 5)"));
    }

    [Fact]
    public void CopyAsTestIsASimulateFormWithTheLogAsTimedSettings()
    {
        string form = OperatorLog.ToSimulateForm("airlock", 719.2, Cycle);
        Assert.StartsWith("(simulate 'airlock #:seconds 720 #:sample-dt 1", form);
        Assert.Contains("(bleed open 1 400.1583333331337)", form);
        Assert.Contains("(pump speed 0.05 1e-05)", form);        // Racket's reader takes a lowercase exponent
        Assert.EndsWith("))", form);
    }

    [Fact]
    public void ASaveCarriesTheLogAndWhetherThePersonTookOver()
    {
        var rt = new MachineRuntime(Airlock(), Materials);
        var save = new WorldSave
        {
            Kind = "machine", Name = "airlock", Machines = [new SavedMachine("airlock", "airlock", 0, RuntimeState.Capture(rt))],
            Operated = Cycle, OperatorTaken = true,
        };
        var read = WorldSave.Parse(save.ToText());
        Assert.Equal(Cycle, read.Operated);
        Assert.True(read.OperatorTaken);
        // a save with nothing done has no log in it at all, and an older save without one still loads
        var empty = new WorldSave { Kind = "machine", Name = "airlock", Machines = save.Machines };
        Assert.DoesNotContain("operator-log", empty.ToText());
        var older = WorldSave.Parse(empty.ToText());
        Assert.Empty(older.Operated);
        Assert.False(older.OperatorTaken);
    }

    [Fact]
    public void TheAirlockBlueprintCarriesItsDemoOperatorInTimeOrderAndItSurvivesTheEditorsWriter()
    {
        var def = Airlock();
        Assert.Equal(
            [new OperatorAction(400, "bleed", "open", 1), new OperatorAction(600, "bleed", "open", 0), new OperatorAction(600, "outer", "open", 1),
             new OperatorAction(700, "outer", "open", 0), new OperatorAction(700, "pump", "speed", 0), new OperatorAction(700, "inner", "open", 1)],
            def.Operator);
        var again = MachineDef.Parse(MachineWriter.Write(def));
        Assert.Equal(def.Operator, again.Operator);
        // every action names a real part and field: the runtime accepts each
        var rt = new MachineRuntime(def, Materials);
        foreach (var a in def.Operator) rt.SetField(a.Target, a.Field, a.Value);
    }

    [Fact]
    public void ADemoActionOnAFieldTheMachineLacksIsAnErrorWhenItIsApplied()
    {
        var rt = new MachineRuntime(Airlock(), Materials);
        Assert.ThrowsAny<Exception>(() => rt.SetField("nosuchpart", "open", 1));
    }
}
