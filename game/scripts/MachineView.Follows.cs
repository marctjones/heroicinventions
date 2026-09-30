using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Fields that follow a mechanism (issue #46): every tick, the angle a lever
/// has turned (degrees from where it started) or the tension of a rope (N) is
/// handed to the runtime, which maps it onto the field the follow sets — a
/// leak's plug lift, a sluice's opening — so a valve opens as far as its lever
/// has tipped, not just open or shut.
/// </summary>
public partial class MachineView
{
    private bool _startPosesTaken;

    /// <summary>The pose each hinged body starts in, for measuring how far it has turned; taken once, before the first step.</summary>
    private void TakeStartPoses()
    {
        if (_startPosesTaken) return;
        _startPosesTaken = true;
        foreach (var b in TracedBodies()) _startBasis.TryAdd(b, b.GlobalTransform.Basis);
    }

    private void DriveFollows()
    {
        if (Runtime.Follows.Count == 0) return;
        TakeStartPoses();
        foreach (var (id, f) in Runtime.Follows)
        {
            double? input = f.Spec.Lever is { } lever
                ? (BodyNamed(lever) is { } body && IsInstanceValid(body) ? HingeAngleDegrees(body) : null)
                : _ropes.FirstOrDefault(r => r.Spec.Id == f.Spec.Rope)?.Tension;
            if (input is { } x) Runtime.ApplyFollow(id, x);
        }
    }
}
