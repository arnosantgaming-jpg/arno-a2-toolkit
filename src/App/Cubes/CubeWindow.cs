using ArnoA2.App.Map;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>The Hidden Cube Tracker: the best spots to check next. Click a row to head for it,
/// ✓ when you've looked there and the ranking moves on.</summary>
public sealed class CubeWindow : MapToolWindow
{
    const string Columns = "3,20,2*,36,76,26";
    readonly CubeLayer layer;
    readonly Border clear;
    string rendered = "";

    public CubeWindow(CubeLayer layer) : base(layer, layer.Preferences, layer.Save, KdeWindowRules.CubeTitle, "HIDDEN CUBES", Columns, new[] { ("AREA", 2, false), ("SPOTS", 3, true), ("DISTANCE", 4, true) }, 340, 238, 150)
    {
        this.layer = layer;
        clear = Dps.Ui.Flat("Forget checked", layer.ClearChecked, "Put every checked spot back in the ranking", Line, Muted);
        AddAction(clear);
    }

    protected override void Refresh()
    {
        clear.IsVisible = layer.CheckedCount > 0;
        var spots = layer.Hotspots;
        string key = string.Join("|", spots.Select(s => $"{s.X:F4},{s.Y:F4},{s.DistanceMetres:F0}")) + "#" + layer.TargetSpot;
        if (key == rendered)
            return;
        rendered = key;
        Rows.Children.Clear();
        for (int i = 0; i < spots.Count; i++)
            Rows.Children.Add(Row(i, spots[i]));
        if (spots.Count == 0)
            Rows.Children.Add(new TextBlock { Text = "No cube spots to show on this map. Cube data covers Verteron and Altgard.", FontSize = 10.5, Foreground = Muted, Margin = new Thickness(6, 8), TextWrapping = TextWrapping.Wrap });
    }

    Control Row(int index, Hotspot spot)
    {
        bool target = index == layer.TargetSpot;
        var colors = layer.Colors;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 22, Background = new SolidColorBrush(colors.Accent, target ? 0.2 : 0.06), Cursor = new Cursor(StandardCursorType.Hand) };
        grid.Children.Add(Column(new Border { Background = target ? Bright : Line }, 0));
        grid.Children.Add(Column(Number((index + 1).ToString(), 10.5, FontWeight.Bold, Bright), 1));
        grid.Children.Add(Column(new TextBlock { Text = layer.Names(spot.Ids, 2), FontSize = 11, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }, 2));
        grid.Children.Add(Column(Number(spot.Spawns.ToString(), 10.5, FontWeight.Normal, Muted), 3));
        string direction = layer.Hub.Player is { } player ? " " + MapHub.Compass(FarmingHotspots.Bearing(player, (spot.X, spot.Y))) : "";
        grid.Children.Add(Column(Number(spot.DistanceMetres is double metres ? $"{metres:F0} m{direction}" : "–", 11, FontWeight.Bold, Bright), 4));
        var tick = new TextBlock { Text = "✓", FontSize = 12, Foreground = Bright, Background = Brushes.Transparent, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(tick, "Checked: skip this spot for a while");
        tick.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                e.Handled = true;
                layer.Check(spot);
            }
        };
        grid.Children.Add(Column(tick, 5));
        ToolTip.SetTip(grid, "Click to head for this spot");
        grid.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                layer.TargetSpot = index;
                layer.Redraw();
            }
        };
        return new Border { Child = grid, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }
}

/// <summary>The Hidden Cube Tracker's drawer: checked spots and how the mini-map behaves.</summary>
public sealed class CubeSettingsPanel : UserControl
{
    public CubeSettingsPanel(CubeLayer layer, Func<CubeWindow?> window)
    {
        var saved = layer.Preferences;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Ui.Heading("Checked spots"));
        panel.Children.Add(new CubePicker(layer));
        panel.Children.Add(Ui.Number("Skip checked spots for minutes", saved.CheckedMinutes, 5, 240, v =>
        {
            saved.CheckedMinutes = v;
            layer.Save();
        }));
        panel.Children.Add(Ui.Note("A cube comes back at any spot of its group and the game has no known timer, so this is only how long the ranking leaves a spot alone. Checked spots are forgotten when the toolkit closes."));
        MapToolWindow.LookSettings(panel, saved, layer.Save, window);
        MiniMapSettings.Add(panel, layer.Hub);
        Content = panel;
    }
}
