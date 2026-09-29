using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// The sun over the scene: where it stands, and how strongly its direct beam
/// arrives through a clear sky. The scene faces north along −Z, east along
/// +X, with +Y up.
///
/// Position (Cooper, 1969; the standard solar geometry): the declination on
/// day n of the year is δ = 23.45°·sin(360°·(284 + n)/365); the hour angle is
/// ω = 15° per hour from solar noon; at latitude φ the sun's elevation is
///   sin α = sin φ·sin δ + cos φ·cos δ·cos ω,
/// and its azimuth, from north towards east,
///   A = atan2(−sin ω·cos δ, sin δ·cos φ − cos δ·sin φ·cos ω).
///
/// Strength (Meinel &amp; Meinel, 1976): the beam loses a share of itself to
/// every thickness of air it crosses, so the direct normal irradiance is
///   DNI = S · 0.7^(AM^0.678),
/// S the solar constant above the air and AM the air mass — how many
/// thicknesses of atmosphere the beam passes through, 1/sin α overhead-ish,
/// with Kasten &amp; Young's (1989) correction so it stays finite at the horizon.
/// About 950 W/m² with the sun overhead; nothing once it has set.
///
/// The clock runs with the simulation (<see cref="ClockRate"/> sun-hours per
/// hour; 0 holds the sun still), rolling over into the next day at midnight.
/// </summary>
public sealed class Sun(double latitudeDeg, int day, double solarTimeHours)
{
    public const double SolarConstant = 1361;   // W/m² above the atmosphere, at Earth's distance

    public double Latitude { get; set; } = latitudeDeg;   // degrees, + north
    public int Day { get; set; } = day;                   // 1–365
    public double Time { get; set; } = solarTimeHours;    // solar hours: 12 is noon
    public double ClockRate { get; set; } = 1;            // sun-seconds per simulated second

    private static double Rad(double deg) => deg * Math.PI / 180;
    private static double Deg(double rad) => rad * 180 / Math.PI;

    public double Declination => 23.45 * Math.Sin(Rad(360.0 * (284 + Day) / 365));   // degrees
    public double HourAngle => 15 * (Time - 12);                                         // degrees

    /// <summary>Degrees above the horizon (negative at night).</summary>
    public double Elevation
    {
        get
        {
            double phi = Rad(Latitude), delta = Rad(Declination), omega = Rad(HourAngle);
            return Deg(Math.Asin(Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta) * Math.Cos(omega)));
        }
    }

    /// <summary>Degrees from north towards east: 90 due east, 180 due south.</summary>
    public double Azimuth
    {
        get
        {
            double phi = Rad(Latitude), delta = Rad(Declination), omega = Rad(HourAngle);
            double a = Deg(Math.Atan2(-Math.Sin(omega) * Math.Cos(delta),
                                      Math.Sin(delta) * Math.Cos(phi) - Math.Cos(delta) * Math.Sin(phi) * Math.Cos(omega)));
            return (a + 360) % 360;
        }
    }

    /// <summary>A unit vector from the scene towards the sun (north −Z, east +X, up +Y).</summary>
    public Vec3 Direction
    {
        get
        {
            double alt = Rad(Elevation), az = Rad(Azimuth);
            return new Vec3(Math.Cos(alt) * Math.Sin(az), Math.Sin(alt), -Math.Cos(alt) * Math.Cos(az));
        }
    }

    /// <summary>Thicknesses of atmosphere the beam crosses (Kasten &amp; Young, 1989); infinite at night.</summary>
    public double AirMass
    {
        get
        {
            double alt = Elevation;
            if (alt <= 0) return double.PositiveInfinity;
            return 1 / (Math.Sin(Rad(alt)) + 0.50572 * Math.Pow(alt + 6.07995, -1.6364));
        }
    }

    /// <summary>Direct beam on a surface square to the sun, W/m² (Meinel's clear sky).</summary>
    public double DirectNormal => Elevation <= 0 ? 0 : SolarConstant * Math.Pow(0.7, Math.Pow(AirMass, 0.678));

    public void Step(double dt)
    {
        Time += dt * ClockRate / 3600;
        while (Time >= 24) { Time -= 24; Day = Day % 365 + 1; }
        while (Time < 0) { Time += 24; Day = (Day + 363) % 365 + 1; }
    }
}
