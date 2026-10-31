using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using ArnoA2.App.Map;

namespace ArnoA2.App;

/// <summary>The full region map: the tool's picker, ranked farming spots and live position.
/// Lives in a launcher drawer or in its own window.</summary>
public sealed class MapPanel : UserControl
{
    readonly MapLayer layer;
    readonly MapHub hub;
    readonly MapCanvas canvas;
    readonly ComboBox region = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CheckBox follow = new() { Content = "Follow me" };
    readonly CheckBox spawns = new();
    readonly TextBlock position = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray, FontSize = 12 };
    readonly TextBlock guidance = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly StackPanel spots = new() { Spacing = 6 };
    readonly TextBlock minimapStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray, FontSize = 11 };
    string renderedSpots = "";
    bool refreshing;

    public MapPanel(MapLayer layer, Action? fullscreen = null)
    {
        this.layer = layer;
        hub = layer.Hub;
        canvas = new MapCanvas(layer, minimap: false) { Zoom = layer.Look.FullZoom };
        guidance.Foreground = new SolidColorBrush(layer.Colors.Bright);
        spawns.Content = $"Show all {layer.Unit} dots";

        var side = new StackPanel { Spacing = 10, Margin = new Thickness(14) };
        side.Children.Add(new TextBlock { Text = layer.PickerTitle, FontSize = 18 });
        side.Children.Add(layer.Picker());
        side.Children.Add(new TextBlock { Text = "Map", FontSize = 18, Margin = new Thickness(0, 8, 0, 0) });
        region.ItemsSource = WorldMaps.Order.Select(key => SpawnAtlas.Region(key).Name).ToArray();
        region.SelectedIndex = Math.Max(0, Array.IndexOf(WorldMaps.Order, hub.Region));
        region.SelectionChanged += (_, _) =>
        {
            if (refreshing || region.SelectedIndex < 0)
                return;
            hub.SetRegion(WorldMaps.Order[region.SelectedIndex]);
            canvas.Follow = false;
            canvas.Center = (0.5, 0.5);
        };
        side.Children.Add(region);
        follow.IsChecked = true;
        follow.IsCheckedChanged += (_, _) =>
        {
            if (!refreshing)
                canvas.Follow = follow.IsChecked == true;
        };
        spawns.IsChecked = layer.Look.ShowAllSpawns;
        spawns.IsCheckedChanged += (_, _) =>
        {
            layer.Look.ShowAllSpawns = spawns.IsChecked == true;
            layer.Save();
        };
        side.Children.Add(follow);
        side.Children.Add(spawns);
        var reader = new CheckBox { Content = "Read in-game minimap (precise position)", IsChecked = hub.Preferences.ReadMinimap };
        reader.IsCheckedChanged += (_, _) =>
        {
            hub.SetMinimapReading(reader.IsChecked == true);
            hub.Save();
            Update();
        };
        side.Children.Add(reader);
        side.Children.Add(Ui.Button("Recalibrate minimap", hub.RecalibrateMinimap));
        side.Children.Add(minimapStatus);
        if (fullscreen != null)
            side.Children.Add(Ui.Button("Fullscreen (F11)", fullscreen));
        side.Children.Add(position);
        side.Children.Add(guidance);
        side.Children.Add(new TextBlock { Text = "Best spots", FontSize = 18, Margin = new Thickness(0, 8, 0, 0) });
        side.Children.Add(spots);
        side.Children.Add(new TextBlock
        {
            Text = layer.Help + " Drag to pan, scroll to zoom, double-click to follow again, click a numbered spot to target it.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.LightSlateGray,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var layout = new DockPanel();
        var scroll = new ScrollViewer { Width = 300, Content = side, Background = new SolidColorBrush(layer.Colors.Panel) };
        DockPanel.SetDock(scroll, Dock.Left);
        layout.Children.Add(scroll);
        layout.Children.Add(canvas);
        Content = layout;

        canvas.ViewChanged += () =>
        {
            layer.Look.FullZoom = canvas.Zoom;
            refreshing = true;
            follow.IsChecked = canvas.Follow;
            refreshing = false;
            Update();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        layer.Changed += Update;
        hub.Attach();
        if (hub.Player is { } player)
            canvas.Center = player;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        layer.Changed -= Update;
        layer.Save();
        hub.Detach();
    }

    void Update()
    {
        refreshing = true;
        region.SelectedIndex = Math.Max(0, Array.IndexOf(WorldMaps.Order, hub.Region));
        refreshing = false;
        var live = hub.Live;
        position.Text = hub.Error != null ? hub.Error
            : live == null ? "Connecting to capture…"
            : live.Region == null && !hub.Assumed ? (live.Zone == 0 ? "Waiting for a zone: start the tracker, then log in." : $"Zone {live.Zone} has no bundled map.")
            : live.X == null ? "Position unknown until you use a skill, get hit or change zone."
            : (hub.Assumed ? "Map assumed from last session · " : "") + (hub.Source switch { "Minimap" => "From the in-game minimap", "Exact" => "Exact position", _ => "Estimated from nearby mobs" }) + $" · {live.AgeSeconds:F0}s ago" + (layer.ShowsMobs ? $" · {live.Mobs.Count} pet mobs nearby" : "");
        guidance.Text = layer.Guidance();
        minimapStatus.Text = hub.Preferences.ReadMinimap ? (hub.MinimapStatus.Length > 0 ? hub.MinimapStatus : "Looking for the game minimap…") : "Using network position only.";
        string key = string.Join("|", layer.Hotspots.Select(s => $"{s.X:F4},{s.Y:F4},{s.DistanceMetres:F0}")) + "#" + layer.TargetSpot;
        if (key == renderedSpots)
            return;
        renderedSpots = key;
        spots.Children.Clear();
        for (int i = 0; i < layer.Hotspots.Count; i++)
        {
            var spot = layer.Hotspots[i];
            int index = i;
            string distance = spot.DistanceMetres is double metres ? $"{metres:F0} m" : "";
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{i + 1}. {spot.Spawns} {layer.Unit}s  {distance}", FontWeight = FontWeight.SemiBold });
            text.Children.Add(new TextBlock { Text = layer.Names(spot.Ids, 4), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brushes.LightSlateGray });
            var button = new Button
            {
                Content = text,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                BorderBrush = i == layer.TargetSpot ? guidance.Foreground : Brushes.Transparent,
                BorderThickness = new Thickness(1)
            };
            button.Click += (_, _) =>
            {
                layer.TargetSpot = index;
                canvas.Frame(spot);
                refreshing = true;
                follow.IsChecked = false;
                refreshing = false;
                Update();
            };
            spots.Children.Add(button);
        }
    }
}

/// <summary>The map on its own, for a second monitor. F11 toggles fullscreen.</summary>
public sealed class FullMapWindow : Window
{
    public FullMapWindow(MapLayer layer)
    {
        Title = "Arno's A2 Toolkit · map";
        Icon = ToolTheme.AppIcon;
        Width = 1280;
        Height = 820;
        MinWidth = 640;
        MinHeight = 420;
        ToolTheme.Frame(this, "MAP", layer.Colors.Accent, new MapPanel(layer, ToggleFullscreen), new SolidColorBrush(layer.Colors.Window), new SolidColorBrush(layer.Colors.Line));
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11)
                ToggleFullscreen();
            else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
                WindowState = WindowState.Normal;
        };
    }

    void ToggleFullscreen() => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
}
