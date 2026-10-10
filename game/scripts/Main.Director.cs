using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The director (an automated, watchable solve): script steps that drive the rover to a place by its real pose, dig with its backhoe,
/// hold the camera on a part, put a caption and the sol clock on screen, and check each step's result, failing the run at the first wrong one.
/// The steps come from <c>HEROIC_INPUT_FILE</c> (one step a line, <c>#</c> lines are comments), e.g.
/// <c>game/routes/lonely-rover-opening/solve.steps</c>, recorded by <c>tools/record-solve.sh</c>.
/// Driving sets the rover's command to the values its keys give (forward/turn of -1, 0 or 1), once a rendered frame from where the rover
/// really is, so a drive ends in the same place at any frame rate.
/// </summary>
public partial class Main
{
    private (double Forward, double Turn)? _directorCommand;   // what the director's "keys" hold this frame (RoverProcess takes it over the keyboard)
    private bool _directorCamera;                              // the camera is the director's: the rover's follow camera stands aside
    private Func<double, bool>? _directorGoal;                 // the running drive/turn/dig: true when it is done
    private bool _directorFinished;
    private double? _sleepBudgetOverride;
    private CanvasLayer? _directorLayer;
    private Label? _captionLabel, _clockLabel;
    private bool _clockOn;
    private string? _captionTemplate;
    private int _joinGesturePhase, _joinGestureWaited;

    /// <summary>The step file's steps, one a line, joined for ScriptedInput (blank and '#' lines dropped; '#:' inside a line stays).</summary>
    private static string DirectorLoad(string path)
    {
        var lines = System.IO.File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'));
        return string.Join("\n", lines);
    }

    private void DirectorFail(string why)
    {
        GD.PrintErr($"[director] FAILED: {why}");
        _heroicSetFailed = true;
        _directorCommand = null;
        _directorGoal = null;
        GetTree().Quit(1);
    }

    /// <summary>Once a rendered frame, before the rover reads its keys: the running goal steers it.</summary>
    private void DirectorTick(double delta)
    {
        if (_clockOn) DirectorClock();
        if (_captionTemplate is not null && _captionLabel is not null) _captionLabel.Text = DirectorFill(_captionTemplate);
        if (_directorGoal is null) return;
        bool done;
        try { done = _directorGoal(delta); }
        catch (Exception e) { DirectorFail(e.Message); return; }
        if (!done) return;
        _directorGoal = null;
        _directorCommand = null;
        _directorFinished = true;
    }

    private ScriptedInput.Step? DirectorStep(string[] w)
    {
        var inv = CultureInfo.InvariantCulture;
        double D(int i) => double.Parse(w[i], inv);
        if (_heroicSetFailed && w[0] is "drive-to" or "turn-to" or "dig" or "join-gesture" or "expect") return ScriptedInput.Step.Again;   // failed: hold until the quit lands
        switch (w[0])
        {
            case "drive-to" or "turn-to" or "dig":
                if (_directorGoal is not null) return ScriptedInput.Step.Again;
                if (_directorFinished) { _directorFinished = false; return ScriptedInput.Step.Next; }
                if (_rover is null) { DirectorFail($"{w[0]}: no rover in this world"); return ScriptedInput.Step.Next; }
                _directorGoal = w[0] switch
                {
                    "drive-to" => DriveGoal(D(1), D(2), w.Length > 3 ? D(3) : 0.5),
                    "turn-to" => TurnGoal(D(1)),
                    _ => DigGoal(w),
                };
                return ScriptedInput.Step.Again;
            case "join-gesture":
                return JoinGesture(w[1], w[2]);
            case "look":
                _directorCamera = true;
                return null;   // ScriptedInput's own look sets the frame
            case "frame":   // frame LABEL.PART [DIST [YAW PITCH]] (degrees)
            {
                var bits = w[1].Split('.');
                if (!_byName.TryGetValue(bits[0], out var view) || view.LinkPoint(bits[1], null) is not { } at) { DirectorFail($"frame: no part {w[1]}"); return ScriptedInput.Step.Next; }
                var orbit = _buildMode?.Orbit ?? _orbit;
                orbit.Pivot = at;
                if (w.Length > 2) orbit.Distance = (float)D(2);
                if (w.Length > 4) { orbit.Yaw = Mathf.DegToRad((float)D(3)); orbit.Pitch = Mathf.DegToRad((float)D(4)); }
                orbit.Apply();
                _directorCamera = true;
                return ScriptedInput.Step.Next;
            }
            case "follow":
                _directorCamera = false;
                if (_rover is not null) { _orbit.Yaw = RoverHeading(); _orbit.Pitch = RoverCameraPitch; _orbit.Distance = RoverCameraDistance; _orbit.Apply(); }
                return ScriptedInput.Step.Next;
            case "caption":
            {
                DirectorOverlay();
                string text = string.Join(' ', w.Skip(1));
                if (text == "off") { _captionTemplate = null; _captionLabel!.Text = ""; _captionLabel.Visible = false; return ScriptedInput.Step.Continue; }
                _captionTemplate = text;
                _captionLabel!.Visible = true;
                _captionLabel.Text = DirectorFill(text);
                GD.Print($"[director] caption: {_captionLabel.Text}");
                return ScriptedInput.Step.Continue;
            }
            case "clock":
                DirectorOverlay();
                _clockOn = w.Length < 2 || w[1] != "off";
                _clockLabel!.Visible = _clockOn;
                return ScriptedInput.Step.Continue;
            case "sleep-budget":   // ms of CPU a paused sleep may use per frame (the default is the tuning's 10 ms): a time-lapse's pace, not its result
                _sleepBudgetOverride = D(1);
                return ScriptedInput.Step.Continue;
            case "expect":
                return Expect(w);
        }
        return null;
    }

    // ---- driving ----------------------------------------------------------------------------------------------------------------------

    private const double StallSeconds = 2.5;

    private Func<double, bool> DriveGoal(double x, double z, double tol)
    {
        double stalled = 0, elapsed = 0;
        bool turning = false;
        GD.Print($"[director] drive-to ({x:0.##} {z:0.##}) from ({_rover!.Chassis.GlobalPosition.X:0.##} {_rover.Chassis.GlobalPosition.Z:0.##})");
        return delta =>
        {
            var rover = _rover!;
            elapsed += delta;
            var p = rover.Chassis.GlobalPosition;
            double dx = x - p.X, dz = z - p.Z, dist = Math.Sqrt(dx * dx + dz * dz);
            if (dist < tol)
            {
                _directorCommand = (0, 0);
                if (Math.Abs(rover.Speed) > 0.05) return false;
                GD.Print($"[director] drive-to reached ({p.X:0.00} {p.Z:0.00}) heading {Mathf.RadToDeg(RoverHeading()):0} after {elapsed:0.0} s");
                return true;
            }
            float want = Mathf.Atan2(-(float)dx, -(float)dz);
            double e = Mathf.RadToDeg(Mathf.AngleDifference(RoverHeading(), want));   // > 0: the target is to the left (Left raises the heading)
            if (Math.Abs(e) > 70) turning = true;   // far off: turn on the spot (on a slope that slides, so only when it must)
            else if (Math.Abs(e) < 30) turning = false;   // else steer while driving, an arc
            double turn = Math.Abs(e) > 3 ? (e > 0 ? -1 : 1) : 0;   // the rover's turn command: +1 is Right
            if (!turning && rover.Speed < 0.5) turn = 0;   // get going straight first: on a steep slope the climb takes the tyres' grip and a turn barely bites (above ~24 deg)
            double forward = turning ? 0 : dist < tol + 0.9 ? 0 : 1;   // coast the last 0.9 m (2 m/s stops in 0.8 m)
            if (!turning && dist < tol + 0.9 && Math.Abs(rover.Speed) < 0.05) forward = 1;   // stopped short: creep on
            _directorCommand = (forward, turn);
            if (OS.GetEnvironment("HEROIC_DIRECTOR_DEBUG") == "1" && (int)(elapsed / 1.0) != (int)((elapsed - delta) / 1.0))
                GD.Print($"[director] at ({p.X:0.00} {p.Z:0.00}) heading {Mathf.RadToDeg(RoverHeading()):0} want {Mathf.RadToDeg(want):0} e {e:0} cmd ({forward} {turn}) speed {rover.Speed:0.00}");
            bool pushing = forward > 0 && Math.Abs(rover.Speed) < 0.15;
            stalled = pushing && elapsed > 1 ? stalled + delta : 0;
            if (stalled > StallSeconds) throw new InvalidOperationException($"drive-to ({x} {z}): stalled at ({p.X:0.00} {p.Z:0.00}), {dist:0.00} m short, tilt {rover.TiltDeg:0} deg");
            if (rover.TiltDeg > 33) throw new InvalidOperationException($"drive-to ({x} {z}): tilting {rover.TiltDeg:0} deg at ({p.X:0.00} {p.Z:0.00})");
            if (elapsed > 120) throw new InvalidOperationException($"drive-to ({x} {z}): not there after 120 s, {dist:0.00} m short");
            return false;
        };
    }

    private Func<double, bool> TurnGoal(double bearingDeg)
    {
        double stalled = 0, elapsed = 0;
        float want = Mathf.DegToRad((float)bearingDeg);
        return delta =>
        {
            var rover = _rover!;
            elapsed += delta;
            double e = Mathf.RadToDeg(Mathf.AngleDifference(RoverHeading(), want));
            if (Math.Abs(e) < 2.5)
            {
                _directorCommand = (0, 0);
                if (Math.Abs(rover.YawRate) > 0.02) return false;
                GD.Print($"[director] turn-to {bearingDeg:0}: heading {Mathf.RadToDeg(RoverHeading()):0.0} after {elapsed:0.0} s");
                return true;
            }
            _directorCommand = (0, e > 0 ? -1 : 1);
            stalled = Math.Abs(rover.YawRate) < 0.05 && elapsed > 1 ? stalled + delta : 0;
            if (stalled > StallSeconds) throw new InvalidOperationException($"turn-to {bearingDeg}: the rover will not turn ({e:0} deg off)");
            return false;
        };
    }

    // ---- digging ----------------------------------------------------------------------------------------------------------------------

    /// <summary>dig N | dig until LABEL:target.field below V max N: backhoe cycles, as the B key starts them, each run to its end.</summary>
    private Func<double, bool> DigGoal(string[] w)
    {
        var inv = CultureInfo.InvariantCulture;
        int max; string? untilPath = null; double untilBelow = 0;
        if (w.Length >= 7 && w[1] == "until" && w[3] == "below" && w[5] == "max")
        { untilPath = w[2]; untilBelow = double.Parse(w[4], inv); max = int.Parse(w[6], inv); }
        else max = int.Parse(w[1], inv);
        int done = 0; int phase = 0;   // 0 start a cycle, 1 waiting for the arm to move, 2 waiting for it to stow
        double waited = 0;
        if (untilPath is not null && DirectorReadPath(untilPath) < untilBelow) { GD.Print($"[director] dig: {untilPath} is already below {untilBelow}"); return _ => true; }
        return delta =>
        {
            var rover = _rover!;
            _directorCommand = (0, 0);
            switch (phase)
            {
                case 0:
                    if (Math.Abs(rover.Speed) > 0.05) return false;
                    rover.StartCycle();
                    phase = 1; waited = 0;
                    return false;
                case 1:
                    waited += delta;
                    if (rover.ArmBusy) { phase = 2; return false; }
                    if (waited > 2) throw new InvalidOperationException($"dig: the backhoe did not start ({rover.ArmStatus})");
                    return false;
                default:
                    if (rover.ArmBusy) return false;
                    done++;
                    string status = rover.ArmStatus;
                    string cover = untilPath is null ? "" : $"; {untilPath} = {DirectorReadPath(untilPath):0.000}";
                    GD.Print($"[director] dig {done}/{max}: {status}; dug {rover.Dug:0.00} m3, dumped {rover.Dumped:0.00} m3{cover}");
                    if (status.StartsWith("Dug nothing") || status.StartsWith("Kept") || status.Contains("too hard"))
                        throw new InvalidOperationException($"dig: {status}");
                    if (untilPath is not null && DirectorReadPath(untilPath) < untilBelow) return true;
                    if (done >= max)
                    {
                        if (untilPath is not null) throw new InvalidOperationException($"dig: {untilPath} still {DirectorReadPath(untilPath):0.000} after {max} cycles");
                        return true;
                    }
                    phase = 0;
                    return false;
            }
        };
    }

    // ---- joining ----------------------------------------------------------------------------------------------------------------------

    /// <summary>join-gesture A B: the Join machines button, a click on A and one on B (in a window, each framed first); headless, the join by name.</summary>
    private ScriptedInput.Step JoinGesture(string a, string b)
    {
        if (DisplayServer.GetName() == "headless") return JoinStep(["join", a, b]);
        switch (_joinGesturePhase)
        {
            case 0:
                SetJoining(true);
                DirectorStep(["frame", a, "6", "135", "18"]);
                _joinGesturePhase = 1;
                return ScriptedInput.Step.Again;
            case 1:
            {
                var s = JoinStep(["joinclick", a, "list"]);
                if (s == ScriptedInput.Step.Again) return s;
                DirectorStep(["frame", b, "6", "135", "18"]);
                _joinGesturePhase = 2;
                return ScriptedInput.Step.Again;
            }
            case 2:
            {
                var s = JoinStep(["joinclick", b]);   // a plain click: a choice from the pick list would replace the first pick, not join to it (#225)
                if (s == ScriptedInput.Step.Again) return s;
                _joinGesturePhase = 3;
                _joinGestureWaited = 0;
                return ScriptedInput.Step.Again;
            }
            default:   // the click is handled a frame later: keep joining on until the link is there (or ~2 s), then the button off
            {
                bool Is(LinkEnd e, string s) => $"{e.Label}.{e.Part}" == s;
                bool made = _world?.Links.Any(l => (Is(l.From, a) && Is(l.To, b)) || (Is(l.From, b) && Is(l.To, a))) ?? false;
                if (!made && ++_joinGestureWaited < 20) return ScriptedInput.Step.Again;
                _joinGesturePhase = 0;
                SetJoining(false);
                return ScriptedInput.Step.Next;
            }
        }
    }

    // ---- reading and checking ---------------------------------------------------------------------------------------------------------

    /// <summary>LABEL:target.field: a machine's field (its runtime's, or a view reading such as a crate's cover).</summary>
    private double DirectorReadPath(string path)
    {
        int colon = path.IndexOf(':');
        if (colon < 0) throw new InvalidOperationException($"'{path}': name the machine, LABEL:target.field");
        string label = path[..colon], rest = path[(colon + 1)..];
        int dot = rest.IndexOf('.');
        if (dot < 0) throw new InvalidOperationException($"'{path}': name the field as target.field");
        if (!_byName.TryGetValue(label, out var view)) throw new InvalidOperationException($"no machine {label}");
        return view.TryReadField(rest[..dot], rest[(dot + 1)..], out double v) ? v : throw new InvalidOperationException($"{label} has no field {rest}");
    }

    private static readonly Regex Fill = new(@"\{([^:{}]+:[^:{}]+)(?::([^{}]+))?\}");

    private string DirectorFill(string text) => Fill.Replace(text, m =>
    {
        try { return DirectorReadPath(m.Groups[1].Value).ToString(m.Groups[2].Success ? m.Groups[2].Value : "0.##", CultureInfo.InvariantCulture); }
        catch (Exception) { return "?"; }
    });

    private ScriptedInput.Step Expect(string[] w)
    {
        var inv = CultureInfo.InvariantCulture;
        double D(int i) => double.Parse(w[i], inv);
        string what = string.Join(' ', w.Skip(1));
        try
        {
            (bool ok, string seen) = w[1] switch
            {
                "rover" when w[2] == "near" => Near(D(3), D(4), D(5)),
                "field" => Field(w[2], w[3], D(4), w.Length > 5 ? D(5) : double.NaN),
                "built" => Built(w[2], int.Parse(w[3], inv)),
                "link" => Linked(w[2], w[3], w[4]),
                "zone" => Zoned(w[2], w[3]),
                _ => throw new InvalidOperationException($"expect {w[1]}: no such check"),
            };
            if (!ok) { DirectorFail($"expect {what}: {seen}"); return ScriptedInput.Step.Next; }
            GD.Print($"[director] expect ok: {what} ({seen})");
        }
        catch (Exception e) { DirectorFail($"expect {what}: {e.Message}"); }
        return ScriptedInput.Step.Continue;

        (bool, string) Near(double x, double z, double tol)
        {
            var p = _rover!.Chassis.GlobalPosition;
            double d = Math.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));
            return (d <= tol, $"rover at ({p.X:0.00} {p.Z:0.00}), {d:0.00} m off");
        }
        (bool, string) Field(string path, string how, double a, double b)
        {
            double v = DirectorReadPath(path);
            bool ok = how switch { "above" => v > a, "below" => v < a, "between" => v >= a && v <= b, _ => throw new InvalidOperationException($"{how}: above, below or between") };
            return (ok, $"{path} = {v.ToString("0.####", inv)}");
        }
        (bool, string) Built(string label, int n)
        {
            if (!_byName.TryGetValue(label, out var view)) return (false, $"no machine {label}");
            int parts = view.Runtime.Def.Parts.Count;
            return (parts == n, $"{label} has {parts} parts");
        }
        (bool, string) Linked(string kind, string from, string to)
        {
            bool Is(LinkEnd e, string s) => $"{e.Label}.{e.Part}" == s;
            bool ok = _world?.Links.Any(l => l.Kind == kind && Is(l.From, from) && Is(l.To, to)) ?? false;
            return (ok, ok ? "joined" : $"links: {string.Join(", ", _world?.Links.Select(l => $"{l.Kind} {l.From.Label}.{l.From.Part}->{l.To.Label}.{l.To.Part}") ?? [])}");
        }
        (bool, string) Zoned(string store, string room)
        {
            var holds = _zones.Holds;
            bool ok = holds.Any(h => $"{h.StoreLabel}.{h.Store}" == store && $"{h.RoomLabel}.{h.Room}" == room);
            return (ok, ok ? "held" : $"zones: {string.Join(", ", holds)}");
        }
    }

    // ---- on screen --------------------------------------------------------------------------------------------------------------------

    private void DirectorOverlay()
    {
        if (_directorLayer is not null) return;
        _directorLayer = new CanvasLayer { Layer = 55, Name = "Director" };
        AddChild(_directorLayer);
        Label Make(int size) => new()
        {
            HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = size, OutlineSize = 8, OutlineColor = Colors.Black, FontColor = new Color(1, 0.97f, 0.88f) },
        };
        _captionLabel = Make(26);
        _captionLabel.AnchorLeft = 0.12f; _captionLabel.AnchorRight = 0.88f; _captionLabel.AnchorTop = 0.80f; _captionLabel.AnchorBottom = 0.97f;
        _clockLabel = Make(20);
        _clockLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _clockLabel.AnchorLeft = 0.3f; _clockLabel.AnchorRight = 0.7f; _clockLabel.AnchorTop = 0.01f; _clockLabel.AnchorBottom = 0.07f;
        _clockLabel.Visible = false;
        _directorLayer.AddChild(_captionLabel);
        _directorLayer.AddChild(_clockLabel);
    }

    private void DirectorClock()
    {
        if (_clockLabel is null || _current is null) return;
        var sun = _current.Runtime.Sun;
        double h = ((sun.Time % 24) + 24) % 24;
        int hh = (int)h, mm = (int)((h - hh) * 60);
        string bank = AllBanks().FirstOrDefault() is { Bank: { } b } ? $"   bank {b.ChargeWh:0} of {b.CapacityWh:0} Wh at {b.Temperature:0.0} °C" : "";
        _clockLabel.Text = $"Sol {sun.SolNumber}  {hh:00}:{mm:00}{bank}{(_sleep.Active ? "   (asleep)" : "")}";
    }
}
