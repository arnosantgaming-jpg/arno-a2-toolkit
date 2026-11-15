using ArnoA2.Protocol;

namespace ArnoA2.Meter;

/// <summary>One line of the item log: an item you obtained and how many.</summary>
public sealed record ObtainedItem(DateTimeOffset At, uint Item, long Amount);

/// <summary>
/// Items you obtain, from the bag's own changes rather than chat: a new stack counts in full,
/// a stack that grew counts by how much. The game sends a stack's count only when it changes,
/// so a stack you already had counts from its second change after the toolkit starts. A gain
/// right after a loss of the same item is the stack moving or splitting, so the loss is taken
/// off it. Repeats of one item a few seconds apart, like Kinah from a pack of mobs, join into
/// one line.
/// </summary>
public sealed class ItemLog
{
    public const int Kept = 200;
    static readonly TimeSpan Join = TimeSpan.FromSeconds(3), Moved = TimeSpan.FromSeconds(2);
    /// <summary>A stack of unknown size left: whatever comes back right after is that stack.</summary>
    const long Unknown = long.MaxValue / 4;
    readonly Dictionary<uint, long> stacks = new();
    readonly Dictionary<uint, (long Amount, DateTimeOffset At)> lost = new();
    readonly List<ObtainedItem> items = new();
    readonly object gate = new();

    /// <summary>Goes up with every change, so a window knows when to redraw.</summary>
    public int Version { get; private set; }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ObtainedItem> Items
    {
        get
        {
            lock (gate)
                return items.AsEnumerable().Reverse().ToArray();
        }
    }

    public void Saw(BagSlot slot, DateTimeOffset now)
    {
        lock (gate)
        {
            bool known = stacks.TryGetValue(slot.Stack, out long before);
            long gained = slot.Change == BagChange.Gone ? -(known ? before : Unknown) : known ? slot.Count - before : slot.Change == BagChange.New ? slot.Count : 0;
            if (slot.Change == BagChange.Gone)
                stacks.Remove(slot.Stack);
            else
                stacks[slot.Stack] = slot.Count;
            var recent = lost.TryGetValue(slot.Item, out var l) && now - l.At <= Moved ? l.Amount : 0;
            if (gained < 0)
            {
                lost[slot.Item] = (recent - gained, now);
                return;
            }

            long offset = Math.Min(recent, gained);
            if (offset > 0)
                lost[slot.Item] = (recent - offset, l.At);
            gained -= offset;
            if (gained <= 0)
                return;
            if (items.Count > 0 && items[^1] is var last && last.Item == slot.Item && now - last.At <= Join)
                items[^1] = last with { At = now, Amount = last.Amount + gained };
            else
                items.Add(new ObtainedItem(now, slot.Item, gained));
            if (items.Count > Kept)
                items.RemoveAt(0);
            Version++;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            items.Clear();
            Version++;
        }
    }

    /// <summary>Forgets the stack counts (the game connection ended, or another character), so
    /// the next change isn't counted as a gain.</summary>
    public void Forget()
    {
        lock (gate)
        {
            stacks.Clear();
            lost.Clear();
        }
    }
}
