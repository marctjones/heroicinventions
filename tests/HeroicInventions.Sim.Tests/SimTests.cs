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

    /// <summary>
    /// A bellows' own air (density x its m3/s) on top of the natural
    /// draught (power / density x air-fuel ratio) scales the burn rate by
    /// their ratio; the energy a burned kilogram gives up stays mass x
    /// density regardless.
    /// </summary>
    [Fact]
    public void AirflowScalesBurnRateButNotEnergyPerKilogram()
    {
        var pot = new Boiler(1, heatInputW: 0);
        var fire = new Hearth(pot, 5000, 1, "wood", 0.5) { Airflow = 0.005 };
        double density = Hearth.EnergyDensity("wood");
        double natural = 5000 / density * Hearth.AirFuelRatio("wood");
        double expectedDraught = (natural + Physics.AirDensity * 0.005) / natural;
        Assert.Equal(expectedDraught, fire.Draught, precision: 9);

        const double dt = 0.01;
        fire.Step(dt);
        Assert.Equal(5000 * expectedDraught * dt / density, fire.FuelBurned, precision: 9);
        Assert.Equal(fire.FuelBurned * density, fire.EnergyReleased, precision: 3);
    }

    [Fact]
    public void NoBellowsLeavesDraughtAtOne()
    {
        var pot = new Boiler(1, heatInputW: 0);
        var fire = new Hearth(pot, 5000, 1, "wood", 0.5);
        Assert.Equal(1, fire.Draught, precision: 12);
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

public class FloatValveTests
{
    /// <summary>
    /// A 2 L/s spring into a 0.25 m² tank drained by a pipe passing C·h: the
    /// level settles where Q_feed·(s − h)/b = C·h, h = (Q_feed·s/b) / (Q_feed/b + C).
    /// </summary>
    [Fact]
    public void AFloatValveHoldsTheLevelWhereFeedAndDrawBalance()
    {
        const double feed = 0.002, shut = 0.4, travel = 0.02, c = 0.002;
        var net = new FluidNetwork();
        var cistern = net.AddTank(new Tank("cistern", 0, 0.25, 0.6, waterVolume: 0.05));
        var sink = net.AddTank(new Tank("sink", -5, 100, 10));
        var drain = net.AddPipe(new Pipe("drain", cistern, 0, sink, 0, c));
        var spring = new WaterSource("spring", cistern, feed) { Valve = new FloatValve(cistern, shut, travel) };
        for (int i = 0; i < 20000; i++) { spring.Step(0.01); net.Step(0.01); }

        double h = feed * shut / travel / (feed / travel + c);
        Assert.Equal(h, cistern.Level, precision: 4);
        Assert.Equal(drain.Flow, spring.Flow, precision: 6);
        Assert.InRange(cistern.Level, shut - travel, shut);
    }

    [Fact]
    public void ThePlugSeatsAtTheShutLevelAndOpensInProportionBelowIt()
    {
        double Opening(double level) => new FloatValve(new Tank("t", 0, 1, 1, waterVolume: level), 0.5, 0.04).Opening;
        Assert.Equal(0, Opening(0.5));
        Assert.Equal(0.25, Opening(0.49), precision: 12);
        Assert.Equal(1, Opening(0.3));
        var tank = new Tank("t", 0, 1, 1, waterVolume: 0.5);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FloatValve(tank, 0.5, 0));

        var spring = new WaterSource("s", tank, 0.01) { Valve = new FloatValve(tank, 0.3, 0.1) };
        spring.Step(0.01);
        Assert.Equal(0, spring.Flow);                       // brimming past the seat: nothing comes in
    }

    /// <summary>Piped or channelled in, the feed passes Opening × what it would unchecked.</summary>
    [Fact]
    public void APipeOrChannelFeedPassesItsOpeningTimesTheUncheckedFlow()
    {
        var net = new FluidNetwork();
        var supply = net.AddTank(new Tank("supply", 2, 10, 1, waterVolume: 5));      // surface 2.5 m
        var cistern = net.AddTank(new Tank("cistern", 0, 1, 1, waterVolume: 0.39));  // 1 cm below a 40 cm seat
        var pipe = net.AddPipe(new Pipe("feed", supply, 2, cistern, 1, 0.001) { Valve = new FloatValve(cistern, 0.40, 0.04) });
        net.Step(0.001);
        Assert.Equal(0.25 * 0.001 * (2.5 - 1.0), pipe.Flow, precision: 9);

        var pool = new Tank("pool", 1, 1, 1, waterVolume: 0.1);                        // 10 cm over a lip at 1 m
        var basin = new Tank("basin", 0, 1, 1, waterVolume: 0.38);                     // 2 cm below the seat
        var race = new Channel("race", pool, 1, basin, 0.9, width: 0.3, length: 2) { Valve = new FloatValve(basin, 0.40, 0.04) };
        race.Step(0.001);
        Assert.Equal(0.5 * Channel.WeirFlow(0.3, 0.1), race.Flow, precision: 9);
    }
}

public class TankLeakTests
{
    private const double A = 0.25, Hole = 5e-4, H0 = 0.8;

    private static (FluidNetwork Net, Tank Tank, TankLeak Leak) Barrel(double holeHeight, Tank? into = null)
    {
        var net = new FluidNetwork();
        var tank = net.AddTank(new Tank("barrel", 0.3, A, 1.0, waterVolume: A * H0));
        if (into is not null) net.AddTank(into);
        return (net, tank, net.AddLeak(new TankLeak(tank, holeHeight, Hole, into)));
    }

    /// <summary>√(h − hole) falls at Cd·a·√(2g)/(2A); the level stops at the hole after T = (A/Cd·a)·√(2H/g).</summary>
    [Fact]
    public void TheLevelDrawsDownAsTorricelliPredictsAndStopsAtTheHole()
    {
        const double hole = 0.10;
        double rate = 0.6 * Hole * Math.Sqrt(2 * 9.81) / (2 * A);
        double drainTime = A / (0.6 * Hole) * Math.Sqrt(2 * (H0 - hole) / 9.81);
        var (net, tank, leak) = Barrel(hole);
        Assert.Equal(Math.Sqrt(H0 - hole) / rate, drainTime, precision: 9);   // the two forms of the law agree

        for (int i = 0; i < 100 * 200; i++) net.Step(0.005);
        Assert.Equal(hole + Math.Pow(Math.Sqrt(H0 - hole) - rate * 100, 2), tank.Level, precision: 3);
        Assert.Equal(0.6 * Hole * Math.Sqrt(2 * 9.81 * (tank.Level - hole)), leak.Flow, precision: 6);

        for (int i = 0; i < 400 * 200; i++) net.Step(0.005);
        Assert.Equal(hole, tank.Level, precision: 6);
        Assert.Equal(0, leak.Flow, precision: 9);
        Assert.Equal(A * (H0 - hole), leak.Lost, precision: 6);
    }

    [Fact]
    public void ALowerHoleLeaksFasterAndFurtherThanAHigherOne()
    {
        var (netLow, tankLow, low) = Barrel(0.10);
        var (netHigh, tankHigh, high) = Barrel(0.40);
        netLow.Step(0.005); netHigh.Step(0.005);
        Assert.True(low.Flow > high.Flow);
        for (int i = 0; i < 100 * 200; i++) { netLow.Step(0.005); netHigh.Step(0.005); }
        Assert.True(tankLow.Level < tankHigh.Level);
        for (int i = 0; i < 500 * 200; i++) { netLow.Step(0.005); netHigh.Step(0.005); }
        Assert.Equal(0.10, tankLow.Level, precision: 6);
        Assert.Equal(0.40, tankHigh.Level, precision: 6);
    }

    /// <summary>A catch tank under the jet gets every litre; when it is full the hole stops.</summary>
    [Fact]
    public void ACatchTankReceivesWhatLeavesAndLimitsTheLeakWhenFull()
    {
        var catchTank = new Tank("catch", 0, 0.5, 0.3);
        var (net, tank, leak) = Barrel(0.10, catchTank);
        for (int i = 0; i < 60 * 200; i++) net.Step(0.005);
        Assert.Equal(A * H0, tank.WaterVolume + catchTank.WaterVolume, precision: 9);
        Assert.Equal(leak.Lost, catchTank.WaterVolume, precision: 9);
        for (int i = 0; i < 400 * 200; i++) net.Step(0.005);
        Assert.Equal(catchTank.Capacity, catchTank.WaterVolume, precision: 9);   // 0.15 m³ < what the barrel would give: full first
        Assert.Equal(A * H0, tank.WaterVolume + catchTank.WaterVolume, precision: 9);
    }

    [Fact]
    public void APluggedHoleOrADryTankLeaksNothingAndASeepTakesAFixedVolumeUntilDry()
    {
        var (net, tank, leak) = Barrel(0.10);
        leak.Area = 0;
        net.Step(1);
        Assert.Equal(A * H0, tank.WaterVolume, precision: 12);

        var seepNet = new FluidNetwork();
        var seep = seepNet.AddTank(new Tank("seep", 0, A, 1.0, waterVolume: 0.01));
        var seepLeak = seepNet.AddLeak(new TankLeak(seep, 0, 0) { Evaporation = 1e-3 });
        seepNet.Step(4);
        Assert.Equal(0.01 - 4e-3, seep.WaterVolume, precision: 9);
        seepNet.Step(20);
        Assert.Equal(0, seep.WaterVolume, precision: 12);
        Assert.Equal(0.01, seepLeak.Evaporated, precision: 9);
    }
}

public class SafetyValveTests
{
    private const double Q = 10_000, H = 2, M = 10, C = 4186, L = 2.257e6, Atm = 101_325;

    private static (Boiler Boiler, SafetyValve? Valve) Rig(bool valve, double burst = 200e3)
    {
        var b = new Boiler(M, 20, Q) { BurstPressure = burst };
        SafetyValve? v = valve ? new SafetyValve(100e3, 0.008) : null;
        if (v is not null) b.Valves.Add(v);
        return (b, v);
    }

    private static double WarmTime(double m, double t0, double t1) =>
        m * C / H * Math.Log((20 + Q / H - t0) / (20 + Q / H - t1));

    /// <summary>The gauge pressure at which the valve, open (P − lift)/(0.1·lift), passes (Q − h(T − 20))/L.</summary>
    private static double HoldPressure(SafetyValve v)
    {
        double lo = v.LiftPressure, hi = v.LiftPressure * 1.1;
        for (int i = 0; i < 60; i++)
        {
            double mid = (lo + hi) / 2, t = Boiler.SaturationTemperature(Atm + mid);
            if (v.Discharge(mid, Atm + mid, t) > (Q - H * (t - 20)) / L) hi = mid; else lo = mid;
        }
        return lo;
    }

    [Fact]
    public void SaturationTemperatureInvertsTheAntoineEquation()
    {
        foreach (double t in new[] { 60.0, 110, 133.9 })
            Assert.Equal(t, Boiler.SaturationTemperature(Boiler.SaturationPressure(t)), precision: 9);
    }

    /// <summary>Above the critical pressure ratio the vent is the choked nozzle, Cd·A·P₀·√(k/RT₀)·(2/(k+1))^((k+1)/2(k−1)).</summary>
    [Fact]
    public void AFullyLiftedValveChokesAsASonicNozzle()
    {
        var v = new SafetyValve(100e3, 0.008);
        double k = 1.3, p0 = 201_325, t0 = 120.5, r = 8.314 / 0.018015;
        double choked = 0.8 * Math.PI * 0.008 * 0.008 / 4 * p0 * Math.Sqrt(k / (r * (t0 + 273.15))) * Math.Pow(2 / (k + 1), (k + 1) / (2 * (k - 1)));
        Assert.Equal(choked, v.Capacity(p0, t0), precision: 9);
        Assert.Equal(0, v.Discharge(99e3, Atm + 99e3, 120), precision: 12);    // seated below its lift
        Assert.Equal(0.5, v.OpeningAt(105e3), precision: 12);
    }

    /// <summary>It lifts where the sealed warming curve reaches 100 kPa, then holds where venting carries off the fire's heat.</summary>
    [Fact]
    public void TheValveHoldsThePressureWhereItVentsWhatTheFireBrings()
    {
        var (b, v) = Rig(valve: true);
        double tLift = WarmTime(M, 20, Boiler.SaturationTemperature(Atm + 100e3));   // 425.1 s
        for (int i = 0; i < 42_000; i++) b.Step(0.01, 0);
        Assert.Equal(0, v!.Vented);
        Assert.InRange(tLift, 420, 430);
        for (int i = 0; i < 38_000; i++) b.Step(0.01, 0);                          // to 800 s
        double hold = HoldPressure(v), tHold = Boiler.SaturationTemperature(Atm + hold);
        Assert.InRange(hold, 103e3, 104e3);
        Assert.Equal(hold, b.GaugePressure, precision: 1);
        Assert.Equal((Q - H * (tHold - 20)) / L, v.Flow, precision: 7);
        Assert.Equal(M - v.Vented, b.WaterMass, precision: 9);
        Assert.False(b.Burst);
    }

    [Fact]
    public void WithoutAValveItBurstsAtItsRatingWhenTheWarmingCurveSays()
    {
        var (b, _) = Rig(valve: false);
        double tBurst = Boiler.SaturationTemperature(Atm + 200e3);
        for (int i = 0; i < 60_000 && !b.Burst; i++) b.Step(0.01, 0);
        Assert.True(b.Burst);
        Assert.Equal(WarmTime(M, 20, tBurst), b.BurstTime, precision: 1);        // 482.3 s
        Assert.InRange(b.BurstGauge, 200e3, 200e3 + 50);
        Assert.Equal(M * C * (tBurst - 100) / L, b.Flashed, precision: 3);      // 0.629 kg
        Assert.Equal(0, b.GaugePressure);
        Assert.True(b.IsDry);
        b.AddWater(1, 20);
        Assert.Equal(0, b.WaterMass);                                           // a burst boiler holds nothing
    }

    [Fact]
    public void AnUnratedBoilerNeverBursts()
    {
        var b = new Boiler(M, 20, Q);
        for (int i = 0; i < 70_000; i++) b.Step(0.01, 0);
        Assert.False(b.Burst);
        Assert.True(b.GaugePressure > 200e3);
    }
}

public class LiftPumpTests
{
    private const double RhoG = 1000 * 9.81, Atm = 101_325, Bore = 0.15, Stroke = 0.5, Eta = 0.8;
    private static readonly double A = Math.PI * Bore * Bore / 4;

    /// <summary>A pump whose bucket's lowest point stands `lift` m over a well so wide its surface never moves.</summary>
    private static (LiftPump Pump, Tank Well, Tank Cistern) Rig(double lift, double force = double.PositiveInfinity)
    {
        var well = new Tank("well", 0, 1e6, 2, 1e6);                     // surface 1 m up
        var cistern = new Tank("cistern", lift + 1, 10, 10);
        var pump = new LiftPump("p", well, cistern, 1 + lift, Bore, Stroke) { Efficiency = Eta, Rpm = 30, Force = force };
        return (pump, well, cistern);
    }

    private static void Strokes(LiftPump p, int n) { for (int i = 0; i < 200 * n; i++) p.Step(0.01); } // 2 s a stroke at 30 rpm

    [Fact]
    public void TheSuctionLimitIsTheAtmosphereLessTheVapourPressure()
    {
        Assert.Equal((Atm - Boiler.SaturationPressure(20)) / RhoG, LiftPump.SuctionLimit(20), precision: 12);
        Assert.InRange(LiftPump.SuctionLimit(20), 10.09, 10.10);
        Assert.InRange(LiftPump.SuctionLimit(60), 8.2, 8.4);   // hot water boils under a shorter column
    }

    /// <summary>Well within the limit: A·S·η a stroke, ρ·g·A·(spout − surface)·S of work, η of it into the water.</summary>
    [Fact]
    public void BelowTheLimitEachStrokeDeliversSweptVolumeTimesEfficiency()
    {
        var (p, _, cistern) = Rig(lift: 5);
        Strokes(p, 10);
        Assert.Equal(10, p.Strokes);
        Assert.Equal(10 * A * Stroke * Eta, p.Delivered, precision: 9);
        Assert.Equal(p.Delivered, cistern.WaterVolume, precision: 12);
        Assert.Equal(10 * RhoG * A * 5.5 * Stroke, p.Work, precision: 3);
        Assert.Equal(Eta, p.Lifted / p.Work, precision: 9);
        Assert.Equal(RhoG * A * 5.5, p.MaxPull, precision: 3);
        Assert.False(p.Broken);
    }

    /// <summary>The bucket's foot 9.9 m up: the water follows it only the 0.19 m left under the limit.</summary>
    [Fact]
    public void WhenTheLimitFallsInsideTheStrokeOnlyThatMuchFills()
    {
        var (p, _, _) = Rig(lift: 9.9);
        Strokes(p, 10);
        Assert.True(p.Broken);
        Assert.Equal(10 * A * Eta * (LiftPump.SuctionLimit(20) - 9.9), p.Delivered, precision: 7);
        Assert.Equal(10 * RhoG * A * (9.9 + Stroke) * (LiftPump.SuctionLimit(20) - 9.9), p.Work, precision: 2); // the vacuum's work comes back
    }

    /// <summary>Past the limit no force helps: the pull tops out at A·(P_atm − P_v), and nothing is lifted.</summary>
    [Fact]
    public void PastTheLimitItLiftsNothingHoweverHardItIsPulled()
    {
        var (p, well, _) = Rig(lift: 10.2, force: 1e7);
        Strokes(p, 10);
        Assert.Equal(10, p.Strokes);
        Assert.Equal(0, p.Delivered);
        Assert.Equal(1e6, well.WaterVolume);
        Assert.Equal(A * (Atm - Boiler.SaturationPressure(20)), p.MaxPull, precision: 6);
        Assert.Equal(0, p.Work, precision: 6);
        Assert.Equal(LiftPump.SuctionLimit(20), p.Column, precision: 12);
    }

    /// <summary>Once primed the rod carries ρ·g·A·(spout − surface); a drive that can't give it stops at its next upstroke.</summary>
    [Fact]
    public void ADriveTooWeakForTheColumnStalls()
    {
        var (p, _, _) = Rig(lift: 5);
        Strokes(p, 1);
        p.Force = RhoG * A * 5.5 - 1;
        Strokes(p, 3);
        Assert.True(p.Stalled);
        Assert.Equal(1, p.Strokes);
        Assert.Equal(A * Stroke * Eta, p.Delivered, precision: 9);
        p.Force = RhoG * A * 5.5 + 1;
        Strokes(p, 3);
        Assert.False(p.Stalled);
        Assert.Equal(4, p.Strokes);
        Assert.Equal(4 * A * Stroke * Eta, p.Delivered, precision: 9);
    }
}
