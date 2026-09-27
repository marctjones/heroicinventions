using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Machines;

/// <summary>
/// Hero of Alexandria's fountain, built from the generic fluid network.
/// Water poured into the top basin drains into the sealed bottom chamber,
/// compressing the air it shares with the middle chamber. That pressure
/// pushes the middle chamber's water up the nozzle, above the basin.
/// </summary>
public sealed class HeronsFountain
{
    public FluidNetwork Network { get; } = new();
    public Tank Basin { get; }
    public Tank Supply { get; }   // middle chamber, starts full
    public Tank Receiver { get; } // bottom chamber, starts empty
    public AirPocket Air { get; }
    public Pipe Drain { get; }
    public Pipe Nozzle { get; }

    public HeronsFountain()
    {
        Basin    = Network.AddTank(new Tank("Basin",    baseElevation: 1.00, area: 0.05, height: 0.10, waterVolume: 0.004));
        Supply   = Network.AddTank(new Tank("Supply",   baseElevation: 0.50, area: 0.02, height: 0.30, waterVolume: 0.005));
        Receiver = Network.AddTank(new Tank("Receiver", baseElevation: 0.00, area: 0.02, height: 0.30));

        // The air tube runs from the top of the receiver to the top of the supply.
        Air = new AirPocket([Supply, Receiver], tubeVolume: 0.0002);

        Drain  = Network.AddPipe(new Pipe("Drain",  Basin,  1.00, Receiver, 0.30, conductance: 2e-4));
        Nozzle = Network.AddPipe(new Pipe("Nozzle", Supply, 0.50, Basin,    1.15, conductance: 1e-4));
    }

    public void Step(double dt) => Network.Step(dt);
}
