using Godot;

namespace HeroicInventions;

/// <summary>
/// A script of mouse and keyboard steps fed through Godot's own input
/// pipeline (Input.ParseInputEvent), the same path real clicks and keys
/// take, so a scripted run exercises the views' actual handling. Nothing
/// touches the operating system's pointer or keyboard, so a run in a
/// background window (tools/gui-check.sh) never takes the user's focus.
///
/// Steps are separated by ';'. One step runs every few frames; a step that
/// only reads or prints runs at once. Positions are viewport pixels.
///
///   wait N              wait N more frames
///   move X Y            move the mouse
///   down X Y · up X Y   press or release the left button (shiftdown: with Shift)
///   wheel X Y N         turn the mouse wheel N notches at X Y (negative: up), as a person scrolling a list does
///   drag X1 Y1 X2 Y2    press, move in ten steps, release (rdrag: right button, mdrag: middle)
///   type TEXT           type the text into the focused text field, one key at a time
///   key NAME            press and release a key; modifiers join with +: ctrl+z, shift+t, meta+s
///   hold NAME SECONDS   keep a key down, then print how long it really was (shift+up works)
///   press NAME · release NAME   hold a key down across the steps between
///   fps N               cap the frame rate (waits are frames, holds are seconds: a drive repeats only at one rate)
///   camera              print where the camera is
///   look YAW PITCH [DISTANCE [X Y Z]]   aim the camera: degrees round and up (pitch is camera height), metres
///                       out, and optionally the point it looks at
///   shot PATH           save what the window shows as a PNG
///   pick X Y · pickworld X Y Z   print which part is drawn at a viewport pixel, or where a world point is drawn (the run view; #151)
///   join A B            join two parts of two placed machines, LABEL.PART[.PORT] each, as the Join machines gesture does (a view that can: Main; #218)
///   joinclick A         click the part A (LABEL.PART[.PORT]) where the camera draws it, while Join machines is on: the real gesture, one end at a time
///   joinclick A N       the same, then A's spot again: a second click on a spot offers the parts under it; take item N (from 1) of that list (#225)
///   joinclick A list    click A; if another part was picked, click again and take A from the list the spot offers
///   joinrightclick A    a right-click on A's spot, which offers that list; joinlist N takes item N of the list open
///   joinbutton          print the Join machines button's label (#224)
///   quit                end the run
///
/// A view adds its own steps through the <c>extra</c> handler, which
/// returns null for a step it doesn't know.
/// </summary>
public sealed class ScriptedInput(string tag, string script, Node owner, Func<OrbitCamera?> camera, Func<string[], ScriptedInput.Step?>? extra = null, char separator = ';')
{
    /// <summary>Next: wait a few frames before the next step; Continue: run the next at once; Again: this step isn't ready, try it again in a few frames.</summary>
    public enum Step { Next, Continue, Again }

    /// <summary>An owner that can join machines' parts by name, as a click on each would (Main, Main.Links.cs).</summary>
    public interface IJoinStep { Step JoinStep(string[] w); }

    private readonly Queue<string> _steps = new(script.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    private int _wait;
    private (InputEventKey Key, double Left, double Held)? _holding;
    private Vector2 _lastMouse;
    private string? _retry;   // a step that said Again

    public bool Done => _steps.Count == 0 && _holding is null && _retry is null;

    /// <summary>Run the waiting step on the next frame (a paused sleep ends on a frame that depends on the CPU: without this the step
    /// after it would start 0 to 2 frames later from one run to the next, and so would everything after it).</summary>
    public void Nudge() => _wait = 0;

    /// <summary>Call once a frame from the owner's _Process, after anything the held keys drive.</summary>
    public void Process(double delta)
    {
        if (_holding is { } h)
        {
            // the camera has just moved for this frame's delta, so count it as held
            double held = h.Held + delta, left = h.Left - delta;
            if (left > 0) { _holding = (h.Key, left, held); return; }
            var up = (InputEventKey)h.Key.Duplicate();
            up.Pressed = false;
            Input.ParseInputEvent(up);
            if (up.ShiftPressed) Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = Key.Shift });
            _holding = null;
            GD.Print($"[{tag}] held {h.Key.AsTextKeycode()} for {held:F3} s");
        }
        if (_wait-- > 0) return;
        _wait = 3;
        while ((_retry ?? (_steps.TryDequeue(out var next) ? next : null)) is { } line)
        {
            if (_retry is null) GD.Print($"[{tag}] input: {line}");
            _retry = null;
            var w = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var step = extra?.Invoke(w) ?? Run(w);
            if (step == Step.Again) { _retry = line; return; }   // not ready yet: the same step again in a few frames
            if (step == Step.Next) return;
        }
    }

    private float N(string[] w, int i) => float.Parse(w[i], System.Globalization.CultureInfo.InvariantCulture);

    private Step Run(string[] w)
    {
        switch (w[0])
        {
            case "wait": _wait = (int)N(w, 1); return Step.Next;
            case "move": Mouse(new Vector2(N(w, 1), N(w, 2))); return Step.Next;
            case "down": Mouse(new Vector2(N(w, 1), N(w, 2)), MouseButton.Left, true); return Step.Next;
            case "up": Mouse(new Vector2(N(w, 1), N(w, 2)), MouseButton.Left, false); return Step.Next;
            case "wheel":
            {
                var at = new Vector2(N(w, 1), N(w, 2));
                int notches = (int)N(w, 3);
                Mouse(at);
                for (int k = 0; k < Math.Abs(notches); k++)
                    foreach (bool pressed in new[] { true, false })
                        Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = notches > 0 ? MouseButton.WheelDown : MouseButton.WheelUp, Pressed = pressed, Factor = 1 });
                return Step.Next;
            }
            case "shiftdown": Mouse(new Vector2(N(w, 1), N(w, 2)), MouseButton.Left, true, shift: true); return Step.Next;
            case "drag" or "rdrag" or "mdrag":
            {
                var button = w[0] == "drag" ? MouseButton.Left : w[0] == "rdrag" ? MouseButton.Right : MouseButton.Middle;
                var a = new Vector2(N(w, 1), N(w, 2)); var b = new Vector2(N(w, 3), N(w, 4));
                Mouse(a, button, true);
                for (int k = 1; k <= 10; k++) Mouse(a.Lerp(b, k / 10f), button);
                Mouse(b, button, false);
                return Step.Next;
            }
            case "type":   // text into whatever has the keyboard (a text field): "type scene.elapsed"; spaces are kept, so it takes the rest of the line
            {
                foreach (char ch in string.Join(' ', w[1..]))
                {
                    var key = new InputEventKey { Pressed = true, Unicode = ch, Keycode = ch is >= 'a' and <= 'z' ? (Key)(ch - 32) : (Key)ch };
                    Input.ParseInputEvent(key);
                    var released = (InputEventKey)key.Duplicate();
                    released.Pressed = false;
                    Input.ParseInputEvent(released);
                }
                return Step.Next;
            }
            case "key":
            {
                var ev = KeyEvent(w[1]);
                Input.ParseInputEvent(ev);
                var up = (InputEventKey)ev.Duplicate();
                up.Pressed = false;
                Input.ParseInputEvent(up);
                return Step.Next;
            }
            case "hold":
            {
                var ev = KeyEvent(w[1]);
                if (ev.ShiftPressed) Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = Key.Shift });
                Input.ParseInputEvent(ev);
                _holding = (ev, N(w, 2), 0);
                return Step.Next;
            }
            case "press" or "release":   // a key held down across other steps: "press r; drag ...; release r"
            {
                var ev = KeyEvent(w[1]);
                ev.Pressed = w[0] == "press";
                Input.ParseInputEvent(ev);
                return Step.Next;
            }
            case "look":   // a fixed frame for screenshots: no dragging blind
                if (camera() is { } l)
                {
                    l.Yaw = Mathf.DegToRad(N(w, 1));
                    l.Pitch = Mathf.DegToRad(N(w, 2));
                    if (w.Length > 3) l.Distance = N(w, 3);
                    if (w.Length > 6) l.Pivot = new Vector3(N(w, 4), N(w, 5), N(w, 6));
                    l.Apply();
                }
                return Step.Next;
            case "fps":   // cap the frame rate at N: `wait` counts frames and `hold` seconds, so a route's drive is only repeatable at one rate (tools/route-gui.sh)
                Engine.MaxFps = (int)N(w, 1);
                return Step.Continue;
            case "camera":
                if (camera() is { } c)
                    GD.Print($"[{tag}] camera: pivot=({F(c.Pivot.X)} {F(c.Pivot.Y)} {F(c.Pivot.Z)}) distance={F(c.Distance)} " +
                             $"yaw={F(c.Yaw)} pitch={F(c.Pitch)} right=({F(c.Camera.GlobalBasis.X.X)} {F(c.Camera.GlobalBasis.X.Z)})");
                return Step.Continue;
            case "shot":
            {
                var image = owner.GetViewport().GetTexture().GetImage();
                string path = w[1].StartsWith("res://") || w[1].StartsWith("user://") ? ProjectSettings.GlobalizePath(w[1]) : w[1];
                var error = image.SavePng(path);
                GD.Print($"[{tag}] shot {path} {image.GetWidth()}x{image.GetHeight()} {error}");
                return Step.Next;
            }
            case "join" or "joinbutton" or "joinclick" or "joinrightclick" or "joinlist":
                if (owner is IJoinStep joiner) return joiner.JoinStep(w);
                GD.PrintErr($"[{tag}] join: this view has no machines to join");
                owner.GetTree().Quit(1);
                return Step.Next;
            case "quit":
                owner.GetTree().Quit();
                return Step.Next;
            default:
                GD.Print($"[{tag}] unknown step: {string.Join(' ', w)}");
                return Step.Continue;
        }
    }

    private static InputEventKey KeyEvent(string spec)
    {
        var ev = new InputEventKey { Pressed = true };
        foreach (var part in spec.Split('+'))
            switch (part)
            {
                case "ctrl": ev.CtrlPressed = true; break;
                case "shift": ev.ShiftPressed = true; break;
                case "meta" or "cmd": ev.MetaPressed = true; break;
                case "alt": ev.AltPressed = true; break;
                default: ev.Keycode = OS.FindKeycodeFromString(part); break;
            }
        return ev;
    }

    /// <summary>Moves the mouse to <paramref name="at"/>, then presses or releases <paramref name="button"/> there if one is given.</summary>
    public void Mouse(Vector2 at, MouseButton button = MouseButton.None, bool? pressed = null, bool shift = false)
    {
        if (at != _lastMouse)
        {
            var mask = pressed is null && button != MouseButton.None ? (MouseButtonMask)(1 << ((int)button - 1)) : 0;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = at - _lastMouse, ShiftPressed = shift, ButtonMask = mask });
            _lastMouse = at;
        }
        if (button != MouseButton.None && pressed is { } p)
            Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = p, ShiftPressed = shift });
    }

    private static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
