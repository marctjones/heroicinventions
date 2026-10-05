using Godot;

namespace HeroicInventions;

/// <summary>
/// The window's menu bar: File, Machine, Simulation, View and Help, driving
/// the same handlers as the side panel's buttons and the keys, so anything
/// the panel does can be reached from the menu too. On macOS (and wherever
/// else Godot has a global menu) the bar is the operating system's own, at
/// the top of the screen; elsewhere it is drawn across the top of the window
/// and the two panels are pushed down to make room.
/// </summary>
public partial class Main
{
    private const int InlineMenuBarHeight = 32;
    private int _menuInset;   // how far the panels sit below the window's top: 0 under a native bar

    private MenuBar _menuBar = null!;
    private AcceptDialog? _controlsDialog;

    /// <summary>One popup menu: its entries' actions by id, and what to re-check each time it opens.</summary>
    private sealed class MenuSpec(PopupMenu popup)
    {
        public PopupMenu Popup { get; } = popup;
        public Dictionary<int, Action> Actions { get; } = [];
        private int _next;

        public int Add(string label, Action action, Key accelerator = Key.None, bool command = true)
        {
            int id = _next++;
            Popup.AddItem(label, id, accelerator == Key.None ? Key.None : (Key)((command ? (int)KeyModifierMask.MaskCmdOrCtrl : 0) | (int)accelerator));
            Actions[id] = action;
            return id;
        }

        public int AddCheck(string label, Action action)
        {
            int id = Add(label, action);
            Popup.SetItemAsCheckable(Popup.GetItemIndex(id), true);
            return id;
        }

        public int AddRadio(string label, Action action)
        {
            int id = Add(label, action);
            Popup.SetItemAsRadioCheckable(Popup.GetItemIndex(id), true);
            return id;
        }

        public void Enable(int id, bool on) => Popup.SetItemDisabled(Popup.GetItemIndex(id), !on);
        public void Check(int id, bool on) => Popup.SetItemChecked(Popup.GetItemIndex(id), on);
        public void Label(int id, string text) => Popup.SetItemText(Popup.GetItemIndex(id), text);
    }

    private MenuSpec NewMenu(string title, Node parent)
    {
        var popup = new PopupMenu { Name = title };
        parent.AddChild(popup);
        var spec = new MenuSpec(popup);
        popup.IdPressed += id => { if (spec.Actions.TryGetValue((int)id, out var act)) act(); };
        return spec;
    }

    private bool MachineOnScreen => _current is not null || _views.Count > 0;

    private void BuildMenuBar(CanvasLayer layer)
    {
        _menuBar = new MenuBar { PreferGlobalMenu = true };
        _menuBar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        layer.AddChild(_menuBar);

        BuildFileMenu();
        BuildMachineMenu();
        BuildSimulationMenu();
        BuildViewMenu();
        BuildHelpMenu();

        // A native bar takes no room in the window; an inline one does.
        bool native = DisplayServer.HasFeature(DisplayServer.Feature.GlobalMenu);
        _menuInset = native ? 0 : InlineMenuBarHeight;
        _leftPanel.OffsetTop = 20 + _menuInset;
        _infoPanel.OffsetTop = 20 + _menuInset;
    }

    // ----------------------------------------------------------------- File

    private void BuildFileMenu()
    {
        var m = NewMenu("File", _menuBar);
        int save = m.Add("Save", () => SaveWorld(auto: false), Key.S);
        int load = m.Add("Load Latest Save", LoadLatestSave, Key.O);
        m.Popup.AddSeparator();
        int back = m.Add("Back to Machine List", DeselectMachine, Key.W);
        m.Popup.AddSeparator();
        m.Add("Quit", () => GetTree().Quit(), Key.Q);

        m.Popup.AboutToPopup += () =>
        {
            m.Enable(save, MachineOnScreen);
            m.Enable(load, MachineOnScreen);
            m.Enable(back, MachineOnScreen);
        };
    }

    // -------------------------------------------------------------- Machine

    private void BuildMachineMenu()
    {
        var m = NewMenu("Machine", _menuBar);

        var list = NewMenu("Run Machine", m.Popup);
        foreach (var (name, _) in _machineFiles)
        {
            string captured = name;
            list.Add(DisplayNames.GetValueOrDefault(name, name), () => SelectMachine(captured));
        }
        m.Popup.AddSubmenuNodeItem("Run Machine", list.Popup);

        m.Add("Every Machine, Together", () => LoadWorldNamed("gallery"));
        m.Add("Build Mode", SelectBuildMode);
        m.Popup.AddSeparator();
        int edit = m.Add("Edit This Machine", EditFocused);
        int join = m.Add("Join Machines", () => SetJoining(!_joining));

        m.Popup.AboutToPopup += () =>
        {
            m.Enable(edit, _views.Count > 0 && _buildMode is null);
            m.Enable(join, _world is not null);
            m.Label(join, _joining ? "Stop Joining" : "Join Machines");
        };
    }

    // ----------------------------------------------------------- Simulation

    private void BuildSimulationMenu()
    {
        var m = NewMenu("Simulation", _menuBar);
        int run = m.Add("Pause", () => SetRunning(!_running));
        int restart = m.Add("Restart", RestartCurrent);
        int fire = m.Add("Fire / Fuel", () => _current?.ToggleFire());
        m.Popup.AddSeparator();

        var speed = NewMenu("Speed", m.Popup);
        var speedIds = new List<int>();
        foreach (var (scale, label) in Speeds)
        {
            double captured = scale;
            speedIds.Add(speed.AddRadio(label, () => SetSpeed(captured)));
        }
        speed.Popup.AddSeparator();
        speed.Add("Faster", () => StepSpeed(+1), Key.Bracketright);
        speed.Add("Slower", () => StepSpeed(-1), Key.Bracketleft);
        m.Popup.AddSubmenuNodeItem("Speed", speed.Popup);
        speed.Popup.AboutToPopup += () =>
        {
            for (int i = 0; i < Speeds.Length; i++)
                speed.Check(speedIds[i], Mathf.IsEqualApprox((float)_timeScale, (float)Speeds[i].Scale));
        };

        m.Popup.AboutToPopup += () =>
        {
            m.Enable(run, _current is not null || _views.Count > 0);
            m.Label(run, _running ? "Pause" : "Run");
            m.Enable(restart, !_restartButton.Disabled);
            m.Enable(fire, _current is not null);
        };
    }

    /// <summary>Moves to the next faster or slower of the speed row's settings, stopping at the ends.</summary>
    private void StepSpeed(int direction)
    {
        int at = Array.FindIndex(Speeds, s => Mathf.IsEqualApprox((float)s.Scale, (float)_timeScale));
        if (at < 0) at = Array.FindIndex(Speeds, s => s.Scale >= _timeScale);   // a speed between presets: step from where it falls
        if (at < 0) at = Speeds.Length - 1;
        SetSpeed(Speeds[Math.Clamp(at + direction, 0, Speeds.Length - 1)].Scale);
    }

    // ----------------------------------------------------------------- View

    private void BuildViewMenu()
    {
        var m = NewMenu("View", _menuBar);
        int details = m.AddCheck("Machine Details", () => { if (!_detailsButton.Disabled) _detailsButton.EmitSignal(BaseButton.SignalName.Pressed); });
        int hud = m.AddCheck("Information Panel", () =>
        {
            _hudHidden = !_hudHidden;
            _infoPanel.Visible = !_hudHidden && _buildMode is null;
        });
        int side = m.AddCheck("Control Panel", () => _leftPanel.Visible = !_leftPanel.Visible && _buildMode is null);
        m.Popup.AddSeparator();
        int reset = m.Add("Reset Camera", ResetCamera);
        m.Popup.AddSeparator();

        var size = NewMenu("Window Size", m.Popup);
        foreach (var (w, h, label) in WindowSizes)
        {
            int width = w, height = h;
            size.Add($"{label} ({w}×{h})", () => SetWindowSize(width, height));
        }
        m.Popup.AddSubmenuNodeItem("Window Size", size.Popup);
        int full = m.AddCheck("Fullscreen", () => ToggleFullscreen(GetWindow().Mode != Window.ModeEnum.Fullscreen));

        m.Popup.AboutToPopup += () =>
        {
            m.Enable(details, !_detailsButton.Disabled);
            m.Check(details, _showDetails);
            m.Check(hud, !_hudHidden);
            m.Check(side, _leftPanel.Visible);
            m.Enable(reset, _buildMode is null);
            m.Check(full, GetWindow().Mode == Window.ModeEnum.Fullscreen);
        };
    }

    /// <summary>Back to the framing the machine (or the menu) first opened with.</summary>
    private void ResetCamera()
    {
        var fallback = MenuCamera with { Eye = new Vector3(0, 1, 2) };
        ApplyCamera(_currentName is { } name && _views.Count == 0 ? Profiles.GetValueOrDefault(name, fallback) : MachineOnScreen ? fallback : MenuCamera);
    }

    // ----------------------------------------------------------------- Help

    private void BuildHelpMenu()
    {
        var m = NewMenu("Help", _menuBar);
        m.Add("Keyboard and Mouse Controls", ShowControls);
    }

    private void ShowControls()
    {
        if (_controlsDialog is null)
        {
            _controlsDialog = new AcceptDialog { Title = "Controls", DialogText = _hudControls.Text, Exclusive = false };
            _controlsDialog.DialogText += "\nCmd/Ctrl+S save · Cmd/Ctrl+O load · Cmd/Ctrl+W back to the list · Cmd/Ctrl+] / [ faster / slower";
            AddChild(_controlsDialog);
        }
        _controlsDialog.PopupCentered();
    }
}
