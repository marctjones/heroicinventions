using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Machines;

/// <summary>
/// Turns a <see cref="MachineDef"/> into running solvers: tanks, pipes and
/// sealed air become a <see cref="FluidNetwork"/>; each boiler joined to a
/// rotor becomes an <see cref="Aeolipile"/>. Blocks are rigid bodies and
/// belong to the engine layer, so they appear here only as data.
///
/// The Racket compiler already checks machines, but .machine files can
/// also be written by hand or by the in-game editor, so every reference is
/// checked again here and reported against its source location.
/// </summary>
public sealed class MachineRuntime
{
    private readonly Dictionary<string, Tank> _tanks = [];
    private readonly Dictionary<string, Pipe> _pipes = [];
    private readonly Dictionary<string, Boiler> _boilers = [];
    private readonly Dictionary<string, Aeolipile> _rotors = [];
    private readonly Dictionary<string, string> _rotorBoiler = []; // rotor id → boiler id
    private readonly List<AirPocket> _air = [];

    public MachineDef Def { get; }
    public FluidNetwork Fluids { get; } = new();
    public IReadOnlyDictionary<string, Tank> Tanks => _tanks;
    public IReadOnlyDictionary<string, Pipe> Pipes => _pipes;
    public IReadOnlyDictionary<string, Boiler> Boilers => _boilers;
    public IReadOnlyDictionary<string, Aeolipile> Rotors => _rotors;
    public IReadOnlyList<AirPocket> AirPockets => _air;
    public double Time { get; private set; }

    public string BoilerFor(string rotorId) => _rotorBoiler[rotorId];

    public MachineRuntime(MachineDef def, MaterialLibrary materials)
    {
        Def = def;

        foreach (var part in def.Parts)
        {
            if (!materials.TryGet(part.Material, out _))
                throw new MachineFormatException($"{part.Kind} {part.Id} uses unknown material {part.Material}", part.Location);

            switch (part.Kind)
            {
                case "tank":
                    _tanks[part.Id] = Fluids.AddTank(new Tank(part.Id, part.At.Y, part.Number("area"), part.Number("height"), part.Number("water", 0)));
                    break;
                case "boiler":
                    _boilers[part.Id] = new Boiler(part.Number("water"), heatInputW: part.Number("fire", 0));
                    break;
                case "rotor" or "block":
                    break; // rotors need their steam connection first; blocks are engine-side
                default:
                    throw new MachineFormatException($"unknown part kind {part.Kind}", part.Location);
            }
        }

        // Sealed air is created after the tanks are filled: its P·V constant
        // is fixed from the air volume at the moment it is sealed.
        foreach (var air in def.SealedAir)
            _air.Add(new AirPocket(air.Tanks.Select(t => TankNamed(t, air.Location)), air.TubeVolume));

        foreach (var pipe in def.Pipes)
        {
            var (from, fromElevation) = TankPort(pipe.From, pipe.Location);
            var (to, toElevation) = TankPort(pipe.To, pipe.Location);
            _pipes[pipe.Id] = Fluids.AddPipe(new Pipe(pipe.Id, from, fromElevation, to, toElevation, pipe.Conductance));
        }

        foreach (var c in def.Connects)
        {
            var (boilerRef, rotorRef) =
                _boilers.ContainsKey(c.A.Part) ? (c.A, c.B) :
                _boilers.ContainsKey(c.B.Part) ? (c.B, c.A) :
                throw new MachineFormatException($"connect {c.A} {c.B}: a steam connection needs a boiler", c.Location);
            if (def.Part(rotorRef.Part) is not { Kind: "rotor" })
                throw new MachineFormatException($"connect {c.A} {c.B}: {rotorRef.Part} is not a rotor", c.Location);
            if (_rotorBoiler.ContainsValue(boilerRef.Part))
                throw new MachineFormatException($"boiler {boilerRef.Part} already feeds a rotor", c.Location);
            _rotorBoiler[rotorRef.Part] = boilerRef.Part;
        }

        foreach (var part in def.Parts.Where(p => p.Kind == "rotor"))
        {
            if (!_rotorBoiler.TryGetValue(part.Id, out var boilerId))
                throw new MachineFormatException($"rotor {part.Id} has no steam supply", part.Location);
            double radius = part.Number("radius");
            double bore = part.Number("bore");
            _rotors[part.Id] = new Aeolipile(_boilers[boilerId])
            {
                NozzleCount = (int)part.Number("nozzles", 2),
                NozzleArea = Math.PI * bore * bore / 4,
                ArmRadius = part.Number("arm"),
                MomentOfInertia = ShellInertia(materials[part.Material].Density, radius, part.Number("wall", 0.001)),
            };
        }
    }

    public void Step(double dt)
    {
        Fluids.Step(dt);
        foreach (var rotor in _rotors.Values) rotor.Step(dt); // steps its own boiler
        foreach (var (id, boiler) in _boilers)
            if (!_rotorBoiler.ContainsValue(id)) boiler.Step(dt, 0);
        Time += dt;
    }

    /// <summary>Thin spherical shell: I = ⅔·m·r², with m = ρ·4πr²·t.</summary>
    public static double ShellInertia(double density, double radius, double wall) =>
        2.0 / 3.0 * (density * 4 * Math.PI * radius * radius * wall) * radius * radius;

    private Tank TankNamed(string id, SourceLocation? usedAt) =>
        _tanks.TryGetValue(id, out var t) ? t : throw new MachineFormatException($"{id} is not a tank in this machine", usedAt);

    private (Tank tank, double elevation) TankPort(PortRef r, SourceLocation? usedAt)
    {
        var tank = TankNamed(r.Part, usedAt);
        var spec = Def.Part(r.Part)!;
        return (tank, spec.At.Y + spec.Port(r.Port, usedAt).Height);
    }
}
