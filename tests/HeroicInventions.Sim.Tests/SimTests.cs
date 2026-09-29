using HeroicInventions.Sim;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;
using HeroicInventions.Sim.Parts;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Tests;

public class MaterialTests
{
    [Fact]
    public void DefaultLibraryLoadsWoodAndStone()
    {
        var lib = MaterialLibrary.LoadDefault();
        Assert.Equal(MaterialCategory.Wood, lib["oak"].Category);
        Assert.Equal(MaterialCategory.Stone, lib["granite"].Category);
        Assert.True(lib["cedar"].Density < lib["oak"].Density);
    }

    [Fact]
    public void OneCubicMetreOfMarbleIsAboutTwoPointSevenTonnes() =>
        Assert.Equal(2700, MaterialLibrary.LoadDefault()["marble"].MassOf(1.0), precision: 0);

    [Fact]
    public void WoodIsWeakAcrossTheGrain()
    {
        var oak = MaterialLibrary.LoadDefault()["oak"];
        // 2 kN on a 1 cm² (1e-4 m²) section = 20 MPa: fine along the grain, fails across it.
        Assert.False(oak.FailsInTension(2000, 1e-4));
        Assert.True(oak.FailsInTension(2000, 1e-4, acrossGrain: true));
    }
}

public class FluidTests
{
    [Fact]
    public void ConnectedOpenTanksLevelOutAndConserveWater()
    {
        var net = new FluidNetwork();
        var a = net.AddTank(new Tank("A", 0, 1, 2, waterVolume: 1.5));
        var b = net.AddTank(new Tank("B", 0, 1, 2, waterVolume: 0.5));
        net.AddPipe(new Pipe("AB", a, 0, b, 0, conductance: 0.5));
        double before = net.TotalWater;

        for (int i = 0; i < 600; i++) net.Step(0.1);

        Assert.Equal(a.Level, b.Level, precision: 3);
        Assert.Equal(before, net.TotalWater, precision: 9);
    }

}

public class SteamTests
{
    [Fact]
    public void SaturationPressureIsOneAtmosphereAtBoiling() =>
        Assert.InRange(Boiler.SaturationPressure(100), 100_000, 102_500);

    [Fact]
    public void AeolipileSpinsUpOnceTheWaterBoils()
    {
        var aeolipile = new Aeolipile(new Boiler(waterMassKg: 0.3, heatInputW: 3000));

        for (int i = 0; i < 20_000; i++) aeolipile.Step(0.01); // 200 s

        Assert.True(aeolipile.Boiler.Temperature >= 100);
        Assert.True(aeolipile.Rpm > 100, $"expected a spinning rotor, got {aeolipile.Rpm:F0} rpm");
    }
}

public class AssemblyTests
{
    private static readonly PartDef Axle = new("axle", "Oak axle", "oak", 0.001,
        [new PortDef("end", PortKind.Axle, 0, 0, 0)]);
    private static readonly PartDef Pipe = new("pipe", "Bronze pipe", "bronze", 0.0001,
        [new PortDef("in", PortKind.PipeFitting, 0, 0, 0)]);

    [Fact]
    public void RejectsConnectingAnAxleToAPipe()
    {
        var asm = new Assembly();
        var axle = asm.Add(Axle);
        var pipe = asm.Add(Pipe);
        Assert.Throws<InvalidOperationException>(() => asm.Connect(axle, "end", pipe, "in"));
    }
}

public class WaterLiftTests
{
    // Vitruvius's 4 m screw: core 0.25 m across, whole 0.5 m, pitch = core
    // circumference, eight starts, blades a fifth of their spacing thick.
    private const double R = 0.25, Core = 0.125, Pitch = Math.PI * 0.25, Blade = Pitch / 8 / 5;
    private static double Pocket(double tiltDeg) =>
        Mechanics.WaterLift.ScrewPocketVolume(R, Core, Pitch, 8, Blade, tiltDeg);

    [Fact]
    public void AVitruvianScrewHoldsWaterButLessThanItsChannels()
    {
        double pocket = Pocket(36.87); // the 3-4-5 slope
        double channelPerTurn = Math.PI * (R * R - Core * Core) * (Pitch / 8 - Blade);
        Assert.InRange(pocket, 0.05 * channelPerTurn, channelPerTurn);
    }

    [Fact]
    public void SteeperScrewsCarryLess() =>
        Assert.True(Pocket(20) > Pocket(30) && Pocket(30) > Pocket(40));

    [Fact]
    public void PastTheCriticalSlopeTheChannelNeverDipsAndNothingIsCarried()
    {
        // pitch = 2π·core, so tan θ = 2π·core/pitch = 1 at 45°
        Assert.True(Pocket(44) > 0);
        Assert.Equal(0, Pocket(46));
    }
}

public class AtmosphericCylinderTests
{
    // Dudley Castle, 1712: a 21-inch (0.53 m) cylinder
    private static Mechanics.AtmosphericCylinder Cylinder(Boiler boiler) =>
        new("cyl", boiler, bore: 0.53, stroke: 1.8, injectionTemperatureC: 60);

    [Fact]
    public void SteamFillsTheCylinderToTheBoilersPressure()
    {
        var boiler = new Boiler(2000, temperatureC: 105, heatInputW: 0);
        var c = Cylinder(boiler);
        c.PistonHeight = 1.0;
        c.Prime();
        for (int i = 0; i < 1000; i++) c.Step(0.01);
        Assert.InRange(c.Pressure, 0.97 * boiler.AbsolutePressure, 1.01 * boiler.AbsolutePressure);
        Assert.True(c.SteamUsed > 0);
    }

    [Fact]
    public void InjectionLeavesAVacuumAndTheAtmospherePushesThePistonDown()
    {
        var c = Cylinder(new Boiler(2000, temperatureC: 105, heatInputW: 0));
        c.PistonHeight = 1.8;       // at the top: the tappet opens the injection
        c.Prime();
        for (int i = 0; i < 100; i++) c.Step(0.01); // 1 s, several condensation time constants
        Assert.True(c.Injecting);
        // what's left is vapour at 60 °C: ~20 kPa
        Assert.InRange(c.Pressure, 18_000, 22_000);
        // so ~81 kPa of atmosphere on 0.22 m² of piston: ~18 kN down
        Assert.InRange(c.Force, 17_000, 19_000);
        Assert.Equal(1, c.Strokes);
    }

    [Fact]
    public void ThePlugRodSwitchesToSteamAtTheBottomOfTheStroke()
    {
        var c = Cylinder(new Boiler(2000, temperatureC: 105, heatInputW: 0));
        c.PistonHeight = 1.8;
        c.Prime();
        c.Step(0.01);
        Assert.True(c.Injecting);
        c.PistonHeight = 0;
        c.Step(0.01);
        Assert.False(c.Injecting);
    }
}

public class OpenChannelTests
{
    [Fact]
    public void NormalDepthCarriesTheFlowByManning()
    {
        double q = 0.5, w = 1.2, s = 0.006;
        double d = Channel.NormalDepth(q, w, s);
        double area = w * d, radius = area / (w + 2 * d);
        Assert.Equal(q, area * Math.Pow(radius, 2.0 / 3) * Math.Sqrt(s) / Channel.Roughness, precision: 6);
    }

    [Fact]
    public void APoolFedSteadilySettlesWhereItsWeirPassesTheSameFlow()
    {
        // a spring of 200 L/s into a 4 m² pool that spills over a 1 m lip, 0.5 m up
        var pool = new Tank("pool", 0, 4, 2);
        var spring = new WaterSource("spring", pool, 0.2);
        var weir = new Channel("weir", pool, 0.5, null, 0.4, width: 1, length: 5);
        for (int i = 0; i < 60_000; i++) { spring.Step(0.01); weir.Step(0.01); } // 10 min
        double expectedHead = Math.Pow(0.2 / (1.705 * 1), 2.0 / 3);           // h = (Q/1.705b)^(2/3)
        // read just after the weir's step: one step's inflow (0.2·0.01/4 = 0.5 mm) below the balance level
        Assert.InRange(pool.SurfaceElevation, 0.5 + expectedHead - 0.001, 0.5 + expectedHead + 0.0001);
        Assert.Equal(0.2, weir.Flow, precision: 4);
        Assert.True(weir.Velocity > 0 && weir.Depth > 0);
    }

    [Fact]
    public void WaterIsNeitherMadeNorLostBetweenTwoTanks()
    {
        var upper = new Tank("upper", 1, 2, 1, waterVolume: 1.5);
        var lower = new Tank("lower", 0, 2, 2);
        var race = new Channel("race", upper, 1.2, lower, 1.0, width: 0.5, length: 3);
        for (int i = 0; i < 10_000; i++) race.Step(0.01);
        Assert.Equal(1.5, upper.WaterVolume + lower.WaterVolume, precision: 9);
        Assert.Equal(1.2, upper.SurfaceElevation, precision: 2); // drained to the lip, no further
    }

    [Fact]
    public void AnOpenedGatePassesTheOrificeFlowAndAShutOneNothing()
    {
        // 1.0 m of water over a 0.5 m sill; a 30 cm gate raised 10 cm (opening 0.1 of 1 m)
        var pool = new Tank("pool", 0, 10, 3, waterVolume: 15);
        var race = new Channel("race", pool, 0.5, null, 0.4, width: 0.3, length: 5) { Gate = new SluiceGate(0.3, 1.0, 0.1) };
        race.Step(0.001);
        double h = 1.5 - (0.5 + 0.05);                                    // above the slot's middle
        Assert.Equal(0.6 * 0.3 * 0.1 * Math.Sqrt(2 * 9.81 * h), race.Flow, precision: 6);
        Assert.True(race.Flow < Channel.WeirFlow(0.3, 1.0), "the gate, not the lip, is holding the water back");

        race.Gate.Opening = 0;
        race.Step(0.001);
        Assert.Equal(0, race.Flow);
        Assert.Equal(0, race.Depth);                                       // the channel below runs dry at once
        Assert.Equal(0, race.Velocity);
    }

    [Fact]
    public void AShutGateIsADamThatOverflowsAboveItsTop()
    {
        // a 40 cm plate shut on a sill 0.5 m up: its crest is 0.9 m; water 10 cm above that spills as a weir
        var pool = new Tank("pool", 0, 10, 3, waterVolume: 10);
        var race = new Channel("race", pool, 0.5, null, 0.4, width: 0.3, length: 5) { Gate = new SluiceGate(0.3, 0.4, 0) };
        race.Step(0.001);
        Assert.Equal(Channel.WeirFlow(0.3, 0.1), race.Flow, precision: 9);
        Assert.Equal(0, race.Gate.Flow);

        var lower = new Tank("lower", 0, 10, 3, waterVolume: 8.5);         // 0.85 m: below the crest, held back entirely
        var held = new Channel("held", lower, 0.5, null, 0.4, width: 0.3, length: 5) { Gate = new SluiceGate(0.3, 0.4, 0) };
        held.Step(0.001);
        Assert.Equal(0, held.Flow);
    }

    [Fact]
    public void AGateClearOfTheWaterLeavesThePlainWeir()
    {
        // drawn up 60 cm, its lower edge stands above water 10 cm over the lip: the lip's weir flow, untouched
        var pool = new Tank("pool", 0, 10, 3, waterVolume: 6);
        var race = new Channel("race", pool, 0.5, null, 0.4, width: 0.3, length: 5) { Gate = new SluiceGate(0.3, 1.0, 0.6) };
        race.Step(0.001);
        Assert.Equal(Channel.WeirFlow(0.3, 0.1), race.Flow, precision: 9);
    }
}

public class QuenchAndFeedTests
{
    private static readonly double QuenchHeat = 4186 * 80 + 2.257e6;

    /// <summary>
    /// Water poured faster than the fire boils it off soaks in at
    /// q − P/L; the fuel burns down at P/ρ; the fire drowns where they
    /// meet, t = F0 / (q − P/L + P/ρ). Until then it heats nothing.
    /// </summary>
    [Fact]
    public void AFireDrownsWhenTheWaterSoakedInOutweighsTheFuelLeft()
    {
        var pot = new Boiler(1, heatInputW: 0);
        var fire = new Hearth(pot, 20000, 2, "wood", 0.3);
        double q = 0.02, dt = 0.01, t = 0;
        while (fire.Lit && t < 1000)
        {
            fire.Douse(q * dt);
            fire.Step(dt);
            Assert.Equal(0, pot.HeatInput, precision: 9);
            t += dt;
        }
        double predicted = 2 / (q - 20000 / QuenchHeat + 20000 / 15e6);
        Assert.True(fire.Drowned);
        Assert.InRange(t, predicted - 0.05, predicted + 0.05);
        Assert.Equal(fire.Doused, fire.Soak + fire.Boiled, precision: 9);
    }

    [Fact]
    public void AFireThatBoilsOffEverythingHeatsWithWhatIsLeft()
    {
        var pot = new Boiler(1, heatInputW: 0);
        var fire = new Hearth(pot, 60000, 2, "wood", 0.3);
        fire.Douse(0.02 * 0.01);
        fire.Step(0.01);
        Assert.False(fire.Drowned);
        Assert.Equal(0, fire.Soak, precision: 12);
        Assert.Equal((60000 - 0.02 * QuenchHeat) * 0.3, pot.HeatInput, precision: 6);
    }

    [Fact]
    public void FeedWaterMixesByMassWeightedTemperature()
    {
        var copper = new Boiler(4, temperatureC: 90, heatInputW: 0);
        copper.AddWater(2, 20);
        Assert.Equal(6, copper.WaterMass, precision: 12);
        Assert.Equal((4 * 90 + 2 * 20) / 6.0, copper.Temperature, precision: 12);
        Assert.Equal(2, copper.WaterFed, precision: 12);
    }
}

public class BearingTests
{
    /// <summary>A flywheel coasting on a dry pin: constant torque μNr stops it at t = Iω₀/τ, all ½Iω₀² as heat, the pin sliding r·ω₀·t/2.</summary>
    [Fact]
    public void DryPinStopsAFlywheelAtIOmegaOverTau()
    {
        double inertia = 2, omega0 = 10, load = 100, dt = 0.001;
        var bearing = new Bearing(0.02) { Mu = 0.3, WearRate = 1e-4 };
        double tau = 0.3 * load * 0.02, omega = omega0, t = 0;
        while (omega > 0) { omega = bearing.Slow(omega, inertia, load, dt); t += dt; }
        Assert.Equal(inertia * omega0 / tau, t, precision: 2);
        Assert.Equal(0.5 * inertia * omega0 * omega0, bearing.Heat, precision: 9);
        Assert.Equal(0.02 * omega0 * (inertia * omega0 / tau) / 2, bearing.Sliding, precision: 3);
        Assert.Equal(1e-4 * load * bearing.Sliding, bearing.Wear, precision: 12);
    }

    /// <summary>Viscous drag alone: ω = ω₀·exp(−c·t / I), never quite stopping.</summary>
    [Fact]
    public void GreasedPinSlowsAFlywheelExponentially()
    {
        double inertia = 2, omega = 10;
        var bearing = new Bearing(0.02) { Drag = 0.5 };
        for (int i = 0; i < 4000; i++) omega = bearing.Slow(omega, inertia, 100, 0.001);
        Assert.Equal(10 * Math.Exp(-0.5 * 4 / inertia), omega, precision: 9);
        Assert.Equal(0.5 * inertia * (100 - omega * omega), bearing.Heat, precision: 9);
    }
}
