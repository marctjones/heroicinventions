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
///
/// Warmth you can see (#169): each boiler's shell takes the warmth tint of its
/// water's temperature (<see cref="Skins.Warm"/>), and a boiler at or above the
/// boiling point of the pressure around it (100 °C at sea level, 0.1 °C on Mars)
/// blows a full plume from its lid, whatever else it is doing.
/// </summary>
public partial class MachineView
{
    private const double WispsFrom = 25;    // K over the air before any vapour shows
    private const double WispsFull = 60;    // K over that for the full plume
    private readonly List<(Boiler boiler, GpuParticles3D wisps)> _wisps = [];
    private readonly List<(string id, Boiler boiler, GpuParticles3D plume)> _boilingPlumes = [];

    /// <summary>Whether the water is at or above its boiling point at the pressure of the air around it.</summary>
    public static bool IsBoiling(Boiler boiler) =>
        !boiler.IsDry && !boiler.Burst && boiler.Temperature >= Boiler.SaturationTemperature(boiler.Zone.Pressure) - 1e-6;

    private void BuildWarmth()
    {
        var busy = Runtime.Rotors.Keys.Select(Runtime.BoilerFor)
            .Concat(Runtime.Cylinders.Values.Select(c => Runtime.Boilers.First(kv => kv.Value == c.Boiler).Key))
            .ToHashSet();
        foreach (var (id, boiler) in Runtime.Boilers)
        {
            _building = id;   // the plume and wisps below are this boiler's (#151)
            // at its boiling point it blows a full plume, whatever else it is doing (#169)
            var lid = Runtime.Def.Part(id)!;
            _boilingPlumes.Add((id, boiler, SteamCloud(V(lid.At) + new Vector3(0, (float)lid.Number("height") + 0.02f, 0),
                                                       amount: 20, radius: (float)lid.Number("radius") * 0.4f, lifetime: 1.6f)));
            // a boiler with a safety valve or a burst rating is a sealed pressure vessel: nothing breathes out of its lid
            if (busy.Contains(id) || boiler.Valves.Count > 0 || boiler.Rating > 0) continue;
            var part = Runtime.Def.Part(id)!;
            float r = (float)part.Number("radius"), h = (float)part.Number("height");
            var wisps = SteamCloud(V(part.At) + new Vector3(0, h + 0.02f, 0), amount: 24, radius: r * 0.6f, lifetime: 2.5f);
            _wisps.Add((boiler, wisps));
        }
    }

    private void DrawWarmth()
    {
        foreach (var (id, boiler, plume) in _boilingPlumes)
        {
            bool boiling = IsBoiling(boiler);
            if (boiling != plume.Emitting && OS.GetEnvironment("HEROIC_DEBUG_PHYSICS") == "1")
                GD.Print($"[warmth] {id} {(boiling ? "boils" : "stops boiling")} at t={Runtime.Time:0.###} s, {boiler.Temperature:0.####} °C");
            plume.Emitting = boiling;
            // the shell is as warm as its water: cold blue-grey in a frost, ochre to dull red as it heats
            if (_boilerBodies.TryGetValue(id, out var body) && body.MaterialOverride is StandardMaterial3D shell)
                Skins.Warm(shell, boiler.Temperature);
        }
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
