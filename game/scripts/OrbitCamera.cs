using Godot;

namespace HeroicInventions;

/// <summary>
/// The camera both views share: a point it looks at (the pivot) and where
/// it stands round it, in spherical coordinates. Run view and build mode
/// each own one, so the same gestures move it the same way in both:
///
///   mouse     drag to orbit (each view says which button) · middle-drag
///             or Shift+drag to pan · scroll to zoom
///   trackpad  two-finger scroll zooms, Shift+two-finger scroll pans,
///             pinch zooms (macOS sends trackpad scrolls as pan gestures,
///             not wheel clicks, so without these a trackpad can't zoom)
///   keys      arrows (or W A S D where the view leaves them free) pan
///             over the ground · Shift+arrows orbit · + and − or Page Up
///             and Page Down zoom · Home goes back to the view's own framing
///
/// Held keys move it a steady amount a second, scaled by how far away it
/// is, so a crane and a crater both cross the screen at a usable speed.
/// </summary>
public sealed class OrbitCamera(Camera3D camera, float minDistance, float maxDistance, float minPitch, float maxPitch)
{
    public const float OrbitPerPixel = 0.008f;      // rad
    public const float PanPerPixel = 0.0015f;       // of the distance
    private const float ZoomStep = 0.9f;            // per wheel click
    private const float KeyPanRate = 0.8f;          // distances a second
    private const float KeyOrbitRate = 1.5f;        // rad a second
    private const float KeyZoomRate = 2.5f;         // e-folds of distance a second, roughly ×12

    public Camera3D Camera { get; } = camera;
    public Vector3 Pivot { get; set; }
    public float Distance { get; set; } = 2.5f;
    public float Yaw { get; set; }
    public float Pitch { get; set; } = 0.5f;

    /// <summary>Whether W A S D also pan: true only where the view has no other use for those letters.</summary>
    public bool Wasd { get; set; }

    /// <summary>The player moved the camera (orbit, pan, zoom, keys), as opposed to the view framing it.</summary>
    public event Action? MovedByPlayer;

    /// <summary>Stands at <paramref name="eye"/> looking at <paramref name="lookAt"/>.</summary>
    public void LookFrom(Vector3 eye, Vector3 lookAt)
    {
        var offset = eye - lookAt;
        Pivot = lookAt;
        Distance = Mathf.Clamp(offset.Length(), minDistance, maxDistance);
        Yaw = Mathf.Atan2(offset.X, offset.Z);
        Pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(offset.Y / Mathf.Max(offset.Length(), 0.001f), -1, 1)), minPitch, maxPitch);
        Apply();
    }

    public void Apply()
    {
        var offset = new Vector3(
            Distance * Mathf.Cos(Pitch) * Mathf.Sin(Yaw),
            Distance * Mathf.Sin(Pitch),
            Distance * Mathf.Cos(Pitch) * Mathf.Cos(Yaw));
        Camera.Position = Pivot + offset;
        Camera.LookAt(Pivot, Vector3.Up);
    }

    /// <summary>Swings round the pivot: right moves the camera round to the left of the scene, down raises it to look from above.</summary>
    public void Orbit(Vector2 pixels)
    {
        Yaw -= pixels.X * OrbitPerPixel;
        Pitch = Mathf.Clamp(Pitch + pixels.Y * OrbitPerPixel, minPitch, maxPitch);
        Apply();
        MovedByPlayer?.Invoke();
    }

    /// <summary>Slides the pivot over the ground as if dragging the scene: the point under the mouse stays under it.</summary>
    public void Pan(Vector2 pixels)
    {
        float scale = Distance * PanPerPixel;
        var right = Camera.GlobalBasis.X;
        var back = new Vector3(Camera.GlobalBasis.Z.X, 0, Camera.GlobalBasis.Z.Z).Normalized();
        Pivot += (-right * pixels.X - back * pixels.Y) * scale;
        Apply();
        MovedByPlayer?.Invoke();
    }

    /// <summary>Moves in (factor below 1) or out.</summary>
    public void Zoom(float factor)
    {
        Distance = Mathf.Clamp(Distance * factor, minDistance, maxDistance);
        Apply();
        MovedByPlayer?.Invoke();
    }

    /// <summary>
    /// The gestures that mean the same in every view: wheel and pinch zoom,
    /// trackpad two-finger scroll. True when the event was one of them.
    /// Mouse buttons are the view's own, since what a left-drag means differs.
    /// </summary>
    public bool HandleGesture(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheel:
                Zoom(Mathf.Pow(ZoomStep, WheelClicks(wheel)));
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheel:
                Zoom(Mathf.Pow(1 / ZoomStep, WheelClicks(wheel)));
                return true;
            case InputEventMagnifyGesture pinch when pinch.Factor > 0:
                Zoom(1 / pinch.Factor);
                return true;
            case InputEventPanGesture scroll:
                if (scroll.ShiftPressed) Pan(scroll.Delta * -12);
                else Zoom(Mathf.Pow(ZoomStep, -scroll.Delta.Y));
                return true;
        }
        return false;
    }

    // A precise wheel (a smooth-scrolling mouse) reports fractions of a click.
    private static float WheelClicks(InputEventMouseButton wheel) => wheel.Factor > 0 ? wheel.Factor : 1;

    private static bool Typing(Viewport viewport) => viewport.GuiGetFocusOwner() is LineEdit or TextEdit;

    /// <summary>
    /// Call from the view's _Input, which runs before the GUI's. The arrow
    /// and page keys belong to the camera unless a text field has the
    /// keyboard: a button, list or slider that kept focus from the last
    /// click lets go, so the key moves the view instead of stepping the
    /// palette's selection or the focus from button to button.
    /// </summary>
    public static void ClaimNavigationKeys(InputEvent @event, Viewport viewport)
    {
        if (@event is InputEventKey { Pressed: true } key
            && key.Keycode is Key.Up or Key.Down or Key.Left or Key.Right or Key.Pageup or Key.Pagedown or Key.Home
            && viewport.GuiGetFocusOwner() is { } focus && !Typing(viewport))
            focus.ReleaseFocus();
    }

    /// <summary>
    /// Held keys, once a frame. Skipped while a text field has the keyboard,
    /// so typing a number into the inspector never moves the camera, and
    /// while Ctrl, Cmd or Alt is down, so shortcuts stay shortcuts.
    /// </summary>
    public void ProcessKeys(double delta, Viewport viewport)
    {
        if (Typing(viewport)) return;
        if (Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta) || Input.IsKeyPressed(Key.Alt)) return;
        float dt = (float)delta;

        var move = Vector2.Zero;
        if (Held(Key.Left) || Wasd && Held(Key.A)) move.X -= 1;
        if (Held(Key.Right) || Wasd && Held(Key.D)) move.X += 1;
        if (Held(Key.Up) || Wasd && Held(Key.W)) move.Y -= 1;
        if (Held(Key.Down) || Wasd && Held(Key.S)) move.Y += 1;

        float zoom = 0;
        if (Held(Key.Equal) || Held(Key.Plus) || Held(Key.KpAdd) || Held(Key.Pageup)) zoom -= 1;
        if (Held(Key.Minus) || Held(Key.KpSubtract) || Held(Key.Pagedown)) zoom += 1;

        if (move == Vector2.Zero && zoom == 0) return;
        if (move != Vector2.Zero)
        {
            if (Input.IsKeyPressed(Key.Shift))
            {
                // Shift+arrows orbit; up looks from higher, as dragging down does
                Yaw += move.X * KeyOrbitRate * dt;
                Pitch = Mathf.Clamp(Pitch - move.Y * KeyOrbitRate * dt, minPitch, maxPitch);
            }
            else
            {
                // arrows move the view the way they point: Up goes forward, away from the camera
                float step = Distance * KeyPanRate * dt;
                var right = Camera.GlobalBasis.X;
                var back = new Vector3(Camera.GlobalBasis.Z.X, 0, Camera.GlobalBasis.Z.Z).Normalized();
                Pivot += (right * move.X + back * move.Y) * step;
            }
        }
        if (zoom != 0) Distance = Mathf.Clamp(Distance * Mathf.Exp(zoom * KeyZoomRate * dt), minDistance, maxDistance);
        Apply();
        MovedByPlayer?.Invoke();
    }

    private static bool Held(Key key) => Input.IsKeyPressed(key);
}
