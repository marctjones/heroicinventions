using HeroicInventions.Sim;
using HeroicInventions.Sim.Mechanics;
using HeroicInventions.Sim.Thermo;
using Xunit;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #65: why Newcomen's engine fails on Mars. The atmosphere pushes its
/// piston down while the steam under it condenses to the vapour pressure of
/// the injection water, so the stroke's force is (P_air − p_sat(T_jet)) × A.
/// With a 53 cm piston (the game's newcomen-engine) and jet water at 40 °C
/// (p_sat 7.36 kPa by the sim's Antoine equation; steam tables give 7.38), predicted:
///   Earth's 101,325 Pa: 20.73 kN;
///   a 13.5 kPa enclosure: 1.355 kN, 6.5% of it;
///   Mars's 610 Pa outdoors: −1.489 kN — the vapour pushes harder than the air.
/// </summary>
public class NewcomenPressureTests
{
    [Theory]
    [InlineData(101_325, 20_731)]
    [InlineData(13_500, 1_355)]
    [InlineData(610, -1_489)]
    public void TheStrokesForceIsTheAirLessTheVapourPressureOfTheJet(double airPa, double predictedN)
    {
        const double bore = 0.53, stroke = 1.8, jet = 40;
        var zone = new Zone(Planet.Earth, 20) { Pressure = airPa };
        var cylinder = new AtmosphericCylinder("cylinder", new Boiler(500, 100, 0) { Zone = zone }, bore, stroke, jet) { Zone = zone };
        cylinder.PistonHeight = stroke;          // at the top of its stroke: the tappet opens the jet
        cylinder.Prime();
        for (int i = 0; i < 300; i++) cylinder.Step(0.01);   // 3 s: the steam condenses (0.15 s time constant)
        Assert.True(cylinder.Injecting);
        double area = Math.PI * bore * bore / 4;
        Assert.Equal((airPa - Boiler.SaturationPressure(jet)) * area, cylinder.Force, 1.0);
        Assert.Equal(predictedN, cylinder.Force, 5.0);
    }
}
