using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The player is the rover (owner decision 2026-10-08, issue #94). In a world that places a rover (<c>(rover (at X Z))</c>,
/// the Lonely Rover's opening) the player drives it with a follow camera, and every click, drag and hook is the rover's own and
/// passes its capability check (reach, force, never up, only what a rover could do: Main.RoverHands.cs, #163). Aiming a mirror and
/// sending a digging gang to a clicked spot (Main.Aim.cs) are off in the game: the rover digs with its own backhoe (B). A machine
/// run has no rover and keeps the free operator unchanged. HEROIC_ROVER=0 keeps the rover out of
/// a world that has one, for tests that need the old behaviour.
///
/// The side panels show the rover (#196): the right panel its speed, nose and tilt, its arm and bucket and what it last refused; the
/// left panel only what the rover can do (sleep until, save and load, menu, speed). Editing and joining machines are build mode's.
///
/// Keys: arrows or W A S D drive (forward, back, turn); B runs the backhoe's dig-and-dump; Shift+arrows orbit the camera, + and -
/// (or Page Up / Down) zoom it, Home puts it back behind the rover; the mouse orbits and zooms as everywhere.
/// </summary>
public partial class Main
{
    private Rover? _rover;
    private VBoxContainer? _roverInfo;     // the rover's section of the right panel
    private Label? _roverDrive, _roverArm, _roverBucket, _roverEnergy, _roverNear, _roverNote;
    private Label? _roverState;
    private readonly Dictionary<Control, bool> _roverHidden = [];   // what the game hides in the panels, as it was
    private string _roverNoteSeen = "";
    private double _roverNoteAt;
    private float _roverHeading;           // the heading the camera last followed, radians
    private string? _plainControls;
    private static readonly bool RoverOff = OS.GetEnvironment("HEROIC_ROVER") == "0";

    private const float RoverCameraDistance = 9f, RoverCameraPitch = 0.42f;

    /// <summary>Whether the player is the rover in this world: the free operator is off, the arrows drive.</summary>
    private bool RoverIsPlayer => _rover is not null && IsInstanceValid(_rover);

    /// <summary>Stands the rover on the ground where the world puts it (LoadWorld, once the ground and machines are in).</summary>
    private void SpawnRover(WorldDef world)
    {
        ClearRover();
        if (RoverOff || world.Rover is not { } start || _groundSim is not { } ground) return;
        _rover = new Rover { Ground = ground.Ground, GroundHeight = ground.Ground.HeightAt, GroundGravity = ground.Water.Gravity };
        AddChild(_rover);
        _rover.Place(start.X, start.Z, start.Heading, ground.Ground.HeightAt(start.X, start.Z));
        _rover.Frozen = !_running;

        _plainControls = _hudControls.Text;
        _hudControls.Text = "Arrows or W A S D drive the rover · B backhoe: dig and dump · Space pause/run · H hide this panel · L all labels · Esc menu\n"
                          + "Drag to orbit · scroll or pinch to zoom · Shift+arrows orbit · + / − or Page Up/Down zoom · Home puts the camera behind the rover";
        BuildRoverPanels();
        InstallRoverHands();   // every action from here on passes the rover's capability check (Main.RoverHands.cs)

        _roverHeading = RoverHeading();
        _orbit.Pivot = _rover.Chassis.GlobalPosition;
        _orbit.Distance = RoverCameraDistance;
        _orbit.Pitch = RoverCameraPitch;
        _orbit.Yaw = _roverHeading;
        _orbit.Apply();
    }

    private void ClearRover()
    {
        if (_rover is null) return;
        if (_plainControls is not null) _hudControls.Text = _plainControls;
        _plainControls = null;
        RemoveRoverHands();
        RestorePanels();
        _rover.QueueFree();
        _rover = null;
    }

    private float RoverHeading()
    {
        var f = _rover!.Forward;
        return Mathf.Atan2(-f.X, -f.Z);
    }

    private static bool RoverKey(Key k) => Input.IsKeyPressed(k);

    private bool Typing() => GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit;

    /// <summary>The rover's keys (one press at a time: B, Home). True when the key was the rover's and is used up.</summary>
    private bool RoverInput(InputEvent @event)
    {
        if (!RoverIsPlayer || Typing()) return false;
        if (@event is not InputEventKey { Pressed: true } key || key.CtrlPressed || key.MetaPressed || key.AltPressed) return false;
        switch (key.Keycode)
        {
            case Key.B:
                if (!key.Echo) _rover!.StartCycle();
                break;
            case Key.E or Key.J or Key.R or Key.D:
                break;   // edit, join, restart and details belong to machine runs and build mode: used up so they do nothing here
            case Key.Home:
                _orbit.Yaw = RoverHeading();
                _orbit.Pitch = RoverCameraPitch;
                _orbit.Distance = RoverCameraDistance;
                _orbit.Apply();
                break;
            case Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right:
                break;   // held keys, read each frame (RoverProcess); here only so D doesn't open the details
            default:
                return false;
        }
        GetViewport().SetInputAsHandled();
        return true;
    }

    /// <summary>Once a frame: held keys drive the rover or move the camera, the camera follows, the HUD line updates. True when the rover has the keys.</summary>
    private bool RoverProcess(double delta)
    {
        if (!RoverIsPlayer) return false;
        var rover = _rover!;
        rover.Frozen = !_running;
        bool free = !Typing() && !Input.IsKeyPressed(Key.Ctrl) && !Input.IsKeyPressed(Key.Meta) && !Input.IsKeyPressed(Key.Alt);
        double forward = 0, turn = 0;
        float dt = (float)delta;
        if (free)
        {
            double x = (RoverKey(Key.Right) || RoverKey(Key.D) ? 1 : 0) - (RoverKey(Key.Left) || RoverKey(Key.A) ? 1 : 0);
            double y = (RoverKey(Key.Up) || RoverKey(Key.W) ? 1 : 0) - (RoverKey(Key.Down) || RoverKey(Key.S) ? 1 : 0);
            if (Input.IsKeyPressed(Key.Shift))
            {
                _orbit.Yaw += (float)x * 1.5f * dt;   // Shift+arrows swing the camera round the rover
                _orbit.Pitch = Mathf.Clamp(_orbit.Pitch - (float)y * 1.5f * dt, 0.12f, MaxPitch);
            }
            else (forward, turn) = (y, x);
            float zoom = (RoverKey(Key.Equal) || RoverKey(Key.Plus) || RoverKey(Key.KpAdd) || RoverKey(Key.Pageup) ? -1 : 0)
                       + (RoverKey(Key.Minus) || RoverKey(Key.KpSubtract) || RoverKey(Key.Pagedown) ? 1 : 0);
            if (zoom != 0) _orbit.Distance = Mathf.Clamp(_orbit.Distance * Mathf.Exp(zoom * 2.5f * dt), 3f, 80f);
        }
        rover.Command = (forward, turn);

        // the camera follows the rover and turns with it, whatever the player has swung it to; the rover is kept in the
        // clear area between the panels (FitBox shifts the pivot sideways, never backing off: mostPullBack 1)
        float heading = RoverHeading();
        _orbit.Yaw += Mathf.AngleDifference(_roverHeading, heading);
        _roverHeading = heading;
        _orbit.Pitch = Mathf.Max(_orbit.Pitch, 0.12f);
        _orbit.Pivot = rover.Chassis.GlobalPosition + Vector3.Up * 0.35f;
        _orbit.Apply();
        var box = rover.Bounds();
        FitBox(box, () => ScreenRect(box), _orbit.Distance, 1f);

        UpdateRoverHud();
        return true;
    }

    // ---- the panels (#196) ----------------------------------------------------------------------------------------

    /// <summary>The rover's section at the top of the right panel, and the panels pared down to what the rover can do.</summary>
    private void BuildRoverPanels()
    {
        var col = (VBoxContainer)_infoPanel.GetChild(0);
        _roverInfo = new VBoxContainer { Name = "RoverInfo" };
        _roverInfo.AddThemeConstantOverride("separation", 6);
        Label Line(string name, int size = 15, bool dim = false)
        {
            var l = new Label { Name = name, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0) };
            l.AddThemeFontSizeOverride("font_size", size);
            if (dim) l.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.75f));
            return l;
        }
        void Section(string title)
        {
            _roverInfo.AddChild(new HSeparator());
            _roverInfo.AddChild(SectionLabel(title, 15));
        }
        _roverInfo.AddChild(SectionLabel("Rover", 20));
        _roverInfo.AddChild(_roverState = Line("State"));
        Section("Driving");
        _roverInfo.AddChild(_roverDrive = Line("Driving"));
        Section("Arm");
        _roverInfo.AddChild(_roverArm = Line("Arm"));
        Section("Bucket");
        _roverInfo.AddChild(_roverBucket = Line("Bucket"));
        Section("Energy");
        _roverInfo.AddChild(_roverEnergy = Line("Energy", dim: true));
        Section("Bank");
        _roverInfo.AddChild(_roverBank = Line("Bank"));   // (Main.Win.cs)
        Section("Nearby");
        _roverInfo.AddChild(_roverNear = Line("Nearby", dim: true));
        _roverInfo.AddChild(_roverNote = Line("Said"));
        _roverNote.AddThemeColorOverride("font_color", new Color(1f, 0.78f, 0.45f));
        col.AddChild(_roverInfo);
        col.MoveChild(_roverInfo, 0);

        // the right panel is the rover's: the focused machine's title, energy and speed mean nothing to the player; its toasts and key help stay
        foreach (var child in col.GetChildren().OfType<Control>())
            if (child != _roverInfo && child != _hudNote && child != _hudControls && child != _operatorSection) Hide(child);
        // the left panel offers sleep, save and load, menu and speed (and pause); the machine list, restart, details, edit and join are for machine runs
        foreach (Control c in new Control[] { _machinesToggle, _machineList, _restartButton, _detailsButton }) Hide(c);
    }

    private void Hide(Control c)
    {
        if (!_roverHidden.ContainsKey(c)) _roverHidden[c] = c.Visible;
        c.Visible = false;
    }

    private void RestorePanels()
    {
        foreach (var (c, was) in _roverHidden) if (IsInstanceValid(c)) c.Visible = was;
        _roverHidden.Clear();
        if (_roverInfo is not null && IsInstanceValid(_roverInfo)) _roverInfo.QueueFree();
        _roverInfo = null;
    }

    private void UpdateRoverHud()
    {
        if (_roverInfo is null || _rover is null) return;
        var r = _rover;
        // Main re-shows these when it focuses a machine or folds the menu; keep them away while the rover is the player
        foreach (var c in _roverHidden.Keys) if (IsInstanceValid(c) && c.Visible) c.Visible = false;
        _editButton.Visible = _joinButton.Visible = false;   // (not restored: ClearWorld and LoadWorld own these two)
        _roverState!.Text = $"{(_running ? "Running" : "Paused")} · time ×{_timeScale:0.##}";
        _roverDrive!.Text = $"{r.Speed:0.0} m/s · nose {r.PitchDeg:+0;-0;0}° · tilt {r.TiltDeg:0}°";
        _roverArm!.Text = $"{r.ArmStatus}\nReaches {Rover.ArmReach:0.0} m · pushes up to {RoverSpec.PushForce(r.GroundGravity) / 1000:0.0} kN · never lifts a load. B digs and dumps.";
        _roverBucket!.Text = $"{r.Carried:0.00} of {Rover.BucketVolume:0.00} m³\ndug {r.Dug:0.00} m³, dumped {r.Dumped:0.00} m³";
        _roverEnergy!.Text = "Upkeep is free for now. The energy budget is not modelled yet (#62).";
        UpdateWinHud();   // the Bank section and the win banner (Main.Win.cs)
        var here = r.Chassis.GlobalPosition;
        var near = _views.Select(v => (View: v, Box: PartsBox(v))).Where(x => x.Box is not null)
                         .Select(x => (x.View, Distance: here.DistanceTo(x.Box!.Value.Position.Max(here.Min(x.Box.Value.End)))))
                         .Where(x => x.Distance < 25).OrderBy(x => x.Distance).Take(3).ToList();
        _roverNear!.Text = near.Count == 0 ? "No machine within 25 m."
            : string.Join("\n", near.Select(x => $"{_byName.FirstOrDefault(kv => kv.Value == x.View).Key ?? "machine"} {x.Distance:0.0} m"
                                                   + (x.Distance <= Rover.ArmReach ? " (in reach)" : "")));
        _roverNote!.Text = RoverSaid is { } said ? said : "";
        _roverNote.Visible = _roverNote.Text.Length > 0;
        // a toast from the rest of the game (Saved ..., a part's click) shows for a few seconds, then goes
        if (_hudNote.Text != _roverNoteSeen) { _roverNoteSeen = _hudNote.Text; _roverNoteAt = Clock; }
        if (_hudNote.Text.Length > 0 && Clock - _roverNoteAt > 6) { _hudNote.Text = ""; _roverNoteSeen = ""; }
    }

    /// <summary>The box round a machine's body, for how far it is from the rover (cached with the view's own bounds).</summary>
    private Aabb? PartsBox(MachineView v)
    {
        if (!_viewBounds.TryGetValue(v, out var box)) _viewBounds[v] = box = BoundsOf(v);
        return box.Size == Vector3.Zero ? null : box;
    }

    /// <summary>Scripted checks (tools/gui-check.sh): "rover" prints where it is and what it is doing; "rover place X Z HEADING" puts it on the ground there.</summary>
    private ScriptedInput.Step? RoverStep(string[] w)
    {
        if (w[0] != "rover") return null;
        if (_rover is null) { GD.Print("[view] rover: none in this world"); return ScriptedInput.Step.Continue; }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (w.Length == 5 && w[1] == "place")
        {
            double x = double.Parse(w[2], inv), z = double.Parse(w[3], inv);
            _rover.Place(x, z, double.Parse(w[4], inv), _groundSim!.Ground.HeightAt(x, z));
            _roverHeading = RoverHeading();
            return ScriptedInput.Step.Next;
        }
        if (w.Length == 3 && w[1] == "until")   // wait for the arm to reach a phase (Digging, Lifting, Swinging, Placing, Dumping, Stowed ...)
            return _rover.PhaseName == w[2] ? ScriptedInput.Step.Next : ScriptedInput.Step.Again;
        if (w.Length > 1 && RoverHandsStep(w) is { } handsStep) return handsStep;   // the rover's hands: Main.RoverHands.cs
        var p = _rover.Chassis.GlobalPosition;
        var clear = SettledClearArea();
        var box = _rover.Bounds();
        string where = ScreenRect(box) is { } s ? $"screen ({s.Position.X:F0} {s.Position.Y:F0} {s.End.X:F0} {s.End.Y:F0}) clear ({clear.Position.X:F0} {clear.Position.Y:F0} {clear.End.X:F0} {clear.End.Y:F0}) inside {(clear.Encloses(s) ? "yes" : "no")}" : "behind the camera";
        GD.Print($"[view] rover: at ({p.X:F2} {p.Y:F2} {p.Z:F2}) ground {_groundSim!.Ground.HeightAt(p.X, p.Z):F2} speed {_rover.Speed:F2} m/s pitch {_rover.PitchDeg:F1} tilt {_rover.TiltDeg:F1} heading {Mathf.RadToDeg(RoverHeading()):F0} rescues {_rover.Rescues}; " +
                 $"arm: {_rover.ArmStatus}, bucket {_rover.Carried:F2} m3, dug {_rover.Dug:F3} dumped {_rover.Dumped:F3}; {where}");
        return ScriptedInput.Step.Continue;
    }
}
