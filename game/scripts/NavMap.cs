using Godot;
using HeroicInventions.Sim;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Game;

namespace HeroicInventions;

/// <summary>
/// The navigation map (#237, M): the crater from above, north up (north is -z, east +x, as the wind and the rim are given), over the
/// clear area between the panels. The ground is drawn by a shader from the ground's own heights (<see cref="Terrain.Heights"/>, the
/// array TerrainView builds its mesh from, uploaded as one float per cell when the ground changes: no second heightmap is kept):
/// a hillshade from TerrainView's fixed map light, contours every TerrainView.ContourInterval (every fifth heavier), the slope tint in
/// TerrainView's own colours against the rover's grade limit (warm from 15 degrees, amber from 25, red from the limit), and, where the
/// map has a wind field, the corridor the notch funnels the wind through, shaded by how much stronger the wind is there. Over it
/// (Marks): the rover with its heading, the rough areas of the cargo and their names, the player's machines, a line to the chosen marker, a
/// scale bar and the legend. It reads the game each frame and costs one quad and a few dozen draw calls, nothing else: it does not
/// pause the game, and the arrow keys still drive the rover. Everything goes through <see cref="Frame"/> (a <see cref="MapFrame"/>):
/// the map position of a place is its world position through that one transform.
/// Drag pans (the map then stays where it is), the wheel zooms about the cursor, a click on an area chooses it for the Driving section's
/// bearing line (again to drop it), Home centres on the rover and follows it again.
/// </summary>
public partial class NavMap : Control
{
    public required Func<Terrain?> Ground { get; init; }
    public required Func<IReadOnlyList<Marker>> Markers { get; init; }
    public required Func<bool> ShowMarkers { get; init; }
    public required Func<(double X, double Z, double Compass)?> Rover { get; init; }
    public required Func<IReadOnlyList<(string Name, double X0, double Z0, double X1, double Z1)>> Machines { get; init; }
    public required Func<string?> Chosen { get; init; }
    public required Action<string?> Choose { get; init; }
    public required Func<Rect2> Area { get; init; }

    private const float MinMpp = 0.1f, MaxMpp = 6f;
    private double _cx, _cz, _mpp = 0.8;
    private bool _follow = true, _placed;
    private ColorRect _quad = null!;
    private Control _marks = null!;
    private ShaderMaterial _material = null!;
    private ImageTexture? _heights, _soils;
    private readonly List<KeyEntry> _key = [];
    private byte[] _soilBytes = [];
    private int _shownVersion = -1;
    private double _sinceUpload;
    private Vector2 _pressAt;
    private bool _dragging;
    private Terrain? _terrain;

    /// <summary>The map's frame now: the world point <c>(CentreX, CentreZ)</c> at this control's middle.</summary>
    public MapFrame Frame => new(_cx, _cz, _mpp, Size.X / 2, Size.Y / 2);
    public bool IsOpen => Visible;

    public void Centre(double x, double z, double? metresPerPixel = null)
    {
        _cx = x; _cz = z; _follow = false; _placed = true;
        if (metresPerPixel is { } m) _mpp = Math.Clamp(m, MinMpp, MaxMpp);
    }

    public void Recentre() { _follow = true; }

    /// <summary>Sets the scale, metres per pixel.</summary>
    public void SetScale(double metresPerPixel) => _mpp = Math.Clamp(metresPerPixel, MinMpp, MaxMpp);

    /// <summary>A new world: the next opening fits the map to it again.</summary>
    public void Reset() { _placed = false; _follow = true; }

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        ClipContents = true;
        var shader = new Shader { Code = ShaderCode };
        _material = new ShaderMaterial { Shader = shader };
        _quad = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Material = _material, Color = Colors.White };
        _quad.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_quad);
        _marks = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _marks.SetAnchorsPreset(LayoutPreset.FullRect);
        _marks.Draw += DrawMarks;
        AddChild(_marks);
    }

    /// <summary>Opens or closes the map; opening fits it to the clear area and, if the rover is there, centres on it.</summary>
    public void SetOpen(bool open)
    {
        if (open == Visible) return;
        Visible = open;
        if (!open) return;
        Place();
        _terrain = Ground();
        if (_terrain is null) return;
        if (!_placed)
        {
            var rover = Rover();
            _cx = rover?.X ?? _terrain.X0 + _terrain.Width / 2;
            _cz = rover?.Z ?? _terrain.Z0 + _terrain.Depth / 2;
            _mpp = Math.Max(MinMpp, 220.0 / Math.Min(Size.X, Size.Y));   // about 220 m across the short side
            _follow = rover is not null;
            _placed = true;
        }
        _shownVersion = -1;
        Upload(force: true);
    }

    private void Place()
    {
        var a = Area();
        Position = a.Position; Size = a.Size;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        Place();
        _terrain = Ground();
        if (_terrain is null) { SetOpen(false); return; }
        _sinceUpload += delta;
        Upload(force: false);
        if (_follow && Rover() is { } rover) { _cx = rover.X; _cz = rover.Z; }
        PushUniforms(_terrain);
        _marks.QueueRedraw();
    }

    private void Upload(bool force)
    {
        if (_terrain is null) return;
        if (!force && (_terrain.Version == _shownVersion || _sinceUpload < 0.25)) return;
        _sinceUpload = 0; _shownVersion = _terrain.Version;
        int n = _terrain.Heights.Length;
        var bytes = new byte[n * 4];
        for (int k = 0; k < n; k++) BitConverter.TryWriteBytes(new Span<byte>(bytes, k * 4, 4), (float)_terrain.Heights[k]);
        var image = Image.CreateFromData(_terrain.Nx, _terrain.Nz, false, Image.Format.Rf, bytes);
        if (_heights is null || _heights.GetWidth() != _terrain.Nx || _heights.GetHeight() != _terrain.Nz) _heights = ImageTexture.CreateFromImage(image);
        else _heights.Update(image);
        _material.SetShaderParameter("heights", _heights);
        UploadSoils();
        _material.SetShaderParameter("origin", new Vector2((float)_terrain.X0, (float)_terrain.Z0));
        _material.SetShaderParameter("cell", (float)_terrain.Cell);
        _material.SetShaderParameter("dims", new Vector2(_terrain.Nx, _terrain.Nz));
        _material.SetShaderParameter("interval", TerrainView.ContourIntervalOf(_terrain));
        _material.SetShaderParameter("low", (float)_terrain.Heights.Min());
        _material.SetShaderParameter("high", (float)_terrain.Heights.Max());
    }

    /// <summary>One entry of the ground key: a soil, the colour it is drawn in (TerrainView's, the ground's own), and its words.</summary>
    public readonly record struct KeyEntry(int Soil, string Material, Color Colour, string Name, string Note);

    /// <summary>The soils the key lists now, those the map has under at least one cell, each in the colour TerrainView draws it in.</summary>
    public IReadOnlyList<KeyEntry> Key => _key;

    /// <summary>
    /// The ground's soil colours, one texel per cell, from TerrainView.SoilColour (the colour the ground itself is drawn in, so the map
    /// shows the soil the player sees and the key's swatches are those texels); and the key's entries, for the soils in the cells.
    /// </summary>
    private void UploadSoils()
    {
        var terrain = _terrain!;
        var colours = terrain.Soils.Select(s => TerrainView.SoilColour(s.Material)).ToArray();
        var bytes = _soilBytes = new byte[terrain.Soil.Length * 3];
        for (int k = 0; k < terrain.Soil.Length; k++)
        {
            var c = colours[terrain.Soil[k]];
            bytes[k * 3] = SoilLook.Byte(c.R); bytes[k * 3 + 1] = SoilLook.Byte(c.G); bytes[k * 3 + 2] = SoilLook.Byte(c.B);
        }
        var image = Image.CreateFromData(terrain.Nx, terrain.Nz, false, Image.Format.Rgb8, bytes);
        if (_soils is null || _soils.GetWidth() != terrain.Nx || _soils.GetHeight() != terrain.Nz) _soils = ImageTexture.CreateFromImage(image);
        else _soils.Update(image);
        _material.SetShaderParameter("soils", _soils);
        _key.Clear();
        foreach (int i in SoilLook.SoilsPresent(terrain))
        {
            var w = SoilLook.WordsFor(terrain.Soils[i].Material);
            _key.Add(new KeyEntry(i, terrain.Soils[i].Material, colours[i], w.Name, w.Note));
        }
    }

    /// <summary>The colour the map's ground texture holds at a cell, "#RRGGBB" (for the scripted check that the key, the map and the ground agree).</summary>
    public string SoilTexelHex(int cell) => $"#{_soilBytes[cell * 3]:X2}{_soilBytes[cell * 3 + 1]:X2}{_soilBytes[cell * 3 + 2]:X2}";

    private void PushUniforms(Terrain terrain)
    {
        _material.SetShaderParameter("centre", new Vector2((float)_cx, (float)_cz));
        _material.SetShaderParameter("mpp", (float)_mpp);
        _material.SetShaderParameter("size_px", Size);
        _material.SetShaderParameter("grade_deg", (float)RoverSpec.GradeDeg);
        _material.SetShaderParameter("tint_warm", TerrainView.SlopeWarm);
        _material.SetShaderParameter("tint_amber", TerrainView.SlopeAmber);
        _material.SetShaderParameter("tint_red", TerrainView.SlopeRed);
        _material.SetShaderParameter("light", TerrainView.MapLight);
        if (terrain.Wind is { } wind)
        {
            _material.SetShaderParameter("wind_on", 1f);
            _material.SetShaderParameter("wind_line", new Vector4((float)wind.ThroughX, (float)wind.ThroughZ, (float)(wind.NotchDeg * Math.PI / 180), (float)wind.Width));
            _material.SetShaderParameter("wind_base", (float)wind.Base);
        }
        else _material.SetShaderParameter("wind_on", 0f);
    }

    // ---- the mouse ---------------------------------------------------------------------------------------------------------

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                Zoom(wheel.ButtonIndex == MouseButton.WheelUp ? 1 / 1.2 : 1.2, wheel.Position);
                AcceptEvent();
                break;
            case InputEventMagnifyGesture magnify:
                Zoom(1 / Math.Max(magnify.Factor, 0.1), magnify.Position);
                AcceptEvent();
                break;
            case InputEventPanGesture pan:   // two-finger scrolling pans
                Pan(-pan.Delta * 10);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _dragging = button.Pressed;
                if (button.Pressed) _pressAt = button.Position;
                else if (button.Position.DistanceTo(_pressAt) < 4) ClickAt(button.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                Pan(motion.Relative);
                AcceptEvent();
                break;
            case InputEventMouse:
                AcceptEvent();   // nothing under the map reacts to the mouse
                break;
        }
    }

    private void Zoom(double factor, Vector2 at)
    {
        var before = Frame.ToWorld(at.X, at.Y);   // the world point under the cursor stays under it
        _mpp = Math.Clamp(_mpp * factor, MinMpp, MaxMpp);
        var f = Frame;
        var after = f.ToWorld(at.X, at.Y);
        _cx += before.X - after.X; _cz += before.Z - after.Z;
        _follow = false;
    }

    private void Pan(Vector2 relative)
    {
        _cx -= relative.X * _mpp; _cz -= relative.Y * _mpp;
        _follow = false;
    }

    private void ClickAt(Vector2 at)
    {
        if (!ShowMarkers()) return;
        var f = Frame;
        Marker? best = null; double bestD = double.MaxValue;
        foreach (var m in Markers())
        {
            var (sx, sy) = f.ToScreen(m.X, m.Z);
            double d = Math.Sqrt((sx - at.X) * (sx - at.X) + (sy - at.Y) * (sy - at.Y)), r = m.Rough ? m.Radius / _mpp : 8;
            if (d <= Math.Max(r, 10) && d < bestD) { best = m; bestD = d; }
        }
        if (best is null) return;
        Choose(best.Id == Chosen() ? null : best.Id);
    }

    // ---- the marks over the ground -------------------------------------------------------------------------------------------

    private static readonly Color Ink = new(0.02f, 0.03f, 0.05f, 0.95f), Cyan = new(0.25f, 0.92f, 1f), Amber = new(1f, 0.78f, 0.2f),
                                  Orange = new(1f, 0.62f, 0.25f), Paper = new(0.97f, 0.95f, 0.9f), Wind = new(0.55f, 0.78f, 1f);

    // Shaped text kept between frames (shaping a string is most of what drawing one costs; the names and legend hardly change)
    private readonly Dictionary<(string, int), TextLine> _lines = [];

    private TextLine Shaped(Font font, string text, int size)
    {
        if (!_lines.TryGetValue((text, size), out var line))
        {
            if (_lines.Count > 200) _lines.Clear();
            _lines[(text, size)] = line = new TextLine();
            line.AddString(text, font, size);
        }
        return line;
    }

    private void Text(Font font, string text, Vector2 at, int size, Color colour, bool centred = false)
    {
        var line = Shaped(font, text, size);
        var origin = new Vector2(centred ? at.X - (float)line.GetSize().X / 2 : at.X, at.Y - line.GetLineAscent());   // at is the baseline's left end
        var item = _marks.GetCanvasItem();
        line.DrawOutline(item, origin, 6, Ink);
        line.Draw(item, origin, colour);
    }

    private void DrawMarks()
    {
        if (!Visible || _terrain is null) return;
        var font = GetThemeDefaultFont();
        var f = Frame;
        string? chosen = Chosen();
        Vector2 S(double x, double z) { var (sx, sy) = f.ToScreen(x, z); return new Vector2((float)sx, (float)sy); }

        // the player's machines: their footprints, in orange
        foreach (var (name, x0, z0, x1, z1) in Machines())
        {
            var a = S(x0, z0); var b = S(x1, z1);
            var rect = new Rect2(a, b - a).Abs().GrowIndividual(3, 3, 3, 3);
            _marks.DrawRect(rect, new Color(Orange, 0.35f));
            _marks.DrawRect(rect, Ink, false, 4);
            _marks.DrawRect(rect, Orange, false, 2);
            Text(font, name, new Vector2(rect.GetCenter().X, rect.Position.Y - 6), 15, Orange, centred: true);
        }

        // the cargo's rough areas: never a point. The discs overlap (the crates lie 5 to 11 m apart, the discs are 12 m across), so the
        // names are stacked: each at its disc's centre, pushed down to 19 px clear of the one above
        if (ShowMarkers())
        {
            var shown = Markers().Select(m => (M: m, C: S(m.X, m.Z))).OrderBy(x => x.C.Y).ThenBy(x => x.C.X).ToList();
            foreach (var (m, c) in shown)
            {
                bool isChosen = m.Id == chosen;
                var colour = isChosen ? Amber : Cyan;
                float r = m.Rough ? (float)(m.Radius / _mpp) : 6;
                _marks.DrawCircle(c, r, new Color(colour, 0.14f));
                _marks.DrawArc(c, r, 0, Mathf.Tau, 72, Ink, isChosen ? 6 : 5, true);
                _marks.DrawArc(c, r, 0, Mathf.Tau, 72, colour, isChosen ? 3 : 2, true);
            }
            float lastY = float.MinValue;
            foreach (var (m, c) in shown)
            {
                float y = Mathf.Max(c.Y + 5, lastY + 19);
                lastY = y;
                Text(font, m.Name, new Vector2(c.X, y), 17, m.Id == chosen ? Amber : Paper, centred: true);
            }
        }

        // the rover, with its heading, and a line to the chosen marker
        if (Rover() is { } rover)
        {
            var p = S(rover.X, rover.Z);
            if (ShowMarkers() && chosen is not null && Markers().FirstOrDefault(m => m.Id == chosen) is { } target)
            {
                var t = S(target.X, target.Z);
                _marks.DrawDashedLine(p, t, Ink, 4, 9);
                _marks.DrawDashedLine(p, t, Amber, 2, 9);
            }
            double a = rover.Compass * Math.PI / 180;
            var fwd = new Vector2((float)Math.Sin(a), -(float)Math.Cos(a)); var side = new Vector2(-fwd.Y, fwd.X);
            var tri = new[] { p + fwd * 15, p - fwd * 9 + side * 9, p - fwd * 4, p - fwd * 9 - side * 9 };
            _marks.DrawColoredPolygon(tri, Paper);
            _marks.DrawPolyline([.. tri, tri[0]], Ink, 3, true);
            Text(font, "rover", p + new Vector2(0, 28), 15, Paper, centred: true);
        }

        DrawFurniture(font, f);
        DrawKey(font);
    }

    /// <summary>
    /// The ground key (#243): a swatch in each soil's own colour (the colours of <see cref="Key"/>, which are TerrainView's, the ground's) with its
    /// name and a few plain words, for the soils this map has; a panel at the left under the title.
    /// </summary>
    private void DrawKey(Font font)
    {
        if (_key.Count == 0) return;
        const int fontSize = 15; const float rowH = 24, swatch = 18, pad = 10;
        string head = "Ground (flat, in the light; a slope turned away looks darker)";
        string Row(KeyEntry e) => $"{char.ToUpper(e.Name[0])}{e.Name[1..]}: {e.Note}";
        float width = Math.Max((float)Shaped(font, head, fontSize).GetSize().X, _key.Max(e => (float)Shaped(font, Row(e), fontSize).GetSize().X + swatch + 10)) + 2 * pad;
        const string foot = "Paler patches: loose or freshly tipped soil · darker: dug away or in shadow";
        width = Math.Max(width, (float)Shaped(font, foot, fontSize).GetSize().X + 2 * pad);
        var box = new Rect2(14, 38, width, pad * 2 + rowH * (_key.Count + 2) - 4);
        _marks.DrawRect(box, new Color(Ink, 0.72f));
        _marks.DrawRect(box, new Color(Paper, 0.5f), false, 1);
        Text(font, head, new Vector2(box.Position.X + pad, box.Position.Y + pad + 15), fontSize, Paper);
        for (int i = 0; i < _key.Count; i++)
        {
            float y = box.Position.Y + pad + rowH * (i + 1);
            var chip = new Rect2(box.Position.X + pad, y + 1, swatch, swatch);
            _marks.DrawRect(chip.Grow(1.5f), Paper);
            _marks.DrawRect(chip, _key[i].Colour);
            Text(font, Row(_key[i]), new Vector2(chip.End.X + 10, y + 15), fontSize, Paper);
        }
        Text(font, foot, new Vector2(box.Position.X + pad, box.Position.Y + pad + rowH * (_key.Count + 1) + 15), fontSize, new Color(Paper, 0.85f));
    }

    /// <summary>The title line, the compass, the scale bar and the legend.</summary>
    private void DrawFurniture(Font font, MapFrame f)
    {
        var size = Size;
        Text(font, "Map (M or Esc closes) · drag pans · wheel zooms · click a cargo area to choose it · Home centres on the rover", new Vector2(14, 22), 15, Paper);
        // north
        var n = new Vector2(size.X - 34, 58);
        _marks.DrawLine(n + new Vector2(0, 24), n - new Vector2(0, 22), Ink, 7); _marks.DrawLine(n + new Vector2(0, 24), n - new Vector2(0, 22), Paper, 3);
        _marks.DrawColoredPolygon([n - new Vector2(0, 30), n + new Vector2(8, -14), n + new Vector2(-8, -14)], Paper);
        Text(font, "N", n + new Vector2(-6, 46), 18, Paper);
        // the scale bar, a round number of metres
        double metres = f.BarMetres(Math.Min(220, size.X / 4));
        float len = (float)(metres / _mpp);
        var a = new Vector2(size.X - 24 - len, size.Y - 62); var b = a + new Vector2(len, 0);
        _marks.DrawLine(a + new Vector2(0, -7), a + new Vector2(0, 7), Ink, 6); _marks.DrawLine(b + new Vector2(0, -7), b + new Vector2(0, 7), Ink, 6);
        _marks.DrawLine(a, b, Ink, 7);
        _marks.DrawLine(a + new Vector2(0, -6), a + new Vector2(0, 6), Paper, 2); _marks.DrawLine(b + new Vector2(0, -6), b + new Vector2(0, 6), Paper, 2);
        _marks.DrawLine(a, b, Paper, 3);
        Text(font, metres >= 1000 ? $"{metres / 1000:0.#} km" : $"{metres:0} m", new Vector2((a.X + b.X) / 2, a.Y - 12), 16, Paper, centred: true);
        // the legend, two lines
        float interval = TerrainView.ContourIntervalOf(_terrain!);
        Text(font, $"Contours every {interval:0.#} m, heavy every {interval * 5:0.#} m · slope: orange 15°, amber 25°, red {RoverSpec.GradeDeg:0}° and over: more than the rover climbs",
             new Vector2(14, size.Y - 32), 14, Paper);
        Text(font, _terrain!.Wind is not null ? "Blue band: the wind's corridor, where the wind is stronger · orange boxes: your machines · rings: where the cargo roughly lies" : "Orange boxes: your machines · rings: where the cargo roughly lies",
             new Vector2(14, size.Y - 12), 14, Paper);
    }

    private const string ShaderCode = """
        shader_type canvas_item;
        uniform sampler2D heights : filter_nearest, repeat_disable;   // one height per cell (R32F), the ground's own array
        uniform sampler2D soils : filter_linear, repeat_disable;   // each cell's soil colour (RGB8), the colour the ground itself is drawn in
        uniform vec2 origin;      // the map's corner (X0, Z0)
        uniform float cell;       // m
        uniform vec2 dims;        // cells
        uniform vec2 centre;      // the world point at the middle of the control
        uniform float mpp;        // metres per pixel
        uniform vec2 size_px;
        uniform float interval = 5.0;
        uniform float low = 0.0;
        uniform float high = 100.0;
        uniform float grade_deg = 30.0;
        uniform vec3 tint_warm;
        uniform vec3 tint_amber;
        uniform vec3 tint_red;
        uniform vec3 light = vec3(0.55, 0.8, 0.4);
        uniform float wind_on = 0.0;
        uniform vec4 wind_line;   // through x, through z, notch azimuth (radians), width
        uniform float wind_base = 0.3;

        float cell_height(ivec2 c) {
            c = clamp(c, ivec2(0), ivec2(dims) - ivec2(1));
            return texelFetch(heights, c, 0).r;
        }
        // the ground's own interpolation: between cell centres, level beyond the outermost ones (Terrain.HeightAt)
        float height_at(vec2 w) {
            vec2 g = (w - origin) / cell - 0.5;
            vec2 fl = floor(g);
            vec2 f = g - fl;
            ivec2 i = ivec2(fl);
            return mix(mix(cell_height(i), cell_height(i + ivec2(1, 0)), f.x),
                       mix(cell_height(i + ivec2(0, 1)), cell_height(i + ivec2(1, 1)), f.x), f.y);
        }
        float line(float h, float px) {
            float w = fwidth(h);
            return 1.0 - smoothstep(0.0, w * px, abs(fract(h - 0.5) - 0.5));
        }
        vec3 ground_colour(vec2 w) {
            float h = height_at(w);
            float e = cell * 0.5;
            float hx = (height_at(w + vec2(e, 0.0)) - height_at(w - vec2(e, 0.0))) / (2.0 * e);
            float hz = (height_at(w + vec2(0.0, e)) - height_at(w - vec2(0.0, e))) / (2.0 * e);
            vec3 n = normalize(vec3(-hx, 1.0, -hz));
            float lit = 0.45 + 0.55 * max(0.0, dot(n, normalize(light)));
            float shade = mix(0.5, 1.2, clamp((lit - 0.45) / 0.55, 0.0, 1.0));
            vec3 base = texture(soils, (w - origin) / (dims * cell)).rgb;   // the soil's own colour, as on the ground (the key's swatches are these)
            float slope = degrees(atan(length(vec2(hx, hz))));
            float s_on = smoothstep(grade_deg - 16.5, grade_deg - 13.5, slope);
            float s_amber = smoothstep(grade_deg - 6.5, grade_deg - 3.5, slope);
            float s_red = smoothstep(grade_deg - 1.5, grade_deg + 1.5, slope);
            vec3 band = mix(mix(tint_warm, tint_amber, s_amber), tint_red, s_red);
            vec3 col = mix(base * shade, band, s_on * mix(mix(0.55, 0.7, s_amber), 0.85, s_red));
            // the wind's corridor, shaded by how much stronger the wind is there than away from it
            if (wind_on > 0.5) {
                float across = -(w.x - wind_line.x) * sin(wind_line.z) + (w.y - wind_line.y) * cos(wind_line.z);
                float strength = (exp(-(across / wind_line.w) * (across / wind_line.w)));
                col = mix(col, vec3(0.45, 0.72, 1.0), 0.2 * strength);
                float edge = 1.0 - smoothstep(0.0, fwidth(across) * 1.6, abs(abs(across) - wind_line.w));
                col = mix(col, vec3(0.7, 0.88, 1.0), 0.55 * edge);
            }
            float hi = h / interval;
            float fine = line(hi, 1.0) * clamp(1.0 - (fwidth(hi) - 0.25) / 0.2, 0.0, 1.0);
            float heavy = line(hi / 5.0, 1.8) * clamp(1.0 - (fwidth(hi / 5.0) - 0.25) / 0.2, 0.0, 1.0);
            return mix(col, vec3(0.12, 0.09, 0.07), clamp(0.45 * fine + 0.85 * heavy, 0.0, 0.9));
        }
        void fragment() {
            vec2 w = centre + (UV * size_px - size_px * 0.5) * mpp;   // y runs down the screen, towards +z: north (-z) is up
            vec2 far = origin + dims * cell;
            bool inside = w.x >= origin.x && w.y >= origin.y && w.x <= far.x && w.y <= far.y;
            COLOR = vec4(inside ? ground_colour(w) : vec3(0.07, 0.07, 0.08), 1.0);
        }
        """;
}
