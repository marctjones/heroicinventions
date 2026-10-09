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
    /// <summary>What turns it: the key the bank files its charge under.</summary>
    public string DrivenBy { get; set; } = "shaft";
    /// <summary>The shaft speed of the prime mover (rad/s) where the sim turns it (a windmill, water wheel, jet wheel, Stirling engine), else null.</summary>
    public Func<double>? PrimeOmega { get; set; }
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
        Omega = Math.Abs(omega);
        Torque = torque;
        ShaftPower = torque * Omega;
        Power = Efficiency * ShaftPower;
        Taken += ShaftPower * dt;
        Generated += Power * dt;
        double accepted = Bank is { } bank && Power > 0 ? bank.Offer(Power * dt, DrivenBy) : 0;
        Delivered = dt > 0 ? accepted / dt : 0;
    }

    /// <summary>One tick on a shaft at ω: the torque it loads with, applied and charged; returns the torque.</summary>
    public double Step(double omega, double dt, double most = double.PositiveInfinity)
    {
        double torque = LoadTorque(omega, most);
        Apply(omega, torque, dt);
        return torque;
    }
}
