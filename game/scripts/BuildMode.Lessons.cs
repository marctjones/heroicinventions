using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Workshop lessons in build mode (#166): the Lessons button, the lesson
/// panel, the lit palette entry or button for each step, the green target
/// where the step's part should go, and the step completing itself. What a
/// lesson says and checks is data (game/lessons/*.lesson, read by
/// <see cref="Lesson"/>); whether a step is done is decided by
/// <see cref="LessonRunner"/>, and for a "balanced" step by a real Test run
/// whose traced beam tilt is handed to it.
///
/// A lesson can be left at any step and taken up again: its step, the parts
/// it knows by name and the design are kept in user://lessons/ (or
/// HEROIC_LESSONS_DIR).
/// </summary>
public partial class BuildMode
{
    private LessonRunner? _lesson;
    private PanelContainer _lessonPanel = null!;
    private Label _lessonTitle = null!, _lessonText = null!, _lessonNote = null!;
    private Button _lessonCheckAgain = null!;
    private PopupMenu _lessonMenu = null!;
    private Node3D? _lessonTarget;
    private string? _lessonHighlight;   // the palette entry, "test", or "part:ROLE" lit for this step
    private bool _lessonChecking;       // a settle command is running: don't check inside it
    private double? _lessonAutoTestIn;  // seconds until the lesson presses Test itself
    private int _lessonShownStep = -1;
    private string? _lessonNudgeShown;  // the "why that didn't count" sentence on the panel (#180), or null

    /// <summary>Whether a lesson has been started in this build mode (for the first-run hint).</summary>
    public bool LessonStarted { get; private set; }

    private static readonly Color LitColor = new(0.35f, 0.75f, 0.35f, 0.55f);
    private static readonly StandardMaterial3D TargetOverlay = Overlay(new Color(0.3f, 1f, 0.4f, 0.45f));
    private static readonly StandardMaterial3D LessonPartOverlay = Overlay(new Color(0.3f, 1f, 0.4f, 0.35f));

    private static string LessonDir()
    {
        string dir = OS.GetEnvironment("HEROIC_LESSONS_DIR") is { Length: > 0 } d ? d : ProjectSettings.GlobalizePath("user://lessons");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Every lesson shipped in game/lessons, in file order.</summary>
    private static List<Lesson> AllLessons()
    {
        var lessons = new List<Lesson>();
        using var dir = DirAccess.Open("res://lessons");
        if (dir is null) return lessons;
        foreach (var f in dir.GetFiles().Where(f => f.EndsWith(".lesson")).OrderBy(f => f))
            try { lessons.Add(Lesson.Parse(Godot.FileAccess.GetFileAsString($"res://lessons/{f}"))); }
            catch (FormatException e) { GD.Print($"[Lesson] {f}: {e.Message}"); }
        return lessons;
    }

    private void BuildLessonUi(CanvasLayer layer, HBoxContainer actions)
    {
        var lessons = new Button { Text = "Lessons", TooltipText = "Step-by-step: build simple machines, from a see-saw to water running between tanks", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Disabled = Live };
        lessons.AddThemeColorOverride("font_color", new Color(0.7f, 1f, 0.7f));
        _lessonMenu = new PopupMenu();
        _lessonMenu.IndexPressed += OnLessonMenu;
        lessons.AddChild(_lessonMenu);
        lessons.Pressed += () =>
        {
            FillLessonMenu();
            _lessonMenu.Position = (Vector2I)(lessons.GetScreenPosition() + new Vector2(0, lessons.Size.Y));
            _lessonMenu.Popup();
        };
        actions.AddChild(lessons);
        actions.MoveChild(lessons, 0);

        _lessonPanel = new PanelContainer { Visible = false };
        _lessonPanel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _lessonPanel.OffsetLeft = -300; _lessonPanel.OffsetRight = 300; _lessonPanel.OffsetTop = 38;
        _lessonPanel.GrowHorizontal = Control.GrowDirection.Both;
        var style = new StyleBoxFlat { BgColor = new Color(0.1f, 0.14f, 0.11f, 0.92f), BorderColor = new Color(0.35f, 0.75f, 0.35f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8 };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(6);
        _lessonPanel.AddThemeStyleboxOverride("panel", style);
        layer.AddChild(_lessonPanel);
        var col = new VBoxContainer();
        _lessonPanel.AddChild(col);
        _lessonTitle = new Label();
        _lessonTitle.AddThemeColorOverride("font_color", new Color(0.7f, 1f, 0.7f));
        col.AddChild(_lessonTitle);
        _lessonText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(570, 0) };
        _lessonText.AddThemeFontSizeOverride("font_size", 15);
        col.AddChild(_lessonText);
        _lessonNote = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(570, 0) };
        col.AddChild(_lessonNote);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _lessonCheckAgain = new Button { Text = "Check again", Visible = false, FocusMode = Control.FocusModeEnum.None };
        _lessonCheckAgain.Pressed += () => StartTest();
        buttons.AddChild(_lessonCheckAgain);
        var restart = new Button { Text = "Start over", FocusMode = Control.FocusModeEnum.None };
        restart.Pressed += () => { if (_lesson is { } l) StartLesson(l.Lesson.Id, resume: false); };
        buttons.AddChild(restart);
        var leave = new Button { Text = "Leave lesson", FocusMode = Control.FocusModeEnum.None, TooltipText = "Stop here; Lessons takes it up again at this step" };
        leave.Pressed += LeaveLesson;
        buttons.AddChild(leave);
        col.AddChild(buttons);

        Tested += OnLessonTested;
    }

    private void FillLessonMenu()
    {
        _lessonMenu.Clear();
        var lessons = AllLessons();
        int i = 0;
        foreach (var l in lessons)
        {
            var saved = LoadProgress(l.Id);
            _lessonMenu.AddItem(saved is { } p && p.Index > 0 ? $"{l.Title}: carry on at step {p.Index + 1}" : l.Title, i);
            _lessonMenu.SetItemMetadata(_lessonMenu.ItemCount - 1, $"resume:{l.Id}");
            if (saved is { Index: > 0 })
            {
                _lessonMenu.AddItem($"{l.Title}: start over", i + 1000);
                _lessonMenu.SetItemMetadata(_lessonMenu.ItemCount - 1, $"fresh:{l.Id}");
            }
            i++;
        }
        if (lessons.Count == 0) _lessonMenu.AddItem("(no lessons found)");
    }

    private void OnLessonMenu(long index)
    {
        if (_lessonMenu.GetItemMetadata((int)index).AsString() is not { Length: > 0 } meta) return;
        var (how, id) = (meta[..meta.IndexOf(':')], meta[(meta.IndexOf(':') + 1)..]);
        StartLesson(id, resume: how == "resume");
    }

    /// <summary>Starts a lesson, or takes it up again where it was left (its design and step).</summary>
    private void StartLesson(string id, bool resume)
    {
        if (Live) return;   // a lesson clears the bench: never a machine running in the world
        if (AllLessons().FirstOrDefault(l => l.Id == id) is not { } lesson) { GD.Print($"[Lesson] no lesson {id}"); return; }
        if (Testing) FinishTest();
        CancelPlacing();
        CancelLink();
        LessonStarted = true;
        _lesson = new LessonRunner(lesson, _materials);
        string design = System.IO.Path.Combine(LessonDir(), $"{id}.machine");
        var saved = resume ? LoadProgress(id) : null;
        _lessonChecking = true;   // setting the bench up is not the player's doing
        if (saved is { } p && System.IO.File.Exists(design))
        {
            RunCommand($"(load {id})", design);
            _lesson.Restore(p.Index, p.Roles);
            _lesson.LastTilt = p.Tilt;
            _lesson.Revalidate(_session.Document);
        }
        else
            foreach (var part in _session.Document.Parts.Keys.ToList()) RunCommand($"(remove {part})");   // a fresh bench, each removal undoable
        _lessonChecking = false;
        SetShowAll(false);
        Select(null);
        var view = lesson.View;
        (_orbit.Pivot, _orbit.Distance, _orbit.Yaw, _orbit.Pitch) = view is null ? (new Vector3(0, 0.4f, 0), 3.4f, 0.35f, 0.5f)
            : (new Vector3((float)view.Pivot.X, (float)view.Pivot.Y, (float)view.Pivot.Z), (float)view.Distance, (float)view.Yaw, (float)view.Pitch);
        _orbit.Apply();
        _lessonPanel.Visible = true;
        GD.Print($"[Lesson] started {id}{(saved is { } s ? $" at step {_lesson.Index + 1}" : "")}");
        _lessonShownStep = -1;
        LessonCheck();
        ShowLessonStep();
    }

    private void LeaveLesson()
    {
        if (_lesson is null) return;
        SaveProgress();
        GD.Print($"[Lesson] left {_lesson.Lesson.Id} at step {_lesson.Index + 1}");
        _lesson = null;
        _lessonPanel.Visible = false;
        _lessonAutoTestIn = null;
        SetLessonHighlight(null);
        ClearLessonTarget();
        RefreshHighlights();
    }

    // ------------------------------------------------------------ progress

    private string ProgressPath => System.IO.Path.Combine(LessonDir(), "progress.cfg");

    private (int Index, Dictionary<string, string> Roles, double? Tilt)? LoadProgress(string id)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ProgressPath) != Error.Ok || !cfg.HasSectionKey(id, "step")) return null;
        var roles = new Dictionary<string, string>();
        foreach (var pair in cfg.GetValue(id, "roles", Array.Empty<string>()).AsStringArray())
            if (pair.Split('=') is [var k, var v]) roles[k] = v;
        double tilt = cfg.GetValue(id, "tilt", -1).AsDouble();
        return (cfg.GetValue(id, "step").AsInt32(), roles, tilt >= 0 ? tilt : null);
    }

    private void SaveProgress()
    {
        if (_lesson is null) return;
        var cfg = new ConfigFile();
        cfg.Load(ProgressPath);
        string id = _lesson.Lesson.Id;
        cfg.SetValue(id, "step", _lesson.Finished ? 0 : _lesson.Index);
        cfg.SetValue(id, "roles", _lesson.Roles.Select(kv => $"{kv.Key}={kv.Value}").ToArray());
        cfg.SetValue(id, "tilt", _lesson.LastTilt ?? -1);
        cfg.Save(ProgressPath);
        if (_session.Document.Parts.Count > 0) _session.SaveFile(System.IO.Path.Combine(LessonDir(), $"{id}.machine"));
    }

    // -------------------------------------------------------------- steps

    /// <summary>After every edit: does the design now do what the step asks? A close placement is then set exactly on its target.</summary>
    private void LessonCheck()
    {
        if (_lesson is null || _lessonChecking) return;
        _lessonChecking = true;
        try
        {
            int before = _lesson.Index;
            string? nudge = null;
            for (int guard = 0; guard < 10; guard++)
            {
                var (done, settle, reason) = _lesson.CheckDesign(_session.Document);
                nudge = reason;
                if (!done) break;
                GD.Print($"[Lesson] step {before + guard + 1} done{(settle.Count == 0 ? "" : $": set on its target {string.Join(" ; ", settle)}")}");
                foreach (var command in settle) RunCommand(command);
            }
            if (_lesson.Index != before || _lessonShownStep != _lesson.Index) { SaveProgress(); ShowLessonStep(); }
            else if (nudge != _lessonNudgeShown) ShowLessonStep();
            else PlaceLessonTarget();
        }
        finally { _lessonChecking = false; }
    }

    private void OnLessonTested(TestResult result)
    {
        if (_lesson is null) return;
        int before = _lesson.Index;
        string? outcome = _lesson.Current?.Check is { IsOutcome: true } oc ? oc.Type : null;
        bool done = _lesson.OnTest(result.Tilts, result.Masses, result.Facts, _session.Document);
        string tilts = string.Join(" ", result.Tilts.Select(kv => $"{kv.Key} most {kv.Value.Select(Math.Abs).DefaultIfEmpty(0).Max():0.###}° end {kv.Value.LastOrDefault():0.###}°"));
        GD.Print($"[Lesson] test seen at step {before + 1}: {tilts}; masses {string.Join(" ", result.Masses.Select(kv => $"{kv.Key}={kv.Value:0.###}kg"))}");
        if (done) GD.Print($"[Lesson] step {before + 1} done{(outcome == "balanced" ? $": balanced, most tilt {_lesson.LastTilt:0.###}°" : outcome is not null ? $": {outcome}" : "")}");
        else if (_lesson.Failure is { } why) GD.Print($"[Lesson] step {before + 1} not yet: {why}");
        SaveProgress();
        _lessonShownStep = -1;
        ShowLessonStep();
    }

    /// <summary>Shows the current step (or the lesson's ending): its words with the numbers filled in, its lit entry and its target.</summary>
    private void ShowLessonStep()
    {
        if (_lesson is not { } run) return;
        var doc = _session.Document;
        int n = run.Lesson.Steps.Count;
        if (run.Finished)
        {
            _lessonTitle.Text = $"{run.Lesson.Title}: done";
            _lessonText.Text = run.Fill(run.Lesson.Done, doc);
            _lessonNote.Text = "";
            _lessonCheckAgain.Visible = false;
            SetLessonHighlight(null);
            ClearLessonTarget();
            if (_lessonShownStep != n) GD.Print($"[Lesson] finished {run.Lesson.Id}: {_lessonText.Text}");
            _lessonShownStep = n;
            return;
        }
        var step = run.Current!;
        bool fresh = _lessonShownStep != run.Index;
        _lessonTitle.Text = $"{run.Lesson.Title} · step {run.Index + 1} of {n}";
        _lessonText.Text = run.Fill(step.Text, doc);
        if (fresh) run.Rebase(doc);   // whatever is on the bench as a step begins is not nudged about
        string? why = run.Failure ?? run.Nudge;
        _lessonNote.Text = why ?? (run.Index > 0 && fresh ? $"✓ Step {run.Index} done." : _lessonNudgeShown is not null ? "" : _lessonNote.Text);
        if (run.Nudge is not null && run.Nudge != _lessonNudgeShown) GD.Print($"[Lesson] step {run.Index + 1} nudge shown: {_lessonNote.Text}");
        _lessonNudgeShown = run.Nudge;
        _lessonNote.Modulate = why is null ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.6f, 0.5f);
        _lessonCheckAgain.Visible = step.Check.IsOutcome && run.Failure is not null;
        if (fresh && step.Variant is { } size && step.Highlight is { } entry) _variantChosen[EntryKeyOf(entry)] = size;   // the card offers the size the lesson uses
        SetLessonHighlight(step.Highlight);
        PlaceLessonTarget();
        if (fresh)
        {
            GD.Print($"[Lesson] step {run.Index + 1} of {n}: {_lessonText.Text}");
            if (step.Check.Type == "balanced") _lessonAutoTestIn = 1.0;   // the lesson runs its own check
        }
        _lessonShownStep = run.Index;
    }

    /// <summary>Called each frame: the lesson pressing Test itself for its final check.</summary>
    private void LessonProcess(double delta)
    {
        if (_lessonAutoTestIn is not { } left) return;
        left -= delta;
        if (left > 0) { _lessonAutoTestIn = left; return; }
        _lessonAutoTestIn = null;
        if (_lesson?.Current?.Check.Type == "balanced" && !Testing) StartTest();
    }

    /// <summary>The material a lesson step asks new parts from its lit entry to be made of, or null.</summary>
    private string? LessonMaterialFor(string key) =>
        _lesson?.Current is { Material: { } m, Highlight: { } h } && h == key ? m : null;

    // -------------------------------------------------------- highlights

    private void SetLessonHighlight(string? highlight)
    {
        _lessonHighlight = highlight;
        // a part that is not among the starter parts (the pulley) is only in the full list
        if (highlight is not (null or "test") && !highlight.StartsWith("part:") && !StarterKeys.Contains(highlight) && !_showAll) SetShowAll(true);
        HighlightPaletteEntry(highlight);
        _testButton.Modulate = highlight == "test" ? new Color(0.55f, 1.3f, 0.55f) : Colors.White;
        RefreshHighlights();
    }

    private void HighlightPaletteEntry(string? key)
    {
        for (int i = 0; i < _paletteList.ItemCount; i++)
            if (_paletteList.IsItemSelectable(i))
                _paletteList.SetItemCustomBgColor(i, key is not null && _paletteList.GetItemMetadata(i).AsString() == key ? LitColor : new Color(0, 0, 0, 0));
    }

    /// <summary>The part a step asks to be moved ("part:light"), lit in the scene.</summary>
    private string? LessonPartId =>
        _lesson is { } run && _lessonHighlight is { } h && h.StartsWith("part:") && run.Roles.TryGetValue(h[5..], out var id) ? id : null;

    private void ClearLessonTarget()
    {
        _lessonTarget?.QueueFree();
        _lessonTarget = null;
    }

    /// <summary>A translucent green copy of the step's part where it should go.</summary>
    private void PlaceLessonTarget()
    {
        ClearLessonTarget();
        if (_lesson is not { Finished: false } run || run.Current?.Target is not { } t || run.TargetAt(_session.Document) is not { } at) return;
        string key = t.Kind;
        var model = BuildPartModel(key, material: run.Current.Material);
        AddChild(model);
        if (model is MachineView mv) mv.SetFrozen(true);
        foreach (var g in Descendants(model).OfType<GeometryInstance3D>())
        {
            g.Transparency = 0.6f;
            g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            g.MaterialOverlay = TargetOverlay;
            if (g is Label3D) g.Visible = false;
        }
        model.Position = new Vector3((float)at.X, (float)at.Y - Lift(key), (float)at.Z);
        _lessonTarget = model;
        // a green pin over the spot, seen through anything, so a small target can't be missed
        var pinMat = Shapes.Mat(new Color(0.3f, 1f, 0.4f));
        pinMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        pinMat.NoDepthTest = true;
        var top = new Vector3((float)at.X, (float)at.Y + 0.35f, (float)at.Z);
        var pin = Shapes.Rod(top, top + new Vector3(0, -0.22f, 0), 0.008f, pinMat);
        var head = Shapes.Sphere(0.03f, pinMat);
        head.Position = top;
        foreach (var n in new GeometryInstance3D[] { pin, head }) { n.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; AddChild(n); n.Reparent(model); }
    }
}
