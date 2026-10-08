using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// The engine side of water lifting. The fluid side (WaterLift, in the
/// sim library) moves water at the rate a machine's turning allows; here
/// the machine's real turning speed is fed to it each tick, and the torque
/// of lifting that water pushes back on the machine — so a screw trodden
/// too slowly, or a noria in too slow a river, lifts less or stalls.
///
/// A noria stands in a river: water flowing past the paddles — at a fixed
/// #:current, or as fast as the channel named by #:current-from runs —
/// dipping into it drags them round. Each submerged paddle feels
/// ½·ρ·Cd·A·(v − u)·|v − u|, where u is the paddle's own speed (so the
/// push fades as the wheel speeds up toward the current), acting at the
/// paddle's middle. Cd ≈ 2 for a flat plate square to the flow.
/// </summary>
public partial class MachineView
{
    private readonly List<LiftDrive> _liftDrives = [];

    private sealed class LiftDrive
    {
        public required WaterLift Lift;
        public required LiftSpec Spec;
        public required PartSpec By;
        public required RigidBody3D Body;
        public required Vector3 Axis;
        public required MeshInstance3D Stream;
        public required Vector3 Spout;       // where the water leaves the machine
    }

    private void BuildLifts()
    {
        foreach (var spec in Runtime.Def.Lifts.Where(l => Runtime.Def.Part(l.By)!.Kind != "piston")) // pumps: see BuildPistonDrives
        {
            _building = spec.Id;
            var by = Runtime.Def.Part(spec.By)!;
            var body = _bodiesById[spec.By];
            var axis = _hinges[body].Axis;
            var lift = Runtime.Lifts[spec.Id];
            // A screw pours from its upper end; a noria's buckets tip out near the top, over the trough.
            var spout = by.Kind == "screw"
                ? V(by.At) + axis * (float)(by.Number("length") / 2)
                : new Vector3((float)Runtime.Def.Part(spec.To)!.At.X, (float)lift.DischargeElevation, (float)by.At.Z);
            var stream = Shapes.Cylinder(1, 1, Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.7f));
            stream.Visible = false;
            AddChild(stream);
            _liftDrives.Add(new LiftDrive { Lift = lift, Spec = spec, By = by, Body = body, Axis = axis, Stream = stream, Spout = spout });
        }
    }

    private void DriveLifts()
    {
        foreach (var d in _liftDrives)
        {
            float omega = d.Body.AngularVelocity.Dot(d.Axis);
            d.Lift.Rpm = Math.Max(0, omega * 60 / Math.Tau);
            if (omega > 0) d.Body.ApplyTorque(-d.Axis * (float)d.Lift.LoadTorque);
            // a fixed current, or the speed the channel feeding the wheel's pool actually runs at
            double? current = d.Spec.CurrentFrom is { } race ? Runtime.Channels[race].Velocity : d.Spec.Current;
            if (current is { } v) d.Body.ApplyTorque(d.Axis * CurrentTorque(d, (float)v, omega));
        }
    }

    private float CurrentTorque(LiftDrive d, float current, float omega)
    {
        float radius = (float)d.By.Number("radius"), paddle = (float)d.By.Number("paddle-depth");
        float tip = radius + paddle, middle = radius + paddle / 2;
        float area = paddle * (float)d.By.Number("width");
        // paddles whose tips are under the river's surface: those within
        // ±α of the bottom, where cos α = (height of the axle above the water) / tip radius
        float above = (float)(d.By.At.Y - d.Lift.From.SurfaceElevation);
        if (above >= tip) return 0;
        float reach = Mathf.Acos(Mathf.Clamp(above / tip, -1, 1));
        float submerged = (float)d.By.Number("buckets") * 2 * reach / Mathf.Tau;
        float slip = current - omega * middle;
        const float cd = 2f;
        return submerged * 0.5f * (float)Sim.Physics.WaterDensity * cd * area * slip * Mathf.Abs(slip) * middle;
    }

    private void DrawLiftStreams()
    {
        foreach (var d in _liftDrives)
        {
            if (_norias.TryGetValue(d, out var noria)) { DrawNoria(d, noria); continue; }   // once per bucket, from its lip (Flow)
            bool pouring = d.Lift.Flow > 1e-5;
            d.Stream.Visible = pouring;
            if (!pouring) continue;
            float bottom = (float)d.Lift.To.SurfaceElevation;
            float height = Mathf.Max(d.Spout.Y - bottom, 0.02f);
            float thickness = Mathf.Clamp(Mathf.Sqrt((float)d.Lift.Flow) * 0.6f, 0.01f, 0.12f); // wider for more water
            d.Stream.Scale = new Vector3(thickness, height, thickness);
            d.Stream.Position = new Vector3(d.Spout.X, bottom + height / 2, d.Spout.Z);
        }
    }
}
