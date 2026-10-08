using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The player is the rover (owner decision 2026-10-08, issue #94). In a world that places a rover (<c>(rover (at X Z))</c>,
/// the Lonely Rover's opening) the player drives it with a follow camera, and the free operator (clicking any control, dragging
/// any body, aiming a digger) is off: in the game every action will pass the rover's capability check (#163), and until that
/// exists a click does nothing. A machine run has no rover and keeps the free operator unchanged. HEROIC_ROVER=0 keeps the rover out of
/// a world that has one, for tests that need the old behaviour.
///
/// Keys: arrows or W A S D drive (forward, back, turn); B runs the backhoe's dig-and-dump; Shift+arrows orbit the camera, + and -
/// (or Page Up / Down) zoom it, Home puts it back behind the rover; the mouse orbits and zooms as everywhere.
/// </summary>
public partial class Main
{
    private Rover? _rover;
    private Label? _roverHud;
    private PanelContainer? _roverPanel;
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
        var layer = new CanvasLayer { Layer = 2 };
        AddChild(layer);
        HudTheme.Install(layer);
        _roverPanel = new PanelContainer { Name = "RoverPanel", MouseFilter = Control.MouseFilterEnum.Ignore };
        _roverHud = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _roverHud.AddThemeFontSizeOverride("font_size", 16);
        _roverPanel.AddChild(_roverHud);
        layer.AddChild(_roverPanel);

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
        if (_roverPanel?.GetParent() is { } layer) layer.QueueFree();
        _roverPanel = null; _roverHud = null;
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

    private void UpdateRoverHud()
    {
        if (_roverHud is null || _roverPanel is null || _rover is null) return;
        var r = _rover;
        double kmh = r.Speed;
        _roverHud.Text = $"Rover   {kmh:0.0} m/s   nose {r.PitchDeg:+0;-0;0}°   tilt {r.TiltDeg:0}°\n"
                       + $"{r.ArmStatus}   ·   bucket {r.Carried:0.00} of {Rover.BucketVolume:0.00} m³   ·   dug {r.Dug:0.00} m³, dumped {r.Dumped:0.00} m³   ·   B digs and dumps";
        var vp = GetViewport().GetVisibleRect().Size;
        var clear = SettledClearArea();
        _roverPanel.ResetSize();
        _roverPanel.Position = new Vector2(clear.GetCenter().X - _roverPanel.Size.X / 2, vp.Y - _roverPanel.Size.Y - 18);
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
        var p = _rover.Chassis.GlobalPosition;
        var clear = SettledClearArea();
        var box = _rover.Bounds();
        string where = ScreenRect(box) is { } s ? $"screen ({s.Position.X:F0} {s.Position.Y:F0} {s.End.X:F0} {s.End.Y:F0}) clear ({clear.Position.X:F0} {clear.Position.Y:F0} {clear.End.X:F0} {clear.End.Y:F0}) inside {(clear.Encloses(s) ? "yes" : "no")}" : "behind the camera";
        GD.Print($"[view] rover: at ({p.X:F2} {p.Y:F2} {p.Z:F2}) ground {_groundSim!.Ground.HeightAt(p.X, p.Z):F2} speed {_rover.Speed:F2} m/s pitch {_rover.PitchDeg:F1} tilt {_rover.TiltDeg:F1} heading {Mathf.RadToDeg(RoverHeading()):F0} rescues {_rover.Rescues}; " +
                 $"arm: {_rover.ArmStatus}, bucket {_rover.Carried:F2} m3, dug {_rover.Dug:F3} dumped {_rover.Dumped:F3}; {where}");
        return ScriptedInput.Step.Continue;
    }
}
