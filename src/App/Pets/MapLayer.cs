using ArnoA2.App.Map;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArnoA2.App;

/// <param name="Learned">Picked up from play, not from the bundled data: drawn with a white edge.</param>
public sealed record MapDot(double X, double Y, IBrush Brush, MarkerShape Shape = MarkerShape.Dot, bool Learned = false);

/// <summary>The one mini-map every tool shares: a tool's Mini-map button adds its ring and its
/// marks, or takes them off again. It closes with the last tool on it.</summary>
public sealed class SharedMiniMap
{
    readonly MapHub hub;
    readonly List<MapLayer> active = new();

    public SharedMiniMap(MapHub hub) => this.hub = hub;

    public MiniMapWindow? Window { get; private set; }
    public IReadOnlyList<MapLayer> Active => active;

    public void Leave(MapLayer layer)
    {
        active.Remove(layer);
        if (active.Count == 0)
            Window?.Close();
        else
            Window?.SetLayers(active.ToArray());
    }

    public void Toggle(MapLayer layer)
    {
        if (active.Contains(layer))
        {
            Leave(layer);
            return;
        }

        active.Add(layer);
        active.Sort((a, b) => a.Ring.CompareTo(b.Ring));
        if (Window != null)
        {
            Window.SetLayers(active.ToArray());
            return;
        }

        Window = new MiniMapWindow(hub, active.ToArray());
        Window.Closed += (_, _) =>
        {
            Window = null;
            active.Clear();
        };
        Window.Show();
    }
}

/// <summary>What one tool shows on the shared maps: its colours, dots and ranked farming spots.
/// Lives as long as the app and follows the <see cref="MapHub"/> while any map is open.</summary>
public abstract class MapLayer
{
    (double X, double Y)? ranked;
    string rankedKey = "";

    protected MapLayer(MapHub hub)
    {
        Hub = hub;
        hub.Changed += () => Refresh(force: false);
    }

    public MapHub Hub { get; }
    public abstract ToolColors Colors { get; }
    /// <summary>Where this tool's ring sits on the shared mini-map, counted from the outside.</summary>
    public abstract int Ring { get; }
    public abstract MapLook Look { get; }
    /// <summary>What one marker is called: "Show all {Unit} dots", "12 {Unit}s".</summary>
    public abstract string Unit { get; }
    public abstract string PickerTitle { get; }
    public abstract string Help { get; }
    /// <summary>The guidance text when no spot is found.</summary>
    protected abstract string Nothing { get; }
    public virtual bool ShowsMobs => false;
    public IReadOnlyList<MapDot> Dots { get; private set; } = Array.Empty<MapDot>();
    public IReadOnlyList<Hotspot> Hotspots { get; private set; } = Array.Empty<Hotspot>();
    public int TargetSpot { get; set; }
    public Hotspot? Target => Hotspots.Count > TargetSpot ? Hotspots[TargetSpot] : null;
    public event Action? Changed;

    public abstract void Save();
    /// <summary>The control that chooses what the map shows, one per map panel.</summary>
    public abstract Control Picker();
    /// <summary>A short list of what the markers with these ids are.</summary>
    public abstract string Names(IEnumerable<int> ids, int limit = 3);
    public virtual bool WantsMob(int petId) => false;
    /// <summary>Takes in what's wanted right now; a change of the returned key redoes the ranking.</summary>
    protected abstract string Wanted();
    protected abstract IReadOnlyList<MapDot> Plot();
    protected abstract IReadOnlyList<Hotspot> Rank();

    protected void Refresh(bool force)
    {
        string key = Hub.Region + "|" + Wanted();
        var map = Hub.Map;
        bool moved = Hub.Player is { } p && (ranked is not { } last || Math.Sqrt(Math.Pow(p.X - last.X, 2) + Math.Pow(p.Y - last.Y, 2)) * map.Metres > 40);
        if (force || moved || key != rankedKey)
        {
            rankedKey = key;
            ranked = Hub.Player;
            Dots = Plot();
            Hotspots = Rank();
            if (TargetSpot >= Hotspots.Count)
                TargetSpot = 0;
        }

        Changed?.Invoke();
    }

    public string Guidance()
    {
        if (Hub.Live is { Region: null } live && !Hub.Assumed)
            return live.Zone == 0 ? "Waiting for zone (log in with the tracker open)" : $"No map for zone {live.Zone}";
        if (Target is not { } spot)
            return Nothing;
        if (Hub.Player is not { } player)
            return $"Best spot: {Names(spot.Ids, 2)} · position unknown";
        double distance = spot.DistanceMetres ?? 0;
        return distance < FarmingHotspots.RadiusMetres
            ? $"You're in the spot · {Names(spot.Ids, 2)}"
            : $"{distance:F0} m {MapHub.Compass(FarmingHotspots.Bearing(player, (spot.X, spot.Y)))} · {Names(spot.Ids, 2)}";
    }
}
