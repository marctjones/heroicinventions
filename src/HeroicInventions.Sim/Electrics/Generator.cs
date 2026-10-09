namespace HeroicInventions.Sim.Electrics;

/// <summary>
/// A generator (issue #64): a salvaged cargo motor driven backwards by a turning shaft. It is found, not built, and there is
/// no circuit: the shaft's work, less the losses, goes into a battery bank down a wire that loses nothing.
///
/// Power: what the shaft gives the generator is P_shaft = τ ω, and the bank is charged with
///
///   P = η τ ω,   η = <see cref="Efficiency"/> (a scenario number, default 0.8; the issue's 0.7 to 0.9).
///
/// The torque it loads the shaft with is the only thing that has to be modelled, and it comes from the motor's own physics.
/// A permanent-magnet machine turned at ω has a back-EMF E = K ω. Driven into a battery at a fixed voltage V through its winding
/// resistance R it passes a current I = (K ω − V) / R, and that current is a torque τ = K I on the shaft, so
///
///   τ(ω) = k (ω − ω_cut)   for ω &gt; ω_cut, 0 below,   k = K² / R,   ω_cut = V / K.
///
/// Below the <see cref="CutInRpm"/> (default 1,500 rpm, 157.08 rad/s) the EMF is under the battery's voltage: no current,
/// no charge, no torque. Just above it the torque, and the power, rise in a straight line with the speed. The winding can
/// carry only so much: the torque is held at <see cref="RatedTorque"/> from <see cref="RatedRpm"/> (default 2,500 rpm, 12 N·m)
/// up, so k = RatedTorque / (ω_rated − ω_cut) = 12 / 104.72 = 0.11459 N·m per rad/s. (η is the issue's single number for all
/// the losses between shaft and bank, so the curve is the machine's shape and η its scale.)
///
/// A generator whose bank will not take charge (full, or outside 0 to 45 °C) is an open circuit: no current, so no torque, and the
/// shaft runs free. A shaft geared up to a generator and left open races on to Jolt's spin limit, where the engine clamps it: keep a
/// load on fast shafts.
///
/// It records <see cref="DrivenBy"/>, what drives the shaft (wind, water-wheel, falling-weight, aeolipile, stirling ...), and
/// charges its bank under that name, so a route achievement can tell how the game was won.
///
/// A paused sleep (owner's ruling, 2026-10-09). A generator on a rigid body (an arbor's rotor geared from a windmill) is turned only by
/// the physics engine, and a sleep that pauses the engine would leave it charging nothing. So it keeps a record of its
/// <see cref="SettledPower"/>: the mean power it charged at over its last <see cref="SettledBins"/> whole seconds, taken when every one of
/// those seconds is within <see cref="SettledSpread"/> (5%, or 0.5 W) of that mean. Only seconds in which its bank was taking charge
/// count: a tick on an open circuit (bank full, or out of 0 to 45 °C) runs the rotor unloaded, so its speed says nothing of the loaded
/// power, and it starts the count again. While <see cref="Held"/> (<see cref="Machines.MachineRuntime.HoldGenerators"/>) it offers that
/// power × dt to its bank every step, by the bank's own rules: nothing while full or out of range, filed under <see cref="DrivenBy"/>.
/// It is an approximation: the wind's changes over the sleep (map wind, veer) are not followed. The record is saved with the machine
/// (a world saved and loaded goes on as if it had never stopped, #67); one that has never charged ten steady seconds has none, and charges nothing.
/// </summary>
public sealed class Generator
{
    public string Name { get; }
    private BatteryBank? _own;
    /// <summary>The bank it charges: the one it is wired to if a world's wire link joins it to one (#208), else its machine's own (<c>#:charges</c>, or none).</summary>
    public BatteryBank? Bank { get => Wired ?? _own; set => _own = value; }
    /// <summary>The bank in another machine a wire link (#208) has joined it to, or null. The wire is lossless and instant, so the generator charges this bank by the same <see cref="Apply"/> and the same open-circuit rule as its own; the bank files the charge under <see cref="DrivenBy"/> as before.</summary>
    public BatteryBank? Wired { get => _wired; set => _wired = value; }
    [NonSerialized] private BatteryBank? _wired;   // not state of this machine: the other machine saves its own bank (RuntimeState skips it)
    /// <summary>
    /// What turns it: the key the bank files its charge under. Its own machine's prime mover (or <c>#:driven-by</c>); where that walk finds
    /// none ("shaft") and a world's shaft link joins its train to another machine's (GAP 9), that machine's prime mover (<see cref="LinkedPrime"/>).
    /// </summary>
    public string DrivenBy { get => _linkedPrime is { } l && _drivenBy == "shaft" && !DrivenByNamed ? l.Name : _drivenBy; set => _drivenBy = value; }
    private string _drivenBy = "shaft";
    /// <summary>The machine's file names what drives it (<c>#:driven-by</c>): no link renames it.</summary>
    public bool DrivenByNamed { get; set; }
    /// <summary>The prime mover in its own machine that its train reaches (a windmill, water wheel, jet wheel, Stirling engine), or null.</summary>
    public PrimeMover? Prime { get => _prime; set => _prime = value; }
    [NonSerialized] private PrimeMover? _prime;
    /// <summary>The prime mover of another machine that a world's shaft link joins its train to (GAP 9), or null; set and cleared by <see cref="Machines.WorldLinks"/>.</summary>
    public PrimeMover? LinkedPrime { get => _linkedPrime; set => _linkedPrime = value; }
    [NonSerialized] private PrimeMover? _linkedPrime;
    /// <summary>Its prime mover, in its own machine or across a shaft link.</summary>
    public PrimeMover? Mover => _prime ?? _linkedPrime;
    /// <summary>The shaft speed of the prime mover (rad/s) where the sim turns it (a windmill, water wheel, jet wheel, Stirling engine), else null.</summary>
    public Func<double>? PrimeOmega => Mover?.Omega;
    /// <summary>The speed ratio the train gives: the rotor's speed over the prime mover's (1 for a generator on the prime mover itself); null where the prime mover is not turned by the sim or is still.</summary>
    public double? TrainRatio => PrimeOmega is { } p && Math.Abs(p()) > 1e-9 ? Omega / Math.Abs(p()) : null;
    public double Efficiency { get; set; } = 0.8;
    public double CutInRpm { get; set; } = 1500;
    public double RatedRpm { get; set; } = 2500;
    public double RatedTorque { get; set; } = 12;     // N·m

    public Generator(string name) => Name = name;

    public double CutInOmega => CutInRpm * 2 * Math.PI / 60;
    public double RatedOmega => RatedRpm * 2 * Math.PI / 60;
    /// <summary>k, N·m per rad/s: how steeply the torque rises above the cut-in.</summary>
    public double Stiffness => RatedOmega > CutInOmega ? RatedTorque / (RatedOmega - CutInOmega) : double.PositiveInfinity;

    /// <summary>The torque the generator loads the shaft with at ω (rad/s, either sense), with a bank that takes charge.</summary>
    public double TorqueAt(double omega)
    {
        double w = Math.Abs(omega);
        if (w <= CutInOmega) return 0;
        return Math.Min(RatedTorque, Stiffness * (w - CutInOmega));
    }

    /// <summary>The speed at which the shaft first loads: the generator's cut-in, rad/s.</summary>
    public bool Charging(double omega) => TorqueAt(omega) > 0 && Bank is { Accepting: true };

    /// <summary>The ratio a prime mover turning at <paramref name="primeRpm"/> must be geared up by to reach the cut-in.</summary>
    public double RatioForCutIn(double primeRpm) => primeRpm > 0 ? CutInRpm / primeRpm : double.PositiveInfinity;

    // ---- what it did on the last tick ----
    public double Omega { get; private set; }          // rad/s of the rotor
    public double Torque { get; private set; }         // N·m it loaded the shaft with
    public double ShaftPower { get; private set; }     // W taken from the shaft, τ ω
    public double Power { get; private set; }          // W of charge offered to the bank, η τ ω
    public double Delivered { get; private set; }      // W the bank accepted
    public double Taken { get; private set; }          // J taken from the shaft all told
    public double Generated { get; private set; }      // J offered to the bank all told
    public double Rpm => Omega * 60 / (2 * Math.PI);

    /// <summary>
    /// The torque to load the shaft with this tick, from the rotor's speed ω; zero if the bank will not take charge.
    /// <paramref name="most"/> caps it, so a rotor is never turned back (the view passes what would stop it within the tick).
    /// </summary>
    public double LoadTorque(double omega, double most = double.PositiveInfinity)
    {
        if (Bank is not { Accepting: true }) return 0;
        return Math.Min(TorqueAt(omega), most);
    }

    /// <summary>Records one tick of <paramref name="dt"/> seconds with <paramref name="torque"/> N·m on the shaft at ω, and charges the bank.</summary>
    public void Apply(double omega, double torque, double dt)
    {
        bool closed = Bank is { Accepting: true };      // the circuit this tick ran under (LoadTorque read the same)
        Omega = Math.Abs(omega);
        Torque = torque;
        ShaftPower = torque * Omega;
        Power = Efficiency * ShaftPower;
        Taken += ShaftPower * dt;
        Generated += Power * dt;
        double accepted = Bank is { } bank && Power > 0 ? bank.Offer(Power * dt, DrivenBy) : 0;
        Delivered = dt > 0 ? accepted / dt : 0;
        Record(Power, dt, closed);
    }

    // ---- the settled-power record, and the hold a paused sleep puts on it ----
    /// <summary>Whole seconds of charging the settled power is the mean of.</summary>
    public const int SettledBins = 10;
    /// <summary>How far each second's mean may be from the mean of all of them for the power to count as settled: 5%, or <see cref="SettledFloor"/> W.</summary>
    public const double SettledSpread = 0.05, SettledFloor = 0.5;
    private double[] _bins = new double[SettledBins];   // W, the mean of each closed second, the oldest overwritten
    private int _binsFilled, _binNext;
    private double _binEnergy, _binTime, _clock;
    private double _settledPower = double.NaN, _settledAt = double.NaN;
    [NonSerialized] private bool _held, _heldEstimated;   // the sleep's, not the machine's: a resumed sleep holds it again
    [NonSerialized] private double _heldPower;

    /// <summary>W: the last settled power it charged at (see the class notes), or NaN if it has not settled in this session.</summary>
    public double SettledPower => _settledPower;
    /// <summary>Seconds of its own running since its power last settled (NaN if never).</summary>
    public double SettledAgo => _clock - _settledAt;
    /// <summary>True while a paused sleep holds it at its settled power.</summary>
    public bool Held => _held;
    /// <summary>W it is held at: its settled power, or the estimate if it had none; 0 when not held.</summary>
    public double HeldPower => _held ? _heldPower : 0;
    /// <summary>True while held at an estimate (<see cref="EstimatePower"/>) because it had no settled power.</summary>
    public bool HeldEstimated => _held && _heldEstimated;

    /// <summary>
    /// A paused sleep begins: it will charge at its last settled power; one that has none (it has never charged ten steady seconds, say into a
    /// bank that was cold) at the estimate of <see cref="EstimatePower"/> (owner's ruling, 2026-10-09), or nothing if there is none. Returns the watts held.
    /// </summary>
    public double Hold()
    {
        _held = true;
        _heldEstimated = double.IsNaN(_settledPower);
        double estimate = _heldEstimated ? EstimatePower() : double.NaN;
        _heldPower = !_heldEstimated ? _settledPower : estimate > 0 ? estimate : 0;
        return _heldPower;
    }

    /// <summary>
    /// W it would charge at once loaded, from its machine's present state, by the torque balance the sim steps to. Through a train of speed
    /// ratio G = ω_rotor / ω_prime and efficiency η_t, the prime mover feels the generator's torque as G τ_g(G ω) / η_t; where the prime mover
    /// has a torque curve (a windmill's sails in the present wind, less their own load) the operating point is where they meet,
    ///   τ_p(ω) = G τ_g(G ω) / η_t,   solved for ω by bisection between rest and the free speed,
    /// and elsewhere it is the prime mover's present speed. The charge is then η τ_g(G ω) G ω. Bearing friction is left out. NaN where
    /// no prime mover is known (<see cref="Mover"/>).
    /// </summary>
    public double EstimatePower()
    {
        if (Mover is not { Ratio: > 0 } m) return double.NaN;
        double g = m.Ratio, eta = m.Eta is > 0 and <= 1 ? m.Eta : 1, omega;
        if (m.Torque is { } prime)
        {
            double hi = 1;
            for (int i = 0; i < 60 && prime(hi) > 0; i++) hi *= 2;              // past the free speed
            double lo = 0;
            if (!(prime(lo) > 0)) return 0;                                       // no wind: nothing turns it
            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (prime(mid) - g * TorqueAt(g * mid) / eta > 0) lo = mid; else hi = mid;
            }
            omega = 0.5 * (lo + hi);
        }
        else omega = Math.Abs(m.Omega?.Invoke() ?? 0);
        double w = g * omega;
        return Efficiency * TorqueAt(w) * w;
    }

    /// <summary>The sleep is over: back to charging from the shaft. The record goes on from where it was (the paused engine kept its bodies as they were).</summary>
    public void Release() => _held = false;

    /// <summary>One step of a paused sleep: offers the held power × dt to the bank, which takes it by its own rules (nothing full or out of 0 to 45 °C).</summary>
    public void ChargeHeld(double dt)
    {
        Power = Bank is { Accepting: true } ? _heldPower : 0;     // an open circuit gives nothing
        Generated += Power * dt;
        double accepted = Bank is { } bank && Power > 0 ? bank.Offer(Power * dt, DrivenBy) : 0;
        Delivered = dt > 0 ? accepted / dt : 0;
    }

    /// <summary>Adds one tick to the record; <paramref name="closed"/>: the bank was taking charge. An open-circuit tick starts the count again.</summary>
    private void Record(double power, double dt, bool closed)
    {
        _clock += dt;
        if (!closed) { _binsFilled = 0; _binEnergy = 0; _binTime = 0; return; }
        _binEnergy += power * dt;
        _binTime += dt;
        if (_binTime < 1 - 1e-9) return;
        _bins[_binNext] = _binEnergy / _binTime;
        _binNext = (_binNext + 1) % SettledBins;
        _binsFilled = Math.Min(_binsFilled + 1, SettledBins);
        _binEnergy = 0; _binTime = 0;
        if (_binsFilled < SettledBins) return;
        double mean = 0;
        foreach (double b in _bins) mean += b;
        mean /= SettledBins;
        double band = Math.Max(SettledSpread * Math.Abs(mean), SettledFloor);
        foreach (double b in _bins) if (Math.Abs(b - mean) > band) return;
        _settledPower = mean;
        _settledAt = _clock;
    }

    /// <summary>One tick on a shaft at ω: the torque it loads with, applied and charged; returns the torque.</summary>
    public double Step(double omega, double dt, double most = double.PositiveInfinity)
    {
        double torque = LoadTorque(omega, most);
        Apply(omega, torque, dt);
        return torque;
    }
}
