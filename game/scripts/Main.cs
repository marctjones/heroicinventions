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
/// </summary>
public partial class Main : Node3D
{
    private const string MachinesDir = "res://machines";

    private static readonly CameraProfile MenuCamera = new(new Vector3(0, 1.4f, 3.0f), new Vector3(0, 0.6f, 0), 60);
    private static readonly Dictionary<string, CameraProfile> Profiles = new()
    {
        ["aeolipile"] = new(new Vector3(0, 0.55f, 0.85f), new Vector3(0, 0.32f, 0), 42),
        ["herons-fountain"] = new(new Vector3(0, 1.3f, 2.2f), new Vector3(0, 0.6f, 0), 45),
        ["material-samples"] = new(new Vector3(0, 0.7f, 1.6f), new Vector3(0, 0.3f, 0.3f), 45),
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
    };
    private static readonly (double Scale, string Label)[] Speeds =
        [(0.1, "0.1×"), (0.25, "0.25×"), (1, "1×"), (5, "5×"), (10, "10×"), (20, "20×")];
    private static readonly Dictionary<string, double> DefaultSpeeds = new()
    {
        ["aeolipile"] = 5,
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
        ["fire-and-water"] = "Fire and Water",
        ["water-wheels"] = "Water Wheels",
        ["heron-temple-doors"] = "Heron's Temple Doors",
        ["bearing-friction"] = "Bearing Friction",
    };

    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["aeolipile"] = "Hero of Alexandria's steam turbine (c. 50 AD). A fire boils water in the sealed kettle; escaping steam jets from two bent nozzles on the sphere, spinning it by reaction — the same principle as a rocket or a lawn sprinkler.",
        ["herons-fountain"] = "Hero of Alexandria's fountain (c. 50 AD). Water draining from the basin into the sealed receiver compresses the air trapped inside. That compressed air pushes water from the supply vessel back up through a nozzle — higher than its own source — with no pump.",
        ["material-samples"] = "Four identical cubes of different materials, dropped from different heights. Shows how density and friction differ by material — nothing here is scripted, it's real physics reading real material properties.",
        ["pendulum-demo"] = "A classic compound pendulum, released from 40° and left to swing. Demonstrates the exchange between potential energy (height) and kinetic energy (speed) that every mechanical clock and metronome relies on.",
        ["lever-demo"] = "A see-saw: two different weights on either end of a beam pivoted at its centre. The heavier side sinks until the beam's own mechanical stop holds it — a direct demonstration of torque and leverage.",
        ["inclined-plane-demo"] = "Galileo's classic experiment: identical-size blocks of different materials released on the same slope. Whether each one slides — and how far — depends only on its material's friction against stone.",
        ["newtons-cradle"] = "Five identical pendulums hung in a touching row. Pull one end back and release it: momentum and energy transfer through the row via collision, animating the far ball instead.",
        ["trebuchet"] = "A counterweight trebuchet (medieval, but Archimedes' lever taken as far as it goes). A 73 kg counterweight hangs on a chain from the short arm; the long arm carries a sling, with the stone lying on the ground behind. The falling weight whips the arm over, the sling whips the stone round faster still, and it flies ~6 m forward. Only rope, hinge and gravity — nothing scripts the flight.",
        ["torsion-catapult"] = "The onager, a late-Roman one-armed stone-thrower (Ammianus Marcellinus, 4th c. AD). Its arm stands in a horizontal skein of twisted sinew — a torsion spring — winched down level. Loosed, the skein flings the arm up against a padded crossbeam near upright, and the stone, in a sling at the tip, whips round and flies ~15 m. The skein's stiffness is our estimate; Ammianus gives none.",
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
        ["sluice-demo"] = "A sluice gate on a mill race. A 20 L/s spring fills a head pool; a wooden gate 1 m tall, raised 5 cm, lets the water out under its lower edge as a jet, Q = 0.6 x slot area x sqrt(2 g h). All the spring must pass the slot, so the pool rises until the water stands 25.2 cm over the slot's middle. Below, the race runs into a reach that drains over a floor-level outfall into a pond. Shut the gate (gate.opening 0) and the race runs dry at once, the reach drains away over two minutes, and the pool backs up and spills over its waste weir.",
    };

    private MaterialLibrary _materials = null!;
    private readonly SortedDictionary<string, string> _machineFiles = []; // name → res:// path
    private readonly Dictionary<string, MachineView> _byName = [];        // just the current one, for the live link
    private MachineView? _current;
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

    public override void _Ready()
    {
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

        if (OS.GetEnvironment("HEROIC_EDITOR") == "1") SelectBuildMode();
        if (double.TryParse(OS.GetEnvironment("HEROIC_EDITOR_QUIT_AFTER_SECONDS"), System.Globalization.CultureInfo.InvariantCulture, out double editorQuit))
            _editorQuitAfterSeconds = editorQuit;

        if (double.TryParse(OS.GetEnvironment("HEROIC_SPEED"), System.Globalization.CultureInfo.InvariantCulture, out double speed))
            SetSpeed(speed);

        if (double.TryParse(OS.GetEnvironment("HEROIC_QUIT_AFTER_SIM_SECONDS"), System.Globalization.CultureInfo.InvariantCulture, out double quitAfter))
            _quitAfterSimSeconds = quitAfter;
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

        var panel = _leftPanel = new PanelContainer { Position = new Vector2(20, 20) };
        layer.AddChild(panel);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) };
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);

        var title = new Label { Text = "Heroic Inventions" };
        title.AddThemeFontSizeOverride("font_size", 22);
        col.AddChild(title);

        col.AddChild(new HSeparator());

        foreach (var (name, _) in _machineFiles)
        {
            var button = BigButton(DisplayNames.GetValueOrDefault(name, name));
            button.Pressed += () => SelectMachine(name);
            col.AddChild(button);
        }

        col.AddChild(new HSeparator());

        var buildModeButton = BigButton("Build Mode");
        buildModeButton.Pressed += SelectBuildMode;
        col.AddChild(buildModeButton);

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

        col.AddChild(new HSeparator());
        col.AddChild(new Label { Text = "Window size" });
        var sizeRow = new HBoxContainer();
        col.AddChild(sizeRow);
        foreach (var (w, h, label) in WindowSizes)
        {
            var b = new Button { Text = label };
            b.Pressed += () => SetWindowSize(w, h);
            sizeRow.AddChild(b);
        }
        var fullscreenButton = new Button { Text = "Fullscreen", ToggleMode = true };
        fullscreenButton.Pressed += () => ToggleFullscreen(fullscreenButton.ButtonPressed);
        col.AddChild(fullscreenButton);

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
        var panel = new PanelContainer();
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
            Text = "Space pause/run · F fire · R restart · D details · Esc menu · 1-9 pick a machine\nDrag to orbit · scroll to zoom",
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

    private void SelectMachine(string name)
    {
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
    }

    private void RestartCurrent()
    {
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
        _current?.QueueFree();
        _current = null;
        _currentName = null;
        _byName.Clear();
        SetRunning(false);

        _leftPanel.Visible = false;
        _buildMode = new BuildMode(_materials);
        _buildMode.RunRequested += RunBuiltMachine;
        AddChild(_buildMode);
        ApplyCamera(MenuCamera with { Eye = new Vector3(0, 3, 4), LookAt = new Vector3(0, 0.3f, 0) });
    }

    private void DeselectBuildMode()
    {
        _buildMode?.QueueFree();
        _buildMode = null;
        _leftPanel.Visible = true;
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
    }

    private void DeselectMachine()
    {
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
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _current?.SetFrozen(!running);
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
        _orbitPivot = profile.LookAt;
        _orbitFov = profile.FovDegrees;
        var offset = profile.Eye - profile.LookAt;
        _orbitDistance = offset.Length();
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
        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
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

        var sun = new DirectionalLight3D { ShadowEnabled = true };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-50, 30, 0);

        // Big enough for a trebuchet's stone, or a catapult bolt skidding on, to land on.
        var floor = new StaticBody3D { Position = new Vector3(0, -0.05f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, 0.1f, 100) } });
        floor.AddChild(Shapes.Box(new Vector3(100, 0.1f, 100), Shapes.Mat(Shapes.Stone)));
        AddChild(floor);

        _camera = new Camera3D();
        AddChild(_camera);
    }

    // Radians per pixel dragged, and the pitch range that keeps the camera
    // from flipping over the top or bottom of its orbit.
    private const float OrbitSensitivity = 0.008f;
    private const float MinPitch = -1.4f, MaxPitch = 1.4f; // ≈ ±80°

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key.Keycode);
                break;

            // Drag with the left or right mouse button to orbit — right
            // works too since the left often lands on a UI button instead.
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } mb:
                _dragging = mb.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp }:
                _orbitDistance = Mathf.Max(0.2f, _orbitDistance * 0.9f);
                UpdateOrbitCamera();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown }:
                _orbitDistance = Mathf.Min(20f, _orbitDistance / 0.9f);
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
            case Key.Escape when _current is not null:
                DeselectMachine();
                break;
            case Key.D when _current is not null:
                _detailsButton.EmitSignal(BaseButton.SignalName.Pressed);
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

    public override void _PhysicsProcess(double delta)
    {
        if (_running && _current is not null)
            _current.Simulate(delta); // already scaled: see SetSpeed

        if (_audit && _running && _current is not null) _current.AuditTick(delta);

        if (_quitAfterSimSeconds is { } limit && _current is not null && _current.Runtime.Time >= limit)
        {
            if (_audit) GD.Print(_current.AuditReport());
            if (_debugPhysics) GD.Print($"[final] {_current.Details}");
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

        UpdateInfoPanel();
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
