using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Mechanics;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Machines;

/// <summary>
/// The found electrics (issue #64) on a running machine: battery banks, the generators that charge them, and the win.
///
/// A generator on a part the sim turns (a windmill, a water wheel, a jet wheel or a Stirling engine) loads that part itself each tick:
/// before the part steps, its load is the part's own plus the generator's torque at the shaft's speed; after, the bank is charged
/// with η τ ω at the mean speed over the tick. A generator on a wheel (an arbor's rotor, geared up from a prime mover) turns on a
/// rigid body that only the game's engine layer steps, so the view loads the body and charges the bank
/// (<see cref="Generator.Step"/>); a headless run has nothing to turn it.
/// </summary>
public sealed partial class MachineRuntime
{
    private readonly Dictionary<string, BatteryBank> _banks = [];
    private readonly Dictionary<string, Generator> _generators = [];
    private readonly List<SimGeneratorDrive> _simDrives = [];

    /// <summary>Battery banks by id (issue #64).</summary>
    public IReadOnlyDictionary<string, BatteryBank> Banks => _banks;
    /// <summary>Generators by id (issue #64).</summary>
    public IReadOnlyDictionary<string, Generator> Generators => _generators;
    /// <summary>The part id a generator sits on (a wheel for the view to load, or a part the sim turns).</summary>
    public string GeneratorShaft(string generatorId) => Def.Part(generatorId)!.Symbol("on", "");
    /// <summary>The game is won: some bank was full and in range at the relay pass (or at any time, in the easy setting).</summary>
    public bool Won => _banks.Values.Any(b => b.Won);

    /// <summary>A generator on a part the sim turns: how to read its speed and add to its load.</summary>
    private sealed class SimGeneratorDrive(Generator generator, Func<double> omega, Func<double> getLoad, Action<double> setLoad)
    {
        public Generator Generator { get; } = generator;
        private double _own, _omega0, _torque;
        /// <summary>Before the part steps: its load is its own plus the generator's torque at its speed.</summary>
        public void Pre()
        {
            _omega0 = omega();
            _own = getLoad();
            _torque = Generator.LoadTorque(_omega0);
            setLoad(_own + _torque);
        }
        /// <summary>After: the load is the part's own again (so a person's setting, or a live edit, never meets the generator's torque), and the bank is charged.</summary>
        public void Post(double dt)
        {
            setLoad(_own);
            Generator.Apply((_omega0 + omega()) / 2, _torque, dt);
        }
    }

    private void BuildElectrics(MachineDef def)
    {
        foreach (var part in def.Parts.Where(p => p.Kind == "battery-bank"))
        {
            double capacityWh = part.Number("capacity", 4000), chargeWh = part.Number("charge", 0);
            if (!(capacityWh > 0) || chargeWh < 0 || chargeWh > capacityWh)
                throw new MachineFormatException($"battery-bank {part.Id}: #:capacity must be above 0 Wh and #:charge from 0 to the capacity", part.Location);
            capacityWh *= Tuning.BankCapacity;                    // the scenario's capacity multiplier (#60)
            chargeWh = Math.Min(chargeWh, capacityWh);
            // the pass the call goes out on: the scene's own relay pass nearest 03:00 (the pre-dawn pass) and its length, unless the bank names its own;
            // a scene with no weather has no passes, and the bank calls at 03:00 for 10 minutes
            double hour = part.Props.GetValueOrDefault("call-hour") is SNumber ch ? ch.Value
                : def.Weather is { Passes.Count: > 0 } wx ? wx.Passes.OrderBy(h => Math.Min(((h - 3) % 24 + 24) % 24, ((3 - h) % 24 + 24) % 24)).First() : 3;
            double minutes = (part.Props.GetValueOrDefault("call-minutes") is SNumber cm ? cm.Value : def.Weather?.PassMinutes ?? 10) * Tuning.CallWindow;   // x the scenario's call-window multiplier (#60)
            double volts = part.Number("volts", 28);
            if (!(volts > 0) || hour < 0 || hour >= 24 || !(minutes > 0))
                throw new MachineFormatException($"battery-bank {part.Id}: #:volts and #:call-minutes must be above 0 and #:call-hour a local solar hour in [0, 24)", part.Location);
            string zone = part.Symbol("in", "");   // (the bank remembers the name: a thermostat is read as sensing it)
            Func<double> sensed = _heatStores.TryGetValue(zone, out var store) ? () => store.Temperature
                : _enclosures.TryGetValue(zone, out var room) ? () => room.Temperature
                : throw new MachineFormatException($"battery-bank {part.Id} sits in {zone}, which is not a heat-store or an enclosure", part.Location);
            _banks[part.Id] = new BatteryBank(part.Id, capacityWh * BatteryBank.JoulesPerWattHour, chargeWh * BatteryBank.JoulesPerWattHour)
            {
                InName = zone, Volts = volts, CallHour = hour, CallMinutes = minutes, Sensed = sensed,
                CallAnyTime = Tuning.CallAnyTime || part.Props.GetValueOrDefault("call-any-time") is SBool { Value: true },
                MinChargeC = Tuning.BankMinChargeC ?? 0, MaxChargeC = Tuning.BankMaxChargeC ?? 45,   // Advanced (#60)
            };
        }
        foreach (var part in def.Parts.Where(p => p.Kind == "generator"))
        {
            string on = part.Symbol("on", ""), charges = part.Symbol("charges", "");
            // a generator may name no bank (#:charges #f): it is open circuit until a world's wire joins it to a bank in another machine (#208)
            BatteryBank? bank = null;
            if (charges.Length > 0 && charges != "#f" && !_banks.TryGetValue(charges, out bank))
                throw new MachineFormatException($"generator {part.Id} charges {charges}, which is not a battery-bank", part.Location);
            double eff = part.Number("efficiency", 0.8), cut = part.Number("cut-in-rpm", 1500), rated = part.Number("rated-rpm", 2500), torque = part.Number("rated-torque", 12);
            if (!(eff > 0 && eff <= 1) || !(cut > 0) || !(rated > cut) || !(torque > 0))
                throw new MachineFormatException($"generator {part.Id}: #:efficiency must be in (0, 1], #:cut-in-rpm above 0, #:rated-rpm over the cut-in and #:rated-torque above 0", part.Location);
            // the scenario's numbers (#60): the cut-in multiplier (the rated speed is lifted over a cut-in that passes it), and Advanced eta
            cut *= Tuning.GeneratorCutIn;
            if (!(rated > cut)) rated = cut * 1.5;
            if (Tuning.GeneratorEfficiency is { } tunedEta) eff = tunedEta;
            string driven = part.Symbol("driven-by", "");
            bool named = driven.Length > 0 && driven != "#f";
            // the prime mover its train reaches, its speed (so the train can be read as a ratio, #68's Gear up) and the train's ratio and losses
            var prime = PrimeMoverFrom(on);
            var gen = new Generator(part.Id)
            {
                Bank = bank, Efficiency = eff, CutInRpm = cut, RatedRpm = rated, RatedTorque = torque,
                DrivenBy = named ? driven : prime?.Name ?? (Def.Ropes.Any(r => r.WindOn is { } drum && TrainOf(on).ContainsKey(drum)) ? "falling-weight" : "shaft"),
                DrivenByNamed = named, Prime = prime,
            };
            _generators[part.Id] = gen;
            if (_windmills.TryGetValue(on, out var wm)) _simDrives.Add(new(gen, () => wm.AngularVelocity, () => wm.Load, v => wm.Load = v));
            else if (_wheels.TryGetValue(on, out var ww)) _simDrives.Add(new(gen, () => ww.AngularVelocity, () => ww.Load, v => ww.Load = v));
            else if (_jetWheels.TryGetValue(on, out var jw)) _simDrives.Add(new(gen, () => jw.AngularVelocity, () => jw.Load, v => jw.Load = v));
            else if (_stirlings.TryGetValue(on, out var se)) _simDrives.Add(new(gen, () => se.AngularVelocity, () => se.Load, v => se.Load = v));
            else if (def.Part(on) is not { Kind: "wheel" })
                throw new MachineFormatException($"generator {part.Id} is on {on}, which is not a wheel, windmill, water wheel, jet wheel or Stirling engine", part.Location);
        }
        RegisterElectricsFields();
    }

    /// <summary>
    /// The parts joined to <paramref name="start"/> by arbors, meshes and belts, each with its speed over the start's (a mesh turns its
    /// partner at teeth / teeth, a belt at radius / radius, an arbor at 1; the sense is dropped) and the share of the power that passes
    /// from it to the start (the meshes' efficiencies multiplied along the way).
    /// </summary>
    public IReadOnlyDictionary<string, (double Ratio, double Eta)> TrainOf(string start)
    {
        var def = Def;
        double Size(string id) => def.Part(id) is { } p ? (p.Number("teeth", 0) is > 0 and var t ? t : p.Number("pitch-radius", 0) is > 0 and var r ? r : p.Number("radius", 0)) : 0;
        double Step(string a, string b) => Size(a) > 0 && Size(b) > 0 ? Size(a) / Size(b) : 1;   // ω_b / ω_a
        var seen = new Dictionary<string, (double Ratio, double Eta)> { [start] = (1, 1) };
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
        {
            string here = queue.Dequeue();
            var (ratio, eta) = seen[here];
            var next = def.Arbors.Where(a => a.Parts.Contains(here)).SelectMany(a => a.Parts).Select(n => (n, 1.0, 1.0))
                .Concat(def.Meshes.Where(m => m.A == here).Select(m => (m.B, Step(here, m.B), m.Efficiency)))
                .Concat(def.Meshes.Where(m => m.B == here).Select(m => (m.A, Step(here, m.A), m.Efficiency)))
                .Concat(def.Belts.Where(b => b.A == here).Select(b => (b.B, Step(here, b.B), 1.0)))
                .Concat(def.Belts.Where(b => b.B == here).Select(b => (b.A, Step(here, b.A), 1.0)));
            foreach (var (n, k, e) in next)
                if (!seen.ContainsKey(n)) { seen[n] = (ratio * k, eta * e); queue.Enqueue(n); }
        }
        return seen;
    }

    /// <summary>
    /// The prime mover a train from <paramref name="on"/> reaches in this machine (a windmill, water wheel, jet wheel or Stirling engine), with
    /// the train's ratio ω_on / ω_prime and efficiency, or null. A windmill's torque curve comes with it, for a paused sleep's estimate.
    /// </summary>
    public PrimeMover? PrimeMoverFrom(string on)
    {
        foreach (var (id, (ratio, eta)) in TrainOf(on))
        {
            if (!(ratio > 0)) continue;
            if (_windmills.TryGetValue(id, out var mill)) return new("wind", id, () => mill.AngularVelocity, 1 / ratio, eta, w => mill.TorqueAt(w) - mill.Load);
            if (_wheels.TryGetValue(id, out var wheel)) return new("water-wheel", id, () => wheel.AngularVelocity, 1 / ratio, eta, null);
            if (_jetWheels.TryGetValue(id, out var jet)) return new("steam-jet", id, () => jet.AngularVelocity, 1 / ratio, eta, null);
            if (_stirlings.TryGetValue(id, out var se)) return new("stirling", id, () => se.AngularVelocity, 1 / ratio, eta, null);
        }
        return null;
    }

    /// <summary>A bank's <c>from-SOURCE</c> field for a source a generator has come to be filed under (a prime mover across a shaft link, GAP 9).</summary>
    public void ShowSource(BatteryBank bank, string source)
    {
        foreach (var (id, b) in _banks)
            if (b == bank) _getters.TryAdd($"{id}.from-{source}", () => b.Sources.GetValueOrDefault(source) / BatteryBank.JoulesPerWattHour);
    }

    private void RegisterElectricsFields()
    {
        foreach (var (id, b) in _banks)
        {
            _getters[$"{id}.charge"] = () => b.ChargeWh;                       // Wh
            _setters[$"{id}.charge"] = wh => b.Charge = Math.Clamp(wh * BatteryBank.JoulesPerWattHour, 0, b.Capacity);
            _getters[$"{id}.capacity"] = () => b.CapacityWh;                   // Wh (a scenario number, #60)
            _setters[$"{id}.capacity"] = wh => { b.Capacity = Math.Max(1e-6, wh * BatteryBank.JoulesPerWattHour); b.Charge = Math.Min(b.Charge, b.Capacity); };
            _getters[$"{id}.fraction"] = () => b.Fraction;                     // 0 empty … 1 full
            _getters[$"{id}.temperature"] = () => b.Temperature;               // °C, the bank's own
            _getters[$"{id}.accepting"] = () => b.Accepting ? 1 : 0;           // takes charge now: 0 to 45 °C and not full
            _getters[$"{id}.full"] = () => b.Full ? 1 : 0;
            _getters[$"{id}.ready"] = () => b.Ready ? 1 : 0;                   // full and in range
            _getters[$"{id}.in-window"] = () => b.InWindow(Sun.Time) ? 1 : 0; // the relay pass is open now
            _getters[$"{id}.won"] = () => b.Won ? 1 : 0;
            _getters[$"{id}.volts"] = () => b.Volts;
            _getters[$"{id}.call-hour"] = () => b.CallHour;
            _setters[$"{id}.call-hour"] = h => b.CallHour = ((h % 24) + 24) % 24;
            _getters[$"{id}.call-minutes"] = () => b.CallMinutes;
            _setters[$"{id}.call-minutes"] = m => b.CallMinutes = Math.Max(1e-6, m);
            _getters[$"{id}.call-any-time"] = () => b.CallAnyTime ? 1 : 0;
            _setters[$"{id}.call-any-time"] = v => b.CallAnyTime = v != 0;     // the easy setting (#60)
            foreach (var source in _generators.Values.Where(g => g.Bank == b).Select(g => g.DrivenBy).Distinct())
                _getters[$"{id}.from-{source}"] = () => b.Sources.GetValueOrDefault(source) / BatteryBank.JoulesPerWattHour;   // Wh that came from this source
        }
        foreach (var (id, g) in _generators)
        {
            _getters[$"{id}.omega"] = () => g.Omega;                           // rad/s of the rotor
            _getters[$"{id}.rpm"] = () => g.Rpm;
            _getters[$"{id}.torque"] = () => g.Torque;                         // N·m it loads the shaft with
            _getters[$"{id}.shaft-power"] = () => g.ShaftPower;                // W taken from the shaft
            _getters[$"{id}.power"] = () => g.Power;                           // W of charge made, η τ ω
            _getters[$"{id}.delivered"] = () => g.Delivered;                   // W the bank took
            _getters[$"{id}.current"] = () => g.Bank is { } b ? g.Delivered / b.Volts : 0;   // A at the pack's nominal voltage: a readout
            _getters[$"{id}.generated"] = () => g.Generated / BatteryBank.JoulesPerWattHour;   // Wh made all told
            _getters[$"{id}.taken"] = () => g.Taken / BatteryBank.JoulesPerWattHour;           // Wh of shaft work all told
            _getters[$"{id}.stiffness"] = () => g.Stiffness;                   // N·m per rad/s above the cut-in
            _getters[$"{id}.efficiency"] = () => g.Efficiency;
            _setters[$"{id}.efficiency"] = v => g.Efficiency = Math.Clamp(v, 1e-6, 1);
            _getters[$"{id}.cut-in-rpm"] = () => g.CutInRpm;
            _setters[$"{id}.cut-in-rpm"] = v => g.CutInRpm = Math.Max(1e-6, v);
            _getters[$"{id}.charging"] = () => g.Delivered > 0 ? 1 : 0;
            _getters[$"{id}.settled-power"] = () => double.IsNaN(g.SettledPower) ? 0 : g.SettledPower;   // W: the last settled power (0 until it has one)
            _getters[$"{id}.held"] = () => g.Held ? 1 : 0;                 // a paused sleep holds it at its settled power (or the estimate)
            _getters[$"{id}.held-estimated"] = () => g.HeldEstimated ? 1 : 0;   // held at the estimate: it had no settled power
            _getters[$"{id}.held-power"] = () => g.HeldPower;              // W it is held at (0 when not held)
            _getters[$"{id}.estimated-power"] = () => g.EstimatePower() is var e && e > 0 ? e : 0;   // W it would charge at once loaded, from the present state
        }
        if (_banks.Count > 0)
        {
            _getters["scene.won"] = () => Won ? 1 : 0;
            _getters["scene.call-ready"] = () => _banks.Values.Any(b => b.CanCall(Sun.Time)) ? 1 : 0;   // a call would go out now
        }
    }

    /// <summary>
    /// A sleep that pauses the physics engine begins (owner's ruling, 2026-10-09): every generator the sim does not turn itself (one on a
    /// rigid body, which the paused engine leaves still) is held at its last settled power (<see cref="Generator.SettledPower"/>) and charges
    /// its bank at that rate each step, by the bank's own rules, until <see cref="ReleaseGenerators"/>. A generator on a part the sim turns
    /// (a windmill, water wheel, jet wheel or Stirling engine the sim steps through the sleep) keeps its real power and is not held, so
    /// nothing is counted twice. An approximation: the wind's changes over the sleep are not followed. Returns each held generator by id.
    /// </summary>
    public IReadOnlyList<(string Id, Generator Generator, double Watts)> HoldGenerators()
    {
        var simDriven = _simDrives.Select(d => d.Generator).ToHashSet();
        var held = new List<(string, Generator, double)>();
        foreach (var (id, g) in _generators)
            if (!simDriven.Contains(g) && g.Bank is not null) held.Add((id, g, g.Hold()));
        return held;
    }

    /// <summary>The paused sleep is over: the held generators charge from their shafts again.</summary>
    public void ReleaseGenerators() { foreach (var g in _generators.Values) g.Release(); }

    /// <summary>Some generator is held by a paused sleep.</summary>
    public bool GeneratorsHeld => _generators.Values.Any(g => g.Held);

    private void PreStepElectrics() { foreach (var d in _simDrives) d.Pre(); }

    private void PostStepElectrics(double dt)
    {
        foreach (var d in _simDrives) d.Post(dt);
        foreach (var g in _generators.Values) if (g.Held) g.ChargeHeld(dt);   // a paused sleep: the last settled power
        foreach (var b in _banks.Values) b.TryCall(Sun.Time, Sun.SolNumber);
    }
}
