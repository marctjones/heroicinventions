using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Boilers burst at what their shell can hold (#139): thin-wall hoop stress σ = P·r/t, so P = σ_t·t/r,
/// derated for a metal near its melting point. The predictions are worked out by hand before the run.
/// </summary>
public class BoilerShellTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();
    private const double Q = 10_000, H = 2, M = 10, C = 4186;

    private static Boiler Pot(string material, double wall, double radius = 0.15, double burst = 0)
    {
        var m = Materials[material];
        return new Boiler(M, 20, Q)
        {
            BurstPressure = burst,
            Wall = wall,
            ShellRadius = radius,
            ShellStrength = m.TensileStrength * 1e6,
            ShellMelting = m.MeltingPoint,
        };
    }

    [Fact]
    public void TheTablesNumbersSetTheRating()
    {
        // 350e6 x 0.003 / 0.15 = 7.0 MPa; 350e6 x 0.0005 / 0.15 = 1.1667 MPa; lead 12e6 x 0.003 / 0.15 = 0.24 MPa
        Assert.Equal(7.0e6, Pot("bronze", 0.003).Rating, 3);
        Assert.Equal(1.1667e6, Pot("bronze", 0.0005).Rating, 100.0);
        Assert.Equal(0.24e6, Pot("lead", 0.003).Rating, 3);
        Assert.Equal(4.4e6, Pot("copper", 0.003).Rating, 3);
        Assert.Equal(0.28e6, Pot("tin", 0.003).Rating, 3);   // 14 MPa
    }

    [Fact]
    public void AnExplicitRatingWinsAndNoWallMeansNoShellRating()
    {
        Assert.Equal(200e3, Pot("lead", 0.003, burst: 200e3).Rating);
        Assert.Equal(200e3, Pot("lead", 0.003, burst: 200e3).BurstLimit);
        var unrated = Pot("bronze", 0);
        Assert.Equal(0, unrated.Rating);
        for (int i = 0; i < 200_000; i++) unrated.Step(0.01, 0);   // 2000 s: past 300 C, 8.6 MPa, and it never bursts
        Assert.False(unrated.Burst);
    }

    [Fact]
    public void AShellLosesStrengthFromHalfItsMeltingPointToNothingAtIt()
    {
        var lead = Pot("lead", 0.003);   // melts 327.5, softens from 163.75
        Assert.Equal(1, lead.Derating(138.2));
        Assert.Equal(1, lead.Derating(163.75), 9);
        Assert.Equal(0.5, lead.Derating(245.625), 9);
        Assert.Equal(0, lead.Derating(400));
        Assert.Equal(1, Pot("bronze", 0.003).Derating(300));   // 950 C: not yet
        Assert.Equal(1, new Boiler(1).Derating(900));          // no shell given: nothing to soften
    }

    [Fact]
    public void ALeadPotBurstsAtItsRatingNear138DegreesOfSteam()
    {
        // 0.24 MPa gauge = 341.3 kPa absolute; the Antoine curve puts that at 138.22 C. Sealed under 10 kW with
        // 2 W/K lost, T(t) = 20 + 5000 (1 - exp(-t/20930)), so t = 20930 ln(5000 / (5000 - 118.22)) = 500.8 s.
        double tBoil = Boiler.SaturationTemperature(101_325 + 240_000);
        Assert.Equal(138.22, tBoil, 2);
        double predicted = M * C / H * Math.Log((20 + Q / H - 20) / (20 + Q / H - tBoil));
        Assert.Equal(500.8, predicted, 1);

        var b = Pot("lead", 0.003);
        while (!b.Burst && b.Time < 900) b.Step(0.01, 0);
        Assert.True(b.Burst);
        Assert.Equal(predicted, b.BurstTime, 1);               // within 0.05 s
    }

    [Fact]
    public void ABurstRecordsThePressureItGaveWayAt()
    {
        var b = Pot("lead", 0.003);
        while (!b.Burst && b.Time < 900) b.Step(0.01, 0);
        Assert.InRange(b.BurstGauge, 240_000, 240_500);        // gauge Pa, one step past the line at most
        Assert.True(b.Flashed > 0);
    }

    [Fact]
    public void ABoilerSealedPastItsSofteningPointHoldsLess()
    {
        // tin melts at 231.9 C: it softens from 115.95 C and holds nothing at 231.9. A fixed 14 MPa x 3 mm / 0.15 = 0.28 MPa
        // would let a tin pot reach 143.8 C of steam; derated, it gives way sooner, where P_sat(T) = 0.28 MPa x (231.9 - T) / 115.95.
        var b = Pot("tin", 0.003);
        while (!b.Burst && b.Time < 900) b.Step(0.01, 0);
        Assert.True(b.Burst);
        double tBurst = b.Time;                                 // s
        double tCold = M * C / H * Math.Log(5000 / (5000 - (Boiler.SaturationTemperature(101_325 + 280_000) - 20)));
        Assert.True(tBurst < tCold - 1, $"derated tin burst at {tBurst:F1} s, undrated would at {tCold:F1} s");
        // and exactly at the derated limit: solve P_sat(T) - atm = 280 kPa x (231.9 - T) / 115.95 for T by bisection
        double lo = 100, hi = 200;
        for (int i = 0; i < 80; i++)
        {
            double mid = (lo + hi) / 2;
            double over = Boiler.SaturationPressure(mid) - 101_325 - 280_000 * (231.9 - mid) / 115.95;
            if (over > 0) hi = mid; else lo = mid;
        }
        double predicted = M * C / H * Math.Log(5000 / (5000 - (lo - 20)));
        Assert.Equal(predicted, tBurst, 1);
    }

    [Fact]
    public void TheBoilerShellsBlueprintBurstsTheLeadPotWhenTheHeatingModelSaysAndTheOthersWhenTheirsDo()
    {
        // Hand-worked (racket/machines/boiler-shells.rkt): t = 20930 ln(5000 / (5000 - (T - 20))) at the Antoine
        // temperature of 101.325 kPa + sigma_t / 50: lead 138.22 C 500.8 s, copper 256.92 C 1016.0 s, bronze 286.0 C 1144.2 s.
        var def = MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "boiler-shells.machine")));
        var run = new MachineRuntime(def, Materials);
        var pots = new[] { ("lead-pot", 500.8, 240.0), ("copper-pot", 1016.0, 4400.0), ("bronze-pot", 1144.2, 7000.0) };
        foreach (var (id, _, kPa) in pots) Assert.Equal(kPa, run.Boilers[id].Rating / 1000, 6);
        var burstAt = new Dictionary<string, double>();
        bool othersSoundAt900 = false;
        for (int i = 0; i < 130_000; i++)
        {
            run.Step(0.01);
            double t = (i + 1) * 0.01;
            foreach (var (id, _, _) in pots)
                if (run.Boilers[id].Burst && !burstAt.ContainsKey(id)) burstAt[id] = t;
            if (i == 89_999) othersSoundAt900 = burstAt.ContainsKey("lead-pot") && !burstAt.ContainsKey("copper-pot") && !burstAt.ContainsKey("bronze-pot");
        }
        Assert.True(othersSoundAt900, "at 900 s lead has burst and copper and bronze have not");
        foreach (var (id, predicted, _) in pots)
            Assert.Equal(predicted, burstAt[id], 0.1);
    }
}
