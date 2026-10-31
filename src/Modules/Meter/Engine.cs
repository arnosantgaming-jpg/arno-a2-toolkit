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
    readonly Dictionary<uint, string> connections = new();
    readonly Dictionary<int, int> owners = new();
    readonly Dictionary<int, int> npcs = new();
    readonly Dictionary<int, long> combatPower = new();
    readonly Dictionary<int, HashSet<int>> powers = new();
    readonly Dictionary<int, HashSet<uint>> skills = new();
    uint? flow;
    bool dirty, namesDirty, claimed;
    DateTimeOffset clock;

    public Engine(Store? store, Settings? initial = null)
    {
        this.store = store;
        Settings = initial ?? store?.Load() ?? new Settings();
        Settings.Normalize();
        Damage.FightEnded += Archive;
    }

    public DamageMeter Damage { get; } = new();
    public IReadOnlyList<FightRecord> History => store?.LoadFights() ?? [];
    /// <summary>Fights saved since the start, so the history can tell it is out of date.</summary>
    public int FightsSaved { get; private set; }
    public Settings Settings { get; }
    public int? LocalEntity { get; private set; }
    public string? Character { get; private set; }
    /// <summary>The character was recognised from game traffic without seeing its login.</summary>
    public bool Assumed { get; private set; }
    public long Frames => streams.Values.Sum(s => s.Frames);
    public long Gaps { get; private set; }

    public Connection Connection => flow == null ? Connection.Waiting : LocalEntity == null ? Connection.Identifying : Connection.Live;

    /// <param name="connection">What identifies the game connection, if known.</param>
    public void Opened(uint id, string? connection = null)
    {
        if (connection != null)
            connections[id] = connection;
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
        Damage.Finish();
        flow = null;
        LocalEntity = null;
        Character = null;
        Assumed = false;
        claimed = false;
        hints.Clear();
        names.Clear();
        classes.Clear();
        owners.Clear();
        npcs.Clear();
        combatPower.Clear();
        powers.Clear();
        skills.Clear();
    }

    /// <summary>A tally's rows with each character's name and class, where known.</summary>
    public IReadOnlyList<MeterRow> Rows(DamageMeter.Tally tally) =>
        tally.Rows(LocalEntity).GroupBy(r => Player(r.Actor)).Select(rows =>
        {
            int player = rows.Key;
            var job = classes.TryGetValue(player, out var own) ? own : rows.Select(r => classes.TryGetValue(r.Actor, out var used) ? used : (PlayerClass?)null).FirstOrDefault(c => c != null);
            return new MeterRow(player, player == LocalEntity, rows.Sum(r => r.Damage), rows.Sum(r => r.Hits), rows.Max(r => r.Biggest), rows.Sum(r => r.Dps), rows.Sum(r => r.Share)) { Name = Named(player), Class = job, CombatPower = combatPower.TryGetValue(player, out long power) ? power : null };
        }).OrderByDescending(r => r.Damage).ThenBy(r => r.Actor).ToArray();

    /// <summary>Called regularly to notice a fight has ended.</summary>
    public void Tick(DateTimeOffset now) => Damage.Tick(now, TimeSpan.FromSeconds(Settings.MeterQuietSeconds));

    /// <summary>What took the most damage in a tally.</summary>
    public string TargetName(DamageMeter.Tally tally) => tally.MainTarget is { } target && npcs.TryGetValue(target, out int code) && GameNames.FindNpc(code) is { } npc ? npc.Name : "";

    /// <summary>A tally as a finished fight: players, then each player's skills.</summary>
    public FightRecord Snapshot(DamageMeter.Tally tally, bool boss)
    {
        var players = Rows(tally).Select(row =>
        {
            var members = tally.Rows(LocalEntity).Where(r => Player(r.Actor) == row.Actor).Select(r => r.Actor);
            var skills = members.SelectMany(tally.Skills).GroupBy(s => GameNames.Skill(s.Key))
                .Select(s => new FightSkill(s.Key, s.Sum(x => x.Value.Damage), s.Sum(x => x.Value.Hits), s.Sum(x => x.Value.Crits), s.Max(x => x.Value.Biggest)))
                .OrderByDescending(s => s.Damage).ToArray();
            return new FightPlayer(row.Actor, row.Name, row.Class, row.You, row.Damage, row.Hits, row.Biggest, row.Dps, row.Share, skills, row.CombatPower);
        }).ToArray();
        var start = tally.Start ?? clock;
        return new FightRecord($"{(boss ? "boss" : "other")}-{start:yyyyMMdd-HHmmss-fff}", start, tally.Seconds, TargetName(tally), boss, false, tally.Damage, players);
    }

    void Archive(DamageMeter.Tally fight)
    {
        var tally = fight.HasBoss ? fight.Bosses : fight;
        if (tally.Seconds < 3)
            return;
        try
        {
            store?.Save(Snapshot(tally, fight.HasBoss) with { Killed = tally.MainTarget is { } target && fight.Deaths.Contains(target) });
            FightsSaved++;
        }
        catch (IOException)
        {
            // a full disk costs one history entry, not the meter
        }
    }

    // Recognised without a login, you still go by the name you last logged in with.
    string? Named(int entity) => names.GetValueOrDefault(entity) ?? (entity == LocalEntity ? Settings.Character : null);

    // Who a summon's damage belongs to: the owner its spawn named, or else one that shares
    // its power and uses at least three times its skills (a summon has a skill or two).
    int Player(int actor)
    {
        for (int steps = 0; steps < 8 && owners.TryGetValue(actor, out int owner); steps++)
            actor = owner;
        if (actor == LocalEntity || names.ContainsKey(actor) || !powers.TryGetValue(actor, out var own))
            return actor;
        int used = skills[actor].Count;
        var likely = powers.Where(p => p.Key != actor && !owners.ContainsKey(p.Key) && p.Value.Overlaps(own) && skills[p.Key].Count >= 3 * used).Select(p => p.Key).Take(2).ToArray();
        return likely.Length == 1 ? likely[0] : actor;
    }

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
            Restore(id);
        }

        if (id != flow)
            return;
        switch (message)
        {
            case CharacterIdentity identity:
                (LocalEntity, Character, Assumed, claimed) = (identity.Entity, identity.Name, false, false);
                hints.Clear();
                namesDirty |= names.GetValueOrDefault(identity.Entity) != identity.Name;
                names[identity.Entity] = identity.Name;
                if (Settings.Character != identity.Name)
                {
                    Settings.Character = identity.Name;
                    dirty = true;
                }

                if (identity.Class is { } job)
                    classes[identity.Entity] = job;
                Powered(identity.Entity, identity.Power);
                break;
            case PlayerSeen player:
                Named(player.Entity, player.Name);
                Powered(player.Entity, player.Power);
                break;
            case OwnerNamed owner:
                Named(owner.Owner, owner.Name);
                break;
            case NpcAppeared npc:
                npcs[npc.Entity] = npc.Npc;
                break;
            case EntityRemoved { Died: true } died:
                Damage.Died(died.Entity);
                break;
            case SummonSeen summon:
                owners[summon.Entity] = summon.Owner;
                break;
            case SkillHits skill:
                foreach (var hit in skill.Hits)
                {
                    Noted(hit);
                    Damage.Hit(hit.Actor, hit.Damage, clock, TimeSpan.FromSeconds(Settings.MeterQuietSeconds), hit.Target, hit.Skill, hit.Crit, IsBoss(hit.Target));
                    if (PlayerClasses.FromSkill(hit.Skill) is { } used)
                        classes[hit.Actor] = used;
                }

                break;
            case SelfHint hint:
                Hint(hint.Entity);
                break;
        }
    }

    bool IsBoss(int target) => npcs.TryGetValue(target, out int code) && GameNames.FindNpc(code)?.Kind == NpcKind.Boss;

    void Powered(int entity, long? power)
    {
        if (power is not { } known || combatPower.GetValueOrDefault(entity) == known)
            return;
        combatPower[entity] = known;
        namesDirty = true;
    }

    void Noted(SkillHit hit)
    {
        if (hit.Power is < 1000 or > 200_000)
            return;
        if (!powers.TryGetValue(hit.Actor, out var known))
            (powers[hit.Actor], skills[hit.Actor]) = (known = new(), new());
        // Buffs change power, but only to a handful of values.
        if (known.Count < 16)
            known.Add(hit.Power);
        skills[hit.Actor].Add(hit.Skill);
    }

    // The meter restarted on a connection it already knew: players in view are not sent again.
    void Restore(uint id)
    {
        if (!connections.TryGetValue(id, out string? connection) || store?.LoadNames() is not { } saved || saved.Connection != connection)
            return;
        foreach (var (entity, name) in saved.Names)
            Named(entity, name);
        foreach (var (entity, power) in saved.CombatPower)
            combatPower[entity] = power;
        if (saved.Self is { } self)
            (LocalEntity, claimed) = (self, true);
    }

    /// <summary>You said which row is yours: the game names you only at login, and in a
    /// crowd nothing else tells you apart. A login overrides this.</summary>
    public void Claim(int entity)
    {
        (LocalEntity, Assumed, claimed) = (entity, false, true);
        if (names.TryGetValue(entity, out string? name) && name != Settings.Character)
        {
            Settings.Character = name;
            dirty = true;
        }

        Character = Named(entity);
        namesDirty = true;
    }

    void Named(int entity, string name)
    {
        namesDirty |= names.GetValueOrDefault(entity) != name;
        // Ids are reused: a new name means a new character.
        if (names.TryGetValue(entity, out string? known) && known != name)
        {
            classes.Remove(entity);
            combatPower.Remove(entity);
            powers.Remove(entity);
            skills.Remove(entity);
        }

        names[entity] = name;
        owners.Remove(entity);
        if (name == Settings.Character)
            (LocalEntity, Character, Assumed) = (entity, name, false);
    }

    // Stat updates and a few other frames name the player's own entity far more often than
    // any other. A clear leader identifies the player when the login was missed.
    void Hint(int entity)
    {
        if (Character != null || claimed)
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
        foreach (var (entity, name) in names)
            if (name == Settings.Character)
                (LocalEntity, Character, Assumed) = (entity, name, false);
        dirty = true;
    }

    public bool SaveIfChanged()
    {
        if (namesDirty && flow is uint id && connections.TryGetValue(id, out string? connection))
        {
            store?.Save(new SavedNames { Connection = connection, Names = new(names), CombatPower = new(combatPower), Self = claimed ? LocalEntity : null });
            namesDirty = false;
        }

        if (!dirty)
            return false;
        store?.Save(Settings);
        dirty = false;
        return true;
    }
}
