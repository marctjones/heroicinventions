namespace HeroicInventions.Sim.Electrics;

/// <summary>
/// The battery bank (issue #64): found in the cargo, armoured for the landing. It holds charge from empty to its
/// <see cref="Capacity"/> (a scenario number, #60, kept here in joules; 1 Wh is 3,600 J), and keeps a record of how much of
/// it came from each source (wind, water-wheel, falling-weight, a heat engine by type), so the way the game was won can be told.
///
/// It takes charge only between <see cref="MinChargeC"/> (0 °C, below which lithium plating makes a cell unsafe to charge) and
/// <see cref="MaxChargeC"/> (45 °C) of its own temperature (inclusive at both ends), and only until full. Outside that range it keeps
/// what it holds and refuses more; it holds its charge for ever (no discharge model, as the issue says). It cannot be destroyed:
/// impacts, fracture, heat and cold stop it charging while they last and do nothing else, so no experiment is a dead end. It has
/// no hit points to lose, and it is not a rigid body that can break, so nothing reduces its charge but a person resetting it.
///
/// Temperature: the bank's own, read from the zone it sits in: the heat store that is its cells (its core temperature), or an
/// enclosure's air. It is not the enclosure's wall surface: a vault's wall face can be a few kelvin from the cells it surrounds, and
/// the cells are what plate. Both the charging rule and the win read this number.
///
/// The win: <see cref="CanCall"/>. At the relay pass (a window of <see cref="CallMinutes"/> after <see cref="CallHour"/> local
/// solar hours, 03:00 and 10 minutes by default; the easy setting <see cref="CallAnyTime"/> lets the call go at any hour) the call
/// succeeds if the bank is full and between 0 and 45 °C. <see cref="Won"/> latches when it does.
/// </summary>
public sealed class BatteryBank
{
    public const double JoulesPerWattHour = 3600;

    public string Name { get; }
    /// <summary>The heat store or enclosure it sits in (its temperature is read from there).</summary>
    public string InName { get; init; } = "";
    /// <summary>J the bank holds when full.</summary>
    public double Capacity { get; set; }
    /// <summary>J it holds now.</summary>
    public double Charge { get; set; }
    /// <summary>Nominal pack voltage, V: only a readout (amps = watts / volts); there is no circuit.</summary>
    public double Volts { get; set; } = 28;
    public double MinChargeC { get; set; } = 0;
    public double MaxChargeC { get; set; } = 45;

    // the call (scenario numbers)
    public double CallHour { get; set; } = 3;           // local solar hour the relay pass begins
    public double CallMinutes { get; set; } = 10;       // minutes it lasts
    public bool CallAnyTime { get; set; }               // the easy setting (#60)
    public bool Won { get; private set; }
    public double WonAtHour { get; private set; } = -1;   // local solar hour the call went out
    public int WonAtSol { get; private set; }

    /// <summary>Where its temperature is read from, °C; unset, <see cref="AssumedTemperature"/> stands in.</summary>
    public Func<double>? Sensed { get; set; }
    public double AssumedTemperature { get; set; } = 20;

    /// <summary>J charged all told, by what drove the generator that charged it.</summary>
    public Dictionary<string, double> Sources { get; } = [];

    public BatteryBank(string name, double capacityJoules, double chargeJoules = 0)
    {
        if (!(capacityJoules > 0)) throw new ArgumentException("capacity must be above 0");
        if (chargeJoules < 0 || chargeJoules > capacityJoules) throw new ArgumentException("charge must be from 0 to the capacity");
        Name = name; Capacity = capacityJoules; Charge = chargeJoules;
    }

    public double Temperature => Sensed is { } s ? s() : AssumedTemperature;
    public double CapacityWh => Capacity / JoulesPerWattHour;
    public double ChargeWh => Charge / JoulesPerWattHour;
    public double Fraction => Capacity > 0 ? Math.Clamp(Charge / Capacity, 0, 1) : 0;
    public bool Full => Charge >= Capacity * (1 - 1e-12);
    /// <summary>Within the temperatures it can be charged at.</summary>
    public bool InRange { get { double t = Temperature; return t >= MinChargeC && t <= MaxChargeC; } }
    /// <summary>Takes charge now: in range and not full.</summary>
    public bool Accepting => InRange && !Full;
    /// <summary>Full and in range: what the call needs of the bank.</summary>
    public bool Ready => Full && InRange;

    /// <summary>Offers joules from a source; returns what was taken (0 outside the range, nothing past full).</summary>
    public double Offer(double joules, string source)
    {
        if (joules <= 0 || !Accepting) return 0;
        double taken = Math.Min(joules, Capacity - Charge);
        Charge += taken;
        Sources[source] = Sources.GetValueOrDefault(source) + taken;
        return taken;
    }

    /// <summary>Is the relay overhead at this local solar hour: in [CallHour, CallHour + CallMinutes) (wrapping midnight)?</summary>
    public bool InWindow(double solarHour) => (((solarHour - CallHour) % 24) + 24) % 24 * 60 < CallMinutes;

    /// <summary>The win rule: the bank full and in range, at the pass or whenever if the easy setting allows.</summary>
    public bool CanCall(double solarHour) => Ready && (CallAnyTime || InWindow(solarHour));

    /// <summary>Makes the call if it can go; latches the win.</summary>
    public void TryCall(double solarHour, int sol)
    {
        if (Won || !CanCall(solarHour)) return;
        Won = true;
        WonAtHour = solarHour;
        WonAtSol = sol;
    }
}
