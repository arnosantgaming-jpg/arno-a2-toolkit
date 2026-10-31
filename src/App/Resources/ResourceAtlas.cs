using System.Text.Json;
using Avalonia.Media;

namespace ArnoA2.App.Map;

public sealed record ResourceName(string Name, string Kind);
/// <param name="Name">Index into <see cref="ResourceAtlas.Names"/>.</param>
public sealed record ResourceNode(int Name, double X, double Y);

/// <summary>Gathering node positions from the game client (data/ResourceNodes.json, see
/// THIRD-PARTY-NOTICES.txt). Places to check, not live nodes: they are shared and respawn.</summary>
public static class ResourceAtlas
{
    sealed record File(ResourceName[] Names, Dictionary<string, double[][]> Regions);

    static readonly File Data = Load();
    static readonly Dictionary<string, IReadOnlyList<ResourceNode>> Placed = new();

    /// <summary>Every kind and its marker, in the order the tool lists them.</summary>
    public static readonly (string Kind, Color Color, MarkerShape Shape)[] Kinds =
    {
        ("Odyle", Color.Parse("#39FF7A"), MarkerShape.Dot), ("Ore", Color.Parse("#C8CCD4"), MarkerShape.Diamond), ("Gems", Color.Parse("#E46CFF"), MarkerShape.Diamond), ("Herbs", Color.Parse("#A5D66A"), MarkerShape.Leaf),
        ("Vegetables", Color.Parse("#FFB74D"), MarkerShape.Square), ("Fruit", Color.Parse("#FF6E8A"), MarkerShape.Drop), ("Wood", Color.Parse("#C49A6C"), MarkerShape.Triangle), ("Shellfish", Color.Parse("#4FD6C8"), MarkerShape.Fan)
    };

    public static IReadOnlyList<ResourceName> Names => Data.Names;

    /// <summary>The region's nodes, placed by the same transform as the player marker.</summary>
    public static IReadOnlyList<ResourceNode> Region(string key)
    {
        if (Placed.TryGetValue(key, out var nodes))
            return nodes;
        var map = WorldMaps.All[key];
        return Placed[key] = Data.Regions.GetValueOrDefault(key, Array.Empty<double[]>()).Select(n =>
        {
            var (x, y) = map.Normalize(n[1], n[2]);
            return new ResourceNode((int)n[0], x, y);
        }).ToArray();
    }

    static File Load()
    {
        using var stream = typeof(ResourceAtlas).Assembly.GetManifestResourceStream("resources.json") ?? throw new InvalidDataException("Missing resource data");
        return JsonSerializer.Deserialize<File>(stream) ?? throw new InvalidDataException("Empty resource data");
    }
}
