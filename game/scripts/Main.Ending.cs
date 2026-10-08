using Godot;
using HeroicInventions.Sim.Electrics;

namespace HeroicInventions;

/// <summary>
/// The ending (issue #95): when a battery bank makes the call (<c>scene.won</c> becomes 1, <see cref="BatteryBank.Won"/>), a page says so.
/// The design doc left open what the ending shows ("the first people arriving to what the rover built?"); this settles it as: the call
/// itself (the sol and the hour it went out, the bank's charge and temperature), where the charge came from, the bank's own per-source
/// record (<c>&lt;bank&gt;.from-&lt;source&gt;</c>, the sim's Sources), as a bar each, and what stands in the crater, with a line that people are
/// meant to come and the call is how the rover tells them what it has built. Then "Keep playing" (the world goes on from where it was)
/// or "Main menu". The sim is paused while the page is up. It is raised once per bank, from <see cref="EndingTick"/>, whatever raised the win.
/// </summary>
public partial class Main
{
    private readonly HashSet<BatteryBank> _ended = [];
    private bool _endingWasRunning;

    /// <summary>Once a frame while the player is the rover: raises the ending the first time a bank has made the call.</summary>
    private void EndingTick()
    {
        if (_screen is Screen.Ending) return;
        foreach (var (view, bank) in AllBanks().ToList())
            if (bank.Won && _ended.Add(bank))
            {
                if (_screen == Screen.Opening) EndOpening(skipped: true);
                if (_screen == Screen.Log) CloseRoverLog();
                RaiseEnding(view, bank);
                return;
            }
    }

    private static string Elapsed(double seconds) =>
        seconds < 120 ? $"{seconds:0} s" : seconds < 7200 ? $"{seconds / 60:0} min" : $"{seconds / 3600:0.0} h";

    private static string SourceName(string key)
    {
        string spaced = key.Replace('-', ' ');
        return spaced.Length == 0 ? "unknown" : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private void RaiseEnding(MachineView view, BatteryBank bank)
    {
        _endingWasRunning = _running;
        SetRunning(false);
        if (RoverIsPlayer) _rover!.Command = (0, 0);

        var col = Page(Screen.Ending, "THE CALL WENT OUT", $"{_scenario?.Title ?? "The Lonely Rover"} · sol {bank.WonAtSol}, {MachineView.HoursText(bank.WonAtHour)}", 700, 38);
        col.AddChild(HudTheme.Body(
            $"On sol {bank.WonAtSol} at {MachineView.HoursText(bank.WonAtHour)}, on the relay pass, the rover called Earth with {bank.Name} full ({bank.ChargeWh:0.0} of {bank.CapacityWh:0.0} Wh) " +
            $"and warm ({bank.Temperature:0.#} °C). That was {Elapsed(view.Runtime.Time)} of the rover's time since the world began.", 18));

        col.AddChild(HudTheme.Heading("Where the charge came from", 20));
        double total = bank.Sources.Values.Sum();
        if (total <= 0) col.AddChild(HudTheme.Body("No generator charged this bank: its charge was set by hand.", 16, dim: true));
        foreach (var (source, joules) in bank.Sources.OrderByDescending(kv => kv.Value))
        {
            double wh = joules / BatteryBank.JoulesPerWattHour, share = joules / total;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var name = HudTheme.Body($"{SourceName(source)}", 16);
            name.AutowrapMode = TextServer.AutowrapMode.Off;
            name.CustomMinimumSize = new Vector2(190, 0);
            row.AddChild(name);
            row.AddChild(new ProgressBar { MinValue = 0, MaxValue = 1, Value = share, ShowPercentage = false, CustomMinimumSize = new Vector2(280, 22), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            var figures = HudTheme.Body($"{wh:0.0} Wh · {share * 100:0} %", 16);
            figures.AutowrapMode = TextServer.AutowrapMode.Off;
            row.AddChild(figures);
            col.AddChild(row);
        }

        var machines = _views.Select(v => _viewMachine.GetValueOrDefault(v, v.Name)).Distinct().Select(m => DisplayNames.GetValueOrDefault(m, m)).ToList();
        col.AddChild(HudTheme.Heading("Standing in the crater", 20));
        col.AddChild(HudTheme.Body($"{_views.Count} {(_views.Count == 1 ? "machine" : "machines")}: {string.Join(", ", machines)}.", 16, dim: true));
        col.AddChild(HudTheme.Body("People are meant to come. The call is how the rover tells them what it has built.", 16, dim: true));
        col.AddChild(new HSeparator());

        var keep = PageButton("Keep playing", KeepPlaying);
        col.AddChild(keep);
        col.AddChild(PageButton("Main menu", () => { CloseFront(); LeaveToTitle(); }));
        keep.GrabFocus();

        GD.Print($"[frontend] ending: {bank.Name} sol {bank.WonAtSol} at {MachineView.HoursText(bank.WonAtHour)}, {bank.ChargeWh:0.00} of {bank.CapacityWh:0.00} Wh at {bank.Temperature:0.0} C, sources {bank.Sources.Count}");
        foreach (var (source, joules) in bank.Sources.OrderByDescending(kv => kv.Value))
            GD.Print($"[frontend] ending source {source}: {joules / BatteryBank.JoulesPerWattHour:F3} Wh");
    }

    private void KeepPlaying()
    {
        if (_screen != Screen.Ending) return;
        CloseFront();
        if (_endingWasRunning) SetRunning(true);
        GD.Print("[frontend] keep playing");
    }
}
