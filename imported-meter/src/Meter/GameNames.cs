using System.Reflection;

namespace ArnoA2.Meter;

public enum NpcKind
{
    Other,
    Boss,
    Dummy
}

public sealed record Npc(string Name, NpcKind Kind);

/// <summary>NPC and skill names from the game's tables. A skill has an id for each level
/// and specialisation, all under one name.</summary>
public static class GameNames
{
    static readonly Lazy<Dictionary<int, Npc>> Npcs = new(() => Read("npcs.tsv").ToDictionary(f => int.Parse(f[0]), f => new Npc(f[2], f[1] switch
    {
        "b" => NpcKind.Boss,
        "d" => NpcKind.Dummy,
        _ => NpcKind.Other
    })));

    static readonly Lazy<Dictionary<uint, string>> Skills = new(() => Read("skills.tsv").ToDictionary(f => uint.Parse(f[0]), f => f[1]));

    public static Npc? FindNpc(int code) => Npcs.Value.GetValueOrDefault(code);

    public static string Skill(uint skill) => Skills.Value.GetValueOrDefault(skill) ?? "Skill " + skill;

    static IEnumerable<string[]> Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException(name + " is missing");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            yield return line.Split('\t');
    }
}
