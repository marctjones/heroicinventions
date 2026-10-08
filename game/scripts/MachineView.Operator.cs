using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

public partial class MachineView
{
    /// <summary>
    /// What the F key would do to each fire: the boiler's id and the watts to set, 0 for a fire that is burning and 3000
    /// for one that is out. Returned rather than applied so Main.Operate sets each one and logs it (issue #153).
    /// </summary>
    public List<(string Id, double Watts)> FireToggles() =>
        _fires.Select(f => (Runtime.Boilers.First(kv => kv.Value == f.boiler).Key, f.boiler.HeatInput > 0 ? 0.0 : 3000.0)).ToList();
}
