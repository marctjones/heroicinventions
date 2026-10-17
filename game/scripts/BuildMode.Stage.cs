using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// "Add a stage" (building plan, phase 4): "Put a gear on this shaft" and "Add a meshing gear" in one click. The inspector of a selected
/// windmill or wheel has the button, under the other two, and a row with the stage's two sizes (the wheel, then the pinion it drives:
/// 72 and 18 to start with, the route's 4:1; any gear sizes can be picked there and are kept for the next stage). A click puts the wheel
/// on the selected part's axle where "Put a gear on this shaft" puts it by default (1.3 m behind a bare windmill's hub, else one gear
/// width, 0.1 m, behind the hindmost wheel on the axle) and the pinion on the wheel's pitch circle at +x (where "Add a meshing gear" starts)
/// as one (begin ...) of the same commands (<see cref="StageSlots"/>), so one Ctrl+Z takes the whole stage back. Then the new pinion is
/// selected (the next click on the button adds the next stage on its axle) and the camera frames the new pair from behind, a little to the side, both gears face on. The click ends any placing
/// first, as Esc does; a stage has no placing of its own to back out of (Ctrl+Z is the way back). Nothing is refused: two sizes of
/// different modules mesh and are drawn orange, as any such mesh.
/// </summary>
public partial class BuildMode
{
    private string _stageWheel = StageSlots.RouteWheel, _stagePinion = StageSlots.RoutePinion;
    private int _stagesAdded;

    /// <summary>The gear sizes the stage's lists offer: the gear card's.</summary>
    private IReadOnlyList<string> StageSizes() => _entryByKey.TryGetValue("gear", out var e) ? e.Variants : [];

    /// <summary>The inspector's rows for a windmill or a wheel: "Add a stage" and the stage's two sizes.</summary>
    private void AddStageButton(PartSpec part)
    {
        if (!StageSlots.Takes(part)) return;
        var sizes = StageSizes();
        var b = new Button
        {
            Text = "Add a stage",
            TooltipText = part.Kind == "windmill"
                ? "A wheel on the windshaft behind the sails and a pinion meshing it, in one click (the sizes below). Ctrl+Z takes the stage back"
                : "A wheel on this gear's axle, 0.1 m behind it, and a pinion meshing that wheel, in one click (the sizes below). Ctrl+Z takes the stage back",
        };
        b.SetMeta("add_stage", part.Id);
        b.Pressed += () => AddStage(part.Id);
        b.MouseEntered += () => { if (ShaftSlots.Of(_session.Document, part.Id) is { } s) DrawAxle(s, (float)s.DefaultT); };
        b.MouseExited += () => { if (_shaft is null) HideAxle(); };
        _inspector.AddChild(b);
        if (sizes.Count == 0) return;
        var row = new HBoxContainer();
        OptionButton List(string chosen, Action<string> pick, string tip)
        {
            var o = new OptionButton { TooltipText = tip, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
            foreach (var v in sizes) o.AddItem(_palette.FirstOrDefault(p => p.Id == v).Catalogue?.ShapeProps.GetValueOrDefault("teeth") is SNumber t ? $"{t.Value:0}" : v);
            o.Select(Math.Max(0, sizes.ToList().IndexOf(chosen)));
            o.ItemSelected += i => pick(sizes[(int)i]);
            return o;
        }
        row.AddChild(new Label { Text = "Teeth:" });
        row.AddChild(List(_stageWheel, v => _stageWheel = v, "The stage's wheel, on the axle"));
        row.AddChild(new Label { Text = "drives" });
        row.AddChild(List(_stagePinion, v => _stagePinion = v, "The stage's pinion, meshing the wheel"));
        _inspector.AddChild(row);
    }

    /// <summary>The button pressed: the stage on <paramref name="id"/>'s axle, one edit; the pinion selected, the pair framed.</summary>
    private void AddStage(string id)
    {
        if (_placingPaletteId is not null) CancelPlacing();   // whatever was being placed: the stage is placed by the click itself
        CancelLink();
        var wheel = _palette.FirstOrDefault(p => p.Id == _stageWheel).Catalogue;
        var pinion = _palette.FirstOrDefault(p => p.Id == _stagePinion).Catalogue;
        if (wheel is null || pinion is null) { GD.Print($"[BuildMode] stage: no gear {(wheel is null ? _stageWheel : _stagePinion)} in the catalogue"); return; }
        string w, p;
        // in a lesson, the names its stage step would remember them by (wheel-a, pinion-b), so labels and the ratio read in its words
        if (_lesson?.StageRolesFor(_session.Document, id, PartTemplates.Create(wheel, "probe", new Vec3(0, 0, 0), "bronze"), PartTemplates.Create(pinion, "probe", new Vec3(0, 0, 0), "bronze")) is { } roles
            && !_session.Document.HasName(roles.Wheel) && !_session.Document.HasName(roles.Pinion))
            (w, p) = roles;
        else
        {
            w = $"wheel_{_serial++}";
            while (_session.Document.HasName(w)) w = $"wheel_{_serial++}";
            p = $"wheel_{_serial++}";
            while (_session.Document.HasName(p) || p == w) p = $"wheel_{_serial++}";
        }
        if (StageSlots.Plan(_session.Document, id, wheel, pinion, w, p, UsualMaterial("gear")) is not { } stage) return;
        int? lessonStep = _lesson?.Index;
        if (!RunCommand(stage.Command)) return;
        // a lesson step that begins with a view of its own (the ground, for the parts that go there) keeps it: no framing of the pair
        bool lessonView = _lesson is { } run && run.Index != lessonStep && run.Current?.View is not null;
        _stagesAdded++;
        var d = new Vector3((float)(stage.PinionAt.X - stage.WheelAt.X), (float)(stage.PinionAt.Y - stage.WheelAt.Y), (float)(stage.PinionAt.Z - stage.WheelAt.Z));
        var anchorAt = _session.Document.Parts[id].At;
        var back = new Vector3((float)(stage.WheelAt.X - anchorAt.X), (float)(stage.WheelAt.Y - anchorAt.Y), (float)(stage.WheelAt.Z - anchorAt.Z));
        GD.Print($"[BuildMode] stage on {id}: {w} ({_stageWheel}) at {Xyz(V(stage.WheelAt))}, {F(back.Length())} m behind {id} on {stage.Partner}'s arbor; "
            + $"{p} ({_stagePinion}) at {Xyz(V(stage.PinionAt))}, {Xyz(d)} from {w}, meshing it (centre distance {F(stage.CentreDistance)} m); one undo takes the stage back");
        MeshNote(w, p);
        Select(p);
        if (lessonView) { _status.Text = WithRatio($"A stage on {id}: {w} on its axle, {p} meshing it. Ctrl+Z takes it back"); return; }
        Frame([w, p]);
        // and from behind, a little to the side, so both gears are seen face on (edge on the pinion hides its wheel): the orbit's camera
        // stands at pivot + (sin yaw, cos yaw) x distance, so yaw = the axle's way back, turned 20 degrees
        if (ShaftSlots.Of(_session.Document, w) is { } axle && Math.Abs(axle.Behind.Y) < 0.9)
        {
            _orbit.Yaw = Mathf.Atan2((float)axle.Behind.X, (float)axle.Behind.Z) + Mathf.DegToRad(20);
            _orbit.Pitch = Mathf.DegToRad(14);
            _orbit.Distance = Mathf.Max(_orbit.Distance, 2f);
            _orbit.Apply();
        }
        _status.Text = WithRatio($"A stage on {id}: {w} on its axle, {p} meshing it. Add a stage again for the next on {p}'s axle; Ctrl+Z takes it back");
    }

    /// <summary>
    /// "add-stage [WHEEL PINION]" (a world's script, "build add-stage" or "build add-stage 72 18"): with a windmill or wheel selected, picks
    /// the sizes in the stage's lists (module-5 teeth, or catalogue ids; the lists' sizes as they are when left out), then presses the
    /// inspector's "Add a stage" by a real click, once, and waits until the stage is in.
    /// </summary>
    private ScriptedInput.Step AddStageStep(string[] w)
    {
        if (_stageFor is null)
        {
            if (_selectedId is not { } id || !_session.Document.Parts.TryGetValue(id, out var part) || !StageSlots.Takes(part))
            { GD.Print($"[BuildMode] add-stage: select a windmill or a wheel first (selected: {_selectedId ?? "none"})"); return ScriptedInput.Step.Next; }
            if (w.Length > 2)
            {
                string Size(string s) => s.All(char.IsDigit) ? $"involute-gear-m5-{s}" : s;
                string a = Size(w[1]), b = Size(w[2]);
                if (_palette.All(q => q.Id != a) || _palette.All(q => q.Id != b)) { GD.Print($"[BuildMode] add-stage: no gear {(_palette.All(q => q.Id != a) ? a : b)} in the catalogue"); return ScriptedInput.Step.Next; }
                (_stageWheel, _stagePinion) = (a, b);   // as picking them in the stage's lists does
                RebuildInspector();
            }
            _stageFor = id;
            _stageBefore = _stagesAdded;
            _stageTries = 0;
        }
        if (_stagesAdded > _stageBefore) { _stageFor = null; return ScriptedInput.Step.Next; }
        if (++_stageTries > 60) { GD.Print($"[BuildMode] add-stage: the button didn't add a stage on {_stageFor}"); _stageFor = null; return ScriptedInput.Step.Next; }
        var button = Descendants(_inspector).OfType<Button>().FirstOrDefault(b => b.HasMeta("add_stage") && b.GetMeta("add_stage").AsString() == _stageFor);
        if (button is null || button.Size.X < 1) return ScriptedInput.Step.Again;   // laid out first
        if (_stageTries % 10 == 1) { _inspector.GetParent<ScrollContainer>().EnsureControlVisible(button); return ScriptedInput.Step.Again; }   // scrolled to it, as a person would
        if (_stageTries % 10 != 3) return ScriptedInput.Step.Again;   // then a click, every 10 frames till it takes
        var at = GetViewport().GetScreenTransform() * button.GetGlobalRect().GetCenter();
        _inputScript!.Mouse(at);
        _inputScript.Mouse(at, MouseButton.Left, true);
        _inputScript.Mouse(at, MouseButton.Left, false);
        return ScriptedInput.Step.Again;
    }

    private string? _stageFor;
    private int _stageBefore, _stageTries;
}
