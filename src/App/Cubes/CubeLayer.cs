using System.Text.Json;
using ArnoA2.App.Map;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArnoA2.App;

public sealed class CubePreferences : ToolWindowLook
{
    public bool ShowChecked { get; set; } = true;
    /// <summary>How long a checked spot stays out of the ranking.</summary>
    public int CheckedMinutes { get; set; } = 30;
}

/// <summary>The Hidden Cube Tracker's maps: every place a cube can appear, as red cubes, with
/// the purple ranking skipping the spots you have just checked.</summary>
public sealed class CubeLayer : MapLayer
{
    static readonly IBrush Red = new SolidColorBrush(CubeAtlas.Red), Done = new SolidColorBrush(Color.Parse("#6E6878"));
    readonly string file;
    readonly Dictionary<CubeSpot, DateTime> done = new();
    int changes;

    public CubeLayer(MapHub hub, string root) : base(hub)
    {
        file = Path.Combine(root, "cube-settings.json");
        try
        {
            if (File.Exists(file))
                Preferences = JsonSerializer.Deserialize<CubePreferences>(File.ReadAllText(file)) ?? new();
        }
        catch (Exception)
        {
            Preferences = new();
        }
    }

    public CubePreferences Preferences { get; } = new();
    public override ToolColors Colors => ToolTheme.CubeColors;
    public override int Ring => 2;
    public override MapLook Look => Preferences;
    public override string Unit => "spot";
    public override string PickerTitle => "Cubes";
    public override string Help => "A cube uses one of its group's spots, so a mark is a place to check, not a live cube. Spots rank by density and distance, skipping the ones you checked.";
    protected override string Nothing => CubeAtlas.Region(Hub.Region).Count == 0 ? "No cube data for this map" : "Every spot here is checked";
    public override Control Picker() => new CubePicker(this);
    public int CheckedCount => done.Count;

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

    /// <summary>Marks a spot's cubes as checked: the ranking skips them for a while.</summary>
    public void Check(Hotspot spot)
    {
        double radius = FarmingHotspots.RadiusMetres / Hub.Map.Metres;
        var until = DateTime.UtcNow.AddMinutes(Preferences.CheckedMinutes);
        foreach (var cube in CubeAtlas.Region(Hub.Region).Where(c => Math.Pow(c.X - spot.X, 2) + Math.Pow(c.Y - spot.Y, 2) <= radius * radius))
            done[cube] = until;
        changes++;
        Refresh(force: true);
    }

    public void ClearChecked()
    {
        done.Clear();
        changes++;
        Refresh(force: true);
    }

    public void Redraw() => Refresh(force: true);

    protected override string Wanted()
    {
        var now = DateTime.UtcNow;
        foreach (var cube in done.Where(d => d.Value <= now).Select(d => d.Key).ToArray())
        {
            done.Remove(cube);
            changes++;
        }

        return changes.ToString();
    }

    protected override IReadOnlyList<MapDot> Plot() => CubeAtlas.Region(Hub.Region)
        .Where(c => Preferences.ShowChecked || !done.ContainsKey(c))
        .Select(c => new MapDot(c.X, c.Y, done.ContainsKey(c) ? Done : Red, MarkerShape.Cube)).ToArray();

    protected override IReadOnlyList<Hotspot> Rank()
    {
        var spots = CubeAtlas.Region(Hub.Region);
        var weights = spots.Select(c => c.Group).Distinct().ToDictionary(id => id, _ => 1.0);
        return FarmingHotspots.Find(spots.Where(c => !done.ContainsKey(c)).Select(c => new MapMarker(c.X, c.Y, c.Group)), Hub.Map, weights, Hub.Player);
    }

    public override string Names(IEnumerable<int> ids, int limit = 3)
    {
        var names = ids.Select(id => CubeAtlas.Area(Hub.Region, id)).Distinct().ToArray();
        return names.Length <= limit ? string.Join(", ", names) : string.Join(", ", names.Take(limit)) + $" +{names.Length - limit}";
    }
}

/// <summary>Checked spots: whether they show, and forgetting them.</summary>
public sealed class CubePicker : UserControl
{
    public CubePicker(CubeLayer layer)
    {
        var show = new CheckBox { Content = "Show checked spots in grey", IsChecked = layer.Preferences.ShowChecked };
        show.IsCheckedChanged += (_, _) =>
        {
            layer.Preferences.ShowChecked = show.IsChecked == true;
            layer.Save();
            layer.Redraw();
        };
        Content = new StackPanel
        {
            Spacing = 10,
            Children = { show, Ui.Button("Forget checked spots", layer.ClearChecked), Ui.Note("Mark spots checked in the Hidden Cubes window. Verteron and Altgard cubes are reported to need a Key: Hidden Cube, so carry some.") }
        };
    }
}
