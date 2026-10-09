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
            var gen = new Generator(part.Id)
            {
                Bank = bank, Efficiency = eff, CutInRpm = cut, RatedRpm = rated, RatedTorque = torque,
                DrivenBy = driven.Length > 0 && driven != "#f" ? driven : PrimeMoverOf(def, on).Name,
            };
            // the prime mover's own shaft speed, so the train between it and the rotor can be read as a ratio (#68's Gear up)
            if (PrimeMoverOf(def, on).Id is { } primeId)
                gen.PrimeOmega = _windmills.TryGetValue(primeId, out var pw) ? () => pw.AngularVelocity
                    : _wheels.TryGetValue(primeId, out var pww) ? () => pww.AngularVelocity
                    : _jetWheels.TryGetValue(primeId, out var pj) ? () => pj.AngularVelocity
                    : _stirlings.TryGetValue(primeId, out var ps) ? () => ps.AngularVelocity : null;
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

    /// <summary>What turns the shaft a generator is on: the part the sim turns, or for a wheel the one it is geared, keyed or belted to, or a rope winding a falling weight on.</summary>
    private (string Name, string? Id) PrimeMoverOf(MachineDef def, string on)
    {
        static string Name(string kind) => kind switch { "windmill" => "wind", "waterwheel" => "water-wheel", "jetwheel" => "steam-jet", "stirling" => "stirling", _ => "shaft" };
        // the wheels joined to this one by arbors, meshes and belts
        var seen = new HashSet<string> { on };
        var queue = new Queue<string>([on]);
        while (queue.Count > 0)
        {
            string here = queue.Dequeue();
            var next = def.Arbors.Where(a => a.Parts.Contains(here)).SelectMany(a => a.Parts)
                .Concat(def.Meshes.Where(m => m.A == here).Select(m => m.B)).Concat(def.Meshes.Where(m => m.B == here).Select(m => m.A))
                .Concat(def.Belts.Where(b => b.A == here).Select(b => b.B)).Concat(def.Belts.Where(b => b.B == here).Select(b => b.A));
            foreach (var n in next) if (seen.Add(n)) queue.Enqueue(n);
        }
        foreach (var id in seen)
            if (def.Part(id) is { Kind: "windmill" or "waterwheel" or "jetwheel" or "stirling" } p) return (Name(p.Kind), id);
        if (def.Ropes.Any(r => r.WindOn is { } drum && seen.Contains(drum))) return ("falling-weight", null);
        return ("shaft", null);
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
        }
        if (_banks.Count > 0)
        {
            _getters["scene.won"] = () => Won ? 1 : 0;
            _getters["scene.call-ready"] = () => _banks.Values.Any(b => b.CanCall(Sun.Time)) ? 1 : 0;   // a call would go out now
        }
    }

    private void PreStepElectrics() { foreach (var d in _simDrives) d.Pre(); }

    private void PostStepElectrics(double dt)
    {
        foreach (var d in _simDrives) d.Post(dt);
        foreach (var b in _banks.Values) b.TryCall(Sun.Time, Sun.SolNumber);
    }
}
