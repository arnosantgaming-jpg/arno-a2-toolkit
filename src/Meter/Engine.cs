using ArnoA2.Protocol;

namespace ArnoA2.Meter;

public enum Connection
{
    /// <summary>No game connection seen yet.</summary>
    Waiting,
    /// <summary>Game connection found; the character is not known yet.</summary>
    Identifying,
    /// <summary>Character known.</summary>
    Live
}

/// <summary>
/// Turns the game stream into meter state. Feed it what the capture daemon sends
/// (<see cref="Opened"/>, <see cref="Data"/>, <see cref="Gap"/>, <see cref="Closed"/>);
/// read the results from its properties. Not thread-safe: call from one thread.
/// </summary>
public sealed class Engine
{
    readonly Store? store;
    readonly Dictionary<uint, FrameStream> streams = new();
    readonly Dictionary<int, int> hints = new();
    readonly Dictionary<int, string> names = new();
    readonly Dictionary<int, PlayerClass> classes = new();
    uint? flow;
    bool dirty;
    DateTimeOffset clock;

    public Engine(Store? store, Settings? initial = null)
    {
        this.store = store;
        Settings = initial ?? store?.Load() ?? new Settings();
        Settings.Normalize();
    }

    public DamageMeter Damage { get; } = new();
    public Settings Settings { get; }
    public int? LocalEntity { get; private set; }
    public string? Character { get; private set; }
    /// <summary>The character was recognised from game traffic without seeing its login.</summary>
    public bool Assumed { get; private set; }
    public long Frames => streams.Values.Sum(s => s.Frames);
    public long Gaps { get; private set; }

    public Connection Connection => flow == null ? Connection.Waiting : LocalEntity == null ? Connection.Identifying : Connection.Live;

    public void Opened(uint id)
    {
        var stream = new FrameStream();
        stream.FrameDecoded += frame => Frame(id, frame);
        streams[id] = stream;
    }

    public void Data(uint id, ReadOnlySpan<byte> bytes, DateTimeOffset now)
    {
        if (!streams.ContainsKey(id))
            Opened(id);
        clock = now;
        streams[id].Push(bytes);
    }

    public void Gap(uint id, long lost, DateTimeOffset now)
    {
        if (streams.TryGetValue(id, out var stream))
            stream.Reset();
        if (id != flow)
            return;
        Gaps++;
        Damage.Interrupt();
    }

    public void Closed(uint id, DateTimeOffset now)
    {
        streams.Remove(id);
        if (id != flow)
            return;
        // The game connection ended (logout, crash): nothing after this is continuous.
        Gap(id, 0, now);
        flow = null;
        LocalEntity = null;
        Character = null;
        Assumed = false;
        hints.Clear();
        names.Clear();
        classes.Clear();
    }

    /// <summary>A tally's rows with each character's name and class, where known.</summary>
    public IReadOnlyList<MeterRow> Rows(DamageMeter.Tally tally) =>
        tally.Rows(LocalEntity).Select(r => r with { Name = names.GetValueOrDefault(r.Actor), Class = classes.TryGetValue(r.Actor, out var job) ? job : null }).ToArray();

    void Frame(uint id, Frame frame)
    {
        var message = MessageDecoder.Decode(frame);
        if (message == null)
            return;
        // The game connection is the one that carries game messages; follow the newest.
        if (flow != id && message is CharacterIdentity or ZoneEntered || flow == null)
        {
            if (flow != null && flow != id)
                Gap(flow.Value, 0, clock);
            flow = id;
        }

        if (id != flow)
            return;
        switch (message)
        {
            case CharacterIdentity identity:
                (LocalEntity, Character, Assumed) = (identity.Entity, identity.Name, false);
                hints.Clear();
                names[identity.Entity] = identity.Name;
                if (identity.Class is { } job)
                    classes[identity.Entity] = job;
                break;
            case PlayerSeen player:
                // Ids are reused: a new name means a new character.
                if (names.TryGetValue(player.Entity, out string? known) && known != player.Name)
                    classes.Remove(player.Entity);
                names[player.Entity] = player.Name;
                break;
            case SkillHits skill:
                foreach (var hit in skill.Hits)
                {
                    Damage.Hit(hit.Actor, hit.Damage, clock, TimeSpan.FromSeconds(Settings.MeterQuietSeconds));
                    if (PlayerClasses.FromSkill(hit.Skill) is { } used)
                        classes[hit.Actor] = used;
                }

                break;
            case SelfHint hint:
                Hint(hint.Entity);
                break;
        }
    }

    // Stat updates and a few other frames name the player's own entity far more often than
    // any other. A clear leader identifies the player when the login was missed.
    void Hint(int entity)
    {
        if (Character != null)
            return;
        hints[entity] = hints.GetValueOrDefault(entity) + 1;
        int total = hints.Values.Sum();
        if (total < 12)
            return;
        var ranked = hints.OrderByDescending(h => h.Value).Take(2).ToArray();
        if (ranked[0].Value >= 6 && ranked[0].Value >= 3 * (ranked.Length > 1 ? ranked[1].Value : 0))
            (LocalEntity, Assumed) = (ranked[0].Key, true);
        if (total >= 400)
            hints.Clear();
    }

    public void ChangeSettings(Action<Settings> change)
    {
        change(Settings);
        Settings.Normalize();
        dirty = true;
    }

    public bool SaveIfChanged()
    {
        if (!dirty)
            return false;
        store?.Save(Settings);
        dirty = false;
        return true;
    }
}
