using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #71, thermal mass: heat stores, a lidded bin and a wall that heat soaks into. Every number was worked out
/// before the class ran (the working is beside each).
/// </summary>
public class HeatStoreTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static HeatSlab Regolith(double thickness, double area, double ground = -55) =>
        new(Materials["regolith"].Conductivity!.Value, Materials["regolith"].Density, Materials["regolith"].SpecificHeat!.Value, thickness, area, ground, ground);

    /// <summary>A wall whose surface is held at <paramref name="surface"/> by a conductance too large to notice.</summary>
    private static void Soak(HeatSlab wall, double surface, double seconds, double dt)
    {
        for (double t = 0; t < seconds - 1e-9; t += dt) wall.Step(dt, 0, 1e9, 1e9 * surface, 0);
    }

    [Fact]
    public void TheMaterialTableGivesRegolith039AndBasalt840()
    {
        Assert.Equal(0.039, Materials["regolith"].Conductivity!.Value, 6);          // InSight HP3, Grott et al. 2021
        Assert.Equal(800, Materials["regolith"].SpecificHeat!.Value, 6);
        Assert.Equal(840, Materials["basalt"].SpecificHeat!.Value, 6);
        Assert.All(Materials.All, m => { Assert.NotNull(m.SpecificHeat); Assert.NotNull(m.Conductivity); });
    }

    [Fact]
    public void AFreshRegolithWallTakesInHeatAs2IDeltaTRootTOverPi()
    {
        // I = √(k ρ c) = √(0.039 × 1500 × 800) = √46800 = 216.33 J/(m² K √s); α = k/(ρ c) = 3.25e-8 m²/s
        var wall = Regolith(thickness: 3, area: 1);
        Assert.Equal(216.33, wall.Inertia, 2);
        Assert.Equal(3.25e-8, wall.Diffusivity, 10);
        // surface held 60 K above the ground for a night: Q = 2 I ΔT √(t/π), t = 12 h = 43,200 s
        //   = 2 × 216.33 × 60 × √(13750.99) = 2 × 216.33 × 60 × 117.26 = 3.0441 MJ/m²
        double expected12 = 2 * 216.33 * 60 * Math.Sqrt(43200 / Math.PI);
        Assert.InRange(expected12, 3.044e6, 3.0442e6);
        Soak(wall, surface: 5, seconds: 43200, dt: 10);
        Assert.InRange(wall.Absorbed / expected12, 0.985, 1.015);
        // the same at 2 h: 2 × 216.33 × 60 × √(7200/π) = 1.2427 MJ
        var early = Regolith(3, 1);
        Soak(early, 5, 7200, 5);
        double expected2 = 2 * 216.33 * 60 * Math.Sqrt(7200 / Math.PI);
        Assert.InRange(early.Absorbed / expected2, 0.985, 1.015);
        // and that is 15 times what the steady loss through 0.5 m would take in the same night: k ΔT t / L = 0.039 × 60 × 43200 / 0.5 = 0.2022 MJ/m²
        double steadyNight = 0.039 * 60 * 43200 / 0.5;
        Assert.True(wall.Absorbed > 14 * steadyNight, $"{wall.Absorbed / steadyNight:0.0} times the steady loss");
        // a night's heat has gone only a few centimetres: √(α t) = √(3.25e-8 × 43200) = 3.75 cm
        Assert.InRange(wall.PenetrationDepth, 0.5 * 0.0375, 3 * 0.0375);
    }

    [Fact]
    public void ItsFluxFallsAsRootTAndApproachesTheSteadyLossOverWeeks()
    {
        // 0.5 m thick, surface held 40 K up: flux = I ΔT / √(π t) at 12 h = 216.33 × 40 / √(π × 43200) = 23.49 W/m²
        var wall = Regolith(0.5, 1);
        Soak(wall, -15, 43200, 10);
        Assert.InRange(wall.Flux / (216.33 * 40 / Math.Sqrt(Math.PI * 43200)), 0.97, 1.03);
        // steady: k A ΔT / L = 0.039 × 40 / 0.5 = 3.12 W (k A / L = 0.078 W/K for this wall). The time to get there is of order
        // L²/α = 0.25 / 3.25e-8 = 7.7e6 s = 89 days; the slowest mode decays as exp(-π² α t / (4 L²)) with a far face held at ground
        // temperature and the surface held: π²α/L² = 1.283e-6 /s, so e-folds in 9 days; after 60 days it is within e^-6.6
        Assert.Equal(0.078, wall.SteadyConductance, 6);
        Soak(wall, -15, 60 * 86400 - 43200, 600);
        Assert.InRange(wall.Flux / (0.078 * 40), 0.995, 1.01);
    }

    [Fact]
    public void ABodyHeatedByPowerPRisesAtPOverMC()
    {
        // 10 kg of iron, c = 450: 100 W for 1,000 s is 100,000 J, over 4,500 J/K = 22.22 K
        var iron = new HeatStore("iron", Substance.Of(Materials["iron"]), 10, 20) { HeatInput = 100, Zone = new Zone(Planet.Earth, 20) };
        for (int i = 0; i < 1000; i++) iron.Absorb(1);
        Assert.Equal(20 + 100_000.0 / 4500, iron.Temperature, 9);
        // water: 33 kg from 10 to 60 °C takes 33 × 4186 × 50 = 6.907 MJ
        var tank = new HeatStore("tank", Substance.Water, 33, 10);
        tank.AddHeat(33 * 4186 * 50);
        Assert.Equal(60, tank.Temperature, 9);
    }

    [Fact]
    public void AHotTankCoolsExponentiallyWithTimeConstantMCOverG()
    {
        // 33 kg of water, 60 °C, a 5 W/K film to still air at 10 °C: τ = 33 × 4186 / 5 = 27,626 s (7.67 h)
        var air = new Zone(Planet.Earth, 10);
        var tank = new HeatStore("tank", Substance.Water, 33, 60) { Conductance = 5, Zone = air };
        double tau = 33 * 4186 / 5.0;
        double step = tau / 1000;
        for (int i = 0; i < 1000; i++) tank.StepOpen(step);
        // after one τ it has lost 1 − 1/e of its excess: 10 + 50/e = 28.394 °C
        Assert.Equal(10 + 50 / Math.E, tank.Temperature, 3);
        for (int i = 0; i < 2000; i++) tank.StepOpen(step);
        Assert.Equal(10 + 50 * Math.Exp(-3), tank.Temperature, 3);
    }

    [Fact]
    public void WaterThatCoolsToZeroHoldsThereWhileItFreezes()
    {
        // 1 kg at 4 °C, a 1 W/K film to air at -10 °C. To 0 °C: τ ln(14/10) with τ = 4186 s: 1,408.4 s. Then it freezes at 0 °C:
        // 334 kJ out at 10 W: 33,400 s. Half frozen 16,700 s after reaching 0.
        var air = new Zone(Planet.Earth, -10);
        var tank = new HeatStore("t", Substance.Water, 1, 4) { Conductance = 1, Zone = air };
        double t = 0;
        while (tank.Temperature > 1e-9 && t < 5000) { tank.StepOpen(1); t++; }
        Assert.InRange(t, 1408 - 2, 1408 + 2);
        Assert.Equal(0, tank.Frozen, 3);
        for (int i = 0; i < 16_700; i++) tank.StepOpen(1);
        Assert.Equal(0, tank.Temperature, 9);
        Assert.Equal(0.5, tank.Frozen, 2);
        for (int i = 0; i < 16_700 + 100; i++) tank.StepOpen(1);
        Assert.Equal(1, tank.Frozen, 9);
        Assert.True(tank.Temperature < 0);
    }

    [Fact]
    public void ATankInARoomAndTheRoomCoolTogetherAsTheTwoNodeBalanceSays()
    {
        // The night's combined heat balance: a room (walls 2 W/K to the air outside at -20 °C, 3,000 J/K) holds 30 kg of water at
        // 60 °C joined to it by 6 W/K. C1 T1' = -U (T1 - T0) + G (Ts - T1), Cs Ts' = -G (Ts - T1), the 2×2 linear system with
        // eigenvalues λ of  [[-(U+G)/C1, G/C1], [G/Cs, -G/Cs]], C1 = 3000, Cs = 125,580.
        double U = 2, G = 6, C1 = 3000, Cs = 30 * 4186, T0 = -20;
        double a = -(U + G) / C1, b = G / C1, c = G / Cs, d = -G / Cs;
        double tr = a + d, det = a * d - b * c, disc = Math.Sqrt(tr * tr / 4 - det);
        double l1 = tr / 2 + disc, l2 = tr / 2 - disc;
        // x0 = (T1 - T0, Ts - T0) = (-20 + 20 - ... ) room starts at the outside's -20, tank at 60 °C: x0 = (0, 80). Eigenvectors (b, l - a).
        double[] v1 = [b, l1 - a], v2 = [b, l2 - a];
        // x0 = k1 v1 + k2 v2
        double k1 = (0 * v2[1] - 80 * v2[0]) / (v1[0] * v2[1] - v1[1] * v2[0]);
        double k2 = (80 * v1[0] - 0 * v1[1]) / (v1[0] * v2[1] - v1[1] * v2[0]);
        double Room(double t) => T0 + k1 * v1[0] * Math.Exp(l1 * t) + k2 * v2[0] * Math.Exp(l2 * t);
        double Tank(double t) => T0 + k1 * v1[1] * Math.Exp(l1 * t) + k2 * v2[1] * Math.Exp(l2 * t);

        var outside = new Zone(Planet.Earth, T0);
        var room = new Enclosure("room", 20, outside, outside.Pressure, T0, outside.Air) { Insulation = U, WallHeatCapacity = C1 - 0 };
        double gas = room.GasHeatCapacity;
        room.WallHeatCapacity = C1 - gas;                               // the room's whole capacity is 3,000 J/K
        var tank = new HeatStore("tank", Substance.Water, 30, 60) { Conductance = G, Zone = room };
        room.AddStore(tank);
        for (double t = 0; t < 12 * 3600 - 1e-9; t += 10)
        {
            room.Step(10);
            if (Math.Abs(t + 10 - 3600) < 1e-9 || Math.Abs(t + 10 - 43200) < 1e-9)
            {
                Assert.InRange(room.Temperature, Room(t + 10) - 0.15, Room(t + 10) + 0.15);
                Assert.InRange(tank.Temperature, Tank(t + 10) - 0.15, Tank(t + 10) + 0.15);
            }
        }
        // energy: what the tank lost, minus what the room holds more, left through the walls
        Assert.True(tank.Temperature < 60 && room.Temperature > T0);
    }

    [Fact]
    public void ALidClosedLeaksOnlyThroughItAndOpenGivesTheBinsFullRadiation()
    {
        // 40 kg of basalt at 200 °C in a bin with a 0.1 W/K lid, in air at -50 °C: τ = 40 × 840 / 0.1 = 336,000 s. Held for 10,000 s the rock cools by
        // (250 - 250 e^(-10000/336000)) = 250 × 0.029324 = 7.331 K to 192.669 °C
        var air = new Zone(Planet.Earth, -50);
        var rock = new HeatStore("rock", Substance.Of(Materials["basalt"]), 40, 200) { Area = 0.35, Zone = air };
        var bin = new HeatBin("bin", rock, 0.1);
        rock.Bin = bin;
        for (int i = 0; i < 1000; i++) rock.StepOpen(10);
        Assert.InRange(rock.Temperature, 200 - 250 * (1 - Math.Exp(-10000 / 336000.0)) - 0.02, 200 - 250 * (1 - Math.Exp(-10000 / 336000.0)) + 0.02);
        // open, the same rock radiates ε A σ (T⁴ - T_air⁴) = 0.9 × 0.35 × 5.670374e-8 × (473.15⁴ - 223.15⁴) = 0.315 × 5.670374e-8 × (5.0117e10 - 2.4797e9) = 850.9 W
        var hot = new HeatStore("rock", Substance.Of(Materials["basalt"]), 40, 200) { Area = 0.35, Zone = air };
        hot.Bin = new HeatBin("bin", hot, 0.1) { Open = 1 };
        hot.StepOpen(0.1);
        double radiated = 0.9 * 0.35 * 5.670374e-8 * (Math.Pow(473.15, 4) - Math.Pow(223.15, 4));
        Assert.Equal(850.9, radiated, 1);
        Assert.InRange(hot.Exchange, radiated * 0.995, radiated * 1.005);
    }

    [Fact]
    public void AThermostatOpensTheLidAt5DegreesAndShutsItAt40()
    {
        var bank = new HeatStore("bank", Substance.Of(Materials["iron"]), 20, 20);
        var rock = new HeatStore("rock", Substance.Of(Materials["basalt"]), 40, 200);
        var bin = new HeatBin("bin", rock, 0.1) { Sense = bank };
        bin.Step(); Assert.Equal(0, bin.Open);                      // between 5 and 40: left as it is (shut)
        bank.Temperature = 5; bin.Step(); Assert.Equal(1, bin.Open);
        bank.Temperature = 30; bin.Step(); Assert.Equal(1, bin.Open); // still open between
        bank.Temperature = 40; bin.Step(); Assert.Equal(0, bin.Open);
        bank.Temperature = 30; bin.Step(); Assert.Equal(0, bin.Open);
        Assert.Equal(1, bin.Openings);
    }

    /// <summary>A vault of a cavity <paramref name="cavity"/> m across in a 0.5 m regolith wall at -55 °C, a 20 kg bank at -55 °C and a bin of basalt at 200 °C, from dusk.</summary>
    private static (Enclosure Vault, HeatStore Bank, HeatStore Rock, HeatBin Bin) Vault(double cavity, double rockKg, double leak)
    {
        var ground = new Zone(Planet.Mars, -55);
        double area = 6 * cavity * cavity;
        var vault = new Enclosure("vault", cavity * cavity * cavity, ground, 610, -55, ground.Air)
        {
            Insulation = 0, WallEmissivity = 0.9, InnerArea = area,
            Wall = new HeatSlab(0.039, 1500, 800, 0.5, area, -55, -55),
        };
        double Surface(double kg, double density) => 6 * Math.Pow(kg / density, 2.0 / 3);
        var bank = new HeatStore("bank", new Substance("cells", 1000, 2000, 1000, 0), 20, -55) { Area = Surface(20, 2000), Zone = vault };
        var rock = new HeatStore("rock", Substance.Of(Materials["basalt"]), rockKg, 200) { Area = Surface(rockKg, 2900), Zone = vault };
        var bin = new HeatBin("bin", rock, leak) { Sense = bank, OpenBelow = 5, CloseAbove = 40 };
        rock.Bin = bin;
        vault.AddStore(bank);
        vault.AddStore(rock);
        return (vault, bank, rock, bin);
    }

    private static void Night(Enclosure vault, HeatBin bin, double hours, double dt)
    {
        for (double t = 0; t < hours * 3600 - 1e-9; t += dt) { bin.Step(); vault.Step(dt); }
    }

    [Fact]
    public void AFreshVaultSoaksUpTheHeatEleven_KgOfRockFailsAndFortyInATightCavityWorks()
    {
        // From dusk to 03:00 (9 local hours, 3,699 s each at 24 per sol: 33,291 s): the bank starts frozen at -55 °C.
        const double nine = 9 * 88775.0 / 24 / 3600;
        // 1 m cavity (6 m² of wall), 11 kg of rock: the rock holds 11 × 840 × 150 K = 1.39 MJ above 50 °C, the wall alone takes 2 I A ΔT √(t/π) with ΔT near 40 K,
        // 2 × 216 × 6 × 40 × √(33291/π) = 10.7 MJ: nothing is left for the bank (-50 °C, no thermostat can help)
        var (wide, wideBank, _, wideBin) = Vault(1.0, 11, 0.1);
        Night(wide, wideBin, nine, 10);
        Assert.True(wideBank.Temperature < -30, $"the bank in a 1 m cavity with 11 kg is at {wideBank.Temperature:0.0} °C at 03:00");
        // 0.5 m cavity (1.5 m² of wall), 40 kg: 40 × 840 × 150 = 5.0 MJ; the wall takes 2 × 216 × 1.5 × 40 × 106 = 2.7 MJ, the bank 20 × 1000 × 55 = 1.1 MJ to reach 0: it works
        var (tight, tightBank, _, tightBin) = Vault(0.5, 40, 0.1);
        Night(tight, tightBin, nine, 10);
        Assert.InRange(tightBank.Temperature, 0, 45);
    }

    [Fact]
    public void ALidThatLeaks01HoldsTheBankUnder45AndOneThatLeaks05LetsItOverheat()
    {
        // Eight sols of the same bin: a fresh 40 kg at 200 °C each dusk. By then the wall has warmed, and a shut lid's leak is what heats the bank.
        double Peak(double leak)
        {
            var (vault, bank, rock, bin) = Vault(0.5, 40, leak);
            double peak = -100;
            for (int sol = 0; sol < 8; sol++)
            {
                rock.Temperature = 200;
                for (double t = 0; t < 88775; t += 10) { bin.Step(); vault.Step(10); peak = sol >= 3 ? Math.Max(peak, bank.Temperature) : peak; }
            }
            return peak;
        }
        Assert.True(Peak(0.1) <= 45, "0.1 W/K holds the bank at or under 45 °C");
        Assert.True(Peak(0.5) >= 55, "0.5 W/K lets it overheat");
    }

    [Fact]
    public void TheVaultAnswersTheSameWhateverTheStep()
    {
        double AtThreeAm(double dt)
        {
            var (vault, bank, _, bin) = Vault(0.5, 40, 0.1);
            Night(vault, bin, 9 * 88775.0 / 24 / 3600, dt);
            return bank.Temperature;
        }
        double slow = AtThreeAm(30), fast = AtThreeAm(1.0 / 8);
        Assert.InRange(slow, fast - 1.0, fast + 1.0);
    }
}
