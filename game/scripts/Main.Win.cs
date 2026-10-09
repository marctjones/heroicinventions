using Godot;
using HeroicInventions.Sim.Electrics;

namespace HeroicInventions;

/// <summary>
/// The win, in the rover's panel (issue #64). The right panel's Bank section says what the found battery bank holds and why it
/// is or isn't taking charge (between 0 and 45 C, not full), how fast the generators are charging it and from what, and how long to
/// the relay pass; once a bank has made the call at the pass, a banner says the game is won. The physics is the sim's
/// (<see cref="MachineRuntime.Won"/>, the bank's own rules); this only reads it. Hooks: <c>BuildRoverPanels</c> adds the section,
/// <c>UpdateRoverHud</c> refreshes it.
/// </summary>
public partial class Main
{
    private Label? _roverBank;        // the Bank section's text
    private Label? _winBanner;
    private bool _winAnnounced;

    /// <summary>Every bank in the world (or the focused machine), with its generators.</summary>
    private IEnumerable<(MachineView View, BatteryBank Bank)> AllBanks() =>
        (_views.Count > 0 ? _views : _current is null ? [] : [_current]).SelectMany(v => v.Runtime.Banks.Values.Select(b => (v, b)));

    private static string HoursIn(double hours) =>
        hours < 1 / 60.0 ? "now" : hours >= 1 ? $"{(int)hours} h {(int)Math.Round((hours - (int)hours) * 60)} min" : $"{(int)Math.Round(hours * 60)} min";

    /// <summary>The Bank section's text.</summary>
    private string BankReport()
    {
        var banks = AllBanks().ToList();
        if (banks.Count == 0) return "No battery bank found yet. It is somewhere in the cargo.";
        var lines = new List<string>();
        foreach (var (view, b) in banks)
        {
            string name = _byName.FirstOrDefault(kv => kv.Value == view).Key is { } m ? $"{b.Name} ({m})" : b.Name;
            // every generator charging it, in its own machine or wired from another (#208)
            var machines = _views.Count > 0 ? _views : (IEnumerable<MachineView>)[view];
            double watts = machines.SelectMany(v => v.Runtime.Generators.Values).Where(g => g.Bank == b).Sum(g => g.Delivered);
            lines.Add($"{name}: {b.ChargeWh:0.0} of {b.CapacityWh:0.0} Wh ({b.Fraction * 100:0}%) at {b.Temperature:0.0} °C" + TunedNote("bank-capacity"));
            string state = b.Won ? $"The call went out on sol {b.WonAtSol}. The game is won."
                : b.Ready ? "Full and warm enough: ready to call."
                : b.Full ? $"Full, but {b.Temperature:0.#} °C is outside {b.MinChargeC:0} to {b.MaxChargeC:0} °C: the call would not go."
                : b.Temperature < b.MinChargeC ? "Too cold to charge (below 0 °C)."
                : b.Temperature > b.MaxChargeC ? "Too hot to charge (above 45 °C)."
                : watts > 0 ? $"Charging at {watts:0} W ({watts / b.Volts:0.0} A)." : "Not charging: nothing is turning its generator fast enough.";
            lines.Add(state);
            if (b.Sources.Count > 0)
                lines.Add("From " + string.Join(", ", b.Sources.Select(kv => $"{kv.Key} {kv.Value / BatteryBank.JoulesPerWattHour:0.0} Wh")) + ".");
            if (!b.Won)
            {
                double sunTime = view.Runtime.Sun.Time;
                lines.Add(b.CallAnyTime ? "The call may go at any hour (easy setting)."
                    : b.InWindow(sunTime) ? $"The relay pass is open ({MachineView.HoursText(b.CallHour)} + {b.CallMinutes:0} min)." + TunedNote("call-window")
                    : $"Next relay pass at {MachineView.HoursText(b.CallHour)}, in {HoursIn((b.CallHour - sunTime + 24) % 24)} of local time.");
            }
        }
        return string.Join("\n", lines);
    }

    /// <summary>Refreshes the section and raises the banner the first time any bank has made the call.</summary>
    private void UpdateWinHud()
    {
        _roverBank!.Text = BankReport();
        bool won = AllBanks().Any(x => x.Bank.Won);
        if (won && _winBanner is null)
        {
            var layer = new CanvasLayer { Layer = 60 };
            AddChild(layer);
            _winBanner = new Label
            {
                Text = "THE CALL WENT OUT\nThe bank was full and warm on the relay pass. You have called Earth.",
                HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(400, 40), Size = new Vector2(800, 120),
            };
            _winBanner.AddThemeFontSizeOverride("font_size", 26);
            _winBanner.AddThemeColorOverride("font_color", new Color(0.65f, 1f, 1f));
            _winBanner.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            _winBanner.AddThemeConstantOverride("outline_size", 8);
            layer.AddChild(_winBanner);
        }
        if (won && !_winAnnounced)
        {
            _winAnnounced = true;
            GD.Print("[view] the call went out: the game is won");
        }
    }
}
