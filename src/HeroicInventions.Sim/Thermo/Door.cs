namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// Moving gas between zones (issue #41). A zone that is an
/// <see cref="Enclosure"/> gives and takes moles of each gas; the planet's
/// open air is too big to notice either way.
/// </summary>
public static class ZoneGas
{
    /// <summary>Takes <paramref name="moles"/> of a zone's own mixture out of it; returns them gas by gas.</summary>
    public static double[] Take(Zone z, double moles)
    {
        if (z is Enclosure e) return e.TakeGas(moles);
        var a = z.Air;
        double t = a.Total > 0 ? a.Total : 1;
        return [moles * a.O2 / t, moles * a.N2 / t, moles * a.CO2 / t, moles * a.H2O / t, moles * a.Ar / t];
    }

    /// <summary>Gives a zone gas at a temperature; the open air just takes it.</summary>
    public static void Give(Zone z, double[] gas, double temperatureC)
    {
        if (z is Enclosure e) e.AddGas(gas, temperatureC);
    }

    public static double Moles(Zone z) => z is Enclosure e ? e.TotalMoles : double.PositiveInfinity;

    public static double Kg(double[] gas) => gas.Select((n, i) => n * Enclosure.MolarMasses[i]).Sum();

    /// <summary>
    /// Moles that would have to pass from <paramref name="high"/> to
    /// <paramref name="low"/> to bring them to one pressure, at their present
    /// temperatures: (P_h − P_l) / (R·T_h/V_h + R·T_l/V_l), the open air's
    /// term being nothing.
    /// </summary>
    public static double ToLevel(Zone high, Zone low)
    {
        double Stiffness(Zone z) => z is Enclosure e ? Enclosure.R * Physics.ToKelvin(e.Temperature) / e.Volume : 0;
        double k = Stiffness(high) + Stiffness(low);
        return k > 0 ? Math.Max(0, high.Pressure - low.Pressure) / k : 0;
    }
}

/// <summary>
/// A door, hatch or valve between two zones (issue #41): an opening of
/// <see cref="Area"/> m², open by <see cref="Open"/> (0 shut … 1 wide).
/// Gas runs from the higher pressure to the lower as a compressible orifice,
/// choked while the lower is under the critical fraction of the higher, as
/// an enclosure's leak does; never in one step past the pressure at which
/// the two would stand level. The gas that stays behind keeps its
/// temperature (isothermal), so a small chamber vented into near-vacuum
/// through a valve empties as e^(−t/τ), τ = V/(Cd·A_open·√(γRT)·Ψ*).
/// </summary>
public sealed class Door(string name, Zone a, Zone b, double area)
{
    public string Name { get; } = name;
    public Zone A { get; set; } = a;
    public Zone B { get; set; } = b;
    public double Area { get; } = area;                            // m² when wide open
    private double _open;
    public double Open { get => _open; set => _open = Math.Clamp(value, 0, 1); }
    public double Cd { get; set; } = Enclosure.DefaultCoefficient;

    public double Flow { get; private set; }                       // kg/s from A to B (negative: B to A), last step
    public double Passed { get; private set; }                     // kg from A to B, net, all told
    public double Moved { get; private set; }                      // kg through it either way, all told
    public bool Choked { get; private set; }

    public void Step(double dt)
    {
        Flow = 0;
        Choked = false;
        double area = Area * Open;
        if (area <= 0 || A.Pressure == B.Pressure) return;
        var (high, low, sign) = A.Pressure > B.Pressure ? (A, B, 1) : (B, A, -1);
        var mix = high is Enclosure he ? he.Moles : [high.Air.O2, high.Air.N2, high.Air.CO2, high.Air.H2O, high.Air.Ar];
        double gamma = Enclosure.Gamma(mix), m = high.MolarMass, tK = Physics.ToKelvin(high.Temperature);
        double mdot = Enclosure.OrificeFlow(Cd, area, high.Pressure, tK, low.Pressure, gamma, Enclosure.R / m);
        Choked = low.Pressure / high.Pressure < Math.Pow(2 / (gamma + 1), gamma / (gamma - 1));
        double dn = Math.Min(mdot * dt / m, 0.5 * ZoneGas.ToLevel(high, low));
        var gas = ZoneGas.Take(high, dn);
        ZoneGas.Give(low, gas, high.Temperature);
        double kg = ZoneGas.Kg(gas);
        Flow = sign * kg / dt;
        Passed += sign * kg;
        Moved += kg;
    }
}

/// <summary>
/// A pump moving gas from one zone to another (issue #41): it sweeps
/// <see cref="Speed"/> m³/s of the gas it draws from, ṅ = P_from·S/(R·T), so
/// a chamber it empties falls as P₀·e^(−S·t/V). It stops once the zone it
/// draws from is down to <see cref="Until"/> Pa (a pressure switch; 0: never).
/// Pushing gas up from P_from to P_to it works as an ideal isothermal
/// compressor, ṅ·R·T·ln(P_to/P_from) watts, so emptying a chamber of volume
/// V from P₀ to P_f into a room held at P₀ costs V·(P₀ − P_f − P_f·ln(P₀/P_f)).
/// </summary>
public sealed class GasPump(string name, Zone from, Zone to, double speed)
{
    public string Name { get; } = name;
    public Zone From { get; set; } = from;
    public Zone To { get; set; } = to;
    private double _speed = Math.Max(0, speed);
    public double Speed { get => _speed; set => _speed = Math.Max(0, value); }   // m³/s swept
    public double Until { get; set; }                                            // Pa it stops at

    public bool Running => Speed > 0 && From.Pressure > Until;
    public double Flow { get; private set; }                       // kg/s moved, last step
    public double Moved { get; private set; }                      // kg, all told
    public double Power { get; private set; }                      // W, last step
    public double Work { get; private set; }                       // J, all told

    public void Step(double dt)
    {
        Flow = Power = 0;
        if (!Running) return;
        double tK = Physics.ToKelvin(From.Temperature);
        double dn = From.Pressure * Speed * dt / (Enclosure.R * tK);
        // no further than the switch: the moles above Until
        if (From is Enclosure e) dn = Math.Min(dn, Math.Max(0, e.TotalMoles - Until * e.Volume / (Enclosure.R * tK)));
        double pFrom = From.Pressure, pTo = To.Pressure;
        var gas = ZoneGas.Take(From, dn);
        ZoneGas.Give(To, gas, From.Temperature);
        double kg = ZoneGas.Kg(gas);
        double joules = pTo > pFrom && pFrom > 0 ? gas.Sum() * Enclosure.R * tK * Math.Log(pTo / pFrom) : 0;
        Flow = kg / dt;
        Moved += kg;
        Power = joules / dt;
        Work += joules;
    }
}
