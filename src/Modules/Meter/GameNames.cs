using System.Reflection;

namespace ArnoA2.Meter;

public enum NpcKind
{
    Other,
    Boss,
    Dummy
}

public sealed record Npc(string Name, NpcKind Kind);

/// <summary>A boss's or training dummy's HP.</summary>
public sealed record TargetHealth(string Name, int Hp, int MaxHp)
{
    public double Share => MaxHp > 0 ? Math.Clamp((double)Hp / MaxHp, 0, 1) : 0;
}

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

    static readonly Lazy<HashSet<uint>> Dots = new(() => Read("dots.tsv").Where(f => f[0].Length > 0).Select(f => uint.Parse(f[0])).ToHashSet());

    /// <summary>Marks a skill's ticks of damage over time, so they tally apart from its hits.</summary>
    public const uint DotTicks = 0x8000_0000;

    public static Npc? FindNpc(int code) => Npcs.Value.GetValueOrDefault(code);

    /// <summary>Whether a skill's ticks are damage over time, from A2Tools DPS Meter's list.</summary>
    public static bool IsDot(uint skill) => Dots.Value.Contains(skill);

    /// <summary>An id missing from the table falls back as A2Tools DPS Meter does: a Theostone
    /// proc comes with its table id cut short (3000017 for 30000171), and any other id goes to its
    /// base skill, the id with its last four digits zeroed.</summary>
    public static string Skill(uint skill) => (skill & DotTicks) != 0 ? Skill(skill & ~DotTicks) + " - DOT" : Skills.Value.GetValueOrDefault(skill)
        ?? (skill is >= 3_000_000 and <= 3_099_999 ? Skills.Value.GetValueOrDefault(skill * 10 + 1) : null)
        ?? Skills.Value.GetValueOrDefault(skill - skill % 10000) ?? "Skill " + skill;

    static IEnumerable<string[]> Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException(name + " is missing");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            yield return line.Split('\t');
    }
}
