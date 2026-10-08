using Godot;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// Cams (issue #48): pegs on a wheel that lift a follower and let it fall. The wheel is a
/// real Jolt body on its axle; the follower is followed analytically (Cam), so each tick the
/// wheel's total angle is handed to the cam, which works out where the follower stands and
/// how much torque the wheel must give to hold it up there (τ = F·dy/dθ, by virtual work),
/// and that torque is applied against the wheel's turning. Pegs are drawn on the wheel, at
/// the tall end of each ramp, and the follower is a hammer head on a thin rod that rises and
/// falls over its anvil, going green as it strikes.
/// </summary>
public partial class MachineView
{
    private sealed class CamView
    {
        public required Cam Cam;
        public required RigidBody3D Wheel;
        public required Vector3 Axis;
        public required MeshInstance3D Hammer, Rod, Anvil;
        public required StandardMaterial3D HammerMaterial;
        public required Vector3 Base;             // the anvil's top, in the view's frame
        public double Theta, LastRaw;             // total angle turned, from the start
        public int SeenStrikes;
        public float Flash;
    }

    private readonly List<CamView> _camViews = [];

    private void BuildCams()
    {
        foreach (var (id, cam) in Runtime.Cams)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var wheel = _bodiesById.GetValueOrDefault(part.Symbol("on", "?"))
                ?? throw new Sim.Machines.MachineFormatException($"cam {id} is pegged on {part.Symbol("on", "?")}, which is not a wheel in this machine", part.Location);
            var wheelPart = Runtime.Def.Part(part.Symbol("on", "?"))!;
            var axis = _hinges[wheel].Axis;
            double radius = wheelPart.Props.ContainsKey("radius") ? wheelPart.Number("radius") : wheelPart.Props.ContainsKey("pitch-radius") ? wheelPart.Number("pitch-radius") : 0.15;

            // pegs: at the wheel-frame angle the reading direction sits at now, minus the rise and each pitch — the tall end of each ramp
            var reference = Mathf.Abs(axis.X) < 0.9f ? Vector3.Right : Vector3.Up;
            var u = (reference - axis * axis.Dot(reference)).Normalized();
            var localU = wheel.GlobalTransform.Basis.Inverse() * u;
            double c0 = Math.Atan2(localU.Y, localU.X);
            var pegMat = Shapes.Mat(new Color(0.55f, 0.4f, 0.2f));
            for (int k = 0; k < cam.Pegs; k++)
            {
                double a = c0 - cam.RiseAngle - k * cam.Pitch;
                var peg = Shapes.Box(new Vector3((float)cam.Lift, 0.02f, 0.03f), pegMat);
                peg.Position = new Vector3((float)((radius + cam.Lift / 2) * Math.Cos(a)), (float)((radius + cam.Lift / 2) * Math.Sin(a)), 0);
                peg.Rotation = new Vector3(0, 0, (float)a);
                wheel.AddChild(peg);
            }

            var top = V(part.At);
            var mat = Shapes.Mat(new Color(0.45f, 0.45f, 0.5f), metallic: 0.5f);
            var hammer = Shapes.Box(new Vector3(0.12f, 0.08f, 0.08f), mat);
            var rod = Shapes.Box(new Vector3(0.02f, 1, 0.02f), Shapes.Mat(new Color(0.5f, 0.35f, 0.2f)));
            var anvil = Shapes.Box(new Vector3(0.2f, 0.04f, 0.14f), Shapes.Mat(new Color(0.3f, 0.3f, 0.32f)));
            anvil.Position = top + new Vector3(0, -0.02f, 0);
            AddChild(hammer); AddChild(rod); AddChild(anvil);
            AddLabel(id, top + new Vector3(0, (float)cam.Lift + 0.25f, 0));
            _camViews.Add(new CamView
            {
                Cam = cam, Wheel = wheel, Axis = axis, Hammer = hammer, Rod = rod, Anvil = anvil, HammerMaterial = mat, Base = top,
                LastRaw = RawAngle(wheel, axis),
            });
        }
    }

    /// <summary>Hands each cam its wheel's angle for one physics tick and loads the wheel with the torque the follower puts on it.</summary>
    private void DriveCams(double dt)
    {
        foreach (var v in _camViews)
        {
            double raw = RawAngle(v.Wheel, v.Axis);
            v.Theta += Unwrap(raw - v.LastRaw);
            v.LastRaw = raw;
            v.Cam.Gravity = Runtime.Outside.Gravity;
            // wheel and follower are solved together each tick (Cam.StepCoupled), so the wheel pays for the lift
            // with its own inertia and the follower's added, and cannot be set shaking by it
            double omega = v.Wheel.AngularVelocity.Dot(v.Axis);
            double impulse = v.Cam.StepCoupled(dt, v.Theta, omega, InertiaOnAxle(v.Wheel));
            if (impulse != 0) v.Wheel.ApplyTorqueImpulse(v.Axis * (float)impulse);
            if (v.Cam.Strikes != v.SeenStrikes) { v.SeenStrikes = v.Cam.Strikes; v.Flash = 1; }
        }
    }

    private void DrawCams()
    {
        foreach (var v in _camViews)
        {
            float y = (float)v.Cam.Height, headY = y + 0.04f;
            v.Hammer.Position = v.Base + new Vector3(0, headY, 0);
            float rodLength = 1.0f;
            v.Rod.Scale = new Vector3(1, rodLength, 1);
            v.Rod.Position = v.Base + new Vector3(0, headY + 0.04f + rodLength / 2, 0);
            v.Flash = Mathf.Max(0, v.Flash - 0.08f);
            v.HammerMaterial.AlbedoColor = new Color(0.45f + 0.4f * v.Flash, 0.45f + 0.45f * v.Flash, 0.5f - 0.2f * v.Flash);
        }
    }
}
