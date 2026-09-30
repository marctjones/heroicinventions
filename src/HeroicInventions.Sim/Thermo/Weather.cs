using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// Time and weather on a planet (issue #69), run from the scene's
/// <see cref="WeatherSpec"/>:
/// <list type="bullet">
/// <item>Sols: the sun's clock divides the planet's solar day into 24 local
/// hours (a Mars sol is 88,775 s); <see cref="Sun.Sols"/> counts them from the
/// start of the run, sol 1 first.</item>
/// <item>The air follows the planet's daily curve, when it has one and the
/// scene lets it: T = (min + max)/2 + (max − min)/2·cos(2π(h − peak)/24),
/// coldest twelve hours before the warmest.</item>
/// <item>Dust storms, on a schedule the player can see: while one blows the
/// air's dust optical depth is its τ, and the sun's beam at the ground falls
/// by Beer–Lambert along the slant path, e^(−Δτ·AM) more than on a clear day
/// (AM ≈ 1/sin α, the air mass). Dust settles on mirrors, taking the
/// fraction <see cref="StormSpec.Settle"/> a sol of what they still
/// reflect, until someone cleans them.</item>
/// <item>Relay passes at fixed local solar hours, each so many minutes long.</item>
/// </list>
/// </summary>
public sealed class Weather(WeatherSpec spec, Sun sun, Func<Planet> planet)
{
    public WeatherSpec Spec { get; } = spec;
    public Sun Sun { get; } = sun;

    /// <summary>The storm blowing now, if any.</summary>
    public StormSpec? Storm
    {
        get
        {
            foreach (var s in Spec.Storms)
            {
                double start = s.Sol - 1 + s.Hour / 24;
                if (Sun.Sols >= start && Sun.Sols < start + s.Sols) return s;
            }
            return null;
        }
    }

    /// <summary>The air's dust optical depth on a clear day, from the planet's sky: −ln T.</summary>
    public double ClearDust => -Math.Log(Math.Max(1e-12, Sun.SkyTransmittance));

    /// <summary>The air's dust optical depth now.</summary>
    public double Dust => Storm is { } s ? Math.Max(ClearDust, s.Tau) : ClearDust;

    /// <summary>°C the air stands at at this local hour, if the planet has a daily curve and the scene follows it.</summary>
    public double? Ambient
    {
        get
        {
            if (!Spec.Daily || planet().DailyTemperature is not { } d) return null;
            double mid = (d.Min + d.Max) / 2, half = (d.Max - d.Min) / 2;
            return mid + half * Math.Cos(2 * Math.PI * (Sun.Time - d.PeakHour) / 24);
        }
    }

    /// <summary>Is the relay orbiter overhead: within a pass's minutes after one of its hours?</summary>
    public bool Relay => Spec.Passes.Any(h => ((Sun.Time - h) % 24 + 24) % 24 * 60 < Spec.PassMinutes);

    /// <summary>Local hours to the start of the next pass (0 during one).</summary>
    public double NextPass => Relay ? 0 : Spec.Passes.Count == 0 ? double.PositiveInfinity
        : Spec.Passes.Min(h => ((h - Sun.Time) % 24 + 24) % 24);

    /// <summary>Sets the sun's extra dust for now; settles dust on the mirrors for dt seconds.</summary>
    public void Step(double dt, IEnumerable<Mirror> mirrors)
    {
        var storm = Storm;
        Sun.ExtraDust = storm is { } s ? Math.Max(0, s.Tau - ClearDust) : 0;
        if (storm is { Settle: > 0 } st)
        {
            double keep = Math.Exp(-st.Settle * dt * Sun.ClockRate / Sun.SolLength);
            foreach (var m in mirrors) m.Dust = 1 - (1 - m.Dust) * keep;
        }
    }
}
