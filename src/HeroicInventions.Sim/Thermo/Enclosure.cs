namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// An enclosure (issue #39): a box with its own air, standing in another
/// zone (the planet's open air, or an enclosure round it). Every part inside
/// reads its conditions from here, not from outside: a pump pushes water up
/// its pipe with this air's pressure, a boiler boils where water's vapour
/// pressure reaches it, a fire (#40) breathes its oxygen.
///
/// The air is a mixture of O2, N2, CO2, H2O and Ar, tracked as moles of
/// each (<see cref="Moles"/>): by Dalton's law each gas's partial pressure is
/// nᵢ·R·T/V and they add up to the whole. Its temperature follows
/// C·dT/dt = Q − UA·(T − T_out), C the gas's own heat capacity Σnᵢ·c_v,ᵢ plus
/// the walls' (<see cref="WallHeatCapacity"/>), UA the walls'
/// <see cref="Insulation"/> in W/K, Q whatever heats it (a <see cref="Heater"/>,
/// a hearth, a mirror): with steady heat it settles at T_out + Q/UA with time
/// constant C/UA, solved exactly each step.
///
/// A hole in the wall (<see cref="LeakArea"/>, discharge coefficient
/// <see cref="Cd"/>) passes gas as a compressible orifice, as steam through a
/// safety valve: ṁ = Cd·A·P₀·√(2γ/((γ−1)·R·T₀)·(r^(2/γ) − r^((γ+1)/γ))), from
/// the higher pressure P₀ to the lower, r their ratio but no less than the
/// critical (2/(γ+1))^(γ/(γ−1)), below which the jet is choked at the speed of
/// sound. The gas that stays is held at the walls' temperature as it thins
/// (a slow leak: isothermal), so while choked the pressure falls
/// exponentially, P = P₀·e^(−t/τ), τ = V / (Cd·A·√(γ·R·T)·(2/(γ+1))^((γ+1)/(2(γ−1)))).
/// What leaks out goes into the zone outside if that is an enclosure too;
/// what leaks in comes from it, its mixture at its temperature.
/// </summary>
public sealed class Enclosure : Zone, IHeated
{
    public const double R = Physics.GasConstant;
    public const double DefaultCoefficient = 0.6;

    /// <summary>Molar heat capacity at constant volume, J/(mol·K), of O2, N2, CO2, H2O (vapour), Ar.</summary>
    public static readonly double[] MolarCv = [21.0, 20.8, 28.9, 25.3, 12.47];
    public static readonly double[] MolarMasses = [GasMix.O2MolarMass, GasMix.N2MolarMass, GasMix.CO2MolarMass, GasMix.H2OMolarMass, GasMix.ArMolarMass];

    public Enclosure(string name, double volume, Zone outside, double pressurePa, double temperatureC, GasMix air)
        : base(outside.Planet, temperatureC)
    {
        Name = name;
        Volume = volume;
        Outside = outside;
        _temperature = temperatureC;
        double total = air.Total > 0 ? air.Total : 1;
        double n = pressurePa * volume / (R * Physics.ToKelvin(temperatureC));
        Moles = [n * air.O2 / total, n * air.N2 / total, n * air.CO2 / total, n * air.H2O / total, n * air.Ar / total];
    }

    public string Name { get; }
    public double Volume { get; }                                    // m³
    /// <summary>The zone it stands in: its walls lose heat to it, its hole leaks into it.</summary>
    public Zone Outside { get; set; }
    /// <summary>mol of O2, N2, CO2, H2O and Ar, in that order.</summary>
    public double[] Moles { get; private set; }
    public double TotalMoles => Moles.Sum();

    public double Insulation { get; set; } = 2;                      // W/K through the walls (UA)
    public double WallHeatCapacity { get; set; }                     // J/K of the walls themselves
    public double Heater { get; set; }                               // W, a steady heat source inside
    public double HeatInput { get; set; }                            // W from fires and mirrors, set each step
    private double _leak;
    public double LeakArea { get => _leak; set => _leak = Math.Max(0, value); } // m², 0 sealed
    public double Cd { get; set; } = DefaultCoefficient;

    public double Flow { get; private set; }                         // kg/s out through the hole (negative: in), last step
    public double Lost { get; private set; }                         // kg that has left, net
    public bool Choked { get; private set; }

    public override Planet Planet { get => Outside.Planet; set => Outside.Planet = value; }

    private double _temperature;
    public override double Temperature { get => _temperature; set => _temperature = value; }

    /// <summary>Pa: Σnᵢ·R·T/V. Set, the gas is pumped in or bled out at the same mixture and temperature.</summary>
    public override double Pressure
    {
        get => Moles is null ? 0 : TotalMoles * R * Physics.ToKelvin(Temperature) / Volume;
        set
        {
            if (Moles is null) return;   // the base constructor's first set, before the air is filled in
            double p = Pressure;
            if (p > 0) Moles = Moles.Select(n => n * Math.Max(0, value) / p).ToArray();
        }
    }

    public override GasMix Air => new(Moles[0], Moles[1], Moles[2], Moles[3], Moles[4]);
    public override double MolarMass => TotalMoles > 0 ? Air.MolarMass : Outside.MolarMass;

    /// <summary>Pa of one gas: its share of the pressure (Dalton).</summary>
    public double PartialPressure(int gas) => Moles[gas] * R * Physics.ToKelvin(Temperature) / Volume;

    public double Mass => Moles.Select((n, i) => n * MolarMasses[i]).Sum();          // kg of gas
    public double GasHeatCapacity => Moles.Select((n, i) => n * MolarCv[i]).Sum();   // J/K
    public double HeatCapacity => GasHeatCapacity + WallHeatCapacity;
    public double GaugePressure => Pressure - Outside.Pressure;

    /// <summary>γ = c_p/c_v of a mixture, c_p = c_v + R per mole.</summary>
    public static double Gamma(double[] moles)
    {
        double n = moles.Sum();
        if (n <= 0) return 1.4;
        double cv = moles.Select((x, i) => x * MolarCv[i]).Sum() / n;
        return (cv + R) / cv;
    }

    /// <summary>The (O2, N2, CO2, H2O, Ar) moles of a zone's air, per mole.</summary>
    private static double[] Fractions(Zone z)
    {
        var a = z.Air;
        double t = a.Total > 0 ? a.Total : 1;
        return [a.O2 / t, a.N2 / t, a.CO2 / t, a.H2O / t, a.Ar / t];
    }

    /// <summary>kg/s through an orifice from P₀ (Pa, at T₀ K, gas constant Rs, ratio γ) to Pd.</summary>
    public static double OrificeFlow(double cd, double area, double p0, double t0K, double pd, double gamma, double rs)
    {
        if (p0 <= pd || area <= 0 || p0 <= 0) return 0;
        double critical = Math.Pow(2 / (gamma + 1), gamma / (gamma - 1));
        double r = Math.Max(pd / p0, critical);
        double psi = 2 * gamma / ((gamma - 1) * rs * t0K) * (Math.Pow(r, 2 / gamma) - Math.Pow(r, (gamma + 1) / gamma));
        return cd * area * p0 * Math.Sqrt(Math.Max(0, psi));
    }

    /// <summary>Gas arriving from somewhere (a leak from inside, a door, a cylinder): moles of each gas at a temperature. Heat mixes by heat capacity.</summary>
    public void AddGas(double[] moles, double temperatureC)
    {
        double cIn = moles.Select((x, i) => x * MolarCv[i]).Sum();
        double c = HeatCapacity;
        if (c + cIn > 0) _temperature = (c * _temperature + cIn * temperatureC) / (c + cIn);
        for (int i = 0; i < Moles.Length; i++) Moles[i] = Math.Max(0, Moles[i] + moles[i]);
    }

    /// <summary>Takes up to that many moles out as its own mixture; returns what went, gas by gas.</summary>
    public double[] TakeGas(double moles)
    {
        double n = TotalMoles;
        if (n <= 0 || moles <= 0) return new double[Moles.Length];
        double f = Math.Min(1, moles / n);
        var taken = Moles.Select(x => x * f).ToArray();
        for (int i = 0; i < Moles.Length; i++) Moles[i] -= taken[i];
        return taken;
    }

    /// <summary>Adds (or, negative, takes) moles of one gas made or used inside it, as a fire does; no less than none.</summary>
    public void ChangeGas(int gas, double moles) => Moles[gas] = Math.Max(0, Moles[gas] + moles);

    /// <summary>m³/s of the surroundings' air a fan or bellows blows in, measured at their pressure and temperature.</summary>
    public double Supply { get; set; }

    /// <summary>Heat from inside another zone (a boiler losing warmth into this room), J.</summary>
    public void AddHeat(double joules)
    {
        double c = HeatCapacity;
        if (c > 0) _temperature += joules / c;
    }

    public void Step(double dt)
    {
        StepSupply(dt);
        StepLeak(dt);
        StepHeat(dt);
    }

    private void StepLeak(double dt)
    {
        Flow = 0;
        Choked = false;
        if (LeakArea <= 0) return;
        double pIn = Pressure, pOut = Outside.Pressure;
        double tIn = Physics.ToKelvin(Temperature), tOut = Physics.ToKelvin(Outside.Temperature);
        if (pIn > pOut)
        {
            double gamma = Gamma(Moles), rs = R / MolarMass;
            double mdot = OrificeFlow(Cd, LeakArea, pIn, tIn, pOut, gamma, rs);
            Choked = pOut / pIn < Math.Pow(2 / (gamma + 1), gamma / (gamma - 1));
            // never past equal pressures: at most half the way there in a step
            double surplus = TotalMoles - pOut * Volume / (R * tIn);
            double dn = Math.Min(mdot * dt / MolarMass, 0.5 * Math.Max(0, surplus));
            var gone = TakeGas(dn);
            if (Outside is Enclosure parent) parent.AddGas(gone, Temperature);
            double kg = gone.Select((x, i) => x * MolarMasses[i]).Sum();
            Flow = kg / dt;
            Lost += kg;
        }
        else if (pOut > pIn)
        {
            var mix = Fractions(Outside);
            double gamma = Gamma(mix), m = Outside.MolarMass, rs = R / m;
            double mdot = OrificeFlow(Cd, LeakArea, pOut, tOut, pIn, gamma, rs);
            Choked = pIn / pOut < Math.Pow(2 / (gamma + 1), gamma / (gamma - 1));
            double deficit = pOut * Volume / (R * tIn) - TotalMoles;
            double dn = Math.Min(mdot * dt / m, 0.5 * Math.Max(0, deficit));
            double[] came = Outside is Enclosure parent ? parent.TakeGas(dn) : mix.Select(x => x * dn).ToArray();
            AddGas(came, Outside.Temperature);
            double kg = came.Select((x, i) => x * MolarMasses[i]).Sum();
            Flow = -kg / dt;
            Lost -= kg;
        }
    }

    private void StepSupply(double dt)
    {
        if (Supply <= 0) return;
        double dn = Outside.Pressure * Supply * dt / (R * Physics.ToKelvin(Outside.Temperature));
        double[] came = Outside is Enclosure parent ? parent.TakeGas(dn) : Fractions(Outside).Select(x => x * dn).ToArray();
        AddGas(came, Outside.Temperature);
    }

    private void StepHeat(double dt)
    {
        double q = Heater + HeatInput, c = HeatCapacity, before = _temperature;
        if (c <= 0) return;
        if (Insulation > 0)
        {
            double steady = Outside.Temperature + q / Insulation;
            _temperature = steady + (_temperature - steady) * Math.Exp(-Insulation * dt / c);
        }
        else _temperature += q * dt / c;
        // what went through the walls warms (or cools) the room outside, if it is one
        if (Outside is Enclosure parent) parent.AddHeat(q * dt - c * (_temperature - before));
    }
}
