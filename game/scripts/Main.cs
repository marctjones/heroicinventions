using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions;

/// <summary>How the camera frames one machine so it fills the screen.</summary>
public readonly record struct CameraProfile(Vector3 Eye, Vector3 LookAt, float FovDegrees);

/// <summary>
/// A one-machine-at-a-time viewer. Pick a machine from the menu; it loads
/// alone, framed close by its own camera profile, and starts running
/// immediately. Restart reloads it fresh from its .machine file — a true
/// reset, not a rewind. Speed buttons control simulated time directly,
/// which matters more than screen size for something like the aeolipile's
/// rotor: it settles near 4,000 rpm, faster than any camera can resolve
/// at 1×, so slow motion is what actually makes it visible spinning up.
///
/// The headline HUD is deliberately small — total energy and its
/// kinetic/potential mix, a single speed number, and whichever of
/// efficiency or "energy retained" applies (see MachineView.EnergyHud) —
/// with the full per-part numbers available behind a Details toggle
/// rather than always on screen.
///
/// Two environment variables help headless recording and remote control:
///   HEROIC_AUTORUN=1              start running immediately (no click)
///   HEROIC_AUTOSELECT=&lt;name&gt;       select a machine at startup
///   HEROIC_LIVE_LINK=1            open the Racket live-link TCP server
///   HEROIC_TRACE=&lt;path&gt;          write a frame per HEROIC_TRACE_DT sim seconds
///                                 (default 0.1) there, for heroic/godothost
///   HEROIC_SET="t f v; ..."       set sim fields (target field value) before the first step
/// </summary>
public partial class Main : Node3D
{
    private const string MachinesDir = "res://machines";

    private static readonly CameraProfile MenuCamera = new(new Vector3(0, 1.4f, 3.0f), new Vector3(0, 0.6f, 0), 60);
    /// <summary>
    /// Machines that throw something: the camera follows the missile once
    /// it leaves, widening to keep both the machine and it in view. The
    /// throw is over in a fraction of a second and the stone or bolt lands
    /// 15-50 m away, so a camera framed on the machine showed nothing happen.
    /// </summary>
    private static readonly Dictionary<string, string> FollowBody = new()
    {
        ["trebuchet"] = "stone",
        ["torsion-catapult"] = "stone",
        ["vitruvian-catapulta"] = "bolt",
    };

    private static readonly Dictionary<string, CameraProfile> Profiles = new()
    {
        ["aeolipile"] = new(new Vector3(0, 0.55f, 0.85f), new Vector3(0, 0.32f, 0), 42),
        ["herons-fountain"] = new(new Vector3(0, 1.3f, 2.2f), new Vector3(0, 0.6f, 0), 45),
        ["material-samples"] = new(new Vector3(0, 0.7f, 1.6f), new Vector3(0, 0.3f, 0.3f), 45),
        ["branca-steam-wheel"] = new(new Vector3(0.35f, 0.95f, 1.35f), new Vector3(0.15f, 0.5f, 0), 45),
        ["kitchen-smoke-jack"] = new(new Vector3(0.9f, 1.75f, 1.3f), new Vector3(0, 1.2f, 0), 50),
        ["solar-steam-wheel"] = new(new Vector3(1.2f, 2.4f, 4.6f), new Vector3(0, 0.8f, 0), 50),
        ["ball-ramp"] = new(new Vector3(3.2f, 1.1f, -1.2f), new Vector3(0f, 0.25f, -1.1f), 50),
        ["wake-clock"] = new(new Vector3(0.2f, 0.9f, 3.2f), new Vector3(0f, 0.45f, 0), 50),
        ["sand-timer"] = new(new Vector3(0.6f, 1.2f, 3.6f), new Vector3(0.6f, 0.7f, 0), 55),
        ["ratchet-windlass"] = new(new Vector3(1.6f, 1.6f, 4.6f), new Vector3(0f, 1.4f, -1.5f), 55),
        ["trip-hammer"] = new(new Vector3(1.5f, 1.4f, 3.3f), new Vector3(0.2f, 0.75f, -0.5f), 55),
        ["tunnel-test"] = new(new Vector3(0f, 1.6f, 4.5f), new Vector3(0f, 1f, 0), 50),
        ["crate-tongs"] = new(new Vector3(1f, 1.1f, 4.2f), new Vector3(1f, 0.75f, 0), 55),
        ["belt-drive"] = new(new Vector3(0.3f, 1.6f, 2.6f), new Vector3(0.3f, 1f, -0.25f), 55),
        ["holy-water"] = new(new Vector3(0.75f, 1.1f, 3.3f), new Vector3(0.75f, 0.5f, 0), 50),
        ["trip-sluice"] = new(new Vector3(2f, 1.3f, 6.2f), new Vector3(2f, 0.75f, 0), 55),
        ["pendulum-demo"] = new(new Vector3(0, 0.7f, 1.3f), new Vector3(0, 0.5f, 0), 42),
        ["lever-demo"] = new(new Vector3(0, 0.75f, 1.5f), new Vector3(0, 0.55f, 0), 42),
        ["inclined-plane-demo"] = new(new Vector3(0, 1.6f, 2.8f), new Vector3(0, 0.3f, -0.6f), 55),
        ["newtons-cradle"] = new(new Vector3(0, 0.75f, 1.0f), new Vector3(0, 0.6f, 0), 38),
        ["trebuchet"] = new(new Vector3(-1.8f, 2.4f, 7.0f), new Vector3(-2.0f, 1.3f, 0), 60), // wide enough to watch the stone land
        ["torsion-catapult"] = new(new Vector3(0.2f, 1.8f, 3.6f), new Vector3(0.2f, 1.0f, 0), 55),
        ["antikythera-lunar-train"] = new(new Vector3(0.13f, 0.19f, 0.19f), new Vector3(0.022f, 0.09f, 0.004f), 38),
        ["archimedes-screw"] = new(new Vector3(0.5f, 2.2f, 7.0f), new Vector3(0, 1.4f, 0), 50),
        ["hama-noria"] = new(new Vector3(0.5f, 6.5f, 17.5f), new Vector3(0.3f, 2.0f, -1.5f), 55),
        ["newcomen-engine"] = new(new Vector3(1.5f, 4.5f, 12.5f), new Vector3(0, 3.6f, 0), 50),
        ["roman-crane"] = new(new Vector3(3.0f, 3.8f, 10.5f), new Vector3(0.3f, 3.2f, 0.5f), 50),
        ["vitruvian-catapulta"] = new(new Vector3(1.7f, 1.4f, 2.0f), new Vector3(0, 0.7f, -0.3f), 50),
        ["component-gallery"] = new(new Vector3(3.5f, 5.5f, 13f), new Vector3(3.5f, 0.6f, 0), 60),
        ["newcomen-hearth"] = new(new Vector3(1.5f, 4.5f, 12.5f), new Vector3(0, 3.6f, 0), 50),
        ["hearth-engine"] = new(new Vector3(0, 1.0f, 1.7f), new Vector3(0, 0.75f, 0), 45),
        ["post-and-lintel-crane"] = new(new Vector3(2.2f, 1.7f, 4.6f), new Vector3(0, 1.1f, 0), 50),
        ["water-mill-race"] = new(new Vector3(5f, 3.5f, 13f), new Vector3(5f, 0.9f, 0), 55),
        ["water-clock"] = new(new Vector3(0, 1.4f, 5.0f), new Vector3(-0.2f, 1.0f, 0), 50),
        ["castellum-aquae"] = new(new Vector3(10.5f, 5.0f, 22f), new Vector3(10.5f, 2.6f, 0), 55),
        ["heron-temple-doors"] = new(new Vector3(1.2f, 2.6f, 5.5f), new Vector3(1.0f, 1.0f, -0.4f), 55),
        ["bearing-friction"] = new(new Vector3(0.1f, 1.1f, 4.0f), new Vector3(0.1f, 0.85f, 0), 45),
        ["axle-friction"] = new(new Vector3(6.5f, 3.2f, 17f), new Vector3(6.5f, 1.2f, 0), 55),
        ["heading-rig"] = new(new Vector3(-1f, 3.5f, 9f), new Vector3(-1f, 0.5f, 0), 60),
        ["water-wheels"] = new(new Vector3(4.5f, 5f, 17f), new Vector3(5.5f, 1.3f, 0), 55),
        ["fire-and-water"] = new(new Vector3(0.9f, 2.4f, 6.0f), new Vector3(0.9f, 0.4f, 0), 50),
        ["sluice-demo"] = new(new Vector3(4.5f, 3.2f, 9.5f), new Vector3(4.2f, 0.8f, -0.5f), 55),
        ["dam-break"] = new(new Vector3(18f, 7f, 15f), new Vector3(22f, 1f, 0), 55),
        ["floats"] = new(new Vector3(1f, 1.6f, 3.2f), new Vector3(1f, 0.3f, 0), 50),
        ["hanging-chain"] = new(new Vector3(0, 1.6f, 3.2f), new Vector3(0, 1.5f, 0), 50),
        ["placer-sluice"] = new(new Vector3(2.5f, 1.6f, 2.6f), new Vector3(2.4f, 0.5f, 0), 50),
        ["constant-head"] = new(new Vector3(2.2f, 2.2f, 2.8f), new Vector3(0.8f, 0.8f, -0.7f), 50),
        ["tank-leaks"] = new(new Vector3(3.6f, 1.8f, 10.5f), new Vector3(3.6f, 0.6f, 0), 50),
        ["boiler-safety"] = new(new Vector3(0.75f, 1.5f, 3.0f), new Vector3(0.75f, 0.4f, 0), 50),
        ["suction-limit"] = new(new Vector3(3.6f, 6.9f, 16f), new Vector3(3.6f, 6.7f, 0), 50),
        ["bellows-forge"] = new(new Vector3(1.2f, 1.1f, 2.2f), new Vector3(0, 0.5f, 0), 45),
        ["windmills"] = new(new Vector3(0, 9f, 46f), new Vector3(0, 8f, 0), 50),
        ["capstans"] = new(new Vector3(0, 2.6f, 7.5f), new Vector3(0, 1.6f, 0), 50),
        ["steam-engines-mars"] = new(new Vector3(1.2f, 2.4f, 8.5f), new Vector3(1.2f, 0.9f, 0), 55),
        ["mars-stirling"] = new(new Vector3(0, 9f, 22f), new Vector3(0, 0.5f, 0), 55),
        ["battering-rams"] = new(new Vector3(0.5f, 2.2f, 7.0f), new Vector3(0.5f, 1.1f, 0), 55),
        ["gristmill"] = new(new Vector3(1.2f, 3.4f, 7.5f), new Vector3(1.2f, 0.5f, 0), 50),
        ["rail-wagons"] = new(new Vector3(-4.0f, 2.5f, 3.5f), new Vector3(0.8f, 0.3f, -3.0f), 55),
        ["carts"] = new(new Vector3(-4.5f, 3.0f, 7.0f), new Vector3(0, 0.3f, 2.5f), 55),
        ["crank-slider"] = new(new Vector3(1.3f, 1.6f, 2.6f), new Vector3(0, 1.15f, 0), 50),
        ["universal-joint"] = new(new Vector3(0.6f, 1.8f, 2.4f), new Vector3(0, 1.25f, 0), 50),
        ["drop-test"] = new(new Vector3(0.5f, 1.4f, 4.5f), new Vector3(0, 0.6f, 0), 50),
        ["rope-over-bars"] = new(new Vector3(5.5f, 4.0f, 9.0f), new Vector3(0, 2.6f, 0), 50),
        ["bar-crane"] = new(new Vector3(7.0f, 5.5f, 19.0f), new Vector3(7.0f, 3.5f, 0), 55),
        ["winter-night"] = new(new Vector3(0, 2.2f, 5.5f), new Vector3(0, 0.4f, 0), 50),
        ["earth-machines-on-mars"] = new(new Vector3(1.5f, 5f, 17f), new Vector3(1.5f, 3f, 0), 50),
        ["two-modules"] = new(new Vector3(0, 6f, 15f), new Vector3(0, 1.5f, 0), 50),
        ["stove-rooms"] = new(new Vector3(0, 6f, 15f), new Vector3(0, 1.2f, 0), 50),
        ["airlock"] = new(new Vector3(-3f, 5f, 11f), new Vector3(-3f, 1.2f, 0), 50),
        ["mars-sols"] = new(new Vector3(4f, 3f, 5f), new Vector3(0, 0.8f, -1.2f), 50),
        ["solar-furnace"] = new(new Vector3(8f, 7f, 15f), new Vector3(8f, 1f, 0), 50),
        ["glass-rooms"] = new(new Vector3(0, 9f, 16f), new Vector3(0, 1.2f, 0), 50),
        ["rain-house"] = new(new Vector3(3f, 4f, 10f), new Vector3(0, 2.2f, 0), 50),
        ["greenhouse"] = new(new Vector3(-1f, 5f, 10f), new Vector3(-1.5f, 1f, 0), 50),
        ["heliostats"] = new(new Vector3(7.5f, 3.2f, 5.5f), new Vector3(0, 0.6f, 0), 50),
    };
    private static readonly (double Scale, string Label)[] Speeds =
        [(0.1, "0.1×"), (0.25, "0.25×"), (1, "1×"), (5, "5×"), (10, "10×"), (20, "20×")];
    private static readonly Dictionary<string, double> DefaultSpeeds = new()
    {
        ["aeolipile"] = 5,
        ["winter-night"] = 20,   // an hour of frost is three minutes
        ["heliostats"] = 20,     // a day in a little over an hour
        // No special default here on purpose: after retuning the vessels
        // (see herons-fountain.rkt), the whole "runs at strength, then
        // stops abruptly once the receiver fills" story completes in
        // ~30 real seconds at a plain 1x — measured over the live link,
        // including the dramatic part (36.6cm to 1.8cm in the space of
        // 5 seconds). A fast default would rush past that cliff — at
        // 10x it happens in half a second, too quick to see happen.
        // 1x is the speed that actually shows it; the speed row is still
        // there for anyone who wants to skip ahead or slow it down.
    };
    private static readonly (int Width, int Height, string Label)[] WindowSizes =
        [(1152, 720, "Small"), (1600, 1000, "Medium"), (1920, 1200, "Large")];

    private static readonly Dictionary<string, string> DisplayNames = new()
    {
        ["aeolipile"] = "Aeolipile",
        ["herons-fountain"] = "Heron's Fountain",
        ["material-samples"] = "Material Samples",
        ["branca-steam-wheel"] = "Branca's Steam Wheel",
        ["solar-steam-wheel"] = "Solar Steam Wheel",
        ["kitchen-smoke-jack"] = "Kitchen Smoke Jack",
        ["pendulum-demo"] = "Pendulum",
        ["lever-demo"] = "Lever / See-Saw",
        ["inclined-plane-demo"] = "Inclined Plane",
        ["newtons-cradle"] = "Newton's Cradle",
        ["trebuchet"] = "Trebuchet",
        ["torsion-catapult"] = "Torsion Catapult (Onager)",
        ["antikythera-lunar-train"] = "Antikythera Lunar Train",
        ["archimedes-screw"] = "Archimedes' Screw",
        ["component-gallery"] = "Component Gallery",
        ["hama-noria"] = "Noria of Hama",
        ["newcomen-engine"] = "Newcomen Engine",
        ["roman-crane"] = "Roman Crane",
        ["vitruvian-catapulta"] = "Vitruvian Catapulta",
        ["newcomen-hearth"] = "Newcomen Engine (Coal Hearth)",
        ["hearth-engine"] = "Hearth-Fired Aeolipile",
        ["post-and-lintel-crane"] = "Post-and-Lintel Crane",
        ["water-mill-race"] = "Water Mill Race",
        ["water-clock"] = "Water Clock (Clepsydra)",
        ["castellum-aquae"] = "Castellum Aquae",
        ["sluice-demo"] = "Sluice Gate",
        ["dam-break"] = "Dam Break",
        ["floats"] = "Floats (Archimedes)",
        ["hanging-chain"] = "Hanging Chain (catenary)",
        ["placer-sluice"] = "Sluice Box (placer gold)",
        ["constant-head"] = "Float Valve (Constant Head)",
        ["tank-leaks"] = "Tank Leaks (Torricelli)",
        ["spill-tank"] = "Broken Water Butt (spills onto the ground)",
        ["cistern-drain"] = "Drain into a Cistern",
        ["boiler-safety"] = "Safety Valve and Burst Boiler",
        ["suction-limit"] = "Lift Pumps and the Suction Limit",
        ["fire-and-water"] = "Fire and Water",
        ["water-wheels"] = "Water Wheels",
        ["heron-temple-doors"] = "Heron's Temple Doors",
        ["bearing-friction"] = "Bearing Friction",
        ["axle-friction"] = "Axle and Hinge Friction",
        ["heading-rig"] = "Parts at a Heading",
        ["bellows-forge"] = "Bellows and Forced Draught",
        ["windmills"] = "Windmills and the Betz Limit",
        ["capstans"] = "Capstans: Rope Friction on a Post",
        ["rope-over-bars"] = "Rope over a Fixed Bar",
        ["drop-test"] = "Drop Test: Impacts and Restitution",
        ["crank-slider"] = "Crank and Connecting Rod",
        ["carts"] = "Carts: Why the Wheel",
        ["rail-wagons"] = "Rail Wagons: Why Rails",
        ["gristmill"] = "Gristmill: Power into Flour",
        ["battering-rams"] = "Battering Rams: When a Post Breaks",
        ["mars-stirling"] = "Hot-Air Engines on Mars",
        ["steam-engines-mars"] = "High-Pressure Steam on Mars",
        ["universal-joint"] = "Universal Joint",
        ["bar-crane"] = "Roman Crane without a Pulley",
        ["winter-night"] = "A Winter Night (Ambient Temperature)",
        ["earth-machines-on-mars"] = "Earth's Machines on Mars (Planet Settings)",
        ["two-modules"] = "Two Modules on Mars (Enclosures)",
        ["stove-rooms"] = "Stoves and Their Air (Oxygen-Limited Fire)",
        ["airlock"] = "An Airlock on Mars (Doors and Air Pumps)",
        ["mars-sols"] = "Sols and a Dust Storm at Meridiani (Mars Time and Weather)",
        ["solar-furnace"] = "A Solar Furnace (Glass from Mars Sand)",
        ["glass-rooms"] = "Glass Walls on Mars (Light and Pressure)",
        ["rain-house"] = "A Rain-House on Mars (Evaporation and Condensation)",
        ["greenhouse"] = "A Greenhouse on Mars (Trees, Oxygen and Wood)",
        ["heliostats"] = "Heliostats: Sunlight and Mirrors",
    };

    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["aeolipile"] = "Hero of Alexandria's steam turbine (c. 50 AD). A fire boils water in the sealed kettle; escaping steam jets from two bent nozzles on the sphere, spinning it by reaction — the same principle as a rocket or a lawn sprinkler.",
        ["herons-fountain"] = "Hero of Alexandria's fountain (c. 50 AD). Water draining from the basin into the sealed receiver compresses the air trapped inside. That compressed air pushes water from the supply vessel back up through a nozzle — higher than its own source — with no pump.",
        ["branca-steam-wheel"] = "Giovanni Branca's steam wheel (1629): a fire boils a pot of water, and the steam jet from its spout blows on the flat paddles of a wheel. The push is the jet's flow times how much faster the steam moves than the paddles. Against a small load, the paddles' own drag on the air and the bearing, it settles near 290 rpm, giving 0.09 W from a 1.5 kW fire: why Branca's wheel never became an engine.",
        ["kitchen-smoke-jack"] = "A smoke jack, as Leonardo sketched and 17th-century kitchens used: a cooking fire heats a cauldron, and what the cauldron doesn't take rises up the chimney through a wheel of pitched vanes that turns the roasting spit. The warm air rises at about 1.2 m/s, turning the vanes at about 39 rpm: slow and weak, but steady and free.",
        ["solar-steam-wheel"] = "Branca's wheel with no fire: four mirrors throw midsummer noon sunlight onto the pot, about 2.5 kW, more than the fire gives the other wheel. The pot boils in minutes and the wheel runs at about 520 rpm.",
        ["material-samples"] = "Four identical cubes of different materials, dropped from different heights. Shows how density and friction differ by material — nothing here is scripted, it's real physics reading real material properties.",
        ["pendulum-demo"] = "A classic compound pendulum, released from 40° and left to swing. Demonstrates the exchange between potential energy (height) and kinetic energy (speed) that every mechanical clock and metronome relies on.",
        ["lever-demo"] = "A see-saw: two different weights on either end of a beam pivoted at its centre. The heavier side sinks until the beam's own mechanical stop holds it — a direct demonstration of torque and leverage.",
        ["inclined-plane-demo"] = "Galileo's classic experiment: identical-size blocks of different materials released on the same slope. Whether each one slides — and how far — depends only on its material's friction against stone.",
        ["newtons-cradle"] = "Five identical pendulums hung in a touching row. Pull one end back and release it: momentum and energy transfer through the row via collision, animating the far ball instead.",
        ["trebuchet"] = "A counterweight trebuchet (medieval, but Archimedes' lever taken as far as it goes). A 73 kg counterweight hangs on a chain from the short arm; the long arm carries a sling, with the stone lying on the ground behind. The falling weight whips the arm over, the sling whips the stone round faster still, and it flies ~16 m forward. Only rope, hinge and gravity — nothing scripts the flight.",
        ["torsion-catapult"] = "The onager, a late-Roman one-armed stone-thrower (Ammianus Marcellinus, 4th c. AD). Its arm stands in a horizontal skein of twisted sinew — a torsion spring — winched down level. Loosed, the skein flings the arm up against a padded crossbeam near upright, and the stone, in a sling at the tip, whips round and flies ~19 m before it lands, and rolls on a little. The skein's stiffness is our estimate; Ammianus gives none.",
        ["antikythera-lunar-train"] = "Six bronze gears from the Antikythera mechanism (c. 100 BC), with their real tooth counts: 64→38, 48→24, 127→32. One turn of the first is a year; the last then turns 254/19 times — the Moon's circuits of the sky in that year. Triangular teeth, as the originals have. Here a crank turns b2 at 2 rpm and the train does the rest: each mesh reverses the sense and scales the speed by the tooth ratio, so e2 runs at 26.7 rpm.",
        ["component-gallery"] = "Five benches, one per family of parts: a pipe settling two tanks (one on a post pier) to equal height; a trilithon of posts, lintel and load; a hearth burning 50 g of wood under a boiler that spins a rotor; a spring feeding a weir, a winding channel with waypoints, a pond and a tailrace; and a pendulum, a lever on a fulcrum post and a ramp.",
        ["archimedes-screw"] = "A water screw built only from Vitruvius's rules (De Architectura X.6) — core a sixteenth of its length, eight helical blades, whole an eighth of its length across, set on a 3-4-5 slope. A man treading it turns it at 12 rpm; its lower end stands in a pool, and each turn carries the water in each dip of its channels one pitch higher, 23 L a turn, pouring into the trough at the top. As the pool drops below the intake the scoops come up part-full and the flow falls off.",
        ["newcomen-engine"] = "Thomas Newcomen's atmospheric engine (1712), after the one at Dudley Castle — the first practical piston engine, built to pump water out of mines. Steam fills the cylinder (white) and the pump rod's weight draws the piston up; at the top a jet of cold water condenses the steam (blue), and the atmosphere — 18 kN on the 53 cm piston — drives it down, rocking the beam and lifting ~47 L of water 48 m up the mine shaft each stroke. It's the air that does the work. About 5% of the fire's heat becomes lifted water here; real engines managed under 1%, because each cold jet also chilled the cylinder walls — the waste Watt's separate condenser later cured.",
        ["hama-noria"] = "A noria, like those that have watered the fields of Hama on the Orontes since Roman times: the river turns it and it lifts the river. The current drags on the paddles dipping into it; buckets in the rim fill at the bottom and tip out at the top into an aqueduct that carries the water off to the fields. The river comes in from upstream (right) at 500 L/s, fills a weir pool, and spills into a stone race that runs 1.5 m/s — worked out from its slope and roughness, not typed in — into the wheel's basin, then leaves over the tailrace. Nothing sets the wheel's speed: it settles where the race's push balances the weight of water it's lifting, about 1.3 rpm. Choke the river and the race slows, the basin drops, and the wheel stalls, as norias do in a dry summer.",
        ["roman-crane"] = "A Roman building crane (Vitruvius X.2): two men walking in a 4.5 m treadwheel turn a 25 cm drum on the same axle, winding a rope over the jib's pulley to lift 580 kg of granite. The wheel and axle is the lever that makes it possible: the men push at 2.25 m, the stone pulls at 0.25 m, so they need only a ninth of its weight. Their ~1,550 N·m just beats the stone's ~1,430 N·m — one man alone couldn't lift it.",
        ["vitruvian-catapulta"] = "A two-armed bolt-shooter proportioned entirely from Vitruvius's table (X.10) — every part a multiple of the spring hole, the hole a ninth of the bolt: here a 69 cm bolt, 7.7 cm hole, 54 cm arms. Each arm turns in an upright spring of twisted sinew; drawn back, the springs twist further. Loosed, they throw the arms forward, the bowstring drives the bolt down the channel, the arms hit their stops and the bolt flies on at ~25 m/s, landing ~11 m away shot level. Vitruvius gives no spring stiffness; 300 N·m per radian is our estimate.",
        ["newcomen-hearth"] = "Newcomen's atmospheric engine, as at Dudley Castle, but with an honest fire: 2 kg of coal (24 MJ/kg, 25% reaching the boiler) burns at 1 MW and goes out after 48 s, having put 12 MJ into 2 000 kg of water. The engine keeps pumping on the heat stored in the boiler, lifting about 48 L a stroke (the pump bore times its stroke) into the cistern.",
        ["hearth-engine"] = "Hero's steam reaction engine with the fire made real. 60 g of wood (15 MJ/kg) burns for 225 s at 4 kW, half of it reaching the kettle: 2 kW, enough to hold the kettle at 105.9 C while steam leaving the two nozzles spins the ball to 2 740 rpm, where air drag balances the thrust. When the wood is gone the kettle cools and the ball slows.",
        ["post-and-lintel-crane"] = "A Bronze Age counterweight lift, a shaduf on a trilithon: two posts and a lintel carry the axle of a 3 m oak beam. A 116 kg granite counterweight on the short arm (63.7 kg.m) outweighs the 21.6 kg load on the long arm plus the beam, so the beam swings to its 15 degree stop and raises the load about 0.4 m. The balance comes out of the densities and Jolt's contacts alone.",
        ["water-mill-race"] = "A spring fills a header tank on a pier, spills over its weir (6.25 cm head) into a stone brook that winds through three bends and runs 2 cm deep at 1.3 m/s, drops into a millpond, and leaves over a tail weir. The brook's current on its paddles turns a small noria standing on its own posts. The wheel settles at 3 rpm, where the paddles' drag equals the weight of water it lifts, and raises 4.6 L/s to a flume on a pier.",
        ["water-clock"] = "A Ctesibian clepsydra. A spring keeps the reservoir brimming over its lip, so the pressure driving the outlet never changes; the outlet fills a tall receiver from the top, so the far end holds steady too. The receiver level therefore climbs at a constant 2.97 mm/s (0.1187 L/s over 0.04 m2): the water level is the time. There is no float here; the level is read off the water itself.",
        ["castellum-aquae"] = "A Roman castellum aquae, as Vitruvius describes: an aqueduct on an arcade of piers ends in a distribution tank with three pipes at three heights, fountains lowest, baths above, houses highest. The 6.7 L/s supply is less than the 9 L/s all three could carry, so the level falls past the houses' pipe and settles at 72 cm, where the fountains (3.7 L/s) and baths (3.0 L/s) take everything and the houses go dry.",
        ["bearing-friction"] = "Three identical 1 m iron pendulums (18.9 kg, I = 17.4 kg m2), each on a 3 cm pin, let go from 15 degrees. The left pin is frictionless: it swings to 15 degrees every time, a 1.99 s swing. The middle pin turns in grease, which drags in proportion to speed (c = 0.6 N m s/rad): the swings die away inside the envelope 15 x exp(-c t / 2I), halving every 40 s. The right pin is dry iron on iron (mu 0.4): friction resists with mu m g r = 1.11 N m whatever the speed, so every swing loses the same 0.73 degrees - a straight line, not a curve - and after 20 of them gravity can't pull it past the pin's grip and it stops, 0.25 degrees off plumb. All 5.9 J of its swing become heat in the pin, and the pin wears by Archard's law, V = K N s = 1e-4 x 186 N x 8 cm = 0.0015 mm3. The red dot marks where each last turned.",
        ["axle-friction"] = "The pendulum's bearing on the wheels and hinges the engine turns. Three iron flywheels (59.9 kg, I = 1.89 kg m2) are let go at 60 rpm on 2 cm pins: the left one is frictionless and spins for ever; the middle one is greased (c = 0.5 N m s/rad) and dies away as exp(-c t / I), losing 63% of its speed in 3.8 s; the right one is dry iron (mu 0.4), friction mu m g r = 4.7 N m at any speed, so its speed falls in a straight line and it stops dead at 2.53 s, all 37.3 J of its spin heat in the pin. Beyond them, two 1 m beams hung 25 cm from one end swing from 15 degrees: the free one for ever, the dry one losing about 1.85 degrees every half swing until it stops within 0.9 degrees of plumb. On the right, the overshot wheel of the water-wheels machine runs on a 3 cm dry axle: the axle takes 23.5 N m on top of the millstone's 300, so it settles at 1.364 rad/s instead of 1.472, and 7% of the water's power heats the axle. Labels show the heat and the pin's wear.",
        ["heading-rig"] = "Parts that stand at any heading, not only along the axes: an iron ball rolls down a 35 degree slope (at (5/7) g sin 35 = 4.0 m/s2), a second slope turned 90 degrees on its own sends its ball the other way, a pendulum swings in the plane its hinge leaves it (a second, hung at heading 90, along z) and a flywheel on an axle along x runs down at 0.2 per second. World headings.world places this rig turned 0, 37 and 90 degrees; each does exactly what the unturned one does, in its turned frame.",
        ["heron-temple-doors"] = "Hero of Alexandria's temple doors that open by themselves (Pneumatica I.38). A fire on a hollow bronze altar heats the air sealed inside; that air, shared with a closed globe half full of water, rises in pressure as it warms (P = m R T / V) and drives the water through a siphon into a hanging bucket. Once the bucket outweighs its counterweight it sinks, and its rope, wound round the doors' spindles, swings them open. The altar settles 30 K warm with a time constant of 11 minutes, so the doors open after about 13 minutes; when the fire burns out, the air cools, the water siphons back, and the counterweight shuts them. Try it at 20x.",
        ["water-wheels"] = "Two water wheels, each grinding against a millstone. Left, overshot: a 20 L/s race pours onto the top of a 3 m wheel whose buckets carry the water down the far side until they tip it out 120 degrees round. The water's weight gives rho g Q r (1 - cos 120) = 441 W at any speed, so against a 300 N m millstone it settles at 14 rpm - 73% of what the water loses falling from the race's lip, inside the 63-78% Smeaton measured. Load it past 562 N m and the brimming buckets can't turn it. Right, undershot: a 150 L/s race pushes on paddles dipping into it; at best it takes 8/27 of the stream's kinetic energy, the old undershot ceiling of about 30%.",
        ["fire-and-water"] = "Water meets fire. Left: a cistern spills 20 g/s onto a 20 kW wood fire. Boiling a kilogram of 20 C water away takes 2.59 MJ, so the fire can boil off only 7.7 g/s; the rest soaks in, and when the soaked water outweighs the fuel left (at about 150 s) the fire drowns. Until then all its heat goes into the water, none into the pot. Right: a copper of 4 kg at 90 C takes 2 L of 20 C feed water and mixes to 66.7 C; its 2 kW stove needs about 7 minutes to bring it back to the boil.",
        ["boiler-safety"] = "Denis Papin's safety valve (1679). Two bronze boilers, each 10 kg of 20 C water rated to burst at 200 kPa, each over a wood fire giving it 10 kW. Sealed, both warm along T = 20 + 5000 (1 - exp(-t / 20930 s)). The left one's weighted lever lifts at 100 kPa (425 s) and its 8 mm valve vents what the fire brings, (Q - h (T - 20)) / L = 4.34 g/s of steam, holding the boiler at 103.4 kPa and 121.1 C for as long as the water lasts. The right one has no valve: it climbs on to 200 kPa and bursts at 482 s, 0.63 kg of its water flashing to steam at once. Tie the valve down (guard.lift 300) and the left one bursts about 51 s later. Try it at 20x.",
        ["suction-limit"] = "Why a lift pump can't raise water more than about 10 m (Galileo's well pump, Berti's tube, Torricelli, 1638-44). The atmosphere pushes the water up after the bucket; once the pressure under the bucket falls to water's vapour pressure the column breaks, at (101325 - 2330 Pa) / (rho g) = 10.09 m over the well. Three pumps, each a 15 cm bucket over a 50 cm stroke at 20 strokes a minute. Left, the bucket 6 m over its water: 0.8 x the swept 8.8 L comes out every stroke, 7.07 L or 2.36 L/s, the rod pulling 1127 N and the water gaining 80% of the work. Middle, 11 m over: the water stands at 10.09 m in the pipe, the rod pulls only 1749 N, A (P_atm - P_v), though the drive could give 10 kN, and nothing comes out. Right, 8 m over a narrow well: it draws the well down 28 mm a stroke, and after 56 strokes the column starts to break partway up; it lifts less and less until the bucket stands 10.09 m over the water, and stops.",
        ["spill-tank"] = "A 1000 L water butt with a stave stove in: a 100 cm2 hole 2 cm up. On its own it leaks onto the floor and the water is gone; in the spill world (a walled slope of sand) the jet lands on the ground, runs downhill and pools at the low wall, and the butt and the ground always hold the 1000 L between them: 369.6 L left in the butt at 30 s, down to the hole at 74.5 s.",
        ["cistern-drain"] = "A 10 cm grate over a pipe into a cistern. On its own there is nothing for it to drain; in the sump world it sits at the bottom of a hollow fed by a 2 L/s spring, and once the hollow is steady the cistern fills at the spring's 2 L/s with the water standing 2.05 cm over the grate, Q = 1.705 P h^1.5. Fill the cistern and the drain backs up.",
        ["tank-leaks"] = "Torricelli's law. Four oak barrels, 0.25 m2 each; three are 80 cm full with a 5 cm2 hole in the wall, Q = 0.6 a sqrt(2 g h), h the water above the hole. sqrt(h - hole) falls at a steady 2.66 mm^1/2 per second, so the level runs down to the hole and stops: a hole 10 cm up takes 315 s (1.11 L/s to start with, at 70 cm of head) and leaves 10 cm; the same hole 40 cm up takes 238 s (0.84 L/s) and leaves 40 cm. Lower holes leak faster and further, and throw the jet farther. The third barrel leaks into a catch tank, litre for litre, until a thumb stops the hole at 100 s. The last has no hole, only a seep of 0.05 L/s off its surface: a steady 0.2 mm/s at any level.",
        ["constant-head"] = "Ctesibius' float valve. Two identical cisterns, 40 cm full, each drain through a tap raised 1 cm into a receiver. The front one is fed by a 2 L/s aqueduct through a mouth that a bronze float closes with a conical plug: seated at 40 cm, wide open 2 cm below. It settles at 39.17 cm, where the valve lets in exactly the 0.83 L/s the tap draws, so its receiver rises a steady 1.65 mm/s, a clock. Open the tap to 2 cm (tap.opening 0.04) and the draw doubles, yet the head drops only 0.8 cm, to 38.38 cm. The back cistern, with no valve and no feed, sinks to 22.5 cm in a minute and 10 cm in two, and its receiver slows as it goes.",
        ["dam-break"] = "A millpond held by a shut sluice while a 250 L/s stream fills it. At 55 cm deep (20 s) a trigger draws the gate right up and the pent water runs down a dry 40 m race into the low pond. The race holds water along its length, 80 cells solved by the shallow-water equations: watch the thin fast front go down and the reach fill behind it. It reaches the low pond at 40 s, between the fastest wave's 33.8 s and a kinematic shock's 52.5 s. Pond, race and low pond always add up to what there was plus the stream's.",
        ["sluice-demo"] = "A sluice gate on a mill race. A 20 L/s spring fills a head pool; a wooden gate 1 m tall, raised 5 cm, lets the water out under its lower edge as a jet, Q = 0.6 x slot area x sqrt(2 g h). All the spring must pass the slot, so the pool rises until the water stands 25.2 cm over the slot's middle. Below, the race runs into a reach that drains over a floor-level outfall into a pond. Shut the gate (gate.opening 0) and the race runs dry at once, the reach drains away over two minutes, and the pool backs up and spills over its waste weir.",
        ["heliostats"] = "Two bronze boilers of a litre of water, each heated by a 0.5 m2 mirror turned through the day to keep throwing the sun onto it - a heliostat - at Alexandria on midsummer's day, from noon. The sun stands 82 degrees up in the south and its direct beam through clear air is 951 W/m2. A mirror sending that light to its boiler must face halfway between the two, so it shows only cos(theta/2) of itself to the sun: the mirror north of its boiler, looking back towards the sun, keeps 0.83 and throws 336 W; the one to the south, standing between boiler and sun, keeps 0.75 and throws 303 W. Watch the day go by at 20x: the beams weaken as the sun lowers, and die at sunset. Try scene.day 355: at midwinter noon the sun is only 35 degrees up, and the north mirror keeps 0.98 of its area while the south one keeps 0.42.",
        ["greenhouse"] = "A glazed 30 m3 greenhouse of fast trees on Mars, 13.5 kPa with CO2 pumped in, held warm by a heater. The trees keep 0.5% of the light through the glass as wood (18 MJ/kg): under the noon sun 1,245 W grows 3.2e-7 kg/s, and each kg of wood (cellulose) gives the air 1.18 kg of O2, so over 30 sols the greenhouse's 1.1 kg of oxygen nearly doubles. Harvest (trees.harvest 10) onto the stove and burn it: it takes back exactly the O2 growing gave. Beside it an ice melter (466 kJ a kg of -63 C ice, 7.7 kg/h at 1 kW) and an electrolyser in its own cell, 70 g of O2 an hour for 500 W: 30 MJ of electricity a kg of wood burned with it, more than the wood's heat.",
        ["rain-house"] = "A sealed room with a warm pond and a roof chilled by the -63 C outside. The pond's 1 kW heater evaporates its water; the vapour condenses on the roof, whose inside stands at the air's dew point, so the heat leaving through it, 40 W/K x (T_dew + 63), is the latent heat of the rain: 1 kW makes 1.6 kg an hour, the dew point settling at -38 C. The rain runs into a gutter 4.6 m up: 39 kg a sol lifted with no pump, 671 J stored against 89 MJ spent. A slow pump whose worth is the water. Best at 20x.",
        ["glass-rooms"] = "Four rooms of 13.5 kPa air at Meridiani noon. The habitat's membranes are opaque: the membrane room gets no sun and cools to -63 C. Glazed with 36 panes of silica glass 30 cm square in its roof, a room takes 0.9 x 427 W/m2 x 3.24 m2 = 1,245 W and settles at -63 + 1245/20 = -0.7 C. Basalt glass lets through 5%: -59.5 C. A pane's bending stress is 0.29 q (a/t)^2 (Roark): 7.5 mm panes crack only past 15.1 kPa, but 5 mm ones at 6.7 kPa, so the thin roof cracks at once and its room's air is gone. Try glazed-roof.strength 3 (MPa).",
        ["solar-furnace"] = "Glass from sand and sunlight, at noon on Mars (433 W/m2). A target heats until it re-radiates what it gets, sigma (T^4 - T_air^4) = C I. Left, ten flat heliostats on one crucible of basalt: a flat mirror's image is as big as itself, so they reach at best C = 10, and here about 5.6 once each one's angle is counted; it stops near 190 C and never melts basalt (1,200 C). Middle, a 12 m2 burning mirror squeezing its light into 50 cm2 (C near 2,000): it would stop at 1,713 C and melts 10 kg of basalt in 82 minutes into dark glass. Right, a 16 m2 burning mirror melts 2 kg of silica sand (1,700 C) in 15 minutes into clear glass. Try scene.clock-rate 0 to hold the sun at noon. Best at 20x.",
        ["mars-sols"] = "Three sols on Opportunity's plain, from noon on sol 1. A sol is 88,775 s, 24 local hours of 3,699 s. The air follows the day, -80 C before dawn to -20 C at 15:00. All of sol 2 a dust storm blows, optical depth 10.8 as in Opportunity's last storm: the sun's beam through it falls a further e^(-10.5 x air mass), about 1/40,000 at noon, and the sky goes brown. Dust settles on the heliostat, which after the storm reflects e^(-0.5) = 61% of what it did; clean it with mirror.dust 0. The relay orbiter passes at 03:00 and 15:00. Try scene.clock-rate 100 to watch the sols go by.",
        ["airlock"] = "A 60 m3 habitat of 50 kPa air, an 8 m3 chamber and two doors onto Mars. Work it: the air pump draws the chamber back into the habitat (pump running), 50 L/s, down to 5 kPa in 8/0.05 x ln 10 = 368 s, spending 289 kJ as an ideal compressor; then open the 5 cm2 bleed valve (bleed.open 1) and the chamber falls to Mars's 610 Pa as e^(-t/134 s); open the outer door (outer.open 1). Each cycle loses only what was left in the chamber, 8 m3 x (5000 - 610) Pa of air = 0.42 kg, against 4.7 kg vented straight from 50 kPa.",
        ["stove-rooms"] = "One 2 kW charcoal stove, three times. A fire breathes its room's air: charcoal takes 2.67 kg of oxygen a kilogram (C + O2 -> CO2) and goes out below 15% oxygen. Left, a sealed 30 m3 room: 74.2 mol of oxygen above the limit at 5.74 mmol/s, so it goes out after 12,924 s (3.6 h) having burned 0.89 kg, the air 6% CO2; meanwhile it holds the room at 60 C and 115 kPa. Middle, the same room with 5 L/s of outside air blown in and a vent: its oxygen settles at 18.2% and it burns on. Right, outdoors: it burns to the end. Try sealed.supply 5, or stove.oxygen-limit 10. Best at 20x.",
        ["two-modules"] = "Two inflated modules on Mars, each holding 50 kPa of 21% oxygen at 20 C against 610 Pa of CO2 at -63 C outside. Left, sealed: walls losing 20 W/K and a 1.8 kW heater, so it settles at -63 + 1800/20 = 27 C (time constant C/UA = 1283 s); its lift pump works because the air inside holds a column (50000 - 2339)/(1000 x 3.71) = 12.85 m high; a nitrogen locker inside it leaks into it, not onto Mars. Right, punctured by a 1 cm2 hole: its air rushes out at the speed of sound and the pressure falls as e^(-t/2513 s), 24.4 kPa after half an hour, 11.9 after an hour, the dial sinking, until at about 1 kPa the membrane sags. Try punctured.leak 0 to patch it, or sealed.heater 0. Best at 20x.",
        ["earth-machines-on-mars"] = "The same parts and formulas, with Mars's numbers: g = 3.71 m/s2, 610 Pa of air that is 95% CO2, -63 C. The pendulums swing sqrt(9.81 / 3.71) = 1.63 times as slowly (the 1 m one on its bearing in 3.245 s). The lift pump holds no water: the air's 610 Pa is barely more than water's vapour pressure at 0 C (605.6 Pa), so its reach is (610 - 605.6) / (1000 x 3.71) = 1.2 mm. The windmill's wind carries 1/2 rho A v^3 = 596 W through 5 m sails at 10 m/s, 1/79 of Earth's 47 kW, because Mars's air is 0.0152 kg/m3. The kettle boils at 0.1 C, a second after the fire is lit. Try scene.gravity 9.81, or scene.pressure 50 (kPa).",
        ["winter-night"] = "A night at -10 C. A copper of 5 kg of water, taken off the fire at 90 C, cools towards the air by Newton's law: 2 W for every kelvin it is warmer, against 5 x 4186 J/K, so T = -10 + 100 exp(-t / 10465 s) - 60.9 C after an hour, 40.3 C after two. The cistern beside it ices over: each new layer's latent heat has to leave up through the ice already there, so the ice thickens as the square root of time (Stefan, 1891) - 22.8 mm in an hour, twice that in four. The shallow basin's seep evaporates nothing under ice. Try scene.ambient 20, or 30 for a summer's day. Best at 20x.",
        ["steam-engines-mars"] = "Two small Trevithick engines, alike in every part: a double-acting 5 cm cylinder fed from a boiler at 150 C (473 kPa), working a flywheel through a crank. The boiler's steam pushes the piston directly, (P_boiler - P_air) x area the whole stroke, so each stroke does (P_boiler - P_air) A S. The left one exhausts into Mars's 610 Pa: 186 J a stroke. The right one stands in a pressurised hut at 101 kPa, like an engine on Earth: 147 J. Newcomen's engine, pushed by the air, fails outright on Mars; this one works better there.",
        ["mars-stirling"] = "Newcomen's engine is pushed by the outside air, which Mars hardly has; a Stirling engine is driven by heat, with Mars's -63 C air as its cold side. Three engines, each under six flat heliostats (1.5 kW on a 1 m2 receiver at noon). The hot end settles where the mirrors' heat equals what it re-radiates plus what its heater passes the engine, K (T - Tc); the engine makes 35% of Carnot, f (1 - Tc/T), of that into work. Left, a small heater: hot, 98 C, but only 74 W. Middle, matched: 40 C and 111 W, the most these mirrors can give. Right, too big: it runs barely above the cold, -28 C, 64 W. Try scene.ambient 20: a warm cold side halves the middle one. Best at 20x.",
        ["battering-rams"] = "Three iron rams (132 kg balls on 2 m pendulums) swing from 30 degrees into posts struck 0.8 m up. A post bends like a spring, k = 3EI/h^3, and stops the blow with F = v sqrt(k m): the stress at its foot is F h c / I. The slim oak post (8 cm) takes 112 MPa against oak's 90 and snaps, the break taking 209 J of the ram's 325; the stout one (14 cm) takes 64 and holds. The limestone column is thick but stone is weak in tension (5 MPa): 74 MPa shatters it, and the break takes only 1.5 J.",
        ["gristmill"] = "Three pairs of 48-inch millstones, each runner turned at 120 rpm from below and set by its miller to resist with 267 N.m: 4.5 horsepower, 3,356 W. Freshly dressed French burr stones grind 54 kg of flour per kWh - 181 kg an hour, the millwrights' 400 lb; dull ones need twice the power for the same, so on the same 4.5 hp the middle pair makes only 80 kg an hour. The right-hand pair's wheel gives only 200 N.m, less than its stones' 267: it never gets them turning. Lighten its stones (weak.grind-torque 150) and it will. The flour heaps up beside each pair.",
        ["rail-wagons"] = "Two iron-wheeled wagons on the same 1 degree grade: one on a limestone road, one with flanged wheels on iron rails. The grade pulls tan 1 = 0.0175 of the weight along it. Rolling on a road takes 0.04 of the load, so the road wagon stays put; iron on iron rail takes only 0.002, so the rail wagon rolls away at g (sin t - C_rr cos t) M / (M + sum I/r^2) = 0.103 m/s2 - a third of the pull goes into turning its heavy flanged wheels.",
        ["carts"] = "Two handcarts and an oak sledge let go on a 10 degree limestone slope. The sledge stays put: oak grips limestone at mu 0.52, and the slope needs only 0.18 to hold it. The carts roll, at g (sin t - C_rr cos t) M / (M + sum I/r^2): the wheels' own turning takes part of the pull, and rolling resistance (C_rr 0.04, a stage coach on a dirt road) the rest. The oak cart on solid wheels runs down at 1.118 m/s2 and slows on the flat at 0.333; the pine cart on spoked wheels at 1.100 and 0.328 - light wheels, but a light bed, so a quarter of it turns.",
        ["crank-slider"] = "A crank disc turning at 60 rpm drives a piston through a 60 cm oak connecting rod: a ball joint at the crank pin, a hinge through the piston's wrist, and the piston's cylinder keeping it upright. With the crank turned theta from pin-down, the piston stands at y_c - r cos(theta) - sqrt(l^2 - r^2 sin^2(theta)) - not a plain sine: at a quarter turn the leaning rod holds it 19 mm below mid-stroke, so it spends longer in the top half of its travel than the bottom. Nothing computes that; the joints and the cylinder do.",
        ["universal-joint"] = "Two shafts meeting at 30 degrees, joined by a Cardan (Hooke's) joint: an iron cross, one arm hinged to each shaft's yoke. The driving shaft turns steadily at 30 rpm; the driven one speeds up and slows down twice a turn, between cos 30 = 0.866 and 1/cos 30 = 1.155 of it, w_out/w_in = cos b / (1 - sin^2 b sin^2 theta). The label over the cross shows the ratio as it swings. Two joints phased to cancel are why a car's drive shaft turns its wheels smoothly.",
        ["drop-test"] = "Four 20 cm cubes let fall 1.25 m onto stone: hardened steel, granite, oak and a bale of hemp. Every strike is recorded and flashes where it lands, with the energy it took: how fast the faces met, and 1/2 m v^2 (1 - e^2) of the block's energy gone to heat, sound and dents. They land together at 4.86 m/s (the engine's 0.1/s damping takes the rest of sqrt(2gh) = 4.95) and leave at e times that: steel 0.95 bounces back to a metre, granite 0.6 to 41 cm, oak 0.5 to 29 cm, hemp 0.1 just thuds. Granite loses the most, 163 J; steel, though three times heavier, only 72 J.",
        ["rope-over-bars"] = "Four fixed oak bars, each with a 173 kg granite block on one side of a hemp rope and a smaller one on the other. Where the rope drags over a bar, the tight side can carry up to e^(mu theta) times the slack side (the capstan equation): 4.44 over half a turn, 87 over a turn and a half. Left to right: half a turn with 21.6 kg (95.9 kg's worth, too little: the rope slides, the pair accelerate at 2.81 m/s2 and the bar glows), half a turn with 72.9 kg (holds), a turn and a half with 0.93 kg (81 kg's worth: slides at 3.57 m/s2), and with 2.7 kg (236 kg's worth: holds). The label over each bar gives its two tensions.",
        ["bar-crane"] = "The Roman crane with its pulley swapped for a fixed oak bar, as ropes ran over beam ends before sheaves. The rope turns 129 degrees over the bar, so lifting, the drum side must pull e^(mu theta) = 2.91 times the stone's 5.7 kN. Two walkers, who lift the stone over a pulley, now pull 6.2 kN at the drum and only 2.1 kN reaches the stone: it stays on the ground. Seven walkers lift it at the wheel's 3 rpm, the drum side carrying 16.7 kN, and the bar glows with the friction's heat.",
        ["capstans"] = "Three oak bollards, each with 200 kg hanging a metre below on a hemp rope, and a sailor holding the other end with 100 N. Friction where rope slides on a post takes off tension in proportion to the tension there, so round the post it falls off exponentially: the tight end carries e^(mu theta) times the slack one (the capstan equation, Euler 1762). Hemp on oak, mu = 0.474. Half a turn multiplies the pull by 4.4 - 444 N, not the load's 1962 N, so it runs out and falls. One turn multiplies it by 19.7 - just enough. Two turns by 388: 5 N would hold it. Try one-turn.hold 99, or haul in over half a turn with half-turn.hold 10000.",
        ["windmills"] = "Two post mills, sails 10 m from hub to tip. The wind carries 1/2 rho A v^3 through the disc they sweep; the sails take a share of it, Cp, that depends on how fast their tips run for the wind - most, 0.3 here, at 2.5 times its speed. No rotor can take more than 16/27 (Betz, 1919): the air behind it has to keep moving. Each miller sets the stones to hold the sails at that best speed: the left, in a 6 m/s breeze, turns at 14.3 rpm and grinds with 12.3 kW; the right, in a 9 m/s wind, at 21.5 rpm with 41.4 kW. Half again the wind, (1.5)^3 = 3.4 times the power. Set the right one's stones light (gale.load 8171) and it races to 33 rpm but takes only 0.21 of the wind.",
        ["bellows-forge"] = "Two forges, each 1 kg of wood at 5 kW. The left burns on its own draught: its steady burn already draws power / density x wood's 6:1 air-fuel ratio, 2 g/s of air, and takes 3000 s to burn the kilogram out. The right has a bellows forcing in 5 L/s more (air's density, 1.204 kg/m3, makes that 6.02 g/s) — 4.01x the air, so 4.01x the burn rate, out in 748 s. Both give up the same 15 MJ; the bellows only ever buys speed, not heat.",
    };

    private MaterialLibrary _materials = null!;
    private readonly SortedDictionary<string, string> _machineFiles = []; // name → res:// path
    private readonly Dictionary<string, MachineView> _byName = [];        // just the current one, for the live link
    private MachineView? _current;           // the focused machine: what the HUD, camera and controls act on

    // World mode (issue #74): many machines standing in one scene, all stepped
    // together; _current is whichever one was last clicked.
    private const string WorldsDir = "res://worlds";
    private readonly List<MachineView> _views = [];
    private readonly Dictionary<MachineView, string> _viewMachine = [];   // view → its machine's name, for the HUD
    private readonly Dictionary<MachineView, Aabb> _viewBounds = [];
    private WorldDef? _world;
    private Vector2 _pressAt;
    private double? _liveEditAfter = double.TryParse(OS.GetEnvironment("HEROIC_LIVE_EDIT_AFTER"), System.Globalization.CultureInfo.InvariantCulture, out double t) ? t : null;
    private string? _currentName;
    private bool _running;
    private double _timeScale = 1;
    private bool _showDetails;
    private double? _quitAfterSimSeconds; // for scripted recording: exact, unlike --quit-after under load
    private readonly bool _debugPhysics = OS.GetEnvironment("HEROIC_DEBUG_PHYSICS") == "1";
    private readonly bool _audit = OS.GetEnvironment("HEROIC_AUDIT") == "1"; // see MachineView.Audit.cs
    private double _debugTimer;

    private Camera3D _camera = null!;
    private Label _hudTitle = null!, _hudDescription = null!, _hudState = null!, _hudEnergy = null!, _hudSpeed = null!,
        _hudNote = null!, _hudDetails = null!, _hudControls = null!;
    private VBoxContainer _detailsSection = null!;
    private Button _restartButton = null!;
    private Button _runButton = null!;
    private Button _menuButton = null!;
    private Button _detailsButton = null!;
    private readonly List<Button> _speedButtons = [];

    // Build mode (M5, GitHub issue #4): a separate mode from running a
    // machine, entered from the menu like any machine but with its own
    // scene (BuildMode.cs) instead of a MachineView. HEROIC_EDITOR_* lets a
    // headless run drive it the same way HEROIC_AUTOSELECT/HEROIC_AUTORUN
    // drive machine selection, for scripted screenshots and save/load checks.
    private BuildMode? _buildMode;
    private double? _editorQuitAfterSeconds; // wall-clock: the editor has no simulated time of its own
    private double _editorTimer;
    private PanelContainer _leftPanel = null!; // hidden while build mode's own panel is up, so the two don't overlap
    private PanelContainer _infoPanel = null!; // the running machine's HUD, hidden in build mode for the same reason
    private VBoxContainer _machineList = null!, _windowSection = null!;
    private Button _machinesToggle = null!;
    private Button _editButton = null!;
    private SleepControl _sleep = null!;
    private const string SavesDir = "user://saves";
    private string? _scriptedSavePath;
    private double _scriptedSaveAt;
    private ScrollContainer _leftScroll = null!;
    private bool _hudHidden;
    private RigidBody3D? _follow;
    private Vector3 _homePivot;
    private float _homeDistance;

    /// <summary>
    /// Folds the menu down to the running machine's own controls (and a
    /// "Machines…" button) while a machine is on screen, and shrinks the
    /// panel to fit; unfolds it at the menu.
    /// </summary>
    private void SetMenuCollapsed(bool collapsed)
    {
        _machineList.Visible = !collapsed;
        _windowSection.Visible = !collapsed;
        _machinesToggle.Visible = collapsed;
        // A scroll container reports no height of its own, so the folded
        // panel would shrink to nothing; unscrolled, it takes its contents'.
        _leftScroll.VerticalScrollMode = collapsed ? ScrollContainer.ScrollMode.Disabled : ScrollContainer.ScrollMode.Auto;
        _leftPanel.SetAnchorsPreset(collapsed ? Control.LayoutPreset.TopLeft : Control.LayoutPreset.LeftWide);
        _leftPanel.OffsetLeft = 20;
        _leftPanel.OffsetTop = 20 + _menuInset;
        _leftPanel.OffsetRight = collapsed ? 240 : 320;
        _leftPanel.OffsetBottom = collapsed ? 20 : -20;
        _leftPanel.Size = Vector2.Zero; // shrink to its contents when collapsed
    }

    public override void _Ready()
    {
        // HEROIC_BACKGROUND=1 (tools/gui-check.sh): a window that never takes
        // the keyboard, for scripted runs while someone works in other apps
        if (OS.GetEnvironment("HEROIC_BACKGROUND") == "1") GetWindow().Unfocusable = true;
        // One gravity for both layers: Godot's default is 9.8, the sim core's 9.81.
        PhysicsServer3D.AreaSetParam(GetWorld3D().Space, PhysicsServer3D.AreaParameter.Gravity, (float)HeroicInventions.Sim.Physics.Gravity);
        _materials = MaterialLibrary.LoadDefault();
        BuildEnvironment();
        ScanMachineFiles();
        BuildUI();
        ApplyCamera(MenuCamera);

        if (OS.GetEnvironment(LiveLinkServer.EnableEnvVar) == "1")
            AddChild(new LiveLinkServer(_byName, running => SetRunning(running)));

        string autoSelect = OS.GetEnvironment("HEROIC_AUTOSELECT");
        if (!string.IsNullOrEmpty(autoSelect) && _machineFiles.ContainsKey(autoSelect))
            SelectMachine(autoSelect);
        else if (OS.GetEnvironment("HEROIC_AUTORUN") == "1" && _machineFiles.Count > 0)
            SelectMachine(_machineFiles.Keys.First());

        string worldName = OS.GetEnvironment("HEROIC_WORLD");
        if (!string.IsNullOrEmpty(worldName)) LoadWorldNamed(worldName);

        if (OS.GetEnvironment("HEROIC_EDITOR") == "1") SelectBuildMode();
        if (double.TryParse(OS.GetEnvironment("HEROIC_EDITOR_QUIT_AFTER_SECONDS"), System.Globalization.CultureInfo.InvariantCulture, out double editorQuit))
            _editorQuitAfterSeconds = editorQuit;

        // HEROIC_INPUT="wait 30; hold right 1; camera; shot /tmp/a.png; quit": scripted
        // mouse and keys for run view (ScriptedInput.cs), plus "select NAME",
        // "run" and "pause"; build mode has its own (HEROIC_EDITOR_INPUT)
        string inputScript = OS.GetEnvironment("HEROIC_INPUT");
        if (!string.IsNullOrEmpty(inputScript)) _inputScript = new ScriptedInput("Main", inputScript, this, () => _orbit, RunViewStep);

        if (double.TryParse(OS.GetEnvironment("HEROIC_SPEED"), System.Globalization.CultureInfo.InvariantCulture, out double speed))
            SetSpeed(speed);

        if (double.TryParse(OS.GetEnvironment("HEROIC_QUIT_AFTER_SIM_SECONDS"), System.Globalization.CultureInfo.InvariantCulture, out double quitAfter))
            _quitAfterSimSeconds = quitAfter;

        // HEROIC_LOAD=<save file>: take up a save as soon as it has loaded; HEROIC_SAVE=<file> with HEROIC_SAVE_AT=<sim seconds> writes one when the clock gets there (scripted checks)
        if ((_current is not null || _views.Count > 0) && OS.GetEnvironment("HEROIC_LOAD") is { Length: > 0 } loadPath) LoadSave(loadPath);
        _scriptedSavePath = OS.GetEnvironment("HEROIC_SAVE") is { Length: > 0 } sp ? sp : null;
        _scriptedSaveAt = double.TryParse(OS.GetEnvironment("HEROIC_SAVE_AT"), System.Globalization.CultureInfo.InvariantCulture, out double sa) ? sa : 0;
        // HEROIC_SLEEP=<wake id>: start sleeping until one of the machine's wake conditions as soon as it has loaded (scripted checks of the game's own path)
        if (_current is not null && OS.GetEnvironment("HEROIC_SLEEP") is { Length: > 0 } sleepId) _sleep.StartNamed(sleepId);

        if (_current is not null)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var setting in OS.GetEnvironment("HEROIC_SET").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (setting.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var target, var field, var value])
                    _current.Runtime.SetField(target, field, double.Parse(value, inv));
                else GD.PrintErr($"HEROIC_SET: expected 'target field value', got '{setting}'");
            string tracePath = OS.GetEnvironment("HEROIC_TRACE");
            double traceEvery = double.TryParse(OS.GetEnvironment("HEROIC_TRACE_DT"), inv, out double traceDt) ? traceDt : 0.1;
            // a world writes one trace per placed machine, <path>.<label>
            if (!string.IsNullOrEmpty(tracePath) && _views.Count > 0)
            {
                foreach (var v in _views) v.StartTrace($"{tracePath}.{v.Name}", traceEvery);
                StartLinksTrace(tracePath, traceEvery);
            }
            else if (!string.IsNullOrEmpty(tracePath))
                _current.StartTrace(tracePath, traceEvery);
        }
    }

    private void ScanMachineFiles()
    {
        foreach (string file in DirAccess.GetFilesAt(MachinesDir).Where(f => f.EndsWith(".machine")).Order())
        {
            string path = $"{MachinesDir}/{file}";
            try
            {
                var def = MachineDef.Parse(Godot.FileAccess.GetFileAsString(path));
                _machineFiles[def.Name] = path;
            }
            catch (Exception e) when (e is MachineFormatException or FormatException)
            {
                GD.PushError($"{path}: {e.Message}");
            }
        }
    }

    // ------------------------------------------------------------------ UI

    private void BuildUI()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        var panel = _leftPanel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        panel.OffsetLeft = 20;
        panel.OffsetRight = 320;
        panel.OffsetTop = 20;
        panel.OffsetBottom = -20; // bottom-bounded, like the info panel: the buttons scroll instead of running off the window
        layer.AddChild(panel);
        var scroll = _leftScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(col);

        var title = new Label { Text = "Heroic Inventions" };
        title.AddThemeFontSizeOverride("font_size", 22);
        col.AddChild(title);

        // While a machine runs, the list folds away behind this button so the
        // machine gets the screen; the list used to cover a quarter of it.
        _machinesToggle = BigButton("Machines…");
        _machinesToggle.Visible = false;
        _machinesToggle.Pressed += () => SetMenuCollapsed(_machineList.Visible);
        col.AddChild(_machinesToggle);

        _machineList = new VBoxContainer();
        _machineList.AddThemeConstantOverride("separation", 10);
        col.AddChild(_machineList);
        _machineList.AddChild(new HSeparator());

        foreach (var (name, _) in _machineFiles)
        {
            var button = BigButton(DisplayNames.GetValueOrDefault(name, name));
            button.Pressed += () => SelectMachine(name);
            _machineList.AddChild(button);
        }

        _machineList.AddChild(new HSeparator());

        var worldButton = BigButton("Every machine, together");
        worldButton.Pressed += () => LoadWorldNamed("gallery");
        _machineList.AddChild(worldButton);

        var buildModeButton = BigButton("Build Mode");
        buildModeButton.Pressed += SelectBuildMode;
        _machineList.AddChild(buildModeButton);

        col.AddChild(new HSeparator());

        _runButton = BigButton("Pause");
        _runButton.Disabled = true;
        _runButton.Pressed += () => SetRunning(!_running);
        col.AddChild(_runButton);

        _restartButton = BigButton("Restart");
        _restartButton.Disabled = true;
        _restartButton.Pressed += RestartCurrent;
        col.AddChild(_restartButton);

        _detailsButton = BigButton("Show details");
        _detailsButton.Disabled = true;
        _detailsButton.Pressed += () =>
        {
            _showDetails = !_showDetails;
            _detailsButton.Text = _showDetails ? "Hide details" : "Show details";
            _detailsSection.Visible = _showDetails;
        };
        col.AddChild(_detailsButton);

        _editButton = BigButton("Edit this machine (E)");
        _editButton.Visible = false;
        _editButton.Pressed += EditFocused;
        col.AddChild(_editButton);
        BuildJoinButton(col);
        _sleep = new SleepControl(() => _views.Count > 0 ? _views : _current is null ? [] : [_current], () => _current, SetRunning, text => { _hudNote.Text = text; _hudNote.Visible = true; });
        col.AddChild(_sleep);
        _sleep.Woke += () => SaveWorld(auto: true);        // a long sleep is worth keeping
        var saveRow = new HBoxContainer();
        var saveButton = new Button { Text = "Save", TooltipText = "Save the whole running world, machines and all, to disk", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        saveButton.Pressed += () => SaveWorld(auto: false);
        var loadButton = new Button { Text = "Load", TooltipText = "Go back to the last save (or autosave) of this machine or world", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        loadButton.Pressed += LoadLatestSave;
        saveRow.AddChild(saveButton); saveRow.AddChild(loadButton);
        col.AddChild(saveRow);

        _menuButton = BigButton("Back to menu");
        _menuButton.Disabled = true;
        _menuButton.Pressed += DeselectMachine;
        col.AddChild(_menuButton);

        col.AddChild(new HSeparator());
        col.AddChild(new Label { Text = "Speed" });
        var speedRow = new HBoxContainer();
        col.AddChild(speedRow);
        foreach (var (scale, label) in Speeds)
        {
            var b = new Button { Text = label, ToggleMode = true, ButtonPressed = scale == 1 };
            b.Pressed += () => SetSpeed(scale);
            speedRow.AddChild(b);
            _speedButtons.Add(b);
        }

        _windowSection = new VBoxContainer();
        col.AddChild(_windowSection);
        _windowSection.AddChild(new HSeparator());
        _windowSection.AddChild(new Label { Text = "Window size" });
        var sizeRow = new HBoxContainer();
        _windowSection.AddChild(sizeRow);
        foreach (var (w, h, label) in WindowSizes)
        {
            var b = new Button { Text = label };
            b.Pressed += () => SetWindowSize(w, h);
            sizeRow.AddChild(b);
        }
        var fullscreenButton = new Button { Text = "Fullscreen", ToggleMode = true };
        fullscreenButton.Pressed += () => ToggleFullscreen(fullscreenButton.ButtonPressed);
        _windowSection.AddChild(fullscreenButton);

        BuildInfoPanel(layer);
        BuildMenuBar(layer);
    }

    /// <summary>
    /// A separate panel anchored to the right edge, so it can never
    /// overlap the left control panel no matter how tall either one
    /// grows — the two used to share hardcoded pixel positions, which
    /// broke as soon as the left panel gained more rows than the HUD's
    /// fixed Y assumed. Laid out as labelled sections rather than one
    /// run-on block of text.
    /// </summary>
    private void BuildInfoPanel(CanvasLayer layer)
    {
        var panel = _infoPanel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.OffsetLeft = -380;
        panel.OffsetRight = -20;
        panel.OffsetTop = 20;
        panel.OffsetBottom = -20; // bottom-bounded so it never grows past the window; Details scrolls within it
        layer.AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        panel.AddChild(col);

        _hudTitle = SectionLabel("", 20);
        col.AddChild(_hudTitle);
        _hudDescription = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0) };
        _hudDescription.AddThemeFontSizeOverride("font_size", 13);
        _hudDescription.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.75f));
        col.AddChild(_hudDescription);
        _hudState = new Label();
        col.AddChild(_hudState);

        col.AddChild(new HSeparator());
        col.AddChild(SectionLabel("Energy", 15));
        _hudEnergy = new Label();
        col.AddChild(_hudEnergy);

        col.AddChild(new HSeparator());
        col.AddChild(SectionLabel("Speed", 15));
        _hudSpeed = new Label();
        col.AddChild(_hudSpeed);

        _hudNote = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        col.AddChild(_hudNote);

        _detailsSection = new VBoxContainer();
        _detailsSection.AddThemeConstantOverride("separation", 6);
        col.AddChild(_detailsSection);
        _detailsSection.AddChild(new HSeparator());
        _detailsSection.AddChild(SectionLabel("Details", 15));
        var detailsScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _detailsSection.AddChild(detailsScroll);
        _hudDetails = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        detailsScroll.AddChild(_hudDetails);
        _detailsSection.Visible = false;

        col.AddChild(new HSeparator());
        _hudControls = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Space pause/run · F fire · R restart · D details · H hide this panel · Esc menu · 1-9 pick a machine\n"
                 + "Drag to orbit · Shift+drag or middle-drag to pan · scroll or pinch to zoom\n"
                 + "Arrows move · Shift+arrows orbit · + / − or Page Up/Down zoom · Home resets the view",
        };
        _hudControls.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.6f));
        _hudControls.AddThemeFontSizeOverride("font_size", 13);
        col.AddChild(_hudControls);
    }

    private static Label SectionLabel(string text, int fontSize)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        return l;
    }

    private static Button BigButton(string text)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40) };
        b.AddThemeFontSizeOverride("font_size", 18);
        return b;
    }

    private void SetWindowSize(int width, int height)
    {
        var window = GetWindow();
        window.Mode = Window.ModeEnum.Windowed;
        window.Size = new Vector2I(width, height);
    }

    private void ToggleFullscreen(bool on) =>
        GetWindow().Mode = on ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;

    // --------------------------------------------------------------- state

    /// <summary>Frees every machine a world placed (the focused one included) and leaves world mode.</summary>
    private void ClearWorld()
    {
        if (_editButton is not null) _editButton.Visible = false;
        if (_joinButton is not null) _joinButton.Visible = false;
        ClearLinks();
        ClearGround();
        foreach (var v in _views) v.QueueFree();
        if (_current is not null && _views.Contains(_current)) _current = null;
        _views.Clear();
        _viewMachine.Clear();
        _viewBounds.Clear();
        _world = null;
    }

    /// <summary>
    /// Loads a world: each placed machine parsed, moved to its place, and
    /// added as its own view, all stepped together. The first is focused;
    /// click another to focus it.
    /// </summary>
    private void LoadWorld(WorldDef world)
    {
        ClearWorld();
        _current?.QueueFree();
        _current = null;
        _byName.Clear();
        LoadGround(world);
        foreach (var p in world.Placements)
        {
            if (!_machineFiles.TryGetValue(p.Machine, out var path)) { GD.PushError($"world {world.Name}: no machine named {p.Machine}"); continue; }
            // on a map a machine stands on the ground (issue #37)
            var def = WorldDef.Placed(MachineDef.Parse(Godot.FileAccess.GetFileAsString(path)), p, _groundSim?.Ground);
            var runtime = new MachineRuntime(def, _materials);
            _groundSim?.Attach(p.Label, runtime);
            var view = new MachineView(runtime, _materials) { Name = p.Label, Position = Vector3.Zero, Ground = _groundSim?.Ground };
            AddChild(view);
            _views.Add(view);
            _viewMachine[view] = p.Machine;
            _byName[p.Label] = view;
        }
        if (_views.Count == 0) return;
        _world = world;
        _current = _views[0];
        _currentName = _viewMachine[_current];

        // frame the whole world
        var (min, max) = (new Vector3(float.MaxValue, 0, float.MaxValue), new Vector3(float.MinValue, 0, float.MinValue));
        foreach (var p in world.Placements.Where(p => _machineFiles.ContainsKey(p.Machine)))
        {
            min = new Vector3(Mathf.Min(min.X, (float)p.At.X), 0, Mathf.Min(min.Z, (float)p.At.Z));
            max = new Vector3(Mathf.Max(max.X, (float)p.At.X), 0, Mathf.Max(max.Z, (float)p.At.Z));
        }
        if (_groundSim?.Ground is { } g)   // a map: frame the whole of the ground
        {
            min = new Vector3((float)g.X0, 0, (float)g.Z0);
            max = new Vector3((float)(g.X0 + g.Width), 0, (float)(g.Z0 + g.Depth));
        }
        var centre = (min + max) / 2;
        float span = Mathf.Max(6, (max - min).Length());
        ApplyCamera(new CameraProfile(centre + new Vector3(0, span * 0.55f, span * 0.75f), centre, 50));
        SetRunning(true);
        SetSpeed(1);
        _restartButton.Disabled = false;
        _menuButton.Disabled = false;
        _runButton.Disabled = false;
        _detailsButton.Disabled = false;
        SetMenuCollapsed(true);
        _follow = null;
        _editButton.Visible = true;
        _joinButton.Visible = true;
        RebuildLinks();
    }

    /// <summary>
    /// A live edit (issue #75): rebuilds one machine in the running world
    /// from its edited definition, carrying over its running state (water,
    /// heat, speeds, bodies in motion) so it carries on rather than starting
    /// again; every other machine is untouched. Throws if the definition
    /// can't be built, leaving the old machine running.
    /// </summary>
    private MachineView ReplaceView(MachineView old, MachineDef def)
    {
        var runtime = new MachineRuntime(def, _materials);
        runtime.TakeStateFrom(old.Runtime);
        _groundSim?.Attach(old.Name, runtime);   // its channels pour onto the ground again
        string label = old.Name;
        old.Name = $"{label}-replaced";   // else Godot renames the new node, and the label no longer finds it (links, traces)
        var view = new MachineView(runtime, _materials) { Name = label, Position = Vector3.Zero, Ground = _groundSim?.Ground };
        AddChild(view);
        view.TakeBodiesFrom(old);
        view.TakeTraceFrom(old);
        view.SetFrozen(!_running);
        int index = _views.IndexOf(old);
        if (index >= 0) _views[index] = view;
        _viewMachine[view] = _viewMachine.GetValueOrDefault(old, def.Name);
        _viewMachine.Remove(old);
        _viewBounds.Remove(old);
        _byName[view.Name] = view;
        if (_current == old) _current = view;
        old.QueueFree();
        RebuildLinks();   // the links take hold of the new machine's parts
        return view;
    }

    /// <summary>
    /// "Edit this machine" in a world: build mode opens on the focused
    /// machine while the world keeps running, and every change rebuilds it
    /// in place with its state carried over.
    /// </summary>
    private void EditFocused()
    {
        if (_current is null || _views.Count == 0 || _buildMode is not null) return;
        var target = _current;
        _leftPanel.Visible = false;
        _infoPanel.Visible = false;
        _buildMode = new BuildMode(_materials, target.Runtime.Def, def => target = ReplaceView(target, def), () => target);
        if (_groundSim is { } ground) _buildMode.GroundHeight = ground.Ground.HeightAt;   // on a map, parts land on the ground (#37)
        _buildMode.ExitRequested += () => CallDeferred(MethodName.CloseLiveEdit);
        _buildMode.RunRequested += () => CallDeferred(MethodName.CloseLiveEdit);
        AddChild(_buildMode);
    }

    private void CloseLiveEdit()
    {
        _buildMode?.QueueFree();
        _buildMode = null;
        _leftPanel.Visible = true;
        _infoPanel.Visible = !_hudHidden;
        _camera.MakeCurrent();
        UpdateInfoPanel();
    }

    private void LoadWorldNamed(string name)
    {
        if (name == "gallery")
        {
            LoadWorld(WorldDef.Gallery(_machineFiles.Values.Select(f => MachineDef.Parse(Godot.FileAccess.GetFileAsString(f)))));
            return;
        }
        string path = $"{WorldsDir}/{name}.world";
        if (!Godot.FileAccess.FileExists(path)) { GD.PushError($"no world file {path}"); return; }
        LoadWorld(WorldDef.Parse(Godot.FileAccess.GetFileAsString(path), path));
    }

    /// <summary>
    /// In a world, a click (not a drag) on a machine focuses it: the HUD shows
    /// it and the camera swings round to it.
    /// </summary>
    private void FocusMachineAt(Vector2 screen)
    {
        if (_views.Count == 0) return;
        var from = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        MachineView? best = null;
        float bestT = float.MaxValue;
        foreach (var v in _views)
        {
            if (!_viewBounds.TryGetValue(v, out var box)) _viewBounds[v] = box = BoundsOf(v);
            if (RayHits(from, dir, box, out float t) && t < bestT) { bestT = t; best = v; }
        }
        if (best is null || best == _current) return;
        _current = best;
        _currentName = _viewMachine[best];
        var b = _viewBounds[best];
        _orbit.Pivot = b.GetCenter();
        _orbit.Distance = Mathf.Clamp(b.Size.Length() * 1.3f + 1f, 2f, 60f);
        _orbit.Apply();
        UpdateInfoPanel();
    }

    private static Aabb BoundsOf(Node node)
    {
        Aabb? box = null;
        foreach (var vi in Descendants(node).OfType<VisualInstance3D>())
        {
            if (vi is GpuParticles3D or Label3D) continue;
            var local = vi.GetAabb();
            if (local.Size == Vector3.Zero) continue;
            var world = vi.GlobalTransform * local;
            box = box is { } b ? b.Merge(world) : world;
        }
        return box ?? new Aabb(Vector3.Zero, Vector3.Zero);
    }

    private static IEnumerable<Node> Descendants(Node n)
    {
        yield return n;
        foreach (var c in n.GetChildren()) foreach (var d in Descendants(c)) yield return d;
    }

    private static bool RayHits(Vector3 from, Vector3 dir, Aabb box, out float t)
    {
        float tMin = 0, tMax = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = from[axis], d = dir[axis], lo = box.Position[axis], hi = box.End[axis];
            if (Mathf.Abs(d) < 1e-9f) { if (o < lo || o > hi) { t = 0; return false; } continue; }
            float t1 = (lo - o) / d, t2 = (hi - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Mathf.Max(tMin, t1); tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax) { t = 0; return false; }
        }
        t = tMin;
        return true;
    }

    private void SelectMachine(string name)
    {
        ClearWorld();
        _current?.QueueFree();
        _byName.Clear();

        var def = MachineDef.Parse(Godot.FileAccess.GetFileAsString(_machineFiles[name]));
        var view = new MachineView(new MachineRuntime(def, _materials), _materials) { Position = Vector3.Zero };
        AddChild(view);
        _current = view;
        _currentName = name;
        _byName[name] = view;

        ApplyCamera(Profiles.GetValueOrDefault(name, MenuCamera with { Eye = new Vector3(0, 1, 2) }));
        SetRunning(true);
        // A real aeolipile doesn't spin until its water boils (~30s of
        // simulated time for 0.3kg at 3kW) — accurate, but a bad first
        // impression on a freshly clicked button, so it defaults faster.
        // Gravity-driven demos (pendulum, lever, ramp) act immediately and
        // are easiest to watch at 1×; the speed row overrides either way.
        SetSpeed(DefaultSpeeds.GetValueOrDefault(name, 1));

        _restartButton.Disabled = false;
        _menuButton.Disabled = false;
        _runButton.Disabled = false;
        _detailsButton.Disabled = false;
        SetMenuCollapsed(true);
        _follow = FollowBody.TryGetValue(name, out var followId) ? view.BodyNamed(followId) : null;
        _sleep.Refresh();   // this machine's own wake conditions
    }

    // ---------------------------------------------------------------- save and load (issue #67)

    private string? SaveName => _world?.Name ?? _currentName;

    private string SavePath(bool auto) =>
        ProjectSettings.GlobalizePath($"{SavesDir}/{SaveName}{(auto ? ".autosave" : "")}.save");

    /// <summary>
    /// Writes the whole running scene to disk, atomically: each machine's clock and simulation state, what the rigid
    /// bodies were doing, and any sleep in progress. The machines themselves are not copied: the save names them and
    /// loading rebuilds them from their own files. <paramref name="path"/> overrides where (scripted checks).
    /// </summary>
    private void SaveWorld(bool auto, string? path = null)
    {
        if (SaveName is null || (_current is null && _views.Count == 0)) return;
        try
        {
            var views = _views.Count > 0 ? _views : [_current!];
            var machines = views.Select(v => new SavedMachine(
                _views.Count > 0 ? v.Name.ToString() : SaveName, _viewMachine.GetValueOrDefault(v, _currentName ?? SaveName),
                v.Runtime.Time, RuntimeState.Capture(v.Runtime), v.CaptureView())).ToList();
            var sleep = _sleep.Saved(machines.FirstOrDefault(m => _current is not null && m.Label == (_views.Count > 0 ? _current.Name.ToString() : SaveName))?.Label ?? machines[0].Label);
            var save = new WorldSave
            {
                Kind = _world is not null ? "world" : "machine", Name = SaveName, Machines = machines, Sleep = sleep,
                Ground = _groundSim is { } ground ? RuntimeState.CaptureGround(ground) : null,     // the dug earth and the water on it
                Boulders = _groundSim is { Ground.Boulders.Count: > 0 } rocky ? rocky.Ground.SaveBoulders() : null,   // and the rocks slides left on it (#88)
            };
            string target = path ?? SavePath(auto);
            save.WriteAtomic(target);
            GD.Print($"[save] {(auto ? "autosaved" : "saved")} {SaveName} to {target}");
            if (!auto) { _hudNote.Text = $"Saved {SaveName}."; _hudNote.Visible = true; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"could not save {SaveName}: {e.Message}");
            _hudNote.Text = $"Could not save: {e.Message}"; _hudNote.Visible = true;
        }
    }

    /// <summary>Loads the newer of this scene's save and autosave.</summary>
    private void LoadLatestSave()
    {
        if (SaveName is null) return;
        var candidates = new[] { SavePath(false), SavePath(true) }.Where(System.IO.File.Exists).OrderByDescending(System.IO.File.GetLastWriteTimeUtc).ToList();
        if (candidates.Count == 0) { _hudNote.Text = $"No save of {SaveName} yet."; _hudNote.Visible = true; return; }
        LoadSave(candidates[0]);
    }

    /// <summary>Rebuilds what a save names from its own files, lays the saved state on it, and takes up a sleep the save was in the middle of.</summary>
    private void LoadSave(string path)
    {
        WorldSave save;
        try { save = WorldSave.Read(path); }
        catch (Exception e) when (e is IOException or FormatException)
        {
            GD.PushError($"could not load {path}: {e.Message}");
            _hudNote.Text = $"Could not load: {e.Message}"; _hudNote.Visible = true;
            return;
        }
        if (save.Kind == "world") LoadWorldNamed(save.Name); else if (_machineFiles.ContainsKey(save.Name)) SelectMachine(save.Name);
        else { _hudNote.Text = $"The save is of {save.Name}, which is not here."; _hudNote.Visible = true; return; }
        int unmatched = 0;
        if (save.Ground is { } groundState && _groundSim is { } groundSim) unmatched += RuntimeState.RestoreGround(groundSim, groundState).Count;
        if (save.Boulders is { } boulders && _groundSim is { } rocky)
        {
            rocky.Ground.LoadBoulders(boulders);   // the boulders lie (and roll) where they were saved (#88)
            rocky.RetraceBoulders();
        }
        foreach (var m in save.Machines)
        {
            var view = _views.Count > 0 ? _views.FirstOrDefault(v => v.Name == m.Label) : _current;
            if (view is null) { unmatched++; continue; }
            unmatched += RuntimeState.Restore(view.Runtime, m.State).Count;
            if (m.View is not null) unmatched += view.RestoreView(m.View);
            view.ShowState();
        }
        if (save.Sleep is { } s && (_views.Count > 0 ? _views.FirstOrDefault(v => v.Name == s.Label) : _current) is { } sleeper) _sleep.Resume(sleeper, s);
        GD.Print($"[save] loaded {save.Name} from {path}{(unmatched > 0 ? $" ({unmatched} entries found nothing to set)" : "")}");
        _hudNote.Text = $"Loaded {save.Name}." + (unmatched > 0 ? $" ({unmatched} entries no longer fit the machine.)" : "");
        _hudNote.Visible = true;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) SaveWorld(auto: true);     // quitting keeps the world
    }

    private void RestartCurrent()
    {
        if (_world is { } world) { LoadWorld(world); return; }
        if (_currentName is { } name) SelectMachine(name);
    }

    /// <summary>
    /// Opens build mode: its own scene (BuildMode.cs), replacing whatever
    /// machine was selected. Distinct from SelectMachine/DeselectMachine
    /// because the editor isn't a running simulation — no speed, pause or
    /// restart controls apply to it.
    /// </summary>
    private void SelectBuildMode()
    {
        ClearWorld();
        _current?.QueueFree();
        _current = null;
        _currentName = null;
        _byName.Clear();
        SetRunning(false);

        _leftPanel.Visible = false;
        _infoPanel.Visible = false;
        _buildMode = new BuildMode(_materials);
        _buildMode.RunRequested += RunBuiltMachine;
        _buildMode.ExitRequested += () => CallDeferred(MethodName.DeselectBuildMode);
        AddChild(_buildMode);   // it brings its own camera and takes all input while open
    }

    private void DeselectBuildMode()
    {
        _buildMode?.QueueFree();
        _buildMode = null;
        _leftPanel.Visible = true;
        _infoPanel.Visible = !_hudHidden;
        _camera.MakeCurrent();
        ApplyCamera(MenuCamera);
    }

    /// <summary>"Run this machine" in build mode: hands the session's current MachineDef straight to a MachineView, the same as picking a saved machine from the menu.</summary>
    private void RunBuiltMachine()
    {
        var def = _buildMode!.CurrentMachineDef();
        DeselectBuildMode();
        var view = new MachineView(new MachineRuntime(def, _materials), _materials) { Position = Vector3.Zero };
        AddChild(view);
        _current = view;
        _currentName = def.Name;
        _byName[def.Name] = view;
        ApplyCamera(MenuCamera with { Eye = new Vector3(0, 1, 2) });
        SetRunning(true);
        _restartButton.Disabled = true; // no file to restart from until saved
        _menuButton.Disabled = false;
        _runButton.Disabled = false;
        _detailsButton.Disabled = false;
        SetMenuCollapsed(true);
        _follow = null;
    }

    private void DeselectMachine()
    {
        ClearWorld();
        _current?.QueueFree();
        _current = null;
        _currentName = null;
        _byName.Clear();
        SetRunning(false);
        ApplyCamera(MenuCamera);
        UpdateInfoPanel();

        _restartButton.Disabled = true;
        _menuButton.Disabled = true;
        _runButton.Disabled = true;
        _detailsButton.Disabled = true;
        SetMenuCollapsed(false);
        _follow = null;
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _current?.SetFrozen(!running);
        foreach (var v in _views) v.SetFrozen(!running);
        _runButton.Text = running ? "Pause" : "Run";
    }

    private const int BaseTicksPerSecond = 120;

    /// <summary>
    /// Runs the whole simulation faster or slower — rigid bodies, ropes and
    /// gears in the physics engine as well as the fluid and steam solvers —
    /// by raising the engine's time scale and its physics tick rate
    /// together. Every step stays 1/120 s of simulated time; there are just
    /// more or fewer of them each real second. (Scaling only the solvers'
    /// time step, as this used to, left the rigid bodies at real time: a
    /// screw at "10×" delivered ten times its water per actual turn.)
    /// </summary>
    private void SetSpeed(double scale)
    {
        _timeScale = scale;
        Engine.TimeScale = scale;
        Engine.PhysicsTicksPerSecond = (int)Math.Max(1, Math.Round(BaseTicksPerSecond * scale));
        Engine.MaxPhysicsStepsPerFrame = Math.Max(8, (int)Math.Ceiling(8 * scale));
        foreach (var b in _speedButtons) b.SetPressedNoSignal(Mathf.IsEqualApprox((float)scale, ParseSpeed(b.Text)));
    }

    private static float ParseSpeed(string label) => float.Parse(label.TrimEnd('×'), System.Globalization.CultureInfo.InvariantCulture);

    // The camera orbits the current profile's LookAt point (OrbitCamera.cs:
    // drag to orbit, middle- or Shift-drag to pan, scroll to zoom, arrows to
    // move). Reset to the profile's own framing every time a machine is
    // (re)selected, and by Home or View > Reset Camera.
    private OrbitCamera _orbit = null!;
    private bool _dragging, _panning;
    private CameraProfile _homeProfile = MenuCamera;

    private void ApplyCamera(CameraProfile profile)
    {
        _homeProfile = profile;
        _homePivot = profile.LookAt;
        _homeDistance = (profile.Eye - profile.LookAt).Length();
        _camera.Fov = profile.FovDegrees;
        _orbit.LookFrom(profile.Eye, profile.LookAt);
        // HEROIC_ORBIT="yaw pitch" (degrees) swings the camera round from the
        // machine's usual view — for checking a machine from another side.
        if (OS.GetEnvironment("HEROIC_ORBIT").Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var yaw, var pitch])
        {
            _orbit.Yaw += Mathf.DegToRad(float.Parse(yaw, System.Globalization.CultureInfo.InvariantCulture));
            _orbit.Pitch = Mathf.Clamp(_orbit.Pitch + Mathf.DegToRad(float.Parse(pitch, System.Globalization.CultureInfo.InvariantCulture)), MinPitch, MaxPitch);
            _orbit.Apply();
        }
    }

    // --------------------------------------------------------------- scene

    private void BuildEnvironment()
    {
        _skyMaterial = new ProceduralSkyMaterial();
        _skyTop = _skyMaterial.SkyTopColor;
        _skyHorizon = _skyMaterial.SkyHorizonColor;
        var sky = new Sky { SkyMaterial = _skyMaterial };
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = sky,
                // Screen-space ambient occlusion darkens creases and gaps
                // where parts nearly touch — a gear resting in front of
                // another, a block on a ramp. Shadow maps can't resolve
                // millimetre gaps; this works at any scale, since it reads
                // the depth buffer rather than the light.
                SsaoEnabled = true,
                SsaoRadius = 0.5f,
                SsaoIntensity = 2.5f,
            },
        });

        _sun = new DirectionalLight3D { ShadowEnabled = true };
        AddChild(_sun);
        _sun.RotationDegrees = new Vector3(-50, 30, 0);

        // 2 km across: a catapulta's bolt lands ~11 m out at 25 m/s and then
        // skids on for tens of metres (oak on stone stops it in ~80 m), and at
        // 100 m across it used to slide off the edge and fall forever. 2 m
        // deep (top still at y = 0) so a fast body can't tunnel through it
        // between ticks even without its swept test.
        var floor = _floor = new StaticBody3D { Position = new Vector3(0, -1f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2000, 2f, 2000) } });
        _floorMaterial = Shapes.Mat(Shapes.Stone);
        floor.AddChild(Shapes.Box(new Vector3(2000, 2f, 2000), _floorMaterial));
        AddChild(floor);

        _camera = new Camera3D();
        AddChild(_camera);
        _orbit = new OrbitCamera(_camera, 0.2f, 200f, MinPitch, MaxPitch);
        _orbit.MovedByPlayer += () => _follow = null;   // the player has taken the camera: stop chasing the missile
    }

    private DirectionalLight3D _sun = null!;
    private ProceduralSkyMaterial _skyMaterial = null!;
    private StandardMaterial3D _floorMaterial = null!;
    private StaticBody3D? _floor;   // sunk beneath a world's map (Main.Ground.cs)
    private Color _skyTop, _skyHorizon;
    private (double ambient, bool sunShown, int elevation, int azimuth, HeroicInventions.Sim.Planet? planet, double storm) _shownSky = (double.NaN, false, 0, 0, null, 0);

    /// <summary>
    /// The sky, seen. At 20 °C under the studio light everything is as it
    /// always was. The air's temperature tints it: colder, the light goes
    /// thin and blue and below freezing the ground whitens with frost (fully
    /// by −5 °C); warmer, it yellows. A scene that sets its sun, or has
    /// mirrors, is lit from where the sun really stands: full daylight above
    /// 10°, warming to orange near the horizon, and dark at night. Only the
    /// scene's look — nothing here touches the sim.
    /// </summary>
    private void ShowSky(MachineRuntime? run)
    {
        double ambient = run?.Ambient ?? 20;
        bool sunShown = run?.SunShown ?? false;
        var sun = run?.Sun;
        var planet = run?.Planet ?? HeroicInventions.Sim.Planet.Earth;
        double storm = sun?.ExtraDust ?? 0;
        var key = (Math.Round(ambient * 2) / 2, sunShown, sunShown ? (int)Math.Round(sun!.Elevation * 4) : 0, sunShown ? (int)Math.Round(sun!.Azimuth * 4) : 0, planet, Math.Round(storm * 10));
        if (key == _shownSky) return;
        _shownSky = key;

        float cold = Mathf.Clamp((20 - (float)ambient) / 30, 0, 1);   // 0 at 20 °C, 1 at −10 °C and below
        float hot = Mathf.Clamp(((float)ambient - 20) / 15, 0, 1);    // 0 at 20 °C, 1 at 35 °C and above
        var light = Colors.White.Lerp(new Color(0.88f, 0.92f, 1f), cold).Lerp(new Color(1f, 0.9f, 0.74f), hot);
        var top = _skyTop.Lerp(new Color(0.55f, 0.62f, 0.72f), cold * 0.7f).Lerp(new Color(0.42f, 0.6f, 0.85f), hot * 0.5f);
        var horizon = _skyHorizon.Lerp(new Color(0.82f, 0.85f, 0.9f), cold * 0.7f).Lerp(new Color(0.9f, 0.82f, 0.68f), hot * 0.5f);
        float energy = 1 - 0.2f * cold;
        // Another planet's sky and ground (issue #38): Mars's butterscotch
        // dust-lit sky over rust-red regolith, its sunlight weaker by the
        // ratio of solar constants (586 W/m² against Earth's 1361).
        bool earth = planet.IsEarth;
        if (!earth)
        {
            Color C(HeroicInventions.Sim.Machines.Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
            top = top.Lerp(C(planet.SkyColor), 0.85f);
            horizon = horizon.Lerp(C(planet.SkyColor).Lightened(0.25f), 0.85f);
            light = light.Lerp(new Color(1f, 0.88f, 0.75f), 0.5f);
            energy *= Mathf.Clamp((float)(0.4 + 0.6 * planet.SolarConstant / HeroicInventions.Sim.Thermo.Sun.EarthSolarConstant), 0.2f, 1.2f);
        }

        if (sunShown)
        {
            float el = (float)sun!.Elevation;
            float day = Mathf.Clamp(el / 10, 0, 1);                   // full daylight above 10°
            float low = 1 - Mathf.Clamp(el / 25, 0, 1);               // a low sun reddens
            light = light.Lerp(new Color(1f, 0.62f, 0.35f), low * day);
            energy *= day;
            top = top.Lerp(new Color(0.02f, 0.03f, 0.08f), 1 - day);
            horizon = horizon.Lerp(new Color(0.95f, 0.55f, 0.35f), low * day * 0.6f).Lerp(new Color(0.07f, 0.08f, 0.13f), 1 - day);
            // a dust storm (issue #69): the beam through the dust falls as e^(−Δτ·AM), and the sky thickens to a dim brown
            if (storm > 0)
            {
                float murk = 1 - Mathf.Exp(-(float)storm);
                energy *= Mathf.Max(0.08f, Mathf.Exp(-(float)storm * 0.35f));
                var dust = new Color(0.45f, 0.3f, 0.2f);
                top = top.Lerp(dust, murk * 0.8f);
                horizon = horizon.Lerp(dust.Lightened(0.15f), murk * 0.8f);
                light = light.Lerp(new Color(0.8f, 0.55f, 0.35f), murk * 0.6f);
            }
            var d = sun.Direction;
            var toSun = new Vector3((float)d.X, (float)d.Y, (float)d.Z);
            var up = Mathf.Abs(toSun.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
            _sun.LookAtFromPosition(Vector3.Zero, -toSun, up);        // shine from the sun, towards the scene
        }
        else _sun.RotationDegrees = new Vector3(-50, 30, 0);

        _sun.LightColor = light;
        _sun.LightEnergy = energy;
        _sun.Visible = energy > 0.001f;
        _skyMaterial.SkyTopColor = top;
        _skyMaterial.SkyHorizonColor = horizon;
        float frost = Mathf.Clamp(-(float)ambient / 5, 0, 1);         // none above 0 °C, white by −5 °C
        var ground = earth ? Shapes.Stone : new Color((float)planet.GroundColor.X, (float)planet.GroundColor.Y, (float)planet.GroundColor.Z);
        // Mars's frost is thin CO2 and water rime: a pale dusting, not an Earth snowfield
        _floorMaterial.AlbedoColor = ground.Lerp(new Color(0.93f, 0.95f, 0.98f), earth ? frost : frost * 0.25f);
        _floorMaterial.Roughness = 0.8f - 0.25f * frost;
    }

    // Radians per pixel dragged, and the pitch range that keeps the camera
    // from flipping over the top or bottom of its orbit.
    private const float MinPitch = -1.4f, MaxPitch = 1.4f; // ≈ ±80°

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_buildMode is not null) return; // build mode handles its own camera, keys and clicks
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key.Keycode);
                break;

            // Drag with the left or right mouse button to orbit — right
            // works too since the left often lands on a UI button instead.
            // Shift+drag or the middle button pans, as in build mode.
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } mb:
                _dragging = mb.Pressed;
                _panning = mb.Pressed && mb.ShiftPressed;
                if (mb.ButtonIndex == MouseButton.Left && mb.Pressed) _pressAt = mb.Position;
                else if (mb.ButtonIndex == MouseButton.Left && mb.Position.DistanceTo(_pressAt) < 4)
                {
                    if (_joining) JoinPickAt(mb.Position);
                    else FocusMachineAt(mb.Position);
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _dragging = _panning = mb.Pressed;
                break;
            case InputEventMouseMotion motion when _dragging:
                if (_panning) _orbit.Pan(motion.Relative);
                else _orbit.Orbit(motion.Relative);
                break;
            default:
                _orbit.HandleGesture(@event);   // wheel, pinch and trackpad scrolling
                break;
        }
    }

    private void HandleKey(Key keycode)
    {
        switch (keycode)
        {
            case Key.Space when _current is not null:
                SetRunning(!_running);
                break;
            case Key.F:
                _current?.ToggleFire();
                break;
            case Key.R when _current is not null:
                RestartCurrent();
                break;
            case Key.Escape when _joining:
                SetJoining(false);
                break;
            case Key.J when _world is not null:
                SetJoining(!_joining);
                break;
            case Key.Escape when _current is not null:
                DeselectMachine();
                break;
            case Key.D when _current is not null:
                _detailsButton.EmitSignal(BaseButton.SignalName.Pressed);
                break;
            case Key.E when _views.Count > 0:
                EditFocused();
                break;
            case Key.Home:
                ResetCamera();
                break;
            case Key.H:
                _hudHidden = !_hudHidden;
                _infoPanel.Visible = !_hudHidden;
                break;
            case >= Key.Key1 and <= Key.Key9:
            {
                int index = (int)(keycode - Key.Key1);
                var names = _machineFiles.Keys.ToList();
                if (index < names.Count) SelectMachine(names[index]);
                break;
            }
        }
    }

    // HEROIC_FPS_REPORT=1: every 2 s, print frames per second and where frame time goes (for #74's budgets)
    private readonly bool _fpsReport = OS.GetEnvironment("HEROIC_FPS_REPORT") == "1";
    private double _fpsTimer;

    public override void _Input(InputEvent @event)
    {
        if (_buildMode is null) OrbitCamera.ClaimNavigationKeys(@event, GetViewport());
    }

    private ScriptedInput? _inputScript;

    private ScriptedInput.Step? RunViewStep(string[] w)
    {
        switch (w[0])
        {
            case "select": SelectMachine(w[1]); return ScriptedInput.Step.Next;
            case "run": SetRunning(true); return ScriptedInput.Step.Next;
            case "pause": SetRunning(false); return ScriptedInput.Step.Next;
        }
        return null;
    }

    public override void _Process(double delta)
    {
        if (_buildMode is null) _orbit.ProcessKeys(delta, GetViewport());
        _inputScript?.Process(delta);
        if (!_fpsReport || (_fpsTimer += delta) < 2) return;
        _fpsTimer = 0;
        GD.Print($"[fps] {Performance.GetMonitor(Performance.Monitor.TimeFps):F0} fps · frame {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:F1} ms process, " +
                 $"{Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:F1} ms physics · {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):F0} draw calls · " +
                 $"{Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):F0} objects · {_views.Count} machines");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_scriptedSavePath is { } scripted && _current is not null && _current.Runtime.Time >= _scriptedSaveAt)
        {
            SaveWorld(auto: false, scripted);
            _scriptedSavePath = null;
        }
        if (_sleep.Active)
            _sleep.Advance();                 // sleeping: run ahead as fast as it can, in place of stepping in real time
        else if (_running && _views.Count > 0)
            StepWorld(delta); // a world: every machine, stepped together, and the links between them
        else if (_running && _current is not null)
            _current.Simulate(delta); // already scaled: see SetSpeed

        if (_audit && _running && _current is not null) _current.AuditTick(delta);

        if (_quitAfterSimSeconds is { } limit && _current is not null && _current.Runtime.Time >= limit)
        {
            if (_audit) GD.Print(_current.AuditReport());
            if (_debugPhysics) GD.Print($"[final] {_current.Details}");
            _current.StopTrace();
            foreach (var v in _views) v.StopTrace();
            _linksView?.StopTrace();
            GetTree().Quit();
        }

        if (_editorQuitAfterSeconds is { } editorLimit && _buildMode is not null && (_editorTimer += delta) >= editorLimit)
        {
            GetTree().Quit();
        }

        if (_debugPhysics && _current is not null && (_debugTimer += delta) >= 0.5)
        {
            _debugTimer = 0;
            GD.Print($"[{_current.Runtime.Time:F2}s] {_current.DebugState()}\n{_current.EnergyHud()}");
        }

        // HEROIC_LIVE_EDIT_AFTER=<s>: open live editing on the focused machine once
        // the world has run that long (for recorded and scripted checks of #75)
        if (_liveEditAfter is { } editAt && _views.Count > 0 && _current is not null && _current.Runtime.Time >= editAt)
        {
            _liveEditAfter = null;
            CallDeferred(MethodName.EditFocused);   // after this physics step, as a click would be
        }
        FollowMissile();
        ShowSky(_current?.Runtime);
        UpdateInfoPanel();
    }

    /// <summary>
    /// Once a thrown stone or bolt is clear of the machine, eases the camera
    /// to look between the two and pulls back far enough to keep both in
    /// view, so the flight and the landing are on screen.
    /// </summary>
    private void FollowMissile()
    {
        if (_follow is null || !IsInstanceValid(_follow)) return;
        var target = _follow.GlobalPosition;
        var home = _homePivot with { Y = 0 };
        float separation = new Vector2(target.X - home.X, target.Z - home.Z).Length();
        if (separation < 1f) return;
        var wantPivot = (_homePivot + target) / 2;
        float wantDistance = Mathf.Max(_homeDistance, separation * 0.6f + 1.5f);
        _orbit.Pivot = _orbit.Pivot.Lerp(wantPivot, 0.06f);
        _orbit.Distance = Mathf.Lerp(_orbit.Distance, wantDistance, 0.06f);
        _orbit.Apply();
    }

    private void UpdateInfoPanel()
    {
        if (_current is null)
        {
            _hudTitle.Text = "No machine selected";
            _hudDescription.Text = "";
            _hudState.Text = "";
            _hudEnergy.Text = "";
            _hudSpeed.Text = "";
            _hudNote.Text = "Pick one from the list on the left.";
            _hudDetails.Text = "";
            return;
        }

        _hudTitle.Text = DisplayNames.GetValueOrDefault(_currentName!, _currentName!);
        _hudDescription.Text = Descriptions.GetValueOrDefault(_currentName!, "");
        _hudState.Text = $"{(_running ? "Running" : "Paused")} · time ×{_timeScale:0.##}";

        var e = _current.Energy();
        double mech = e.KineticJ + e.PotentialJ;
        _hudEnergy.Text = mech > 1e-9
            ? $"{MachineView.FormatJoules(mech)} mechanical\n{e.KineticJ / mech * 100:F0}% kinetic · {e.PotentialJ / mech * 100:F0}% potential" +
              (e.ThermalDeliveredJ > 0 ? $"\n{MachineView.FormatJoules(e.ThermalDeliveredJ)} heat delivered" : "")
            : e.ThermalDeliveredJ > 0
                ? $"{MachineView.FormatJoules(e.ThermalDeliveredJ)} heat delivered"
                : "0 J";

        _hudSpeed.Text = e.SpeedLabel;

        var note = new List<string>();
        if (e.EfficiencyPercent is { } eff) note.Add($"Efficiency: {MachineView.FormatPercent(eff)} of heat became motion");
        if (e.RetainedPercent is { } ret) note.Add($"Energy retained: {ret:F0}% of its starting mechanical energy");
        string boiling = BoilingHint();
        if (boiling.Length > 0) note.Add(boiling);
        // a map that settles as it loads (issue #61): the ground comes down on screen, and the HUD says so while it does
        if (_groundSim is { Ground: { SettleOnLoad: true } ground } sim && ground.SettleRate > 0 && !ground.Stood)
            note.Add($"The ground is settling: {sim.Settled} faces have failed so far, after {sim.SettlePasses} passes");
        else if (_groundSim is { Ground.Boulders.Count: > 0 } rocky)
            note.Add($"The slide left {rocky.Ground.Boulders.Count} boulders and brought down {rocky.Ground.Collapsed:F0} m³ of rim");
        _hudNote.Text = string.Join("\n", note);

        _hudDetails.Text = _current.Details.Replace(" · ", "\n");
    }

    /// <summary>
    /// Nothing spins until a boiler's water reaches 100°C — real physics,
    /// but easy to mistake for "it's broken" while it's still heating.
    /// </summary>
    private string BoilingHint()
    {
        if (_current is null) return "";
        var coldBoilers = _current.Runtime.Boilers.Values.Where(b => b.Temperature < 99).ToList();
        if (coldBoilers.Count == 0 || _current.Runtime.Rotors.Count == 0) return "";
        double hottest = coldBoilers.Max(b => b.Temperature);
        return $"Heating — {hottest:F0}°C of 100°C, then it starts spinning (try a higher speed above)";
    }
}
