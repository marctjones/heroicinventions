using Godot;

namespace HeroicInventions;

/// <summary>
/// The small list the Join machines tool offers when a click lies over more than one part's pick box (#225): the parts under the click,
/// nearest first, each a button; Esc or the next click elsewhere closes it. Main.Links.cs fills it (<see cref="Offer"/>) and takes the choice.
/// </summary>
public partial class PickList : CanvasLayer
{
    private PanelContainer? _panel;

    public bool IsOpen => _panel is not null && IsInstanceValid(_panel);

    public PickList() { Layer = 75; }

    /// <summary>Shows the list beside <paramref name="screen"/>; <paramref name="chosen"/> gets the 0-based index of the button pressed.</summary>
    public void Offer(Vector2 screen, IReadOnlyList<string> items, Action<int> chosen)
    {
        Close();
        _panel = new PanelContainer { Name = "Panel" };
        var col = new VBoxContainer();
        _panel.AddChild(col);
        col.AddChild(new Label { Text = "Several parts are here: pick one" });
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            var b = new Button { Text = $"{i + 1}  {items[i]}", Alignment = HorizontalAlignment.Left, Name = $"Item{i + 1}" };
            b.Pressed += () => chosen(index);
            col.AddChild(b);
        }
        AddChild(_panel);
        var view = GetViewport().GetVisibleRect().Size;
        _panel.Position = new Vector2(Mathf.Clamp(screen.X + 12, 0, Math.Max(0, view.X - 360)), Mathf.Clamp(screen.Y + 12, 0, Math.Max(0, view.Y - 40 - 34 * items.Count)));
    }

    public void Close()
    {
        if (_panel is not null && IsInstanceValid(_panel)) _panel.QueueFree();
        _panel = null;
    }
}
