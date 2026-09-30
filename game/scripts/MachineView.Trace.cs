using System.Globalization;
using System.Text;
using Godot;

namespace HeroicInventions;

/// <summary>
/// A trace of a run for the headless test runner (racket/heroic/godothost.rkt):
/// one frame per <c>sample-dt</c> of simulated time, written as an
/// S-expression line, <c>(time (target.field value) ...)</c>, the same frame
/// shape heroic/simhost returns, so rackunit reads engine-side and sim-side
/// runs alike. A frame holds every field the sim core offers (tank levels,
/// boiler temperatures, pump strokes...) and, for every Jolt body, its centre
/// of mass (x y z), velocity (vx vy vz, speed), rotation (rot-x/y/z degrees,
/// and for a body on a hinge its signed angle turned about it, "angle"), spin
/// rate (omega, rad/s) and contacts (bodies touching now, and "hits", how
/// many contacts have begun so far); every rope's tension; and the machine's
/// kinetic, potential and mechanical energy (scene.kinetic ...).
/// </summary>
public partial class MachineView
{
    private StreamWriter? _trace;
    private double _traceDt;
    private long _traceFrames;
    private readonly Dictionary<RigidBody3D, Basis> _startBasis = [];
    private readonly Dictionary<RigidBody3D, int> _hits = [];

    public void StartTrace(string path, double sampleDt)
    {
        _trace = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = false };
        _traceDt = sampleDt;
        foreach (var b in TracedBodies())
        {
            _startBasis[b] = b.GlobalTransform.Basis;
            b.ContactMonitor = true;
            b.MaxContactsReported = Math.Max(b.MaxContactsReported, 8);
            _hits[b] = 0;
            var body = b;
            b.BodyEntered += _ => _hits[body]++;
        }
        WriteTraceFrame();
    }

    public void StopTrace()
    {
        _trace?.Flush();
        _trace?.Dispose();
        _trace = null;
    }

    /// <summary>Called after each step: writes a frame once the sim clock reaches the next sample (by count, so no drift).</summary>
    /// <summary>After a live edit, keeps writing the same trace, so the record runs on across the rebuild.</summary>
    public void TakeTraceFrom(MachineView old)
    {
        (_trace, old._trace) = (old._trace, null);
        _traceDt = old._traceDt;
        _traceFrames = old._traceFrames;
    }

    private void TraceTick(double dt)
    {
        if (_trace is null) return;
        if (Runtime.Time + dt / 2 >= _traceFrames * _traceDt) WriteTraceFrame();
    }

    private IEnumerable<RigidBody3D> TracedBodies() =>
        _freezable.Concat(_axles.Select(a => a.Body)).Distinct();

    private void WriteTraceFrame()
    {
        _traceFrames++;
        var sb = new StringBuilder();
        sb.Append('(').Append(N(Runtime.Time));
        void Add(string key, double v) { if (double.IsFinite(v)) sb.Append(" (").Append(key).Append(' ').Append(N(v)).Append(')'); }

        foreach (var (key, get) in Runtime.FieldGetters)
        {
            double v;
            try { v = get(); } catch { continue; }
            Add(key, v);
        }
        foreach (var b in TracedBodies())
        {
            string id = b.Name;
            var com = b.GlobalTransform * (_comOffset.TryGetValue(b, out var off) ? off : b.CenterOfMass);
            Add($"{id}.x", com.X); Add($"{id}.y", com.Y); Add($"{id}.z", com.Z);
            var v = b.LinearVelocity;
            Add($"{id}.vx", v.X); Add($"{id}.vy", v.Y); Add($"{id}.vz", v.Z); Add($"{id}.speed", v.Length());
            var rot = b.GlobalRotationDegrees;
            Add($"{id}.rot-x", rot.X); Add($"{id}.rot-y", rot.Y); Add($"{id}.rot-z", rot.Z);
            Add($"{id}.omega", b.AngularVelocity.Length());
            if (HingeAngleDegrees(b) is { } turned) Add($"{id}.angle", turned);
            if (b.ContactMonitor) Add($"{id}.contacts", b.GetContactCount());
            if (_hits.TryGetValue(b, out int hits)) Add($"{id}.hits", hits);
            if (_impactRecords.TryGetValue(b, out var struck))
            {
                // strikes so far, and the last one's closing speed (m/s), impulse (N s) and the energy it took (J)
                Add($"{id}.impacts", struck.Count);
                Add($"{id}.impact-speed", struck.Last?.Speed ?? 0);
                Add($"{id}.impact-impulse", struck.Last?.Impulse ?? 0);
                Add($"{id}.impact-energy", struck.Last?.EnergyLost ?? 0);
                Add($"{id}.impact-energy-total", struck.TotalEnergy);
            }
        }
        foreach (var r in _ropes)
        {
            Add($"{r.Spec.Id}.tension", r.Tension);
            Add($"{r.Spec.Id}.released", r.Released ? 1 : 0);
            Add($"{r.Spec.Id}.broken", r.Broken ? 1 : 0);
            if (r.Mu > 0)
            {
                Add($"{r.Spec.Id}.tension-from", r.TensionFrom);
                Add($"{r.Spec.Id}.tension-to", r.TensionTo);
                Add($"{r.Spec.Id}.slip", r.Slip);
                Add($"{r.Spec.Id}.wrap-deg", Mathf.RadToDeg(r.Wrap));
            }
        }
        var e = Energy();
        Add("scene.kinetic", e.KineticJ); Add("scene.potential", e.PotentialJ); Add("scene.mechanical", e.KineticJ + e.PotentialJ);
        sb.Append(')');
        _trace!.WriteLine(sb.ToString());
    }

    /// <summary>How far a hinged body has turned from where it started, in degrees, signed about its hinge; null for a body with no hinge or no recorded start.</summary>
    private double? HingeAngleDegrees(RigidBody3D b)
    {
        if (!_hinges.TryGetValue(b, out var hinge) || !_startBasis.TryGetValue(b, out var start)) return null;
        // the turn from the start pose, signed about the hinge's axis
        var q = (b.GlobalTransform.Basis * start.Inverse()).GetRotationQuaternion();
        float angle = q.GetAngle();
        var axis = angle > 1e-6f ? q.GetAxis() : hinge.Axis;
        return Mathf.RadToDeg(angle) * Mathf.Sign(axis.Dot(hinge.Axis) == 0 ? 1 : axis.Dot(hinge.Axis));
    }

    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
