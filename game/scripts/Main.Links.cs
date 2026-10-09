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
    /// <summary>The Jolt locks that make shafts between two machines' hinged bodies turn as one piece (#191).</summary>
    private readonly List<Action> _shaftLocks = [];
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
        UnlockShafts();   // a shaft end is its own machine's arbor: undo the locks' mates before resolving the ends again
        _links?.Release();   // and take the wires off their generators: a wire is the generator's state, run again below
        _links = WorldLinks.Build(_world.Links,
            label => _byName.GetValueOrDefault(label)?.Runtime,
            end => _byName.GetValueOrDefault(end.Label)?.ShaftEnd(end.Part));
        if (_linksView is null) AddChild(_linksView = new WorldLinksView { Name = "Links" });
        _linksView.Show(_links, end => _byName.GetValueOrDefault(end.Label)?.LinkPoint(end.Part, end.Port));
        LockShafts();
        StartLinksTraceIfLinked();
        foreach (var l in _links.All.Where(l => l.Unfinished is not null)) GD.Print($"[links] {l.Spec.Id} unfinished: {l.Unfinished}");
    }

    /// <summary>
    /// A shaft between two machines' Jolt bodies on one axle line is also locked inside Jolt's step (#191):
    /// the once-a-tick exchange alone let a motor on one end hold only that end at its speed, and the other
    /// ran a tick of the shaft's torque behind (the split crane's rope 2.25% slow). The exchange still runs,
    /// before the step, and still reads the torque the shaft carries; the lock carries what acts within the
    /// step. A shaft a lock can't make (a sim part at an end, a ratio other than one, axles off one line) is
    /// the exchange alone, as before; which it is goes to the log.
    /// </summary>
    private void LockShafts()
    {
        UnlockShafts();
        if (_links is null) return;
        foreach (var link in _links.All)
        {
            if (link.Shaft is null || link.Unfinished is not null) continue;
            var (from, to) = (link.Spec.From, link.Spec.To);
            if (_byName.GetValueOrDefault(from.Label) is not { } a || _byName.GetValueOrDefault(to.Label) is not { } b) continue;
            var (unlock, why) = a.LockAxleTo(from.Part, b, to.Part, link.Spec.Ratio, this);
            if (unlock is not null) _shaftLocks.Add(unlock);
            link.Shaft.Locked = unlock is not null;
            GD.Print(unlock is not null
                ? $"[links] {link.Spec.Id}: locked in Jolt's step, {from} to {to}"
                : $"[links] {link.Spec.Id}: exchange only ({why})");
        }
    }

    private void UnlockShafts()
    {
        foreach (var unlock in _shaftLocks) unlock();
        _shaftLocks.Clear();
    }

    private void ClearLinks()
    {
        UnlockShafts();
        _links?.Release();
        _links = null;
        _linksView?.StopTrace();
        _linksView?.QueueFree();
        _linksView = null;
        SetJoining(false);
    }

    /// <summary>One tick of a world: the locked shafts read after Jolt's step, water through the cross-machine pipes, every machine, then the shafts, then the records.</summary>
    private void StepWorld(double delta)
    {
        _links?.ReadLockedShafts();   // what the locked shafts carried through Jolt's last step (#191)
        _links?.StepPipes(delta);
        foreach (var v in _views) v.SimulateUntraced(delta);
        _links?.StepShafts(delta);
        _groundSim?.Step(delta);          // after the machines: what they poured is on the ground's ledger
        _terrainView?.Refresh(delta);
        foreach (var v in _views) v.TraceStep(delta);
        _linksView?.Refresh(delta);
        if (_scriptedJoins.Count > 0) RunScriptedJoin();
    }

    /// <summary>
    /// Every link the world has now, for a save (issue #82): the ones its file declares and the ones joined during play. Null outside a world.
    /// Main.SaveWorld puts it in <c>WorldSave.Links</c>.
    /// </summary>
    private IReadOnlyList<LinkSpec>? LinksForSave() => _world?.Links;

    /// <summary>
    /// A save's links laid on the world just loaded from its file (issue #82): the world has exactly the links the save had,
    /// resolved against the machines again, so a link made during play is back and working. A save with no link list (an older
    /// one) leaves the file's links as they are. A link naming a placement this world no longer has is left out and said so.
    /// Call it before the machines' saved state is laid on: the links hold the machines' parts, which the state is restored into.
    /// </summary>
    private void LinksRestore(WorldSave save)
    {
        if (_world is null || save.Links is not { } saved) return;
        var left = new List<string>();
        var world = LinkForms.Restore(_world, saved, left);
        foreach (var why in left) GD.PrintErr($"[links] the save's link was left out: {why}");
        _world = world;
        RebuildLinks();
        GD.Print($"[links] restored {world.Links.Count} link(s) from the save: {string.Join(", ", world.Links.Select(l => $"{l.Id} ({l.Kind})"))}");
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
