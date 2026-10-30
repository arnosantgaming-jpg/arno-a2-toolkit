namespace ArnoA2.App.Map;

public sealed record LiveMob(int Entity, int Npc, float X, float Y, DateTimeOffset SeenUtc);
public sealed record LiveMapMob(int Entity, int Npc, int PetId, string Name, float X, float Y);
public sealed record LiveMapView(int Zone, string? Region, float? X, float? Y, bool Exact, double AgeSeconds, IReadOnlyList<LiveMapMob> Mobs);

/// <summary>World-to-map transform of the bundled region maps: pixel = offset + scale * world
/// (no flip or rotation), normalized by the full tile span. Calibration is the upstream
/// SkeeveAN/Aion-DPS-Meter fit (MIT) for the same tiles; zone ids from its zone table.</summary>
/// <param name="Background">Colour around the tiles: the sea, or the Abyss's void (the tiles' own edge colour).</param>
/// <param name="VoidGray">The game's minimap draws this map's void pale grey where the tiles have it dark:
/// the tiles' void brightness, painted over the minimap's void before matching (null = no void).</param>
public sealed record WorldMap(string Key, int Tile, int Grid, double Scale, double OffsetX, double OffsetY, string Background = "#0E1A22", float? VoidGray = null)
{
    public double Span => Tile * Grid;
    public (double X, double Y) Normalize(double worldX, double worldY) => ((OffsetX + Scale * worldX) / Span, (OffsetY + Scale * worldY) / Span);
    public (double X, double Y) World(double x, double y) => ((x * Span - OffsetX) / Scale, (y * Span - OffsetY) / Scale);
    /// <summary>Metres per whole-map unit (game units are centimetres).</summary>
    public double Metres => Span / Scale / 100;
}

public static class WorldMaps
{
    public static IReadOnlyDictionary<string, WorldMap> All { get; } = new Dictionary<string, WorldMap>
    {
        ["verteron"] = new("verteron", 2048, 8, 0.019583509634873143, 8218.937975984913, 8147.450753846877),
        ["altgard"] = new("altgard", 1024, 8, 0.0098227439264612, 4137.781824319782, 4062.9907075320293),
        // Reshanta (Abyss_Reshanta_A): four tiles in the middle of a 4×4 grid.
        ["abyss"] = new("abyss", 1024, 4, 0.009878286530741168, 2047.1930445744983, 2056.9061147139305, "#1E272C", 37)
    };

    /// <summary>Maps in the order the full map offers them.</summary>
    public static string[] Order { get; } = { "verteron", "altgard", "abyss" };

    /// <summary>Maps that may be entered under zone ids not listed here (the Abyss has several
    /// layers): recognised from their pet creatures in any zone without a map.</summary>
    public static IReadOnlySet<string> Unzoned { get; } = new HashSet<string> { "abyss" };

    static readonly Lazy<Dictionary<string, ILookup<int, SpawnPoint>>> spawns = new(() => SpawnAtlas.Regions.Where(r => All.ContainsKey(r.Key)).ToDictionary(r => r.Key, r => r.Points.ToLookup(p => p.NpcId)));

    /// <summary>The region whose spawn table places these mobs: each mob within 40 m of a
    /// spawn point of its own NPC votes for that region. Used when the zone frame was missed
    /// (tracker started after login), or a map without known zone ids. Null without at least
    /// two agreeing mobs.</summary>
    /// <param name="among">Only these maps (null = all).</param>
    public static string? Detect(IEnumerable<LiveMob> mobs, IReadOnlySet<string>? among = null)
    {
        var votes = new Dictionary<string, int>();
        foreach (var mob in mobs)
            foreach (var (key, table) in spawns.Value)
            {
                if (among != null && !among.Contains(key))
                    continue;
                var map = All[key];
                var (x, y) = map.Normalize(mob.X, mob.Y);
                double limit = 40 / map.Metres;
                if (table[mob.Npc].Any(p => Math.Abs(p.X - x) <= limit && Math.Abs(p.Y - y) <= limit))
                    votes[key] = votes.GetValueOrDefault(key) + 1;
            }

        var best = votes.OrderByDescending(v => v.Value).FirstOrDefault();
        return best.Value >= 2 ? best.Key : null;
    }

    // The legion airships hover above their region and use its coordinates.
    public static string? RegionOfZone(int zone) => zone switch
    {
        1010 or 902 => "verteron",
        1110 or 901 => "altgard",
        20 => "abyss", // Reshanta, seen live on 2026-10-30
        _ => null
    };
}
