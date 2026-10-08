using Godot;
using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// The found electrics you can see (issue #64).
/// <list type="bullet">
/// <item>The battery bank is an armoured case with a fill column in it that rises with the charge, drawn on both faces so it is
/// read from either side. The column's colour is the bank's state: green while it takes charge, blue-grey when the cold stops it,
/// red when the heat does, amber when it is full but out of range, and cyan-white (with a faint glow, a state cue as
/// art-direction 12.6 allows) when it is full and in range, ready to call. A label gives Wh, per cent, its own temperature and
/// what it is doing; once the call has gone out a column of light stands over it and the label says so.</item>
/// <item>A generator is a motor can on the shaft's axle with a dial on its face: a needle sweeps from nothing to the current it
/// would give at its rated torque, an amp-like readout, and a stripe on its end cap turns with the rotor. A wheel it is on is loaded
/// as a millstone is: the torque the sim says it takes is put on the body, never more than would stop it in a tick.</item>
/// </list>
/// Physics is the sim's; the look is for legibility (the case, the fill and the dial are drawn larger than a real pack's).
/// </summary>
public partial class MachineView
{
    private sealed record BankView(BatteryBank Bank, MeshInstance3D Fill, StandardMaterial3D FillMat, float Height, Label3D Label, MeshInstance3D Beacon, StandardMaterial3D BeaconMat);
    private sealed record GeneratorView(Generator Gen, Node3D Needle, MeshInstance3D Cap, Label3D Label, Vector3 Axis, double RatedAmps);
    private sealed class GeneratorLoad { public required Generator Gen; public required RigidBody3D Body; public required Vector3 Axis; }

    private readonly List<BankView> _bankViews = [];
    private readonly List<GeneratorView> _generatorViews = [];
    private readonly List<GeneratorLoad> _generatorLoads = [];
    private double _generatorSeen;
    private float _capAngle;

    private static readonly Color BankGreen = new(0.25f, 0.8f, 0.35f), BankCold = new(0.45f, 0.58f, 0.8f), BankHot = new(0.9f, 0.3f, 0.2f),
                                   BankAmber = new(0.95f, 0.7f, 0.15f), BankReady = new(0.55f, 0.95f, 1f);

    private void BuildElectrics()
    {
        foreach (var (id, bank) in Runtime.Banks)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var at = V(part.At);
            const float w = 0.34f, h = 0.26f, d = 0.2f;
            var armour = Shapes.Mat(new Color(0.16f, 0.17f, 0.2f), metallic: 0.5f, roughness: 0.5f);
            var caseBox = Shapes.Box(new Vector3(w, h, d), armour);
            caseBox.Position = at + new Vector3(0, h / 2, 0);
            AddChild(caseBox);
            foreach (float x in new[] { -0.09f, 0.09f })   // the terminals
            {
                var post = Shapes.Cylinder(0.018f, 0.04f, Shapes.Mat(Shapes.Copper, metallic: 0.7f, roughness: 0.4f));
                post.Position = at + new Vector3(x, h + 0.02f, 0);
                AddChild(post);
            }
            // the fill: a column through the case, a hair proud of both faces, rising from the bottom
            float fh = h * 0.8f;
            var fillMat = Shapes.Mat(BankGreen, roughness: 0.4f, outline: false);
            var fill = Shapes.Box(new Vector3(w * 0.72f, fh, d * 1.04f), fillMat);
            AddChild(fill);
            fill.SetMeta("bank_base", at + new Vector3(0, (h - fh) / 2, 0));
            var beaconMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                                                     AlbedoColor = new Color(0.6f, 1f, 1f, 0.45f), NoDepthTest = false };
            var beacon = Shapes.Cylinder(0.035f, 3f, beaconMat);
            beacon.Position = at + new Vector3(0, h + 1.5f, 0);
            beacon.Visible = false;
            AddChild(beacon);
            var label = new Label3D
            {
                FontSize = 22, OutlineSize = 6, PixelSize = 0.0035f, NoDepthTest = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Position = at + new Vector3(0, h + 0.2f, 0),
            };
            AddChild(label);
            _bankViews.Add(new BankView(bank, fill, fillMat, fh, label, beacon, beaconMat));
        }
        foreach (var (id, gen) in Runtime.Generators)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var at = V(part.At);
            string on = Runtime.GeneratorShaft(id);
            RigidBody3D? body = _bodiesById.GetValueOrDefault(on);
            var axis = body is not null && _hinges.TryGetValue(body, out var hinge) ? hinge.Axis.Normalized() : Vector3.Back;
            if (body is not null && _hinges.ContainsKey(body))
            {
                Undamped(body);   // the generator is its drag: nothing else slows the rotor
                _generatorLoads.Add(new GeneratorLoad { Gen = gen, Body = body, Axis = axis });
            }
            var basis = new Basis(new Quaternion(Vector3.Up, axis));   // Y of the meshes along the axle
            // the motor can, an iron cylinder along the axle, its face toward -axis (where the dial is read)
            var can = Shapes.Cylinder(0.11f, 0.18f, Surface(part.Material));
            can.Position = at; can.Basis = basis;
            AddChild(can);
            var capMat = Shapes.Mat(new Color(0.9f, 0.9f, 0.92f), metallic: 0.3f, roughness: 0.5f);
            var cap = Shapes.Box(new Vector3(0.012f, 0.1f, 0.012f), capMat);   // the stripe that turns with the rotor, on the -axis end
            AddChild(cap);
            // the dial: a plate on the -axis face with a needle from nothing to the current at rated torque
            var plate = Shapes.Cylinder(0.085f, 0.008f, Shapes.Mat(new Color(0.93f, 0.93f, 0.88f), roughness: 0.7f));
            plate.Position = at - axis * 0.094f; plate.Basis = basis;
            AddChild(plate);
            var needleRoot = new Node3D { Position = at - axis * 0.1f, Basis = basis };
            AddChild(needleRoot);
            var needle = Shapes.Box(new Vector3(0.01f, 0.012f, 0.075f), Shapes.Mat(new Color(0.8f, 0.1f, 0.1f), roughness: 0.4f));
            needle.Position = new Vector3(0, 0, 0.034f);
            needleRoot.AddChild(needle);
            var label = new Label3D
            {
                FontSize = 22, OutlineSize = 6, PixelSize = 0.0035f, NoDepthTest = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Position = at + new Vector3(0, 0.3f, 0),
            };
            AddChild(label);
            double volts = gen.Bank?.Volts ?? 28;
            double ratedWatts = gen.Efficiency * gen.RatedTorque * gen.RatedOmega;
            _generatorViews.Add(new GeneratorView(gen, needleRoot, cap, label, axis, ratedWatts / volts));
            cap.SetMeta("gen_at", at - axis * 0.1f);
        }
        _building = null;
    }

    /// <summary>The wheels a generator is on take its load, as a millstone's stones do: never more than stops the rotor within the tick.</summary>
    private void LoadGenerators(double dt)
    {
        foreach (var g in _generatorLoads)
        {
            float spin = g.Body.AngularVelocity.Dot(g.Axis);
            var state = PhysicsServer3D.BodyGetDirectState(g.Body.GetRid());
            float inertia = g.Axis.Dot(state.InverseInertiaTensor.Inverse() * g.Axis);
            double torque = g.Gen.Step(Math.Abs(spin), dt, inertia * Math.Abs(spin) / dt);
            g.Body.ApplyTorque(-Mathf.Sign(spin) * (float)torque * g.Axis);
        }
    }

    private static string BankState(BatteryBank b) =>
        b.Won ? "the call went out" : b.Ready ? "full and warm: ready to call"
        : b.Full ? $"full, but {b.Temperature:0.#} °C is outside {b.MinChargeC:0} to {b.MaxChargeC:0} °C"
        : b.Temperature < b.MinChargeC ? "too cold to charge" : b.Temperature > b.MaxChargeC ? "too hot to charge" : "taking charge";

    private void DrawElectrics()
    {
        foreach (var v in _bankViews)
        {
            var b = v.Bank;
            float frac = Mathf.Max((float)b.Fraction, 0.005f);
            var baseAt = (Vector3)v.Fill.GetMeta("bank_base");
            v.Fill.Scale = new Vector3(1, frac, 1);
            v.Fill.Position = baseAt + new Vector3(0, v.Height * frac / 2, 0);
            Color c = b.Ready ? BankReady : b.Full ? BankAmber : b.Temperature < b.MinChargeC ? BankCold : b.Temperature > b.MaxChargeC ? BankHot : BankGreen;
            v.FillMat.AlbedoColor = c;
            v.FillMat.EmissionEnabled = b.Ready;
            v.FillMat.Emission = c; v.FillMat.EmissionEnergyMultiplier = 0.6f;
            v.Beacon.Visible = b.Won;
            v.Label.Text = $"{b.Name}: {b.ChargeWh:0.0} / {b.CapacityWh:0.0} Wh ({b.Fraction * 100:0}%) · {b.Temperature:0.0} °C\n{BankState(b)}"
                         + (b.Won ? $" (sol {b.WonAtSol}, {HoursText(b.WonAtHour)})" : b.CallAnyTime ? " · the call may go at any hour"
                            : b.InWindow(Runtime.Sun.Time) ? " · relay pass open" : $" · pass at {HoursText(b.CallHour)}");
        }
        double now = Runtime.Time, dt = now - _generatorSeen;
        _generatorSeen = now;
        foreach (var v in _generatorViews)
        {
            var g = v.Gen;
            // the needle sweeps 270 degrees over the rated current; the stripe turns with the rotor
            double amps = g.Bank is { } b ? g.Delivered / b.Volts : 0;
            float sweep = Mathf.DegToRad((float)(Math.Clamp(amps / v.RatedAmps, 0, 1) * 270 - 135));
            v.Needle.Basis = new Basis(new Quaternion(Vector3.Up, v.Axis)) * new Basis(Vector3.Up, sweep);
            _capAngle += (float)(g.Omega * Math.Max(dt, 0));
            var capBasis = new Basis(new Quaternion(Vector3.Up, v.Axis)) * new Basis(Vector3.Up, _capAngle);
            v.Cap.Basis = capBasis;
            v.Cap.Position = (Vector3)v.Cap.GetMeta("gen_at") - v.Axis * 0.012f;
            v.Label.Text = $"{g.Name}: {g.Rpm:#,0} rpm · {g.Torque:0.00} N·m\n{g.Delivered:0} W into {g.Bank?.Name} · {amps:0.0} A at {g.Bank?.Volts:0} V"
                         + (g.Rpm < g.CutInRpm ? $"\nunder its {g.CutInRpm:#,0} rpm cut-in: nothing" : "");
        }
    }

    private static string HoursText(double hours)
    {
        int minutes = (int)Math.Round(hours * 60);
        return $"{minutes / 60 % 24:00}:{minutes % 60:00}";
    }
}
