using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Mechanics;

/// <summary>A cylinder a piston works in, as the engine side sees it: where the piston is, and the force on it.</summary>
public interface ICylinder
{
    string Name { get; }
    Boiler Boiler { get; }
    Zone Zone { get; set; }
    double PistonHeight { get; set; }
    /// <summary>Net downward force on the piston, N (negative pushes it up).</summary>
    double Force { get; }
    /// <summary>Pa, absolute, of the steam working the piston now.</summary>
    double Pressure { get; }
    bool Injecting { get; }
    int Strokes { get; }
    double SteamDraw { get; }
    double SteamUsed { get; }
    void Prime();
    void Step(double dt);
}

/// <summary>
/// A high-pressure steam cylinder (issue #65; Richard Trevithick, 1800s):
/// boiler steam pushes the piston directly, and what has done its work
/// exhausts into the air around it. Double-acting: a slide valve, moved in
/// step with the crank, admits steam behind the piston whichever way it is
/// travelling and opens the other side to the exhaust, so the push always
/// goes with the motion, (P_boiler − P_air) × A, the whole stroke (full
/// admission; no expansion). Each stroke does
///
///   W = (P_boiler − P_air) × A × stroke,
///
/// so, unlike Newcomen's engine, it works on Mars, and works better there:
/// 610 Pa behind the exhaust instead of 101 kPa.
///
/// The steam it takes is the swept volume at boiler pressure: ρ_steam A |v|.
/// The piston's travel is set by whatever it drives (a crank), not here.
/// </summary>
public sealed class SteamCylinder(string name, Boiler boiler, double bore) : ICylinder
{
    public string Name { get; } = name;
    public Boiler Boiler { get; } = boiler;
    public Zone Zone { get; set; } = new();
    public double Bore { get; } = bore;
    public double Area => Math.PI * Bore * Bore / 4;
    public double PistonHeight { get; set; }
    public bool Injecting => false;
    public int Strokes { get; private set; }
    public double SteamDraw { get; private set; }            // kg/s from the boiler, last step
    public double SteamUsed { get; private set; }            // kg, all told
    public double Work { get; private set; }                 // J the steam has done on the piston
    public double LastStrokeWork { get; private set; }       // J, the stroke just finished
    public double LastStrokeLength { get; private set; }     // m, how far it travelled
    /// <summary>+1 while the valve sends steam under the piston (it rises), −1 over it.</summary>
    public int Direction { get; private set; } = 1;
    /// <summary>
    /// Set each step by whatever the valve is geared to — an eccentric on the
    /// crankshaft: +1 while the crank's forward turn raises the piston. Unset,
    /// the valve simply follows the piston's own motion.
    /// </summary>
    public int? Valve { get; set; }

    private double _lastHeight = double.NaN;
    private double _strokeWork, _low = double.MaxValue, _high = double.MinValue;
    private int _appliedDirection = 1;

    public double Pressure => Boiler.IsDry ? Zone.Pressure : Boiler.AbsolutePressure;
    /// <summary>The push, (P_boiler − P_air) A, along the valve's direction; down is positive.</summary>
    public double Force => -Direction * Math.Max(0, Pressure - Zone.Pressure) * Area;

    public void Prime() => _lastHeight = PistonHeight;

    public void Step(double dt)
    {
        if (double.IsNaN(_lastHeight)) _lastHeight = PistonHeight;
        double moved = PistonHeight - _lastHeight;
        _lastHeight = PistonHeight;
        // the piston went this far under the push the engine applied a step
        // ago (the tick it turns back, against it): the direction then
        double push = Math.Max(0, Pressure - Zone.Pressure) * Area;
        int applied = _appliedDirection;
        _appliedDirection = Direction;
        _strokeWork += push * applied * moved;
        Work += push * applied * moved;
        _low = Math.Min(_low, PistonHeight);
        _high = Math.Max(_high, PistonHeight);
        // the valve follows the crank: when the piston turns back, so does the steam
        int next = Valve ?? (Math.Abs(moved) > 1e-6 ? Math.Sign(moved) : Direction);
        if (next != Direction)
        {
            Direction = next;
            Strokes++;
            LastStrokeWork = _strokeWork;
            LastStrokeLength = _high - _low;
            _strokeWork = 0;
            _low = _high = PistonHeight;
        }
        SteamDraw = Boiler.IsDry ? 0 : Boiler.SteamDensity * Area * Math.Abs(moved) / dt;
        SteamUsed += SteamDraw * dt;
    }
}
