using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>Issue #67: the save file, its atomic write, and a sleep carried across a save. The state walk itself is in RuntimeStateTests.</summary>
public class WorldSaveTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private const double Dt = 1.0 / 120;

    private static MachineDef WakeClock() =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "wake-clock.machine")));

    private static string TempPath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-saves-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "world.save");
    }

    [Fact]
    public void ASaveWritesAndReadsBackWithItsMachinesAndState()
    {
        var rt = new MachineRuntime(WakeClock(), Materials);
        for (int i = 0; i < 1200; i++) rt.Step(Dt);                       // 10 s
        var save = WorldSave.OfMachine("wake-clock", rt);
        string path = TempPath();
        save.WriteAtomic(path);
        var read = WorldSave.Read(path);
        Assert.Equal(("machine", "wake-clock"), (read.Kind, read.Name));
        var m = Assert.Single(read.Machines);
        Assert.Equal(("wake-clock", "wake-clock"), (m.Label, m.Machine));
        Assert.Equal(10.0, m.Clock, precision: 9);
        Assert.Equal(save.Machines[0].State.Items.Count, m.State.Items.Count);
        Assert.Null(read.Sleep);
        Assert.Contains("(world-save 1", File.ReadAllText(path));
    }

    [Fact]
    public void AnAtomicWriteReplacesTheOldSaveWholeAndLeavesNoTemporaryFile()
    {
        string path = TempPath();
        var rt = new MachineRuntime(WakeClock(), Materials);
        WorldSave.OfMachine("wake-clock", rt).WriteAtomic(path);
        string first = File.ReadAllText(path);
        for (int i = 0; i < 600; i++) rt.Step(Dt);
        WorldSave.OfMachine("wake-clock", rt).WriteAtomic(path);
        string second = File.ReadAllText(path);
        Assert.NotEqual(first, second);
        Assert.Equal(5.0, WorldSave.Parse(second).Machines[0].Clock, precision: 9);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void AFailedWriteLeavesTheLastGoodSaveUntouched()
    {
        string path = TempPath();
        var rt = new MachineRuntime(WakeClock(), Materials);
        WorldSave.OfMachine("wake-clock", rt).WriteAtomic(path);
        string good = File.ReadAllText(path);
        // a save that cannot be written (its temporary file's name is taken by a directory) throws, and the old file is as it was
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(Path.Combine(dir, $".world.save.{Environment.ProcessId}.tmp"));
        Assert.ThrowsAny<Exception>(() => WorldSave.OfMachine("wake-clock", rt).WriteAtomic(path));
        Assert.Equal(good, File.ReadAllText(path));
    }

    [Fact]
    public void ABadSaveIsRefusedWithAReason()
    {
        Assert.Contains("not a saved world", Assert.Throws<FormatException>(() => WorldSave.Parse("(machine x)")).Message);
        Assert.Contains("version 9", Assert.Throws<FormatException>(() => WorldSave.Parse("(world-save 9 (kind machine) (name x))")).Message);
        Assert.Contains("no (name", Assert.Throws<FormatException>(() => WorldSave.Parse("(world-save 1 (kind machine))")).Message);
    }

    /// <summary>Sleep 10 s of a 25 s wait, save, load into a fresh runtime, and the sleep goes on to wake when it would have, with the estimate it began with.</summary>
    [Fact]
    public void ASleepSavedHalfWayResumesWithTheSameEstimateAndWakesAtTheSameTime()
    {
        var def = WakeClock();
        var plan = def.Wakes.Single(w => w.Id == "filled");

        // the sleep that is never interrupted
        var straight = new MachineRuntime(def, Materials);
        var uninterrupted = SleepSession.FastForward(straight, plan);

        // the one that is saved 10 s in
        var first = new MachineRuntime(def, Materials);
        double predicted = SleepPlanner.Predict(first, plan).Seconds!.Value;
        var session = new SleepSession(first, plan, Dt, predicted);
        while (session.Elapsed < 10) session.Run(60);
        Assert.False(session.Done);
        string path = TempPath();
        WorldSave.OfMachine("wake-clock", first, new SavedSleep("wake-clock", plan, session.StartedAt, session.Predicted)).WriteAtomic(path);

        // a new run of the game: rebuild the machine, lay the state on it, resume the sleep
        var save = WorldSave.Read(path);
        var resumed = new MachineRuntime(def, Materials);
        Assert.Empty(RuntimeState.Restore(resumed, save.Machines[0].State));
        var s = save.Sleep!;
        Assert.Equal(predicted, s.Predicted!.Value, precision: 12);        // the estimate it set out with
        var again = new SleepSession(resumed, s.Plan, Dt, s.Predicted, s.StartedAt);
        Assert.Equal(session.Elapsed, again.Elapsed, precision: 6);        // it knows how long it has slept
        while (!again.Done) again.Run(600);
        Assert.Equal(WakeReason.Condition, again.Result!.Reason);
        Assert.Equal(uninterrupted.Elapsed, again.Result.Elapsed, precision: 6);   // wakes when it would have
        Assert.Equal(straight.Time, resumed.Time, precision: 6);
        Assert.Equal(straight.GetField("cistern", "water"), resumed.GetField("cistern", "water"), precision: 9);
    }

    /// <summary>
    /// A crew digs for 60 s, the ground is saved, and a fresh copy of the map takes the save: every height and soil
    /// comes back to the bit, and the volume is what the digging left (dug = heaped), not what the map file says.
    /// </summary>
    [Fact]
    public void ADugGroundSurvivesASave()
    {
        string Dir(string d, string f) => Path.Combine(AppContext.BaseDirectory, d, f);
        Terrain Load(WorldDef w) => Terrain.Parse(File.ReadAllText(Dir("maps", w.Map + ".map")), w.Map!);
        var world = WorldDef.Parse(File.ReadAllText(Dir("worlds", "trench.world")), "trench.world");
        var map = Load(world);
        var ground = new WorldGround(map);
        var place = Assert.Single(world.Placements);
        var crew = new MachineRuntime(WorldDef.Placed(MachineDef.Parse(File.ReadAllText(Dir("machines", place.Machine + ".machine"))), place, map), Materials);
        ground.Attach(place.Label, crew);
        for (double t = 0; t < 60; t += 0.1) { crew.Step(0.1); ground.Step(0.1); }
        Assert.NotEqual(Load(world).Heights, map.Heights);                 // there is something to save

        string path = TempPath();
        new WorldSave { Kind = "world", Name = "trench", Machines = [], Ground = RuntimeState.CaptureGround(ground) }.WriteAtomic(path);

        var fresh = Load(world);
        var freshGround = new WorldGround(fresh);
        Assert.Empty(RuntimeState.RestoreGround(freshGround, WorldSave.Read(path).Ground!));
        Assert.Equal(map.Heights, fresh.Heights);
        Assert.Equal(map.Soil, fresh.Soil);
    }
}
