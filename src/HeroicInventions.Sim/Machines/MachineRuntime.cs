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
    private readonly Dictionary<string, Hearth> _hearths = [];
    private readonly Dictionary<string, Hearth> _bellows = []; // bellows id → the hearth it forces draught into
    private readonly Dictionary<string, Aeolipile> _rotors = [];
    private readonly Dictionary<string, string> _rotorBoiler = []; // rotor or jet wheel id → boiler id
    private readonly Dictionary<string, JetWheel> _jetWheels = [];
    private readonly List<AirPocket> _air = [];
    private readonly Dictionary<string, WaterLift> _lifts = [];
    private readonly Dictionary<string, AtmosphericCylinder> _cylinders = [];
    private readonly Dictionary<string, WaterSource> _sources = [];
    private readonly Dictionary<string, Trigger> _triggers = [];
    private readonly Dictionary<string, Follow> _follows = [];
    private readonly Dictionary<string, Belt> _belts = [];
    private readonly Dictionary<string, Grip> _grips = [];
    private readonly Dictionary<string, Cam> _cams = [];
    private readonly Dictionary<string, Ratchet> _ratchets = [];
    private readonly Dictionary<string, Hopper> _hoppers = [];
    private readonly Dictionary<string, Channel> _channels = [];
    private readonly Dictionary<string, SluiceGate> _gates = [];
    private readonly Dictionary<string, (FloatValve Valve, Func<double> Flow)> _floatValves = [];
    private readonly Dictionary<string, TankLeak> _leaks = [];
    private readonly Dictionary<string, (SafetyValve Valve, Boiler Boiler)> _safetyValves = [];
    private readonly Dictionary<string, LiftPump> _pumps = [];
    private readonly Dictionary<string, WaterWheel> _wheels = [];
    private readonly Dictionary<string, Windmill> _windmills = [];
    private readonly Dictionary<string, Capstan> _capstans = [];
    private readonly Dictionary<string, Mirror> _mirrors = [];
    private readonly Dictionary<string, Enclosure> _enclosures = [];
    private readonly Dictionary<string, Crucible> _crucibles = [];
    private readonly Dictionary<string, Pane> _panes = [];
    private readonly Dictionary<string, Door> _doors = [];
    private readonly Dictionary<string, GasPump> _gasPumps = [];
    // parts standing inside an enclosure, by id: the zone they read; everything else stands in Outside
    private readonly Dictionary<string, Zone> _zoneOfPart = [];
    private readonly Dictionary<string, double> _boilerLossSeen = [];
    // every target a hearth or mirror heats, with those sources: summed onto its own fire each step
    private readonly Dictionary<IHeated, List<Func<double>>> _heatSources = [];
    private readonly Dictionary<IHeated, double> _ownHeat = [];
    private readonly Dictionary<string, Counterpoise> _counterpoises = [];
    private readonly Dictionary<string, Pendulum> _pendulums = [];
    private readonly Dictionary<string, Digger> _diggers = [];
    private readonly Dictionary<string, Float> _floats = [];
    private readonly Dictionary<string, Func<double>> _getters = [];
    private readonly Dictionary<string, Action<double>> _setters = [];

    public MachineDef Def { get; }
    public FluidNetwork Fluids { get; } = new();
    public IReadOnlyDictionary<string, Tank> Tanks => _tanks;
    public IReadOnlyDictionary<string, Pipe> Pipes => _pipes;
    public IReadOnlyDictionary<string, Boiler> Boilers => _boilers;
    public IReadOnlyDictionary<string, Hearth> Hearths => _hearths;
    /// <summary>Bellows, each with the hearth it forces draught into.</summary>
    public IReadOnlyDictionary<string, Hearth> Bellows => _bellows;
    public IReadOnlyDictionary<string, Aeolipile> Rotors => _rotors;
    public IReadOnlyDictionary<string, JetWheel> JetWheels => _jetWheels;
    public IReadOnlyList<AirPocket> AirPockets => _air;
    public IReadOnlyDictionary<string, WaterLift> Lifts => _lifts;
    public IReadOnlyDictionary<string, AtmosphericCylinder> Cylinders => _cylinders;
    public IReadOnlyDictionary<string, WaterSource> Sources => _sources;
    /// <summary>Triggers, by id. Body triggers are tested by the view that owns the bodies, through <see cref="TestBodyTrigger"/>.</summary>
    public IReadOnlyDictionary<string, Trigger> Triggers => _triggers;
    /// <summary>Fields that follow a lever or rope, by id; the view that owns the mechanism feeds each one through <see cref="ApplyFollow"/>.</summary>
    public IReadOnlyDictionary<string, Follow> Follows => _follows;
    /// <summary>Belts between drums, by id. Whoever owns the drums (the view) grips them each tick with <see cref="Belt.Grip"/>.</summary>
    public IReadOnlyDictionary<string, Belt> Belts => _belts;
    /// <summary>Grips, by id: hooks and tongs that pick up loose bodies. The view owns the bodies and the joints; the grip's state and limits live here.</summary>
    public IReadOnlyDictionary<string, Grip> Grips => _grips;
    /// <summary>Cams (peg wheels and their followers), by id. The view owns the wheel and feeds each cam its angle every tick; the cam works the follower and gives back the torque it puts on the wheel.</summary>
    public IReadOnlyDictionary<string, Cam> Cams => _cams;
    /// <summary>Ratchets (toothed wheels with a pawl), by id. The view feeds each its wheel's angle every tick and applies the impulse it gives back.</summary>
    public IReadOnlyDictionary<string, Ratchet> Ratchets => _ratchets;
    /// <summary>Hoppers of grain draining at Beverloo's steady rate, by id (issue #50).</summary>
    public IReadOnlyDictionary<string, Hopper> Hoppers => _hoppers;
    public IReadOnlyDictionary<string, Channel> Channels => _channels;
    public IReadOnlyDictionary<string, SluiceGate> Gates => _gates;
    /// <summary>Float valves, each with the flow of the feed it throttles (m³/s).</summary>
    public IReadOnlyDictionary<string, (FloatValve Valve, Func<double> Flow)> FloatValves => _floatValves;
    /// <summary>Holes in tank walls (and seeps), draining by Torricelli's law.</summary>
    public IReadOnlyDictionary<string, TankLeak> Leaks => _leaks;
    /// <summary>Safety valves, each with the boiler whose lid it sits in.</summary>
    public IReadOnlyDictionary<string, (SafetyValve Valve, Boiler Boiler)> SafetyValves => _safetyValves;
    /// <summary>Lift pumps, each drawing up a suction pipe no higher than the atmosphere can push the water.</summary>
    public IReadOnlyDictionary<string, LiftPump> Pumps => _pumps;
    public IReadOnlyDictionary<string, WaterWheel> WaterWheels => _wheels;
    /// <summary>Windmills: sails turning a millstone, taking at most the Betz limit of the wind's power.</summary>
    public IReadOnlyDictionary<string, Windmill> Windmills => _windmills;
    /// <summary>Ropes wrapped round fixed posts, holding a load by friction (the capstan equation).</summary>
    public IReadOnlyDictionary<string, Capstan> Capstans => _capstans;
    /// <summary>Heliostats throwing sunlight onto boilers and sealed vessels.</summary>
    public IReadOnlyDictionary<string, Mirror> Mirrors => _mirrors;
    /// <summary>Enclosures (issue #39): boxes with their own air, which the parts inside read their conditions from.</summary>
    public IReadOnlyDictionary<string, Enclosure> Enclosures => _enclosures;
    /// <summary>Crucibles of sand at a focal spot, melting to glass (issue #56).</summary>
    public IReadOnlyDictionary<string, Crucible> Crucibles => _crucibles;
    /// <summary>Glass panes in enclosures' walls (issue #57): the only way light gets in, and they crack past their pressure.</summary>
    public IReadOnlyDictionary<string, Pane> Panes => _panes;
    /// <summary>Doors, hatches and valves between zones (issue #41).</summary>
    public IReadOnlyDictionary<string, Door> Doors => _doors;
    /// <summary>Pumps moving gas from one zone to another (issue #41).</summary>
    public IReadOnlyDictionary<string, GasPump> GasPumps => _gasPumps;

    /// <summary>A zone by name: an enclosure's id, or outside for the planet's open air.</summary>
    private Zone ZoneNamed(string name, PartSpec by) =>
        name == "outside" ? Outside
        : _enclosures.TryGetValue(name, out var e) ? e
        : throw new MachineFormatException($"{by.Kind} {by.Id} joins {name}, which is not an enclosure (or outside)", by.Location);
    /// <summary>The zone a part stands in: the innermost enclosure round it, or the open air.</summary>
    public Zone ZoneOf(string partId) => _zoneOfPart.GetValueOrDefault(partId, Outside);
    public Sun Sun { get; }
    /// <summary>Sols, the daily air, relay passes and dust storms (issue #69), if the scene has weather.</summary>
    public Weather? Weather { get; }
    /// <summary>The planet the scene stands on (issue #38).</summary>
    public Planet Planet => Outside.Planet;
    /// <summary>
    /// The planet's open air round the machine: its gravity, pressure, gas mix
    /// and temperature (the <see cref="Ambient"/>). Every part reads its
    /// conditions from the zone it stands in, and outside an enclosure that is
    /// this one.
    /// </summary>
    public Zone Outside { get; }
    /// <summary>Whether the scene sets its sun or has mirrors: then the view lights it by the sun, else by its fixed studio light.</summary>
    public bool SunShown => Def.Sun is not null || _mirrors.Count > 0;
    public IReadOnlyDictionary<string, Counterpoise> Counterpoises => _counterpoises;
    /// <summary>Pendulums hung on a bearing (#:bearing-radius): swung here, not by Jolt, so their friction and wear can be checked.</summary>
    public IReadOnlyDictionary<string, Pendulum> Pendulums => _pendulums;
    /// <summary>Digging gangs (issue #44): they dig the map the world stands the machine on (WorldGround attaches it), and nothing without one.</summary>
    public IReadOnlyDictionary<string, Digger> Diggers => _diggers;
    /// <summary>Floats riding tanks' water (issue #29).</summary>
    public IReadOnlyDictionary<string, Float> Floats => _floats;
    public double Time { get; private set; }

    /// <summary>
    /// Named, human-scaled readouts and controls for the live link and the
    /// HUD: "kettle.fire" (W), "kettle.temperature" (°C), "kettle.pressure"
    /// (kPa gauge), "kettle.water" (kg), "ball.rpm", "basin.water" (L),
    /// "nozzle.jet-height" (cm). Every tank, boiler, rotor and pipe gets an
    /// entry; only a few fields (fire, water level) are settable.
    /// </summary>
    /// <summary>
    /// Takes over a previous build of (an edited version of) this machine's
    /// running state, part by part (issue #75): every part that has the same
    /// id and kind in both keeps its water, heat, speed and so on; new parts
    /// start from their own initial state; the clock carries on. Settings the
    /// edit changed stay changed (see <see cref="StateCopy"/>).
    /// </summary>
    public void TakeStateFrom(MachineRuntime previous)
    {
        // the original machine freshly built: what tells an edited setting from running state
        var baseline = new MachineRuntime(previous.Def, _materials);
        foreach (var field in typeof(MachineRuntime).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
        {
            if (field.GetValue(previous) is not System.Collections.IDictionary running
                || field.GetValue(baseline) is not System.Collections.IDictionary fresh
                || field.GetValue(this) is not System.Collections.IDictionary edited) continue;
            if (!field.FieldType.IsGenericType || field.FieldType.GetGenericArguments()[0] != typeof(string)) continue;
            foreach (System.Collections.DictionaryEntry entry in edited)
            {
                if (!running.Contains(entry.Key) || !fresh.Contains(entry.Key)) continue;   // a new part: starts as built
                object? r = running[entry.Key], b = fresh[entry.Key], e = entry.Value;
                // (FloatValve, Func) and (SafetyValve, Boiler) pairs: carry the valve
                if (r is System.Runtime.CompilerServices.ITuple rt && b is System.Runtime.CompilerServices.ITuple bt && e is System.Runtime.CompilerServices.ITuple et)
                    (r, b, e) = (rt[0], bt[0], et[0]);
                if (r is not null && b is not null && e is not null && e.GetType().IsClass) StateCopy.Carry(r, b, e);
            }
        }
        StateCopy.Carry(previous.Sun, baseline.Sun, Sun);
        StateCopy.Carry(previous.Fluids, baseline.Fluids, Fluids);
        // sealed air belongs to its tanks, in the order they were declared
        for (int i = 0; i < Math.Min(_air.Count, Math.Min(previous._air.Count, baseline._air.Count)); i++)
            StateCopy.Carry(previous._air[i], baseline._air[i], _air[i]);
        Time = previous.Time;
    }

    public IReadOnlyDictionary<string, Func<double>> FieldGetters => _getters;
    public IReadOnlyDictionary<string, Action<double>> FieldSetters => _setters;

    public double GetField(string target, string field) =>
        _getters.TryGetValue($"{target}.{field}", out var get)
            ? get()
            : throw new MachineFormatException($"{target} has no readable field {field}");

    public void SetField(string target, string field, double value)
    {
        if (!_setters.TryGetValue($"{target}.{field}", out var set))
            throw new MachineFormatException($"{target} has no settable field {field}");
        set(value);
    }

    public string BoilerFor(string rotorId) => _rotorBoiler[rotorId];

    /// <summary>
    /// The air round the machine, °C. Set, it reaches every part that feels
    /// it: boilers and sealed air cool towards it, a hearth's quench and feed
    /// water and a windmill's or bellows' air are at it, tanks freeze below 0.
    /// </summary>
    public double Ambient
    {
        get => _ambient;
        set
        {
            _ambient = value;
            Outside.Temperature = value;
            Fluids.Ambient = value;
            SyncZones();
        }
    }
    private double _ambient = 20;

    /// <summary>A part's #:temperature if it gave one, else the ambient — water drawn from outside is at the air's temperature, or just above freezing in a frost.</summary>
    private double TemperatureOr(PartSpec part, string key, bool freezing = true) =>
        part.Props.GetValueOrDefault(key) is SNumber t ? t.Value
        : freezing ? Math.Max(0, ZoneOf(part.Id).Temperature) : ZoneOf(part.Id).Temperature;   // water is at least just thawed; sand is at the air's

    private readonly MaterialLibrary _materials;

    public MachineRuntime(MachineDef def, MaterialLibrary materials)
    {
        Def = def;
        _materials = materials;
        _ambient = def.Ambient;
        Outside = new Zone(def.Planet, def.Ambient);
        Sun = (def.Sun is { } sun ? new Sun(sun.Latitude, sun.Day, sun.Time) : new Sun(31.2, 172, 12)).On(def.Planet);
        if (def.Weather is { } wx) Weather = new Weather(wx, Sun, () => Outside.Planet);
        BuildEnclosures(def);

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
                    _boilers[part.Id] = new Boiler(part.Number("water"), TemperatureOr(part, "temperature"), heatInputW: part.Number("fire", 0))
                    {
                        BurstPressure = part.Number("burst", 0),
                    };
                    break;
                case "waterwheel": break; // built once the channels that drive it exist
                case "capstan":
                {
                    string rope = part.Symbol("rope", "hemp");
                    if (!materials.TryGet(rope, out var ropeMat))
                        throw new MachineFormatException($"capstan {part.Id}'s rope is of unknown material {rope}", part.Location);
                    // rope on post as Jolt combines two surfaces' friction: √(μ₁μ₂)
                    double mu = part.Props.GetValueOrDefault("mu") is SNumber given ? given.Value
                        : Math.Sqrt(ropeMat.Friction * materials[part.Material].Friction);
                    _capstans[part.Id] = new Capstan(part.Id, part.Number("turns"), mu, part.Number("load"), part.At.Y - part.Number("drop", 1), part.At.Y)
                    {
                        Hold = part.Number("hold", 0),
                    };
                    break;
                }
                case "windmill":
                {
                    double r = part.Number("radius"), cp = part.Number("cp", 0.3);
                    if (!(cp > 0 && cp <= Windmill.BetzLimit))
                        throw new MachineFormatException($"windmill {part.Id}: #:cp must be above 0 and at most the Betz limit, 16/27 = 0.593 (no rotor takes more of the wind), got {cp}", part.Location);
                    _windmills[part.Id] = new Windmill(part.Id, r, part.Number("mass") * r * r / 3) // slender sails from the hub: ⅓·m·R²
                    {
                        Wind = part.Number("wind"),
                        Load = part.Number("load", 0),
                        CpMax = cp,
                        TipSpeedRatio = part.Number("tip-speed-ratio", 2.5),
                    };
                    break;
                }
                case "counterpoise": break; // built once its vessel exists
                case "pendulum" when part.Props.GetValueOrDefault("bearing-radius") is SNumber journal:
                    _pendulums[part.Id] = new Pendulum(part.Id, part.Number("length"), materials[part.Material].Density,
                        part.Number("start-angle-deg", 0) * Math.PI / 180,
                        new Bearing(journal.Value)
                        {
                            Mu = part.Number("bearing-mu", 0),
                            Drag = part.Number("bearing-drag", 0),
                            WearRate = part.Number("bearing-wear", 0),
                        });
                    break;
                case "mirror" or "burning-mirror" or "pane": break; // built once what it heats exists
                case "crucible":
                {
                    SandKind sand;
                    try { sand = SandKind.Named(part.Symbol("sand", "basalt")); }
                    catch (ArgumentException e) { throw new MachineFormatException($"crucible {part.Id}: {e.Message}", part.Location); }
                    double spot = part.Number("spot"), charge = part.Number("charge");
                    if (spot <= 0 || charge < 0) throw new MachineFormatException($"crucible {part.Id}: #:spot must be above 0 and #:charge 0 or more", part.Location);
                    _crucibles[part.Id] = new Crucible(part.Id, sand, charge, spot, TemperatureOr(part, "temperature", freezing: false))
                    {
                        Emissivity = part.Number("emissivity", 0.9),
                    };
                    break;
                }
                case "enclosure": break; // built first: every other part reads its zone
                case "door":
                {
                    Zone a = ZoneNamed(part.Symbol("from", ""), part), b = ZoneNamed(part.Symbol("to", ""), part);
                    if (a == b) throw new MachineFormatException($"door {part.Id} joins {part.Symbol("from", "")} to itself", part.Location);
                    _doors[part.Id] = new Door(part.Id, a, b, part.Number("area"))
                    {
                        Open = part.Number("open", 0),
                        Cd = part.Number("coefficient", Enclosure.DefaultCoefficient),
                    };
                    break;
                }
                case "air-pump":
                {
                    Zone a = ZoneNamed(part.Symbol("from", ""), part), b = ZoneNamed(part.Symbol("to", ""), part);
                    if (a == b) throw new MachineFormatException($"air-pump {part.Id} draws from and delivers to {part.Symbol("from", "")}", part.Location);
                    _gasPumps[part.Id] = new GasPump(part.Id, a, b, part.Number("speed")) { Until = part.Number("until", 0) };
                    break;
                }
                case "float": break;   // built once its tank exists
                case "digger":
                {
                    double len = part.Number("length"), w = part.Number("width"), d = part.Number("depth");
                    double power = part.Number("power", 150), spit = part.Number("spit", 0.25);
                    if (!(len > 0 && w > 0 && d > 0 && spit > 0) || power < 0)
                        throw new MachineFormatException($"digger {part.Id}: length, width, depth and spit must be more than 0, and power not less", part.Location);
                    _diggers[part.Id] = new Digger(part.Id, part.At.X, part.At.Z, len, w, d, power, spit, part.Number("spoil", 5));
                    break;
                }
                case "rotor" or "jetwheel" or "smokejack" or "block" or "pendulum" or "lever" or "ramp" or "wheel" or "screw" or "fixture" or "piston" or "post" or "hearth" or "bellows" or "sluice" or "float-valve" or "leak" or "safety-valve" or "pump" or "grip" or "cam" or "ratchet" or "hopper":
                    break; // rotors need their steam connection first; the rest are pure Jolt rigid-body physics, engine-side only
                default:
                    throw new MachineFormatException($"unknown part kind {part.Kind}", part.Location);
            }
        }

        // Sealed air is created after the tanks are filled: its P·V constant
        // is fixed from the air volume at the moment it is sealed.
        foreach (var air in def.SealedAir)
            _air.Add(new AirPocket(air.Tanks.Select(t => TankNamed(t, air.Location)), air.TubeVolume,
                                   sealedAtC: ZoneOf(air.Tanks[0]).Temperature, zone: ZoneOf(air.Tanks[0]))
            {
                HeatLoss = air.HeatLoss,
                VesselHeatCapacity = air.HeatCapacity,
            });

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
            if (def.Part(rotorRef.Part) is not { Kind: "rotor" or "jetwheel" })
                throw new MachineFormatException($"connect {c.A} {c.B}: {rotorRef.Part} is not a rotor or jetwheel", c.Location);
            if (_rotorBoiler.ContainsValue(boilerRef.Part))
                throw new MachineFormatException($"boiler {boilerRef.Part} already feeds a rotor or jetwheel", c.Location);
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

        foreach (var part in def.Parts.Where(p => p.Kind == "jetwheel"))
        {
            if (!_rotorBoiler.TryGetValue(part.Id, out var boilerId))
                throw new MachineFormatException($"jetwheel {part.Id} has no steam supply: connect a boiler's steam to its steam-in", part.Location);
            double radius = part.Number("radius");
            double bore = part.Number("bore");
            _jetWheels[part.Id] = new JetWheel(_boilers[boilerId])
            {
                SpoutArea = Math.PI * bore * bore / 4,
                Radius = radius,
                MomentOfInertia = Math.Max(1e-5, part.Number("mass", 0.5) * radius * radius),
                Load = part.Number("load", 0),
                // square paddles #:width across, beating the scene's air
                AirDrag = JetWheel.Windage(Outside.AirDensityAt(_ambient), (int)part.Number("paddles", 8),
                                           part.Number("width", 0.03) * part.Number("width", 0.03), radius),
            };
        }

        foreach (var part in def.Parts.Where(p => p.Kind == "hearth"))
        {
            var heats = part.Symbol("heats", "");
            var (target, _) = HeatedNamed(heats, part);
            _hearths[part.Id] = new Hearth(target, part.Number("power"), part.Number("fuel"),
                                           part.Symbol("fuel-kind", "wood"), part.Number("efficiency", 0.5));
        }

        foreach (var part in def.Parts.Where(p => p.Kind is "mirror" or "burning-mirror"))
        {
            var onto = part.Symbol("onto", "");
            var (target, targetPart) = HeatedNamed(onto, part);
            var aim = new Vec3(targetPart.At.X, targetPart.At.Y + targetPart.Number("height", targetPart.Number("size-y", 0)) / 2, targetPart.At.Z);
            bool burning = part.Kind == "burning-mirror";
            var mirror = new Mirror(Sun, part.At, aim, part.Number("area"), part.Number("reflectivity", 0.85))
            {
                Focusing = burning,
                Image = burning ? part.Number("image") : part.Number("area"),
            };
            _mirrors[part.Id] = mirror;
            // a crucible's spot takes only the share of the mirror's image that falls on it
            if (target is Crucible c) AddHeatSource(target, () => mirror.Power * Math.Min(1, c.Spot / mirror.Image));
            else AddHeatSource(target, () => mirror.Power);
        }
        foreach (var (_, h) in _hearths) AddHeatSource(h.Target, () => h.HeatOut);
        foreach (var part in def.Parts.Where(p => p.Kind == "pane"))
        {
            var on = part.Symbol("on", "");
            if (!_enclosures.TryGetValue(on, out var room))
                throw new MachineFormatException($"pane {part.Id} is on {on}, which is not an enclosure", part.Location);
            double side = part.Number("side"), thickness = part.Number("thickness");
            int count = (int)part.Number("count", 1);
            if (side <= 0 || thickness <= 0 || count < 1)
                throw new MachineFormatException($"pane {part.Id}: #:side and #:thickness must be above 0, #:count 1 or more", part.Location);
            SandKind glass;
            Vec3 facing;
            try { glass = SandKind.Named(part.Symbol("glass", "silica")); facing = Pane.FacingNamed(part.Symbol("facing", "up")); }
            catch (ArgumentException e) { throw new MachineFormatException($"pane {part.Id}: {e.Message}", part.Location); }
            var pane = new Pane(part.Id, room, Sun, side, thickness, count, glass.Transmittance, facing)
            {
                Strength = part.Number("strength", Pane.DefaultStrength),
            };
            _panes[part.Id] = pane;
            AddHeatSource(room, () => pane.Gain);
        }

        foreach (var part in def.Parts.Where(p => p.Kind == "smokejack"))
        {
            var over = part.Symbol("over", "");
            if (!_hearths.TryGetValue(over, out var fire))
                throw new MachineFormatException($"smokejack {part.Id} is over {over}, which is not a fire (hearth)", part.Location);
            double radius = part.Number("radius");
            _jetWheels[part.Id] = new JetWheel(null)
            {
                // what the pot doesn't take goes up the chimney
                ChimneyHeat = () => fire.Efficiency > 0 ? fire.HeatOut * (1 - fire.Efficiency) / fire.Efficiency : 0,
                ChimneyHeight = part.Number("chimney-height", 2),
                ChimneyArea = part.Number("chimney-area", 0.05),
                AmbientTemperature = _ambient,
                Radius = radius,
                MomentOfInertia = Math.Max(1e-5, part.Number("mass", 0.3) * radius * radius),
                Load = part.Number("load", 0),
                AirDrag = JetWheel.Windage(Outside.AirDensityAt(_ambient), (int)part.Number("vanes", 6),
                                           part.Number("width", 0.06) * part.Number("width", 0.06), radius),
            };
        }

        foreach (var part in def.Parts.Where(p => p.Kind == "bellows"))
        {
            var on = part.Symbol("on", "");
            if (!_hearths.TryGetValue(on, out var hearth))
                throw new MachineFormatException($"bellows {part.Id} is on {on}, which is not a hearth", part.Location);
            hearth.Airflow = part.Number("airflow");
            _bellows[part.Id] = hearth;
        }

        foreach (var lift in def.Lifts) _lifts[lift.Id] = BuildLift(def, lift);
        foreach (var src in def.Sources)
            _sources[src.Id] = new WaterSource(src.Id, TankNamed(src.Into, src.Location), src.Flow);
        foreach (var part in def.Parts.Where(p => p.Kind == "counterpoise"))
            _counterpoises[part.Id] = new Counterpoise(part.Id, TankNamed(part.Symbol("vessel", ""), part.Location),
                part.Number("vessel-mass"), part.Number("counterweight"), part.Number("radius"), part.Number("turn-deg") * Math.PI / 180)
            {
                Friction = part.Number("friction", 0),
                LeafInertia = part.Number("leaf-inertia", 0),
            };
        foreach (var ch in def.Channels) _channels[ch.Id] = BuildChannel(def, ch);
        foreach (var part in def.Parts.Where(p => p.Kind == "waterwheel"))
        {
            double r = part.Number("radius");
            string race = part.Symbol("race", ""), tail = part.Symbol("tail", "");
            if (race != "" && !_channels.ContainsKey(race))
                throw new MachineFormatException($"waterwheel {part.Id} stands in {race}, which is not a channel", part.Location);
            _wheels[part.Id] = new WaterWheel(part.Id, r, part.Number("width"), part.Number("mass") * r * r)
            {
                Load = part.Number("load", 0),
                Buckets = (int)part.Number("buckets", 0),
                BucketVolume = part.Number("bucket-volume", 0),
                SpillAngle = part.Number("spill-deg", 120) * Math.PI / 180,
                Tail = tail == "" ? null : TankNamed(tail, part.Location),
                Race = race == "" ? null : _channels[race],
                PaddleDepth = part.Number("paddle-depth", 0),
            };
        }
        foreach (var ch in def.Channels)
        {
            var channel = _channels[ch.Id];
            if (ch.Onto is not { } onto) continue;
            if (_hearths.TryGetValue(onto, out var hearth))
                channel.Pour = m3 => hearth.Douse(m3 * Physics.WaterDensity);
            else if (_boilers.TryGetValue(onto, out var boiler))
                channel.Pour = m3 => boiler.AddWater(m3 * Physics.WaterDensity, Math.Max(0, Ambient));
            else if (_wheels.TryGetValue(onto, out var wheel))
                channel.Pour = wheel.Pour;
            else throw new MachineFormatException($"channel {ch.Id} pours onto {onto}, which is not a hearth, boiler or water wheel", ch.Location);
            if (ch.To is not null)
                throw new MachineFormatException($"channel {ch.Id} runs into a tank; only a channel run off the scene can pour onto {onto}", ch.Location);
        }
        foreach (var part in def.Parts.Where(p => p.Kind == "sluice"))
        {
            var on = part.Symbol("on", "");
            if (!_channels.TryGetValue(on, out var channel))
                throw new MachineFormatException($"sluice {part.Id} stands on {on}, which is not a channel", part.Location);
            if (channel.Gate is not null)
                throw new MachineFormatException($"sluice {part.Id}: channel {on} already has a gate", part.Location);
            channel.Gate = _gates[part.Id] = new SluiceGate(part.Number("width", channel.Width), part.Number("height"), part.Number("opening", 1));
        }
        foreach (var part in def.Parts.Where(p => p.Kind == "float-valve")) BuildFloatValve(part);
        foreach (var part in def.Parts.Where(p => p.Kind == "leak")) BuildLeak(part);
        foreach (var part in def.Parts.Where(p => p.Kind == "float"))
        {
            var tank = TankNamed(part.Symbol("in", ""), part.Location);
            double mass = part.Number("mass"), area = part.Number("area"), height = part.Number("height", 0.1);
            if (!(mass > 0 && area > 0 && height > 0))
                throw new MachineFormatException($"float {part.Id}: mass, area and height must be more than 0", part.Location);
            _floats[part.Id] = new Float(part.Id, tank, mass, area, height);
        }
        foreach (var part in def.Parts.Where(p => p.Kind == "safety-valve")) BuildSafetyValve(part);
        foreach (var part in def.Parts.Where(p => p.Kind == "pump")) BuildPump(part);
        foreach (var c in def.Cylinders)
        {
            var piston = def.Part(c.Piston) ?? throw new MachineFormatException($"cylinder {c.Id}: no piston {c.Piston}", c.Location);
            if (!_boilers.TryGetValue(c.Boiler, out var boiler))
                throw new MachineFormatException($"cylinder {c.Id}: {c.Boiler} is not a boiler", c.Location);
            _cylinders[c.Id] = new AtmosphericCylinder(c.Id, boiler, piston.Number("bore"), piston.Number("stroke"), c.InjectionTemperature ?? _ambient + 40) // jet water warms ~40 K condensing the steam
            {
                PistonHeight = piston.Number("start", 0) * piston.Number("stroke"),
            };
            _cylinders[c.Id].Prime();
        }

        SetZones();
        Ambient = _ambient;   // hand it to every part now they all exist
        RegisterFields();
        // belts and grips register their own fields, which triggers and follows may watch and set: build them first
        BuildBelts(def);
        BuildGrips(def);
        BuildCams(def);
        BuildRatchets(def);
        BuildHoppers(def);
        BuildTriggers(def);
        BuildFollows(def);
    }

    private void BuildHoppers(MachineDef def)
    {
        foreach (var part in def.Parts.Where(p => p.Kind == "hopper"))
        {
            double area = part.Number("area"), grain = part.Number("grain"), orifice = part.Number("orifice");
            double size = part.Number("grain-size"), density = part.Number("density", 1600);
            if (area <= 0 || orifice <= 0 || size <= 0 || density <= 0)
                throw new MachineFormatException($"hopper {part.Id}: #:area, #:orifice, #:grain-size and #:density must be more than 0", part.Location);
            if (grain < 0) throw new MachineFormatException($"hopper {part.Id}: #:grain (kg) cannot be negative", part.Location);
            var hopper = _hoppers[part.Id] = new Hopper(part.Id, area, grain, orifice, size, density, ZoneOf(part.Id) is { } z ? z.Gravity : Outside.Gravity);
            string id = part.Id;
            _getters[$"{id}.level"] = () => hopper.Level * 100;                 // cm of grain
            _getters[$"{id}.mass"] = () => hopper.Mass;                         // kg left
            _getters[$"{id}.drained"] = () => hopper.Drained;                   // kg run out
            _getters[$"{id}.flow"] = () => hopper.Flow * 1000;                  // g/s, steady
            _getters[$"{id}.speed"] = () => hopper.SurfaceSpeed * 1000;         // mm/s the surface (and a weight on it) sinks
            _getters[$"{id}.empty"] = () => hopper.Empty ? 1 : 0;
            _getters[$"{id}.arched"] = () => hopper.Arched ? 1 : 0;             // the orifice is under five grains across
            _getters[$"{id}.gravity"] = () => hopper.Gravity;
            _setters[$"{id}.orifice"] = mm => hopper.Orifice = Math.Max(0, mm / 1000);   // open or close the gate
            _setters[$"{id}.grain"] = kg => hopper.Mass = Math.Max(0, kg);               // refill it
        }
    }

    private void BuildRatchets(MachineDef def)
    {
        foreach (var part in def.Parts.Where(p => p.Kind == "ratchet"))
        {
            string on = part.Symbol("on", "?");
            if (def.Part(on) is not { Kind: "wheel" } wheel)
                throw new MachineFormatException($"ratchet {part.Id} is on {on}, which is not a wheel, pulley or drum for its teeth to be cut on", part.Location);
            int teeth = (int)Math.Round(part.Number("teeth", 12));
            if (teeth < 3) throw new MachineFormatException($"ratchet {part.Id} needs at least three teeth", part.Location);
            double radius = part.Number("radius", 0);
            if (radius <= 0) radius = DrumRadius(wheel) * 1.5;                       // teeth a half again the wheel's radius unless said
            var ratchet = _ratchets[part.Id] = new Ratchet(part.Id, teeth, radius, part.Props.GetValueOrDefault("reverse") is SBool { Value: true });
            string id = part.Id;
            _getters[$"{id}.steps"] = () => ratchet.Steps;                            // teeth advanced
            _getters[$"{id}.pitch"] = () => ratchet.Pitch * 180 / Math.PI;            // degrees a tooth
            _getters[$"{id}.angle"] = () => ratchet.Angle * 180 / Math.PI;            // degrees the wheel has turned the allowed way
            _getters[$"{id}.locked"] = () => ratchet.Locked * 180 / Math.PI;          // degrees to the valley the pawl is in
            _getters[$"{id}.held"] = () => ratchet.Holding ? 1 : 0;
            _getters[$"{id}.torque"] = () => ratchet.Torque;                          // N·m the pawl carries now
            _getters[$"{id}.force"] = () => ratchet.Force;                            // N at the tooth circle
            _getters[$"{id}.peak-force"] = () => ratchet.PeakForce;
        }
    }

    private void BuildCams(MachineDef def)
    {
        foreach (var part in def.Parts.Where(p => p.Kind == "cam"))
        {
            string on = part.Symbol("on", "?");
            if (def.Part(on) is not { Kind: "wheel" })
                throw new MachineFormatException($"cam {part.Id} is on {on}, which is not a wheel, pulley or drum for it to be pegged on", part.Location);
            int pegs = (int)Math.Round(part.Number("pegs", 4));
            double lift = part.Number("lift", 0.1), rise = part.Number("rise", 0.5), mass = part.Number("mass", 5);
            if (pegs < 1) throw new MachineFormatException($"cam {part.Id} needs at least one peg", part.Location);
            if (lift <= 0 || mass <= 0) throw new MachineFormatException($"cam {part.Id}: #:lift and #:mass must be more than 0", part.Location);
            if (rise is <= 0 or > 1) throw new MachineFormatException($"cam {part.Id}: #:rise is the share of a peg's pitch it lifts over, more than 0 and at most 1, not {rise}", part.Location);
            var cam = _cams[part.Id] = new Cam(part.Id, pegs, lift, rise, mass, Outside.Gravity);
            string id = part.Id;
            _getters[$"{id}.height"] = () => cam.Height * 100;                 // cm the follower stands above its anvil
            _getters[$"{id}.strikes"] = () => cam.Strikes;
            _getters[$"{id}.speed"] = () => cam.LastStrikeSpeed;               // m/s of the last strike
            _getters[$"{id}.fastest"] = () => cam.FastestStrike;
            _getters[$"{id}.torque"] = () => cam.Torque;                       // N·m it asks of the wheel now
            _getters[$"{id}.work"] = () => cam.Work;                           // J the wheel has put into lifting it
            _getters[$"{id}.contact"] = () => cam.InContact ? 1 : 0;
            _getters[$"{id}.pitch"] = () => cam.Pitch * 180 / Math.PI;         // degrees between pegs
            _setters[$"{id}.mass"] = kg => cam.Mass = Math.Max(1e-6, kg);      // a heavier hammer
        }
    }

    private void BuildGrips(MachineDef def)
    {
        foreach (var part in def.Parts.Where(p => p.Kind == "grip"))
        {
            string on = part.Symbol("on", "world");
            if (on != "world" && (def.Part(on) is not { } host || host.Kind is "tank" or "boiler" or "grip" or "hearth" or "bellows" or "leak" or "sluice" or "float-valve" or "safety-valve" or "pump"))
                throw new MachineFormatException($"grip {part.Id} is on {on}, which is not a body it can hang from (a lever, wheel, block, post or other rigid part, or world)", part.Location);
            string kind = part.Symbol("kind", "tongs");
            if (kind is not ("tongs" or "hook"))
                throw new MachineFormatException($"grip {part.Id}: #:kind is tongs or hook, not {kind}", part.Location);
            double reach = part.Number("reach", 0.15), force = part.Number("force", 0), strength = part.Number("strength", 0);
            if (reach <= 0) throw new MachineFormatException($"grip {part.Id}: #:reach must be more than 0", part.Location);
            if (kind == "tongs" && force <= 0) throw new MachineFormatException($"tongs {part.Id} need a #:force above 0 to hold anything", part.Location);
            if (kind == "hook" && strength <= 0) throw new MachineFormatException($"hook {part.Id} needs a #:strength above 0 to carry anything", part.Location);
            var grip = _grips[part.Id] = new Grip(part.Id, reach, kind == "tongs", force, strength, _materials[part.Material].Friction)
            {
                Closed = part.Number("closed", 0) != 0,
            };
            string id = part.Id;
            _getters[$"{id}.closed"] = () => grip.Closed ? 1 : 0;
            _setters[$"{id}.closed"] = v =>
            {
                bool closed = v != 0;
                if (!closed) { grip.Overloaded = false; }
                grip.Closed = closed;
            };
            _getters[$"{id}.held"] = () => grip.Held ? 1 : 0;
            _getters[$"{id}.held-for"] = () => grip.HeldFor;                  // s
            _getters[$"{id}.load"] = () => grip.Load;                         // N
            _getters[$"{id}.capacity"] = () => grip.Capacity;                 // N
            _getters[$"{id}.overloaded"] = () => grip.Overloaded ? 1 : 0;
            _getters[$"{id}.force"] = () => grip.Force;                       // N a jaw presses with
            _setters[$"{id}.force"] = n => grip.Force = Math.Max(0, n);       // squeeze harder
            _setters[$"{id}.strength"] = n => grip.Strength = Math.Max(0, n);
        }
    }

    private static double DrumRadius(PartSpec p) =>
        p.Props.ContainsKey("radius") ? p.Number("radius") : p.Number("pitch-radius");

    private void BuildBelts(MachineDef def)
    {
        foreach (var spec in def.Belts)
        {
            PartSpec Drum(string id) => def.Part(id) is { Kind: "wheel" } p && (p.Props.ContainsKey("radius") || p.Props.ContainsKey("pitch-radius"))
                ? p : throw new MachineFormatException($"belt {spec.Id}: {id} is not a drum, pulley or wheel with a radius for a belt to run on", spec.Location);
            var a = Drum(spec.A);
            var b = Drum(spec.B);
            if (spec.A == spec.B) throw new MachineFormatException($"belt {spec.Id} needs two different drums", spec.Location);
            string axis = a.Symbol("axis", "z");
            if (axis != b.Symbol("axis", "z"))
                throw new MachineFormatException($"belt {spec.Id}: {spec.A} and {spec.B} turn on different axes ({axis} and {b.Symbol("axis", "z")}); a belt needs parallel axles", spec.Location);
            if (spec.Tension <= 0) throw new MachineFormatException($"belt {spec.Id}: a belt with no tension grips nothing (#:tension is in N)", spec.Location);
            // the axles' distance apart, across the axis
            double dx = a.At.X - b.At.X, dy = a.At.Y - b.At.Y, dz = a.At.Z - b.At.Z;
            double along = axis == "x" ? dx : axis == "y" ? dy : dz;
            double centres = Math.Sqrt(dx * dx + dy * dy + dz * dz - along * along);
            double ra = DrumRadius(a), rb = DrumRadius(b);
            if (centres <= Math.Abs(ra - rb))
                throw new MachineFormatException($"belt {spec.Id}: {spec.A} and {spec.B} are {centres * 1000:F0} mm apart, too close for a belt to reach round drums of {ra * 1000:F0} and {rb * 1000:F0} mm", spec.Location);
            var belt = _belts[spec.Id] = new Belt(spec.Id, ra, rb, centres, _materials[spec.Material].Friction, spec.Tension);
            _getters[$"{spec.Id}.capacity"] = () => belt.MaxForce;             // N
            _getters[$"{spec.Id}.force"] = () => belt.Force;                   // N carried, driver to driven
            _getters[$"{spec.Id}.slip"] = () => belt.Slip;                     // m/s of rim speed lost
            _getters[$"{spec.Id}.wrap"] = () => belt.Wrap * 180 / Math.PI;     // degrees round the smaller drum
            _getters[$"{spec.Id}.length"] = () => belt.Length;
            _getters[$"{spec.Id}.tension"] = () => belt.Tension;               // N pretension
            _setters[$"{spec.Id}.tension"] = n => belt.Tension = Math.Max(1e-6, n);   // tighten or slacken it
        }
    }

    private void BuildFollows(MachineDef def)
    {
        foreach (var spec in def.Follows)
        {
            if ((spec.Lever is null) == (spec.Rope is null))
                throw new MachineFormatException($"follow {spec.Id} must follow either a lever (#:lever) or a rope (#:rope), not both or neither", spec.Location);
            if (spec.Lever is { } lever && def.Part(lever) is not { Kind: "lever" })
                throw new MachineFormatException($"follow {spec.Id} follows {lever}, which is not a lever", spec.Location);
            if (spec.Rope is { } rope && def.Ropes.All(r => r.Id != rope))
                throw new MachineFormatException($"follow {spec.Id} follows {rope}, which is not a rope", spec.Location);
            if (spec.From == spec.To)
                throw new MachineFormatException($"follow {spec.Id}: #:from and #:to are the same, so nothing changes between them", spec.Location);
            if (!_setters.ContainsKey($"{spec.Target}.{spec.Field}"))
                throw new MachineFormatException($"follow {spec.Id} sets {spec.Target}.{spec.Field}, which is not a settable field", spec.Location);
            if (_follows.ContainsKey(spec.Id))
                throw new MachineFormatException($"two follows are called {spec.Id}", spec.Location);
            var f = _follows[spec.Id] = new Follow(spec);
            _getters[$"{spec.Id}.input"] = () => f.Input;
            _getters[$"{spec.Id}.value"] = () => double.IsNaN(f.Value) ? spec.Low : f.Value;
        }
    }

    /// <summary>Gives a follow its mechanism's reading (degrees turned by a lever, newtons of pull on a rope) and sets its field to match.</summary>
    public void ApplyFollow(string id, double input)
    {
        var f = _follows[id];
        f.Input = input;
        f.Value = f.Map(input);
        SetField(f.Spec.Target, f.Spec.Field, f.Value);
    }

    /// <summary>
    /// Triggers are built last, once every field exists, so a trigger's watched
    /// field and each of its actions can be checked against the real getters
    /// and setters and reported at the clause.
    /// </summary>
    private void BuildTriggers(MachineDef def)
    {
        foreach (var spec in def.Triggers)
        {
            bool body = spec.Body is not null;
            if (body == (spec.WatchTarget is not null))
                throw new MachineFormatException($"trigger {spec.Id} must watch either a body (#:watch part, with #:at and #:size) or a field (#:when target.field), not both or neither", spec.Location);
            if (body)
            {
                if (spec.At is null || spec.Size is null || spec.Size is { } sz && (sz.X <= 0 || sz.Y <= 0 || sz.Z <= 0))
                    throw new MachineFormatException($"trigger {spec.Id} needs #:at and a positive #:size to watch {spec.Body}", spec.Location);
                if (def.Part(spec.Body!) is null)
                    throw new MachineFormatException($"trigger {spec.Id} watches {spec.Body}, which is not a part", spec.Location);
            }
            else if (!_getters.ContainsKey($"{spec.WatchTarget}.{spec.WatchField}"))
                throw new MachineFormatException($"trigger {spec.Id} watches {spec.WatchTarget}.{spec.WatchField}, which is not a readable field", spec.Location);
            if (spec.Actions.Count == 0)
                throw new MachineFormatException($"trigger {spec.Id} does nothing: give it a #:set (target field value)", spec.Location);
            foreach (var a in spec.Actions)
                if (!_setters.ContainsKey($"{a.Target}.{a.Field}"))
                    throw new MachineFormatException($"trigger {spec.Id} sets {a.Target}.{a.Field}, which is not a settable field", spec.Location);
            if (_triggers.ContainsKey(spec.Id))
                throw new MachineFormatException($"two triggers are called {spec.Id}", spec.Location);
            var t = _triggers[spec.Id] = new Trigger(spec);
            _getters[$"{spec.Id}.fired"] = () => t.Fired ? 1 : 0;
            _getters[$"{spec.Id}.fired-at"] = () => t.FiredAt;          // s; -1 until it fires
        }
    }

    /// <summary>
    /// Tells a body trigger where its watched body is now; fires it (once) if
    /// the point is inside its box. Returns true on the call that fires it.
    /// </summary>
    public bool TestBodyTrigger(string id, double x, double y, double z)
    {
        var t = _triggers[id];
        if (t.Fired || !t.Contains(x, y, z)) return false;
        Fire(t);
        return true;
    }

    private void Fire(Trigger t)
    {
        t.Fired = true;
        t.FiredAt = Time;
        foreach (var a in t.Spec.Actions) SetField(a.Target, a.Field, a.Value);
    }

    private void StepFieldTriggers()
    {
        foreach (var t in _triggers.Values)
        {
            if (t.Fired || t.Spec.WatchTarget is null) continue;
            double v = GetField(t.Spec.WatchTarget, t.Spec.WatchField!);
            if (t.Spec.Rising ? v >= t.Spec.Threshold : v <= t.Spec.Threshold) Fire(t);
        }
    }

    /// <summary>
    /// Enclosures, largest first, so each finds the one it stands in (its
    /// #:at, the middle of its floor) already built; then every other part
    /// is stood in the innermost enclosure round its #:at, or the open air.
    /// Unsaid, an enclosure's air is its surroundings': their pressure, mix
    /// and temperature, as if it had been shut there.
    /// </summary>
    private void BuildEnclosures(MachineDef def)
    {
        bool Inside(Vec3 p, PartSpec box) =>
            Math.Abs(p.X - box.At.X) <= box.Number("size-x") / 2 && Math.Abs(p.Z - box.At.Z) <= box.Number("size-z") / 2
            && p.Y >= box.At.Y - 1e-9 && p.Y <= box.At.Y + box.Number("size-y");
        double VolumeOf(PartSpec box) => box.Number("size-x") * box.Number("size-y") * box.Number("size-z");
        var boxes = def.Parts.Where(p => p.Kind == "enclosure").OrderByDescending(VolumeOf).ToList();
        Zone Around(PartSpec part) =>
            boxes.Where(b => b.Id != part.Id && _enclosures.ContainsKey(b.Id) && Inside(part.At, b))
                 .OrderBy(VolumeOf).Select(b => (Zone)_enclosures[b.Id]).FirstOrDefault() ?? Outside;
        foreach (var box in boxes)
        {
            double v = VolumeOf(box);
            if (!(v > 0)) throw new MachineFormatException($"enclosure {box.Id} needs a size above 0 each way", box.Location);
            var around = Around(box);
            double Gas(string g) => box.Props.GetValueOrDefault(g) is SNumber n ? n.Value : 0;
            bool mixGiven = GasMix.Names.Any(g => box.Props.GetValueOrDefault(g) is SNumber);
            var air = mixGiven ? new GasMix(Gas("o2"), Gas("n2"), Gas("co2"), Gas("h2o"), Gas("ar")) : around.Air;
            if (air.Total <= 0) throw new MachineFormatException($"enclosure {box.Id}'s air has no gas in it", box.Location);
            var e = new Enclosure(box.Id, v, around,
                box.Props.GetValueOrDefault("pressure") is SNumber p ? p.Value : around.Pressure,
                box.Props.GetValueOrDefault("temperature") is SNumber t ? t.Value : around.Temperature, air)
            {
                Insulation = box.Number("insulation", 2),
                WallHeatCapacity = box.Number("heat-capacity", 0),
                Heater = box.Number("heater", 0),
                LeakArea = box.Number("leak", 0),
                Supply = box.Number("supply", 0),
                Cd = box.Number("coefficient", Enclosure.DefaultCoefficient),
            };
            _enclosures[box.Id] = e;
            if (around is Enclosure) _zoneOfPart[box.Id] = around;
        }
        foreach (var part in def.Parts.Where(p => p.Kind != "enclosure"))
            if (Around(part) is Enclosure e) _zoneOfPart[part.Id] = e;
    }

    /// <summary>Stands every part in its zone: the gravity it falls under, the air it breathes and pushes against.</summary>
    private void SetZones()
    {
        foreach (var (id, t) in _tanks) t.Zone = ZoneOf(id);
        foreach (var (id, c) in _crucibles) c.Zone = ZoneOf(id);
        foreach (var (id, b) in _boilers) b.Zone = ZoneOf(id);
        foreach (var (id, h) in _hearths) h.Zone = ZoneOf(id);
        foreach (var (id, w) in _jetWheels) w.Zone = ZoneOf(id);
        foreach (var l in _lifts.Values) l.Zone = l.From.Zone;
        foreach (var c in _cylinders.Values) c.Zone = ZoneOf(Def.Cylinders.First(s => _cylinders[s.Id] == c).Piston);
        foreach (var (id, p) in _pumps) p.Zone = ZoneOf(id);
        foreach (var (id, w) in _wheels) w.Zone = ZoneOf(id);
        foreach (var (id, c) in _capstans) c.Zone = ZoneOf(id);
        foreach (var (id, c) in _counterpoises) c.Zone = ZoneOf(id);
        foreach (var (id, p) in _pendulums) p.Zone = ZoneOf(id);
        foreach (var c in _channels.Values) c.Gravity = c.From.Zone.Gravity;
        foreach (var g in _gates.Values) g.Gravity = Outside.Gravity;
        SyncZones();
    }

    /// <summary>
    /// What changes with a zone's air, handed on to the parts in it: the
    /// temperature boilers cool to and water arrives at, a windmill's air
    /// density. Each step, since an enclosure's air warms and thins.
    /// </summary>
    private void SyncZones()
    {
        foreach (var (id, b) in _boilers) b.AmbientTemperature = ZoneOf(id).Temperature;
        foreach (var (id, h) in _hearths) h.AmbientTemperature = ZoneOf(id).Temperature;
        foreach (var (id, w) in _jetWheels) w.AmbientTemperature = ZoneOf(id).Temperature;
        foreach (var (id, m) in _windmills) m.AirDensity = ZoneOf(id).AirDensity;
        foreach (var a in _air) a.Ambient = a.Zone.Temperature;
    }

    /// <summary>A different planet, live — "what if Mars had Earth's gravity?". Every part keeps standing where it stands; only the numbers change.</summary>
    public void ChangePlanet(Planet planet)
    {
        Outside.Planet = planet;
        Sun.On(planet);
        SetZones();
    }

    private void BuildSafetyValve(PartSpec part)
    {
        var on = part.Symbol("on", "");
        if (!_boilers.TryGetValue(on, out var boiler))
            throw new MachineFormatException($"safety valve {part.Id} is on {on}, which is not a boiler", part.Location);
        double lift = part.Number("lift"), bore = part.Number("bore");
        if (lift <= 0 || bore <= 0)
            throw new MachineFormatException($"safety valve {part.Id}: lift and bore must be more than 0", part.Location);
        if (boiler.BurstPressure > 0 && lift >= boiler.BurstPressure)
            throw new MachineFormatException($"safety valve {part.Id} lifts at {lift} Pa, but {on} bursts at {boiler.BurstPressure} Pa", part.Location);
        var valve = new SafetyValve(lift, bore)
        {
            Cd = part.Number("coefficient", SafetyValve.DefaultCoefficient),
            Accumulation = part.Number("accumulation", SafetyValve.DefaultAccumulation),
        };
        boiler.Valves.Add(valve);
        _safetyValves[part.Id] = (valve, boiler);
    }

    private void BuildPump(PartSpec part)
    {
        var from = TankNamed(part.Symbol("from", ""), part.Location);
        var to = TankNamed(part.Symbol("to", ""), part.Location);
        if (from == to)
            throw new MachineFormatException($"pump {part.Id} draws from and delivers to the same tank", part.Location);
        double bore = part.Number("bore"), stroke = part.Number("stroke");
        double efficiency = part.Number("efficiency", LiftPump.DefaultEfficiency), force = part.Number("force", double.PositiveInfinity);
        if (bore <= 0 || stroke <= 0)
            throw new MachineFormatException($"pump {part.Id}: bore and stroke must be more than 0", part.Location);
        if (efficiency is <= 0 or > 1)
            throw new MachineFormatException($"pump {part.Id}: efficiency must be in (0, 1], got {efficiency}", part.Location);
        if (force <= 0)
            throw new MachineFormatException($"pump {part.Id}: force must be more than 0", part.Location);
        _pumps[part.Id] = new LiftPump(part.Id, from, to, part.At.Y, bore, stroke)
        {
            Efficiency = efficiency,
            Temperature = TemperatureOr(part, "temperature"),
            Rpm = Math.Max(0, part.Number("rpm", 0)),
            Force = force,
        };
    }

    private void BuildLeak(PartSpec part)
    {
        var on = part.Symbol("on", "");
        if (!_tanks.TryGetValue(on, out var tank))
            throw new MachineFormatException($"leak {part.Id} is on {on}, which is not a tank", part.Location);
        var into = part.Symbol("into", "");
        Tank? catchTank = null;
        if (into != "" && !_tanks.TryGetValue(into, out catchTank))
            throw new MachineFormatException($"leak {part.Id} runs into {into}, which is not a tank", part.Location);
        double height = part.Number("height"), area = part.Number("area", 0), evaporation = part.Number("evaporation", 0);
        if (height < 0 || height > tank.Height)
            throw new MachineFormatException($"leak {part.Id}: a hole at {height} m is not in {on}'s wall (0 to {tank.Height} m)", part.Location);
        if (area < 0 || evaporation < 0)
            throw new MachineFormatException($"leak {part.Id}: area and evaporation cannot be negative", part.Location);
        _leaks[part.Id] = Fluids.AddLeak(new TankLeak(tank, height, area, catchTank)
        {
            Cd = part.Number("coefficient", TankLeak.DischargeCoefficient),
            Evaporation = evaporation,
            Bore = part.Number("bore", 0),
        });
        if (part.Number("bore", 0) > 0) _leaks[part.Id].Lift = part.Number("lift", 0);
    }

    /// <summary>
    /// A float valve rides in the tank its feed fills — an inflow's, a
    /// pipe's or a channel's far tank — and throttles that feed.
    /// </summary>
    private void BuildFloatValve(PartSpec part)
    {
        var on = part.Symbol("on", "");
        FloatValve Make(Tank tank) => new(tank, part.Number("shut"), part.Number("travel"));
        FloatValve valve;
        Func<double> flow;
        string Taken(string feed) => $"float-valve {part.Id}: {feed} {on} already has a float valve";
        if (_sources.TryGetValue(on, out var src))
        {
            if (src.Valve is not null) throw new MachineFormatException(Taken("inflow"), part.Location);
            src.Valve = valve = Make(src.Into);
            flow = () => src.Flow;
        }
        else if (_pipes.TryGetValue(on, out var pipe))
        {
            if (pipe.Valve is not null) throw new MachineFormatException(Taken("pipe"), part.Location);
            pipe.Valve = valve = Make(pipe.To);
            flow = () => pipe.Flow;
        }
        else if (_channels.TryGetValue(on, out var ch))
        {
            if (ch.To is null)
                throw new MachineFormatException($"float-valve {part.Id}: channel {on} runs off the scene; the float rides in the tank a feed fills", part.Location);
            if (ch.Valve is not null) throw new MachineFormatException(Taken("channel"), part.Location);
            ch.Valve = valve = Make(ch.To);
            flow = () => ch.Flow;
        }
        else throw new MachineFormatException($"float-valve {part.Id} is on {on}, which is not an inflow, pipe or channel", part.Location);
        _floatValves[part.Id] = (valve, flow);
    }

    /// <summary>
    /// A channel runs from its tank's port lip either to another tank's port
    /// or out of the scene to an end point. Its length, unless given, is the
    /// gap between the two tanks' walls (tanks are square, side √area) or
    /// from the wall to the end point.
    /// </summary>
    private Channel BuildChannel(MachineDef def, ChannelSpec spec)
    {
        var (from, lip) = TankPort(spec.From, spec.Location);
        var fromPart = def.Part(spec.From.Part)!;
        double Half(PartSpec tank) => Math.Sqrt(tank.Number("area")) / 2;
        Tank? to = null;
        double endY;
        (double X, double Z) far;
        double farHalf = 0;
        if (spec.To is { } toRef)
        {
            (to, endY) = TankPort(toRef, spec.Location);
            var toPart = def.Part(toRef.Part)!;
            far = (toPart.At.X, toPart.At.Z);
            farHalf = Half(toPart);
        }
        else if (spec.End is { } end)
        {
            endY = end.Y;
            far = (end.X, end.Z);
        }
        else throw new MachineFormatException($"channel {spec.Id} needs a tank to run into, or an end point", spec.Location);
        // the path runs centre to centre through any waypoints; the tanks' half-walls come off the ends
        double apart = 0;
        (double X, double Z) prev = (fromPart.At.X, fromPart.At.Z);
        foreach (var p in (spec.Via ?? []).Append(far))
        {
            apart += Math.Sqrt(Math.Pow(p.X - prev.X, 2) + Math.Pow(p.Z - prev.Z, 2));
            prev = p;
        }
        double length = spec.Length ?? Math.Max(0.1, apart - Half(fromPart) - farHalf);
        if (endY > lip)
            throw new MachineFormatException($"channel {spec.Id} would run uphill: its lip is at {lip:F2} m, its end at {endY:F2} m", spec.Location);
        // a dynamic reach (issue #36): cells half a metre long unless told, 10 to 400 of them
        int cells = !spec.Dynamic ? 0 : spec.Cells ?? Math.Clamp((int)Math.Round(length / 0.5), 10, 400);
        if (spec.Dynamic && cells is < 2 or > 2000)
            throw new MachineFormatException($"channel {spec.Id}: #:cells must be from 2 to 2000, got {cells}", spec.Location);
        return new Channel(spec.Id, from, lip, to, endY, spec.Width, length) { Cells = cells };
    }

    /// <summary>
    /// A lift's water per turn and where it draws and delivers, from the
    /// shape of the machine doing it. A screw's axle runs along X tilted up
    /// by tilt-deg, so its ends sit half its length either side of At; a
    /// noria scoops at the bottom of its rim and empties at the top.
    /// </summary>
    private WaterLift BuildLift(MachineDef def, LiftSpec spec)
    {
        var by = def.Part(spec.By) ?? throw new MachineFormatException($"lift {spec.Id}: no part {spec.By}", spec.Location);
        var from = TankNamed(spec.From, spec.Location);
        var to = TankNamed(spec.To, spec.Location);
        switch (by.Kind, by.Symbol("shape", ""))
        {
            case ("screw", _):
            {
                double tilt = by.Number("tilt-deg", 0), radius = by.Number("radius");
                double half = by.Number("length") / 2 * Math.Sin(tilt * Math.PI / 180);
                double perTurn = by.Number("starts") * WaterLift.ScrewPocketVolume(
                    radius, by.Number("core-radius"), by.Number("pitch"), (int)by.Number("starts"),
                    by.Number("blade-thickness"), tilt);
                return new WaterLift(spec.Id, from, to, perTurn,
                                     intakeElevation: by.At.Y - half - radius, intakeDepth: 2 * radius,
                                     dischargeElevation: by.At.Y + half);
            }
            case ("piston", _):
            {
                double bore = by.Number("bore");
                // the pump draws at the bottom of the sump's water and delivers at the top of its stroke
                return new WaterLift(spec.Id, from, to, 0,
                                     intakeElevation: from.BaseElevation, intakeDepth: 0.05,
                                     dischargeElevation: by.At.Y + by.Number("stroke"))
                { VolumePerMetre = Math.PI * bore * bore / 4 };
            }
            case ("wheel", "noria"):
            {
                double radius = by.Number("radius");
                return new WaterLift(spec.Id, from, to, by.Number("buckets") * by.Number("bucket-volume"),
                                     intakeElevation: by.At.Y - radius, intakeDepth: by.Number("bucket-depth"),
                                     dischargeElevation: by.At.Y + radius);
            }
            default:
                throw new MachineFormatException($"lift {spec.Id}: {spec.By} is a {by.Symbol("shape", by.Kind)}, which can't lift water (a screw or a noria can)", spec.Location);
        }
    }

    private void RegisterFields()
    {
        _getters["scene.ambient"] = () => Ambient;                     // °C
        _setters["scene.ambient"] = c => Ambient = Math.Max(-273.15, c);
        _getters["scene.air-density"] = () => Outside.AirDensity;      // kg/m³
        _getters["scene.gravity"] = () => Outside.Gravity;             // m/s²
        _setters["scene.gravity"] = g => ChangePlanet(Planet with { Gravity = Math.Max(0, g) });
        _getters["scene.pressure"] = () => Outside.Pressure / 1000;    // kPa, absolute
        _setters["scene.pressure"] = kPa => ChangePlanet(Planet with { Pressure = Math.Max(0, kPa * 1000) });
        _getters["scene.boiling-point"] = () => Outside.BoilingPoint;  // °C, where water's vapour pressure reaches the air's
        _getters["scene.oxygen"] = () => Outside.OxygenFraction * 100; // % of the air by volume
        _getters["scene.molar-mass"] = () => Outside.MolarMass * 1000; // g/mol of the air
        _getters["scene.solar-constant"] = () => Sun.SolarConstant;    // W/m² above the air
        _getters["scene.sol"] = () => Sun.SolNumber;                   // the run's sol, 1 first
        _getters["scene.sols"] = () => Sun.Sols;                       // sols since the midnight before the run
        _getters["scene.air-mass"] = () => Sun.AirMass;                // thicknesses of air the beam crosses
        _getters["scene.dust"] = () => Weather?.Dust ?? -Math.Log(Sun.SkyTransmittance);   // optical depth
        _getters["scene.storm"] = () => Weather?.Storm is null ? 0 : 1;
        _getters["scene.relay"] = () => Weather?.Relay == true ? 1 : 0; // the relay orbiter is overhead
        _getters["scene.next-pass"] = () => Weather?.NextPass ?? -1;    // local hours to the next pass
        _getters["scene.time"] = () => Sun.Time;                        // solar hours
        _setters["scene.time"] = h => Sun.Time = ((h % 24) + 24) % 24;
        _getters["scene.day"] = () => Sun.Day;
        _setters["scene.day"] = d => Sun.Day = Math.Clamp((int)Math.Round(d), 1, 365);
        _getters["scene.latitude"] = () => Sun.Latitude;
        _setters["scene.latitude"] = deg => Sun.Latitude = Math.Clamp(deg, -90, 90);
        _getters["scene.clock-rate"] = () => Sun.ClockRate;            // sun-seconds per second; 0 holds it still
        _setters["scene.clock-rate"] = r => Sun.ClockRate = r;
        _getters["scene.sun-elevation"] = () => Sun.Elevation;         // degrees
        _getters["scene.sun-azimuth"] = () => Sun.Azimuth;             // degrees from north towards east
        _getters["scene.irradiance"] = () => Sun.DirectNormal;         // W/m², direct beam
        foreach (var (id, e) in _enclosures)
        {
            _getters[$"{id}.pressure"] = () => e.Pressure / 1000;             // kPa, absolute
            _setters[$"{id}.pressure"] = kPa => e.Pressure = Math.Max(0, kPa * 1000);   // pumped up or bled down, same mix
            _getters[$"{id}.gauge"] = () => e.GaugePressure / 1000;           // kPa over the air outside
            _getters[$"{id}.temperature"] = () => e.Temperature;              // °C
            _getters[$"{id}.air-density"] = () => e.AirDensity;               // kg/m³
            _getters[$"{id}.boiling-point"] = () => e.BoilingPoint;           // °C
            _getters[$"{id}.mass"] = () => e.Mass;                            // kg of gas
            _getters[$"{id}.flow"] = () => e.Flow * 1000;                     // g/s out of the hole (negative: in)
            _getters[$"{id}.lost"] = () => e.Lost;                            // kg out, net
            _getters[$"{id}.choked"] = () => e.Choked ? 1 : 0;
            _getters[$"{id}.leak"] = () => e.LeakArea * 10000;                // cm²
            _setters[$"{id}.leak"] = cm2 => e.LeakArea = cm2 / 10000;         // patch it: 0
            _getters[$"{id}.heater"] = () => e.Heater;                        // W
            _setters[$"{id}.heater"] = w => e.Heater = Math.Max(0, w);
            _getters[$"{id}.insulation"] = () => e.Insulation;                // W/K
            _setters[$"{id}.insulation"] = ua => e.Insulation = Math.Max(0, ua);
            _getters[$"{id}.heat"] = () => e.HeatInput;                       // W from fires and mirrors
            _getters[$"{id}.supply"] = () => e.Supply * 1000;                 // L/s of the surroundings' air blown in
            _setters[$"{id}.supply"] = ls => e.Supply = Math.Max(0, ls / 1000);
            for (int i = 0; i < GasMix.Names.Length; i++)
            {
                int gas = i;
                _getters[$"{id}.{GasMix.Names[i]}"] = () => e.TotalMoles > 0 ? e.Moles[gas] / e.TotalMoles * 100 : 0;   // % by volume
                _getters[$"{id}.{GasMix.Names[i]}-pressure"] = () => e.PartialPressure(gas) / 1000;                     // kPa (Dalton)
            }
        }
        foreach (var (id, p) in _panes)
        {
            _getters[$"{id}.gain"] = () => p.Gain;                        // W of sunlight into the room
            _getters[$"{id}.incidence"] = () => p.Incidence;              // cos of the sun's angle to the glass
            _getters[$"{id}.stress"] = () => p.Stress / 1e6;              // MPa, 0.29 q (a/t)²
            _getters[$"{id}.crack-pressure"] = () => p.CrackPressure / 1000;   // kPa across it that cracks it
            _getters[$"{id}.cracked"] = () => p.Cracked ? 1 : 0;
            _getters[$"{id}.crack-time"] = () => double.IsNaN(p.CrackTime) ? -1 : p.CrackTime;
            _getters[$"{id}.transmittance"] = () => p.Transmittance;
            _getters[$"{id}.area"] = () => p.Area;                        // m² of glass
            _getters[$"{id}.strength"] = () => p.Strength / 1e6;          // MPa: an advanced setting
            _setters[$"{id}.strength"] = mpa => p.Strength = Math.Max(0, mpa * 1e6);
        }
        foreach (var (id, c) in _crucibles)
        {
            _getters[$"{id}.temperature"] = () => c.Temperature;         // °C
            _getters[$"{id}.flux"] = () => c.Flux;                       // W/m² on the spot: C·I
            _getters[$"{id}.power"] = () => c.HeatInput;                 // W landing on the spot
            _getters[$"{id}.radiation"] = () => c.Radiation;             // W its hot face gives back
            _getters[$"{id}.stagnation"] = () => c.Stagnation;           // °C it would stop at in this light
            _getters[$"{id}.melted"] = () => c.Melted;                   // kg of glass
            _getters[$"{id}.melt-time"] = () => double.IsNaN(c.MeltTime) ? -1 : c.MeltTime;   // s, when all of it had melted
            _getters[$"{id}.transmittance"] = () => c.Sand.Transmittance; // of its glass
            _getters[$"{id}.absorbed"] = () => c.Absorbed / 1e6;         // MJ
        }
        foreach (var (id, d) in _doors)
        {
            _getters[$"{id}.open"] = () => d.Open;                            // 0 shut .. 1 wide
            _setters[$"{id}.open"] = o => d.Open = o;
            _getters[$"{id}.flow"] = () => d.Flow * 1000;                     // g/s from its #:from to its #:to
            _getters[$"{id}.passed"] = () => d.Passed;                        // kg from to to, net
            _getters[$"{id}.moved"] = () => d.Moved;                          // kg through it either way
            _getters[$"{id}.choked"] = () => d.Choked ? 1 : 0;
        }
        foreach (var (id, p) in _gasPumps)
        {
            _getters[$"{id}.speed"] = () => p.Speed * 1000;                   // L/s swept
            _setters[$"{id}.speed"] = ls => p.Speed = ls / 1000;
            _getters[$"{id}.until"] = () => p.Until / 1000;                   // kPa it stops at
            _setters[$"{id}.until"] = kPa => p.Until = Math.Max(0, kPa * 1000);
            _getters[$"{id}.running"] = () => p.Running ? 1 : 0;
            _getters[$"{id}.flow"] = () => p.Flow * 1000;                     // g/s
            _getters[$"{id}.moved"] = () => p.Moved;                          // kg
            _getters[$"{id}.power"] = () => p.Power;                          // W
            _getters[$"{id}.work"] = () => p.Work / 1000;                     // kJ
        }
        foreach (var (id, m) in _mirrors)
        {
            _getters[$"{id}.power"] = () => m.Power;                   // W onto the target
            _getters[$"{id}.cosine"] = () => m.Cosine;                 // cos(θ/2)
            _getters[$"{id}.collected"] = () => m.Collected / 1000;    // kJ so far
            _getters[$"{id}.area"] = () => m.Area;
            _getters[$"{id}.dust"] = () => m.Dust;                     // share of its light dust stops; 0 clean
            _setters[$"{id}.dust"] = d => m.Dust = d;                  // clean it: 0
            _setters[$"{id}.area"] = a => m.Area = Math.Max(0, a);     // cover it: 0
        }
        foreach (var (id, tank) in _tanks)
        {
            _getters[$"{id}.water"] = () => tank.WaterVolume * 1000;   // L
            _getters[$"{id}.level"] = () => tank.Level * 100;          // cm
            _getters[$"{id}.ice"] = () => tank.Ice * 1000;            // mm thick
            _getters[$"{id}.frozen-solid"] = () => tank.FrozenSolid ? 1 : 0;
            _setters[$"{id}.water"] = liters => tank.WaterVolume = Math.Clamp(liters / 1000, 0, tank.Capacity);
        }
        foreach (var (id, boiler) in _boilers)
        {
            _getters[$"{id}.fire"] = () => boiler.HeatInput;           // W
            _getters[$"{id}.temperature"] = () => boiler.Temperature;  // °C
            _getters[$"{id}.pressure"] = () => boiler.GaugePressure / 1000; // kPa
            _getters[$"{id}.water"] = () => boiler.WaterMass;          // kg
            _getters[$"{id}.fed"] = () => boiler.WaterFed;             // kg of feed water taken in
            _getters[$"{id}.heat"] = () => boiler.HeatDelivered / 1000; // kJ from the fire, all told
            _getters[$"{id}.lost"] = () => boiler.HeatLost / 1000;      // kJ lost to the air, all told
            _setters[$"{id}.fire"] = watts =>
            {
                boiler.HeatInput = Math.Max(0, watts);
                if (_ownHeat.ContainsKey(boiler)) _ownHeat[boiler] = boiler.HeatInput;
            };
            _getters[$"{id}.burst"] = () => boiler.Burst ? 1 : 0;
            _getters[$"{id}.burst-time"] = () => boiler.BurstTime;          // s
            _getters[$"{id}.burst-pressure"] = () => boiler.BurstGauge / 1000; // kPa it gave way at
            _getters[$"{id}.flashed"] = () => boiler.Flashed;              // kg flashed to steam as it burst
            _getters[$"{id}.vented"] = () => boiler.Vented;                // kg out of its safety valves
        }
        foreach (var (id, (v, _)) in _safetyValves)
        {
            _getters[$"{id}.lift"] = () => v.LiftPressure / 1000;          // kPa gauge
            _setters[$"{id}.lift"] = kPa => v.LiftPressure = kPa * 1000;   // tie it down: a lift past the boiler's rating
            _getters[$"{id}.opening"] = () => v.Opening;
            _getters[$"{id}.flow"] = () => v.Flow * 1000;                  // g/s of steam
            _getters[$"{id}.vented"] = () => v.Vented;                     // kg
        }
        foreach (var (id, h) in _hearths)
        {
            _getters[$"{id}.fuel"] = () => h.Fuel;                     // kg
            _getters[$"{id}.power"] = () => h.Power;                   // W released
            _getters[$"{id}.lit"] = () => h.Lit ? 1 : 0;
            _getters[$"{id}.burned"] = () => h.FuelBurned;             // kg
            _getters[$"{id}.energy"] = () => h.EnergyReleased / 1e6;   // MJ
            _getters[$"{id}.soak"] = () => h.Soak;                     // kg of water lying on the fuel
            _getters[$"{id}.doused"] = () => h.Doused;                 // kg poured on, all told
            _getters[$"{id}.boiled"] = () => h.Boiled;                 // kg of it boiled off
            _getters[$"{id}.drowned"] = () => h.Drowned ? 1 : 0;
            _getters[$"{id}.draught"] = () => h.Draught;
            _getters[$"{id}.breathing"] = () => h.Breathing ? 1 : 0;          // its air holds oxygen enough to burn
            _getters[$"{id}.oxygen"] = () => h.Zone.OxygenFraction * 100;      // % of the air it breathes
            _getters[$"{id}.oxygen-used"] = () => h.OxygenUsed;                // kg taken from its room
            _getters[$"{id}.oxygen-limit"] = () => h.OxygenLimit * 100;        // %, below which it goes out
            _setters[$"{id}.oxygen-limit"] = pc => h.OxygenLimit = Math.Clamp(pc / 100, 0, 1);
            _setters[$"{id}.fuel"] = kg => h.Fuel = Math.Max(0, kg);
            _setters[$"{id}.power"] = w => h.Power = Math.Max(0, w);
        }
        foreach (var (id, h) in _bellows)
        {
            _getters[$"{id}.airflow"] = () => h.Airflow;
            _setters[$"{id}.airflow"] = m3s => h.Airflow = Math.Max(0, m3s);
        }
        foreach (var (id, rotor) in _rotors)
        {
            _getters[$"{id}.rpm"] = () => rotor.Rpm;
            _getters[$"{id}.thrust"] = () => rotor.Thrust;
        }
        foreach (var (id, w) in _jetWheels)
        {
            _getters[$"{id}.rpm"] = () => w.Rpm;
            _getters[$"{id}.omega"] = () => w.AngularVelocity;
            _getters[$"{id}.jet-speed"] = () => w.JetVelocity;
            _getters[$"{id}.steam-flow"] = () => w.SteamFlow * 1000;      // g/s
            _getters[$"{id}.push"] = () => w.Push;
            _getters[$"{id}.power"] = () => w.Power;
            _getters[$"{id}.load"] = () => w.Load;
            _getters[$"{id}.air-flow"] = () => w.SteamFlow;               // kg/s, for a smoke jack
            _getters[$"{id}.warming"] = () => w.DraughtWarming;             // K, for a smoke jack
            _setters[$"{id}.load"] = nm => w.Load = Math.Max(0, nm);
        }
        foreach (var (id, pipe) in _pipes)
        {
            _getters[$"{id}.flow"] = () => pipe.Flow * 1000;               // L/s
            _getters[$"{id}.jet-height"] = () => pipe.Flow > 0 ? pipe.JetHeight * 100 : 0; // cm
        }
        foreach (var (id, lift) in _lifts)
        {
            _getters[$"{id}.flow"] = () => lift.Flow * 1000;           // L/s
            _getters[$"{id}.rpm"] = () => lift.Rpm;
            _getters[$"{id}.load-torque"] = () => lift.LoadTorque;     // N·m
            _getters[$"{id}.per-turn"] = () => lift.VolumePerTurn * 1000; // L
            _setters[$"{id}.rpm"] = rpm => lift.Rpm = rpm;
        }
        foreach (var (id, p) in _pumps)
        {
            _getters[$"{id}.flow"] = () => p.Flow * 1000;              // L/s out of the spout
            _getters[$"{id}.delivered"] = () => p.Delivered * 1000;    // L, all told
            _getters[$"{id}.strokes"] = () => p.Strokes;
            _getters[$"{id}.bucket"] = () => p.Bucket * 100;           // cm above the barrel's foot
            _getters[$"{id}.lift"] = () => p.SuctionLift;              // m, the water's surface to the bucket's lowest point
            _getters[$"{id}.limit"] = () => p.Limit;                   // m the atmosphere can hold a column up
            _getters[$"{id}.column"] = () => p.Column;                 // m the water stands over the surface
            _getters[$"{id}.broken"] = () => p.Broken ? 1 : 0;
            _getters[$"{id}.pull"] = () => p.Pull;                     // N on the rod
            _getters[$"{id}.max-pull"] = () => p.MaxPull;              // N, the most it has needed
            _getters[$"{id}.work"] = () => p.Work / 1000;              // kJ done on the rod, net
            _getters[$"{id}.lifted"] = () => p.Lifted / 1000;          // kJ given the water
            _getters[$"{id}.stalled"] = () => p.Stalled ? 1 : 0;
            _getters[$"{id}.rpm"] = () => p.Rpm;
            _setters[$"{id}.rpm"] = rpm => p.Rpm = Math.Max(0, rpm);
            _getters[$"{id}.force"] = () => double.IsPositiveInfinity(p.Force) ? -1 : p.Force; // N the drive can pull; -1 unlimited
            _setters[$"{id}.force"] = n => p.Force = n > 0 ? n : double.PositiveInfinity;
        }
        foreach (var (id, src) in _sources)
            _getters[$"{id}.flow"] = () => src.Flow * 1000;            // L/s
        foreach (var (id, ch) in _channels)
        {
            _getters[$"{id}.flow"] = () => ch.Flow * 1000;             // L/s
            _getters[$"{id}.depth"] = () => ch.Depth * 100;            // cm
            _getters[$"{id}.velocity"] = () => ch.Velocity;            // m/s
            if (!ch.Dynamic) continue;
            // a reach holding water (issue #36): flow is what enters at the head; depth and velocity are at its middle
            double probe = ch.Length / 2;
            _getters[$"{id}.outflow"] = () => ch.Outflow * 1000;       // L/s leaving the foot
            _getters[$"{id}.stored"] = () => ch.Stored * 1000;         // L standing in the reach
            _getters[$"{id}.front"] = () => ch.Front;                  // m from the head to the wet edge
            _getters[$"{id}.arrival"] = () => ch.Arrival;              // s when water first reached the foot; -1 not yet
            _getters[$"{id}.probe"] = () => probe;                     // m down the reach that depth-at reads
            _setters[$"{id}.probe"] = m => probe = Math.Clamp(m, 0, ch.Length);
            _getters[$"{id}.depth-at"] = () => ch.Depths[ch.CellAt(probe)] * 100;   // cm at the probe
            _getters[$"{id}.velocity-at"] = () => ch.VelocityAt(ch.CellAt(probe));  // m/s at the probe
        }
        foreach (var (id, g) in _gates)
        {
            _getters[$"{id}.opening"] = () => g.Opening;               // 0 shut .. 1 fully drawn
            _getters[$"{id}.flow"] = () => g.Flow * 1000;              // L/s under the gate
            _getters[$"{id}.over"] = () => g.OverFlow * 1000;          // L/s over its top
            _getters[$"{id}.head"] = () => g.OrificeHead * 100;        // cm above the slot's middle
            _setters[$"{id}.opening"] = o => g.Opening = o;
        }
        foreach (var (id, (v, flow)) in _floatValves)
        {
            _getters[$"{id}.opening"] = () => v.Opening;               // 0 seated .. 1 fully clear
            _getters[$"{id}.flow"] = () => flow() * 1000;              // L/s let through
            _getters[$"{id}.shut"] = () => v.ShutLevel * 100;          // cm above the tank's floor
            _setters[$"{id}.shut"] = cm => v.ShutLevel = Math.Clamp(cm / 100, 0, v.Tank.Height);
        }
        foreach (var (id, l) in _leaks)
        {
            _getters[$"{id}.flow"] = () => l.Flow * 1000;              // L/s out of the hole
            _getters[$"{id}.head"] = () => l.Head * 100;               // cm of water above the hole
            _getters[$"{id}.lost"] = () => l.Lost * 1000;              // L leaked so far
            _getters[$"{id}.evaporated"] = () => l.Evaporated * 1000;  // L taken from the surface so far
            _getters[$"{id}.area"] = () => l.Area * 10000;             // cm²
            _setters[$"{id}.area"] = cm2 => l.Area = cm2 / 10000;      // stop it with a thumb: 0
            if (l.Bore > 0)
            {
                _getters[$"{id}.lift"] = () => l.Lift * 1000;          // mm the plug stands off its seat
                _setters[$"{id}.lift"] = mm => l.Lift = mm / 1000;     // fully open from a quarter of the bore up
            }
        }
        foreach (var (id, w) in _wheels)
        {
            _getters[$"{id}.rpm"] = () => w.Rpm;
            _getters[$"{id}.torque"] = () => w.Torque;                 // N·m from the water
            _getters[$"{id}.power"] = () => w.Power;                   // W into the millstone
            _getters[$"{id}.work"] = () => w.Work / 1000;              // kJ done on the millstone
            _getters[$"{id}.water"] = () => w.Water;                   // kg on the descending arc
            _getters[$"{id}.taken"] = () => w.Taken;                   // kg caught, all told
            _getters[$"{id}.overflow"] = () => w.Overflow;             // kg spilled on arrival
            _getters[$"{id}.load"] = () => w.Load;
            _setters[$"{id}.load"] = t => w.Load = Math.Max(0, t);
        }
        foreach (var (id, c) in _capstans)
        {
            _getters[$"{id}.held"] = () => c.Held ? 1 : 0;
            _getters[$"{id}.grounded"] = () => c.Grounded ? 1 : 0;
            _getters[$"{id}.height"] = () => c.Height;                 // m, the load above the ground
            _getters[$"{id}.speed"] = () => c.Velocity;                // m/s, + up
            _getters[$"{id}.lowered"] = () => c.Lowered;               // m run out
            _getters[$"{id}.hauled"] = () => c.Hauled;                 // m brought in
            _getters[$"{id}.load-tension"] = () => c.LoadTension;      // N at the load
            _getters[$"{id}.ratio"] = () => c.Ratio;                   // e^(μθ)
            _getters[$"{id}.least-hold"] = () => c.LeastHold;          // N: m·g·e^(−μθ)
            _getters[$"{id}.hauling-pull"] = () => c.HaulingPull;      // N: m·g·e^(μθ)
            _getters[$"{id}.mu"] = () => c.Mu;
            _getters[$"{id}.hold"] = () => c.Hold;
            _setters[$"{id}.hold"] = n => c.Hold = Math.Max(0, n);
            _getters[$"{id}.load"] = () => c.LoadMass;
            _setters[$"{id}.load"] = kg => c.LoadMass = Math.Max(1e-6, kg);
        }
        foreach (var (id, m) in _windmills)
        {
            _getters[$"{id}.rpm"] = () => m.Rpm;
            _getters[$"{id}.torque"] = () => m.Torque;                 // N·m from the wind
            _getters[$"{id}.power"] = () => m.Power;                   // W into the millstone
            _getters[$"{id}.wind-power"] = () => m.WindPower;          // W the wind carries through the sails' disc
            _getters[$"{id}.cp"] = () => m.PowerCoefficient;           // fraction of it taken; never above 16/27
            _getters[$"{id}.tsr"] = () => m.TipSpeedRatioNow;          // sail tip speed ÷ wind speed
            _getters[$"{id}.work"] = () => m.Work / 1000;              // kJ done on the millstone
            _getters[$"{id}.wind"] = () => m.Wind;
            _setters[$"{id}.wind"] = v => m.Wind = Math.Max(0, v);
            _getters[$"{id}.load"] = () => m.Load;
            _setters[$"{id}.load"] = t => m.Load = Math.Max(0, t);
        }
        foreach (var (id, c) in _cylinders)
        {
            _getters[$"{id}.pressure"] = () => c.Pressure / 1000;      // kPa absolute
            _getters[$"{id}.force"] = () => c.Force;                   // N, down
            _getters[$"{id}.strokes"] = () => c.Strokes;
            _getters[$"{id}.injecting"] = () => c.Injecting ? 1 : 0;
            _getters[$"{id}.steam-used"] = () => c.SteamUsed;          // kg
            _setters[$"{id}.piston-height"] = h => c.PistonHeight = h;
        }
        foreach (var air in _air)
            foreach (var tank in _tanks.Values.Where(t => t.Air == air))
            {
                _getters[$"{tank.Name}.air-pressure"] = () => air.GaugePressure / 1000; // kPa
                _getters[$"{tank.Name}.air-temperature"] = () => air.Temperature;     // °C
                _getters[$"{tank.Name}.air-volume"] = () => air.Volume * 1000;        // L
            }
        foreach (var (id, cp) in _counterpoises)
        {
            _getters[$"{id}.angle"] = () => cp.Angle * 180 / Math.PI;               // deg, 0 shut
            _getters[$"{id}.hanging"] = () => cp.Hanging;                           // kg on the vessel's rope
            _getters[$"{id}.torque"] = () => cp.Torque;                             // N·m, + opening
        }
        foreach (var (id, f) in _floats)
        {
            _getters[$"{id}.height"] = () => f.Bottom;                 // m, the elevation of its bottom
            _getters[$"{id}.draft"] = () => f.Draft * 100;             // cm it sinks to afloat
            _getters[$"{id}.submerged"] = () => f.Submerged * 100;     // cm of it under water now
            _getters[$"{id}.grounded"] = () => f.Grounded ? 1 : 0;
        }
        foreach (var (id, d) in _diggers)
        {
            _getters[$"{id}.dug"] = () => d.Dug;                       // m³ taken out
            _getters[$"{id}.work"] = () => d.Work / 1000;              // kJ done
            _getters[$"{id}.specific-work"] = () => d.SpecificWork / 1000;   // kJ per m³
            _getters[$"{id}.depth"] = () => d.Depth;                   // m the trench is down to
            _getters[$"{id}.collapsed"] = () => d.Collapsed ? 1 : 0;
            _getters[$"{id}.collapse-depth"] = () => d.CollapseDepth;  // m when a wall fell in; -1 none has
            _getters[$"{id}.done"] = () => d.Done ? 1 : 0;
            _getters[$"{id}.power"] = () => d.Power;                   // W the gang works at
            _setters[$"{id}.power"] = w => d.Power = Math.Max(0, w);
        }
        foreach (var (id, p) in _pendulums)
        {
            _getters[$"{id}.angle"] = () => p.Angle * 180 / Math.PI;                // deg from hanging
            _getters[$"{id}.amplitude"] = () => p.Amplitude * 180 / Math.PI;        // deg, at the last turning point
            _getters[$"{id}.peak-time"] = () => p.PeakTime;                         // s, when it turned there
            _getters[$"{id}.swings"] = () => p.Swings;                              // turning points since release
            _getters[$"{id}.stopped"] = () => p.Stopped ? 1 : 0;
            _getters[$"{id}.energy"] = () => p.Energy;                              // J of swing left
            _getters[$"{id}.heat"] = () => p.Bearing.Heat;                          // J made in the bearing
            _getters[$"{id}.sliding"] = () => p.Bearing.Sliding * 1000;             // mm the pin has slid in its eye
            _getters[$"{id}.wear"] = () => p.Bearing.Wear;                          // mm³ worn off
            _getters[$"{id}.mu"] = () => p.Bearing.Mu;
            _getters[$"{id}.drag"] = () => p.Bearing.Drag;
            _setters[$"{id}.mu"] = mu => p.Bearing.Mu = Math.Max(0, mu);
            _setters[$"{id}.drag"] = c => p.Bearing.Drag = Math.Max(0, c);
        }
    }

    private (IHeated target, PartSpec part) HeatedNamed(string id, PartSpec by) =>
        _boilers.TryGetValue(id, out var boiler) ? (boiler, Def.Part(id)!)
        : _tanks.TryGetValue(id, out var vessel) && vessel.Air is { } air ? (air, Def.Part(id)!)
        : _enclosures.TryGetValue(id, out var room) ? (room, Def.Part(id)!)
        : _crucibles.TryGetValue(id, out var pot) ? (pot, Def.Part(id)!)
        : throw new MachineFormatException($"{by.Kind} {by.Id} heats {id}, which is not a boiler, a sealed vessel or an enclosure", by.Location);

    private void AddHeatSource(IHeated target, Func<double> watts)
    {
        if (!_heatSources.TryGetValue(target, out var list))
        {
            _heatSources[target] = list = [];
            _ownHeat[target] = target.HeatInput;   // a boiler's own #:fire, kept under whatever else heats it
        }
        list.Add(watts);
    }

    public void Step(double dt)
    {
        Sun.Step(dt);
        if (Weather is { } wx)
        {
            wx.Step(dt, _mirrors.Values);
            if (wx.Ambient is { } air) Ambient = air;   // the air on the planet's daily curve
        }
        foreach (var h in _hearths.Values) h.Step(dt);
        foreach (var m in _mirrors.Values) m.Step(dt);
        foreach (var (target, sources) in _heatSources) target.HeatInput = _ownHeat[target] + sources.Sum(w => w());
        foreach (var e in _enclosures.Values) e.Step(dt);
        foreach (var c in _crucibles.Values) c.Step(dt);
        foreach (var p in _panes.Values) p.Step(dt);
        foreach (var d in _doors.Values) d.Step(dt);
        foreach (var p in _gasPumps.Values) p.Step(dt);
        if (_zoneOfPart.Count > 0) SyncZones();
        foreach (var air in _air) air.Step(dt);
        Fluids.Step(dt);
        // Springs and open channels, in steps short enough that a weir can't
        // overshoot: a pool's level answers its own outflow within a second.
        int n = Math.Max(1, (int)Math.Ceiling(dt / 0.01));
        for (int i = 0; i < n; i++)
        {
            foreach (var src in _sources.Values) src.Step(dt / n);
            foreach (var ch in _channels.Values) ch.Step(dt / n);
            foreach (var w in _wheels.Values) w.Step(dt / n);
        }
        foreach (var lift in _lifts.Values) lift.Step(dt);
        foreach (var pump in _pumps.Values) pump.Step(dt);
        foreach (var cp in _counterpoises.Values) cp.Step(dt);
        foreach (var m in _windmills.Values) m.Step(dt);
        foreach (var c in _capstans.Values) c.Step(dt);
        foreach (var p in _pendulums.Values) p.Step(dt);
        foreach (var d in _diggers.Values) d.Step(dt);
        foreach (var rotor in _rotors.Values) rotor.Step(dt); // steps its own boiler
        foreach (var w in _jetWheels.Values) w.Step(dt);      // so does a jet wheel
        foreach (var c in _cylinders.Values) c.Step(dt);
        foreach (var (id, boiler) in _boilers)
            if (!_rotorBoiler.ContainsValue(id))
                boiler.Step(dt, _cylinders.Values.Where(c => c.Boiler == boiler).Sum(c => c.SteamDraw));
        // a boiler's lost warmth goes into the room it stands in
        foreach (var (id, boiler) in _boilers)
            if (ZoneOf(id) is Enclosure room)
            {
                room.AddHeat(boiler.HeatLost - _boilerLossSeen.GetValueOrDefault(id));
                _boilerLossSeen[id] = boiler.HeatLost;
            }
        foreach (var g in _grips.Values) if (g.Held) g.HeldFor += dt;
        foreach (var h in _hoppers.Values) { h.Gravity = ZoneOf(h.Id) is { } hz ? hz.Gravity : Outside.Gravity; h.Step(dt); }
        Time += dt;
        StepFieldTriggers();
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
