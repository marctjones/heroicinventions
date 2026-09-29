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
    private readonly Dictionary<string, string> _rotorBoiler = []; // rotor id → boiler id
    private readonly List<AirPocket> _air = [];
    private readonly Dictionary<string, WaterLift> _lifts = [];
    private readonly Dictionary<string, AtmosphericCylinder> _cylinders = [];
    private readonly Dictionary<string, WaterSource> _sources = [];
    private readonly Dictionary<string, Channel> _channels = [];
    private readonly Dictionary<string, SluiceGate> _gates = [];
    private readonly Dictionary<string, (FloatValve Valve, Func<double> Flow)> _floatValves = [];
    private readonly Dictionary<string, TankLeak> _leaks = [];
    private readonly Dictionary<string, (SafetyValve Valve, Boiler Boiler)> _safetyValves = [];
    private readonly Dictionary<string, LiftPump> _pumps = [];
    private readonly Dictionary<string, WaterWheel> _wheels = [];
    private readonly Dictionary<string, Windmill> _windmills = [];
    private readonly Dictionary<string, Capstan> _capstans = [];
    private readonly Dictionary<string, Counterpoise> _counterpoises = [];
    private readonly Dictionary<string, Pendulum> _pendulums = [];
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
    public IReadOnlyList<AirPocket> AirPockets => _air;
    public IReadOnlyDictionary<string, WaterLift> Lifts => _lifts;
    public IReadOnlyDictionary<string, AtmosphericCylinder> Cylinders => _cylinders;
    public IReadOnlyDictionary<string, WaterSource> Sources => _sources;
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
    public IReadOnlyDictionary<string, Counterpoise> Counterpoises => _counterpoises;
    /// <summary>Pendulums hung on a bearing (#:bearing-radius): swung here, not by Jolt, so their friction and wear can be checked.</summary>
    public IReadOnlyDictionary<string, Pendulum> Pendulums => _pendulums;
    public double Time { get; private set; }

    /// <summary>
    /// Named, human-scaled readouts and controls for the live link and the
    /// HUD: "kettle.fire" (W), "kettle.temperature" (°C), "kettle.pressure"
    /// (kPa gauge), "kettle.water" (kg), "ball.rpm", "basin.water" (L),
    /// "nozzle.jet-height" (cm). Every tank, boiler, rotor and pipe gets an
    /// entry; only a few fields (fire, water level) are settable.
    /// </summary>
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
            Fluids.Ambient = value;
            foreach (var b in _boilers.Values) b.AmbientTemperature = value;
            foreach (var h in _hearths.Values) h.AmbientTemperature = value;
            foreach (var a in _air) a.Ambient = value;
            foreach (var m in _windmills.Values) m.AirDensity = Physics.AirDensityAt(value);
        }
    }
    private double _ambient = 20;

    /// <summary>A part's #:temperature if it gave one, else the ambient — water drawn from outside is at the air's temperature, or just above freezing in a frost.</summary>
    private double TemperatureOr(PartSpec part, string key) =>
        part.Props.GetValueOrDefault(key) is SNumber t ? t.Value : Math.Max(0, _ambient);

    public MachineRuntime(MachineDef def, MaterialLibrary materials)
    {
        Def = def;
        _ambient = def.Ambient;

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
                case "rotor" or "block" or "pendulum" or "lever" or "ramp" or "wheel" or "screw" or "fixture" or "piston" or "post" or "hearth" or "bellows" or "sluice" or "float-valve" or "leak" or "safety-valve" or "pump":
                    break; // rotors need their steam connection first; the rest are pure Jolt rigid-body physics, engine-side only
                default:
                    throw new MachineFormatException($"unknown part kind {part.Kind}", part.Location);
            }
        }

        // Sealed air is created after the tanks are filled: its P·V constant
        // is fixed from the air volume at the moment it is sealed.
        foreach (var air in def.SealedAir)
            _air.Add(new AirPocket(air.Tanks.Select(t => TankNamed(t, air.Location)), air.TubeVolume, sealedAtC: _ambient)
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

        foreach (var part in def.Parts.Where(p => p.Kind == "hearth"))
        {
            var heats = part.Symbol("heats", "");
            IHeated target = _boilers.TryGetValue(heats, out var boiler) ? boiler
                : _tanks.TryGetValue(heats, out var vessel) && vessel.Air is { } air ? air
                : throw new MachineFormatException($"hearth {part.Id} heats {heats}, which is neither a boiler nor a sealed vessel", part.Location);
            _hearths[part.Id] = new Hearth(target, part.Number("power"), part.Number("fuel"),
                                           part.Symbol("fuel-kind", "wood"), part.Number("efficiency", 0.5));
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

        Ambient = _ambient;   // hand it to every part now they all exist
        RegisterFields();
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
        });
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
        return new Channel(spec.Id, from, lip, to, endY, spec.Width, length);
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
        _getters["scene.air-density"] = () => Physics.AirDensityAt(Ambient);   // kg/m³
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
            _setters[$"{id}.fire"] = watts => boiler.HeatInput = Math.Max(0, watts);
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

    public void Step(double dt)
    {
        foreach (var h in _hearths.Values) h.Step(dt);
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
        foreach (var rotor in _rotors.Values) rotor.Step(dt); // steps its own boiler
        foreach (var c in _cylinders.Values) c.Step(dt);
        foreach (var (id, boiler) in _boilers)
            if (!_rotorBoiler.ContainsValue(id))
                boiler.Step(dt, _cylinders.Values.Where(c => c.Boiler == boiler).Sum(c => c.SteamDraw));
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
