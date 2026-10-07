namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// A hot-air envelope (issue #111): a skin of <see cref="Volume"/> m³ open at its mouth, as a sky lantern's is,
/// with a burner under it. It floats in air the way a float floats in water. The air inside is at the ambient
/// pressure (it can leave by the mouth as it swells) but at its own temperature T, so by the ideal gas law
///
///   ρ = P / (R_s T)   inside and outside,   lift = (ρ_out − ρ_in) · g · V,
///
/// and it carries a weight of its <see cref="EnvelopeMass"/> plus its <see cref="BurnerMass"/> (the burner with
/// the fuel it holds; the few grams burned off in a flight are neglected, so the weight is steady). The air
/// inside is not counted: it is what the lift is measured against. It leaves the ground when the lift passes the
/// weight, i.e. ρ_in ≤ ρ_out − m/V. Never, if ρ_out · V is below m: the thinnest air could lift at most the air
/// it displaces, which is why a lantern that floats on Earth stays on the ground on Mars.
///
/// Heat. The burner gives <see cref="BurnerPower"/> W while its <see cref="Fuel"/> (kg of wax, oil or the like,
/// <see cref="FuelEnergy"/> J/kg) lasts; the skin loses <see cref="SkinConductance"/> (UA, W/K) to the air
/// outside; <see cref="HeatInput"/> is whatever else heats it (a mirror, a hearth). Held at the ambient
/// pressure, the air inside is heated at constant pressure, the part of it that swells out the mouth carrying its
/// enthalpy away, so what warms is the air that is in there now plus the skin:
///
///   C(T) dT/dt = Q − UA (T − T_out),   C(T) = ρ_in(T) · V · c_p + m_envelope · c_skin.
///
/// C falls as the air thins (about 6% over the heating of a lantern); it is integrated, not frozen. With no
/// loss it has a closed form, Q t = (P V c_p / R_s) ln(T / T₀) + m c_skin (T − T₀), kelvin.
/// </summary>
public sealed class Envelope(string name, double volume, double envelopeMass, double burnerMass, double burnerPower, double fuel) : IHeated
{
    /// <summary>J/(kg·K), air at constant pressure.</summary>
    public const double AirCp = 1005;
    /// <summary>J/(kg·K), paper or cloth: what the skin itself takes to warm.</summary>
    public const double SkinSpecificHeat = 1400;
    /// <summary>J/kg of candle wax or lamp oil, burned.</summary>
    public const double DefaultFuelEnergy = 4.0e7;
    /// <summary>s: the longest step the heating is integrated in.</summary>
    private const double MaxStep = 0.05;

    public string Name { get; } = name;
    public double Volume { get; } = volume;                          // m³
    public double EnvelopeMass { get; } = envelopeMass;              // kg of skin
    public double BurnerMass { get; } = burnerMass;                  // kg of burner with its fuel
    public double BurnerPower { get; set; } = burnerPower;           // W while it burns
    public double InitialFuel { get; } = fuel;                       // kg it was lit with
    public double Fuel { get; private set; } = fuel;                 // kg of fuel left
    public double FuelEnergy { get; set; } = DefaultFuelEnergy;      // J/kg
    public double SkinConductance { get; set; }                      // W/K (UA) from the air inside to the air outside
    public double HeatInput { get; set; }                            // W from mirrors and fires
    public Zone Zone { get; set; } = new();

    /// <summary>m tall as a body: a round skin standing on its mouth. Default 1.2 × the cube root of its volume.</summary>
    public double Height { get; set; } = DefaultHeight(volume);
    public static double DefaultHeight(double volume) => 1.2 * Math.Cbrt(volume);
    /// <summary>m, the radius of a cylinder of this volume and height.</summary>
    public double Radius => Math.Sqrt(Volume / (Math.PI * Height));

    /// <summary>°C of the air inside. Starts at the air outside.</summary>
    public double Temperature { get; set; }
    /// <summary>W the burner gives now.</summary>
    public double Burning { get; private set; }
    /// <summary>J the burner has given.</summary>
    public double Burned { get; private set; }

    /// <summary>kg that rise with it: skin and burner.</summary>
    public double Mass => EnvelopeMass + BurnerMass;
    public double Weight => Mass * Zone.Gravity;                                       // N
    public double OutsideDensity => Zone.AirDensity;                                   // kg/m³
    public double InsideDensity => Zone.AirDensityAt(Temperature);                     // kg/m³, at the ambient pressure
    /// <summary>N up: (ρ_out − ρ_in) g V. Negative when the air inside is the cooler.</summary>
    public double Lift => (OutsideDensity - InsideDensity) * Zone.Gravity * Volume;
    /// <summary>The most it could ever lift: all the air inside gone, ρ_out g V.</summary>
    public double LiftLimit => OutsideDensity * Zone.Gravity * Volume;
    public bool CanLift => LiftLimit > Weight;
    /// <summary>°C inside at which lift equals weight, ρ_in = ρ_out − m/V; NaN where the air is too thin ever to lift it.</summary>
    public double LiftOffTemperature =>
        CanLift ? Zone.Pressure / (Zone.AirGasConstant * (OutsideDensity - Mass / Volume)) - 273.15 : double.NaN;
    /// <summary>J/K the air inside and the skin take to warm one kelvin, now.</summary>
    public double HeatCapacity => InsideDensity * Volume * AirCp + EnvelopeMass * SkinSpecificHeat;
    public double FuelBurned => InitialFuel - Fuel;
    public bool Lit => Burning > 0;

    private double Rate(double t, double q) => (q - SkinConductance * (t - Zone.Temperature))
        / (Zone.AirDensityAt(t) * Volume * AirCp + EnvelopeMass * SkinSpecificHeat);

    public void Step(double dt)
    {
        int n = Math.Max(1, (int)Math.Ceiling(dt / MaxStep));
        double h = dt / n, burnt = 0;
        for (int i = 0; i < n; i++)
        {
            // the fuel left caps what the burner can give this sub-step
            double burn = Math.Min(Math.Max(0, BurnerPower) * h, Fuel * FuelEnergy);
            double q = burn / h + HeatInput;
            Fuel = Math.Max(0, Fuel - burn / FuelEnergy);
            Burned += burn;
            burnt += burn;
            double k1 = Rate(Temperature, q);
            double k2 = Rate(Temperature + 0.5 * h * k1, q);
            Temperature += h * k2;
        }
        Burning = dt > 0 ? burnt / dt : 0;
    }
}
