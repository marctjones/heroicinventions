using Godot;
using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Building inside a selected room (building phase 5, #254; <see cref="RoomSlots"/>). With an enclosure selected, or a part inside one
/// (after the rock is placed the rock is selected and the bin must still go into the room), a part being placed rides the room's floor:
/// the ray meets the plane y = the room's floor, and inside the room's footprint the ghost stands on it (5 cm steps with grid snap), the
/// floor drawn as a green slab and the ghost seen through the soil and the walls over it. Outside the footprint, or with Alt held, it is the
/// free ghost as before (on the ground, or the top of what the ray hits). A click places the part with the ordinary command; a
/// heat bin's holds, a strip's senses and drives (the `?` references) are filled in by the same edit when the room has exactly one thing
/// to name (<see cref="RoomSlots.Defaults"/>), as one (begin ...) so one undo takes the part and its defaults back. Nothing is refused:
/// the references stay blank when there are several candidates (the log names them), and the inspector's list still offers every part.
/// </summary>
public partial class BuildMode
{
    private string? _room;           // the room the placing part goes into, while it does (set when the entry starts)
    private bool _ghostInRoom;       // whether the ghost stands on that room's floor now (not Alt-freed, not outside its footprint)
    private MeshInstance3D? _roomFloor;

    private static readonly StandardMaterial3D RoomGhostOverlay = new()
    {
        AlbedoColor = new Color(0.4f, 1f, 0.4f, 0.4f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = 1,
    };

    /// <summary>The room the next placing goes into: the selected enclosure, or the one holding the selected part.</summary>
    private void SetRoomForPlacing() => _room = RoomSlots.RoomOf(_session.Document, _selectedId);

    /// <summary>
    /// The ghost on the room's floor where the mouse's ray meets that plane, if that is inside the footprint. False (the free ghost takes
    /// over) when the room is gone, the ray points up, or it meets the plane outside the room.
    /// </summary>
    private bool MoveGhostInRoom(Vector2 screen, string room)
    {
        if (RoomSlots.FloorOf(_session.Document, room) is not { } floor) { _room = null; return false; }
        var (from, dir) = Ray(screen);
        if (dir.Y >= -1e-6f) return false;
        float t = ((float)floor.Y - from.Y) / dir.Y;
        if (t < 0) return false;
        var p = from + dir * t;
        if (!floor.Holds(p.X, p.Z)) return false;
        PutGhostOnFloor(floor, p.X, p.Z);
        return true;
    }

    /// <summary>The ghost set on the floor at (x, z) (snapped as the free ghost is), the floor drawn, the ghost seen through what hides it.</summary>
    private void PutGhostOnFloor(RoomSlots.Floor floor, float x, float z)
    {
        var s = Snap(new Vector3(x, 0, z));
        bool entering = !_ghostInRoom;
        _ghost!.Visible = true;
        _ghost.Position = new Vector3(s.X, (float)floor.Y - _ghostBottom, s.Z);
        _ghostInRoom = true;
        foreach (var g in Descendants(_ghost).OfType<GeometryInstance3D>())
            if (g is not Label3D) g.MaterialOverlay = RoomGhostOverlay;
        DrawRoomFloor(floor);
        if (entering) GD.Print($"[BuildMode] in {floor.Room}: the ghost on its floor, {F(Depth(floor.Y, s.X, s.Z))} m below the ground");
        _status.Text = $"Inside {floor.Room}, on its floor: click to put it there. Alt places it on the surface, Esc cancels";
    }

    /// <summary>How far below the ground at (x, z) a height is (0 on a world with no map).</summary>
    private float Depth(double y, double x, double z) => GroundHeight is { } g ? (float)(g(x, z) - y) : 0;

    /// <summary>The free ghost again (Alt, or off the floor): its overlay and the drawn floor go.</summary>
    private void LeaveRoomFloor()
    {
        if (!_ghostInRoom) return;
        _ghostInRoom = false;
        if (_ghost is not null)
            foreach (var g in Descendants(_ghost).OfType<GeometryInstance3D>())
                if (g is not Label3D) g.MaterialOverlay = null;
        HideRoomFloor();
    }

    /// <summary>The room's floor as a thin green slab, drawn over everything (it is under the soil).</summary>
    private void DrawRoomFloor(RoomSlots.Floor floor)
    {
        HideRoomFloor();
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.3f, 1f, 0.35f, 0.35f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _roomFloor = new MeshInstance3D
        {
            Name = "RoomFloor",
            Mesh = new BoxMesh { Size = new Vector3((float)floor.HalfX * 2, 0.004f, (float)floor.HalfZ * 2) },
            MaterialOverride = mat,
            Position = new Vector3((float)floor.CentreX, (float)floor.Y + 0.002f, (float)floor.CentreZ),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_roomFloor);
    }

    private void HideRoomFloor()
    {
        _roomFloor?.QueueFree();
        _roomFloor = null;
    }

    /// <summary>
    /// A part put down on the room's floor: the command is the ordinary one, with the references the room can fill in set in the same edit
    /// (one (begin ...), one undo). Logs a line for the part and one for each reference: filled, or left blank with the candidates.
    /// </summary>
    private string RoomCommand(string command, string head, string id, string room)
    {
        var doc = _session.Document;
        var plan = RoomSlots.Defaults(doc, room, PartTemplates.Create(head, "probe", new Vec3(0, 0, 0), "bronze"));
        string result = RoomSlots.Command(command, id, plan);
        _pendingRoomLog = [
            $"[BuildMode] placed in {room}: {id} on its floor at {Xyz(_ghost!.Position + new Vector3(0, Lift(EntryKeyOf(head)), 0))}, {F(Depth(_ghost.Position.Y, _ghost.Position.X, _ghost.Position.Z))} m below the ground",
            .. plan.Chosen.Select(c => $"[BuildMode] in {room}: {id} {c.Key} {c.Target} ({RoomWhy(c.Key, c.Target, room)})"),
            .. plan.Open.Select(o => $"[BuildMode] in {room}: {id} {o.Key} stays blank, " + (o.Candidates.Count == 0 ? "nothing in the room it could name" : $"it could name {string.Join(" or ", o.Candidates)}")),
        ];
        return result;
    }

    private static string RoomWhy(string key, string target, string room) =>
        key == "senses" ? target == room ? "the room itself" : "its heat store"
        : key == "holds" ? "the room's only heat store" : key == "drives" ? "the room's only bin without a strip" : "the room's only one";

    private List<string> _pendingRoomLog = [];

    private void FlushRoomLog()
    {
        foreach (var l in _pendingRoomLog) GD.Print(l);
        _pendingRoomLog = [];
    }

    /// <summary>"put X Z" inside a room: the floor if (x, z) is within the selected room's footprint, else the ground (the caller's).</summary>
    private bool PutOnRoomFloor(float x, float z)
    {
        if (_room is not { } room || RoomSlots.FloorOf(_session.Document, room) is not { } floor || !floor.Holds(x, z)) return false;
        PutGhostOnFloor(floor, x, z);
        return true;
    }

    private int _fieldTries, _fieldPhase;

    /// <summary>
    /// "field KEY VALUE" (a world's script, "build field open 1"): with a part selected, the number field of its KEY typed into as a person
    /// does. A real click on "Show all its numbers" first if the list is folded, the row scrolled into view, a real click on its field,
    /// everything in it selected, VALUE typed as key events, Enter. The same (set ID #:KEY V) the field commits. Works for a number and for
    /// an optional number that is not set (#f). It takes a few frames (the inspector is laid out and rebuilt between them).
    /// </summary>
    private ScriptedInput.Step FieldStep(string[] w)
    {
        string key = w[1], text = w[2];
        ScriptedInput.Step End(string why)
        {
            if (why != "") GD.Print($"[BuildMode] field {key}: {why}");
            _fieldTries = _fieldPhase = 0;
            return ScriptedInput.Step.Next;
        }
        if (_selectedId is not { } id || !_session.Document.Parts.ContainsKey(id)) return End("select a part first");
        if (++_fieldTries > 240) return End($"gave up after {_fieldTries} frames (phase {_fieldPhase}) on {id}");
        void ClickOn(Control c)
        {
            var at = GetViewport().GetScreenTransform() * c.GetGlobalRect().GetCenter();
            _inputScript!.Mouse(at);
            _inputScript.Mouse(at, MouseButton.Left, true);
            _inputScript.Mouse(at, MouseButton.Left, false);
        }
        var scroll = _inspector.GetParent<ScrollContainer>();
        if (!_showNumbers)
        {
            var show = Descendants(_inspector).OfType<Button>().FirstOrDefault(b => b.HasMeta("numbers"));
            if (show is null || show.Size.X < 1) return ScriptedInput.Step.Again;
            if (_fieldPhase == 0) { scroll.EnsureControlVisible(show); _fieldPhase = 1; return ScriptedInput.Step.Again; }
            if (_fieldPhase == 1) { ClickOn(show); _fieldPhase = 2; }
            return ScriptedInput.Step.Again;   // the click is handled next frame and the inspector rebuilt
        }
        var row = Descendants(_inspector).OfType<HBoxContainer>().FirstOrDefault(r => r.GetChildCount() > 1 && r.GetChild(0) is Label l && l.Text == key);
        if (row is null) return _fieldTries > 20 ? End($"{id} has no number {key} in its list") : ScriptedInput.Step.Again;
        if (row.GetChild(1) is not LineEdit field) return End($"{id}'s {key} is not a number field (a list or a switch)");
        if (field.Size.X < 1) return ScriptedInput.Step.Again;
        if (_fieldPhase <= 2) { scroll.EnsureControlVisible(field); _fieldPhase = 3; return ScriptedInput.Step.Again; }
        if (_fieldPhase == 3) { ClickOn(field); _fieldPhase = 4; return ScriptedInput.Step.Again; }
        if (!field.HasFocus()) return ScriptedInput.Step.Again;
        field.SelectAll();
        _inputScript!.TypeText(text);
        _inputScript.PressKey("enter");
        return End($"typed {text} into {id}'s field");
    }
}
