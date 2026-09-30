using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Links between machines in a world (issue #78): pipes and shafts joining
/// parts of different machines, stepped with them, drawn between them, and
/// made by clicking one machine's part and then another's ("Join machines",
/// or J). Every time a machine is rebuilt by a live edit the links are
/// resolved again against the new machine, which has already taken over the
/// old one's water and speed, so the flow and the turning carry on.
/// </summary>
public partial class Main
{
    private WorldLinks? _links;
    private WorldLinksView? _linksView;
    private Button _joinButton = null!;
    private bool _joining;
    private LinkEnd? _firstPick;
    private readonly Queue<string> _scriptedJoins = new(
        OS.GetEnvironment("HEROIC_WORLD_JOIN").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private void BuildJoinButton(VBoxContainer col)
    {
        _joinButton = BigButton("Join machines (J)");
        _joinButton.Visible = false;
        _joinButton.Pressed += () => SetJoining(!_joining);
        col.AddChild(_joinButton);
    }

    /// <summary>Resolves the world's links against the machines as they now stand, and draws them.</summary>
    private void RebuildLinks()
    {
        if (_world is null) { ClearLinks(); return; }
        _links = WorldLinks.Build(_world.Links,
            label => _byName.GetValueOrDefault(label)?.Runtime,
            end => _byName.GetValueOrDefault(end.Label)?.ShaftEnd(end.Part));
        if (_linksView is null) AddChild(_linksView = new WorldLinksView { Name = "Links" });
        _linksView.Show(_links, end => _byName.GetValueOrDefault(end.Label)?.LinkPoint(end.Part, end.Port));
        StartLinksTraceIfLinked();
        foreach (var l in _links.All.Where(l => l.Unfinished is not null)) GD.Print($"[links] {l.Spec.Id} unfinished: {l.Unfinished}");
    }

    private void ClearLinks()
    {
        _links = null;
        _linksView?.StopTrace();
        _linksView?.QueueFree();
        _linksView = null;
        SetJoining(false);
    }

    /// <summary>One tick of a world: water through the cross-machine pipes, every machine, then the shafts, then the records.</summary>
    private void StepWorld(double delta)
    {
        _links?.StepPipes(delta);
        foreach (var v in _views) v.SimulateUntraced(delta);
        _links?.StepShafts(delta);
        _groundSim?.Step(delta);          // after the machines: what they poured is on the ground's ledger
        _terrainView?.Refresh(delta);
        foreach (var v in _views) v.TraceStep(delta);
        _linksView?.Refresh(delta);
        if (_scriptedJoins.Count > 0) RunScriptedJoin();
    }

    private (string Path, double Every)? _linksTrace;

    /// <summary>Traces the links to &lt;path&gt;.links, from now or from when the first link is made.</summary>
    private void StartLinksTrace(string path, double every)
    {
        _linksTrace = ($"{path}.links", every);
        StartLinksTraceIfLinked();
    }

    private void StartLinksTraceIfLinked()
    {
        if (_linksView is not null) _linksView.ExtraFields = GroundFields;
        if (_linksTrace is { } t && _linksView is not null && !_linksView.Tracing && (_world is { Links.Count: > 0 } || _groundSim is not null))
            _linksView.StartTrace(t.Path, t.Every, _current?.Runtime.Time ?? 0);
    }

    private void SetJoining(bool on)
    {
        _joining = on && _world is not null;
        _firstPick = null;
        if (_joinButton is not null) _joinButton.Text = _joining ? "Stop joining (Esc)" : "Join machines (J)";
        if (_joining) GD.Print($"[links] {WorldLinkGestures.Prompt(null)}");
    }

    /// <summary>
    /// A click while joining: the part under the cursor (and, on a tank, the
    /// port nearest the cursor) is picked; the second pick on another machine
    /// makes the link, or is refused with the reason.
    /// </summary>
    private void JoinPickAt(Vector2 screen)
    {
        var from = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        (MachineView View, string Part, float T)? best = null;
        foreach (var v in _views)
            foreach (var (id, nodes) in v.PartNodes)
            {
                Aabb? box = null;
                foreach (var n in nodes)
                    if (BoundsOf(n) is { Size: var s } b && s != Vector3.Zero) box = box is { } x ? x.Merge(b) : b;
                if (box is { } bb && RayHits(from, dir, bb.Grow(0.05f), out float t) && (best is null || t < best.Value.T))
                    best = (v, id, t);
            }
        if (best is not { } hit) { GD.Print($"[links] nothing there to join (at {screen}, looking along {dir} from {from})"); return; }
        var part = hit.View.Runtime.Def.Part(hit.Part)!;
        // on a tank, the port closest to where the ray passes
        string? port = part.Ports.Where(p => p.Kind == "water")
            .Select(p => (p.Name, D: DistanceToRay(hit.View.LinkPoint(hit.Part, p.Name)!.Value, from, dir)))
            .OrderBy(p => p.D).Select(p => p.Name).FirstOrDefault();
        Pick(new LinkEnd(hit.View.Name, hit.Part, part.Kind == "tank" ? port : null));
    }

    private static float DistanceToRay(Vector3 p, Vector3 origin, Vector3 dir)
    {
        var d = p - origin;
        return (d - dir * d.Dot(dir)).Length();
    }

    private void Pick(LinkEnd end)
    {
        if (_world is null) return;
        if (_firstPick is not { } first)
        {
            _firstPick = end;
            GD.Print($"[links] {WorldLinkGestures.Prompt(end)}");
            return;
        }
        _firstPick = null;
        try
        {
            var link = WorldLinkGestures.Link(_world, label => _byName.GetValueOrDefault(label)?.Runtime.Def, first, end);
            _world = _world.WithLink(link);
            RebuildLinks();
            GD.Print($"[links] joined: {link.Kind} {link.Id} from {link.From} to {link.To}");
        }
        catch (Exception e) when (e is InvalidOperationException or MachineFormatException)
        {
            GD.Print($"[links] can't join: {e.Message}");
        }
    }

    /// <summary>
    /// HEROIC_WORLD_JOIN="tank-a.cistern.outlet tank-b.trough.inlet; ...": each
    /// pair clicked in the running world through the real input pipeline, at
    /// the ends' places on screen (needs a real window; headless has no
    /// screen to project onto). "wait S" holds until the world has run S s.
    /// </summary>
    private void RunScriptedJoin()
    {
        if (_current is null) return;
        var step = _scriptedJoins.Peek().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (step is ["wait", var s])
        {
            if (_current.Runtime.Time < double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)) return;
            _scriptedJoins.Dequeue();
            return;
        }
        _scriptedJoins.Dequeue();
        SetJoining(true);
        foreach (var end in step)
        {
            var bits = end.Split('.');
            if (bits.Length < 2 || !_byName.TryGetValue(bits[0], out var view) || view.LinkPoint(bits[1], bits.Length > 2 ? bits[2] : null) is not { } at)
            {
                GD.PrintErr($"HEROIC_WORLD_JOIN: no {end} to click");
                continue;
            }
            var screen = GetViewport().GetScreenTransform() * _camera.UnprojectPosition(at);   // window coordinates, as a real click arrives
            foreach (bool down in new[] { true, false })
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = screen, GlobalPosition = screen });
        }
    }
}
