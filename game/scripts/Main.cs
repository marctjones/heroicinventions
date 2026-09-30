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
        ["water-wheels"] = new(new Vector3(4.5f, 5f, 17f), new Vector3(5.5f, 1.3f, 0), 55),
        ["fire-and-water"] = new(new Vector3(0.9f, 2.4f, 6.0f), new Vector3(0.9f, 0.4f, 0), 50),
        ["sluice-demo"] = new(new Vector3(4.5f, 3.2f, 9.5f), new Vector3(4.2f, 0.8f, -0.5f), 55),
        ["constant-head"] = new(new Vector3(2.2f, 2.2f, 2.8f), new Vector3(0.8f, 0.8f, -0.7f), 50),
        ["tank-leaks"] = new(new Vector3(3.6f, 1.8f, 10.5f), new Vector3(3.6f, 0.6f, 0), 50),
        ["boiler-safety"] = new(new Vector3(0.75f, 1.5f, 3.0f), new Vector3(0.75f, 0.4f, 0), 50),
        ["suction-limit"] = new(new Vector3(3.6f, 6.9f, 16f), new Vector3(3.6f, 6.7f, 0), 50),
        ["bellows-forge"] = new(new Vector3(1.2f, 1.1f, 2.2f), new Vector3(0, 0.5f, 0), 45),
        ["windmills"] = new(new Vector3(0, 9f, 46f), new Vector3(0, 8f, 0), 50),
        ["capstans"] = new(new Vector3(0, 2.6f, 7.5f), new Vector3(0, 1.6f, 0), 50),
        ["crank-slider"] = new(new Vector3(1.3f, 1.6f, 2.6f), new Vector3(0, 1.15f, 0), 50),
        ["universal-joint"] = new(new Vector3(0.6f, 1.8f, 2.4f), new Vector3(0, 1.25f, 0), 50),
        ["drop-test"] = new(new Vector3(0.5f, 1.4f, 4.5f), new Vector3(0, 0.6f, 0), 50),
        ["rope-over-bars"] = new(new Vector3(5.5f, 4.0f, 9.0f), new Vector3(0, 2.6f, 0), 50),
        ["bar-crane"] = new(new Vector3(7.0f, 5.5f, 19.0f), new Vector3(7.0f, 3.5f, 0), 55),
        ["winter-night"] = new(new Vector3(0, 2.2f, 5.5f), new Vector3(0, 0.4f, 0), 50),
        ["earth-machines-on-mars"] = new(new Vector3(1.5f, 5f, 17f), new Vector3(1.5f, 3f, 0), 50),
        ["two-modules"] = new(new Vector3(0, 6f, 15f), new Vector3(0, 1.5f, 0), 50),
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
        ["constant-head"] = "Float Valve (Constant Head)",
        ["tank-leaks"] = "Tank Leaks (Torricelli)",
        ["boiler-safety"] = "Safety Valve and Burst Boiler",
        ["suction-limit"] = "Lift Pumps and the Suction Limit",
        ["fire-and-water"] = "Fire and Water",
        ["water-wheels"] = "Water Wheels",
        ["heron-temple-doors"] = "Heron's Temple Doors",
        ["bearing-friction"] = "Bearing Friction",
        ["bellows-forge"] = "Bellows and Forced Draught",
        ["windmills"] = "Windmills and the Betz Limit",
        ["capstans"] = "Capstans: Rope Friction on a Post",
        ["rope-over-bars"] = "Rope over a Fixed Bar",
        ["drop-test"] = "Drop Test: Impacts and Restitution",
        ["crank-slider"] = "Crank and Connecting Rod",
        ["universal-joint"] = "Universal Joint",
        ["bar-crane"] = "Roman Crane without a Pulley",
        ["winter-night"] = "A Winter Night (Ambient Temperature)",
        ["earth-machines-on-mars"] = "Earth's Machines on Mars (Planet Settings)",
        ["two-modules"] = "Two Modules on Mars (Enclosures)",
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
        ["heron-temple-doors"] = "Hero of Alexandria's temple doors that open by themselves (Pneumatica I.38). A fire on a hollow bronze altar heats the air sealed inside; that air, shared with a closed globe half full of water, rises in pressure as it warms (P = m R T / V) and drives the water through a siphon into a hanging bucket. Once the bucket outweighs its counterweight it sinks, and its rope, wound round the doors' spindles, swings them open. The altar settles 30 K warm with a time constant of 11 minutes, so the doors open after about 13 minutes; when the fire burns out, the air cools, the water siphons back, and the counterweight shuts them. Try it at 20x.",
        ["water-wheels"] = "Two water wheels, each grinding against a millstone. Left, overshot: a 20 L/s race pours onto the top of a 3 m wheel whose buckets carry the water down the far side until they tip it out 120 degrees round. The water's weight gives rho g Q r (1 - cos 120) = 441 W at any speed, so against a 300 N m millstone it settles at 14 rpm - 73% of what the water loses falling from the race's lip, inside the 63-78% Smeaton measured. Load it past 562 N m and the brimming buckets can't turn it. Right, undershot: a 150 L/s race pushes on paddles dipping into it; at best it takes 8/27 of the stream's kinetic energy, the old undershot ceiling of about 30%.",
        ["fire-and-water"] = "Water meets fire. Left: a cistern spills 20 g/s onto a 20 kW wood fire. Boiling a kilogram of 20 C water away takes 2.59 MJ, so the fire can boil off only 7.7 g/s; the rest soaks in, and when the soaked water outweighs the fuel left (at about 150 s) the fire drowns. Until then all its heat goes into the water, none into the pot. Right: a copper of 4 kg at 90 C takes 2 L of 20 C feed water and mixes to 66.7 C; its 2 kW stove needs about 7 minutes to bring it back to the boil.",
        ["boiler-safety"] = "Denis Papin's safety valve (1679). Two bronze boilers, each 10 kg of 20 C water rated to burst at 200 kPa, each over a wood fire giving it 10 kW. Sealed, both warm along T = 20 + 5000 (1 - exp(-t / 20930 s)). The left one's weighted lever lifts at 100 kPa (425 s) and its 8 mm valve vents what the fire brings, (Q - h (T - 20)) / L = 4.34 g/s of steam, holding the boiler at 103.4 kPa and 121.1 C for as long as the water lasts. The right one has no valve: it climbs on to 200 kPa and bursts at 482 s, 0.63 kg of its water flashing to steam at once. Tie the valve down (guard.lift 300) and the left one bursts about 51 s later. Try it at 20x.",
        ["suction-limit"] = "Why a lift pump can't raise water more than about 10 m (Galileo's well pump, Berti's tube, Torricelli, 1638-44). The atmosphere pushes the water up after the bucket; once the pressure under the bucket falls to water's vapour pressure the column breaks, at (101325 - 2330 Pa) / (rho g) = 10.09 m over the well. Three pumps, each a 15 cm bucket over a 50 cm stroke at 20 strokes a minute. Left, the bucket 6 m over its water: 0.8 x the swept 8.8 L comes out every stroke, 7.07 L or 2.36 L/s, the rod pulling 1127 N and the water gaining 80% of the work. Middle, 11 m over: the water stands at 10.09 m in the pipe, the rod pulls only 1749 N, A (P_atm - P_v), though the drive could give 10 kN, and nothing comes out. Right, 8 m over a narrow well: it draws the well down 28 mm a stroke, and after 56 strokes the column starts to break partway up; it lifts less and less until the bucket stands 10.09 m over the water, and stops.",
        ["tank-leaks"] = "Torricelli's law. Four oak barrels, 0.25 m2 each; three are 80 cm full with a 5 cm2 hole in the wall, Q = 0.6 a sqrt(2 g h), h the water above the hole. sqrt(h - hole) falls at a steady 2.66 mm^1/2 per second, so the level runs down to the hole and stops: a hole 10 cm up takes 315 s (1.11 L/s to start with, at 70 cm of head) and leaves 10 cm; the same hole 40 cm up takes 238 s (0.84 L/s) and leaves 40 cm. Lower holes leak faster and further, and throw the jet farther. The third barrel leaks into a catch tank, litre for litre, until a thumb stops the hole at 100 s. The last has no hole, only a seep of 0.05 L/s off its surface: a steady 0.2 mm/s at any level.",
        ["constant-head"] = "Ctesibius' float valve. Two identical cisterns, 40 cm full, each drain through a tap raised 1 cm into a receiver. The front one is fed by a 2 L/s aqueduct through a mouth that a bronze float closes with a conical plug: seated at 40 cm, wide open 2 cm below. It settles at 39.17 cm, where the valve lets in exactly the 0.83 L/s the tap draws, so its receiver rises a steady 1.65 mm/s, a clock. Open the tap to 2 cm (tap.opening 0.04) and the draw doubles, yet the head drops only 0.8 cm, to 38.38 cm. The back cistern, with no valve and no feed, sinks to 22.5 cm in a minute and 10 cm in two, and its receiver slows as it goes.",
        ["sluice-demo"] = "A sluice gate on a mill race. A 20 L/s spring fills a head pool; a wooden gate 1 m tall, raised 5 cm, lets the water out under its lower edge as a jet, Q = 0.6 x slot area x sqrt(2 g h). All the spring must pass the slot, so the pool rises until the water stands 25.2 cm over the slot's middle. Below, the race runs into a reach that drains over a floor-level outfall into a pond. Shut the gate (gate.opening 0) and the race runs dry at once, the reach drains away over two minutes, and the pool backs up and spills over its waste weir.",
        ["heliostats"] = "Two bronze boilers of a litre of water, each heated by a 0.5 m2 mirror turned through the day to keep throwing the sun onto it - a heliostat - at Alexandria on midsummer's day, from noon. The sun stands 82 degrees up in the south and its direct beam through clear air is 951 W/m2. A mirror sending that light to its boiler must face halfway between the two, so it shows only cos(theta/2) of itself to the sun: the mirror north of its boiler, looking back towards the sun, keeps 0.83 and throws 336 W; the one to the south, standing between boiler and sun, keeps 0.75 and throws 303 W. Watch the day go by at 20x: the beams weaken as the sun lowers, and die at sunset. Try scene.day 355: at midwinter noon the sun is only 35 degrees up, and the north mirror keeps 0.98 of its area while the south one keeps 0.42.",
        ["two-modules"] = "Two inflated modules on Mars, each holding 50 kPa of 21% oxygen at 20 C against 610 Pa of CO2 at -63 C outside. Left, sealed: walls losing 20 W/K and a 1.8 kW heater, so it settles at -63 + 1800/20 = 27 C (time constant C/UA = 1283 s); its lift pump works because the air inside holds a column (50000 - 2339)/(1000 x 3.71) = 12.85 m high; a nitrogen locker inside it leaks into it, not onto Mars. Right, punctured by a 1 cm2 hole: its air rushes out at the speed of sound and the pressure falls as e^(-t/2513 s), 24.4 kPa after half an hour, 11.9 after an hour, the dial sinking, until at about 1 kPa the membrane sags. Try punctured.leak 0 to patch it, or sealed.heater 0. Best at 20x.",
        ["earth-machines-on-mars"] = "The same parts and formulas, with Mars's numbers: g = 3.71 m/s2, 610 Pa of air that is 95% CO2, -63 C. The pendulums swing sqrt(9.81 / 3.71) = 1.63 times as slowly (the 1 m one on its bearing in 3.245 s). The lift pump holds no water: the air's 610 Pa is barely more than water's vapour pressure at 0 C (605.6 Pa), so its reach is (610 - 605.6) / (1000 x 3.71) = 1.2 mm. The windmill's wind carries 1/2 rho A v^3 = 596 W through 5 m sails at 10 m/s, 1/79 of Earth's 47 kW, because Mars's air is 0.0152 kg/m3. The kettle boils at 0.1 C, a second after the fire is lit. Try scene.gravity 9.81, or scene.pressure 50 (kPa).",
        ["winter-night"] = "A night at -10 C. A copper of 5 kg of water, taken off the fire at 90 C, cools towards the air by Newton's law: 2 W for every kelvin it is warmer, against 5 x 4186 J/K, so T = -10 + 100 exp(-t / 10465 s) - 60.9 C after an hour, 40.3 C after two. The cistern beside it ices over: each new layer's latent heat has to leave up through the ice already there, so the ice thickens as the square root of time (Stefan, 1891) - 22.8 mm in an hour, twice that in four. The shallow basin's seep evaporates nothing under ice. Try scene.ambient 20, or 30 for a summer's day. Best at 20x.",
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
        _leftPanel.OffsetTop = 20;
        _leftPanel.OffsetRight = collapsed ? 240 : 320;
        _leftPanel.OffsetBottom = collapsed ? 20 : -20;
        _leftPanel.Size = Vector2.Zero; // shrink to its contents when collapsed
    }

    public override void _Ready()
    {
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

        if (double.TryParse(OS.GetEnvironment("HEROIC_SPEED"), System.Globalization.CultureInfo.InvariantCulture, out double speed))
            SetSpeed(speed);

        if (double.TryParse(OS.GetEnvironment("HEROIC_QUIT_AFTER_SIM_SECONDS"), System.Globalization.CultureInfo.InvariantCulture, out double quitAfter))
            _quitAfterSimSeconds = quitAfter;

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
            Text = "Space pause/run · F fire · R restart · D details · H hide this panel · Esc menu · 1-9 pick a machine\nDrag to orbit · scroll to zoom",
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
        foreach (var p in world.Placements)
        {
            if (!_machineFiles.TryGetValue(p.Machine, out var path)) { GD.PushError($"world {world.Name}: no machine named {p.Machine}"); continue; }
            var def = MachineDef.Parse(Godot.FileAccess.GetFileAsString(path)).Translated(p.At);
            var view = new MachineView(new MachineRuntime(def, _materials), _materials) { Name = p.Label, Position = Vector3.Zero };
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
        string label = old.Name;
        old.Name = $"{label}-replaced";   // else Godot renames the new node, and the label no longer finds it (links, traces)
        var view = new MachineView(runtime, _materials) { Name = label, Position = Vector3.Zero };
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
        _orbitPivot = b.GetCenter();
        _orbitDistance = Mathf.Clamp(b.Size.Length() * 1.3f + 1f, 2f, 60f);
        UpdateOrbitCamera();
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

    // Orbit state, in spherical coordinates around the current profile's
    // LookAt point — drag to orbit, scroll to zoom. Reset to the profile's
    // own framing every time a machine is (re)selected.
    private Vector3 _orbitPivot;
    private float _orbitDistance, _orbitYaw, _orbitPitch, _orbitFov;
    private bool _dragging;

    private void ApplyCamera(CameraProfile profile)
    {
        _orbitPivot = _homePivot = profile.LookAt;
        _orbitFov = profile.FovDegrees;
        var offset = profile.Eye - profile.LookAt;
        _orbitDistance = _homeDistance = offset.Length();
        _orbitYaw = Mathf.Atan2(offset.X, offset.Z);
        _orbitPitch = Mathf.Asin(Mathf.Clamp(offset.Y / Mathf.Max(_orbitDistance, 0.001f), -1, 1));
        // HEROIC_ORBIT="yaw pitch" (degrees) swings the camera round from the
        // machine's usual view — for checking a machine from another side.
        if (OS.GetEnvironment("HEROIC_ORBIT").Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var yaw, var pitch])
        {
            _orbitYaw += Mathf.DegToRad(float.Parse(yaw, System.Globalization.CultureInfo.InvariantCulture));
            _orbitPitch = Mathf.Clamp(_orbitPitch + Mathf.DegToRad(float.Parse(pitch, System.Globalization.CultureInfo.InvariantCulture)), MinPitch, MaxPitch);
        }
        UpdateOrbitCamera();
    }

    private void UpdateOrbitCamera()
    {
        var offset = new Vector3(
            _orbitDistance * Mathf.Cos(_orbitPitch) * Mathf.Sin(_orbitYaw),
            _orbitDistance * Mathf.Sin(_orbitPitch),
            _orbitDistance * Mathf.Cos(_orbitPitch) * Mathf.Cos(_orbitYaw));
        _camera.Position = _orbitPivot + offset;
        _camera.LookAt(_orbitPivot, Vector3.Up);
        _camera.Fov = _orbitFov;
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
        var floor = new StaticBody3D { Position = new Vector3(0, -1f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2000, 2f, 2000) } });
        _floorMaterial = Shapes.Mat(Shapes.Stone);
        floor.AddChild(Shapes.Box(new Vector3(2000, 2f, 2000), _floorMaterial));
        AddChild(floor);

        _camera = new Camera3D();
        AddChild(_camera);
    }

    private DirectionalLight3D _sun = null!;
    private ProceduralSkyMaterial _skyMaterial = null!;
    private StandardMaterial3D _floorMaterial = null!;
    private Color _skyTop, _skyHorizon;
    private (double ambient, bool sunShown, int elevation, int azimuth, HeroicInventions.Sim.Planet? planet) _shownSky = (double.NaN, false, 0, 0, null);

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
        var key = (ambient, sunShown, sunShown ? (int)Math.Round(sun!.Elevation * 4) : 0, sunShown ? (int)Math.Round(sun!.Azimuth * 4) : 0, planet);
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
    private const float OrbitSensitivity = 0.008f;
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
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } mb:
                _dragging = mb.Pressed;
                if (mb.ButtonIndex == MouseButton.Left && mb.Pressed) _pressAt = mb.Position;
                else if (mb.ButtonIndex == MouseButton.Left && mb.Position.DistanceTo(_pressAt) < 4)
                {
                    if (_joining) JoinPickAt(mb.Position);
                    else FocusMachineAt(mb.Position);
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp }:
                _orbitDistance = Mathf.Max(0.2f, _orbitDistance * 0.9f);
                UpdateOrbitCamera();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown }:
                _orbitDistance = Mathf.Min(200f, _orbitDistance / 0.9f);
                UpdateOrbitCamera();
                break;
            case InputEventMouseMotion motion when _dragging:
                _orbitYaw -= motion.Relative.X * OrbitSensitivity;
                _orbitPitch = Mathf.Clamp(_orbitPitch - motion.Relative.Y * OrbitSensitivity, MinPitch, MaxPitch);
                UpdateOrbitCamera();
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

    public override void _Process(double delta)
    {
        if (!_fpsReport || (_fpsTimer += delta) < 2) return;
        _fpsTimer = 0;
        GD.Print($"[fps] {Performance.GetMonitor(Performance.Monitor.TimeFps):F0} fps · frame {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:F1} ms process, " +
                 $"{Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:F1} ms physics · {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):F0} draw calls · " +
                 $"{Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):F0} objects · {_views.Count} machines");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_running && _views.Count > 0)
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
        _orbitPivot = _orbitPivot.Lerp(wantPivot, 0.06f);
        _orbitDistance = Mathf.Lerp(_orbitDistance, wantDistance, 0.06f);
        UpdateOrbitCamera();
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
