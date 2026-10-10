using Godot;
using HeroicInventions.Sim.Game;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Where things are (#235): the rough areas of the rover's cargo in the world (#236, CargoMarkers.cs), the bearing and distance to a chosen
/// one in the Driving section (#239) and the navigation map (#237, NavMap.cs). Owner decision on #240: the rover knows roughly where each
/// crate landed in the slide, never its depth; finding a crate still takes digging. So nothing here is anchored at a crate's true place: the
/// discs, the names, the bearing line and the map all use the disc's centre (<see cref="RoughArea"/>), and no text says how deep a crate lies
/// or under what. The cargo is every placed crate of a rover world (cargo-crate, found-bank, found-motor); the player's own machines are
/// drawn on the map from the placements that are not cargo.
/// Keys: M opens and closes the map (Esc closes it, Home centres it on the rover), T chooses the next cargo area for the bearing line (and
/// then none). View menu: Cargo Markers turns the areas and the bearing line off (remembered in settings.cfg, section [markers]).
/// Hooks: Main._Process calls <see cref="MarkersProcess"/>, Main._UnhandledInput <see cref="MarkersInput"/>, Main.RunViewStep <see cref="MarkerStep"/>,
/// BuildRoverPanels adds <see cref="BuildMarkRow"/> after the "ahead:" line, the View menu adds the toggle.
/// </summary>
public partial class Main
{
    private static readonly string[] CargoMachines = ["cargo-crate", "found-bank", "found-motor"];

    private bool _markersOn = MarkerSettings.Enabled();
    private CargoMarkers? _cargoNode;
    private NavMap? _navMap;
    private WorldDef? _markersWorld;
    private IReadOnlyList<Marker> _markerList = [];
    private Marker? _testMarker;
    private string? _chosenMarker;
    private Label? _markText;
    private BearingArrow? _markArrow;

    private void EnsureMarkerNodes()
    {
        if (_cargoNode is null)
        {
            _cargoNode = new CargoMarkers
            {
                Name = "CargoMarkers", Ground = () => _groundSim?.Ground, Markers = () => _markerList, Chosen = () => _chosenMarker,
                Visible3D = () => _markersOn && RoverIsPlayer,
            };
            AddChild(_cargoNode);
        }
        if (_navMap is null)
        {
            var layer = new CanvasLayer { Name = "MapLayer", Layer = 0 };   // under the HUD's layer: hints and toasts stay on top of it
            AddChild(layer);
            _navMap = new NavMap
            {
                Name = "NavMap", Ground = () => _groundSim?.Ground, Markers = () => _markerList, ShowMarkers = () => _markersOn,
                Rover = () => RoverIsPlayer ? (_rover!.Chassis.GlobalPosition.X, _rover.Chassis.GlobalPosition.Z, Compass.Bearing(_rover.Forward.X, _rover.Forward.Z)) : null,
                Machines = PlayerMachinesOnMap, Chosen = () => _chosenMarker, Choose = id => _chosenMarker = id, Area = MapArea,
            };
            layer.AddChild(_navMap);
        }
    }

    /// <summary>The map's rectangle: the clear area between the panels, under the menu bar, with a margin.</summary>
    private Rect2 MapArea()
    {
        var clear = SettledClearArea();
        float top = 30 + _menuInset;
        return new Rect2(clear.Position.X + 10, top, clear.Size.X - 20, clear.Size.Y - top - 12);
    }

    /// <summary>The placed machines that are not cargo: the player's own (a built machine, a vault), as footprints on the ground.</summary>
    private IReadOnlyList<(string Name, double X0, double Z0, double X1, double Z1)> PlayerMachinesOnMap()
    {
        var list = new List<(string, double, double, double, double)>();
        if (_world is null) return list;
        foreach (var p in _world.Placements)
        {
            if (p.Built is null && CargoMachines.Contains(p.Machine)) continue;   // the cargo is shown as its rough area, never as a machine
            if (!_byName.TryGetValue(p.Label, out var view) || !IsInstanceValid(view) || PartsBox(view) is not { } box) continue;
            list.Add((p.Label, box.Position.X, box.Position.Z, box.End.X, box.End.Z));
        }
        return list;
    }

    /// <summary>The crates' true places (physics server state, as BankCrateReading reads the bank's), by placement label.</summary>
    private List<(string Label, double X, double Z)> CargoPlaces()
    {
        var places = new List<(string, double, double)>();
        if (_world is null) return places;
        foreach (var p in _world.Placements)
        {
            if (p.Built is not null || !CargoMachines.Contains(p.Machine) || !_byName.TryGetValue(p.Label, out var view) || !IsInstanceValid(view)) continue;
            if (view.BodyNamed("crate") is not { } body || !IsInstanceValid(body)) continue;
            var at = PhysicsServer3D.BodyGetDirectState(body.GetRid()).Transform.Origin;
            places.Add((p.Label, at.X, at.Z));
        }
        return places;
    }

    /// <summary>Once a frame: makes the nodes, lists the markers (a disc for each crate, at its place now), updates the Driving section's line.</summary>
    private void MarkersProcess()
    {
        EnsureMarkerNodes();
        if (_world != _markersWorld) { _markersWorld = _world; _navMap!.SetOpen(false); _navMap.Reset(); _chosenMarker = null; _testMarker = null; }
        if (_buildMode is not null && _navMap!.IsOpen) _navMap.SetOpen(false);   // build mode has the screen
        var list = new List<Marker>();
        if (RoverIsPlayer)
        {
            var places = CargoPlaces();
            foreach (var (label, x, z) in places)
            {
                var disc = RoughArea.For(label, x, z);
                list.Add(new Marker(label, RoughArea.Name(label), disc.X, disc.Z, disc.Radius));
            }
            if (_testMarker is not null) list.Add(_testMarker);
        }
        _markerList = list;
        if (_chosenMarker is not null && list.All(m => m.Id != _chosenMarker)) _chosenMarker = null;
        UpdateMarkReadout();
    }

    // ---- the Driving section's line (#239) ------------------------------------------------------------------------

    /// <summary>The chosen marker's line and arrow, built into the Driving section after "ahead:" (BuildRoverPanels).</summary>
    private Control BuildMarkRow()
    {
        var row = new HBoxContainer { Name = "Marker" };
        row.AddThemeConstantOverride("separation", 8);
        _markText = new Label { Name = "MarkerText", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _markText.AddThemeFontSizeOverride("font_size", 15);
        _markArrow = new BearingArrow { CustomMinimumSize = new Vector2(36, 36) };
        row.AddChild(_markText);
        row.AddChild(_markArrow);
        return row;
    }

    /// <summary>The distance, bearing and relative angle from the rover to a marker's centre now, or null with no rover or marker.</summary>
    private (double Distance, double Bearing, double Heading, double Relative)? MarkerReading(Marker marker)
    {
        if (!RoverIsPlayer) return null;
        var at = _rover!.Chassis.GlobalPosition; var f = _rover.Forward;
        var (distance, bearing) = Compass.To(at.X, at.Z, marker.X, marker.Z);
        double heading = Compass.Bearing(f.X, f.Z);
        return (distance, bearing, heading, Compass.Relative(bearing, heading));
    }

    private void UpdateMarkReadout()
    {
        if (_markText is null || !IsInstanceValid(_markText)) return;
        var chosen = _markerList.FirstOrDefault(m => m.Id == _chosenMarker);
        if (!_markersOn) { _markText.Text = "marker: off (View ▸ Cargo Markers)"; _markArrow!.Show(null); return; }
        if (chosen is null || MarkerReading(chosen) is not { } r)
        {
            _markText.Text = _markerList.Count == 0 ? "marker: none" : "marker: none chosen · T picks a cargo area, M opens the map";
            _markArrow!.Show(null);
            return;
        }
        string away = r.Distance < 10 ? $"{r.Distance:0.0} m" : $"{r.Distance:0} m";
        _markText.Text = $"marker: {chosen.Name}\n{away} · bearing {r.Bearing:000}° {Compass.Point(r.Bearing)} · {RelativeWords(r.Relative)}";
        _markArrow!.Show(r.Relative);
    }

    /// <summary>"dead ahead", "12° right", "behind".</summary>
    private static string RelativeWords(double rel)
    {
        double a = Math.Abs(rel);
        if (a < 3) return "dead ahead";
        if (a > 177) return "behind";
        return $"{a:0}° {(rel > 0 ? "right" : "left")}";
    }

    // ---- keys -----------------------------------------------------------------------------------------------------

    /// <summary>M, T, and the map's Esc and Home. True when the key was used.</summary>
    private bool MarkersInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key || key.CtrlPressed || key.MetaPressed || key.AltPressed || Typing()) return false;
        EnsureMarkerNodes();
        switch (key.Keycode)
        {
            case Key.M when !key.Echo:
                if (_groundSim is null) return false;
                _navMap!.SetOpen(!_navMap.IsOpen);
                break;
            case Key.Escape when _navMap!.IsOpen:
                _navMap.SetOpen(false);
                break;
            case Key.Home when _navMap!.IsOpen:
                _navMap.Recentre();
                break;
            case Key.T when RoverIsPlayer && !key.Echo:
                CycleMarker();
                break;
            default:
                return false;
        }
        GetViewport().SetInputAsHandled();
        return true;
    }

    /// <summary>The next cargo area, in the world's order, then none.</summary>
    private void CycleMarker()
    {
        if (!_markersOn) return;
        int at = _markerList.ToList().FindIndex(m => m.Id == _chosenMarker);
        _chosenMarker = at + 1 < _markerList.Count ? _markerList[at + 1].Id : null;
    }

    private void SetMarkersOn(bool on)
    {
        _markersOn = on;
        MarkerSettings.Save(on);
    }

    // ---- scripted checks (tools/gui-check.sh, racket/heroic/tests/markers-test.rkt) -------------------------------------

    /// <summary>
    /// "marker" prints each marker and the chosen one's reading; "marker place X Z" puts an exact test marker there and chooses it; "marker next"
    /// cycles; "marker choose ID"; "marker clear". "markers on|off" (not saved). "map open|close|print|follow|centre X Z [MPP]|zoom MPP".
    /// </summary>
    private ScriptedInput.Step? MarkerStep(string[] w)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (w[0] == "markers" && w.Length == 2) { _markersOn = w[1] == "on"; return ScriptedInput.Step.Next; }
        if (w[0] == "marker")
        {
            EnsureMarkerNodes();
            if (w.Length == 4 && w[1] == "place")
            {
                _testMarker = new Marker("test", "test marker", double.Parse(w[2], inv), double.Parse(w[3], inv), 0, false);
                _chosenMarker = "test"; MarkersProcess();
                return ScriptedInput.Step.Next;
            }
            if (w.Length == 2 && w[1] == "next") { MarkersProcess(); CycleMarker(); return ScriptedInput.Step.Next; }
            if (w.Length == 3 && w[1] == "choose") { MarkersProcess(); _chosenMarker = w[2]; return ScriptedInput.Step.Next; }
            if (w.Length == 2 && w[1] == "clear") { _testMarker = null; _chosenMarker = null; return ScriptedInput.Step.Next; }
            MarkersProcess();
            double t = _views.Count > 0 ? _views[0].Runtime.Time : 0;
            var places = CargoPlaces();
            foreach (var m in _markerList)
            {
                string line = $"[marker] {m.Id}: centre ({m.X:F2} {m.Z:F2}) radius {m.Radius:F1}";
                if (places.FirstOrDefault(p => p.Label == m.Id) is { Label: not null } c)
                {
                    var (dx, dz) = RoughArea.Offset(m.Id);
                    double off = Math.Sqrt(dx * dx + dz * dz);
                    // the crate's place worked back from the disc (the disc is the place plus a fixed offset): for the trace-match check
                    line += $" crate ({m.X - dx:F2} {m.Z - dz:F2}) offset {off:F2} inside {(off < m.Radius ? "yes" : "no")} t {t:F1}";
                }
                GD.Print(line);
            }
            if (_markerList.FirstOrDefault(m => m.Id == _chosenMarker) is { } chosen && MarkerReading(chosen) is { } r)
                GD.Print($"[marker] target {chosen.Id}: distance {r.Distance:F2} m bearing {r.Bearing:F1} deg heading {r.Heading:F1} deg relative {r.Relative:F1} deg · {_markText?.Text.Replace("\n", " / ")}");
            else GD.Print("[marker] target none");
            return ScriptedInput.Step.Continue;
        }
        if (w[0] != "map") return null;
        EnsureMarkerNodes();
        var map = _navMap!;
        switch (w.Length > 1 ? w[1] : "print")
        {
            case "open": map.SetOpen(true); return ScriptedInput.Step.Next;
            case "close": map.SetOpen(false); return ScriptedInput.Step.Next;
            case "follow": map.Recentre(); return ScriptedInput.Step.Next;
            case "zoom" when w.Length == 3: map.SetScale(double.Parse(w[2], inv)); return ScriptedInput.Step.Next;
            case "centre" when w.Length >= 4:
                map.Centre(double.Parse(w[2], inv), double.Parse(w[3], inv), w.Length > 4 ? double.Parse(w[4], inv) : null);
                return ScriptedInput.Step.Next;
            case "print":
                MarkersProcess();
                var f = map.Frame; var area = map.GetRect();
                GD.Print($"[map] {(map.IsOpen ? "open" : "closed")} rect ({area.Position.X:F0} {area.Position.Y:F0} {area.End.X:F0} {area.End.Y:F0}) clear ({SettledClearArea().Position.X:F0} {SettledClearArea().End.X:F0}) centre ({f.CentreX:F2} {f.CentreZ:F2}) {f.MetresPerPixel:F3} m/px");
                foreach (var m in _markerList)
                {
                    var (sx, sy) = f.ToScreen(m.X, m.Z);
                    var (bx, bz) = f.ToWorld(sx, sy);
                    GD.Print($"[map] {m.Id}: world ({m.X:F3} {m.Z:F3}) map ({sx:F1} {sy:F1}) back ({bx:F3} {bz:F3})");
                }
                if (RoverIsPlayer)
                {
                    var p = _rover!.Chassis.GlobalPosition; var (sx, sy) = f.ToScreen(p.X, p.Z);
                    GD.Print($"[map] rover: world ({p.X:F3} {p.Z:F3}) map ({sx:F1} {sy:F1}) heading {Compass.Bearing(_rover.Forward.X, _rover.Forward.Z):F1}");
                }
                return ScriptedInput.Step.Continue;
        }
        return null;
    }
}

/// <summary>Where the chosen marker lies relative to the rover's nose: a ring with an arrow turned clockwise by the angle (up is dead ahead), or nothing.</summary>
public partial class BearingArrow : Control
{
    private double? _relative;

    public void Show(double? relative)
    {
        if (relative == _relative) return;
        _relative = relative;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var c = Size / 2; float r = Mathf.Min(c.X, c.Y) - 2;
        DrawArc(c, r, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.35f), 1.5f, true);
        if (_relative is not { } rel) return;
        double a = rel * Math.PI / 180;
        var fwd = new Vector2((float)Math.Sin(a), -(float)Math.Cos(a)); var side = new Vector2(-fwd.Y, fwd.X);
        var arrow = new[] { c + fwd * (r - 2), c - fwd * (r * 0.6f) + side * (r * 0.5f), c - fwd * (r * 0.25f), c - fwd * (r * 0.6f) - side * (r * 0.5f) };
        DrawColoredPolygon(arrow, new Color(1f, 0.78f, 0.2f));
        DrawPolyline([.. arrow, arrow[0]], new Color(0.02f, 0.03f, 0.05f), 1.5f, true);
    }
}

/// <summary>
/// Whether the cargo markers show: View ▸ Cargo Markers, remembered in section [markers] of the settings file (settings.cfg; HEROIC_SETTINGS names
/// another). Since #248 every writer of that file loads it, sets its own keys and saves, so the section survives the hints. Before that the choice
/// lived in <c>markers.cfg</c> (HEROIC_SETTINGS plus ".markers"): when the settings file has no [markers] value yet, that old file's value is read once
/// and copied into the settings file, so a saved choice is not lost. The old file is left in place and never read again after that.
/// </summary>
public static class MarkerSettings
{
    private static string Path => OS.GetEnvironment("HEROIC_SETTINGS") is { Length: > 0 } p ? p : "user://settings.cfg";
    private static string LegacyPath => OS.GetEnvironment("HEROIC_SETTINGS") is { Length: > 0 } p ? p + ".markers" : "user://markers.cfg";

    /// <summary>HEROIC_MARKERS=1/0 decides; a scripted or headless run shows them; a player's choice is read from the settings file (else from the old markers.cfg once, and moved).</summary>
    public static bool Enabled()
    {
        switch (OS.GetEnvironment("HEROIC_MARKERS"))
        {
            case "1": return true;
            case "0": return false;
        }
        bool scripted = OS.GetEnvironment("HEROIC_INPUT") != "" || OS.GetEnvironment("HEROIC_EDITOR_INPUT") != "" || DisplayServer.GetName() == "headless";
        if (scripted) return true;
        var cfg = new ConfigFile();
        cfg.Load(Path);
        if (cfg.HasSectionKey("markers", "enabled")) return cfg.GetValue("markers", "enabled", true).AsBool();
        var old = new ConfigFile();
        if (old.Load(LegacyPath) == Error.Ok && old.HasSectionKey("markers", "enabled"))
        {
            bool on = old.GetValue("markers", "enabled", true).AsBool();
            Save(on);   // copied once: the settings file now holds the choice
            return on;
        }
        return true;
    }

    /// <summary>Load, set, save in one go; no copy is held between writes.</summary>
    public static void Save(bool on)
    {
        if (OS.GetEnvironment("HEROIC_INPUT") != "" || DisplayServer.GetName() == "headless") return;   // a scripted run leaves the player's settings alone
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("markers", "enabled", on);
        cfg.Save(Path);
    }
}
