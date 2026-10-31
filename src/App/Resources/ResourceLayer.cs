using System.Text.Json;
using ArnoA2.App.Map;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace ArnoA2.App;

public sealed class ResourcePreferences : ToolWindowLook
{
    public List<string> Kinds { get; set; } = ResourceAtlas.Kinds.Select(k => k.Kind).ToList();
}

/// <summary>The Resource Tracker's maps: gathering nodes of the chosen kinds, in green.</summary>
public sealed class ResourceLayer : MapLayer
{
    static readonly Dictionary<string, (IBrush Brush, MarkerShape Shape)> Markers = ResourceAtlas.Kinds.ToDictionary(k => k.Kind, k => ((IBrush)new SolidColorBrush(k.Color), k.Shape));
    readonly string file;

    public ResourceLayer(MapHub hub, string root) : base(hub)
    {
        file = Path.Combine(root, "resource-settings.json");
        try
        {
            if (File.Exists(file))
                Preferences = JsonSerializer.Deserialize<ResourcePreferences>(File.ReadAllText(file)) ?? new();
        }
        catch (Exception)
        {
            Preferences = new();
        }
    }

    public ResourcePreferences Preferences { get; } = new();
    public override ToolColors Colors => ToolTheme.ResourceColors;
    public override int Ring => 1;
    public override MapLook Look => Preferences;
    public override string Unit => "node";
    public override string PickerTitle => "Gather";
    public override string Help => "Spots rank the chosen resources by density and distance. Nodes are shared and respawn, so a dot is a place to check, not a sure pickup.";
    protected override string Nothing => Preferences.Kinds.Count == 0 ? "Choose something to gather" : "None of these on this map";
    public override Control Picker() => new ResourcePicker(this);

    public override void Save()
    {
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(Preferences));
        }
        catch (Exception)
        {
        }
    }

    public bool Shows(string kind) => Preferences.Kinds.Contains(kind);

    public void Choose(string kind, bool on)
    {
        if (on == Shows(kind))
            return;
        if (on)
            Preferences.Kinds.Add(kind);
        else
            Preferences.Kinds.Remove(kind);
        Save();
        TargetSpot = 0;
        Refresh(force: true);
    }

    /// <summary>How many nodes of this kind the shown map has, and how far the closest one is.</summary>
    public (int Count, double? Metres, string Direction) Closest(string kind)
    {
        int count = 0;
        double best = double.MaxValue;
        (double X, double Y) nearest = default;
        var player = Hub.Player;
        foreach (var node in ResourceAtlas.Region(Hub.Region))
        {
            if (ResourceAtlas.Names[node.Name].Kind != kind)
                continue;
            count++;
            if (player is { } p && Math.Pow(node.X - p.X, 2) + Math.Pow(node.Y - p.Y, 2) is var distance && distance < best)
                (best, nearest) = (distance, (node.X, node.Y));
        }

        return count == 0 || player is not { } from
            ? (count, null, "")
            : (count, Math.Sqrt(best) * Hub.Map.Metres, MapHub.Compass(FarmingHotspots.Bearing(from, nearest)));
    }

    IEnumerable<ResourceNode> Chosen => ResourceAtlas.Region(Hub.Region).Where(n => Shows(ResourceAtlas.Names[n.Name].Kind));

    protected override string Wanted() => string.Join(",", Preferences.Kinds.Order());

    protected override IReadOnlyList<MapDot> Plot() => Chosen.Select(n =>
    {
        var (brush, shape) = Markers[ResourceAtlas.Names[n.Name].Kind];
        return new MapDot(n.X, n.Y, brush, shape);
    }).ToArray();

    protected override IReadOnlyList<Hotspot> Rank()
    {
        var weights = Enumerable.Range(0, ResourceAtlas.Names.Count).ToDictionary(id => id, _ => 1.0);
        return FarmingHotspots.Find(Chosen.Select(n => new MapMarker(n.X, n.Y, n.Name)), Hub.Map, weights, Hub.Player);
    }

    public override string Names(IEnumerable<int> ids, int limit = 3)
    {
        var names = ids.Select(id => ResourceAtlas.Names[id].Name).ToArray();
        return names.Length <= limit ? string.Join(", ", names) : string.Join(", ", names.Take(limit)) + $" +{names.Length - limit}";
    }
}

/// <summary>A tick box for each kind of resource.</summary>
public sealed class ResourcePicker : UserControl
{
    readonly ResourceLayer layer;
    readonly List<(string Kind, CheckBox Box)> boxes = new();

    public ResourcePicker(ResourceLayer layer)
    {
        this.layer = layer;
        var grid = new UniformGrid { Columns = 2 };
        foreach (var (kind, color, shape) in ResourceAtlas.Kinds)
        {
            var brush = new SolidColorBrush(color);
            var name = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { new MarkerGlyph(shape, brush), new TextBlock { Text = kind, Foreground = brush } } };
            var box = new CheckBox { Content = name, IsChecked = layer.Shows(kind) };
            box.IsCheckedChanged += (_, _) => layer.Choose(kind, box.IsChecked == true);
            boxes.Add((kind, box));
            grid.Children.Add(box);
        }

        Content = grid;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        layer.Changed += Update;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        layer.Changed -= Update;
    }

    void Update()
    {
        foreach (var (kind, box) in boxes)
            box.IsChecked = layer.Shows(kind);
    }
}
