using System.Text.Json;
using ArnoA2.Protocol;

namespace ArnoA2.Meter;

public sealed class Settings
{
    /// <summary>Seconds without a hit that end a fight.</summary>
    public int MeterQuietSeconds { get; set; } = 8;
    /// <summary>Your character's name, from your last login: it recognises you when the
    /// game names you, without seeing a login.</summary>
    public string? Character { get; set; }

    /// <summary>How solid the meter's background is, in percent. Text and bars stay solid.</summary>
    public int MeterOpacity { get; set; } = 50;
    /// <summary>Count only damage to bosses while a boss is being fought.</summary>
    public bool MeterBossOnly { get; set; } = true;
    /// <summary>Where the meter was left, in screen pixels.</summary>
    public int? MeterX { get; set; }
    public int? MeterY { get; set; }
    public double? MeterWidth { get; set; }
    public double? MeterHeight { get; set; }

    public void Normalize()
    {
        MeterQuietSeconds = Math.Clamp(MeterQuietSeconds, 3, 60);
        MeterOpacity = Math.Clamp(MeterOpacity, 20, 100);
    }
}

/// <summary>Names the game sent on one connection. It sends a name only as a player comes
/// into view, and entity ids hold for the connection, so a restart can pick these up.</summary>
public sealed class SavedNames
{
    public string Connection { get; set; } = "";
    public Dictionary<int, string> Names { get; set; } = new();
    public Dictionary<int, long> CombatPower { get; set; } = new();
    /// <summary>The row you marked as yours.</summary>
    public int? Self { get; set; }
}

public sealed record FightSkill(string Name, long Damage, int Hits, int Crits, int Biggest);

public sealed record FightPlayer(int Actor, string? Name, PlayerClass? Class, bool You, long Damage, int Hits, int Biggest, double Dps, double Share, IReadOnlyList<FightSkill> Skills, long? CombatPower = null);

/// <summary>One fight in the history: boss fights count damage to bosses only.</summary>
public sealed record FightRecord(string Id, DateTimeOffset Start, double Seconds, string Target, bool Boss, bool Killed, long Damage, IReadOnlyList<FightPlayer> Players);

/// <summary>Atomic JSON storage in the user's data folder.</summary>
public sealed class Store(string folder)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string Folder { get; } = folder;
    string File => Path.Combine(Folder, "settings.json");
    string NamesFile => Path.Combine(Folder, "names.json");
    string HistoryFolder => Path.Combine(Folder, "history");
    // Enough for months of bosses, and trash cannot push them out.
    const int KeepBossFights = 500, KeepOtherFights = 200;

    public static string DefaultFolder() => Path.Combine(OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } home ? home : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "arno-a2-toolkit", "meter");

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

    // First run: the Pets module may already know the character.
    string? FromPetTracker()
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(Folder)!, "pets", "data.json");
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

    public void Save(FightRecord fight)
    {
        Directory.CreateDirectory(HistoryFolder);
        Write(Path.Combine(HistoryFolder, fight.Id + ".json"), fight);
        string prefix = fight.Boss ? "boss-" : "other-";
        foreach (string old in Directory.GetFiles(HistoryFolder, prefix + "*.json").OrderDescending(StringComparer.Ordinal).Skip(fight.Boss ? KeepBossFights : KeepOtherFights))
            System.IO.File.Delete(old);
    }

    /// <summary>Saved fights, newest first.</summary>
    public IReadOnlyList<FightRecord> LoadFights()
    {
        if (!Directory.Exists(HistoryFolder))
            return [];
        var fights = new List<FightRecord>();
        foreach (string path in Directory.GetFiles(HistoryFolder, "*.json"))
            try
            {
                if (JsonSerializer.Deserialize<FightRecord>(System.IO.File.ReadAllText(path), Json) is { } fight)
                    fights.Add(fight);
            }
            catch (JsonException)
            {
                // a damaged file is one lost fight
            }

        return fights.OrderByDescending(f => f.Start).ToArray();
    }

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
