using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// What build mode's "Test it" reads off a running copy of the design each
/// tick (#182): the loose bodies' height and speed, how far each wheel on
/// an axle turned, each tank's water, each rope's pull. The numbers go to
/// <see cref="TestRecorder"/>, which keeps the facts and decides when the
/// run has seen enough.
/// </summary>
public partial class MachineView
{
    private readonly Dictionary<RigidBody3D, Basis> _testBasis = [];

    /// <summary>The part kinds whose turning a test reports; a lever's or pendulum's swing is a tilt, not turns.</summary>
    private static readonly HashSet<string> TurningKinds = ["wheel", "waterwheel", "windmill", "jetwheel", "smokejack", "rotor"];

    /// <summary>Heights of the named parts' bodies now, and each tank's water (m³): the run's starting state.</summary>
    public (Dictionary<string, double> Heights, Dictionary<string, double> Tanks) TestStart(IEnumerable<string> partIds)
    {
        var heights = new Dictionary<string, double>();
        foreach (var id in partIds)
            if (BodyNamed(id) is { } b) { heights[id] = b.GlobalPosition.Y; _testBasis[b] = b.GlobalBasis; }
        return (heights, Runtime.Tanks.ToDictionary(kv => kv.Key, kv => kv.Value.WaterVolume));
    }

    /// <summary>One tick's readings; the turning of each wheel is taken since the last call (or <see cref="TestStart"/>).</summary>
    public TestTick TestTick(double dt, IReadOnlyDictionary<string, string> kindOf)
    {
        var bodies = new Dictionary<string, BodySample>();
        foreach (var (id, kind) in kindOf)
        {
            double turned = 0;
            if (_bodiesById.GetValueOrDefault(id) is { } b)
            {
                if (TurningKinds.Contains(kind) && _hinges.TryGetValue(b, out var hinge) && _testBasis.TryGetValue(b, out var before))
                {
                    var delta = (b.GlobalBasis * before.Inverse()).GetRotationQuaternion();
                    float angle = 2 * Mathf.Acos(Mathf.Clamp(delta.W, -1f, 1f));
                    if (angle > Mathf.Pi) angle -= Mathf.Tau;
                    var axis = new Vector3(delta.X, delta.Y, delta.Z);
                    turned = axis.LengthSquared() > 1e-12f ? angle * axis.Normalized().Dot(hinge.Axis.Normalized()) : 0;
                }
                _testBasis[b] = b.GlobalBasis;
                bodies[id] = new BodySample(b.GlobalPosition.Y, b.LinearVelocity.Length(), b.AngularVelocity.Length(), turned, b.GlobalPosition.X, b.GlobalPosition.Z);
            }
            else if (TurningKinds.Contains(kind) && IsSimTurned(id))
            {
                double omega = SimShaft(id).AngularVelocity;   // the sim turns it: speed times the tick
                bodies[id] = new BodySample(0, 0, Math.Abs(omega), omega * dt);
            }
        }
        var ropes = _ropes.ToDictionary(r => r.Spec.Id, r => new RopeSample(r.Tension, r.Strength, r.Stretch, r.Broken, r.Released));
        return new TestTick(dt, bodies, Runtime.Tanks.ToDictionary(kv => kv.Key, kv => kv.Value.WaterVolume), ropes);
    }
}
