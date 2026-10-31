using System.Text.Json;
using ArnoA2.App.Map;
using Avalonia.Threading;

namespace ArnoA2.App;

/// <summary>What each tool remembers about its own maps.</summary>
public class MapLook
{
    public double MiniOpacity { get; set; } = 0.8;
    public double MiniZoom { get; set; } = 5;
    public double FullZoom { get; set; } = 1;
    public int? MiniX { get; set; }
    public int? MiniY { get; set; }
    public double MiniWidth { get; set; } = 300;
    public double MiniHeight { get; set; } = 300;
    public bool ShowAllSpawns { get; set; } = true;
    /// <summary>Mini-map shape: "Round" or "Rectangle".</summary>
    public string MiniShape { get; set; } = "Round";
    /// <summary>Mini-map turns with your camera (the camera direction points up) instead of north up.</summary>
    public bool MiniTurn { get; set; }
}

/// <summary>The Pet Tracker's map look and target, and what all maps share, the mini-map's look among it.</summary>
public sealed class MapPreferences : MapLook
{
    public string Target { get; set; } = "Unfinished";
    public int? PetId { get; set; }
    public string? LastRegion { get; set; } = "verteron";
    public bool ReadMinimap { get; set; } = true;
    public bool LearnSpawns { get; set; }
    /// <summary>Tile pixels per minimap pixel, found by calibration (null = calibrate).</summary>
    public double? MinimapRatio { get; set; }
}

/// <summary>Where the player is, for every tool's maps: follows the position while a map is
/// open. What a tool draws on top of it lives in its <see cref="MapLayer"/>.</summary>
public sealed class MapHub
{
    readonly AppState app;
    readonly string file;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    int open;
    MinimapTracker? minimap;
    (double X, double Y, DateTime At)? minimapFix;
    (double X, double Y)? network;
    public MapHub(AppState app, string root)
    {
        this.app = app;
        Mini = new SharedMiniMap(this);
        file = Path.Combine(root, "map-settings.json");
        try
        {
            if (File.Exists(file))
                Preferences = JsonSerializer.Deserialize<MapPreferences>(File.ReadAllText(file)) ?? new();
        }
        catch (Exception)
        {
            Preferences = new();
        }

        Learned = new LearnedSpawns(root);
        app.Positions.Appeared += (region, entity, npc, x, y) =>
        {
            if (Preferences.LearnSpawns && Learned.See(region, entity, npc, x, y))
                Changed?.Invoke();
        };
        timer.Tick += (_, _) => Poll();
    }

    public MapPreferences Preferences { get; } = new();
    public SharedMiniMap Mini { get; }
    public LearnedSpawns Learned { get; }
    public LiveMapView? Live { get; private set; }
    public string? Error { get; private set; }
    /// <summary>The region is remembered, not confirmed by a zone frame or nearby spawns.</summary>
    public bool Assumed { get; private set; }
    public string Region { get; private set; } = "verteron";
    public (double X, double Y)? Player { get; private set; }
    /// <summary>Where the shown position comes from: Minimap, Exact (network), Estimated.</summary>
    public string Source { get; private set; } = "";
    public bool Precise => Source is "Minimap" or "Exact";
    public string MinimapStatus { get; private set; } = "";
    public WorldMap Map => WorldMaps.All[Region];
    public SpawnRegion Spawns => SpawnAtlas.Region(Region);
    public event Action? Changed;

    public void Attach()
    {
        if (open++ == 0)
        {
            timer.Start();
            SetMinimapReading(Preferences.ReadMinimap);
        }

        Poll();
    }

    public void Detach()
    {
        if (--open <= 0)
        {
            open = 0;
            timer.Stop();
            minimap?.Dispose();
            minimap = null;
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(Preferences));
        }
        catch (Exception)
        {
        }
    }

    public void ForgetLearned()
    {
        Learned.Forget();
        Changed?.Invoke();
    }

    public void SetMinimapReading(bool enabled)
    {
        Preferences.ReadMinimap = enabled;
        if (enabled && minimap == null && open > 0)
            minimap = new MinimapTracker(this);
        else if (!enabled && minimap != null)
        {
            minimap.Dispose();
            minimap = null;
            minimapFix = null;
            MinimapStatus = "";
        }
    }

    /// <summary>Forget the minimap scale (after changing resolution or minimap zoom).</summary>
    public void RecalibrateMinimap()
    {
        Preferences.MinimapRatio = null;
        Save();
        if (minimap != null)
        {
            minimap.Dispose();
            minimap = open > 0 && Preferences.ReadMinimap ? new MinimapTracker(this) : null;
        }
    }

    public void AcceptMinimap(MinimapFix? fix, string? status, string? region)
    {
        MinimapStatus = fix != null ? $"Minimap lock ({fix.Score:F2})" : status ?? "";
        if (fix != null && region == Region)
        {
            minimapFix = (fix.X, fix.Y, DateTime.UtcNow);
            (double X, double Y) next = (fix.X, fix.Y);
            // Halfway toward each match while it stays close: single matches wobble by a
            // couple of metres (icons and the view cone move over the minimap); a jump is taken at once.
            if (Source == "Minimap" && Player is { } shown && Math.Sqrt(Math.Pow(next.X - shown.X, 2) + Math.Pow(next.Y - shown.Y, 2)) * Map.Metres < 12)
                next = ((shown.X + next.X) / 2, (shown.Y + next.Y) / 2);
            Move(next, "Minimap");
        }

        Changed?.Invoke();
    }

    bool MinimapFresh => minimapFix is { } m && (DateTime.UtcNow - m.At).TotalSeconds < 1.5;

    void Move((double X, double Y) next, string source)
    {
        if (Player is { } previous)
        {
            double metres = Math.Sqrt(Math.Pow(next.X - previous.X, 2) + Math.Pow(next.Y - previous.Y, 2)) * Map.Metres;
            if (metres < 1.5 && source != "Minimap")
                next = previous;
        }

        Player = next;
        Source = source;
    }

    public void SetRegion(string region)
    {
        if (Region == region || !WorldMaps.All.ContainsKey(region))
            return;
        Region = region;
        Changed?.Invoke();
    }

    void Poll()
    {
        Error = app.CaptureProblem ? app.CaptureStatus : null;
        Accept(app.Positions.View(DateTimeOffset.UtcNow));
    }

    public void Accept(LiveMapView live)
    {
        // Zone 0 means no zone frame was seen (started mid-session) and no pet mob placed
        // the player yet: assume the last region a map was shown for until one does.
        string? region = live.Region ?? (live.Zone == 0 ? Preferences.LastRegion : null);
        Assumed = live.Region == null && region != null;
        Live = live;
        if (region != null)
            Region = region;
        if (live.Region != null && Preferences.LastRegion != live.Region)
        {
            Preferences.LastRegion = live.Region;
            Save();
        }

        if (region != null && live.X is float x && live.Y is float y)
        {
            network = Map.Normalize(x, y);
            minimap?.Update(new(region, network.Value.X, network.Value.Y, live.Exact));
            // A fresh minimap match is more precise than anything the network sends.
            if (!MinimapFresh)
                Move(network.Value, live.Exact ? "Exact" : "Estimated");
        }
        else if (region == null)
        {
            network = null;
            minimap?.Update(null);
            Player = null;
            Source = "";
        }

        Changed?.Invoke();
    }

    public static string Compass(double bearing) => new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[(int)Math.Round(bearing / 45) % 8];
}
