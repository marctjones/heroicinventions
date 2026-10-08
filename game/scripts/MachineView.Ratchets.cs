using Godot;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// Ratchets (issue #49): a toothed wheel that turns one way, a tooth at a time, and a pawl. Each tick the
/// wheel's total angle is handed to the ratchet, which lets it go forward, drops the pawl into the next valley
/// as a whole tooth passes, and stops it turning back past the valley it is in with an impulse (the pawl
/// carrying the load). It is drawn as N small teeth on the wheel, at the tooth circle, and a pawl at the
/// wheel's reading direction that flashes when it clicks into a valley and turns red while it is holding.
/// </summary>
public partial class MachineView
{
    private sealed class RatchetView
    {
        public required Ratchet Ratchet;
        public required RigidBody3D Wheel;
        public required Vector3 Axis;
        public required MeshInstance3D Pawl;
        public required StandardMaterial3D PawlMaterial;
        public double Theta, LastRaw;
        public int SeenSteps;
        public float Flash;
    }

    private readonly List<RatchetView> _ratchetViews = [];

    private void BuildRatchets()
    {
        foreach (var (id, ratchet) in Runtime.Ratchets)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var wheel = _bodiesById.GetValueOrDefault(part.Symbol("on", "?"))
                ?? throw new Sim.Machines.MachineFormatException($"ratchet {id} is cut on {part.Symbol("on", "?")}, which is not a wheel in this machine", part.Location);
            var axis = _hinges[wheel].Axis;
            var reference = Mathf.Abs(axis.X) < 0.9f ? Vector3.Right : Vector3.Up;
            var u = (reference - axis * axis.Dot(reference)).Normalized();
            var v = axis.Cross(u);
            var localU = wheel.GlobalTransform.Basis.Inverse() * u;
            double c0 = Math.Atan2(localU.Y, localU.X);
            double R = ratchet.ToothRadius;

            // teeth: little wedges (boxes turned to slope) on the wheel, one a pitch apart, their steep faces towards the blocked way
            var toothMat = Shapes.Mat(new Color(0.42f, 0.42f, 0.46f), metallic: 0.5f);
            for (int k = 0; k < ratchet.Teeth; k++)
            {
                double a = c0 + k * ratchet.Pitch;
                var tooth = Shapes.Box(new Vector3(0.025f, 0.02f, 0.03f), toothMat);
                tooth.Position = new Vector3((float)(R * Math.Cos(a)), (float)(R * Math.Sin(a)), 0);
                tooth.Rotation = new Vector3(0, 0, (float)(a + (ratchet.Reverse ? -0.5 : 0.5)));
                wheel.AddChild(tooth);
            }
            // the pawl: at the reading direction, just outside the teeth, in the view's frame
            var wheelPos = V(part.At);
            var pawlMat = Shapes.Mat(new Color(0.6f, 0.6f, 0.65f), metallic: 0.5f);
            var pawl = Shapes.Box(new Vector3(0.05f, 0.015f, 0.035f), pawlMat);
            pawl.Position = wheelPos + u * (float)(R + 0.02);
            pawl.Rotation = new Vector3(0, 0, (float)Math.Atan2(u.Y, u.X) + (ratchet.Reverse ? 0.7f : -0.7f));
            AddChild(pawl);
            AddLabel(id, wheelPos + u * (float)(R + 0.1));
            _ratchetViews.Add(new RatchetView { Ratchet = ratchet, Wheel = wheel, Axis = axis, Pawl = pawl, PawlMaterial = pawlMat, LastRaw = RawAngle(wheel, axis) });
        }
    }

    /// <summary>Works each pawl for one physics tick and gives the wheel the impulse that stops it going back.</summary>
    private void DriveRatchets(double dt)
    {
        foreach (var v in _ratchetViews)
        {
            double raw = RawAngle(v.Wheel, v.Axis);
            v.Theta += Unwrap(raw - v.LastRaw);
            v.LastRaw = raw;
            double omega = v.Wheel.AngularVelocity.Dot(v.Axis);
            double impulse = v.Ratchet.Step(dt, v.Theta, omega, TurningInertia(v.Wheel));
            if (impulse != 0) v.Wheel.ApplyTorqueImpulse(v.Axis * (float)impulse);
            if (v.Ratchet.Steps != v.SeenSteps) { v.SeenSteps = v.Ratchet.Steps; v.Flash = 1; }
        }
    }

    /// <summary>
    /// What stopping the wheel has to stop: the wheel and whatever turns with it, and any load hanging by a rope wound on it, which
    /// the rope makes turn the drum's way (m·r² of it). Reckoning the drum alone, a pawl under-corrects five times over.
    /// </summary>
    private double TurningInertia(RigidBody3D wheel) =>
        InertiaOnAxle(wheel) + _ropes.Where(r => r.Active && r.Drum == wheel && r.B is not null).Sum(r => r.B!.Mass * r.DrumRadius * r.DrumRadius);

    private void DrawRatchets()
    {
        foreach (var v in _ratchetViews)
        {
            v.Flash = Mathf.Max(0, v.Flash - 0.1f);
            v.PawlMaterial.AlbedoColor = v.Ratchet.Holding ? new Color(0.9f, 0.3f, 0.2f) : new Color(0.6f + 0.35f * v.Flash, 0.6f + 0.3f * v.Flash, 0.65f - 0.35f * v.Flash);
        }
    }
}
