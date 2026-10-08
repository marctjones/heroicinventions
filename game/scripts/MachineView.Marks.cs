using Godot;

namespace HeroicInventions;

/// <summary>
/// Turning you can see (#172): a plain disc or smooth shaft at speed reads as standing still, so every wheel, disc, drum,
/// pulley, gear, shaft and rotor carries a mark painted on its own surface (<see cref="Skins.MarkTurning"/>), a stripe that
/// turns with it. Above 15 turns a second (900 rpm), where a stripe aliases at 60 frames a second, the stripe gives way to a
/// blur ring whose opacity grows with speed. The speed is the part's own: a body's angular velocity about its axle, a rotor's or
/// jet wheel's rpm from the sim. Each mark updates in its own node's process step, so nothing here touches the draw dispatch.
/// </summary>
public partial class MachineView
{
    private sealed partial class TurnMarkDriver : Node
    {
        public readonly List<(Func<double> Turns, Skins.TurnMark Mark)> Marks = [];

        /// <summary>Where each wheel's mark points now, in degrees about its axle (HEROIC_MARKS_REPORT=1 prints them, to check frames against the sim).</summary>
        public readonly List<(string Name, Func<double> Degrees)> Angles = [];
        private readonly bool _report = OS.GetEnvironment("HEROIC_MARKS_REPORT") == "1";
        private int _frame;
        public Func<double> Clock = () => 0;

        public override void _Process(double delta)
        {
            for (int i = 0; i < Marks.Count; i++)
            {
                var (turns, mark) = Marks[i];
                bool ring = mark.RingAlpha > 0;
                double f = turns();
                mark.Spin(f);
                if (_report && ring != mark.RingAlpha > 0)
                    GD.Print($"[marks] #{i} ring {(mark.RingAlpha > 0 ? "on" : "off")} at t={Clock():0.###} s, {f * 60:0.#} rpm");
            }
            if (_report && ++_frame % 10 == 0)
                GD.Print($"[marks] t={Clock():0.##} " + string.Join(" ", Angles.Select(a => $"{a.Name}={a.Degrees():0.00}"))
                         + " | rpm " + string.Join(" ", Marks.Select(m => $"{m.Turns() * 60:0}")));
        }
    }

    /// <summary>The marks on this view, for checks: how many parts, and what each shows now.</summary>
    internal IReadOnlyList<Skins.TurnMark> TurnMarks => _turnMarks?.Marks.Select(m => m.Mark).ToList() ?? [];
    private TurnMarkDriver? _turnMarks;

    private void BuildTurnMarks()
    {
        _turnMarks = new TurnMarkDriver { Name = "turn-marks" };
        AddChild(_turnMarks);
        _turnMarks.Clock = () => Runtime.Time;
        // every wheel, disc, drum, pulley, gear and screw is a body on an axle along its mesh's own Z
        var bodies = _axles.Select(a => a.Body).Concat(_toCarry.Select(c => c.Wheel)).Distinct();
        foreach (var body in bodies)
        {
            var mesh = body.GetChildren().OfType<MeshInstance3D>().FirstOrDefault(m => m.MaterialOverride is StandardMaterial3D);
            if (mesh is null || Skins.MarkTurning(mesh, Vector3.Back) is not { } mark) continue;
            _turnMarks.Marks.Add((() => body.AngularVelocity.Dot(body.GlobalBasis.Z) / Mathf.Tau, mark));
            // the mark starts along the body's own Y: its angle about the axle, measured from the world's up (or X, for an upright axle)
            _turnMarks.Angles.Add((body.Name, () =>
            {
                var a = body.GlobalBasis.Z.Normalized();
                var e = Mathf.Abs(a.Y) > 0.9f ? Vector3.Right : Vector3.Up;
                e = (e - a * e.Dot(a)).Normalized();
                var d = body.GlobalBasis.Y;
                return Mathf.RadToDeg(Mathf.Atan2(a.Cross(e).Dot(d), e.Dot(d)));
            }));
        }
        // an aeolipile's ball turns about its node's X
        foreach (var (rotor, node) in _rotors)
            if (node.GetChildren().OfType<MeshInstance3D>().FirstOrDefault() is { } ball && Skins.MarkTurning(ball, Vector3.Right) is { } mark)
                _turnMarks.Marks.Add((() => rotor.Rpm / 60, mark));
        // a jet wheel's hub is a cylinder stood on its side: its own Y is the wheel's axle
        foreach (var (wheel, node, _) in _jetWheelViews)
            if (node.GetChildren().OfType<MeshInstance3D>().FirstOrDefault() is { } hub && Skins.MarkTurning(hub, Vector3.Up) is { } mark)
                _turnMarks.Marks.Add((() => wheel.Rpm / 60, mark));
    }
}
