namespace ArnoA2.Protocol;

public enum PlayerClass
{
    Gladiator,
    Templar,
    Assassin,
    Ranger,
    Sorcerer,
    Spiritmaster,
    Cleric,
    Chanter,
    Fighter
}

public static class PlayerClasses
{
    public static string Name(PlayerClass job) => job.ToString();

    /// <summary>The class field of a self record: each class has a block of four values.</summary>
    public static PlayerClass? FromRoster(uint value) => value switch
    {
        >= 5 and <= 8 => PlayerClass.Gladiator,
        >= 9 and <= 12 => PlayerClass.Templar,
        >= 13 and <= 16 => PlayerClass.Ranger,
        >= 17 and <= 20 => PlayerClass.Assassin,
        >= 21 and <= 24 => PlayerClass.Spiritmaster,
        >= 25 and <= 28 => PlayerClass.Sorcerer,
        >= 29 and <= 32 => PlayerClass.Cleric,
        >= 33 and <= 36 => PlayerClass.Chanter,
        _ => null
    };

    /// <summary>The class a skill belongs to. Skills are 8 digits, class first; a zero in
    /// the next two digits is a generic skill any creature can use, and most of the
    /// Spiritmaster's block belongs to spirits, so only its player ranges count.</summary>
    public static PlayerClass? FromSkill(uint skill)
    {
        if (skill is (>= 100_510 and <= 103_500) or (>= 109_300 and <= 109_362))
            return PlayerClass.Spiritmaster;
        if (skill is < 10_000_000 or >= 20_000_000)
            return null;
        uint prefix = skill / 1_000_000, sub = skill / 10_000 % 100;
        if (sub == 0)
            return prefix == 16 && skill / 100 % 100 is >= 11 and <= 13 ? PlayerClass.Spiritmaster : null;
        if (prefix == 16)
            return sub is (>= 1 and <= 8) or 14 or 15 or 17 or 19 or (>= 21 and <= 26) or 30 or 31 or 32 or 34 or 35 or 36 or 37 or (>= 70 and <= 76) or 80 ? PlayerClass.Spiritmaster : null;
        return prefix switch
        {
            11 => PlayerClass.Gladiator,
            12 => PlayerClass.Templar,
            13 => PlayerClass.Assassin,
            14 => PlayerClass.Ranger,
            15 => PlayerClass.Sorcerer,
            17 => PlayerClass.Cleric,
            18 => PlayerClass.Chanter,
            19 => PlayerClass.Fighter,
            _ => null
        };
    }
}
