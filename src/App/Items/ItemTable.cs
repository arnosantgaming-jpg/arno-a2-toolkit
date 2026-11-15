namespace ArnoA2.App;

public sealed record ItemInfo(string Name, string Category, string Grade)
{
    /// <summary>The broad kind the item log filters by.</summary>
    public string Type => ItemTable.TypeOf(Category);
}

/// <summary>Item names, categories and grades from aion2data.com's item list (data/Items.tsv,
/// refreshed with tools/update-items.sh; see THIRD-PARTY-NOTICES.txt).</summary>
public static class ItemTable
{
    public const string Source = "aion2data.com", SourcePage = "https://aion2data.com";

    /// <summary>Grades from plainest to rarest, with the colour the game gives each.</summary>
    public static readonly (string Grade, string Color)[] Grades =
    {
        ("Common", "#C3C3C3"), ("Rare", "#19E048"), ("Epic", "#379AFF"), ("Unique", "#FFD02B"), ("Heroic", "#FF6C00"), ("Special", "#00FFD8")
    };

    public static readonly string[] Types = { "Currency", "Materials", "Consumables", "Gear", "Pets and souls", "Other" };

    static readonly HashSet<string> Gear = new()
    {
        "Amulet", "Arcana", "Belt", "Bell", "Bow", "Bracelet", "Chalice", "Cloak", "Compass", "Dagger", "Earrings", "Fist", "Gloves", "Greatsword", "Guard", "Helm", "Legs",
        "Longsword", "Mace", "Manastone", "Mirror", "Necklace", "Orb", "Pauldrons", "Ring", "Rune", "Shoes", "Spellbook", "Staff", "Theostone", "Top"
    };

    static readonly HashSet<string> Materials = new()
    {
        "Power Shard", "Potential Enhance Stone", "Upgrade", "Soul Crystal", "Philosopher's Stone", "Transfer Stone", "Empyrean Trace", "Substance Morph Design", "Parchment"
    };

    static readonly HashSet<string> Consumables = new() { "Potion", "Food", "Beverage", "Scroll", "Consumables", "Teleport", "Kisk", "Rebirth", "Key", "Quna Recharge" };

    static readonly Dictionary<uint, ItemInfo> Items = Load();

    public static int Count => Items.Count;

    /// <summary>The item, or a stand-in naming its number when the list doesn't have it.</summary>
    public static ItemInfo Get(uint item) => Items.TryGetValue(item, out var info) ? info : new($"Item {item}", "", "Common");

    public static string ColorOf(string grade) => Grades.FirstOrDefault(g => g.Grade == grade).Color ?? Grades[0].Color;

    public static string TypeOf(string category) => category switch
    {
        "Currencies" or "Special Points" => "Currency",
        "Pet" or "Pet Transformation" => "Pets and souls",
        _ when Gear.Contains(category) => "Gear",
        _ when Consumables.Contains(category) => "Consumables",
        _ when Materials.Contains(category) || category.Contains("Material") => "Materials",
        _ => "Other"
    };

    static Dictionary<uint, ItemInfo> Load()
    {
        using var stream = typeof(ItemTable).Assembly.GetManifestResourceStream("items.tsv") ?? throw new InvalidDataException("Missing item data");
        using var reader = new StreamReader(stream);
        var items = new Dictionary<uint, ItemInfo>();
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length == 4 && uint.TryParse(parts[0], out uint id))
                items[id] = new(parts[1], parts[2], parts[3]);
        }

        return items;
    }
}
