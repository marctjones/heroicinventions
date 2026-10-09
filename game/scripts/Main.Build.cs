using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Building in a world (issue #204). Building is free and of unlimited reach (docs/lonely-rover.html, "Building is free"): the
/// player starts a new machine at a spot (the ground ahead of the rover, or where the camera looks), and build mode opens there,
/// live: the first part placed adds a placement to the world (<see cref="WorldDef.WithPlacement"/>) and every change rebuilds it in
/// place, as a live edit does (#75). The machine is an ordinary placement with its design on it (<see cref="Placement.Built"/>, in
/// world coordinates), so links (#78) join it like any other, the world steps it like any other, and it obeys the same physics.
/// Its design, unfinished parts and all, is kept: a part that can't be built yet (a generator with no bank) is set aside as build
/// mode sets it aside (<see cref="BuildSession.Buildable"/>) and the rest runs. Saves carry the built machines whole (<see cref="WorldSave.Built"/>).
///
/// In the game (a world with a rover) only a machine the player built can be rebuilt: the cargo was found, not built
/// (<see cref="EditMachine"/>). The left panel's Build section offers "Build a new machine" and an Edit button for each built one.
///
/// Scripted (HEROIC_INPUT, tools/gui-check.sh or headless): "build new [X Z]" starts a machine at X Z (default: ahead of the rover);
/// "build (COMMAND …)" runs one build-mode command, with every <c>#:at (x y z)</c> taken from the build's spot on the ground;
/// "build edit LABEL" opens a built machine; "build done" closes build mode; "build list" prints the built machines.
/// </summary>
public partial class Main
{
    private string? _buildingLabel;                 // the built machine build mode is open on, if any
    private Vector3 _buildOrigin;                   // its spot on the ground: what a scripted command's #:at is taken from
    private (string Path, double Every)? _worldTrace;
    private VBoxContainer _buildSection = null!;
    private VBoxContainer _builtList = null!;
    private const float BuildAhead = 8f;           // m ahead of the rover a new machine is started

    private void BuildBuildButtons(VBoxContainer col)
    {
        _buildSection = new VBoxContainer { Name = "Build", Visible = false };
        var start = BigButton("Build a new machine");
        start.TooltipText = "Opens build mode on the ground ahead of the rover. Building is free and reaches anywhere; what is built obeys physics.";
        start.Pressed += () => StartNewBuild(null);
        _buildSection.AddChild(start);
        _builtList = new VBoxContainer { Name = "Built" };
        _buildSection.AddChild(_builtList);
        col.AddChild(_buildSection);
    }

    /// <summary>The Build section shows in a world; one Edit button for each machine the player built.</summary>
    private void RefreshBuiltList()
    {
        if (_buildSection is null) return;
        _buildSection.Visible = _world is not null;
        foreach (var c in _builtList.GetChildren()) { _builtList.RemoveChild(c); c.QueueFree(); }
        foreach (var p in _world?.Placements.Where(p => p.Built is not null) ?? [])
        {
            string label = p.Label;
            var edit = BigButton($"Edit {label}");
            edit.Pressed += () => { if (_byName.GetValueOrDefault(label) is { } v) EditMachine(v); };
            _builtList.AddChild(edit);
        }
    }

    /// <summary>A built machine's design built as far as it can be (parts that can't be built yet set aside), or null if nothing in it builds.</summary>
    private MachineRuntime? BuiltRuntime(string label, MachineDef design)
    {
        MachineRuntime? runtime = null;
        var unfinished = new Dictionary<string, string>();
        BuildSession.Buildable(design, def => runtime = new MachineRuntime(def, _materials, _activeTuning), unfinished);
        foreach (var (id, why) in unfinished) GD.Print($"[build] {label}: {id} not finished: {why}");
        return runtime;
    }

    /// <summary>The spot a new machine starts at: the ground ahead of the rover, or under the camera's pivot.</summary>
    private Vector3 BuildSpot()
    {
        var p = RoverIsPlayer ? _rover!.Chassis.GlobalPosition + (_rover.Forward with { Y = 0 }).Normalized() * BuildAhead : _orbit.Pivot;
        return new Vector3(p.X, (float)(_groundSim?.Ground.HeightAt(p.X, p.Z) ?? 0), p.Z);
    }

    /// <summary>
    /// Opens build mode on a new machine at <paramref name="at"/> (x and z; null: <see cref="BuildSpot"/>). Nothing is placed until
    /// the first part is: then the world gains a placement, labelled built-1, built-2, …
    /// </summary>
    private void StartNewBuild(Vector2? at)
    {
        if (_world is null || _buildMode is not null) return;
        var spot = at is { } xz ? new Vector3(xz.X, (float)(_groundSim?.Ground.HeightAt(xz.X, xz.Y) ?? 0), xz.Y) : BuildSpot();
        string label = _world.NextPlacementLabel("built");
        // the scene the world's machines stand in (the planet's air and gravity, its sun and weather): a new machine stands in it too,
        // not on the default Earth. Taken from a placed machine, the one with a sky and weather by preference.
        var scene = _views.Select(v => v.Runtime.Def).OrderByDescending(d => (d.Weather is not null ? 2 : 0) + (d.Sun is not null ? 1 : 0)).FirstOrDefault();
        var seed = new MachineDef
        {
            Name = label, Source = $"built by the player in {_world.Name}",
            Ambient = scene?.Ambient ?? 20, Sun = scene?.Sun, Planet = scene?.Planet ?? HeroicInventions.Sim.Planet.Earth, Weather = scene?.Weather,
            Parts = [], Pipes = [], Connects = [], SealedAir = [],
        };
        var placement = new Placement(label, label, new Vec3(spot.X, 0, spot.Z), null);   // (at x 0 z): on the ground there, as a file's placement is
        MachineView? view = null;
        _leftPanel.Visible = false;
        _infoPanel.Visible = false;
        _buildMode = new BuildMode(_materials, seed, def =>
        {
            var design = _buildMode!.CurrentMachineDef();   // with any unfinished parts: they stay in the design
            if (view is null)
            {
                _world = _world!.WithPlacement(placement with { Built = design });
                var runtime = new MachineRuntime(def, _materials, _activeTuning);
                // the sun where the world's is (its clock starts now; the sky's hour is the world's)
                if (scene is not null && _views.FirstOrDefault(v => v.Runtime.Def == scene) is { } sibling
                    && sibling.Runtime.FieldGetters.TryGetValue("scene.time", out var hour) && runtime.FieldSetters.TryGetValue("scene.time", out var setHour))
                    setHour(hour());
                view = AddPlacedView(label, label, runtime);
                view.SetFrozen(!_running);
                if (_worldTrace is { } t) view.StartTrace($"{t.Path}.{label}", t.Every);
                RebuildLinks();
                RefreshBuiltList();
                GD.Print($"[build] placed {label} at ({spot.X:F2} {spot.Y:F2} {spot.Z:F2})");
            }
            else
            {
                _world = _world!.WithBuilt(label, design);
                view = ReplaceView(view, def);
            }
            return view;
        }, () => view!);
        _buildingLabel = label;
        _buildOrigin = spot;
        if (_groundSim is { } ground) _buildMode.GroundHeight = ground.Ground.HeightAt;   // parts land on the ground (#37)
        _buildMode.StartPivot = spot + Vector3.Up * 2;
        _buildMode.StartYaw = _orbit.Yaw;   // from where the player was looking
        _buildMode.ExitRequested += () => CallDeferred(MethodName.CloseLiveEdit);
        _buildMode.RunRequested += () => CallDeferred(MethodName.CloseLiveEdit);
        _buildMode.InGame = RoverIsPlayer;
        AddChild(_buildMode);
        GD.Print($"[build] new machine {label} at ({spot.X:F2} {spot.Y:F2} {spot.Z:F2})");
    }

    /// <summary>
    /// Puts the open build's design into the world's placement: a change that didn't alter what runs (an unfinished part added)
    /// is still the player's design. A built machine emptied of parts is taken out of the world.
    /// </summary>
    private void SyncBuiltDesign()
    {
        if (_buildMode is null || _buildingLabel is not { } label || _world is null) return;
        if (_world.Placements.FirstOrDefault(p => p.Label == label) is not { Built: not null }) return;   // nothing placed yet
        var design = _buildMode.CurrentMachineDef();
        if (design.Parts.Count > 0) { _world = _world.WithBuilt(label, design); return; }
        _world = _world.WithoutPlacement(label);
        if (_byName.GetValueOrDefault(label) is { } view)
        {
            view.GetParent()?.RemoveChild(view);
            view.QueueFree();
            _views.Remove(view);
            _viewMachine.Remove(view);
            _viewBounds.Remove(view);
            _byName.Remove(label);
            if (_current == view) _current = _views.FirstOrDefault();
        }
        RebuildLinks();
        RefreshBuiltList();
        GD.Print($"[build] {label} has no parts left: taken out of the world");
    }

    /// <summary>LoadSave: the machines the player built go back into the world the save names, before the state is laid on.</summary>
    private void PlaceSavedBuilds(WorldSave save)
    {
        if (_world is null || save.Built.Count == 0) return;
        foreach (var b in save.Built)
        {
            if (_byName.GetValueOrDefault(b.Label) is { } old)   // the world file has a machine by that label: the save's build stands instead
            {
                if (_world.Placements.Any(p => p.Label == b.Label && p.Built is not null)) _world = _world.WithBuilt(b.Label, b.Built!);
                else { GD.PushError($"the save's built {b.Label} has the label of a machine the world places; left out"); continue; }
                old.GetParent()?.RemoveChild(old);
                old.QueueFree();
                _views.Remove(old);
                _viewMachine.Remove(old);
                _viewBounds.Remove(old);
            }
            else _world = _world.WithPlacement(b);
            if (BuiltRuntime(b.Label, b.Built!) is not { } runtime) continue;
            var view = AddPlacedView(b.Label, b.Label, runtime);
            view.SetFrozen(!_running);
            GD.Print($"[build] loaded {b.Label}: {runtime.Def.Parts.Count} of {b.Built!.Parts.Count} parts running");
        }
        if (_current is null || !IsInstanceValid(_current)) _current = _views.FirstOrDefault();
        RebuildLinks();
        RefreshBuiltList();
    }

    private static readonly System.Text.RegularExpressions.Regex AtClause =
        new(@"#:at\s*\(\s*(-?[0-9.eE+-]+)\s+(-?[0-9.eE+-]+)\s+(-?[0-9.eE+-]+)\s*\)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Scripted building (see the class summary).</summary>
    private ScriptedInput.Step? BuildStep(string[] w)
    {
        if (w[0] != "build" || w.Length < 2) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        switch (w[1])
        {
            case "new":
                StartNewBuild(w.Length >= 4 ? new Vector2(float.Parse(w[2], inv), float.Parse(w[3], inv)) : null);
                return ScriptedInput.Step.Next;
            case "edit" when w.Length >= 3:
                if (_byName.GetValueOrDefault(w[2]) is { } view)
                {
                    EditMachine(view);
                    if (_world?.Placements.FirstOrDefault(p => p.Label == w[2]) is { } p)
                        _buildOrigin = new Vector3((float)p.At.X, (float)(_groundSim?.Ground.HeightAt(p.At.X, p.At.Z) ?? 0), (float)p.At.Z);
                }
                else GD.Print($"[build] no machine placed as {w[2]}");
                return ScriptedInput.Step.Next;
            case "done":
                if (_buildMode is not null) CloseLiveEdit();
                return ScriptedInput.Step.Next;
            case "list":
                foreach (var p in _world?.Placements.Where(p => p.Built is not null) ?? [])
                {
                    var running = _byName.GetValueOrDefault(p.Label)?.Runtime.Def;
                    var unfinished = p.Built!.Parts.Select(q => q.Id).Where(id => running?.Part(id) is null).ToList();
                    GD.Print($"[build] {p.Label}: design {string.Join(" ", p.Built.Parts.Select(q => $"{q.Id}:{q.Kind}"))}; running {running?.Parts.Count ?? 0} parts"
                             + (unfinished.Count > 0 ? $"; unfinished {string.Join(" ", unfinished)}" : ""));
                }
                return ScriptedInput.Step.Next;
        }
        if (_buildMode is null) { GD.Print("[build] no build open"); return ScriptedInput.Step.Next; }
        string command = string.Join(' ', w[1..]);
        command = AtClause.Replace(command, m =>
        {
            double X(int i) => double.Parse(m.Groups[i].Value, inv);
            return $"#:at ({(X(1) + _buildOrigin.X).ToString("R", inv)} {(X(2) + _buildOrigin.Y).ToString("R", inv)} {(X(3) + _buildOrigin.Z).ToString("R", inv)})";
        });
        _buildMode.Command(command);
        return ScriptedInput.Step.Next;
    }
}
