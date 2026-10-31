using System.Text.Json;
using ArnoA2.Tracker;

namespace ArnoA2.App.Map;

public sealed record LearnedSpawn(string Region, int PetId, int NpcId, string Name, double X, double Y);
public sealed record SpawnSighting(string Region, int NpcId, int Entity, double X, double Y);

/// <summary>
/// Pet spawns the bundled data doesn't have, learned from play so a patch that moves them is
/// picked up. Mobs wander, so a place counts only when two different mobs of the same NPC
/// appear within <see cref="SameMetres"/> of each other, further than <see cref="NewMetres"/>
/// from every known spawn of that NPC. It can only add: a spawn a patch removed stays on the map.
/// </summary>
public sealed class LearnedSpawns
{
    public const double NewMetres = 40, SameMetres = 15;
    const int MaxSightings = 3000;

    sealed class Saved
    {
        public List<LearnedSpawn> Spawns { get; set; } = new();
        public List<SpawnSighting> Sightings { get; set; } = new();
    }

    readonly string file;
    readonly Saved saved = new();
    readonly Dictionary<string, ILookup<int, SpawnPoint>> known = new();
    DateTime lastSave;

    public LearnedSpawns(string root)
    {
        file = Path.Combine(root, "learned-spawns.json");
        try
        {
            if (File.Exists(file))
                saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(file)) ?? new();
        }
        catch (Exception)
        {
            saved = new();
        }
    }

    public int Count => saved.Spawns.Count;

    public IEnumerable<SpawnPoint> Region(string region) => saved.Spawns.Where(s => s.Region == region).Select(s => new SpawnPoint(s.PetId, s.NpcId, s.Name, s.X, s.Y));

    /// <summary>A pet creature came into view at this place. True when that taught a new spawn.</summary>
    public bool See(string region, int entity, int npc, double worldX, double worldY)
    {
        var data = GameData.Instance;
        if (!data.PetOfNpc.TryGetValue(npc, out int pet) || !WorldMaps.All.TryGetValue(region, out var map))
            return false;
        var (x, y) = map.Normalize(worldX, worldY);
        double Metres(double ax, double ay) => Math.Sqrt(Math.Pow(ax - x, 2) + Math.Pow(ay - y, 2)) * map.Metres;
        if (!known.TryGetValue(region, out var table))
            table = known[region] = SpawnAtlas.Region(region).Points.ToLookup(p => p.NpcId);
        if (table[npc].Any(p => Metres(p.X, p.Y) <= NewMetres) || saved.Spawns.Any(s => s.Region == region && s.NpcId == npc && Metres(s.X, s.Y) <= NewMetres))
            return false;

        var twin = saved.Sightings.FirstOrDefault(s => s.Region == region && s.NpcId == npc && s.Entity != entity && Metres(s.X, s.Y) <= SameMetres);
        if (twin == null)
        {
            if (saved.Sightings.Any(s => s.Region == region && s.Entity == entity && s.NpcId == npc))
                return false;
            saved.Sightings.Add(new(region, npc, entity, x, y));
            if (saved.Sightings.Count > MaxSightings)
                saved.Sightings.RemoveAt(0);
            Save(soon: false);
            return false;
        }

        saved.Sightings.RemoveAll(s => s.Region == region && s.NpcId == npc && Metres(s.X, s.Y) <= SameMetres);
        saved.Spawns.Add(new(region, pet, npc, data.NpcName(npc), (x + twin.X) / 2, (y + twin.Y) / 2));
        Save(soon: true);
        return true;
    }

    public void Forget()
    {
        saved.Spawns.Clear();
        saved.Sightings.Clear();
        Save(soon: true);
    }

    void Save(bool soon)
    {
        if (!soon && DateTime.UtcNow - lastSave < TimeSpan.FromSeconds(30))
            return;
        lastSave = DateTime.UtcNow;
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(saved));
        }
        catch (Exception)
        {
        }
    }
}
