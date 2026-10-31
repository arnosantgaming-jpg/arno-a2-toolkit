using ArnoA2.App.Map;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>The Resource Tracker: each kind of resource with the closest node and how many this
/// map has. Click a row to show or hide that kind.</summary>
public sealed class ResourceWindow : MapToolWindow
{
    const string Columns = "3,2*,76,44";
    readonly ResourceLayer layer;
    string rendered = "";

    public ResourceWindow(ResourceLayer layer) : base(layer, layer.Preferences, layer.Save, KdeWindowRules.ResourceTitle, "RESOURCES", Columns, new[] { ("RESOURCE", 1, false), ("CLOSEST", 2, true), ("HERE", 3, true) }, 320, 262, 610) => this.layer = layer;

    protected override void Refresh()
    {
        var shown = ResourceAtlas.Kinds.Select(k => (k.Kind, k.Color, k.Shape, Near: layer.Closest(k.Kind), On: layer.Shows(k.Kind))).Where(k => k.Near.Count > 0).ToArray();
        string key = string.Join("|", shown.Select(k => $"{k.Kind}:{k.Near.Count}:{k.Near.Metres:F0}:{k.Near.Direction}:{k.On}"));
        if (key == rendered)
            return;
        rendered = key;
        Rows.Children.Clear();
        foreach (var (kind, color, shape, near, on) in shown)
            Rows.Children.Add(Row(kind, color, shape, near, on));
    }

    Control Row(string kind, Color color, MarkerShape shape, (int Count, double? Metres, string Direction) near, bool on)
    {
        var brush = new SolidColorBrush(color);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 22, Background = new SolidColorBrush(color, on ? 0.09 : 0), Cursor = new Cursor(StandardCursorType.Hand) };
        grid.Children.Add(Column(new Border { Background = on ? brush : Line }, 0));
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(4, 0, 0, 0), Children = { new MarkerGlyph(shape, on ? brush : Muted, 11), new TextBlock { Text = kind, FontSize = 11, Foreground = on ? Brushes.White : Muted, VerticalAlignment = VerticalAlignment.Center } } };
        grid.Children.Add(Column(name, 1));
        grid.Children.Add(Column(Number(near.Metres is double metres ? $"{metres:F0} m {near.Direction}" : "–", 11, FontWeight.Bold, on ? brush : Muted), 2));
        grid.Children.Add(Column(Number(near.Count.ToString(), 10.5, FontWeight.Normal, Muted), 3));
        ToolTip.SetTip(grid, on ? "Click to hide this from the maps" : "Click to show this on the maps");
        grid.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                layer.Choose(kind, !on);
        };
        return new Border { Child = grid, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }
}

/// <summary>The Resource Tracker's drawer: what it shows and how the mini-map behaves.</summary>
public sealed class ResourceSettingsPanel : UserControl
{
    public ResourceSettingsPanel(ResourceLayer layer, Func<ResourceWindow?> window)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Ui.Heading("Show"));
        panel.Children.Add(new ResourcePicker(layer));
        panel.Children.Add(Ui.Note("Nodes are shared between players and respawn, so a dot is a place to check. There are no respawn timers: nothing reliable is known about them."));
        MapToolWindow.LookSettings(panel, layer.Preferences, layer.Save, window);
        MiniMapSettings.Add(panel, layer.Hub);
        Content = panel;
    }
}
