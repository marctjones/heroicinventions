using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Mechanics;

/// <summary>
/// The cylinder of Newcomen's atmospheric engine (1712). The piston is
/// open to the air above; below it is either steam or, after the cold
/// injection jet, a partial vacuum. The engine side moves the piston and
/// reports where it is; this works out the pressure under it and the
/// force the atmosphere then puts on it.
///
/// Steam phase: the steam valve is open and steam flows in from the boiler
/// at a rate proportional to the pressure difference. The piston is drawn
/// up by the weight of the pump rod on the far end of the beam.
/// Injection phase: cold water sprayed in condenses the steam. What's left
/// is water vapour at the injection water's temperature — at 60 °C, about
/// 20 kPa against the atmosphere's 101 — reached with a short time
/// constant. Atmospheric pressure on the piston's top, less that, drives
/// the working stroke down, lifting the pump.
/// The valves switch at the ends of the stroke, as the tappets on the
/// engine's plug rod did.
///
/// Steam is treated as an ideal gas at 100 °C; the heat real cylinders lost
/// warming their own walls back up after each injection (why a Newcomen
/// engine used several times the steam its cylinder held, and why Watt's
/// separate condenser mattered) isn't modelled.
/// </summary>
public sealed class AtmosphericCylinder(string name, Boiler boiler, double bore, double stroke, double injectionTemperatureC) : ICylinder
{
    /// <summary>The air and gravity it stands in: the planet's open air unless it is inside an enclosure.</summary>
    public Zone Zone { get; set; } = new();
    private const double SteamGasConstant = 461.5;   // J/(kg·K)
    private const double SteamTemperature = 373.15;  // K
    private const double CondensationTime = 0.15;    // s
    // kg/s of steam per Pa of pressure difference across the open steam valve
    private const double ValveConductance = 6e-6;

    public string Name { get; } = name;
    public Boiler Boiler { get; } = boiler;
    public double Bore { get; } = bore;
    public double Stroke { get; } = stroke;
    public double Area => Math.PI * Bore * Bore / 4;
    public double InjectionTemperature { get; } = injectionTemperatureC;

    /// <summary>Height of the piston above the bottom of its travel, m — set by the engine side each step.</summary>
    public double PistonHeight { get; set; }
    public bool Injecting { get; private set; }
    public int Strokes { get; private set; }
    public double SteamMass { get; private set; }   // kg under the piston
    public double SteamDraw { get; private set; }   // kg/s from the boiler, last step
    public double SteamUsed { get; private set; }   // kg, all told

    // a little space below the piston even at the bottom of its stroke
    private double Volume => Area * (Math.Max(0, PistonHeight) + 0.05);
    public double Pressure => SteamMass * SteamGasConstant * SteamTemperature / Volume;   // Pa, absolute

    /// <summary>Net downward force on the piston: the atmosphere on top against the pressure below. N.</summary>
    public double Force => (Zone.Pressure - Pressure) * Area;

    /// <summary>Fill the space under the piston with steam at atmospheric pressure, as at the start of a steam stroke.</summary>
    public void Prime() => SteamMass = Zone.Pressure * Volume / (SteamGasConstant * SteamTemperature);

    public void Step(double dt)
    {
        // the plug rod's tappets: inject at the top, admit steam at the bottom
        if (!Injecting && PistonHeight >= Stroke - 0.02) { Injecting = true; Strokes++; }
        else if (Injecting && PistonHeight <= 0.02) Injecting = false;

        if (Injecting)
        {
            // the mass that, in this model's ideal gas at SteamTemperature,
            // exerts the vapour pressure of water at the injection temperature
            double residual = Boiler.SaturationPressure(InjectionTemperature) * Volume
                              / (SteamGasConstant * SteamTemperature);
            SteamMass += (residual - SteamMass) * (1 - Math.Exp(-dt / CondensationTime));
            SteamDraw = 0;
        }
        else
        {
            SteamDraw = Boiler.IsDry ? 0 : ValveConductance * Math.Max(0, Boiler.AbsolutePressure - Pressure);
            SteamMass += SteamDraw * dt;
            SteamUsed += SteamDraw * dt;
        }
    }
}
