using System.Text.Json;

namespace ArnoA2.Meter;

public sealed class Settings
{
    /// <summary>Seconds without a hit that end a fight.</summary>
    public int MeterQuietSeconds { get; set; } = 8;

    public void Normalize() => MeterQuietSeconds = Math.Clamp(MeterQuietSeconds, 3, 60);
}

/// <summary>Atomic JSON storage in the user's data folder.</summary>
public sealed class Store(string folder)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string Folder { get; } = folder;
    string File => Path.Combine(Folder, "settings.json");

    public static string DefaultFolder() => Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } home ? home : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "arno-dps-meter");

    public Settings Load()
    {
        try
        {
            if (System.IO.File.Exists(File) && JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File), Json) is { } settings)
                return settings;
        }
        catch (JsonException)
        {
            // a damaged file falls back to the defaults
        }

        return new Settings();
    }

    public void Save(Settings settings)
    {
        Directory.CreateDirectory(Folder);
        string temp = File + ".tmp";
        System.IO.File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        System.IO.File.Move(temp, File, overwrite: true);
    }
}
