using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Triggers that watch a body (issue #32). Each is drawn as a faint amber box
/// where it stands, so a designer sees where the sensor is; it turns green
/// when it fires and stays green. Every physics tick the watched body's
/// centre is handed to the runtime, which fires the trigger (once) if it is
/// inside the box and applies its actions. Triggers that watch a field have no
/// place in the scene and are stepped by the runtime itself.
/// </summary>
public partial class MachineView
{
    private static readonly Color TriggerWaiting = new(1f, 0.75f, 0.2f), TriggerFired = new(0.3f, 0.9f, 0.4f);
    private readonly List<(Trigger Trigger, MeshInstance3D Box)> _triggerViews = [];

    private void BuildTriggers()
    {
        foreach (var t in Runtime.Triggers.Values)
        {
            if (t.Spec.Body is null || t.Spec.At is not { } at || t.Spec.Size is not { } size) continue;
            var box = Shapes.Box(new Vector3((float)size.X, (float)size.Y, (float)size.Z), Shapes.Mat(TriggerWaiting, alpha: 0.22f));
            ((StandardMaterial3D)box.MaterialOverride).Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            box.Position = V(at);
            AddChild(box);
            AddLabel(t.Spec.Id, V(at) + new Vector3(0, (float)size.Y / 2 + 0.06f, 0));
            _triggerViews.Add((t, box));
        }
    }

    /// <summary>Hands each unfired body trigger the position of the body it watches.</summary>
    private void TestTriggers()
    {
        foreach (var (t, _) in _triggerViews)
        {
            if (t.Fired || !_bodiesById.TryGetValue(t.Spec.Body!, out var body) || !IsInstanceValid(body)) continue;
            var p = body.GlobalPosition;
            Runtime.TestBodyTrigger(t.Spec.Id, p.X, p.Y, p.Z);
        }
    }

    private void DrawTriggers()
    {
        foreach (var (t, box) in _triggerViews)
        {
            var mat = (StandardMaterial3D)box.MaterialOverride;
            var color = t.Fired ? TriggerFired : TriggerWaiting;
            mat.AlbedoColor = new Color(color.R, color.G, color.B, t.Fired ? 0.4f : 0.22f);
        }
    }
}
