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
///   HEROIC_SET="t f v [at]; ..."  set sim fields (target field value) before the first step, or at [at] seconds (Main.Timed.cs)
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
        ["kongming-lantern"] = "lantern",
        ["torsion-catapult"] = "stone",
        ["vitruvian-catapulta"] = "bolt",
    };

    private static readonly Dictionary<string, CameraProfile> Profiles = new()
    {
        // #148: machines that had no profile, framed from their bounds and then checked by eye in a shot each
        ["boulder"] = new(new Vector3(1.19f, 1.69f, 3.39f), new Vector3(0.00f, 0.50f, 0.00f), 45),
        ["cargo-crate"] = new(new Vector3(1.0f, 1.3f, 2.6f), new Vector3(0, 0.25f, 0), 45),   // the crate with ground round it, not filling the frame
        ["crate"] = new(new Vector3(1.0f, 1.3f, 2.6f), new Vector3(0, 0.25f, 0), 45),
        ["cistern"] = new(new Vector3(1.4f, 2.4f, 4.4f), new Vector3(0, 1.0f, 0), 45),
        ["cistern-and-trough"] = new(new Vector3(4.2f, 3.2f, 7.4f), new Vector3(2.0f, 1.0f, 0), 45),
        ["cistern-drain"] = new(new Vector3(1.6f, 1.6f, 4.0f), new Vector3(0.75f, -0.4f, 0), 45),   // down through the ground to the buried cistern
        ["crane-hoist"] = new(new Vector3(5.14f, 7.71f, 13.31f), new Vector3(0.85f, 3.42f, 1.05f), 45),
        ["dry-mill"] = new(new Vector3(2.84f, 4.62f, 8.69f), new Vector3(-0.08f, 1.70f, 0.35f), 45),
        ["fall-and-swing"] = new(new Vector3(2.4f, 3.2f, 7.0f), new Vector3(1.0f, 2.3f, 0), 45),
        ["free-sails"] = new(new Vector3(14.12f, 24.39f, 41.21f), new Vector3(0.00f, 10.27f, 0.87f), 45),
        ["hillside-pond"] = new(new Vector3(7.56f, 6.61f, 15.36f), new Vector3(2.29f, 1.34f, 0.30f), 45),
        ["spill-tank"] = new(new Vector3(1.41f, 1.76f, 4.02f), new Vector3(0.00f, 0.35f, 0.00f), 45),
        ["trench-crew"] = new(new Vector3(4.86f, 3.50f, 7.70f), new Vector3(2.00f, 0.64f, -0.48f), 45),
        ["trough"] = new(new Vector3(1.0f, 1.6f, 3.0f), new Vector3(0, 0.5f, 0), 45),
        ["walkers-wheel"] = new(new Vector3(1.57f, 5.59f, 9.05f), new Vector3(-1.60f, 2.42f, 0.00f), 45),
        ["falling-stones"] = new(new Vector3(-25f, 175f, 470f), new Vector3(-25f, 175f, 0), 50),   // the whole 350 m fall, side-on (#192)
        ["aeolipile"] = new(new Vector3(0, 0.55f, 0.85f), new Vector3(0, 0.32f, 0), 42),
        ["shaduf"] = new(new Vector3(1.0f, 3.2f, 7.5f), new Vector3(1.0f, 1.8f, 0), 50),
        ["baghdad-battery"] = new(new Vector3(0.3f, 1.82f, 2.61f), new Vector3(0.3f, 0.1f, 0.15f), 45),   // pivot (0.3,0.1,0.15), 3 m out at 35 deg: the ten-jar lamp at x~1.2 clears the info panel (unverified against the new geometry)
        ["herons-fountain"] = new(new Vector3(0.25f, 1.25f, 2.3f), new Vector3(0.1f, 0.85f, 0), 45),
        ["material-samples"] = new(new Vector3(0, 0.7f, 1.6f), new Vector3(0, 0.3f, 0.3f), 45),
        ["branca-steam-wheel"] = new(new Vector3(0.35f, 0.95f, 1.35f), new Vector3(0.15f, 0.5f, 0), 45),
        ["kitchen-smoke-jack"] = new(new Vector3(1.5f, 1.6f, 4.2f), new Vector3(0, 1.1f, 0.2f), 50),   // takes in the spit with its joint and the rising warm-air column (unverified against the new geometry)
        ["solar-steam-wheel"] = new(new Vector3(1.2f, 2.4f, 4.6f), new Vector3(0, 0.8f, 0), 50),
        ["ball-ramp"] = new(new Vector3(4.5f, 1.6f, -1.4f), new Vector3(0f, 0.2f, -2.0f), 55),
        ["wake-clock"] = new(new Vector3(0.2f, 0.9f, 3.2f), new Vector3(0f, 0.45f, 0), 50),
        ["sand-timer"] = new(new Vector3(0.6f, 1.25f, 2.9f), new Vector3(0.6f, 0.7f, 0), 50),   // close enough that the three timers, not the scale figure, fill the frame (#190)
        ["ratchet-windlass"] = new(new Vector3(1.55f, 1.7f, 4.2f), new Vector3(1.55f, 1.1f, 0), 55),   // from the front: four windlasses in a row, ratchets facing the lens (#192)
        ["trip-hammer"] = new(new Vector3(0.9f, 1.1f, 2.4f), new Vector3(0.7f, 0.65f, 0), 55),   // the two rigs side by side, peg wheels face on (#192)
        ["tunnel-test"] = new(new Vector3(0f, 1.5f, 3.2f), new Vector3(0f, 0.75f, 0), 50),   // the planks and the ground under them, where the lead bolts come to rest (#192)
        ["crate-tongs"] = new(new Vector3(1.5f, 1.1f, 1.9f), new Vector3(1.5f, 0.7f, 0), 55),   // close on the three rigs, 0.5 m apart, so the tongs read (#192)
        ["belt-drive"] = new(new Vector3(2.4f, 1.2f, -0.25f), new Vector3(0.35f, 0.6f, -0.25f), 55),
        ["holy-water"] = new(new Vector3(0.75f, 1.1f, 3.3f), new Vector3(0.75f, 0.5f, 0), 50),
        ["trip-sluice"] = new(new Vector3(2f, 1.3f, 6.2f), new Vector3(2f, 0.75f, 0), 55),
        ["pendulum-demo"] = new(new Vector3(0, 0.7f, 1.3f), new Vector3(0, 0.5f, 0), 42),
        ["lever-demo"] = new(new Vector3(0, 0.75f, 1.5f), new Vector3(0, 0.55f, 0), 42),
        ["inclined-plane-demo"] = new(new Vector3(2.3f, 0.8f, -1.0f), new Vector3(0f, 0.3f, -1.0f), 50),
        ["newtons-cradle"] = new(new Vector3(0, 0.8f, 1.6f), new Vector3(0, 0.6f, 0), 38),
        // throwers (#190): side-on to the throw, which runs to screen-left; the machine in the right half of the clear area,
        // so the follow camera (Main.Trail.cs) widens towards the landing and comes back here for the reload
        ["trebuchet"] = new(new Vector3(-1.0f, 2.6f, 9.5f), new Vector3(-1.0f, 1.2f, 0), 50),   // throws to -x
        ["torsion-catapult"] = new(new Vector3(-0.4f, 1.5f, 4.4f), new Vector3(-0.4f, 0.5f, 0), 50),   // throws to -x
        ["antikythera-lunar-train"] = new(new Vector3(0.13f, 0.19f, 0.19f), new Vector3(0.022f, 0.09f, 0.004f), 38),
        ["archimedes-screw"] = new(new Vector3(0.5f, 2.2f, 7.0f), new Vector3(0, 1.4f, 0), 50),
        ["hama-noria"] = new(new Vector3(0.9f, 7.5f, 22f), new Vector3(0.9f, 3.0f, -1.6f), 55),
        ["newcomen-engine"] = new(new Vector3(1.5f, 4.5f, 12.5f), new Vector3(0, 3.6f, 0), 50),
        ["roman-crane"] = new(new Vector3(3.0f, 3.8f, 10.5f), new Vector3(0.3f, 3.2f, 0.5f), 50),
        // #161's reload demos had no profile, so the menu's camera stood inside the treadwheel (#190); the crane's own view, fitted
        ["roman-crane-reload"] = new(new Vector3(3.0f, 3.8f, 10.5f), new Vector3(0.3f, 3.2f, 0.5f), 50),
        ["roman-crane-reload-gang"] = new(new Vector3(3.0f, 3.8f, 10.5f), new Vector3(0.3f, 3.2f, 0.5f), 50),
        ["vitruvian-catapulta"] = new(new Vector3(7.5f, 2.2f, 1.0f), new Vector3(0, 0.8f, 1.0f), 50),   // throws to +z, screen-left from +x
        ["component-gallery"] = new(new Vector3(4.5f, 6.5f, 25f), new Vector3(4.5f, 0.8f, 0), 60),
        ["newcomen-hearth"] = new(new Vector3(1.5f, 4.5f, 12.5f), new Vector3(0, 3.6f, 0), 50),
        ["hearth-engine"] = new(new Vector3(0, 1.0f, 1.7f), new Vector3(0, 0.75f, 0), 45),
        ["post-and-lintel-crane"] = new(new Vector3(2.2f, 1.7f, 4.6f), new Vector3(0, 1.1f, 0), 50),
        ["water-mill-race"] = new(new Vector3(5f, 3.5f, 13f), new Vector3(5f, 0.9f, 0), 55),
        ["water-clock"] = new(new Vector3(0, 1.4f, 5.0f), new Vector3(-0.2f, 1.0f, 0), 50),
        ["castellum-aquae"] = new(new Vector3(11.3f, 8f, 32f), new Vector3(11.3f, 2.6f, 0), 55),
        ["heron-temple-doors"] = new(new Vector3(1.2f, 2.6f, 5.5f), new Vector3(1.0f, 1.0f, -0.4f), 55),
        ["bearing-friction"] = new(new Vector3(0.1f, 1.1f, 4.0f), new Vector3(0.1f, 0.85f, 0), 45),
        ["axle-friction"] = new(new Vector3(6.05f, 3.5f, 16f), new Vector3(6.05f, 1.7f, 0), 55),
        ["heading-rig"] = new(new Vector3(-1.0f, 3.4f, 7.2f), new Vector3(-1.0f, 0.6f, -0.66f), 60),
        ["field-windmill"] = new(new Vector3(-1.0f, 17.0f, 32.0f), new Vector3(-1.0f, 9.0f, -1.9f), 50),
        ["water-wheels"] = new(new Vector3(6.8f, 6f, 24f), new Vector3(6.8f, 1.7f, 0), 55),
        ["fire-and-water"] = new(new Vector3(0.3f, 2.5f, 8.2f), new Vector3(0.3f, 0.5f, 0), 50),
        ["sluice-demo"] = new(new Vector3(4.1f, 4.5f, 13.5f), new Vector3(4.1f, 1.0f, -1.1f), 55),
        ["dam-break"] = new(new Vector3(18f, 18f, 46f), new Vector3(18f, 1f, 0), 55),
        ["floats"] = new(new Vector3(1f, 1.6f, 3.2f), new Vector3(1f, 0.3f, 0), 50),
        ["hanging-chain"] = new(new Vector3(0, 1.6f, 3.2f), new Vector3(0, 1.5f, 0), 50),
        ["placer-sluice"] = new(new Vector3(1.6f, 4.6f, 7.4f), new Vector3(1.6f, 0.3f, 0), 50),   // from above enough to see into the channel and the gold behind the riffles (#190)
        ["constant-head"] = new(new Vector3(2.2f, 2.2f, 2.8f), new Vector3(0.8f, 0.8f, -0.7f), 50),
        ["tank-leaks"] = new(new Vector3(3.6f, 1.8f, 10.5f), new Vector3(3.6f, 0.6f, 0), 50),
        ["boiler-safety"] = new(new Vector3(0.75f, 1.5f, 3.0f), new Vector3(0.75f, 0.4f, 0), 50),
        ["boiler-shells"] = new(new Vector3(1.5f, 1.4f, 4.4f), new Vector3(1.5f, 0.4f, 0), 50),
        ["suction-limit"] = new(new Vector3(3.6f, 6.9f, 16f), new Vector3(3.6f, 6.7f, 0), 50),
        ["bellows-forge"] = new(new Vector3(1.2f, 1.1f, 2.2f), new Vector3(0, 0.5f, 0), 45),
        ["windmills"] = new(new Vector3(0, 9f, 46f), new Vector3(0, 8f, 0), 50),
        ["capstans"] = new(new Vector3(0, 2.6f, 7.5f), new Vector3(0, 1.6f, 0), 50),
        ["steam-engines-mars"] = new(new Vector3(0.4f, 2.4f, 10f), new Vector3(0.4f, 1.0f, 0), 55),
        ["mars-stirling"] = new(new Vector3(0, 14f, 34f), new Vector3(0, 0.8f, 0), 55),
        ["battering-rams"] = new(new Vector3(0.5f, 2.2f, 7.0f), new Vector3(0.5f, 1.1f, 0), 55),
        ["gristmill"] = new(new Vector3(1.2f, 3.4f, 7.5f), new Vector3(1.2f, 0.5f, 0), 50),
        ["rail-wagons"] = new(new Vector3(11f, 3.2f, 0.5f), new Vector3(0.4f, 0.3f, 0.5f), 50),   // side-on: the wagons run across the screen, not at the lens (#190)
        ["carts"] = new(new Vector3(10f, 3.5f, 3.5f), new Vector3(0f, 0.3f, 3.5f), 50),   // side-on: the carts roll across the screen, not at the lens (#190)
        ["crank-slider"] = new(new Vector3(1.3f, 1.6f, 2.6f), new Vector3(0, 1.15f, 0), 50),
        ["hierapolis-sawmill"] = new(new Vector3(2.6f, 2.4f, -4.6f), new Vector3(0.5f, 1.3f, -0.5f), 55), // from the saw side: the crank is behind the wheel seen from +z
        ["geared-brake"] = new(new Vector3(1.5f, 1.8f, -2.4f), new Vector3(0.6f, 0.95f, -0.1f), 50), // from behind: the gears sit behind the flywheels
        ["universal-joint"] = new(new Vector3(0.6f, 1.8f, 2.4f), new Vector3(0, 1.25f, 0), 50),
        ["drop-test"] = new(new Vector3(0.5f, 1.4f, 4.5f), new Vector3(0, 0.6f, 0), 50),
        ["rope-over-bars"] = new(new Vector3(5.5f, 4.0f, 9.0f), new Vector3(0, 2.6f, 0), 50),
        ["bar-crane"] = new(new Vector3(7.0f, 5.5f, 19.0f), new Vector3(7.0f, 3.5f, 0), 55),
        ["winter-night"] = new(new Vector3(0, 2.2f, 5.5f), new Vector3(0, 0.4f, 0), 50),
        ["earth-machines-on-mars"] = new(new Vector3(1.5f, 5f, 17f), new Vector3(1.5f, 3f, 0), 50),
        ["kongming-lantern"] = new(new Vector3(1.5f, 2.6f, 7.5f), new Vector3(1.5f, 1.6f, 0), 50),
        ["kongming-lantern-mars"] = new(new Vector3(1.5f, 1.6f, 5f), new Vector3(0f, 0.8f, 0), 50),
        ["two-modules"] = new(new Vector3(0, 6f, 15f), new Vector3(0, 1.5f, 0), 50),
        ["stove-rooms"] = new(new Vector3(0, 6f, 15f), new Vector3(0, 1.2f, 0), 50),
        ["airlock"] = new(new Vector3(-3f, 5f, 11f), new Vector3(-3f, 1.2f, 0), 50),
        ["mars-sols"] = new(new Vector3(4f, 3f, 5f), new Vector3(0, 0.8f, -1.2f), 50),
        ["solar-furnace"] = new(new Vector3(8f, 12.1f, 27.8f), new Vector3(8f, 1f, 0), 50),   // 30 m out: all the mirrors and the larger crucible charge (unverified against the new geometry)
        ["glass-rooms"] = new(new Vector3(0, 10f, 24f), new Vector3(0, 1.2f, 0), 50),
        ["rain-house"] = new(new Vector3(3f, 4f, 10f), new Vector3(0, 2.2f, 0), 50),
        ["greenhouse"] = new(new Vector3(-1f, 5f, 10f), new Vector3(-1.5f, 1f, 0), 50),
        ["heliostats"] = new(new Vector3(7.5f, 3.2f, 5.5f), new Vector3(0, 0.6f, 0), 50),
    };

    /// <summary>
    /// Worlds whose whole-map framing misses the action (#190), each checked by a shot. flood-plain: the pond, the race,
    /// the run down the valley and the hollow at x = 30 between the panels (look 0 32 50 14 1.5 0). dig-out: the crate
    /// and the ground over it, not the whole 20 x 12 m map with the crate a speck (look 90 35 8 0 -0.75 0).
    /// </summary>
    private static readonly Dictionary<string, CameraProfile> WorldProfiles = new()
    {
        ["flood-plain"] = new(new Vector3(14f, 28.0f, 42.4f), new Vector3(14f, 1.5f, 0), 50),
        ["dig-out"] = new(new Vector3(6.55f, 3.84f, 0), new Vector3(0, -0.75f, 0), 50),
    };
    private static readonly (double Scale, string Label)[] Speeds =
        [(0.1, "0.1×"), (0.25, "0.25×"), (1, "1×"), (5, "5×"), (10, "10×"), (20, "20×")];
    private static readonly Dictionary<string, double> DefaultSpeeds = new()
    {
        ["herons-fountain"] = 1,   // its demo operator refills it at 36 s; at the 20x a demo otherwise gets, the whole cycle is two seconds
        ["aeolipile"] = 5,
        ["winter-night"] = 20,   // an hour of frost is three minutes
        ["heliostats"] = 20,     // a day in a little over an hour
        // #168: each speed brings the machine's key event (its header's own time)
        // to ~10-40 s of real time, capped at 20x. Event time / speed in brackets.
        ["boiler-safety"] = 20,       // valve lifts at 425 s [21 s]
        ["boiler-shells"] = 20,       // lead pot bursts at 501 s [25 s]
        ["heron-temple-doors"] = 20,  // doors open at ~780 s (altar tau 660 s) [39 s]
        ["hearth-engine"] = 10,       // kettle boils at ~55 s, 225 s of fire [6 s, 23 s]
        ["mars-stirling"] = 20,       // hot ends settle over minutes; its text says "best at 20x" [~20 s]
        ["mars-sols"] = 20,           // a sol is 88,775 s: even 20x is 74 min, so the cap is the answer; Sleep-until skips to the storm
        ["solar-furnace"] = 20,       // clear glass melts at 930 s [47 s]; basalt (4,895 s) needs Sleep-until
        ["solar-steam-wheel"] = 20,   // pot boils within 300 s from cold [15 s]
        ["dam-break"] = 2,            // gate at 20 s, front at the low pond 34-52 s [10 s, 17-26 s]; faster rushes the wave
        ["constant-head"] = 5,        // 120 s of the bare cistern's slowing drain [24 s]; the filling's 47.5 s [9.5 s]
        ["water-clock"] = 10,   // the receiver fills in 404.5 s (17.8 cm/min, its header corrected by #157): 40 s at 10x
        ["wake-clock"] = 1,           // fills 50 L in 25 s: already in range, so the sleep control, not speed, is the point
        ["sand-timer"] = 10,          // 193 s to empty [19 s]
        ["rain-house"] = 20,          // 0.44 g/s of rain, 8.9 g/s at 20x
        ["greenhouse"] = 20,          // air and glass settle over an hour; the trees take 30 sols
        ["two-modules"] = 20,         // heater tau 1283 s [64 s]; puncture tau 2513 s [126 s]
        ["kongming-lantern"] = 5,     // 1 cm off the ground at 34 s, 4.9 m at 45 s [7 s, 9 s]; at 2x it lifted only at 17 s, after the 10 s frame
        ["fire-and-water"] = 10,      // left fire drowns at 150 s [15 s]; the copper recovers in 419 s [42 s]
        ["field-windmill"] = 20,      // standalone at 6 m/s; time constant 728 s, ~40 min to settle [~2 min]: the cap
        ["bearing-friction"] = 2,     // swings halve every 40 s [20 s], the 2 s period still readable at 1 s
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
        ["boiler-shells"] = "Boiler Shells: Lead, Copper and Bronze",
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
        ["hierapolis-sawmill"] = "Hierapolis Sawmill",
        ["geared-brake"] = "Geared Brake: Loaded Gear Trains",
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
        ["kongming-lantern"] = "Kongming Sky Lantern (Hot-Air Lift)",
        ["kongming-lantern-mars"] = "Sky Lantern on Mars (No Air to Float In)",
        ["two-modules"] = "Two Modules on Mars (Enclosures)",
        ["stove-rooms"] = "Stoves and Their Air (Oxygen-Limited Fire)",
        ["airlock"] = "An Airlock on Mars (Doors and Air Pumps)",
        ["mars-sols"] = "Sols and a Dust Storm at Meridiani (Mars Time and Weather)",
        ["solar-furnace"] = "A Solar Furnace (Glass from Mars Sand)",
        ["baghdad-battery"] = "The Baghdad Battery (Galvanic Jars)",
        ["shaduf"] = "The Shaduf (Well Sweep)",
        ["glass-rooms"] = "Glass Walls on Mars (Light and Pressure)",
        ["rain-house"] = "A Rain-House on Mars (Evaporation and Condensation)",
        ["greenhouse"] = "A Greenhouse on Mars (Trees, Oxygen and Wood)",
        ["heliostats"] = "Heliostats: Sunlight and Mirrors",
        ["ball-ramp"] = "Balls Rolling Down a Ramp",
        ["belt-drive"] = "Belt Drive: Tension Carries Torque",
        ["boulder"] = "Boulder (Block Dropped on Ground)",
        ["buried-crate"] = "Buried Crate and the Digging Gang",
        ["cargo-crate"] = "Cargo Crate (Lonely Rover)",
        ["cart-push"] = "Handcart for a Hand to Push",
        ["cistern"] = "Raised Cistern (for a Pipe)",
        ["cistern-and-trough"] = "Cistern and Trough, Piped Together",
        ["crane-hoist"] = "Crane Hoist without its Treadwheel",
        ["crate"] = "Crate at a Cliff's Foot",
        ["crate-tongs"] = "Crate Tongs: Grip and Load",
        ["dry-mill"] = "Dry Mill (Wheel with no Stream)",
        ["fall-and-swing"] = "Falling Block and Pendulum",
        ["falling-stones"] = "Falling Stones: Cedar, Iron, No Air",
        ["field-windmill"] = "Post Mill in the Crater Wind",
        ["free-sails"] = "Free Sails (Mill with no Stones)",
        ["hillside-pond"] = "Hillside Pond and Water Clock",
        ["holy-water"] = "Heron's Coin-Operated Holy Water",
        ["ratchet-windlass"] = "Ratchet Windlasses: Hold, Free, Wind, Lower",
        ["roman-crane-reload"] = "Roman Crane with Spare Stones",
        ["roman-crane-reload-gang"] = "Roman Crane, Three Walkers, Spare Stones",
        ["sand-timer"] = "Sand Timer against a Water Clock",
        ["trench-crew"] = "Trench Gang (Unshored Clay)",
        ["trip-hammer"] = "Trip-Hammers: Strong Wheel, Weak Wheel",
        ["trip-sluice"] = "Tripwire Sluice",
        ["trough"] = "Empty Trough (for a Pipe)",
        ["tunnel-test"] = "Tunnelling Test (Fast Bolts, Thin Planks)",
        ["wake-clock"] = "Sleep until Something Happens",
        ["walkers-wheel"] = "Treadwheel with Two Walkers",
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
        ["trebuchet"] = "A counterweight trebuchet (medieval, but Archimedes' lever taken as far as it goes). A 73 kg counterweight hangs on a chain from the short arm; the long arm carries a sling, with the stone lying on the ground behind. The falling weight whips the arm over, the sling whips the stone round faster still, and it lets go 3.5 m up at 6.6 m/s, 15 degrees above level, to first touch down about 9.5 m from where it lay (thrown from height, so not the 2.2 m of v2 sin 2θ / g), and skids on to rest ~13 m out. Only rope, hinge and gravity — nothing scripts the flight.",
        ["torsion-catapult"] = "The onager, a late-Roman one-armed stone-thrower (Ammianus Marcellinus, 4th c. AD). Its arm stands in a horizontal skein of twisted sinew — a torsion spring — winched down level. Loosed, the skein flings the arm up against a padded crossbeam near upright, and the stone, in a sling at the tip, whips round (12.4 m/s at its fastest), leaves at 11.7 m/s, 28 degrees above level and 2.1 m up, and first touches down 15.2 m from where it hung. The skein's stiffness is our estimate; Ammianus gives none.",
        ["antikythera-lunar-train"] = "Six bronze gears from the Antikythera mechanism (c. 100 BC), with their real tooth counts: 64→38, 48→24, 127→32. One turn of the first is a year; the last then turns 254/19 times — the Moon's circuits of the sky in that year. Triangular teeth, as the originals have. Here a crank turns b2 at 2 rpm and the train does the rest: each mesh reverses the sense and scales the speed by the tooth ratio, so e2 runs at 26.7 rpm.",
        ["component-gallery"] = "Five benches, one per family of parts: a pipe settling two tanks (one on a post pier) to equal height; a trilithon of posts, lintel and load; a hearth burning 50 g of wood under a boiler that spins a rotor; a spring feeding a weir, a winding channel with waypoints, a pond and a tailrace; and a pendulum, a lever on a fulcrum post and a ramp.",
        ["archimedes-screw"] = "A water screw built only from Vitruvius's rules (De Architectura X.6) — core a sixteenth of its length, eight helical blades, whole an eighth of its length across, set on a 3-4-5 slope. A man treading it turns it at 12 rpm; its lower end stands in a pool, and each turn carries the water in each dip of its channels one pitch higher, 23 L a turn, pouring into the trough at the top. As the pool drops below the intake the scoops come up part-full and the flow falls off.",
        ["newcomen-engine"] = "Thomas Newcomen's atmospheric engine (1712), after the one at Dudley Castle — the first practical piston engine, built to pump water out of mines. Steam fills the cylinder (white) and the pump rod's weight draws the piston up; at the top a jet of cold water condenses the steam (blue), and the atmosphere — 18 kN on the 53 cm piston — drives it down, rocking the beam and lifting ~47 L of water 48 m up the mine shaft each stroke. It's the air that does the work. About 5% of the fire's heat becomes lifted water here; real engines managed under 1%, because each cold jet also chilled the cylinder walls — the waste Watt's separate condenser later cured.",
        ["hama-noria"] = "A noria, like those that have watered the fields of Hama on the Orontes since Roman times: the river turns it and it lifts the river. The current drags on the paddles dipping into it; buckets in the rim fill at the bottom and tip out at the top into an aqueduct that carries the water off to the fields. The river comes in from upstream (right) at 500 L/s, fills a weir pool, and spills into a stone race that runs 1.5 m/s — worked out from its slope and roughness, not typed in — into the wheel's basin, then leaves over the tailrace. Nothing sets the wheel's speed: it settles where the race's push balances the weight of water it's lifting, about 1.3 rpm. Choke the river and the race slows, the basin drops, and the wheel stalls, as norias do in a dry summer.",
        ["roman-crane"] = "A Roman building crane (Vitruvius X.2): two men walking in a 4.5 m treadwheel turn a 25 cm drum on the same axle, winding a rope over the jib's pulley to lift 580 kg of granite. The wheel and axle is the lever that makes it possible: the men push at 2.25 m, the stone pulls at 0.25 m, so they need only a ninth of its weight. Their ~1,550 N·m just beats the stone's ~1,430 N·m — one man alone couldn't lift it.",
        ["vitruvian-catapulta"] = "A two-armed bolt-shooter proportioned entirely from Vitruvius's table (X.10) — every part a multiple of the spring hole, the hole a ninth of the bolt: here a 69 cm bolt, 7.7 cm hole, 54 cm arms. Each arm turns in an upright spring of twisted sinew; drawn back, the springs twist further. Loosed, they throw the arms forward, the bowstring drives the bolt down the channel, the arms hit their stops and the bolt flies on level at 17.6 m/s (22 m/s while the string drives it), falls the 0.67 m from the trough in 0.37 s and lands about 7 m out, then slides on at 4.4 m/s2 to rest 36 m from where it lay. Vitruvius gives no spring stiffness; 300 N·m per radian is our estimate.",
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
        ["boiler-shells"] = "Three identical pots, 30 cm across with 3 mm walls, each 10 kg of 20 C water over the same 10 kW fire, differing only in what the shell is made of. A thin shell holds P = strength x wall / radius (hoop stress): lead (12 MPa) 0.24 MPa gauge, copper (220 MPa) 4.4 MPa, bronze (350 MPa) 7.0 MPa. All warm along T = 20 + 5000 (1 - exp(-t / 20930 s)); steam in the pot reaches a pressure when water boils at it, so lead gives way at 138.2 C and 501 s, copper at 257 C and 1016 s, bronze at 286 C and 1144 s. Each outline warms to amber and reddens as its pot nears what it holds. Nothing here is forbidden: a lead pot works, for about eight minutes. Try it at 20x.",
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
        ["shaduf"] = "The well sweep of Egypt and Mesopotamia, from about 2000 BC. Each pole pivots 1 m from its counterweight and 3 m from its bucket. The counterweight is sized for a half-full bucket (44 kg: 9.5 kg x 3 m plus the pole's own weight), so the lifter pushes and pulls with the same 74 N either way, against 167 N to lift a full bucket with no counterweight. Middle, half full: it balances and stays level. Front, full: the bucket sinks. Back, empty: it rises.",
        ["baghdad-battery"] = "Clay jars from near Ctesiphon (Parthian or Sasanian) hold a copper tube round an iron rod. Filled with vinegar they make a cell, though whether anyone used them so is doubted: no wires were found and the tops were sealed. Left, one replica as Eggebrecht built it: 0.5 V at 0.15 mA, 75 microwatts, for about 279 days until its 45 mL of vinegar is spent. Right, ten in series as MythBusters wired them: 4.33 V. Front, a jar with a few drops left, spent in 15 hours: watch its iron rod rust (Sleep until, or 20x). The rover's 5 kWh bank would take one jar about 7,600 years, and the vinegar of about 10,000 jars.",
        ["solar-furnace"] = "Glass from sand and sunlight, at noon on Mars (433 W/m2). A target heats until it re-radiates what it gets, sigma (T^4 - T_air^4) = C I. Left, ten flat heliostats on one crucible of basalt: a flat mirror's image is as big as itself, so they reach at best C = 10, and here about 5.6 once each one's angle is counted; it stops near 190 C and never melts basalt (1,200 C). Middle, a 12 m2 burning mirror squeezing its light into 50 cm2 (C near 2,000): it would stop at 1,713 C and melts 10 kg of basalt in 82 minutes into dark glass. Right, a 16 m2 burning mirror melts 2 kg of silica sand (1,700 C) in 15 minutes into clear glass. Try scene.clock-rate 0 to hold the sun at noon. Best at 20x.",
        ["mars-sols"] = "Three sols on Opportunity's plain, from noon on sol 1. A sol is 88,775 s, 24 local hours of 3,699 s. The air follows the day, -80 C before dawn to -20 C at 15:00. All of sol 2 a dust storm blows, optical depth 10.8 as in Opportunity's last storm: the sun's beam through it falls a further e^(-10.5 x air mass), about 1/40,000 at noon, and the sky goes brown. Dust settles on the heliostat, which after the storm reflects e^(-0.5) = 61% of what it did; clean it with mirror.dust 0. The relay orbiter passes at 03:00 and 15:00. Try scene.clock-rate 100 to watch the sols go by.",
        ["airlock"] = "A 60 m3 habitat of 50 kPa air, an 8 m3 chamber and two doors onto Mars. Work it: the air pump draws the chamber back into the habitat (pump running), 50 L/s, down to 5 kPa in 8/0.05 x ln 10 = 368 s, spending 289 kJ as an ideal compressor; then open the 5 cm2 bleed valve (bleed.open 1) and the chamber falls to Mars's 610 Pa as e^(-t/134 s); open the outer door (outer.open 1). Each cycle loses only what was left in the chamber, 8 m3 x (5000 - 610) Pa of air = 0.42 kg, against 4.7 kg vented straight from 50 kPa.",
        ["stove-rooms"] = "One 2 kW charcoal stove, three times. A fire breathes its room's air: charcoal takes 2.67 kg of oxygen a kilogram (C + O2 -> CO2) and goes out below 15% oxygen. Left, a sealed 30 m3 room: 74.2 mol of oxygen above the limit at 5.74 mmol/s, so it goes out after 12,924 s (3.6 h) having burned 0.89 kg, the air 6% CO2; meanwhile it holds the room at 60 C and 115 kPa. Middle, the same room with 5 L/s of outside air blown in and a vent: its oxygen settles at 18.2% and it burns on. Right, outdoors: it burns to the end. Try sealed.supply 5, or stove.oxygen-limit 10. Best at 20x.",
        ["two-modules"] = "Two inflated modules on Mars, each holding 50 kPa of 21% oxygen at 20 C against 610 Pa of CO2 at -63 C outside. Left, sealed: walls losing 20 W/K and a 1.8 kW heater, so it settles at -63 + 1800/20 = 27 C (time constant C/UA = 1283 s); its lift pump works because the air inside holds a column (50000 - 2339)/(1000 x 3.71) = 12.85 m high; a nitrogen locker inside it leaks into it, not onto Mars. Right, punctured by a 1 cm2 hole: its air rushes out at the speed of sound and the pressure falls as e^(-t/2513 s), 24.4 kPa after half an hour, 11.9 after an hour, the dial sinking, until at about 1 kPa the membrane sags. Try punctured.leak 0 to patch it, or sealed.heater 0. Best at 20x.",
        ["earth-machines-on-mars"] = "The same parts and formulas, with Mars's numbers: g = 3.71 m/s2, 610 Pa of air that is 95% CO2, -63 C. The pendulums swing sqrt(9.81 / 3.71) = 1.63 times as slowly (the 1 m one on its bearing in 3.245 s). The lift pump holds no water: the air's 610 Pa is barely more than water's vapour pressure at 0 C (605.6 Pa), so its reach is (610 - 605.6) / (1000 x 3.71) = 1.2 mm. The windmill's wind carries 1/2 rho A v^3 = 596 W through 5 m sails at 10 m/s, 1/79 of Earth's 47 kW, because Mars's air is 0.0152 kg/m3. The kettle boils at 0.1 C, a second after the fire is lit. Try scene.gravity 9.81, or scene.pressure 50 (kPa).",
        ["kongming-lantern"] = "A 1 m3 paper envelope of 50 g with a 20 g, 800 W burner, open at the foot, in 15 C air. Air is lifted as water lifts a float: (rho_out - rho_in) g V, each density rho = P / (R T) at the same pressure. It leaves the ground when the air inside is light enough to carry the 70 g, rho_in <= 1.155 kg/m3, i.e. above 32.46 C: worked by hand, 33.4 s into the burn (heat 800 W less 15 W/K through the paper, on the air plus the paper). It then climbs where air drag balances lift less weight, about 0.65 m/s at 37 C inside. The paper glows warmer as the air heats, and its label gives the temperature inside and the lift against the weight. The twin on the right has no flame and stays put. Tradition names the Kongming lantern for Zhuge Liang (3rd c. AD); that attribution is uncertain, and the physics here does not depend on it. Not modelled: the air a rising lantern drags with it, and wind.",
        ["kongming-lantern-mars"] = "The same lantern on Mars: 610 Pa of CO2 at -63 C is 0.0152 kg/m3, 80 times thinner than Earth's. Even with every bit of air out of it the lift is the weight of 0.0152 kg of air, short of the lantern's 0.070 kg, so however hot the burner makes it (its skin settles at -9.7 C) it stays on the ground. A lantern needs air to float in.",
        ["winter-night"] = "A night at -10 C. A copper of 5 kg of water, taken off the fire at 90 C, cools towards the air by Newton's law: 2 W for every kelvin it is warmer, against 5 x 4186 J/K, so T = -10 + 100 exp(-t / 10465 s) - 60.9 C after an hour, 40.3 C after two. The cistern beside it ices over: each new layer's latent heat has to leave up through the ice already there, so the ice thickens as the square root of time (Stefan, 1891) - 22.8 mm in an hour, twice that in four. The shallow basin's seep evaporates nothing under ice. Try scene.ambient 20, or 30 for a summer's day. Best at 20x.",
        ["steam-engines-mars"] = "Two small Trevithick engines, alike in every part: a double-acting 5 cm cylinder fed from a boiler at 150 C (473 kPa), working a flywheel through a crank. The boiler's steam pushes the piston directly, (P_boiler - P_air) x area the whole stroke, so each stroke does (P_boiler - P_air) A S. The left one exhausts into Mars's 610 Pa: 186 J a stroke. The right one stands in a pressurised hut at 101 kPa, like an engine on Earth: 147 J. The hut is pressurised with Mars's own air, 95% CO2 and 0.2% O2: the engine does not mind, since steam needs no oxygen and its boiler fire here is a plain heat input, but nothing could breathe there, and a real wood fire would go out (see Stoves and Their Air). Newcomen's engine, pushed by the air, fails outright on Mars; this one works better there.",
        ["mars-stirling"] = "Newcomen's engine is pushed by the outside air, which Mars hardly has; a Stirling engine is driven by heat, with Mars's -63 C air as its cold side. Three engines, each under six flat heliostats (1.5 kW on a 1 m2 receiver at noon). The hot end settles where the mirrors' heat equals what it re-radiates plus what its heater passes the engine, K (T - Tc); the engine makes 35% of Carnot, f (1 - Tc/T), of that into work. Left, a small heater: hot, 98 C, but only 74 W. Middle, matched: 40 C and 111 W, the most these mirrors can give. Right, too big: it runs barely above the cold, -28 C, 64 W. Try scene.ambient 20: a warm cold side halves the middle one. Best at 20x.",
        ["battering-rams"] = "Three iron rams (132 kg balls on 2 m pendulums) swing from 30 degrees into posts struck 0.8 m up. A post bends like a spring, k = 3EI/h^3, and stops the blow with F = v sqrt(k m): the stress at its foot is F h c / I. The slim oak post (8 cm) takes 112 MPa against oak's 90 and snaps, the break taking 209 J of the ram's 325; the stout one (14 cm) takes 64 and holds. The limestone column is thick but stone is weak in tension (5 MPa): 74 MPa shatters it, and the break takes only 1.5 J.",
        ["gristmill"] = "Three pairs of 48-inch millstones, each runner turned at 120 rpm from below and set by its miller to resist with 267 N.m: 4.5 horsepower, 3,356 W. Freshly dressed French burr stones grind 54 kg of flour per kWh - 181 kg an hour, the millwrights' 400 lb; dull ones need twice the power for the same, so on the same 4.5 hp the middle pair makes only 80 kg an hour. The right-hand pair's wheel gives only 200 N.m, less than its stones' 267: it never gets them turning. Lighten its stones (weak.grind-torque 150) and it will. The flour heaps up beside each pair.",
        ["rail-wagons"] = "Two iron-wheeled wagons on the same 1 degree grade: one on a limestone road, one with flanged wheels on iron rails. The grade pulls tan 1 = 0.0175 of the weight along it. Rolling on a road takes 0.04 of the load, so the road wagon stays put; iron on iron rail takes only 0.002, so the rail wagon rolls away at g (sin t - C_rr cos t) M / (M + sum I/r^2) = 0.103 m/s2 - a third of the pull goes into turning its heavy flanged wheels.",
        ["carts"] = "Two handcarts and an oak sledge let go on a 10 degree limestone slope. The sledge stays put: oak grips limestone at mu 0.52, and the slope needs only 0.18 to hold it. The carts roll, at g (sin t - C_rr cos t) M / (M + sum I/r^2): the wheels' own turning takes part of the pull, and rolling resistance (C_rr 0.04, a stage coach on a dirt road) the rest. The oak cart on solid wheels runs down at 1.118 m/s2 and slows on the flat at 0.333; the pine cart on spoked wheels at 1.100 and 0.328 - light wheels, but a light bed, so a quarter of it turns.",
        ["hierapolis-sawmill"] = "A water wheel saws stone, as on the relief carved on the sarcophagus of Marcus Aurelius Ammianus at Hierapolis (3rd century AD): the earliest known crank and connecting rod. The relief shows a wheel, a gear train and two frame saws on connecting rods; the cranks are not carved and are inferred, since the rods need them. Here an iron crank on the wheel's own axle draws one iron frame back and forth across a limestone block, its friction standing in for the cutting. The wheel is turned by the water, the crank by the wheel, and the saw's drag slows the wheel: 2.9 L/s onto the 1 m overshot wheel gives 42.7 W at any speed, the frame takes 20.5 N m on average (more than its plain drag, because the leaning rod presses it onto the stone), and the wheel settles near 20 rpm, a 0.5 m stroke at a mean 0.33 m/s. The label under the crank shows the speed, torque and power the wheel passes it.",
        ["geared-brake"] = "Two gear trains, each a flywheel and a 100-tooth gear on one arbor driving a 10-tooth pinion and a brake drum ten times as fast, let go at 36 rpm. Nothing cranks them: the only thing slowing each flywheel is the 0.1 N m brake at the far end, felt ten times over through the mesh. The trains are 1 kg m2 seen from the flywheel, so the plain one stops in 3.77 s, slowing 1 rad/s every second; the other meshes at 90% efficiency, so it feels 1.11 N m and stops in 3.40 s, and only 90% of the flywheel's energy reaches the brake as heat. Labels under the pinions show the speed and the torque each mesh carries.",
        ["crank-slider"] = "A crank disc turning at 60 rpm drives a piston through a 60 cm oak connecting rod: a ball joint at the crank pin, a hinge through the piston's wrist, and the piston's cylinder keeping it upright. With the crank turned theta from pin-down, the piston stands at y_c - r cos(theta) - sqrt(l^2 - r^2 sin^2(theta)) - not a plain sine: at a quarter turn the leaning rod holds it 19 mm below mid-stroke, so it spends longer in the top half of its travel than the bottom. Nothing computes that; the joints and the cylinder do.",
        ["universal-joint"] = "Two shafts meeting at 30 degrees, joined by a Cardan (Hooke's) joint: an iron cross, one arm hinged to each shaft's yoke. The driving shaft turns steadily at 30 rpm; the driven one speeds up and slows down twice a turn, between cos 30 = 0.866 and 1/cos 30 = 1.155 of it, w_out/w_in = cos b / (1 - sin^2 b sin^2 theta). The label over the cross shows the ratio as it swings. Two joints phased to cancel are why a car's drive shaft turns its wheels smoothly.",
        ["drop-test"] = "Four 20 cm cubes let fall 1.25 m onto stone: hardened steel, granite, oak and a bale of hemp. Every strike is recorded and flashes where it lands, with the energy it took: how fast the faces met, and 1/2 m v^2 (1 - e^2) of the block's energy gone to heat, sound and dents. They land together at 4.86 m/s (the engine's 0.1/s damping takes the rest of sqrt(2gh) = 4.95) and leave at e times that: steel 0.95 bounces back to a metre, granite 0.6 to 41 cm, oak 0.5 to 29 cm, hemp 0.1 just thuds. Granite loses the most, 163 J; steel, though three times heavier, only 72 J.",
        ["rope-over-bars"] = "Four fixed oak bars, each with a 173 kg granite block on one side of a hemp rope and a smaller one on the other. Where the rope drags over a bar, the tight side can carry up to e^(mu theta) times the slack side (the capstan equation): 4.44 over half a turn, 87 over a turn and a half. Left to right: half a turn with 21.6 kg (95.9 kg's worth, too little: the rope slides, the pair accelerate at 2.81 m/s2 and the bar glows), half a turn with 72.9 kg (holds), a turn and a half with 0.93 kg (81 kg's worth: slides at 3.57 m/s2), and with 2.7 kg (236 kg's worth: holds). The label over each bar gives its two tensions.",
        ["bar-crane"] = "The Roman crane with its pulley swapped for a fixed oak bar, as ropes ran over beam ends before sheaves. The rope turns 129 degrees over the bar, so lifting, the drum side must pull e^(mu theta) = 2.91 times the stone's 5.7 kN. Two walkers, who lift the stone over a pulley, now pull 6.2 kN at the drum and only 2.1 kN reaches the stone: it stays on the ground. Seven walkers lift it at the wheel's 3 rpm, the drum side carrying 16.7 kN, and the bar glows with the friction's heat.",
        ["capstans"] = "Three oak bollards, each with 200 kg hanging a metre below on a hemp rope, and a sailor holding the other end with 100 N. Friction where rope slides on a post takes off tension in proportion to the tension there, so round the post it falls off exponentially: the tight end carries e^(mu theta) times the slack one (the capstan equation, Euler 1762). Hemp on oak, mu = 0.474. Half a turn multiplies the pull by 4.4 - 444 N, not the load's 1962 N, so it runs out and falls. One turn multiplies it by 19.7 - just enough. Two turns by 388: 5 N would hold it. Try one-turn.hold 99, or haul in over half a turn with half-turn.hold 10000.",
        ["windmills"] = "Two post mills, sails 10 m from hub to tip. The wind carries 1/2 rho A v^3 through the disc they sweep; the sails take a share of it, Cp, that depends on how fast their tips run for the wind - most, 0.3 here, at 2.5 times its speed. No rotor can take more than 16/27 (Betz, 1919): the air behind it has to keep moving. Each miller sets the stones to hold the sails at that best speed: the left, in a 6 m/s breeze, turns at 14.3 rpm and grinds with 12.3 kW; the right, in a 9 m/s wind, at 21.5 rpm with 41.4 kW. Half again the wind, (1.5)^3 = 3.4 times the power. Set the right one's stones light (gale.load 8171) and it races to 33 rpm but takes only 0.21 of the wind.",
        ["bellows-forge"] = "Two forges, each 1 kg of wood at 5 kW. The left burns on its own draught: its steady burn already draws power / density x wood's 6:1 air-fuel ratio, 2 g/s of air, and takes 3000 s to burn the kilogram out. The right has a bellows forcing in 5 L/s more (air's density, 1.204 kg/m3, makes that 6.02 g/s) — 4.01x the air, so 4.01x the burn rate, out in 748 s. Both give up the same 15 MJ; the bellows only ever buys speed, not heat.",
        ["ball-ramp"] = "Two balls roll down a 2 m limestone ramp at 15 degrees: a 12 cm iron one from 1.5 m up the slope and a 6 cm bronze one from 0.5 m. A solid sphere puts 2/7 of the energy it loses into turning, so both gain speed at 5/7 g sin 15 = 1.81 m/s2, whatever their size or mass, where a sliding block would have 2.54. The big ball arrives after 1.29 s at 2.33 m/s, turning at 38.9 rad/s without slipping.",
        ["belt-drive"] = "Two open hemp belts, alike but for how tight they are, join a 10 cm pulley driven by a 1.0 N·m motor to a 20 cm pulley 60 cm away. A belt wrapped 161 degrees carries at most 1.21 times its tension: the loose one (5 N) 0.61 N·m, the tight one (10 N) 1.21 N·m. So the tight belt grips and the big pulley turns at half the small one's speed; the loose belt slips, and the big pulley turns at only 0.09 of it.",
        ["boulder"] = "A 50 cm granite block dropped from 2 m onto the flat floor. Placed on a map (the ground-check world) it falls onto the hill's collision surface and stays where it lands: its friction of 0.6 is ten times the hill's 1-in-17 grade, so it rests with its centre 0.2504 m above the ground under it.",
        ["buried-crate"] = "A part for a world. An oak crate (50 cm, 90 kg) lies with its lid 1 m under loam, and a gang of labourers (1.5 kW) cuts a trench over it a 25 cm spit at a time. Pulling it out takes 14.2 kN under 1 m of cover; it is held until its cover is under a quarter of its height, and the fourth spit (2 m3, 31 kJ, 20.9 s) bares its lid and frees it.",
        ["cargo-crate"] = "One crate of the Lonely Rover's cargo: an oak box 50 cm on a side, 90 kg, standing on Mars, where it weighs 334 N, not the 883 N it would on Earth. The opening world places five of them at the foot of the crater's weakened rim, and the rim's collapse buries whichever it reaches. Dropped a metre, it hits at 2.7 m/s, not 4.4.",
        ["cart-push"] = "A handcart on the flat for a hand to push: an oak bed on four 30 cm disc wheels, 28.5 kg in all. Nothing else moves it. Pushed up to 1 m/s and let go it slows at 0.33 m/s2 (rolling resistance 0.04 of its weight, less what the turning wheels cost) and coasts 1.5 m before it stops. Drag it by its bed to try.",
        ["cistern"] = "A part for a world: a raised cistern, 400 L standing a metre off the ground, with an outlet at its floor and nowhere for the water to go. Alone it just stands full. In the linked-pipe world a pipe runs from its outlet to the trough 4 m away.",
        ["cistern-and-trough"] = "A raised cistern and a trough in one machine, joined by a pipe, doing exactly what the two separate machines joined across a world must. Flow is 2 L/s per metre of head difference. The trough's water reaches its inlet, 10 cm up, at 15.2 s; the cistern holds 229 L at 60 s and 123 L at 120 s, and runs dry at 275 s, leaving 400 L at 80 cm in the trough.",
        ["crane-hoist"] = "The Roman crane's drum, jib, pulley, rope and 583 kg granite block without its treadwheel. Alone nothing turns the drum and the stone rests on the ground. In the split-crane world a shaft joins the drum to the separate treadwheel machine, which turns it at 3 rpm: rope comes in at 7.85 cm/s and the stone asks 1430 N·m of the walkers' 1545.",
        ["crate"] = "A part for a world: an oak crate, 50 cm on a side and 90 kg, standing at the foot of the cliff map's slope. When the cliff collapses it buries the crate.",
        ["crate-tongs"] = "Three pairs of bronze tongs hold iron crates in their jaws, which grip with mu 0.30 from two sides, so a crate of mass m needs a squeeze of m g / 0.6. A 5.07 kg crate in 100 N tongs holds (83% of the limit); a 7.47 kg one slips (it needs 122 N); in 140 N tongs it holds. The light one is let go 1.2 m up and lands in 0.42 s at 4.10 m/s.",
        ["dry-mill"] = "A part for a world: a water mill whose stream has dried up, an overshot wheel with empty buckets set to grind against 8171 N·m, standing still. Joined by a shaft to the free sails in the linked-shaft world, the pair settle at the sails' best tip-speed ratio of 2.5: 14.3 rpm on both, the shaft carrying 8171 N·m and 12.3 kW.",
        ["fall-and-swing"] = "The two oldest checks in mechanics, run in the physics engine. A 10 cm iron block let go 5 m up falls freely at 9.81 m/s2, with no damping. A 1 m iron rod with a ball on its end is let go from 5 degrees and swings with a period of 1.986 s, which is Huygens' 1.98649 s for a physical pendulum.",
        ["falling-stones"] = "Three 10 cm balls let go together 350 m up: a cedar ball, an iron ball, and one that falls through no air at all. Air drag stops the cedar ball at 29.6 m/s (13.9 s to land), the iron ball reaches 75.4 m/s (8.73 s), and the bare ball 82.8 m/s (8.46 s, whatever it is made of). Watch the pole beside them, marked every 100 m.",
        ["field-windmill"] = "A part for a world: a post mill that takes its wind from the map under it. Four 10 m sails take at best 0.3 of the wind's power in Mars's air (0.0152 kg/m3), which at 6 m/s is only 515 W. The stones take 154.5 W at 14.0 rpm, and the thin air makes the sails slow: a time constant of 728 s, so 11.3 rpm after 1200 s.",
        ["floats"] = "Water holds things up. A cork float on a draining cistern rides 5 cm below the surface and sits on the floor once the water is 5 cm deep, at 308 s. In a bath, 20 cm blocks float with their density ratio under: cedar 7.6 cm, pine 10 cm, oak 14.4 cm; iron sinks to the floor.",
        ["free-sails"] = "A part for a world: a post mill with its stones taken out, 10 m sails in a 6 m/s breeze with nothing to grind. Alone it races at tip-speed ratio 5, 28.6 rpm, the sails taking nothing from the wind. In the linked-shaft world its windshaft drives the dry mill's stones.",
        ["hanging-chain"] = "A chain of 40 iron links, 2.5 m long, hung between two hooks 2 m apart. It sags into a catenary, y = a cosh(x/a) with a = 0.8455 m, 0.664 m below the hooks. It weighs 5.93 N a metre, so each hook carries 8.95 N: 5.01 N pulling inward and 7.41 N up.",
        ["hillside-pond"] = "A part for a world: an 8 m3 pond a metre deep on a hillside, behind a shut sluice, with a small cup (a clepsydra) fed by a thin pipe from its floor. The cup fills to 10 cm in 10.5 s and trips the sluice, and the pond empties down a race at first at 437 L/s; the water runs down the valley and pools in the hollow. The sluice can be drawn by hand sooner.",
        ["holy-water"] = "Heron's coin-operated holy water (Pneumatica I.21). A coin on a pan tips a lever, which lifts a plug out of an urn's spout until the pan slides the coin off, at 16.7 degrees. Wide open the 12 mm spout passes 0.178 L/s; the lever is back and the plug seated about 1.5 s after the coin goes down, and about 0.2 L has poured: a coin buys a fixed dose, not a running tap.",
        ["placer-sluice"] = "A placer miner's sluice box: a 2 L/s spring feeds a race running 5 m down a 1-in-100 fall, with crushed ore fed in at 0.1 kg/s, 2% of it gold. The flow drags on its floor with 1.46 Pa, which lifts anything lighter than 7340 kg/m3, so the box keeps all the gold (19300) and washes the sand (2650) on: 2 g of gold a second.",
        ["ratchet-windlass"] = "Four windlasses winding 20 kg granite blocks on 10 cm drums, alike but for their ratchets. With a ratchet and no crank the pawl pushes back with 131 N and the block does not fall; with no ratchet it falls in half a second; with a crank (6 rpm) it rises 6.28 cm/s, a tooth every 30 degrees; and with the pawl lifted it lowers itself at 7.88 m/s2 (not g, the drum turns too).",
        ["roman-crane-reload"] = "The Roman crane with two more stones on the ground beside the first, for a hand to change loads: lift one, let it down, unhook it, drag the hook over another and lift that. The 583 kg stone rises at 7.85 cm/s; the 900 kg one asks 2207 N·m at the drum, more than the two walkers' 1545, so the wheel stalls with it on the ground.",
        ["roman-crane-reload-gang"] = "The Roman crane with spare stones, worked by three walkers (2318 N·m) instead of two. Three are enough for the 900 kg stone (2207 N·m at the drum) as well as the 583 kg one, and reach the treadwheel's 3 rpm limit, so the rope comes in at 7.85 cm/s; two, with the wheel's bearing friction eating 115 N·m, raise the first stone at only 4 cm/s.",
        ["sand-timer"] = "Grain against water as a clock: three hoppers of 5 kg of sand and a water tank of the same footing. Sand through a 10 mm orifice flows at a steady 25.9 g/s whatever the depth, so the level falls linearly and it is empty in 193 s, half left at half time. The water tank, emptying in the same 193 s, has only 25% left at half time. Turn the timer over and the sand runs again; the third hopper's orifice is under five grains wide, so it arches over and gives none.",
        ["trench-crew"] = "A part for a world: twenty labourers (3 kW) cutting a trench 4 m long and 1 m wide in clay, a 25 cm spit at a time, throwing the spoil in a ridge 5 m away. Unshored, the walls stand to 3.19 m, so the spit that takes it to 3.25 m is the one they fall in at. The 13 m3 out costs 634 kJ, 211 s at 3 kW.",
        ["trip-hammer"] = "Two peg wheels each lift a 5 kg hammer 10 cm four times a turn and let it fall on an anvil at 1.40 m/s. The strong wheel (20 N·m, 30 rpm) strikes every 0.5 s; lifting the hammer takes 3.1 N·m on average and 11.9 at the peak. The weak wheel gives only 5 N·m and stalls on the first peg, with the hammer 0.7 cm up and never striking.",
        ["trip-sluice"] = "A tripwire. A 10 cm iron weight hangs 1.5 m up on a cord over a stone catch; a trigger box 95 cm up fires once when the weight's middle enters it. Let go, the weight falls 0.55 m in 0.335 s, at 1.335 s, and the trigger opens a shut sluice: the 300 L pool behind it runs down the race into the reach, at first about 30 L/s.",
        ["trough"] = "A part for a world: an empty trough on the ground, with an inlet 10 cm up its wall. In the linked-pipe world it is filled through a pipe from the raised cistern 4 m away.",
        ["tunnel-test"] = "Continuous collision detection. Two lead bolts, 1.42 kg, fall 299 m onto thin lead planks, arriving at 76.6 m/s: 0.64 m a tick, against 7 cm of plank and bolt. The default bolt is swept along its path, lands on its plank and bounces (e 0.2); the other has fast switched off and goes straight through its plank to the ground a metre below.",
        ["wake-clock"] = "Sleeping until something happens. A spring of 2 L/s runs into an empty 500 L cistern, and the sleep control wakes on named conditions: 50 L at 25 s; both 20 L and 40 L at 20 s; either at 10 s; 50 L unless 30 L is passed first, woken early at 15 s and saying so; and 5000 L, which it never reaches, so it stops at its 60 s limit.",
        ["walkers-wheel"] = "A part for a world: the Roman crane's treadwheel on its own, two men walking inside a 4.5 m wheel that turns at 3 rpm with at most 1545 N·m. Alone it turns nothing but itself. In the split-crane world a shaft joins its axle to the crane hoist's drum. Click the wheel to stop the walkers, or Shift+click to turn them round.",
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
    private Hints _hints = null!;
    private bool _cameraMoved;
    private int _machinesWatched;
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
        HandActions = new LoggedHandActions(this);   // drags and hooks go to the operator log (Main.Operator.cs)
        MachineView.HookChanged += (view, rope, action, load, point) => HandActions.RecordHook(view, rope, action, load, point);   // (Main.Drag.cs)
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
            ApplyHeroicSet(OS.GetEnvironment("HEROIC_SET"));
            if (OS.GetEnvironment("HEROIC_DRAG") is { Length: > 0 } dragText) ParseHeroicDrag(dragText);   // a person's hand, replayed (Main.Drag.cs)
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
        HudTheme.Install(layer);

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
            button.Pressed += () => OpenPart(name);   // a part for a world opens its world (#175); HEROIC_AUTOSELECT still opens it alone, for tests
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

        // first-run hints (#98), above the panels
        _hints = new Hints(() => new Hints.State(MachineOnScreen, _running, _timeScale, _buildMode is not null, _cameraMoved,
                                                 _machinesWatched, _buildMode?.PartCount ?? 0, _buildMode?.LessonStarted ?? false));
        layer.AddChild(_hints);
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
        // long titles ("Glass Walls on Mars (Light and Pressure)") wrap at the panel's width instead of widening the panel past
        // the right edge of the window (machine review 2026-10-07, #190)
        Wrap(_hudTitle);
        col.AddChild(_hudTitle);
        _hudDescription = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0) };
        _hudDescription.AddThemeFontSizeOverride("font_size", 13);
        _hudDescription.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.75f));
        col.AddChild(_hudDescription);
        _hudState = new Label();
        Wrap(_hudState);
        col.AddChild(_hudState);

        col.AddChild(new HSeparator());
        col.AddChild(SectionLabel("Energy", 15));
        _hudEnergy = new Label();
        Wrap(_hudEnergy);
        col.AddChild(_hudEnergy);

        col.AddChild(new HSeparator());
        col.AddChild(SectionLabel("Speed", 15));
        _hudSpeed = new Label();
        Wrap(_hudSpeed);
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

        BuildOperatorSection(col);
        col.AddChild(new HSeparator());
        _hudControls = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Space pause/run · F fire · O operator log · R restart · D details · H hide this panel · P person for scale · L all labels · Esc menu · 1-9 pick a machine\n"
                 + "Drag to orbit · Shift+drag or middle-drag to pan · scroll or pinch to zoom\n"
                 + "Arrows move · Shift+arrows orbit · + / − or Page Up/Down zoom · Home resets the view",
        };
        _hudControls.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.6f));
        _hudControls.AddThemeFontSizeOverride("font_size", 13);
        col.AddChild(_hudControls);
    }

    /// <summary>A panel label that wraps at the info panel's width rather than growing it.</summary>
    private static void Wrap(Label l)
    {
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(340, 0);
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
        ClearRover();   // the player's rover goes with its world (Main.Rover.cs)
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
        // #86: on a large map the whole-ground framing pivots on the map's middle, a kilometre from the
        // action (lonely-rover-opening). Pivot on the placed machines instead, on the ground under them.
        if (_groundSim?.Ground is { } map && world.Placements.Count > 0)
        {
            var (pmin, pmax) = (new Vector2(float.MaxValue, float.MaxValue), new Vector2(float.MinValue, float.MinValue));
            foreach (var p in world.Placements)
            {
                pmin = new Vector2(Mathf.Min(pmin.X, (float)p.At.X), Mathf.Min(pmin.Y, (float)p.At.Z));
                pmax = new Vector2(Mathf.Max(pmax.X, (float)p.At.X), Mathf.Max(pmax.Y, (float)p.At.Z));
            }
            var mid = (pmin + pmax) / 2;
            if ((pmax - pmin).Length() < span / 4)
            {
                _orbit.Pivot = new Vector3(mid.X, (float)map.HeightAt(mid.X, mid.Y), mid.Y);
                _orbit.Distance = Mathf.Max(40, (pmax - pmin).Length() * 1.3f);
                _orbit.Yaw = Mathf.DegToRad(200);
                _orbit.Pitch = Mathf.DegToRad(25);
                _orbit.Apply();
                _homePivot = _orbit.Pivot;
                _homeDistance = _orbit.Distance;
                _homeProfile = new CameraProfile(_camera.GlobalPosition, _orbit.Pivot, 50);   // Home returns here
            }
        }
        if (WorldProfiles.TryGetValue(world.Name, out var worldShot)) ApplyCamera(worldShot);
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
        SpawnRover(world);   // a world that places a rover is the game: the player drives it (Main.Rover.cs)
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
        _machinesWatched++;
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
        CentreInClearArea(name, view);
        CheckFraming(name, view);
        PlaceFigure(view);      // a person for scale (Main.Composition.cs)
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
        StartTrail(view, _follow);   // the flight drawn (Main.Trail.cs)
        _sleep.Refresh();   // this machine's own wake conditions
        StartOperatorRun(view);   // a fresh operator log, and a replay or the blueprint's demo operator (Main.Operator.cs)
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
                Operated = _operatorLog.ToList(), OperatorTaken = _operatorTaken,   // what was done to the machine, in order (Main.Operator.cs)
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
        if (_views.Count == 0 && _current is not null) RestoreOperator(save, _current);   // the operator log goes back with the machine (Main.Operator.cs)
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
        _skyMaterial.SunAngleMax = 25;   // the glow round the sun's disc fades out by 25°
        _skyMaterial.SunCurve = 0.08f;
        var sky = new Sky { SkyMaterial = _skyMaterial };
        AddChild(new WorldEnvironment
        {
            Environment = _environment = new Godot.Environment
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
                // Filmic tonemap (art direction, owner decision 3): sunlit ground and marble roll off
                // gently instead of clipping to flat white, so they keep their shading.
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapWhite = 6,
                // the shadow side of a part about half its lit value: never black, never flat
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightSkyContribution = 1,
                AmbientLightEnergy = 0.6f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
                // Haze toward the horizon: the 2 km floor fades out instead of ending at a seam, and a far
                // machine sits back behind a near one. Thin enough that a bench-sized scene shows none.
                FogEnabled = true,
                FogMode = Godot.Environment.FogModeEnum.Depth,   // begin and end follow the camera (_Process)
                FogDensity = 1,
                FogDepthCurve = 1.6f,
                FogSkyAffect = 0,
                FogAerialPerspective = 0.3f,
            },
        });

        _sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            // shadows for scenes from a bench to a crater: four cascades out to 250 m, blended at the seams
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 250,
            DirectionalShadowBlendSplits = true,
            ShadowBlur = 1.5f,
        };
        AddChild(_sun);
        // Mars's blue aureole round a low sun: a light that only draws in the sky, along the sun's own line
        _halo = new DirectionalLight3D { SkyMode = DirectionalLight3D.SkyModeEnum.SkyOnly, LightAngularDistance = 0.3f, Visible = false };
        AddChild(_halo);
        _sun.RotationDegrees = new Vector3(-50, 30, 0);

        // The ground is an infinite plane (top at y = 0), not a box (#186). It was a
        // 2 km box, 2 m deep so a fast body could not tunnel through it, but Jolt
        // finds a resting contact's normal from the two shapes' support points, and
        // at 1 km from the box's centre the float error in those (~6e-5 m against
        // a ~1 mm penetration) tilted the normal by a few hundredths of a radian:
        // a ball set down on flat ground picked up 0.05 m/s^2 along the floor and
        // rolled 1.5 m in 10 s. A plane is exact, has no edge for a skidding bolt
        // to fall off, and no thickness to tunnel through. The shape sits 1 m above
        // its body so Main.Ground can still sink the whole floor under a map.
        var floor = _floor = new StaticBody3D { Position = new Vector3(0, -1f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D(), Position = new Vector3(0, 1f, 0) });
        _floorMaterial = Shapes.Mat(SkyLook.GroundFor(HeroicInventions.Sim.Planet.Earth, 0.5), roughness: 0.85f, outline: false);   // ShowSky sets it per machine
        floor.AddChild(Shapes.Box(new Vector3(2000, 2f, 2000), _floorMaterial));
        AddChild(floor);

        _camera = new Camera3D();
        AddChild(_camera);
        _orbit = new OrbitCamera(_camera, 0.2f, 200f, MinPitch, MaxPitch);
        _orbit.MovedByPlayer += () => { _follow = null; _cameraMoved = true; };   // the player has taken the camera: stop chasing the missile
    }

    private DirectionalLight3D _sun = null!, _halo = null!;
    private Godot.Environment _environment = null!;
    private float _hazeReach = 3;   // the haze begins this many camera distances out (SkyLook.Fog)
    private ProceduralSkyMaterial _skyMaterial = null!;
    private StandardMaterial3D _floorMaterial = null!;
    private StaticBody3D? _floor;   // sunk beneath a world's map (Main.Ground.cs)
    private (double ambient, bool sunShown, int elevation, int azimuth, HeroicInventions.Sim.Planet? planet, double storm, MachineRuntime? run) _shownSky = (double.NaN, false, 0, 0, null, 0, null);

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
        // the machine is part of the key: the ground is chosen against its parts (SkyLook.GroundFor), so a new machine under
        // the same sky must still get its own ground
        var key = (Math.Round(ambient * 2) / 2, sunShown, sunShown ? (int)Math.Round(sun!.Elevation * 4) : 0, sunShown ? (int)Math.Round(sun!.Azimuth * 4) : 0, planet, Math.Round(storm * 10), run);
        if (key == _shownSky) return;
        _shownSky = key;

        // the sky is a function of the conditions (SkyLook): the studio's fixed sun stands 50° up in clear air
        double elevation = sunShown ? sun!.Elevation : SkyLook.StudioElevation;
        double airMass = sunShown ? sun!.AirMass : 1 / Math.Sin(SkyLook.StudioElevation * Math.PI / 180);
        bool earth = planet.IsEarth;
        var look = SkyLook.Of(planet, elevation, airMass, storm, ambient, PartsValue(run));

        if (sunShown)
        {
            var d = sun!.Direction;
            var toSun = new Vector3((float)d.X, (float)d.Y, (float)d.Z);
            var up = Mathf.Abs(toSun.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
            _sun.LookAtFromPosition(Vector3.Zero, -toSun, up);        // shine from the sun, towards the scene
        }
        else _sun.RotationDegrees = new Vector3(-50, 30, 0);
        _halo.Transform = _sun.Transform;

        _sun.LightColor = look.Light;
        _sun.LightEnergy = look.LightEnergy;
        _sun.Visible = look.LightEnergy > 0.001f;
        _sun.LightAngularDistance = look.SunSizeDeg;
        _halo.LightColor = look.Halo;
        _halo.LightEnergy = look.HaloEnergy;
        _halo.Visible = look.HaloEnergy > 0.001f;
        _skyMaterial.SkyTopColor = look.Zenith;
        _skyMaterial.SkyHorizonColor = look.Horizon;
        _skyMaterial.GroundHorizonColor = look.GroundHorizon;
        _skyMaterial.GroundBottomColor = look.GroundHorizon * 0.8f;
        _environment.FogLightColor = look.Horizon;   // the haze is the horizon's colour, on Mars and at dusk too
        _hazeReach = look.Fog;
        _environment.AmbientLightEnergy = look.AmbientEnergy;
        // at night the sky gives almost nothing, so a faint cool fill takes over and keeps machines in silhouette
        _environment.AmbientLightColor = look.NightAmbient;
        _environment.AmbientLightSkyContribution = 1 - 0.6f * look.NightShare;
        float frost = Mathf.Clamp(-(float)ambient / 5, 0, 1);         // none above 0 °C, white by −5 °C
        var ground = look.Ground;
        // Mars's frost is thin CO2 and water rime: a pale dusting, not an Earth snowfield
        _floorMaterial.AlbedoColor = ground.Lerp(new Color(0.93f, 0.95f, 0.98f), earth ? frost : frost * 0.25f);
        _floorMaterial.Roughness = 0.8f - 0.25f * frost;
    }

    /// <summary>
    /// How light a machine's parts look on average (relative luminance, 0 to 1), for <see cref="SkyLook.GroundFor"/>. A part with
    /// no material, or none at all (an empty scene), counts as mid-toned.
    /// </summary>
    private double PartsValue(MachineRuntime? run)
    {
        // As drawn, each opaque surface weighted by its size: a big pale ramp under two small dark carts makes the machine
        // read pale, so the ground goes dark under it (ball-ramp and carts had a ramp the floor's own colour, machine review
        // #190). Counting each part once, as before, let a wheel weigh as much as the slope it rolls down.
        var view = _current is { } v && IsInstanceValid(v) && v.Runtime == run ? v : null;
        if (view is not null)
        {
            double sum = 0, weight = 0;
            foreach (var node in view.FindChildren("*", "MeshInstance3D", true, false))
            {
                var m = (MeshInstance3D)node;
                if (!m.IsVisibleInTree() || m.MaterialOverride is not StandardMaterial3D mat) continue;
                if (mat.Transparency != BaseMaterial3D.TransparencyEnum.Disabled) continue;
                var size = m.GetAabb().Size * m.GlobalBasis.Scale.Abs();
                double area = 2 * (size.X * size.Y + size.Y * size.Z + size.Z * size.X);
                sum += area * Luminance(mat.AlbedoColor);
                weight += area;
            }
            if (weight > 0) return sum / weight;
        }
        var values = run?.Def.Parts.Where(p => !string.IsNullOrEmpty(p.Material)).Select(p => Luminance(Skins.ColorOf(p.Material))).ToList();
        return values is { Count: > 0 } ? values.Average() : 0.5;
    }

    // as the eye weighs it: bronze #CC8F4A is mid-toned (0.6), though its HSV value says 0.8
    private static double Luminance(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    // Radians per pixel dragged, and the pitch range that keeps the camera
    // from flipping over the top or bottom of its orbit.
    private const float MinPitch = -1.4f, MaxPitch = 1.4f; // ≈ ±80°

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_buildMode is not null) return; // build mode handles its own camera, keys and clicks
        if (RoverInput(@event)) return;   // the game: the rover's keys (Main.Rover.cs)
        if (!RoverIsPlayer && AimInput(@event)) return;   // dragging a mirror's spot, Alt+click, a click on the ground for a digger (Main.Aim.cs): a machine run's; the rover digs with its own backhoe
        OperateInput(@event);   // hover, click-to-operate, right-click list (Main.Operate.cs); consumes nothing. In the game every action passes the rover's capability check (#163)
        if (HandleHandInput(@event)) return;   // a press on a dynamic body drags it instead of orbiting (Main.Drag.cs)
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
                    else if (!RoverIsPlayer) FocusMachineAt(mb.Position);   // (in the game the rover's hand focuses what it touches, without moving the follow camera)
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
                OperateFire();   // sets each fire and logs it (Main.Operator.cs)
                break;
            case Key.O:
                ToggleOperatorLog();
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
            case Key.P:
                ToggleFigure();   // the scale figure
                break;
            case Key.L:
                ToggleAllLabels();   // every label, or only those that fit without overlapping (Main.Labels.cs)
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
            case "pick": PrintPick(new Vector2(float.Parse(w[1]), float.Parse(w[2]))); return ScriptedInput.Step.Next;   // which part is drawn at that pixel (#151)
            case "pickworld": PrintPick(_camera.UnprojectPosition(new Vector3(float.Parse(w[1]), float.Parse(w[2]), float.Parse(w[3])))); return ScriptedInput.Step.Next;   // ... or where that point of the world is drawn
        }
        return OperatorStep(w) ?? ClickStep(w) ?? AimStep(w) ?? RoverStep(w);   // aim spots and the digger (Main.Aim.cs); operate / waitsim (Main.Operator.cs), hovered (Main.Operate.cs)
    }

    public override void _Process(double delta)
    {
        bool roverDrives = RoverProcess(delta);   // the game: held keys drive the rover, the camera follows it (Main.Rover.cs)
        if (_buildMode is null && !roverDrives) _orbit.ProcessKeys(delta, GetViewport());
        // the haze stays behind whatever the camera is looking at, at any scale
        float d = Mathf.Max(_orbit.Distance, 2);
        _environment.FogDepthBegin = d * _hazeReach;
        _environment.FogDepthEnd = d * _hazeReach * 6;
        _inputScript?.Process(delta);
        OperateHoverTick(delta); OperatePanelTick();   // (in the game the tooltip says why the rover can't)
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
        if (_running && !_sleep.Active) PreStepHand();   // a hand holding a body sets its target for this step (Main.Drag.cs)
        if (_sleep.Active)
            _sleep.Advance();                 // sleeping: run ahead as fast as it can, in place of stepping in real time
        else if (_running && _views.Count > 0)
            StepWorld(delta); // a world: every machine, stepped together, and the links between them
        else if (_running && _current is not null)
            _current.Simulate(delta); // already scaled: see SetSpeed
        PostStepHand();

        ApplyDueSettings(); // after the step, as SimHost's ApplyDue is: a setting due at t is seen by the sample taken at t
        if (_audit && _running && _current is not null) _current.AuditTick(delta);

        if (_quitAfterSimSeconds is { } limit && _current is not null && _current.Runtime.Time >= limit)
        {
            if (_audit)
            {
                GD.Print(_current.AuditReport());
                foreach (var v in _views.Where(v => v != _current)) GD.Print(v.AuditReport());   // a world: every machine in it (#151)
            }
            if (_debugPhysics) GD.Print($"[final] {_current.Details}");
            _current.StopTrace();
            foreach (var v in _views) v.StopTrace();
            _linksView?.StopTrace();
            ReportUnappliedSettings();
            WriteOperatorOutputs();
            ReportUnappliedDrags();
            GetTree().Quit(_heroicSetFailed ? 1 : 0);
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
        KeepActionInFrame();   // a ball off the ramp's foot, a cart along the floor (Main.Framing.cs)
        DrawTrail(delta);
        ApplySilhouette(_current);   // review mode only (HEROIC_SILHOUETTE)
        DeclutterLabels();
        if (_figure is not null && (_current is null || !IsInstanceValid(_current))) { _figure.QueueFree(); _figure = null; }
        ShowSky(_current?.Runtime);
        UpdateInfoPanel();
    }

    private void UpdateInfoPanel()
    {
        if (RoverIsPlayer) return;   // the rover's own panel is kept by Main.Rover.cs (#196)
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
