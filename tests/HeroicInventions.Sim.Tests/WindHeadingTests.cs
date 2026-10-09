using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #193: the wind has a heading. Only the part of it along the sails' axle goes through the disc, v·cos θ,
/// so a mill that cannot turn takes ½ρAv³·cos³θ; one with a vane turns itself into the wind.
/// </summary>
public class WindHeadingTests
{
    private static readonly double Rho = Physics.AirDensity;
    private const double R = 10, V = 6, Mass = 1500;
    private static double WindPowerSquareOn => 0.5 * Rho * Math.PI * R * R * V * V * V;       // ½ρAv³ at 6 m/s, W

    private static Windmill Mill(double windFrom = 90, double facing = 90, bool vane = false, double veer = 0) =>
        new("mill", R, Mass * R * R / 3) { Wind = V, WindFromDeg = windFrom, FacingDeg = facing, Vane = vane, Veer = veer };

    [Theory]
    [InlineData(0, 1.0)]                       // square on
    [InlineData(30, 0.6495190528383291)]       // cos³ 30° = (√3/2)³
    [InlineData(45, 0.3535533905932738)]       // (1/√2)³
    [InlineData(60, 0.125)]                    // (1/2)³
    [InlineData(80, 0.0052361332501977675)]               // cos³ 80° = 0.173648³
    [InlineData(90, 0.0)]                      // edge-on
    [InlineData(120, 0.0)]                     // back to the wind: the sails take nothing
    [InlineData(180, 0.0)]
    public void AFixedMillTakesTheCubeOfTheCosineOfItsAngleToTheWind(double degrees, double cos3)
    {
        // worked first: the through-wind is 6 cos θ, so the power through the disc is 40854.77 W × cos³ θ
        var mill = Mill(windFrom: 90 - degrees);
        Assert.Equal(degrees, Math.Abs(mill.MisalignmentDeg), 9);
        Assert.Equal(WindPowerSquareOn * cos3, mill.WindPower, 0.01 * Math.Max(1, WindPowerSquareOn * cos3 / 100));
        Assert.Equal(cos3, mill.WindPower / Mill().WindPower, 1e-6);
    }

    [Fact]
    public void AFixedMillSixtyDegreesOffTheWindGivesAnEighthOfTheSquareOnOutput_1532W()
    {
        // 6 m/s at 60°: 3 m/s along the axle. Stones set to τ0 = ½ρA(3)²R·Cp/λ* = 2042.74 N·m, the sails at λ* = 2.5×3/10 = 0.75 rad/s,
        // P = τ0 ω = 1532.05 W = 0.3 × 40854.77 W / 8
        var mill = Mill(windFrom: 30);
        double tau0 = 0.5 * Rho * Math.PI * R * R * 3 * 3 * R * 0.3 / 2.5;
        mill.Load = tau0;
        mill.AddAngularImpulse(mill.MomentOfInertia * 0.75);
        for (int i = 0; i < 1000; i++) mill.Step(0.01);
        Assert.Equal(3.0, mill.ThroughWind, 1e-9);
        Assert.Equal(0.75, mill.AngularVelocity, 1e-6);
        Assert.Equal(0.3 * WindPowerSquareOn / 8, mill.Power, 0.01);
        Assert.Equal(1532.05, mill.Power, 0.01);
        Assert.Equal(0.3, mill.PowerCoefficient, 1e-6);       // Cp is of the wind that reaches the sails
    }

    [Fact]
    public void ADefaultMillIsBitIdenticalToOneWithNoHeadingAtAll()
    {
        // wind from +z, facing +z: cos 0 = 1 exactly, so every older machine is untouched
        var mill = Mill();
        Assert.Equal(0, mill.MisalignmentDeg);
        Assert.Equal(1.0, mill.AlignmentFactor);
        Assert.Equal(V, mill.ThroughWind);
        Assert.Equal(0.5 * Rho * mill.SweptArea * V * V * V, mill.WindPower);
    }

    [Fact]
    public void AVaneTurnsTheMillIntoTheWindAtItsYawRate_AndStopsThere()
    {
        var mill = Mill(windFrom: 30, vane: true);               // 60° to turn at 2°/s: 30 s
        for (int i = 0; i < 1500; i++) mill.Step(0.01);          // 15 s
        Assert.Equal(60, mill.FacingDeg, 1e-6);
        Assert.Equal(Math.Pow(Math.Cos(Math.PI / 6), 1), mill.AlignmentFactor, 1e-9);
        for (int i = 0; i < 1500; i++) mill.Step(0.01);          // 30 s
        Assert.Equal(30, mill.FacingDeg, 1e-6);
        Assert.Equal(1.0, mill.AlignmentFactor, 1e-12);
        for (int i = 0; i < 1000; i++) mill.Step(0.01);
        Assert.Equal(30, mill.FacingDeg, 1e-9);                  // it does not hunt
    }

    [Fact]
    public void AVaneTakesTheShortWayRound()
    {
        var mill = Mill(windFrom: 350, facing: 10, vane: true);  // 20° the short way, through 0, not 340° the long
        for (int i = 0; i < 1000; i++) mill.Step(0.01);          // 10 s: 20°
        Assert.Equal(-10, mill.FacingDeg, 1e-6);
        Assert.Equal(0, mill.MisalignmentDeg, 1e-6);
    }

    [Fact]
    public void AFixedMillStaysWhereItWasBuilt_AndALongerHourOfVeerMovesTheWindOffIt()
    {
        var mill = Mill(windFrom: 90, veer: -30);                // the wind backs 30° an hour
        for (int i = 0; i < 60; i++) mill.Step(60);              // an hour
        Assert.Equal(60, mill.WindFromDeg, 1e-9);
        Assert.Equal(90, mill.FacingDeg);
        Assert.Equal(0.6495190528383291, mill.WindPower / Mill().WindPower, 1e-9);     // 30° off in the end: cos³ 30° = (√3/2)³
    }

    [Fact]
    public void ADrivenMachineRunsTheFourMillsToTheirWorkedOutputs()
    {
        // wind-heading.rkt: all four in 6 m/s. Square on 12256.43 W; skewed 60° with stones for 3 m/s: 1532.05 W (= /8);
        // the vane mill, turned in 30 s, 12256.43 W; the one across the wind nothing.
        var def = MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", "wind-heading.machine")));
        var rt = new MachineRuntime(def, MaterialLibrary.LoadDefault());
        for (int i = 0; i < 40000; i++) rt.Step(0.01);           // 400 s: the skewed mill's time constant is I / (2 tau0 / omega0) = 50000 / 2723.6 = 18.4 s
        Assert.Equal(12256.43, rt.GetField("square", "power"), 0.01);
        Assert.Equal(1532.05, rt.GetField("skewed", "power"), 0.01);
        Assert.Equal(12256.43, rt.GetField("vane", "power"), 0.01);
        Assert.Equal(0, rt.GetField("across", "power"), 1e-6);
        Assert.Equal(0, rt.GetField("across", "rpm"), 1e-9);
        Assert.Equal(30, rt.GetField("vane", "facing"), 1e-6);
        Assert.Equal(90, rt.GetField("skewed", "facing"));
        Assert.Equal(0.125, rt.GetField("skewed", "wind-power") / rt.GetField("square", "wind-power"), 1e-9);
    }

    [Fact]
    public void AMapWindHeadingAndVeerRoundTripAndTurnEveryFieldMill()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "maps", "victoria.map"));
        var plain = Terrain.Parse(text, "victoria");
        Assert.Null(plain.Wind!.HeadingDeg);                     // the crater keeps every mill's own heading: nothing about it changes
        string line = text.Split('\n').First(l => l.TrimStart().StartsWith("(wind (corridor"));
        var headed = Terrain.Parse(text.Replace(line, line.TrimEnd().TrimEnd(')') + ") (heading 20 6))"), "victoria");
        Assert.Equal(20, headed.Wind!.HeadingDeg);
        Assert.Equal(6, headed.Wind.VeerDegPerHour);
        var again = Terrain.Parse(headed.Write(), "again");
        Assert.Equal(headed.Wind, again.Wind);
        Assert.Equal(20 + 6 * 0.5, again.Wind!.HeadingAt(1800), 1e-12);        // half an hour of veer

        // a mill taking the map's wind sees it from that heading, and at 20° with sails facing 90° the cube law applies
        var materials = MaterialLibrary.LoadDefault();
        var ground = new WorldGround(headed);
        var world = WorldDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "worlds", "crater-wind.world")), "crater-wind");
        var p = world.Placements.First();
        var def = WorldDef.Placed(MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", p.Machine + ".machine"))), p, ground.Ground);
        var rt = new MachineRuntime(def, materials);
        ground.Attach(p.Label, rt);
        var mill = rt.Windmills.Values.Single();
        for (int i = 0; i < 100; i++) { mill.Step(0.1); ground.Step(0.1); }
        double seconds = rt.Sun.Sols * rt.Sun.SolLength;
        Assert.Equal(20 + 6 * seconds / 3600, mill.WindFromDeg, 1e-9);
        Assert.Equal(mill.Wind * Math.Cos((70 + 0) * Math.PI / 180 - 6 * seconds / 3600 * Math.PI / 180), mill.ThroughWind, 1e-6 * mill.Wind);
    }
}
