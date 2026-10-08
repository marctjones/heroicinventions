using Godot;

namespace HeroicInventions;

/// <summary>
/// HEROIC_SET="target field value [at]; ...": what a test's hand does to a
/// running machine. A setting with no time is applied before the first step;
/// one with a time waits in a queue until the run's clock reaches it, then is
/// applied at the top of the next physics step -- the order SimHost's
/// ApplyDue uses, so the same list means the same thing in both run paths
/// (issue #150). A setting that can't be read or applied is an error, printed
/// and remembered: a run that quits on its own sim-seconds limit then exits
/// non-zero, so a test never goes on to check a machine nobody touched.
/// </summary>
public partial class Main
{
    private readonly List<(string Target, string Field, double Value, double At)> _timedSettings = new();
    private bool _heroicSetFailed;

    private void ApplyHeroicSet(string text)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var setting in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = setting.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            double at = 0;
            if (parts.Length is not (3 or 4)
                || !double.TryParse(parts[2], inv, out double value)
                || (parts.Length == 4 && !double.TryParse(parts[3], inv, out at)))
            {
                SettingFailed($"expected 'target field value [at]', got '{setting}'");
                continue;
            }
            _timedSettings.Add((parts[0], parts[1], value, at));
        }
        ApplyDueSettings();
    }

    private void ApplyDueSettings()
    {
        if (_timedSettings.Count == 0 || _current is null) return;
        double now = _current.Runtime.Time;
        foreach (var due in _timedSettings.Where(s => s.At <= now + 1e-9).ToList())
        {
            _timedSettings.Remove(due);
            try { _current.Runtime.SetField(due.Target, due.Field, due.Value); }
            catch (Exception e) { SettingFailed($"'{due.Target} {due.Field} {due.Value} {due.At}': {e.Message}"); }
        }
    }

    // a setting still waiting when the run ends was never applied: the run was shorter than the test thought
    private void ReportUnappliedSettings()
    {
        foreach (var s in _timedSettings) SettingFailed($"'{s.Target} {s.Field} {s.Value} {s.At}' was never applied: the run ended first");
        _timedSettings.Clear();
    }

    private void SettingFailed(string message)
    {
        _heroicSetFailed = true;
        GD.PrintErr($"HEROIC_SET: {message}");
    }
}
