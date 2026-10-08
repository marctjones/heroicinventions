using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Click a part to operate it (#152), in a machine run. What a click does is the table in <see cref="Controls"/>; this file is the
/// pointer side: hover, click, the slider and the right-click list. Everything a person does here goes through <see cref="Operate"/>,
/// so it is logged (#153) and a blueprint's demo operator hands over.
///
/// How a click is told from the other uses of the left button (orbit, and dragging a body, #159): a press and release that
/// stay within <see cref="ClickSlop"/> pixels of each other are a click and operate the part under them; a press that moves
/// further is a drag, whatever it grabbed, and operates nothing. This observer never consumes an event, so the orbit and a
/// drag handler see every press and motion as before.
///
/// Hover: the part under the cursor gets a cyan overlay on its meshes (<see cref="Skins.HoverLine"/>, a per-mesh overlay,
/// never a shared material) and a tooltip naming its click, Shift+click and right-click.
/// Shift+click does the part's second control, right-click lists the controls and every settable field.
/// </summary>
public partial class Main
{
    private const float ClickSlop = 4;

    private Vector2? _hoverPos;
    private Vector2 _clickPressAt, _rightPressAt;
    private double _hoverClock;
    private (MachineView View, string Id)? _hovered;
    private readonly Dictionary<MeshInstance3D, Material?> _hoverOverlays = new();
    private CanvasLayer? _operateLayer;
    private PanelContainer? _tooltip;
    private Label? _tooltipText;
    private PanelContainer? _operatePanel;
    private (MachineView View, string Id)? _panelPart;

    /// <summary>Watches the pointer for hover, clicks and right-clicks. Called first from _UnhandledInput; consumes nothing.</summary>
    private void OperateInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m:
                _hoverPos = m.Position;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed) { _clickPressAt = mb.Position; ClosePanel(); }
                else if (!_joining && mb.Position.DistanceTo(_clickPressAt) < ClickSlop) ClickAt(mb.Position, mb.ShiftPressed);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } mb:
                if (mb.Pressed) _rightPressAt = mb.Position;
                else if (!_joining && mb.Position.DistanceTo(_rightPressAt) < ClickSlop) ContextAt(mb.Position);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                ClosePanel();
                break;
        }
    }

    /// <summary>The part of the current machine drawn at a pixel, null for anything else (another machine in a world is focused by a click, not operated).</summary>
    private (MachineView View, string Id)? OperablePartAt(Vector2 screen) =>
        PartAt(screen) is { } p && p.View == _current ? (p.View, p.PartId) : null;

    private void ClickAt(Vector2 screen, bool second)
    {
        if (OperablePartAt(screen) is not { } part) return;
        var actions = Controls.For(part.View, part.Id);
        if (actions.Count > (second ? 1 : 0))
        {
            var a = actions[second ? 1 : 0];
            Operate(part.View, part.Id, a.Field, a.Value);
            Toast($"{part.Id}: {a.Label.ToLowerInvariant()}");
        }
        if (Controls.Ranges(part.View, part.Id).Count > 0) ShowPanel(part, screen, listAll: false);
    }

    private void ContextAt(Vector2 screen)
    {
        ClosePanel();
        if (OperablePartAt(screen) is { } part && Controls.Fields(part.View, part.Id).Count > 0) ShowPanel(part, screen, listAll: true);
    }

    private void Toast(string text)
    {
        _hudNote.Text = text;
        _hudNote.Visible = true;
    }

    // ------------------------------------------------------------------ hover

    /// <summary>Each frame from _Process: re-reads what is under the cursor ten times a second (parts move) and keeps highlight and tooltip with it.</summary>
    private void OperateHoverTick(double delta)
    {
        if ((_hoverClock += delta) < 0.1) return;
        _hoverClock = 0;
        (MachineView, string)? now = null;
        if (_buildMode is null && _current is not null && IsInstanceValid(_current) && _hoverPos is { } pos && _operatePanel is null
            && GetViewport().GuiGetHoveredControl() is null)
            now = OperablePartAt(pos);
        if (now != _hovered) SetHover(now);
        if (_hovered is { } h && _hoverPos is { } at) ShowTooltip(h, at); else _tooltip?.Hide();
    }

    private void SetHover((MachineView View, string Id)? part)
    {
        foreach (var (mesh, before) in _hoverOverlays)
            if (IsInstanceValid(mesh)) mesh.MaterialOverlay = before;
        _hoverOverlays.Clear();
        _hovered = part;
        if (part is not { } p) return;
        Controls.Remember(p.View, p.Id);   // before anything is switched: the working values its toggles come back to
        // by each mesh's own nearest part_id, so a part drawn inside another's frame (a sluice's gate in its channel) is found too
        var meshes = p.View.Meshes().Where(m => m.IsVisibleInTree() && m.Mesh is not null && PartOf(m) is { } o && o.PartId == p.Id).ToList();
        // glass and water would hull the whole pane; mark the solid parts, and only use them when the part has none
        foreach (var m in meshes.Any(MachineView.IsOpaque) ? meshes.Where(MachineView.IsOpaque) : meshes)
        {
            _hoverOverlays[m] = m.MaterialOverlay;
            m.MaterialOverlay = Skins.HoverLine;
        }
    }

    private void ShowTooltip((MachineView View, string Id) part, Vector2 at)
    {
        if (_tooltip is null)
        {
            EnsureOperateLayer();
            _tooltip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            _tooltipText = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
            _tooltip.AddChild(_tooltipText);
            _operateLayer!.AddChild(_tooltip);
        }
        _tooltipText!.Text = TooltipText(part.View, part.Id);
        _tooltip.Position = (at + new Vector2(18, 18)).Clamp(Vector2.Zero, GetViewport().GetVisibleRect().Size - _tooltip.Size);
        _tooltip.Show();
    }

    private static string TooltipText(MachineView view, string id)
    {
        var actions = Controls.For(view, id);
        var lines = new List<string> { $"{id} ({Controls.KindOf(view, id) ?? "part"})" };
        if (actions.Count > 0) lines.Add($"Click: {actions[0].Label}");
        if (actions.Count > 1) lines.Add($"Shift+click: {actions[1].Label}");
        if (Controls.Fields(view, id).Count > 0) lines.Add("Right-click: all fields");
        else if (actions.Count == 0) lines.Add("(nothing to operate)");
        return string.Join('\n', lines);
    }

    /// <summary>What the hover is on and says, for a scripted check ("hovered" step).</summary>
    private string HoverReport() => _hovered is { } h
        ? $"{h.Id} | {TooltipText(h.View, h.Id).Replace('\n', '|')} | {_hoverOverlays.Count} meshes lit"
        : "nothing";

    // ------------------------------------------------------------------ slider and field list

    private void EnsureOperateLayer()
    {
        if (_operateLayer is not null) return;
        _operateLayer = new CanvasLayer { Layer = 5 };
        AddChild(_operateLayer);
    }

    private void ClosePanel()
    {
        _operatePanel?.QueueFree();
        _operatePanel = null;
        _panelPart = null;
    }

    /// <summary>
    /// The small panel by the part: a slider for each of its range controls (opening, rpm, power), and, with
    /// <paramref name="listAll"/>, its named controls as buttons and a box for every settable field. A slider acts when let go,
    /// so a drag is one logged action, not a hundred.
    /// </summary>
    private void ShowPanel((MachineView View, string Id) part, Vector2 at, bool listAll)
    {
        ClosePanel();
        EnsureOperateLayer();
        var (view, id) = part;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        col.AddChild(new Label { Text = $"{id} ({Controls.KindOf(view, id) ?? "part"})" });

        if (listAll)
            foreach (var a in Controls.For(view, id))
            {
                var action = a;
                var b = new Button { Text = a.Label };
                b.Pressed += () => { Operate(view, id, action.Field, action.Value); ClosePanel(); };
                col.AddChild(b);
            }
        var ranged = new HashSet<string>();
        foreach (var (c, max) in Controls.Ranges(view, id))
        {
            ranged.Add(c.Field);
            col.AddChild(SliderRow(view, id, c, max));
        }
        if (listAll)
        {
            col.AddChild(new HSeparator());
            var grid = new GridContainer { Columns = 2 };
            foreach (string field in Controls.Fields(view, id).Where(f => !ranged.Contains(f)))
            {
                grid.AddChild(new Label { Text = field });
                var box = new LineEdit { Text = F3(Controls.Read(view, id, field)), CustomMinimumSize = new Vector2(90, 0) };
                string f = field;
                box.TextSubmitted += text =>
                {
                    if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                        Operate(view, id, f, v);
                    else Toast($"{id} {f}: {text} is not a number");
                    box.Text = F3(Controls.Read(view, id, f));
                };
                grid.AddChild(box);
            }
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, Math.Min(360, 30 * grid.GetChildCount() / 2 + 8)) };
            scroll.AddChild(grid);
            col.AddChild(scroll);
        }

        var panel = new PanelContainer();
        panel.AddChild(col);
        _operateLayer!.AddChild(panel);
        _operatePanel = panel;
        _panelPart = part;
        var room = GetViewport().GetVisibleRect().Size;
        panel.ResetSize();
        panel.Position = (at + new Vector2(14, 14)).Clamp(Vector2.Zero, (room - panel.Size).Max(Vector2.Zero));
    }

    private Control SliderRow(MachineView view, string id, Controls.Control c, double max)
    {
        double now = Controls.Read(view, id, c.Field);
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = c.Field + (c.Unit == "" ? "" : $" ({c.Unit})"), CustomMinimumSize = new Vector2(110, 0) });
        var slider = new HSlider { MinValue = c.Min, MaxValue = max, Step = (max - c.Min) / 200, Value = Math.Clamp(now, c.Min, max), CustomMinimumSize = new Vector2(140, 0) };
        var value = new Label { Text = F3(now), CustomMinimumSize = new Vector2(60, 0) };
        slider.ValueChanged += v => value.Text = F3(v);
        slider.DragEnded += changed => { if (changed) Operate(view, id, c.Field, slider.Value); };
        row.AddChild(slider);
        row.AddChild(value);
        return row;
    }

    private static string F3(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The panel goes when the pointer has left it well behind (it is a tool by the part, not a window).</summary>
    private void OperatePanelTick()
    {
        if (_operatePanel is null || _hoverPos is not { } at) return;
        if (!new Rect2(_operatePanel.Position, _operatePanel.Size).Grow(120).HasPoint(at)) ClosePanel();
    }

    /// <summary>Scripted steps: "hovered" prints the part under the cursor, its tooltip and the meshes lit; "lasthover" the same without re-reading.</summary>
    private ScriptedInput.Step? ClickStep(string[] w)
    {
        if (w[0] == "controls" && _current is not null)
        {
            // every part and rope with a settable field: its click, Shift+click, and the fields (the table's coverage, for a check)
            var rt = _current.Runtime;
            var ids = rt.Def.Parts.Select(p => p.Id).Concat(rt.Def.Ropes.Select(r => r.Id)).Concat(rt.Def.Sources.Select(x => x.Id)).Concat(rt.Def.Lifts.Select(l => l.Id)).Distinct();
            foreach (string id in ids)
            {
                var fields = Controls.Fields(_current, id);
                if (fields.Count == 0) continue;
                var acts = Controls.For(_current, id);
                GD.Print($"[controls] {_currentName} {id} ({Controls.KindOf(_current, id)}): click={(acts.Count > 0 ? acts[0].Label : "-")} shift={(acts.Count > 1 ? acts[1].Label : "-")} fields={string.Join(',', fields)}");
            }
            return ScriptedInput.Step.Continue;
        }
        if (w[0] != "hovered") return null;
        _hoverClock = 1;   // read the cursor now rather than at the next tick
        OperateHoverTick(0);
        GD.Print($"[hover] {HoverReport()} (cursor {_hoverPos}, over UI: {GetViewport().GuiGetHoveredControl()?.GetPath()})");
        return ScriptedInput.Step.Continue;
    }
}
