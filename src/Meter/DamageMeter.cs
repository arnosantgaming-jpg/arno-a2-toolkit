using ArnoA2.Protocol;

namespace ArnoA2.Meter;

public sealed record MeterRow(int Actor, bool You, long Damage, int Hits, int Biggest, double Dps, double Share)
{
    /// <summary>The character's name, once the game has sent it.</summary>
    public string? Name { get; init; }
    public PlayerClass? Class { get; init; }
}

/// <summary>
/// Damage per character, from the skill hits the game sends. A fight starts with the first
/// hit and ends after a quiet spell, and stays readable until the next one starts; the
/// session adds up every fight since the last reset. Damage per second uses time in
/// combat, so waiting between fights does not lower it. Nothing here is saved.
/// </summary>
public sealed class DamageMeter
{
    public sealed class Tally
    {
        sealed class Actor
        {
            public long Damage;
            public int Hits, Biggest;
        }

        readonly Dictionary<int, Actor> actors = new();
        DateTimeOffset? last;

        public double Seconds { get; private set; }
        public bool Incomplete { get; internal set; }
        public bool Empty => actors.Count == 0;
        public long Damage => actors.Values.Sum(a => a.Damage);

        internal void Add(int actor, int damage, DateTimeOffset now, TimeSpan quiet)
        {
            if (last is { } previous && now > previous && now - previous < quiet)
                Seconds += (now - previous).TotalSeconds;
            if (last == null || now > last)
                last = now;
            if (!actors.TryGetValue(actor, out var a))
                actors[actor] = a = new();
            a.Damage += damage;
            a.Hits++;
            a.Biggest = Math.Max(a.Biggest, damage);
        }

        /// <summary>Highest damage first. A fight shorter than a second counts as one second,
        /// so a single opening hit does not read as an enormous rate.</summary>
        public IReadOnlyList<MeterRow> Rows(int? local)
        {
            double total = Math.Max(1, Damage), seconds = Math.Max(1, Seconds);
            return actors.OrderByDescending(a => a.Value.Damage).ThenBy(a => a.Key)
                .Select(a => new MeterRow(a.Key, a.Key == local, a.Value.Damage, a.Value.Hits, a.Value.Biggest, a.Value.Damage / seconds, a.Value.Damage / total)).ToArray();
        }

        public bool Quiet(DateTimeOffset now, TimeSpan quiet) => last is { } previous && now - previous >= quiet;
    }

    public Tally Fight { get; private set; } = new();
    public Tally Session { get; private set; } = new();

    public void Hit(int actor, int damage, DateTimeOffset now, TimeSpan quiet)
    {
        if (Fight.Quiet(now, quiet))
            Fight = new();
        Fight.Add(actor, damage, now, quiet);
        Session.Add(actor, damage, now, quiet);
    }

    /// <summary>Hits were lost: what is on the meter is a lower bound.</summary>
    public void Interrupt()
    {
        Fight.Incomplete = !Fight.Empty;
        Session.Incomplete = !Session.Empty;
    }

    public void Reset()
    {
        Fight = new();
        Session = new();
    }
}
