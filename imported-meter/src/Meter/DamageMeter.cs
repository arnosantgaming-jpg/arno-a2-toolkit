using ArnoA2.Protocol;

namespace ArnoA2.Meter;

public sealed record MeterRow(int Actor, bool You, long Damage, int Hits, int Biggest, double Dps, double Share)
{
    /// <summary>The character's name, once the game has sent it.</summary>
    public string? Name { get; init; }
    public PlayerClass? Class { get; init; }
    public long? CombatPower { get; init; }
}

public sealed class SkillTally
{
    public long Damage { get; internal set; }
    public int Hits { get; internal set; }
    public int Crits { get; internal set; }
    public int Biggest { get; internal set; }
}

/// <summary>
/// Damage per character, from the skill hits the game sends. A fight starts with the first
/// hit and ends after a quiet spell, and stays readable until the next one starts; the
/// session adds up every fight since the last reset. Damage per second uses time in
/// combat, so waiting between fights does not lower it.
/// </summary>
public sealed class DamageMeter
{
    public sealed class Tally
    {
        sealed class Actor
        {
            public long Damage;
            public int Hits, Biggest;
            public readonly Dictionary<uint, SkillTally> Skills = new();
        }

        readonly Dictionary<int, Actor> actors = new();
        readonly Dictionary<int, long> targets = new();
        Tally? bosses;
        DateTimeOffset? last;

        public double Seconds { get; private set; }
        public bool Incomplete { get; internal set; }
        public bool Empty => actors.Count == 0;
        public long Damage => actors.Values.Sum(a => a.Damage);
        public DateTimeOffset? Start { get; private set; }
        /// <summary>The fight is over and has gone to the history.</summary>
        public bool Ended { get; internal set; }
        /// <summary>What died during this tally.</summary>
        public HashSet<int> Deaths { get; } = new();
        public bool HasBoss => bosses != null;
        /// <summary>The same tally, counting only damage to bosses.</summary>
        public Tally Bosses => bosses ??= new();
        /// <summary>What took the most damage.</summary>
        public int? MainTarget => targets.Count == 0 ? null : targets.MaxBy(t => t.Value).Key;

        public IReadOnlyDictionary<uint, SkillTally> Skills(int actor) => actors.TryGetValue(actor, out var a) ? a.Skills : new Dictionary<uint, SkillTally>();

        internal void Add(int actor, int damage, DateTimeOffset now, TimeSpan quiet, int target, uint skill, bool crit, bool boss)
        {
            if (boss)
                Bosses.Add(actor, damage, now, quiet, target, skill, crit, false);
            Start ??= now;
            if (last is { } previous && now > previous && now - previous < quiet)
                Seconds += (now - previous).TotalSeconds;
            if (last == null || now > last)
                last = now;
            if (!actors.TryGetValue(actor, out var a))
                actors[actor] = a = new();
            a.Damage += damage;
            a.Hits++;
            a.Biggest = Math.Max(a.Biggest, damage);
            targets[target] = targets.GetValueOrDefault(target) + damage;
            if (!a.Skills.TryGetValue(skill, out var used))
                a.Skills[skill] = used = new();
            used.Damage += damage;
            used.Hits++;
            used.Crits += crit ? 1 : 0;
            used.Biggest = Math.Max(used.Biggest, damage);
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

    /// <summary>A fight finished; it stays on the meter until the next one starts.</summary>
    public event Action<Tally>? FightEnded;

    public void Hit(int actor, int damage, DateTimeOffset now, TimeSpan quiet, int target = 0, uint skill = 0, bool crit = false, bool boss = false)
    {
        if (Fight.Quiet(now, quiet))
        {
            Finish();
            Fight = new();
        }

        Fight.Add(actor, damage, now, quiet, target, skill, crit, boss);
        Session.Add(actor, damage, now, quiet, target, skill, crit, boss);
    }

    /// <summary>Ends the fight once it has gone quiet.</summary>
    public void Tick(DateTimeOffset now, TimeSpan quiet)
    {
        if (Fight.Quiet(now, quiet))
            Finish();
    }

    public void Finish()
    {
        if (Fight.Empty || Fight.Ended)
            return;
        Fight.Ended = true;
        FightEnded?.Invoke(Fight);
    }

    public void Died(int entity) => Fight.Deaths.Add(entity);

    /// <summary>Hits were lost: what is on the meter is a lower bound.</summary>
    public void Interrupt()
    {
        foreach (var tally in new[] { Fight, Session })
        {
            tally.Incomplete = !tally.Empty;
            if (tally.HasBoss)
                tally.Bosses.Incomplete = !tally.Bosses.Empty;
        }
    }

    public void Reset()
    {
        Finish();
        Fight = new();
        Session = new();
    }
}
