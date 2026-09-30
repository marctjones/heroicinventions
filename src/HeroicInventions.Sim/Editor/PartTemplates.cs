using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// Builds a freshly-placed <see cref="PartSpec"/> for one of the part kinds
/// the palette offers, with the same default values machine.rkt's clause
/// patterns give an omitted keyword argument (a tank's #:water defaults to
/// 0, a lever's #:damping to 8.0, and so on) — so a part the editor places
/// behaves exactly like the equivalent hand-written Racket clause would.
///
/// The "primitive" kinds (tank, boiler, block, pendulum, lever, ramp,
/// piston) need no mesh: MachineView builds their geometry straight from
/// these props. The catalogue kinds (wheel, screw, fixture) carry a
/// generated mesh's numbers instead, taken from a <see cref="CatalogueEntry"/>
/// rather than invented here, since only Racket can compute a gear's exact
/// volume and inertia.
/// </summary>
public static class PartTemplates
{
    /// <summary>Part kinds the palette can place without a catalogue (no generated mesh).</summary>
    public static readonly IReadOnlyList<string> PrimitiveKinds = ["tank", "boiler", "hearth", "jetwheel", "smokejack", "rotor", "block", "pendulum", "lever", "ramp", "piston", "post", "sluice", "waterwheel", "counterpoise", "float-valve", "leak", "safety-valve", "pump", "bellows", "windmill", "capstan", "mirror", "enclosure"];

    public static PartSpec Create(string kind, string id, Vec3 at, string material) => kind switch
    {
        "tank" => new PartSpec(id, "tank", material, at,
            Props(("area", 0.05), ("height", 0.3), ("water", 0)),
            [new PortSpec("inlet", "water", 0), new PortSpec("outlet", "water", 0)],
            null),
        "boiler" => new PartSpec(id, "boiler", material, at,
            // a litre of water, heated by a hearth or a mirror aimed at it (or its own #:fire)
            Props(("radius", 0.15), ("height", 0.3), ("water", 1.0), ("fire", 0), ("temperature", 20), ("burst", 0)),
            [new PortSpec("steam", "steam", 0.3)],
            null),
        // a wood fire; #:heats names the pot it sits under
        "hearth" => new PartSpec(id, "hearth", material, at,
            new Dictionary<string, SExpr>
            {
                ["heats"] = new SSymbol("?"), ["power"] = new SNumber(3000), ["fuel"] = new SNumber(1.0),
                ["fuel-kind"] = new SSymbol("wood"), ["efficiency"] = new SNumber(0.5),
            },
            [], null),
        // Branca's wheel: flat paddles struck by a boiler's steam jet
        "jetwheel" => new PartSpec(id, "jetwheel", material, at,
            Props(("radius", 0.15), ("bore", 0.0025), ("paddles", 8), ("width", 0.03), ("mass", 0.5), ("load", 0)),
            [new PortSpec("steam-in", "steam", 0)],
            null),
        // a smoke jack: vanes in a chimney, turned by a fire's rising hot air
        "smokejack" => new PartSpec(id, "smokejack", material, at,
            new Dictionary<string, SExpr>
            {
                ["over"] = new SSymbol("?"), ["radius"] = new SNumber(0.15), ["vanes"] = new SNumber(6),
                ["width"] = new SNumber(0.06), ["mass"] = new SNumber(0.3), ["load"] = new SNumber(0),
                ["chimney-height"] = new SNumber(2), ["chimney-area"] = new SNumber(0.05),
            },
            [], null),
        // Heron's aeolipile: a sphere spun by its own steam jets
        "rotor" => new PartSpec(id, "rotor", material, at,
            Props(("radius", 0.06), ("wall", 0.001), ("bore", 0.002), ("arm", 0.08), ("nozzles", 2)),
            [new PortSpec("steam-in", "steam", 0)],
            null),
        "block" => new PartSpec(id, "block", material, at, Props(("size", 0.1), ("tilt-deg", 0)), [], null),
        // no bearing (Jolt swings it) until given a #:bearing-radius; then the sim swings it against the bearing's friction
        "pendulum" => new PartSpec(id, "pendulum", material, at,
            Props(("length", 0.5), ("start-angle-deg", 30), ("bearing-radius", new SBool(false)),
                  ("bearing-mu", 0), ("bearing-drag", 0), ("bearing-wear", 0)),
            [], null),
        "lever" => new PartSpec(id, "lever", material, at,
            Props(("length", 1.0), ("start-angle-deg", 0), ("pivot-fraction", 0.5), ("limit-deg", 18),
                  ("damping", 8.0), ("spring-stiffness", 0), ("spring-rest-deg", 0))
                .Concat([new KeyValuePair<string, SExpr>("axis", new SSymbol("z")),
                         new KeyValuePair<string, SExpr>("limit-lower-deg", new SBool(false)),
                         new KeyValuePair<string, SExpr>("limit-upper-deg", new SBool(false)),
                         new KeyValuePair<string, SExpr>("section", new SBool(false))])
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            [], null),
        "ramp" => new PartSpec(id, "ramp", material, at, Props(("length", 1.0), ("width", 0.5), ("angle-deg", 15)), [], null),
        "piston" => new PartSpec(id, "piston", material, at, Props(("bore", 0.1), ("stroke", 0.3), ("start", 0), ("rod-mass", 0)), [], null),
        "post" => new PartSpec(id, "post", material, at,
            new Dictionary<string, SExpr> { ["size-x"] = new SNumber(0.2), ["size-y"] = new SNumber(1.0), ["size-z"] = new SNumber(0.2), ["round"] = new SBool(false) },
            [], null),
        // a gate across the head of a channel; #:on names it (the editor's
        // placeholder, until set), #:width is the channel's unless given
        "sluice" => new PartSpec(id, "sluice", material, at,
            new Dictionary<string, SExpr> { ["on"] = new SSymbol("?"), ["height"] = new SNumber(1.0), ["opening"] = new SNumber(1.0), ["width"] = new SBool(false) },
            [], null),
        // a float and plug in the tank that the feed #:on (an inflow, pipe or channel) fills
        "float-valve" => new PartSpec(id, "float-valve", material, at,
            new Dictionary<string, SExpr> { ["on"] = new SSymbol("?"), ["shut"] = new SNumber(0.2), ["travel"] = new SNumber(0.02) },
            [], null),
        // a hole in the wall of the tank #:on, #:height above its floor; #:into names a tank under its jet
        "leak" => new PartSpec(id, "leak", material, at,
            new Dictionary<string, SExpr>
            {
                ["on"] = new SSymbol("?"), ["height"] = new SNumber(0.1), ["area"] = new SNumber(0.0005),
                ["coefficient"] = new SNumber(0.6), ["into"] = new SBool(false), ["evaporation"] = new SNumber(0),
                ["bore"] = new SNumber(0), ["lift"] = new SNumber(0),
            },
            [], null),
        // in the lid of the boiler #:on; lifts at #:lift gauge Pa, fully open #:accumulation over it
        "safety-valve" => new PartSpec(id, "safety-valve", material, at,
            new Dictionary<string, SExpr>
            {
                ["on"] = new SSymbol("?"), ["lift"] = new SNumber(100_000), ["bore"] = new SNumber(0.008),
                ["coefficient"] = new SNumber(0.8), ["accumulation"] = new SNumber(0.1),
            },
            [], null),
        // a bellows or fan forcing draught into the hearth #:on; #:airflow (m3/s) starts at 0, still until set
        "bellows" => new PartSpec(id, "bellows", material, at,
            new Dictionary<string, SExpr> { ["on"] = new SSymbol("?"), ["airflow"] = new SNumber(0) },
            [], null),
        // a lift pump whose barrel's foot is at `at`, drawing from #:from into #:to; #:force #f is a drive as strong as it takes
        "pump" => new PartSpec(id, "pump", material, at,
            new Dictionary<string, SExpr>
            {
                ["from"] = new SSymbol("?"), ["to"] = new SSymbol("?"), ["bore"] = new SNumber(0.15), ["stroke"] = new SNumber(0.5),
                ["rpm"] = new SNumber(20), ["efficiency"] = new SNumber(0.8), ["force"] = new SBool(false), ["temperature"] = new SBool(false),   // #f: the water is at the air's temperature, as in the DSL
            },
            [], null),
        // an overshot wheel by default: 12 buckets; give it a #:race to drive it undershot
        "waterwheel" => new PartSpec(id, "waterwheel", material, at,
            new Dictionary<string, SExpr>
            {
                ["radius"] = new SNumber(1.0), ["width"] = new SNumber(0.3), ["mass"] = new SNumber(100), ["load"] = new SNumber(0),
                ["buckets"] = new SNumber(12), ["bucket-volume"] = new SNumber(0.005), ["spill-deg"] = new SNumber(120),
                ["tail"] = new SBool(false), ["race"] = new SBool(false), ["paddle-depth"] = new SNumber(0),
            },
            [], null),
        // a 0.5 m2 heliostat of polished bronze, throwing the sun onto the boiler or sealed vessel #:onto names
        // a 2 m room of its surroundings' air (#f: theirs), shut in; set #:pressure, #:o2 … to fill it, #:leak to hole it
        "enclosure" => new PartSpec(id, "enclosure", material, at,
            new Dictionary<string, SExpr>
            {
                ["size-x"] = new SNumber(2), ["size-y"] = new SNumber(2), ["size-z"] = new SNumber(2),
                ["pressure"] = new SBool(false), ["temperature"] = new SBool(false),
                ["insulation"] = new SNumber(2), ["heat-capacity"] = new SNumber(0), ["heater"] = new SNumber(0),
                ["leak"] = new SNumber(0), ["supply"] = new SNumber(0), ["coefficient"] = new SNumber(0.6),
                ["o2"] = new SBool(false), ["n2"] = new SBool(false), ["co2"] = new SBool(false), ["h2o"] = new SBool(false), ["ar"] = new SBool(false),
            },
            [], null),
        "mirror" => new PartSpec(id, "mirror", material, at,
            new Dictionary<string, SExpr> { ["onto"] = new SSymbol("?"), ["area"] = new SNumber(0.5), ["reflectivity"] = new SNumber(0.85) },
            [], null),
        // a bollard 1.5 m up: 200 kg hanging 1 m below on one turn of hemp, nobody holding the other end until set
        "capstan" => new PartSpec(id, "capstan", material, at,
            new Dictionary<string, SExpr>
            {
                ["turns"] = new SNumber(1), ["load"] = new SNumber(200), ["hold"] = new SNumber(0), ["mu"] = new SBool(false),
                ["drop"] = new SNumber(1), ["radius"] = new SNumber(0.15), ["rope"] = new SSymbol("hemp"),
            },
            [], null),
        // four sails 10 m from hub to tip facing a 6 m/s breeze, the stones free (load 0) until set
        "windmill" => new PartSpec(id, "windmill", material, at,
            new Dictionary<string, SExpr>
            {
                ["radius"] = new SNumber(10), ["mass"] = new SNumber(1500), ["wind"] = new SNumber(6), ["load"] = new SNumber(0),
                ["cp"] = new SNumber(0.3), ["tip-speed-ratio"] = new SNumber(2.5),
            },
            [], null),
        // a spindle turned by a hanging vessel against a counterweight; #:vessel names the tank
        "counterpoise" => new PartSpec(id, "counterpoise", material, at,
            new Dictionary<string, SExpr>
            {
                ["vessel"] = new SSymbol("?"), ["vessel-mass"] = new SNumber(2), ["counterweight"] = new SNumber(4),
                ["radius"] = new SNumber(0.05), ["turn-deg"] = new SNumber(90), ["friction"] = new SNumber(0),
                ["leaf-inertia"] = new SNumber(0), ["leaf-width"] = new SNumber(1), ["leaf-height"] = new SNumber(2),
            },
            [], null),
        _ => throw new ArgumentException($"not a primitive part kind: {kind}; catalogue kinds are placed from a CatalogueEntry instead"),
    };

    /// <summary>
    /// A wheel/screw/fixture part from a catalogue entry — the same shape
    /// props emit.rkt writes for a machine-authored shaped part (shape,
    /// mesh, volume, inertia-x/y/z, then the shape's own props), plus the
    /// clause-specific defaults (machine.rkt's #:axis, #:drive-rpm, ...).
    /// </summary>
    public static PartSpec Create(CatalogueEntry entry, string id, Vec3 at, string material)
    {
        var props = new Dictionary<string, SExpr>
        {
            ["catalogue"] = new SSymbol(entry.Id),
            ["shape"] = new SSymbol(entry.ShapeKind),
            ["mesh"] = new SString(entry.MeshStem),
            ["volume"] = new SNumber(entry.Volume),
            ["inertia-x"] = new SNumber(entry.Inertia.X),
            ["inertia-y"] = new SNumber(entry.Inertia.Y),
            ["inertia-z"] = new SNumber(entry.Inertia.Z),
        };
        foreach (var (k, v) in entry.ShapeProps) props[k] = v;
        foreach (var (k, v) in entry.PartKind switch
                 {
                     "wheel" => Props(("axis", "z"), ("angle-deg", 0), ("drive-rpm", 0)).Append(new("drive-torque", new SBool(false))),
                     "screw" => Props(("tilt-deg", 0), ("drive-rpm", 0)).Append(new("drive-torque", new SBool(false))),
                     _ => Props(("turn-deg", 0)),
                 })
            props[k] = v;
        return new PartSpec(id, entry.PartKind, material, at, props, [], null);
    }

    private static Dictionary<string, SExpr> Props(params (string Key, object Value)[] items) =>
        items.ToDictionary(kv => kv.Key, kv => ToSExpr(kv.Value));

    private static SExpr ToSExpr(object v) => v switch
    {
        SExpr e => e,
        string s => new SSymbol(s),
        bool b => new SBool(b),
        _ => new SNumber(Convert.ToDouble(v)),
    };
}
