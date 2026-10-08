using Godot;

namespace HeroicInventions;

/// <summary>
/// The HUD's look, one Godot Theme built in code (docs/art-direction.md §7, §12.10, #106): dark warm-grey
/// panels, a bronze accent, cream text, and the build mode's console set as a ledger page. It is installed
/// once on each layer's top-level panels and dialogs, so every panel, button, label, menu and dialog in Main and in build mode takes
/// it with no per-widget styling. Per-widget overrides that remain (font sizes, dimmed notes) only change
/// size or alpha. The default font is Godot's bundled one (permissively licensed), so no font asset is added.
/// </summary>
public static class HudTheme
{
    public static readonly Color Panel = Color.FromHtml("#2B2724");
    public static readonly Color PanelRaised = Color.FromHtml("#3B342F");
    public static readonly Color PanelHover = Color.FromHtml("#4A4038");
    public static readonly Color Bronze = Color.FromHtml("#CC8F4A");
    public static readonly Color Cream = Color.FromHtml("#F1E9D8");
    public static readonly Color CreamDim = Color.FromHtml("#B8AE9C");
    public static readonly Color Paper = Color.FromHtml("#E8DFC8");
    public static readonly Color Ink = Color.FromHtml("#2B2724");

    private static Theme? _theme;

    /// <summary>
    /// Theme everything that is, or later becomes, a direct child of <paramref name="layer"/>. A theme reaches a
    /// Control's descendants but not across a CanvasLayer, and there is no project theme to fall back on at
    /// runtime, so each top-level panel or dialog of a layer carries it.
    /// </summary>
    public static void Install(CanvasLayer layer)
    {
        layer.ChildEnteredTree += Apply;
        foreach (var child in layer.GetChildren()) Apply(child);
    }

    private static void Apply(Node child)
    {
        if (child is Control { Theme: null } c) c.Theme = Get();
        else if (child is Window { Theme: null } w) w.Theme = Get();
    }

    public static Theme Get() => _theme ??= Build();

    private static StyleBoxFlat Box(Color fill, Color? border = null, int radius = 3, int margin = 6, int borderWidth = 1)
    {
        var s = new StyleBoxFlat { BgColor = fill };
        s.SetCornerRadiusAll(radius);
        s.SetContentMarginAll(margin);
        if (border is { } b) { s.BorderColor = b; s.SetBorderWidthAll(borderWidth); }
        return s;
    }

    private static Theme Build()
    {
        var t = new Theme();
        var panel = Box(new Color(Panel, 0.94f), Color.FromHtml("#5A4A38"), 4, 8);

        t.SetStylebox("panel", "PanelContainer", panel);
        t.SetStylebox("panel", "Panel", panel);
        t.SetStylebox("panel", "PopupPanel", Box(Panel, Bronze, 3, 6));
        t.SetStylebox("panel", "PopupMenu", Box(Panel, Bronze, 3, 4));
        t.SetStylebox("hover", "PopupMenu", Box(PanelHover, null, 2, 3));
        t.SetColor("font_color", "PopupMenu", Cream);
        t.SetColor("font_hover_color", "PopupMenu", Cream);
        t.SetColor("font_disabled_color", "PopupMenu", CreamDim);
        t.SetStylebox("panel", "AcceptDialog", Box(Panel, Bronze, 3, 8));
        t.SetStylebox("embedded_border", "Window", Box(Panel, Bronze, 3, 8));
        t.SetColor("title_color", "Window", Bronze);

        // text
        t.SetColor("font_color", "Label", Cream);
        t.SetColor("font_outline_color", "Label", new Color(Panel, 0.9f));
        t.SetConstant("outline_size", "Label", 3); // legible over a pale ground as well as on a panel
        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = new Color(Bronze, 0.45f), Thickness = 1 });
        t.SetConstant("separation", "HSeparator", 8);

        // buttons: raised warm grey, bronze edge on hover, solid bronze with ink text when pressed or on
        foreach (var type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            t.SetStylebox("normal", type, Box(PanelRaised, Color.FromHtml("#5A4A38"), 3, 5));
            t.SetStylebox("hover", type, Box(PanelHover, Bronze, 3, 5));
            t.SetStylebox("pressed", type, Box(Bronze, Bronze, 3, 5));
            t.SetStylebox("hover_pressed", type, Box(Color.FromHtml("#DDA463"), Bronze, 3, 5));
            t.SetStylebox("disabled", type, Box(new Color(PanelRaised, 0.5f), Color.FromHtml("#3F362D"), 3, 5));
            t.SetStylebox("focus", type, new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = Cream, DrawCenter = false, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 });
            t.SetColor("font_color", type, Cream);
            t.SetColor("font_hover_color", type, Cream);
            t.SetColor("font_focus_color", type, Cream);
            t.SetColor("font_pressed_color", type, Ink);
            t.SetColor("font_hover_pressed_color", type, Ink);
            t.SetColor("font_disabled_color", type, CreamDim);
        }
        t.SetColor("font_color", "CheckButton", Cream);
        t.SetColor("font_color", "CheckBox", Cream);

        // fields
        t.SetStylebox("normal", "LineEdit", Box(Color.FromHtml("#1F1C1A"), Color.FromHtml("#5A4A38"), 3, 5));
        t.SetStylebox("focus", "LineEdit", Box(Color.FromHtml("#1F1C1A"), Bronze, 3, 5));
        t.SetColor("font_color", "LineEdit", Cream);
        t.SetColor("caret_color", "LineEdit", Bronze);
        t.SetColor("font_placeholder_color", "LineEdit", CreamDim);
        t.SetColor("selection_color", "LineEdit", new Color(Bronze, 0.45f));
        t.SetStylebox("fill", "ProgressBar", Box(Bronze, null, 2, 0));
        t.SetStylebox("background", "ProgressBar", Box(Color.FromHtml("#1F1C1A"), null, 2, 0));

        // scrolling and sliders
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", bar, Box(new Color(0, 0, 0, 0.25f), null, 3, 0));
            t.SetStylebox("grabber", bar, Box(new Color(Bronze, 0.75f), null, 3, 0));
            t.SetStylebox("grabber_highlight", bar, Box(Bronze, null, 3, 0));
            t.SetStylebox("grabber_pressed", bar, Box(Color.FromHtml("#DDA463"), null, 3, 0));
        }
        t.SetStylebox("slider", "HSlider", Box(Color.FromHtml("#1F1C1A"), null, 2, 0));
        t.SetStylebox("grabber_area", "HSlider", Box(Bronze, null, 2, 0));

        // the console is the rover's log: a ledger page, ink on paper
        t.SetStylebox("normal", "RichTextLabel", Box(Paper, Color.FromHtml("#9A8A68"), 2, 6));
        t.SetStylebox("focus", "RichTextLabel", Box(Paper, Bronze, 2, 6));
        t.SetColor("default_color", "RichTextLabel", Ink);
        t.SetColor("selection_color", "RichTextLabel", new Color(Bronze, 0.5f));
        t.SetColor("font_selected_color", "RichTextLabel", Ink);
        t.SetColor("table_odd_row_bg", "RichTextLabel", new Color(Ink, 0.05f));
        return t;
    }
}
