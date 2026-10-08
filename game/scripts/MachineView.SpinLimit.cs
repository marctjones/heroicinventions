using Godot;

namespace HeroicInventions;

/// <summary>
/// The silent clamp (#202): Jolt will not let a body spin faster than <c>max_angular_velocity</c> (314.159 rad/s, 3,000 rpm, since
/// #187) and does not say so. A body pinned at the limit is braked by it: an unloaded rotor behind a fast train sat at the
/// limit and the clamp took 83 N·m off the generator-train's sails in a test variant, which looked like a machine that
/// would not turn. Each tick, after the bodies have stepped, a body found at the limit is reported once in the log, and
/// counted in the trace (<c>spin-limit.hits</c>: bodies that have reached it; <c>spin-limit.ticks</c>; and <c>[body].spin-limit-ticks</c> for each that has),
/// so a test can assert it never happens.
/// </summary>
public partial class MachineView
{
    private static readonly float SpinLimit = (float)(double)ProjectSettings.GetSetting("physics/jolt_physics_3d/limits/max_angular_velocity", 314.159);
    private readonly Dictionary<RigidBody3D, int> _atSpinLimit = [];   // body -> ticks found at the limit
    private int _spinLimitTicks;

    private void CheckSpinLimit()
    {
        foreach (var body in _bodiesById.Values)
        {
            if (body.Freeze || !IsInstanceValid(body)) continue;
            // Jolt clamps the speed's length, so a body at the limit reads it to float precision
            if (body.AngularVelocity.Length() < SpinLimit * 0.999f) continue;
            _spinLimitTicks++;
            if (_atSpinLimit.TryGetValue(body, out var ticks)) { _atSpinLimit[body] = ticks + 1; continue; }
            _atSpinLimit[body] = 1;
            GD.Print($"warning: {body.Name} is spinning at Jolt's limit, {body.AngularVelocity.Length():F1} rad/s ({body.AngularVelocity.Length() * 60 / Mathf.Tau:F0} rpm) at {Runtime.Time:F3}s: the engine clamps it there, which acts as a brake; give it a load or a lower ratio");
        }
    }

    private IEnumerable<(string Key, double Value)> SpinLimitFields()
    {
        yield return ("spin-limit.hits", _atSpinLimit.Count);
        yield return ("spin-limit.ticks", _spinLimitTicks);
        foreach (var (body, ticks) in _atSpinLimit) yield return ($"{body.Name}.spin-limit-ticks", ticks);
    }
}
