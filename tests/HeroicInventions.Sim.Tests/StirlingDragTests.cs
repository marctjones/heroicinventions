using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// #269: a Stirling engine's own drag, a torque k ω² (the owner's choice: one number, the simplest model that gives an unloaded engine a
/// free speed under the physics engine's 3,000 rpm limit). The engine here is #220's at the crater's noon, its hot end held at 98.75 °C
/// (a huge heat capacity) over -28.79 °C air, with the template's numbers: K 14 W/K, 0.4375 of Carnot, k 1.38e-5 N·m·s².
/// Worked before running: P = 0.4375 (1 - 244.36 / 371.90) 14 (127.54) = 267.9 W; the free speed (P / k)^⅓ = 268.8 rad/s (2,567 rpm).
/// </summary>
public class StirlingDragTests
{
    private static StirlingEngine Engine(double drag) =>
        new("eng", 1, 14, 98.75) { HeatCapacity = 1e15, Drag = drag, Zone = new Zone { Temperature = -28.79 } };

    [Fact]
    public void TheTemplatesDefaultsAreTheShaftsThirtyFivePercentAtFifteenHundredRpm()
    {
        var e = new StirlingEngine("eng", 1, 14, 20);
        Assert.Equal(0.4375, e.CarnotFraction, 12);
        Assert.Equal(1.38e-5, e.Drag, 12);
        // at 1,500 rpm the drag takes a fifth of the 267.9 W, so the shaft keeps 0.8 of 0.4375 = 0.35 of Carnot
        Assert.Equal(0.2 * 267.9, e.Drag * Math.Pow(157.08, 3), 0.2);
    }

    [Fact]
    public void UnloadedItSettlesAtItsFreeSpeed()
    {
        var e = Engine(StirlingEngine.DefaultDrag);
        for (int i = 0; i < 600 * 120; i++) e.Step(1.0 / 120);
        Assert.Equal(267.9, e.IndicatedPower, 0.3);
        Assert.Equal(268.8, e.AngularVelocity, 0.5);            // rad/s: 2,567 rpm
        Assert.Equal(e.FreeSpeed, e.AngularVelocity, 0.1);
        Assert.Equal(0, e.ShaftPower, 0.5);                      // all of it is drag at the free speed
        Assert.True(e.Rpm < 3000, "under the physics engine's 3,000 rpm clamp");
    }

    [Fact]
    public void OnALoadItTurnsWhereThePowerMeetsLoadAndDrag()
    {
        // 3 N.m, as mars-stirling's engines: 267.9 = 3 w + 1.38e-5 w^3, w = 86.34 rad/s (worked by bisection)
        var e = Engine(StirlingEngine.DefaultDrag);
        e.Load = 3;
        for (int i = 0; i < 600 * 120; i++) e.Step(1.0 / 120);
        Assert.Equal(86.34, e.AngularVelocity, 0.2);
        Assert.Equal(3 * e.AngularVelocity, e.ShaftPower, 0.5);  // what the drag leaves is what the load takes
    }

    [Fact]
    public void WithNoDragTheOldRunawayComesBackSoTheDragIsANumberNotAFormula()
    {
        // k = 0: the engine gains its work as speed, sqrt(2 P t / I) = sqrt(2 x 267.9 x 20 / 0.5) = 146.4 rad/s after 20 s
        var e = Engine(0);
        for (int i = 0; i < 20 * 120; i++) e.Step(1.0 / 120);
        Assert.Equal(Math.Sqrt(2 * 267.9 * 20 / 0.5), e.AngularVelocity, 0.5);
        Assert.Equal(double.PositiveInfinity, e.FreeSpeed);
    }
}
