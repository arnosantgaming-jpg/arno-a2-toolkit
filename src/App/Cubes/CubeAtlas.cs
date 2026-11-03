using System.Text.Json;
using Avalonia.Media;

namespace ArnoA2.App.Map;

/// <param name="Group">The cube this is a possible place of: the index of its group on the map.</param>
public sealed record CubeSpot(int Group, double X, double Y);

/// <summary>Every place a Hidden Cube can appear, from the game client (data/CubeSpots.json, see
/// THIRD-PARTY-NOTICES.txt). The server picks one spot per group, so none of these is a live cube.</summary>
public static class CubeAtlas
{
    sealed record Group(string Area, double[][] Spots);

    public static readonly Color Red = Color.Parse("#FF3B3B");
    static readonly Dictionary<string, Group[]> Data = Load();
    static readonly Dictionary<string, IReadOnlyList<CubeSpot>> Placed = new();

    /// <summary>The map's spots, placed by the same transform as the player marker.</summary>
    public static IReadOnlyList<CubeSpot> Region(string key)
    {
        if (Placed.TryGetValue(key, out var spots))
            return spots;
        var map = WorldMaps.All[key];
        return Placed[key] = Data.GetValueOrDefault(key, Array.Empty<Group>()).SelectMany((group, id) => group.Spots.Select(s =>
        {
            var (x, y) = map.Normalize(s[0], s[1]);
            return new CubeSpot(id, x, y);
        })).ToArray();
    }

    public static string Area(string region, int group) => Data[region][group].Area;

    /// <summary>The cube spot at a game position, to within half a metre.</summary>
    public static CubeSighting? Exact(double x, double y)
    {
        foreach (var (region, groups) in Data)
            foreach (var group in groups)
                foreach (var spot in group.Spots)
                    if (Math.Abs(spot[0] - x) <= 50 && Math.Abs(spot[1] - y) <= 50)
                        return new(region, group.Area, spot[0], spot[1]);
        return null;
    }

    static Dictionary<string, Group[]> Load()
    {
        using var stream = typeof(CubeAtlas).Assembly.GetManifestResourceStream("cubes.json") ?? throw new InvalidDataException("Missing cube data");
        return JsonSerializer.Deserialize<Dictionary<string, Group[]>>(stream) ?? throw new InvalidDataException("Empty cube data");
    }
}
