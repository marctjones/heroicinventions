using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Heat you can see. A vessel of hot water breathes a little vapour at its
/// lid, and the warmer it is than the air the more — so a copper cooling on
/// a frosty night wisps hard at first and fades as it nears the air's
/// temperature, and the same copper in a warm room hardly shows at all
/// (vapour is only visible where it condenses in cooler air). Boilers
/// already showing steam another way — driving a rotor or a cylinder, or
/// burst — are left to that; a sealed pressure vessel (a safety valve, a
/// burst rating) and any boiler under pressure hold their steam in.
/// </summary>
public partial class MachineView
{
    private const double WispsFrom = 25;    // K over the air before any vapour shows
    private const double WispsFull = 60;    // K over that for the full plume
    private readonly List<(Boiler boiler, GpuParticles3D wisps)> _wisps = [];

    private void BuildWarmth()
    {
        var busy = Runtime.Rotors.Keys.Select(Runtime.BoilerFor)
            .Concat(Runtime.Cylinders.Values.Select(c => Runtime.Boilers.First(kv => kv.Value == c.Boiler).Key))
            .ToHashSet();
        foreach (var (id, boiler) in Runtime.Boilers)
        {
            // a boiler with a safety valve or a burst rating is a sealed pressure vessel: nothing breathes out of its lid
            if (busy.Contains(id) || boiler.Valves.Count > 0 || boiler.BurstPressure > 0) continue;
            var part = Runtime.Def.Part(id)!;
            float r = (float)part.Number("radius"), h = (float)part.Number("height");
            var wisps = SteamCloud(V(part.At) + new Vector3(0, h + 0.02f, 0), amount: 24, radius: r * 0.6f, lifetime: 2.5f);
            _wisps.Add((boiler, wisps));
        }
    }

    private void DrawWarmth()
    {
        foreach (var (boiler, wisps) in _wisps)
        {
            double over = boiler.Temperature - Runtime.Ambient - WispsFrom;
            // under pressure its lid is holding the steam in: that is a safety valve's business, not a wisp's
            bool showing = over > 0 && boiler.GaugePressure < 1000 && !boiler.Burst && !boiler.IsDry;
            wisps.Emitting = showing;
            if (showing) wisps.AmountRatio = (float)Math.Clamp(over / WispsFull, 0.15, 1);
        }
    }
}
