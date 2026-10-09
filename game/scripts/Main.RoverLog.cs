using Godot;
using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The rover's log (issue #95): a screen, opened with I (or the left panel's Rover log button), listing what the rover noticed, in order:
/// what it refused to do and why, each backhoe dig and dump, a fall through the ground, the battery bank's milestones (25, 50, 75 % and
/// full, going out of its charging range and back), each sol beginning, and the call. Nothing in Rover*.cs is changed and no hook is needed:
/// the log is read off what the game already keeps, once a frame (<see cref="RoverLogTick"/>, from FrontEndTick): the last refusal
/// (<c>_roverSaid</c>, the same text the panel shows and the "[rover] refused" print), the rover's own counters and arm phase
/// (<c>Dug</c>, <c>Dumped</c>, <c>Rescues</c>, <c>PhaseName</c>), each bank's charge and temperature, and the sun's sol number. The log is of
/// this session: a loaded save starts a new one, and it is not in the save file (the save format is the sim's). It is also written, line by line, to "<scene>.rover-log.txt" in the saves folder (#217).
/// The achievements the design doc names (#68) are not here; when they exist, their earning is one more <see cref="AddLog"/> line.
/// </summary>
public partial class Main
{
    private sealed record LogEntry(int Sol, double Hour, string Kind, string Text, double Time = 0);

    private readonly List<LogEntry> _roverLog = [];
    private WorldDef? _logWorld;                       // the world the log is of: a new one starts it again
    private double _logSaidAt;
    private string _logPhase = "Stowed";
    private double _logDugAt, _logDumpedAt;
    private int _logRescues, _logSol;
    private readonly Dictionary<BatteryBank, (int Milestone, bool InRange, bool Full, bool Won)> _logBanks = [];
    private RichTextLabel? _logText;

    /// <summary>The sol and local solar hour of the scene's sun, where the log stamps its lines.</summary>
    private (int Sol, double Hour) LogClock()
    {
        var view = _views.Count > 0 ? _views[0] : _current;
        return view is null ? (0, 0) : (view.Runtime.Sun.SolNumber, view.Runtime.Sun.Time);
    }

    /// <summary>
    /// The log file (#217): "&lt;scene&gt;.rover-log.txt" in the saves folder, beside the autosave (the same folder logic as <c>SavePath</c>,
    /// HEROIC_SAVES_DIR included). Null in a scripted run with no HEROIC_SAVES_DIR, which must not write into the player's folder.
    /// </summary>
    private string? LogFilePath()
    {
        if (SaveName is null) return null;
        string name = $"{SaveName}.rover-log.txt";
        if (OS.GetEnvironment("HEROIC_SAVES_DIR") is { Length: > 0 } dir) return System.IO.Path.Combine(dir, name);
        bool scripted = OS.GetEnvironment("HEROIC_QUIT_AFTER_SIM_SECONDS") is { Length: > 0 } || OS.GetEnvironment("HEROIC_INPUT") is { Length: > 0 }
                        || OS.GetEnvironment("HEROIC_EDITOR_INPUT") is { Length: > 0 };
        return scripted ? null : ProjectSettings.GlobalizePath($"{SavesDir}/{name}");
    }

    /// <summary>Appends one line to the file as it is made (so a crash or a quit loses nothing). A new log adds a session header; older sessions stay above it.</summary>
    private void WriteLogFile(string line, bool newSession = false)
    {
        if (LogFilePath() is not { } path) return;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            string head = newSession ? $"# rover log of {SaveName}, session begun {DateTime.Now:yyyy-MM-dd HH:mm:ss} (times: sol, local solar hour, scene clock in seconds)\n" : "";
            System.IO.File.AppendAllText(path, head + line + "\n");   // opened, written and closed: flushed
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"could not write the rover log {path}: {e.Message}");
        }
    }

    private void AddLog(string kind, string text)
    {
        var (sol, hour) = LogClock();
        var view = _views.Count > 0 ? _views[0] : _current;
        var entry = new LogEntry(sol, hour, kind, text, view?.Runtime.Time ?? 0);
        bool first = _roverLog.Count == 0;
        _roverLog.Add(entry);
        GD.Print($"[log] sol {sol} {MachineView.HoursText(hour)} {kind}: {text}");
        WriteLogFile($"sol {sol} {MachineView.HoursText(hour)} t={entry.Time:0.0}s {kind}: {text}", first);
        if (_logText is not null && IsInstanceValid(_logText)) AppendLogLine(_logText, _roverLog[^1]);
    }

    /// <summary>Once a frame while the player is the rover: notices what changed since the last frame and writes it down.</summary>
    private void RoverLogTick()
    {
        var r = _rover!;
        if (_world != _logWorld && _world?.Name == _logWorld?.Name && _logWorld is not null) _logWorld = _world;   // the same world edited (a build, a join): the log carries on, with no new header (#226)
        else if (_world != _logWorld)
        {
            _logWorld = _world;
            _roverLog.Clear();
            _logBanks.Clear();
            _logSaidAt = _roverSaidAt;
            _logPhase = r.PhaseName;
            _logRescues = r.Rescues;
            _logSol = LogClock().Sol;
            AddLog("sol", $"The log begins on sol {_logSol}.");
        }

        if (_roverSaid is { } said && _roverSaidAt != _logSaidAt) { _logSaidAt = _roverSaidAt; AddLog("refused", said); }

        string phase = r.PhaseName;
        if (phase != _logPhase)
        {
            if (phase == "Digging") _logDugAt = r.Dug;
            if (_logPhase == "Digging")
            {
                double dug = r.Dug - _logDugAt;
                AddLog("dig", dug > 0.005 ? $"The backhoe dug {dug:0.00} m³ ({r.Dug:0.00} m³ in all)." : "The backhoe came up empty.");
            }
            if (phase == "Dumping") _logDumpedAt = r.Dumped;
            if (_logPhase == "Dumping")
            {
                double dumped = r.Dumped - _logDumpedAt;
                AddLog("dig", dumped > 0.005 ? $"The backhoe tipped out {dumped:0.00} m³ ({r.Dumped:0.00} m³ in all)." : "The backhoe tipped out nothing.");
            }
            _logPhase = phase;
        }
        if (r.Rescues > _logRescues) { _logRescues = r.Rescues; AddLog("rescue", "The rover fell through the ground and was stood back on it."); }

        int sol = LogClock().Sol;
        if (sol != _logSol) { _logSol = sol; AddLog("sol", $"Sol {sol} begins."); }

        foreach (var (_, bank) in AllBanks())
        {
            if (!_logBanks.TryGetValue(bank, out var seen)) seen = (bank.Full ? 4 : Math.Min(3, (int)(bank.Fraction / 0.25)), bank.InRange, bank.Full, bank.Won);   // as found: only what happens from here is logged
            double f = bank.Fraction;
            while (seen.Milestone < 3 && f >= 0.25 * (seen.Milestone + 1) - 1e-9 && !bank.Full)
            {
                seen.Milestone++;
                AddLog("bank", $"{bank.Name} is {seen.Milestone * 25} % charged ({bank.ChargeWh:0.0} of {bank.CapacityWh:0.0} Wh).");
            }
            if (bank.Full && !seen.Full)
            {
                seen.Milestone = 4;
                AddLog("bank", bank.InRange
                    ? $"{bank.Name} is full ({bank.CapacityWh:0.0} Wh) and warm enough ({bank.Temperature:0.#} °C). It waits for the relay pass."
                    : $"{bank.Name} is full, but at {bank.Temperature:0.#} °C it is outside {bank.MinChargeC:0} to {bank.MaxChargeC:0} °C: the call would not go.");
            }
            seen.Full = bank.Full;
            if (bank.InRange != seen.InRange)
                AddLog("bank", bank.InRange ? $"{bank.Name} is back in its charging range ({bank.Temperature:0.#} °C)."
                    : $"{bank.Name} left its charging range at {bank.Temperature:0.#} °C and takes no charge.");
            seen.InRange = bank.InRange;
            if (bank.Won && !seen.Won)
                AddLog("win", $"The call went out from {bank.Name} on sol {bank.WonAtSol} at {MachineView.HoursText(bank.WonAtHour)}. The game is won.");
            seen.Won = bank.Won;
            _logBanks[bank] = seen;
        }
    }

    // ------------------------------------------------------------------ screen

    private static void AppendLogLine(RichTextLabel text, LogEntry e)
    {
        text.PushBold();
        text.AddText($"Sol {e.Sol} {MachineView.HoursText(e.Hour)}   ");
        text.Pop();
        text.AddText(e.Text + "\n");
    }

    private void ShowRoverLog()
    {
        if (!RoverIsPlayer || _screen != Screen.None) return;
        var col = Page(Screen.Log, "Rover log", $"{_roverLog.Count} entries, oldest first.", 780, 30);
        _logText = new RichTextLabel { CustomMinimumSize = new Vector2(740, 420), ScrollFollowing = true, FitContent = false, SelectionEnabled = false };
        _logText.AddThemeFontSizeOverride("normal_font_size", 16);
        _logText.AddThemeFontSizeOverride("bold_font_size", 16);
        col.AddChild(_logText);
        if (_roverLog.Count == 0) _logText.AddText("Nothing yet.");
        foreach (var e in _roverLog) AppendLogLine(_logText, e);
        var close = PageButton("Close (I)", CloseRoverLog, 16);
        col.AddChild(close);
        close.GrabFocus();
    }

    private void CloseRoverLog()
    {
        if (_screen != Screen.Log) return;
        _logText = null;
        CloseFront();
    }
}
