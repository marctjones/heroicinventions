using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// "Windmill sites matter" (owner ruling 2026-10-09): in a world whose map has a wind field, a windmill the player builds reads the
/// map's wind at its own place by default. Two palette windmills in the Lonely Rover's crater, one on the corridor's line and one 80 m
/// off it, are worked out beforehand from the map's formula (6 m/s x corridor x daily x gusts) at 02:00 and at 14:00, and the sim's
/// wind, the power the wind carries through the sails (1/2 rho A v^3) and the power a loaded pair of sails settles at must match.
/// </summary>
public class WindSitingTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private const string MarsScene = """
        (machine scene (ambient -63.0)
          (planet mars (name "Mars") (gravity 3.71) (pressure 610.0) (temperature -63.0) (air (o2 0.0017) (n2 0.0259) (co2 0.9527) (h2o 0.0003) (ar 0.0194)) (molar-mass #f) (solar-constant 586.2) (sky-transmittance 0.741) (air-mass-exponent 1.0) (sol 88775.0) (year 669.0) (obliquity 25.19) (daily-temperature -80.0 -20.0 15.0) (sky-color 0.78 0.6 0.45) (ground-color 0.6 0.36 0.22)))
        """;

    // on the line from the notch (azimuth 200) through the centre, 100 m from the middle; and 80 m across it from the first
    private const double OnX = -94.0, OnZ = -34.2;
    private static readonly double OffX = OnX + 80 * -Math.Sin(200 * Math.PI / 180), OffZ = OnZ + 80 * Math.Cos(200 * Math.PI / 180);

    private static string MapText() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "maps", "victoria.map"));

    /// <summary>A windmill placed with the palette's own defaults, as the player's build does, at a place in the crater.</summary>
    private static MachineRuntime Built(string id, double x, double z, string extra = "")
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-wind-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var session = new BuildSession(Materials, [], dir, id);
        var scene = MachineDef.Parse(MarsScene);
        session.Open(new MachineDef { Name = id, Ambient = scene.Ambient, Planet = scene.Planet, Parts = [], Pipes = [], Connects = [], SealedAir = [] });
        Assert.StartsWith("placed", session.Execute($"(windmill sails #:at ({x} 11 {z}){extra})"));
        return new MachineRuntime(session.Document.ToMachineDef(), Materials);
    }

    private static double Corridor(double across) => 0.3 + 0.7 * Math.Exp(-across * across / 6400);
    private static double Daily(double hour) => 1 + 0.35 * Math.Cos(2 * Math.PI * (hour - 2) / 24);
    private static double Gusts(double s) => 1 + 0.25 * (Math.Sin(2 * Math.PI * s / 37) + Math.Sin(2 * Math.PI * s / 91 + 1.3)) / 2;

    [Fact]
    public void TheOffCorridorPlaceIs80MetresAcrossTheLine()
    {
        var w = Terrain.Parse(MapText(), "victoria").Wind!;
        Assert.Equal(1, w.Corridor(OnX, OnZ), 1e-3);
        Assert.Equal(Corridor(80), w.Corridor(OffX, OffZ), 1e-3);
        Assert.Equal(0.3 + 0.7 / Math.E, w.Corridor(OffX, OffZ), 1e-3);   // 0.5575
    }

    [Fact]
    public void APaletteWindmillOnTheCorridorAndOneOffIt_ReadTheMapsWindAtTheirOwnPlace_At0200And1400()
    {
        var ground = new WorldGround(Terrain.Parse(MapText(), "victoria"));
        var on = Built("on", OnX, OnZ);
        var off = Built("off", OffX, OffZ);
        var flat = Built("flat", OnX, OnZ, " #:wind-from-map #f");   // the same mill that asked for a flat wind
        foreach (var (id, m) in new[] { ("on", on), ("off", off), ("flat", flat) }) ground.Attach(id, m);
        Assert.Equal(6, flat.Windmills["sails"].Wind);                 // the palette's own flat 6 m/s
        foreach (double hour in new[] { 2.0, 14.0 })
        {
            foreach (var m in new[] { on, off, flat }) m.SetField("scene", "time", hour);
            ground.Step(0.1);
            double seconds = on.Sun.Sols * on.Sun.SolLength;
            double daily = Daily(on.Sun.Time), gusts = Gusts(seconds);
            double vOn = 6 * 1.0 * daily * gusts, vOff = 6 * Corridor(80) * daily * gusts;
            var sOn = on.Windmills["sails"];
            var sOff = off.Windmills["sails"];
            Assert.Equal(vOn, sOn.Wind, 0.01);
            Assert.Equal(vOff, sOff.Wind, 0.01);
            Assert.Equal(6, flat.Windmills["sails"].Wind);             // an explicit flat mill is not moved by the hour
            // the wind carries 1/2 rho A v^3 through the sails: from the worked wind, the same mill on the corridor takes (vOn/vOff)^3 as much
            double rho = on.FieldGetters["scene.air-density"]();
            Assert.Equal(0.5 * rho * Math.PI * 100 * Math.Pow(sOn.Wind, 3), sOn.WindPower, 1e-6 * sOn.WindPower);
            Assert.Equal(0.5 * rho * Math.PI * 100 * Math.Pow(sOff.Wind, 3), sOff.WindPower, 1e-6 * sOff.WindPower);
            Assert.Equal(1 / Corridor(80), sOn.Wind / sOff.Wind, 1e-3);                                   // 1.794 as strong,
            Assert.Equal(Math.Pow(1 / Corridor(80), 3), sOn.WindPower / sOff.WindPower, 0.02);            // so 5.77 times the power
            // the corridor at 02:00 is a different place from the corridor at 14:00: the hour, by (1.35 / 0.65)
            output.WriteLine($"{hour:00}:00 corridor {sOn.Wind:F3} m/s {sOn.WindPower:F0} W through the sails, 80 m off {sOff.Wind:F3} m/s {sOff.WindPower:F0} W (worked {vOn:F3}, {vOff:F3})");
            if (hour == 2) Assert.InRange(vOn / gusts, 8.099, 8.101);   // 6 x 1.35
            else Assert.InRange(vOn / gusts, 3.899, 3.901);              // 6 x 0.65
        }
    }

    [Fact]
    public void LoadedSailsSettleAtThePowerTheWindHereCanGive_OnTheCorridorAndOffIt_At0200And1400()
    {
        // gusts out and the clock pinned, so each mill settles to a steady wind; a 70 N.m stone on 10 m sails of 1500 kg in Mars's air
        // (tau(w) = tau* (2 - w R / (v lambda*)), tau* = 1/2 rho A v^2 R Cp / lambda*: the sails' torque falls in a straight line with speed)
        var map = Terrain.Parse(MapText().Replace("0.35 2.0 0.25))", "0.35 2.0 0.0))"), "victoria-still");
        Assert.Equal(0, map.Wind!.Gust);
        const double load = 70;
        var predicted = new Dictionary<string, double>();
        var measured = new Dictionary<string, double>();
        foreach (double hour in new[] { 2.0, 14.0 })
            foreach (var (place, x, z, across) in new[] { ("on", OnX, OnZ, 0.0), ("off", OffX, OffZ, 80.0) })
            {
                var ground = new WorldGround(map);
                var m = Built(place, x, z, $" #:load {load}");
                ground.Attach(place, m);
                var sails = m.Windmills["sails"];
                double rho = m.FieldGetters["scene.air-density"]();
                double v = 6 * Corridor(across) * Daily(hour);
                double tauStar = 0.5 * rho * Math.PI * 100 * v * v * 10 * 0.3 / 2.5;
                double omega = Math.Max(0, (2 - load / tauStar) * v * 2.5 / 10);   // 0 when the stone is more than the sails can turn (they stall)
                predicted[$"{place}{hour}"] = load * omega;
                for (int i = 0; i < 10000; i++) { m.SetField("scene", "time", hour); m.Step(1.0); ground.Step(1.0); }
                measured[$"{place}{hour}"] = sails.Power;
                output.WriteLine($"{place} {hour:00}:00 wind {v:F3} m/s (sim {sails.Wind:F3}), wind carries {sails.WindPower:F0} W, stone power predicted {predicted[$"{place}{hour}"]:F2} W, measured {sails.Power:F2} W");
                Assert.Equal(v, sails.Wind, 0.01);
            }
        foreach (var k in predicted.Keys)
            Assert.Equal(predicted[k], measured[k], Math.Max(0.5, predicted[k] * 0.01));   // within 1%
        // the numbers themselves: ~231 W on the corridor at night, ~27 W by day, ~63 W off it at night, and by day off the corridor
        // the wind is too thin for the stone to turn at all
        Assert.InRange(predicted["on2"], 215, 245);
        Assert.InRange(predicted["on14"], 20, 35);
        Assert.InRange(predicted["off2"], 50, 75);
        Assert.Equal(0, predicted["off14"]);
        Assert.True(predicted["on2"] > 3 * predicted["off2"], "a mill on the corridor takes several times the power of one 80 m off it");
    }
}
