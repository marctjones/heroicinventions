using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #111, air buoyancy: a hot-air envelope (the Kongming lantern's, #125) lifts by (ρ_out − ρ_in) g V.
/// Every number below was worked out before the class ran (see the working beside each).
/// </summary>
public class EnvelopeTests
{
    /// <summary>1 m³ of paper, 50 g, with a 20 g burner, in 15 °C air; 800 W and UA 15 W/K as in kongming-lantern.rkt.</summary>
    private static Envelope Lantern(Zone air) => new("lantern", 1.0, 0.05, 0.02, 800, fuel: 0.010) { SkinConductance = 15, Zone = air, Temperature = air.Temperature };

    [Fact]
    public void ALanternLiftsOffOnceTheAirInsidePasses32Point46DegreesAndMarsNeverLiftsIt()
    {
        var air = new Zone(Planet.Earth, 15);
        // ρ_out = P / (R_s T) = 101325 / (287.05 × 288.15) = 1.22501 kg/m³ (the issue's 1.225)
        Assert.Equal(1.22501, air.AirDensity, 5);
        // Δρ·V = 0.070 kg  →  ρ_in = 1.15501  →  T = P / (R_s ρ_in) = 305.613 K = 32.463 °C
        var lantern = Lantern(air);
        Assert.Equal(1.15501, air.AirDensity - 0.070 / 1.0, 5);
        Assert.Equal(32.463, lantern.LiftOffTemperature, 3);

        // heat it in small steps: lift is below weight until the inside reaches 32.463 °C, above it after
        // (independent RK4 at 1 ms of the same energy balance, C(T) = ρ_in V c_p + m c_skin: 33.398 s)
        double t = 0, dt = 0.01, before = double.NaN, hotAt = double.NaN;
        while (t < 60 && double.IsNaN(hotAt))
        {
            before = lantern.Lift - lantern.Weight;
            lantern.Step(dt);
            t += dt;
            if (lantern.Lift >= lantern.Weight) hotAt = t;
        }
        Assert.True(before < 0, "it was still too heavy the step before");
        Assert.InRange(hotAt, 33.398 - dt, 33.398 + dt);                     // within a step of the hand-integrated time
        Assert.InRange(lantern.Temperature, 32.463, 32.463 + 0.5);           // and no further than one step's warming past the threshold
        Assert.Equal(0.070 * 9.81, lantern.Weight, 6);

        // Mars: 610 Pa of CO2 at −63 °C is 0.0152 kg/m³: even with every bit of air out of it the lantern lifts 0.0152 kg < 0.070
        var mars = new Zone(Planet.Mars, -63);
        var cold = Lantern(mars);
        Assert.False(cold.CanLift);
        Assert.True(double.IsNaN(cold.LiftOffTemperature));
        for (int i = 0; i < 20_000; i++) { cold.Step(0.05); Assert.True(cold.Lift < cold.Weight); }
        Assert.True(cold.Lift < cold.LiftLimit && cold.LiftLimit < cold.Weight);
        Assert.Equal(mars.AirDensity * 3.71, cold.LiftLimit, 6);
    }

    private static MachineRuntime Load(string name) =>
        new(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", name + ".machine"))), MaterialLibrary.LoadDefault());

    /// <summary>kongming-lantern.rkt's own numbers: lift-off at 33.40 s and 32.46 °C inside, while the unlit twin stays put; and on Mars it never lifts.</summary>
    [Fact]
    public void TheBlueprintLanternPassesItsLiftOffTemperatureAt33Point4SecondsAndTheMartianOneNeverLifts()
    {
        var rt = Load("kongming-lantern");
        var lantern = rt.Envelopes["lantern"];
        Assert.Equal(32.463, rt.GetField("lantern", "lift-off-temperature"), 3);
        double t = 0;
        while (rt.GetField("lantern", "lift") < rt.GetField("lantern", "weight") && t < 100) { rt.Step(0.01); t += 0.01; }
        Assert.InRange(t, 33.398 - 0.02, 33.398 + 0.02);
        Assert.InRange(lantern.Temperature, 32.463, 32.463 + 0.3);
        Assert.Equal(15, rt.Envelopes["control"].Temperature, 9);
        Assert.True(rt.GetField("control", "lift") <= 0);

        var mars = Load("kongming-lantern-mars");
        Assert.Equal(0, mars.GetField("lantern", "can-lift"));
        for (int i = 0; i < 12_000; i++) { mars.Step(0.01); Assert.True(mars.GetField("lantern", "lift") < mars.GetField("lantern", "weight")); }
        // its skin settles at T_out + Q/UA = -63 + 800/15 = -9.7 °C, the thin air in it taking next to no heat
        Assert.InRange(mars.GetField("lantern", "temperature"), -10.5, -9);
        Assert.Equal(0.0152 * 3.71, mars.GetField("lantern", "lift-limit"), 3);
    }

    [Fact]
    public void TheEditorPlacesAnEnvelopeAndItRunsAndExports()
    {
        var s = new BuildSession(MaterialLibrary.LoadDefault(), catalogue: [], machinesDir: Path.GetTempPath(), name: "bench");
        s.Execute("(envelope balloon #:at (0 0 0) #:volume 2 #:envelope-mass 0.1)");
        s.Execute("(set balloon #:burner-power 500)");
        s.Execute("(set balloon #:skin-conductance 20)");
        Assert.StartsWith("ok:", s.Execute("(check)"));
        string rkt = RktExporter.Write(s.Document.ToMachineDef());
        Assert.Contains("(envelope balloon", rkt);
        Assert.Contains("#:volume 2", rkt);
        Assert.Contains("#:burner-power 500", rkt);
        Assert.Contains("#:skin-conductance 20", rkt);
        var rt = new MachineRuntime(s.Document.ToMachineDef(), MaterialLibrary.LoadDefault());
        Assert.Equal(2, rt.Envelopes["balloon"].Volume);
        Assert.Equal(0.12, rt.Envelopes["balloon"].Mass, 9);
        rt.SetField("balloon", "burner-power", 0);
        Assert.Equal(0, rt.Envelopes["balloon"].BurnerPower);
    }

    [Fact]
    public void WithNoLossTheHeatingFollowsItsClosedForm()
    {
        // Q t = (P V c_p / R_s) ln(T/T0) + m c_skin (T − T0), T in kelvin: to the 32.463 °C of lift-off at 800 W that is 27.620 s
        var air = new Zone(Planet.Earth, 15);
        var lantern = Lantern(air);
        lantern.SkinConductance = 0;
        double t = 0;
        while (lantern.Lift < lantern.Weight && t < 60) { lantern.Step(0.01); t += 0.01; }
        Assert.InRange(t, 27.620, 27.640);
        // and the heat given is exactly the burner's power times the time: nothing is made or lost
        Assert.Equal(800 * t, lantern.Burned, 6);
    }

    [Fact]
    public void ABurnerSpentOfItsFuelGoesOutAndTheAirCoolsBackTowardTheAmbientAir()
    {
        var air = new Zone(Planet.Earth, 15);
        // 0.5 g of wax is 20 kJ, 25 s of 800 W; a loss of 15 W/K then cools it with time constant C/UA
        var lantern = new Envelope("l", 1.0, 0.05, 0.02, 800, fuel: 0.0005) { SkinConductance = 15, Zone = air, Temperature = 15 };
        for (int i = 0; i < 10_000; i++) lantern.Step(0.05);   // 500 s
        Assert.Equal(0, lantern.Fuel, 12);
        Assert.Equal(0.0005 * 4e7, lantern.Burned, 6);
        Assert.False(lantern.Lit);
        Assert.InRange(lantern.Temperature, 15, 15.3);   // 25 K at the end of the burn × e^(−475 s / 87 s) is 0.1 K
    }
}
