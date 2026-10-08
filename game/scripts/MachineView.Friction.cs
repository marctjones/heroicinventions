using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// Friction in the bearings of the wheels and levers Jolt turns (issue #13,
/// part 2). A part with #:bearing-radius carries the sim's
/// <see cref="Bearing"/> (the pendulum's, the water wheel's): each tick the
/// body's spin about its hinge is run through <see cref="Bearing.Slow"/> and
/// the difference given back as a torque, so Jolt turns it a step later
/// slowed by Coulomb friction μ·N·r (which can hold it still, never reverse
/// it) and viscous drag c·ω. N is the part's weight. The engine's own 0.2/s
/// axle damping is switched off for it, so what it loses is what the machine
/// file says. The heat made and the pin's Archard wear read from the same
/// getters as a pendulum's, and are labelled above the part.
/// </summary>
public partial class MachineView
{
    private sealed class AxleFriction
    {
        public required string Id;
        public required RigidBody3D Body;
        public required (Vector3 Pivot, Vector3 Axis) Hinge;
        public required Bearing Bearing;
        public required Label3D Label;
    }

    private readonly List<AxleFriction> _axleFriction = [];

    private void BuildAxleFriction()
    {
        foreach (var (id, bearing) in Runtime.AxleBearings)
        {
            _building = id;
            var part = Runtime.Def.Part(id);
            if (part is null || part.Kind is not ("wheel" or "lever")) continue;
            if (!_bodiesById.TryGetValue(id, out var body) || !_hinges.TryGetValue(body, out var hinge)) continue;
            Undamped(body);   // the bearing is the only thing that slows it
            float reach = part.Kind == "wheel" ? (float)part.Number("radius", 0.3) : (float)part.Number("length", 1) / 2;
            var label = new Label3D
            {
                Position = V(part.At) + new Vector3(0, reach + 0.25f, 0),
                FontSize = 24, OutlineSize = 6, PixelSize = 0.0025f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            };
            AddChild(label);
            _axleFriction.Add(new AxleFriction { Id = id, Body = body, Hinge = hinge, Bearing = bearing, Label = label });
        }
    }

    /// <summary>
    /// Each bearing slows its part by one tick's friction. Runs before the physics step, as the other drives do.
    /// A bearing in a driven gear train (#113) is left to <see cref="FrictionDrivenTrains"/>.
    /// </summary>
    private void FrictionAxles(double dt)
    {
        foreach (var f in _axleFriction)
            if (!_trainOf.ContainsKey(f.Body)) Rub(f, f.Body.AngularVelocity.Dot(f.Hinge.Axis.Normalized()), InertiaOnAxle(f.Body), dt);
    }

    /// <summary>
    /// The bearings in driven gear trains (#113), as the trains are coupled for the
    /// tick, between the angle pull and the mesh exchange: each reads the speed the whole train turns its shaft at
    /// (<see cref="TrainSpeed"/>; not the one its own wheel was braked to in the last
    /// step, which a light shaft overshoots, nor its angle pull) and is clamped
    /// against the inertia of everything geared to it, so dry friction stops the
    /// whole train, and only stops it, and its heat is the train's energy.
    /// </summary>
    private void FrictionDrivenTrains(double dt)
    {
        foreach (var f in _axleFriction)
            if (TrainInertia(f.Body) is { } inertia)
            {
                // as an impulse now, before the mesh exchange (#85): the exchange then shares it out over the
                // train, its efficiency and all, and Jolt steps every gear already on its ratio. Given as a
                // torque through Jolt's step, the light brake shaft alone took it and the next tick's exchange
                // gave it back: a 10-tooth pinion ran 10° behind its gear's gaps (of the 18° that is tooth on tooth).
                double omega = TrainSpeed(f.Body)!.Value;
                double impulse = inertia * (f.Bearing.Slow(omega, inertia, BearingLoad(f), dt) - omega);
                var shaft = _arborMates.GetValueOrDefault(f.Body, []).Prepend(f.Body).ToList();
                double total = shaft.Sum(b => (double)InertiaAbout(b, f.Hinge));
                foreach (var b in shaft)
                    PhysicsServer3D.BodyGetDirectState(b.GetRid())
                        .ApplyTorqueImpulse(f.Hinge.Axis.Normalized() * (float)(impulse * InertiaAbout(b, f.Hinge) / total));
            }
    }

    /// <summary>What presses a bearing's pin: the part's own weight, and any wheels fixed on its arbor.</summary>
    private double BearingLoad(AxleFriction f) =>
        (f.Body.Mass + _arborMates.GetValueOrDefault(f.Body, []).Sum(m => m.Mass)) * Runtime.Outside.Gravity;

    private void Rub(AxleFriction f, double omega, double inertia, double dt)
    {
        var axis = f.Hinge.Axis.Normalized();
        double slowed = f.Bearing.Slow(omega, inertia, BearingLoad(f), dt);
        f.Body.ApplyTorque(axis * (float)(inertia * (slowed - omega) / dt));
    }

    private void DrawAxleFriction()
    {
        foreach (var f in _axleFriction)
        {
            double omega = f.Body.AngularVelocity.Dot(f.Hinge.Axis.Normalized());
            string state = Math.Abs(omega) < 1e-4 ? "at rest" : $"{omega * 60 / Math.Tau:F1} rpm";
            string wear = f.Bearing.Wear > 0 ? $"\npin worn {f.Bearing.Wear * 1000:F2}×10⁻³ mm³" : "";
            f.Label.Text = $"{f.Id}\n{state}\nheat {f.Bearing.Heat:F2} J{wear}";
        }
    }
}
