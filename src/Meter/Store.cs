using System.Text.Json;

namespace ArnoA2.Meter;

public sealed class Settings
{
    /// <summary>Seconds without a hit that end a fight.</summary>
    public int MeterQuietSeconds { get; set; } = 8;
    /// <summary>Your character's name, from your last login: it recognises you when the
    /// game names you, without seeing a login.</summary>
    public string? Character { get; set; }

    public void Normalize() => MeterQuietSeconds = Math.Clamp(MeterQuietSeconds, 3, 60);
}

/// <summary>Names the game sent on one connection. It sends a name only as a player comes
/// into view, and entity ids hold for the connection, so a restart can pick these up.</summary>
public sealed class SavedNames
{
    public string Connection { get; set; } = "";
    public Dictionary<int, string> Names { get; set; } = new();
}

/// <summary>Atomic JSON storage in the user's data folder.</summary>
public sealed class Store(string folder)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string Folder { get; } = folder;
    string File => Path.Combine(Folder, "settings.json");
    string NamesFile => Path.Combine(Folder, "names.json");

    public static string DefaultFolder() => Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } home ? home : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "arno-dps-meter");

    public Settings Load()
    {
        try
        {
            if (System.IO.File.Exists(File) && JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File), Json) is { } settings)
            {
                settings.Character ??= FromPetTracker();
                return settings;
            }
        }
        catch (JsonException)
        {
            // a damaged file falls back to the defaults
        }

        return new Settings { Character = FromPetTracker() };
    }

    // First run: Arno's Pet Tracker may already know the character.
    string? FromPetTracker()
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(Folder)!, "arno-pet-tracker", "data.json");
            if (!System.IO.File.Exists(path))
                return null;
            using var saved = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            string? active = saved.RootElement.GetProperty("ActiveProfile").GetString();
            foreach (var profile in saved.RootElement.GetProperty("Profiles").EnumerateArray())
                if (profile.GetProperty("Id").GetString() == active && profile.GetProperty("Characters") is { ValueKind: JsonValueKind.Array } names && names.GetArrayLength() == 1)
                    return names[0].GetString();
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // only a head start: the next login names the character anyway
        }

        return null;
    }

    public void Save(Settings settings) => Write(File, settings);

    public void Save(SavedNames names) => Write(NamesFile, names);

    public SavedNames? LoadNames()
    {
        try
        {
            return System.IO.File.Exists(NamesFile) ? JsonSerializer.Deserialize<SavedNames>(System.IO.File.ReadAllText(NamesFile), Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Folder);
        string temp = path + ".tmp";
        System.IO.File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        System.IO.File.Move(temp, path, overwrite: true);
    }
}
