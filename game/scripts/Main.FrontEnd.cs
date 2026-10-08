using Godot;

namespace HeroicInventions;

/// <summary>
/// The front end (issue #95): the title page, New game and scenario select, Continue and Load, and the opening in which the rim
/// comes down on the cargo. The ending and the rover log are Main.Ending.cs and Main.RoverLog.cs; this file owns the screens' shared
/// frame (one full-screen page at a time, on its own CanvasLayer above the HUD) and the state machine between them.
///
/// <c>Screen</c> says what is on: None (the game or the machine list, as before), Title, Scenario, Load, Opening, Ending or Log. Every screen
/// but None takes the keyboard (<see cref="FrontEndInput"/>, first in _UnhandledInput) and holds the rover still (<see cref="FrontEndTick"/>,
/// first in _Process), so a held arrow key drives nothing behind a page.
///
/// A plain launch (no HEROIC_WORLD, HEROIC_AUTOSELECT, HEROIC_INPUT ... set) opens on the title page; every scripted run goes straight to
/// what it asked for, as before. HEROIC_FRONTEND=1 forces the title page, =0 keeps it away. The opening plays only from New game: Continue,
/// Load and a world opened from the machine list go straight to the rover.
///
/// Scripted checks: "frontend screen" prints the screen; "frontend press TEXT" presses the visible front-end button whose text holds TEXT;
/// "frontend until SCREEN" waits for a screen; "frontend log" toggles the log; "frontend skip" skips the opening; "frontend save" saves the world as the Save button does; "frontend menu" goes back to the title page. Each screen change prints
/// "[frontend] screen: NAME".
/// </summary>
public partial class Main
{
    private enum Screen { None, Title, Scenario, Load, Opening, Ending, Log }

    private Screen _screen = Screen.None;
    private CanvasLayer _frontLayer = null!;
    private Control? _front;                // the page on show
    private Button? _continueButton, _logButton;
    private ScenarioInfo? _scenario;        // what New game started

    /// <summary>The short lines of the opening, from the design doc's Premise (docs/lonely-rover.html). Each stays up CaptionSeconds.</summary>
    private static readonly string[] OpeningCaptions =
    [
        "A dust storm closed over the rover. When the sky cleared, the link to Earth had gone quiet.",
        "Beside the landing site stand the crates of a habitat no one has occupied: panels, cylinders, tools, a battery bank.",
        "The storm has weakened the crater's rim. Now a section of it comes down onto the cargo.",
        "The rover cannot make electronics, only mechanisms. Earth is probably still calling. Find the battery bank, charge it, and call back at 03:00.",
    ];
    private const double CaptionSeconds = 7.5;
    private Control? _caption;
    private Label? _captionText;
    private double _openingTime;
    private int _openingShown = -1;
    private bool _openingHeld;     // the world is paused for the first captions
    private const int RimCaption = 2;   // the caption on which the world is let go

    // ---------------------------------------------------------------- building

    private void BuildFrontEnd()
    {
        _frontLayer = new CanvasLayer { Layer = 70 };
        AddChild(_frontLayer);
        HudTheme.Install(_frontLayer);

        // the machine list's own way in: New game and Continue at its top, a Rover log button beside "Back to menu"
        var newGame = BigButton("New game");
        newGame.Pressed += ShowScenarios;
        _continueButton = BigButton("Continue");
        _continueButton.Pressed += Continue;
        var mainMenu = BigButton("Main menu");
        mainMenu.Pressed += () => { if (_current is not null || _views.Count > 0) DeselectMachine(); ShowTitle(); };
        _machineList.AddChild(newGame); _machineList.MoveChild(newGame, 0);
        _machineList.AddChild(_continueButton); _machineList.MoveChild(_continueButton, 1);
        _machineList.AddChild(mainMenu); _machineList.MoveChild(mainMenu, 2);
        _logButton = BigButton("Rover log (I)");
        _logButton.Visible = false;
        _logButton.Pressed += ShowRoverLog;
        var col = _menuButton.GetParent();
        col.AddChild(_logButton);
        col.MoveChild(_logButton, _menuButton.GetIndex());

        if (FrontEndWanted()) ShowTitle();
    }

    private static bool FrontEndWanted()
    {
        string setting = OS.GetEnvironment("HEROIC_FRONTEND");
        if (setting == "0") return false;
        if (setting == "1") return true;
        return new[] { "HEROIC_WORLD", "HEROIC_AUTOSELECT", "HEROIC_AUTORUN", "HEROIC_EDITOR", "HEROIC_EDITOR_INPUT", "HEROIC_INPUT", "HEROIC_LOAD", "HEROIC_LIVE_LINK" }
            .All(v => string.IsNullOrEmpty(OS.GetEnvironment(v)));
    }

    private void SetScreen(Screen screen)
    {
        if (_screen == screen) return;
        _screen = screen;
        if (_hints is not null) _hints.Modulate = screen is Screen.None ? Colors.White : Colors.Transparent;   // no first-run hint over a page (it keeps its own Visible)
        GD.Print($"[frontend] screen: {screen}");
    }

    /// <summary>Takes the page down (the Opening's caption stays until the opening ends).</summary>
    private void CloseFront()
    {
        if (_front is { } page && IsInstanceValid(page)) { _frontLayer.RemoveChild(page); page.QueueFree(); }
        _front = null;
        if (_screen is not (Screen.Opening)) SetScreen(Screen.None);
    }

    /// <summary>A new full-screen page: dim backdrop, a centred panel of the given width. Returns the column to fill. Replaces any page on show.</summary>
    private VBoxContainer Page(Screen screen, string heading, string? sub, int width = 560, int headingSize = 34)
    {
        if (_front is { } old && IsInstanceValid(old)) { _frontLayer.RemoveChild(old); old.QueueFree(); }
        var root = new Control { Name = $"Page{screen}", MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = screen is Screen.Title or Screen.Scenario or Screen.Load ? HudTheme.Panel : HudTheme.Backdrop };   // over the empty menu, solid: the panels behind it do not show
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(dim);
        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(centre);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        centre.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        panel.AddChild(col);
        col.AddChild(HudTheme.Heading(heading, headingSize, centred: true));
        if (sub is not null) col.AddChild(HudTheme.Body(sub, 16, dim: true, centred: true));
        col.AddChild(new HSeparator());
        _frontLayer.AddChild(root);
        _front = root;
        SetScreen(screen);
        return col;
    }

    private static Button PageButton(string text, Action pressed, int size = 20)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 46) };
        b.AddThemeFontSizeOverride("font_size", size);
        b.Pressed += pressed;
        return b;
    }

    // ------------------------------------------------------------------- title

    private void ShowTitle()
    {
        var col = Page(Screen.Title, "Heroic Inventions", "Pre-industrial machines, with the real planet's numbers in the formulas.", 520, 40);
        var newest = NewestSave();
        var first = PageButton("New game", ShowScenarios);
        col.AddChild(first);
        var cont = PageButton(newest is { } n ? $"Continue ({SaveTitle(n)})" : "Continue", Continue);
        cont.Disabled = newest is null;
        col.AddChild(cont);
        var load = PageButton("Load game...", ShowLoad);
        load.Disabled = ListSaves().Count == 0;
        col.AddChild(load);
        col.AddChild(PageButton("Machine workshop", CloseFront));
        col.AddChild(PageButton("Build mode", () => { CloseFront(); SelectBuildMode(); }));
        col.AddChild(PageButton("Quit", () => GetTree().Quit()));
        first.GrabFocus();
        _continueButton!.Disabled = newest is null;
    }

    // ------------------------------------------------------------ scenario select

    private void ShowScenarios()
    {
        var col = Page(Screen.Scenario, "New game", "Choose a scenario.", 720);
        foreach (var s in ScenarioList.Discover(WorldsDir))
        {
            var card = HudTheme.Card();
            var inner = new VBoxContainer();
            inner.AddThemeConstantOverride("separation", 8);
            card.AddChild(inner);
            inner.AddChild(HudTheme.Heading(s.Title, 26));
            inner.AddChild(HudTheme.Body(s.Description, 16));
            var start = PageButton($"Start {s.Title}", () => StartScenario(s));
            inner.AddChild(start);
            col.AddChild(card);
            start.GrabFocus();
        }
        col.AddChild(PageButton("Back", ShowTitle, 16));
    }

    /// <summary>A new game of this scenario: its world loaded (the rim collapses on load, #61), and the opening over it.</summary>
    private void StartScenario(ScenarioInfo scenario)
    {
        CloseFront();
        _scenario = scenario;
        _roverLog.Clear();
        _logWorld = null;
        LoadWorldNamed(scenario.World);
        if (RoverIsPlayer) BeginOpening(); else SetScreen(Screen.None);
    }

    // ---------------------------------------------------------------- saves

    private sealed record SaveFile(string Path, string Name, bool Auto, DateTime Modified);

    private static List<SaveFile> ListSaves()
    {
        var list = new List<SaveFile>();
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(SavesDir))) return list;
        foreach (string file in DirAccess.GetFilesAt(SavesDir).Where(f => f.EndsWith(".save")))
        {
            string full = ProjectSettings.GlobalizePath($"{SavesDir}/{file}");
            string stem = file[..^".save".Length];
            bool auto = stem.EndsWith(".autosave");
            list.Add(new SaveFile(full, auto ? stem[..^".autosave".Length] : stem, auto, System.IO.File.GetLastWriteTime(full)));
        }
        return [.. list.OrderByDescending(s => s.Modified)];
    }

    private static SaveFile? NewestSave() => ListSaves().FirstOrDefault();

    private string SaveTitle(SaveFile s) =>
        (ScenarioList.Discover(WorldsDir).FirstOrDefault(x => x.World == s.Name)?.Title ?? DisplayNames.GetValueOrDefault(s.Name, s.Name)) + (s.Auto ? ", autosave" : "");

    private void Continue()
    {
        if (NewestSave() is { } save) OpenSave(save);
    }

    /// <summary>Takes up a save with no opening: the rover where it was left.</summary>
    private void OpenSave(SaveFile save)
    {
        CloseFront();
        _roverLog.Clear();
        _logWorld = null;
        LoadSave(save.Path);
        SetScreen(Screen.None);
    }

    private void ShowLoad()
    {
        var col = Page(Screen.Load, "Load game", "The newest first. A save is the whole world: machines, ground and rover.", 640);
        var saves = ListSaves();
        if (saves.Count == 0) col.AddChild(HudTheme.Body("No saves yet. Save from the left panel, or quit: the game keeps an autosave.", 16, dim: true));
        foreach (var s in saves.Take(10))
        {
            var captured = s;
            col.AddChild(PageButton($"{SaveTitle(s)}   ·   {s.Modified:yyyy-MM-dd HH:mm}", () => OpenSave(captured), 16));
        }
        col.AddChild(PageButton("Back", ShowTitle, 16));
    }

    // ----------------------------------------------------------------- opening

    private void BeginOpening()
    {
        SetScreen(Screen.Opening);
        _openingTime = 0;
        _openingShown = -1;
        _rover!.Frozen = true;   // the rover waits; the crater is the show
        SetRunning(false);       // and the world holds, whole, until the third caption: then the rim comes down
        _openingHeld = true;
        _leftPanel.Visible = false;
        _infoPanel.Visible = false;

        // the camera on the cargo, from behind the rover, a little above: the rim stands beyond the crates
        double x = 0, z = 0;
        var placements = _world!.Placements;
        foreach (var p in placements) { x += p.At.X; z += p.At.Z; }
        if (placements.Count > 0) { x /= placements.Count; z /= placements.Count; }
        else { x = _rover.Chassis.GlobalPosition.X; z = _rover.Chassis.GlobalPosition.Z; }
        double y = _groundSim?.Ground.HeightAt(x, z) ?? 0;
        _follow = null;
        _orbit.Pivot = new Vector3((float)x, (float)y + 8f, (float)z);
        _orbit.Distance = 55f;
        _orbit.Pitch = 0.30f;
        _orbit.Yaw = RoverHeading();
        _orbit.Apply();

        // the caption: a panel along the bottom, over the scene, ignoring the mouse everywhere but its Skip button
        var holder = new VBoxContainer { Name = "Caption", MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        holder.AddChild(centre);
        holder.AddChild(new Control { CustomMinimumSize = new Vector2(0, 40), MouseFilter = Control.MouseFilterEnum.Ignore });
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(860, 0) };
        centre.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        panel.AddChild(col);
        _captionText = HudTheme.Body("", 22, centred: true);
        _captionText.CustomMinimumSize = new Vector2(820, 64);
        col.AddChild(_captionText);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var hint = HudTheme.Body("Space, Enter or Esc to skip", 14, dim: true);
        hint.AutowrapMode = TextServer.AutowrapMode.Off;
        row.AddChild(hint);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });
        var skip = new Button { Text = "Skip" };
        skip.Pressed += () => EndOpening(skipped: true);
        row.AddChild(skip);
        col.AddChild(row);
        _frontLayer.AddChild(holder);
        _caption = holder;
        ShowCaption(0);
    }

    private void ShowCaption(int index)
    {
        _openingShown = index;
        _captionText!.Text = OpeningCaptions[index];
        GD.Print($"[frontend] caption {index + 1} of {OpeningCaptions.Length}: {OpeningCaptions[index]}");
    }

    private void OpeningTick(double delta)
    {
        _openingTime += delta;
        int index = (int)(_openingTime / CaptionSeconds);
        if (index >= OpeningCaptions.Length) { EndOpening(skipped: false); return; }
        if (index != _openingShown) ShowCaption(index);
        if (index >= RimCaption && _openingHeld) { _openingHeld = false; SetRunning(true); }
        double into = _openingTime - index * CaptionSeconds;
        if (_caption is not null) _caption.Modulate = new Color(1, 1, 1, (float)Math.Clamp(Math.Min(into / 0.6, (CaptionSeconds - into) / 0.6), 0, 1));
        if (RoverIsPlayer) _rover!.Frozen = true;
        _orbit.Yaw += 0.012f * (float)delta;   // a slow drift, so the shot is not a still
        _orbit.Apply();
    }

    /// <summary>The opening is over, played out or skipped: the panels come back, the camera goes behind the rover, and the rover has the keys.</summary>
    private void EndOpening(bool skipped)
    {
        if (_screen != Screen.Opening) return;
        if (_caption is { } c && IsInstanceValid(c)) { _frontLayer.RemoveChild(c); c.QueueFree(); }
        _caption = null;
        if (_openingHeld) { _openingHeld = false; SetRunning(true); }
        _screen = Screen.None;   // (not through SetScreen: the print below is the opening's own)
        GD.Print($"[frontend] opening {(skipped ? "skipped" : "finished")} after {_openingTime:F1} s; the rover has control");
        GD.Print("[frontend] screen: None");
        _hints.Modulate = Colors.White;
        _leftPanel.Visible = true;
        _infoPanel.Visible = !_hudHidden;
        if (RoverIsPlayer)
        {
            _rover!.Frozen = !_running;
            _roverHeading = RoverHeading();
            _orbit.Pivot = _rover.Chassis.GlobalPosition;
            _orbit.Yaw = _roverHeading;
            _orbit.Pitch = RoverCameraPitch;
            _orbit.Distance = RoverCameraDistance;
            _orbit.Apply();
        }
    }

    // ------------------------------------------------------------ input and frame

    /// <summary>Called first from _UnhandledInput: true when the event belongs to a page (or opens the log) and nothing else may act on it.</summary>
    private bool FrontEndInput(InputEvent @event)
    {
        if (_screen == Screen.None)
        {
            if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.I, CtrlPressed: false, MetaPressed: false, AltPressed: false } && RoverIsPlayer && !Typing())
            {
                ShowRoverLog();
                return true;
            }
            return false;
        }
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (_screen)
            {
                case Screen.Opening when key.Keycode is Key.Space or Key.Enter or Key.KpEnter or Key.Escape:
                    EndOpening(skipped: true);
                    break;
                case Screen.Scenario or Screen.Load when key.Keycode == Key.Escape:
                    ShowTitle();
                    break;
                case Screen.Ending when key.Keycode == Key.Escape:
                    KeepPlaying();
                    break;
                case Screen.Log when key.Keycode is Key.Escape or Key.I:
                    CloseRoverLog();
                    break;
            }
        }
        return true;   // a page takes every key and click: nothing behind it acts
    }

    /// <summary>Once a frame, first in _Process. True while a page holds the rover (then the rover's keys and the camera's are off).</summary>
    private bool FrontEndTick(double delta)
    {
        bool holds = _screen != Screen.None;
        if (_logButton is not null) _logButton.Visible = RoverIsPlayer;
        if (RoverIsPlayer)
        {
            if (holds) _rover!.Command = (0, 0);
            RoverLogTick();
            EndingTick();
            if (!_hudControls.Text.Contains("I opens the rover log")) _hudControls.Text += "\nI opens the rover log";
        }
        if (_screen == Screen.Opening) OpeningTick(delta);
        return holds && _screen != Screen.None;
    }

    // ----------------------------------------------------------- scripted checks

    private ScriptedInput.Step? FrontEndStep(string[] w)
    {
        if (w[0] != "frontend") return null;
        switch (w.Length > 1 ? w[1] : "screen")
        {
            case "screen":
                GD.Print($"[frontend] now: {_screen}");
                return ScriptedInput.Step.Continue;
            case "until" when w.Length > 2:
                return _screen.ToString().Equals(w[2], StringComparison.OrdinalIgnoreCase) ? ScriptedInput.Step.Next : ScriptedInput.Step.Again;
            case "skip":
                EndOpening(skipped: true);
                return ScriptedInput.Step.Next;
            case "save":
                SaveWorld(auto: false);
                return ScriptedInput.Step.Next;
            case "menu":
                if (_screen == Screen.None) { if (MachineOnScreen) DeselectMachine(); ShowTitle(); }
                return ScriptedInput.Step.Next;
            case "log":
                if (_screen == Screen.Log) CloseRoverLog(); else ShowRoverLog();
                return ScriptedInput.Step.Next;
            case "press" when w.Length > 2:
            {
                string text = string.Join(' ', w.Skip(2));
                var button = Buttons(_frontLayer).FirstOrDefault(b => b.IsVisibleInTree() && !b.Disabled && b.Text.Contains(text, StringComparison.OrdinalIgnoreCase));
                if (button is null) { GD.Print($"[frontend] press: no button '{text}' on {_screen}"); return ScriptedInput.Step.Continue; }
                GD.Print($"[frontend] press: {button.Text}");
                button.EmitSignal(BaseButton.SignalName.Pressed);
                return ScriptedInput.Step.Next;
            }
        }
        GD.Print($"[frontend] unknown step: {string.Join(' ', w)}");
        return ScriptedInput.Step.Continue;
    }

    private static IEnumerable<Button> Buttons(Node n) => Descendants(n).OfType<Button>();
}
