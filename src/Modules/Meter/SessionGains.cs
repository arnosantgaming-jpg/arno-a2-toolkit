namespace ArnoA2.Meter;

/// <summary>
/// What you have earned of each tracked currency since the session started: the rises of its
/// total, so spending doesn't take from it, or for Rank Points the net change, since a defeat
/// costs some. The game sends a total only when it changes, so the first change after the
/// toolkit starts only sets where it stands.
/// </summary>
public sealed class SessionGains
{
    /// <summary>Rank Points aren't an item; this stands in for one.</summary>
    public const uint RankPoints = 1;

    /// <summary>The currencies followed, by the item the game keeps them as. Net ones count losses too.</summary>
    public static readonly (uint Item, string Name, bool Net)[] Tracked = { (930100031, "Kinah", false), (930100001, "Bound Kinah", false), (930100003, "Abyss Points", false), (RankPoints, "Rank Points", true) };

    readonly Dictionary<uint, long> last = new(), gained = new();

    public SessionGains(DateTimeOffset start) => Since = start;

    public DateTimeOffset Since { get; private set; }

    public void Saw(uint item, long count)
    {
        if (Tracked.FirstOrDefault(t => t.Item == item) is not { Name: not null } tracked)
            return;
        if (last.TryGetValue(item, out long before) && (count > before || tracked.Net))
            gained[item] = gained.GetValueOrDefault(item) + count - before;
        last[item] = count;
    }

    public long Gained(uint item) => gained.GetValueOrDefault(item);

    /// <summary>Per hour since the session started, once a minute has passed.</summary>
    public double? PerHour(uint item, DateTimeOffset now) => now - Since is { TotalMinutes: >= 1 } span ? Gained(item) / span.TotalHours : null;

    /// <summary>Starts counting again from now.</summary>
    public void Restart(DateTimeOffset now)
    {
        gained.Clear();
        Since = now;
    }

    /// <summary>Forgets the totals (the game connection ended, or another character), so the next
    /// one isn't counted as a gain.</summary>
    public void Forget() => last.Clear();
}
