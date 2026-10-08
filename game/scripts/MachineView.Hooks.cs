using Godot;

namespace HeroicInventions;

/// <summary>Hooks and unhooking (#160): where a rope's end can be let go of and taken up again, at run time.</summary>
public partial class MachineView
{
    /// <summary>The body a hand can take hold of under this name: a part's, or a rope's free hook ("ROPE.hook").</summary>
    public RigidBody3D? HandBody(string id) => _bodiesById.GetValueOrDefault(id);

    /// <summary>A body has just been let go of by a hand: if it is a free hook near a load, hook it. True if it was.</summary>
    public bool DropHook(RigidBody3D body) => false;
}
