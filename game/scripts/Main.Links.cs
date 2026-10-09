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
public partial class Main : ScriptedInput.IJoinStep
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
        _joinButton = BigButton(JoinLabel());
        _joinButton.Visible = false;
        _joinButton.Pressed += () => SetJoining(!_joining);
        _joinButton.Draw += RefreshJoinLabel;   // the rover may be spawned after the button is made, and the world can change under it
        col.AddChild(_joinButton);
    }

    /// <summary>
    /// The button's text. J joins in a machine run, but the rover game uses the key up (Main.Rover.cs: RoverInput swallows J while you
    /// drive), so there the label does not offer it (#224).
    /// </summary>
    private string JoinLabel() => _joining ? "Stop joining (Esc)" : RoverIsPlayer ? "Join machines" : "Join machines (J)";

    private void RefreshJoinLabel()
    {
        if (_joinButton is not null && _joinButton.Text != JoinLabel()) _joinButton.Text = JoinLabel();
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
        RefreshJoinLabel();
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
        if (_linksView is not null) _linksView.ExtraFields = GroundFields.Concat(RouteFields);   // lazy, not a copy: the ground adds boulder-N fields after its collapse
        if (_linksTrace is { } t && _linksView is not null && !_linksView.Tracing && (_world is { Links.Count: > 0 } || _groundSim is not null))
            _linksView.StartTrace(t.Path, t.Every, _current?.Runtime.Time ?? 0);
    }

    private void SetJoining(bool on)
    {
        _joining = on && _world is not null;
        _firstPick = null;
        _lastJoinClick = null;
        _pickList?.Close();
        RefreshJoinLabel();
        if (_joining) GD.Print($"[links] {WorldLinkGestures.Prompt(null)}");
    }

    private PickList? _pickList;
    private IReadOnlyList<(PickHit Hit, MachineView View, string Part)> _listed = [];
    private Vector2 _joinRightAt;
    /// <summary>Where the last join click landed, and the first pick it was made on top of, so a second click on the spot (or a right-click) can offer the parts there and replace that pick (#225).</summary>
    private (Vector2 Screen, LinkEnd? FirstBefore)? _lastJoinClick;

    /// <summary>The parts whose pick boxes the ray through <paramref name="screen"/> crosses, with their views. The generator and the bank, which the view draws no body for, get a small box at their place (<see cref="PickBoxes.SimPartSide"/>).</summary>
    private List<(PickHit Hit, MachineView View, string Part)> PartsUnder(Vector3 from, Vector3 dir)
    {
        var crossed = new List<(PickHit, MachineView, string)>();
        foreach (var v in _views)
            foreach (var part in v.Runtime.Def.Parts)
            {
                Aabb? box = null;
                if (v.PartNodes.TryGetValue(part.Id, out var nodes))
                    foreach (var n in nodes)
                        if (BoundsOf(n) is { Size: var s } b && s != Vector3.Zero) box = box is { } x ? x.Merge(b) : b;
                if (box is null && PickBoxes.SimPartSide(part.Kind) is { } side && v.LinkPoint(part.Id, null) is { } at)
                    box = new Aabb(at - Vector3.One * (float)(side / 2), Vector3.One * (float)side);
                if (box is { } bb && RayHits(from, dir, bb.Grow(0.05f), out float t))
                {
                    var g = bb.Grow(0.05f);
                    crossed.Add((new PickHit($"{v.Name}.{part.Id}", new PickBox(g.Position.X, g.Position.Y, g.Position.Z, g.End.X, g.End.Y, g.End.Z), t), v, part.Id));
                }
            }
        return crossed;
    }

    private LinkEnd EndOf(MachineView view, string partId, Vector3 from, Vector3 dir)
    {
        var part = view.Runtime.Def.Part(partId)!;
        // on a tank, the port closest to where the ray passes
        string? port = part.Kind == "tank" ? part.Ports.Where(p => p.Kind == "water")
            .Select(p => (p.Name, D: DistanceToRay(view.LinkPoint(partId, p.Name)!.Value, from, dir)))
            .OrderBy(p => p.D).Select(p => p.Name).FirstOrDefault() : null;
        return new LinkEnd(view.Name, partId, port);
    }

    /// <summary>
    /// A click while joining: the part under the cursor (and, on a tank, the port nearest the cursor) is picked; the second pick on
    /// another machine makes the link, or is refused with the reason. Which part (#225): where the ray crosses boxes that do not hold
    /// one another the nearest wins; where the nearest holds others (the gears inside the sails' box, the bank inside its crate's) the
    /// smaller wins. A second click on the same spot, or a right-click (<paramref name="offerList"/>), offers the parts under the click,
    /// nearest first, to choose from (<see cref="PickList"/>), so every part can be joined.
    /// </summary>
    private void JoinPickAt(Vector2 screen, bool offerList = false)
    {
        _pickList?.Close();
        var from = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        var under = PartsUnder(from, dir);
        var ordered = PickBoxes.List(under.Select(u => u.Hit));
        if (under.Count > 1)   // say which boxes it crossed, nearest first
            GD.Print($"[links] the click crossed {under.Count} parts' boxes, nearest first: {string.Join(", ", ordered.Select(c => $"{c.Name} ({c.T:F1} m)"))}");
        if (PickBoxes.Choose(under.Select(u => u.Hit)) is not { } chosen) { GD.Print($"[links] nothing there to join (at {screen}, looking along {dir} from {from})"); _lastJoinClick = null; _lastPicked = null; return; }
        bool again = _lastJoinClick is { } last && last.Screen.DistanceTo(screen) < 4;
        if (under.Count > 1 && (offerList || again))
        {
            var firstBefore = again ? _lastJoinClick!.Value.FirstBefore : _firstPick;
            _listed = ordered.Select(h => under.First(u => u.Hit.Name == h.Name)).ToList();
            GD.Print($"[links] pick list at ({screen.X:F0} {screen.Y:F0}): {string.Join(", ", _listed.Select((u, i) => $"{i + 1} {u.Hit.Name}"))}");
            _listFirstBefore = firstBefore;
            _lastJoinClick = (screen, firstBefore);
            _listFrom = (from, dir);
            if (_pickList is null) AddChild(_pickList = new PickList { Name = "PickList" });
            _pickList.Offer(screen, _listed.Select(u => $"{u.Hit.Name} ({u.Hit.T:F1} m){(u.Hit.Name == chosen.Name ? " - the click's pick" : "")}").ToList(), ChooseFromPickList);
            return;
        }
        var u0 = under.First(u => u.Hit.Name == chosen.Name);
        _lastPicked = chosen.Name;
        if (under.Count > 1 && chosen.Name != ordered[0].Name) GD.Print($"[links] {chosen.Name} lies inside {ordered[0].Name}'s box and is the smaller: it takes the click");
        var before = _firstPick;
        MakePick(EndOf(u0.View, u0.Part, from, dir), screen, before);
    }

    private ScriptedInput.Step ChooseListed(int n, Func<string, bool> fail)
    {
        if (_pickList is not { IsOpen: true }) { fail($"no pick list is open (a click is offered one only where it crosses more than one part's box)"); return ScriptedInput.Step.Next; }
        if (n > _listed.Count) { fail($"the pick list has {_listed.Count} item(s), not {n}"); return ScriptedInput.Step.Next; }
        ChooseFromPickList(n - 1);
        return ScriptedInput.Step.Next;
    }

    private int _joinClickPhase;
    private ScriptedInput.Step Done() { _joinClickPhase = 0; return ScriptedInput.Step.Next; }
    private string? _lastPicked;   // LABEL.PART of the last click's pick (for the scripted joinclick ... list)

    private LinkEnd? _listFirstBefore;
    private (Vector3 From, Vector3 Dir) _listFrom;

    /// <summary>One pick from a click, remembering where, so the same spot clicked again offers the list.</summary>
    private void MakePick(LinkEnd end, Vector2 screen, LinkEnd? firstBefore)
    {
        bool wasSecond = _firstPick is not null;
        bool ok = Pick(end);
        _lastJoinClick = wasSecond && ok ? null : (screen, firstBefore);
    }

    /// <summary>Item <paramref name="index"/> (0-based) of the open pick list is the click's pick: it takes the place of the pick the click made.</summary>
    private void ChooseFromPickList(int index)
    {
        if (index < 0 || index >= _listed.Count) return;
        var (hit, view, part) = _listed[index];
        var screen = _lastJoinClick?.Screen ?? Vector2.Zero;
        _pickList?.Close();
        _firstPick = _listFirstBefore;
        GD.Print($"[links] chosen from the list: {index + 1} {hit.Name}");
        MakePick(EndOf(view, part, _listFrom.From, _listFrom.Dir), screen, _listFirstBefore);
    }

    private static float DistanceToRay(Vector3 p, Vector3 origin, Vector3 dir)
    {
        var d = p - origin;
        return (d - dir * d.Dot(dir)).Length();
    }

    /// <summary>One pick of the join gesture (a click's part, or a scripted <c>join</c>'s). False when the second pick was refused.</summary>
    private bool Pick(LinkEnd end)
    {
        if (_world is null) return false;
        if (_firstPick is not { } first)
        {
            _firstPick = end;
            GD.Print($"[links] {WorldLinkGestures.Prompt(end)}");
            return true;
        }
        _firstPick = null;
        try
        {
            var link = WorldLinkGestures.Link(_world, label => _byName.GetValueOrDefault(label)?.Runtime.Def, first, end);
            _world = _world.WithLink(link);
            RebuildLinks();
            GD.Print($"[links] joined: {link.Kind} {link.Id} from {link.From} to {link.To}");
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or MachineFormatException)
        {
            GD.Print($"[links] can't join: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// The scripted step "join A B" (#218), anywhere in a script, after a build or not: A and B are LABEL.PART or LABEL.PART.PORT
    /// (a tank's port), the parts of two machines placed in the world. It is the gesture without the screen: the Join machines button's
    /// SetJoining, then the two picks a click on each part would make (<see cref="Pick"/>, what <see cref="JoinPickAt"/> ends in, so the
    /// same rules, the same link and the same rebuild), then joining off as Esc does. The parts may be tens of metres apart, which a
    /// click on one screen cannot reach. A name no machine or part answers to, or a join the rules refuse, prints the error, sets the
    /// run's failure and ends it with exit code 1.
    /// </summary>
    public ScriptedInput.Step JoinStep(string[] w)
    {
        bool Fail(string why)
        {
            GD.PrintErr($"join: {why}");
            _heroicSetFailed = true;
            GetTree().Quit(1);
            return false;
        }
        if (w[0] is "joinclick" or "joinrightclick")   // one real click on a part: where the camera draws it, through the input pipeline, while the Join machines button is on
        {
            bool right = w[0] == "joinrightclick";
            // 'joinclick A N' clicks A, then A's spot again (a second click on one spot offers the list) and takes item N; 'joinclick A list'
            // clicks A and, if the pick was not A, clicks again and takes A from the list, as a person does; 'joinrightclick A' offers the list by a right-click
            // (take an item with 'joinlist N')
            int? item = null;
            bool named = w.Length == 3 && !right && w[2] == "list";
            if (w.Length == 3 && !right && int.TryParse(w[2], out int n) && n >= 1) item = n;
            if (w.Length != (item is null && !named ? 2 : 3)) { Fail($"expected '{w[0]} LABEL.PART[.PORT]{(right ? "" : " [N|list]")}'"); return ScriptedInput.Step.Next; }
            var bits = w[1].Split('.');
            if (bits.Length is < 2 or > 3 || !_byName.TryGetValue(bits[0], out var target)) { Fail($"no machine part '{w[1]}' to click"); return ScriptedInput.Step.Next; }
            if (!_joining) { Fail($"{w[0]}: joining is not on (press the Join machines button first)"); return ScriptedInput.Step.Next; }
            if (target.LinkPoint(bits[1], bits.Length == 3 ? bits[2] : null) is not { } at) { Fail($"{w[1]} has no point to click"); return ScriptedInput.Step.Next; }
            var screen = GetViewport().GetScreenTransform() * _camera.UnprojectPosition(at);
            bool shown = !_camera.IsPositionBehind(at) && GetViewport().GetVisibleRect().HasPoint(screen);
            GD.Print($"[links] {(right ? "right-click" : "click")} {w[1]} at ({at.X:F2} {at.Y:F2} {at.Z:F2}) drawn at screen ({screen.X:F0} {screen.Y:F0}){(shown ? "" : ": NOT ON SCREEN")}");
            if (!shown) { Fail($"{w[1]} is not on screen: the camera must see a part to click it"); return ScriptedInput.Step.Next; }
            // an input event is handled a frame later, so each click is one pass of this step and the next pass (Step.Again) sees what it did
            var button = right ? MouseButton.Right : MouseButton.Left;
            void Click()
            {
                Input.ParseInputEvent(new InputEventMouseMotion { Position = screen, GlobalPosition = screen });
                foreach (bool down in new[] { true, false }) Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = down, Position = screen, GlobalPosition = screen });
            }
            string want = $"{bits[0]}.{bits[1]}";
            switch (_joinClickPhase++)
            {
                case 0:
                    Click();
                    return item is null && !named ? Done() : ScriptedInput.Step.Again;
                case 1:
                    if (named && _lastPicked == want) return Done();
                    if (named) GD.Print($"[links] the click picked {_lastPicked ?? "nothing"}, not {want}: a second click on the spot offers the list");
                    Click();
                    return ScriptedInput.Step.Again;
                default:
                    int index = named ? _listed.ToList().FindIndex(u => u.Hit.Name == want) + 1 : item!.Value;
                    if (_pickList is not { IsOpen: true } || index < 1) { _joinClickPhase = 0; Fail(named ? $"{want} is not among the parts under the click" : "no pick list is open (a click is offered one only where it crosses more than one part's box)"); return ScriptedInput.Step.Next; }
                    _joinClickPhase = 0;
                    return ChooseListed(index, Fail);
            }
        }
        if (w[0] == "joinlist")   // item N (from 1) of the pick list the last click offered
        {
            if (w.Length != 2 || !int.TryParse(w[1], out int n) || n < 1) { Fail("expected 'joinlist N' (N from 1)"); return ScriptedInput.Step.Next; }
            return ChooseListed(n, Fail);
        }
        if (w[0] == "joinbutton")   // what the button says, and whether it shows (a check of #224)
        {
            GD.Print($"[links] join button: '{_joinButton.Text}' visible {_joinButton.IsVisibleInTree()} rover {RoverIsPlayer}");
            return ScriptedInput.Step.Continue;
        }
        if (w.Length != 3) { Fail($"expected 'join LABEL.PART[.PORT] LABEL.PART[.PORT]', got '{string.Join(' ', w)}'"); return ScriptedInput.Step.Next; }
        if (_world is null) { Fail("no world is open"); return ScriptedInput.Step.Next; }
        var ends = new List<LinkEnd>();
        foreach (string text in w[1..])
        {
            var bits = text.Split('.');
            if (bits.Length is < 2 or > 3 || bits.Any(b => b.Length == 0)) { Fail($"'{text}' is not LABEL.PART[.PORT]"); return ScriptedInput.Step.Next; }
            if (!_byName.TryGetValue(bits[0], out var view)) { Fail($"no machine is placed as '{bits[0]}' (in '{text}'); placed: {string.Join(' ', _byName.Keys)}"); return ScriptedInput.Step.Next; }
            if (!view.PartNodes.ContainsKey(bits[1]) || view.Runtime.Def.Part(bits[1]) is null)
            {
                Fail($"{bits[0]} has no drawn part '{bits[1]}' (in '{text}'); its parts: {string.Join(' ', view.PartNodes.Keys)}");
                return ScriptedInput.Step.Next;
            }
            ends.Add(new LinkEnd(bits[0], bits[1], bits.Length == 3 ? bits[2] : null));
        }
        SetJoining(true);
        bool ok = Pick(ends[0]) && Pick(ends[1]);
        SetJoining(false);
        if (!ok) Fail($"'{w[1]}' and '{w[2]}' were not joined (see the [links] line above)");
        return ScriptedInput.Step.Next;
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
