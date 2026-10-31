using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ArnoA2.Protocol;

/// <summary>A decoded game message. Positions are game units (centimetres).</summary>
public abstract record Message;

/// <summary>33:36 — the logged-in character (sent at login and zone entry).</summary>
public sealed record CharacterIdentity(int Entity, string Name, ushort Server, PlayerClass? Class = null, long? Power = null) : Message;

/// <summary>44:36 / 45:36 — another player came into view.</summary>
public sealed record PlayerSeen(int Entity, string Name, long? Power = null) : Message;

/// <summary>40:36 / 41:36 — a summon or spirit appeared, and who it belongs to.</summary>
public sealed record SummonSeen(int Entity, int Owner) : Message;

/// <summary>04:8D — a creature's kill and loot went to this player, or a summon's owner.
/// The game sends it for your own kills, so it names you without a login.</summary>
public sealed record OwnerNamed(int Target, int Owner, string Name, ushort Server) : Message;

/// <summary>21:36 — zone entered; the place is where the player appears.</summary>
public sealed record ZoneEntered(int Zone, float? X, float? Y) : Message;

/// <summary>00:90 — pet collection at login: equipped pet, owned ranks, current souls.</summary>
public sealed record CollectionSnapshot(int EquippedPet, IReadOnlyDictionary<int, int> Ranks, IReadOnlyDictionary<int, int> Souls) : Message;

/// <summary>0D:90 souls gained, 0E:90 souls spent (rank-up cost), per pet.</summary>
public sealed record SoulChange(bool Spent, IReadOnlyList<(int Pet, int Amount)> Entries) : Message;

/// <summary>14:90 — a pet reached a new rank.</summary>
public sealed record RankReached(int Pet, int Rank) : Message;

/// <summary>40–42:36 — an NPC came into view.</summary>
public sealed record NpcAppeared(int Entity, int Npc, float? X, float? Y) : Message;

/// <summary>42:36 (two-byte form) — an entity left view; reason 3 is death.</summary>
public sealed record EntityRemoved(int Entity, byte Reason) : Message
{
    public bool Died => Reason == 3;
}

/// <summary>3C:37 — an entity's exact position (the player's own after skills/hits).</summary>
public sealed record PositionReport(int Entity, float X, float Y) : Message;

/// <summary>xx:37 — another entity moved.</summary>
public sealed record EntityMoved(int Entity, float X, float Y) : Message;

/// <summary>2A:38 / 2B:38 — an effect was applied; the caster's position is attached.</summary>
public sealed record EffectCast(int Target, int Caster, float X, float Y) : Message;

/// <summary>One player skill's damage to one target.</summary>
/// <param name="Power">The actor's attack power, which a summon shares with its owner.</param>
public sealed record SkillHit(int Actor, int Target, int Damage, uint Skill = 0, int Power = 0, bool Crit = false);

/// <summary>04:38 — a player skill hit one target, or several for an area skill.</summary>
public sealed record SkillHits(IReadOnlyList<SkillHit> Hits) : Message;

/// <summary>Frames that, in captures, almost only ever name the player's own entity
/// (stat updates and a few others). Used to recognise the player mid-session.</summary>
public sealed record SelfHint(int Entity) : Message;

/// <summary>91:56 — Black Cloud Merchant stock for one tab.</summary>
public sealed record MerchantStock(int Tab, DateTimeOffset RefreshUtc, IReadOnlyList<MerchantOffer> Offers) : Message;

public sealed record MerchantOffer(int Offer, int Item, long Price);

/// <summary>93:56 — the merchant window's stock was invalidated.</summary>
public sealed record MerchantCleared : Message;

public static class MessageDecoder
{
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    // Comes just before the owner in a summon's spawn record.
    static readonly byte[] OwnerMark = [0x80, 0x75, 0xD5, 0x2A, 0xBB, 0x03, 0x00, 0x00];

    /// <summary>For a reader with no use for pets or merchants.</summary>
    public static Message? Decode(Frame frame) => Decode(frame, _ => false, default);

    /// <param name="knownPet">Validates pet ids in collection messages.</param>
    public static Message? Decode(Frame frame, Func<int, bool> knownPet, DateTimeOffset now)
    {
        var body = frame.Body.AsSpan();
        return (frame.Op, frame.Family) switch
        {
            (0x33, 0x36) => Identity(body),
            (0x21, 0x36) => Zone(body),
            (0x00, 0x90) => Collection(body, knownPet),
            (0x0D, 0x90) => Souls(body, knownPet, spent: false),
            (0x0E, 0x90) => Souls(body, knownPet, spent: true),
            (0x14, 0x90) => Rank(body, knownPet),
            (0x42, 0x36) => Removal(body) ?? Spawn(body),
            (0x40 or 0x41, 0x36) => Spawn(body),
            (0x44 or 0x45, 0x36) => Player(body),
            (0x04, 0x8D) => Owner(body),
            (0x3C, 0x37) => Position(body),
            (_, 0x37) when MoveOffset(frame.Op) is int offset => Moved(body, offset),
            (0x2A or 0x2B, 0x38) => Effect(body, renewed: frame.Op == 0x2B),
            (0x04, 0x38) => Hits(body),
            (0x00, 0x8D) or (0x44, 0x37) or (0x4A, 0x36) => Hint(body),
            (0x91, 0x56) => Merchant(body, now),
            (0x93, 0x56) => new MerchantCleared(),
            _ => null
        };
    }

    static bool Place(float x, float y, float limit) => float.IsFinite(x) && float.IsFinite(y) && MathF.Abs(x) < limit && MathF.Abs(y) < limit && (MathF.Abs(x) > 1 || MathF.Abs(y) > 1);

    // Name field: a flag byte whose low bit says a name follows, then length-prefixed UTF-8.
    static Message? Identity(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        if (!Named(ref r, out int entity, out string? name) || !r.U16(out ushort server))
            return null;
        // The class need not be there: some characters have other bytes in its place.
        var rest = r.Rest;
        return new CharacterIdentity(entity, name, server, r.U32(out uint job) ? PlayerClasses.FromRoster(job) : null, OwnPower(rest));
    }

    static Message? Player(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        return Named(ref r, out int entity, out string? name) ? new PlayerSeen(entity, name, OthersPower(r.Rest)) : null;
    }

    // Near the record's end: f4 cb 1f u32 count:u32, one or four bytes or nothing, then the
    // combat power as a u64 and one or two last bytes.
    static long? OthersPower(ReadOnlySpan<byte> rest)
    {
        var last = rest[Math.Max(0, rest.Length - 40)..];
        int mark = last.LastIndexOf(new byte[] { 0xF4, 0xCB, 0x1F });
        if (mark < 0)
            return null;
        var tail = last[(mark + 3)..];
        if (tail.Length < 17 || tail[4] is < 1 or > 0x40 || tail[5] != 0 || tail[6] != 0 || tail[7] != 0)
            return null;
        // The nearest fits: read a byte early, a power comes out hundreds of times too high.
        foreach (int at in new[] { 8, 9, 12, 13 })
            if (tail.Length - at - 8 is 1 or 2 && BitConverter.ToInt64(tail[at..]) is >= 1_000 and <= 3_000_000 and var power)
                return power;
        return null;
    }

    // Your own, at the self record's end: two u64 right before a count of 5 and entries
    // keyed 1 to 5, a u16 each.
    static long? OwnPower(ReadOnlySpan<byte> rest)
    {
        for (int p = 0; p + 32 <= rest.Length; p++)
        {
            var tail = rest.Slice(p + 16, 16);
            if (tail[0] != 5 || tail[1] != 1 || tail[4] != 2 || tail[7] != 3 || tail[10] != 4 || tail[13] != 5)
                continue;
            long power = BitConverter.ToInt64(rest[p..]), other = BitConverter.ToInt64(rest[(p + 8)..]);
            if (power is >= 1 and < 100_000_000 && other is >= 1 and < 100_000_000)
                return power;
        }

        return null;
    }

    // entity:varint u32 flags:u8 [flags & 1: length:u8 name]
    static bool Named(ref Reader r, out int entity, [NotNullWhen(true)] out string? name)
    {
        name = null;
        if (!r.VarInt(out entity) || entity is < 1 or > 9_999_999 || !r.Skip(4) || !r.Byte(out byte flags) || (flags & 1) == 0)
            return false;
        return Name(ref r, out name);
    }

    // target:varint u32 owner:varint server:u16 length:u8 name …
    static Message? Owner(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        if (!r.VarInt(out int target) || target is < 100 or > 9_999_999 || !r.Skip(4) || !r.VarInt(out int owner) || owner is < 100 or > 9_999_999 || owner == target || !r.U16(out ushort server) || server is < 1000 or >= 3000 || !Name(ref r, out string? name))
            return null;
        return new OwnerNamed(target, owner, name, server);
    }

    // length:u8 name, letters and digits
    static bool Name(ref Reader r, [NotNullWhen(true)] out string? name)
    {
        name = null;
        if (!r.Byte(out byte length) || length is < 1 or > 48 || !r.Bytes(length, out var raw))
            return false;
        try
        {
            name = StrictUtf8.GetString(raw);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return name.Length <= 12 && name.Any(char.IsLetter) && name.All(c => char.IsLetterOrDigit(c) || c is '\'' or '-' or '_');
    }

    // counter:u32 zone:i32 … x:f32@16 y:f32@20 (place present in longer frames)
    static Message? Zone(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        if (!r.Skip(4) || !r.I32(out int zone) || zone <= 0)
            return null;
        float? x = null, y = null;
        if (r.Skip(8) && r.F32(out float px) && r.F32(out float py) && Place(px, py, 500_000) && MathF.Abs(px) > 100 && MathF.Abs(py) > 100)
            (x, y) = (px, py);
        return new ZoneEntered(zone, x, y);
    }

    // equipped:i32 n:varint n×(pet:i32 pet:i32 rank:i32) m:varint m×(pet:i32 souls:i32)
    static Message? Collection(ReadOnlySpan<byte> body, Func<int, bool> knownPet)
    {
        var r = new Reader(body);
        if (!r.I32(out int equipped) || equipped != 0 && !knownPet(equipped) || !r.VarInt(out int owned) || owned > r.Remaining / 12)
            return null;
        var ranks = new Dictionary<int, int>(owned);
        for (int i = 0; i < owned; i++)
        {
            if (!r.I32(out int pet) || !r.I32(out int again) || !r.I32(out int rank) || pet != again || !knownPet(pet) || rank is < 1 or > 3 || !ranks.TryAdd(pet, rank))
                return null;
        }

        if (equipped != 0 && !ranks.ContainsKey(equipped) || !r.VarInt(out int counted) || counted > r.Remaining / 8)
            return null;
        var souls = new Dictionary<int, int>(counted);
        for (int i = 0; i < counted; i++)
        {
            if (!r.I32(out int pet) || !r.I32(out int value) || !knownPet(pet) || value < 0 || !souls.TryAdd(pet, value))
                return null;
            // An owned pet below max rank cannot hold more than its next upgrade costs.
            // Unowned pets are not capped: an unlock can be pending with 5 or more.
            if (ranks.GetValueOrDefault(pet) is 1 or 2 && value > Ranks.Requirement(ranks[pet]))
                return null;
        }

        return new CollectionSnapshot(equipped, ranks, souls);
    }

    // n:varint n×(pet:i32 amount:i32), exactly
    static Message? Souls(ReadOnlySpan<byte> body, Func<int, bool> knownPet, bool spent)
    {
        var r = new Reader(body);
        if (!r.VarInt(out int count) || count <= 0 || r.Remaining != count * 8)
            return null;
        var entries = new List<(int, int)>(count);
        var seen = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            r.I32(out int pet);
            r.I32(out int amount);
            if (!knownPet(pet) || !seen.Add(pet) || amount is <= 0 or > 75)
                return null;
            entries.Add((pet, amount));
        }

        return new SoulChange(spent, entries);
    }

    // status:u16(0) pet:i32 rank:i32 …, 23 bytes
    static Message? Rank(ReadOnlySpan<byte> body, Func<int, bool> knownPet)
    {
        var r = new Reader(body);
        if (body.Length != 23 || !r.U16(out ushort status) || status != 0 || !r.I32(out int pet) || !r.I32(out int rank) || !knownPet(pet) || rank is < 1 or > 3)
            return null;
        return new RankReached(pet, rank);
    }

    static Message? Removal(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        if (!r.VarInt(out int entity) || entity <= 0 || r.Remaining != 2 || !r.Byte(out byte zero) || zero != 0 || !r.Byte(out byte reason))
            return null;
        return new EntityRemoved(entity, reason);
    }

    // Summons name their owner; creatures have one of two forms, and an older third.
    static Message? Spawn(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        if (!r.VarInt(out int entity) || entity is < 1 or > 9_999_999 || !r.Peek(0, out byte kind))
            return null;
        if (kind is 0x5F or 0x1F or 0x1D or 0x5D && Summon(r.Rest, entity) is { } summon)
            return summon;
        return Appearance(body) ?? (kind is 0x5F or 0x1F or 0x1D or 0x5D ? null : Code(r.Rest, entity));
    }

    // kind:u8 … code:u24 00 (00|40) 02, within the first 60 bytes.
    static Message? Code(ReadOnlySpan<byte> rest, int entity)
    {
        for (int i = 3; i + 2 < rest.Length && i < 60; i++)
            if (rest[i] == 0 && rest[i + 1] is 0 or 0x40 && rest[i + 2] == 2)
                return new NpcAppeared(entity, rest[i - 3] | rest[i - 2] << 8 | rest[i - 1] << 16, null, null);
        return null;
    }

    // kind:u8 … mark owner:varint, the mark within the first 120 bytes.
    static Message? Summon(ReadOnlySpan<byte> rest, int entity)
    {
        int mark = rest[..Math.Min(rest.Length, 120 + OwnerMark.Length)].IndexOf(OwnerMark);
        if (mark < 0)
            return null;
        var owner = new Reader(rest[(mark + OwnerMark.Length)..]);
        return owner.VarInt(out int id) && id is >= 1 and <= 9_999_999 && id != entity ? new SummonSeen(entity, id) : null;
    }

    // entity:varint kind:u8 u8 named:u8 [name…] npc:i32 … x:f32 y:f32. Summons (kind low
    // five bits all set) are skipped. Two layouts place the NPC id at +3 or +5 after the
    // entity; the byte five after a genuine NPC id is 2 in both.
    static Message? Appearance(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        if (!r.VarInt(out int entity) || entity <= 0 || !r.Peek(0, out byte kind) || (kind & 0x1f) == 0x1f)
            return null;
        int at = r.Position;
        foreach (int offset in new[] { 5, 3 })
        {
            int npcAt = at + offset;
            if (npcAt + 6 > body.Length || body[npcAt + 5] != 2)
                continue;
            int npc = BitConverter.ToInt32(body.Slice(npcAt, 4));
            if (npc is < 1_000_000 or > 10_000_000)
                continue;
            float? x = null, y = null;
            if (npcAt + 14 <= body.Length)
            {
                float px = BitConverter.ToSingle(body.Slice(npcAt + 6, 4)), py = BitConverter.ToSingle(body.Slice(npcAt + 10, 4));
                if (Place(px, py, 1_000_000))
                    (x, y) = (px, py);
            }

            return new NpcAppeared(entity, npc, x, y);
        }

        return null;
    }

    // entity:varint 03 x:f32 y:f32 …
    static Message? Position(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        if (!r.VarInt(out int entity) || entity <= 0 || !r.Byte(out byte tag) || tag != 3 || !r.F32(out float x) || !r.F32(out float y) || !Place(x, y, 1_000_000))
            return null;
        return new PositionReport(entity, x, y);
    }

    // Movement frames: the position sits one or two bytes after the entity, by opcode.
    static int? MoveOffset(byte op) => op switch
    {
        0x1A or 0x1B or 0x1C or 0x1E or 0x20 or 0x28 or 0x29 or 0x2A => 2,
        0x1F or 0x2F => 1,
        _ => null
    };

    static Message? Moved(ReadOnlySpan<byte> body, int offset)
    {
        var r = new Reader(body);
        if (!r.VarInt(out int entity) || entity is < 1 or > 10_000_000 || !r.Skip(offset) || !r.F32(out float x) || !r.F32(out float y) || !Place(x, y, 200_000))
            return null;
        return new EntityMoved(entity, x, y);
    }

    // target:varint [01] type:u8 instance:varint effect:u32 … (20 bytes) caster:varint … x y z (last 12 bytes)
    static Message? Effect(ReadOnlySpan<byte> body, bool renewed)
    {
        var r = new Reader(body);
        if (!r.VarInt(out int target) || target is < 1 or > 10_000_000)
            return null;
        if (!renewed && (!r.Byte(out byte applied) || applied != 1))
            return null;
        if (!r.Skip(1) || !r.VarInt(out _) || !r.U32(out uint effect) || effect is < 10 or > 2_999_999_999u || !r.Skip(16) || !r.VarInt(out int caster) || caster is < 1 or > 10_000_000 || r.Remaining <= 12)
            return null;
        var tail = body[^12..];
        float x = BitConverter.ToSingle(tail), y = BitConverter.ToSingle(tail[4..]), z = BitConverter.ToSingle(tail[8..]);
        if (!Place(x, y, 500_000) || MathF.Abs(x) <= 100 || MathF.Abs(y) <= 100 || !float.IsFinite(z) || MathF.Abs(z) >= 50_000)
            return null;
        return new EffectCast(target, caster, x, y);
    }

    // One record, or several joined by 01 00 (an area skill names each target in turn):
    // target:varint layout:varint varint actor:varint skill:u32 uid:u8 type:varint
    // … (8, 12, 10 or 14 bytes by layout) power:varint damage:varint tail
    // Records without a value (misses, resists) and creatures' skills end the frame.
    static Message? Hits(ReadOnlySpan<byte> body)
    {
        var hits = new List<SkillHit>();
        var r = new Reader(body);
        for (bool first = true; ; first = false)
        {
            bool chained = r.Peek(0, out byte a) && r.Peek(1, out byte b) && a == 1 && b == 0;
            if (chained)
                r.Skip(2);
            else if (!first)
                break;
            if (!r.VarInt(out int target) || target <= 0 || !r.VarInt(out int flags) || (flags & 15) is < 4 or > 7 || !r.VarInt(out _) || !r.VarInt(out int actor) || actor <= 0 || !r.U32(out uint skill))
                break;
            if (skill is not ((>= 10_000_000 and < 300_000_000) or (>= 3_000_000 and < 3_100_000) or (>= 100_510 and <= 103_500) or (>= 109_300 and <= 109_362)) || !r.Skip(1) || !r.VarInt(out int type))
                break;
            int layout = flags & 15;
            int size = layout switch
            {
                5 => 12,
                6 => 10,
                7 => 14,
                _ => 8
            };
            // Layouts 6 and 7 open with a flags byte and a varint counted here as one byte.
            if ((layout & 2) != 0 && (!r.Skip(1) || !r.VarInt(out _)))
                break;
            if (!r.Skip((layout & 2) != 0 ? size - 2 : size) || !r.VarInt(out int power) || !r.VarInt(out int damage))
                break;
            // A zero here pads the actor's power, which pushes the damage one along.
            if (power == 0 && r.VarInt(out int shifted))
                (power, damage) = (damage, shifted);
            // Hitting oneself is a heal.
            if (actor != target && damage is > 0 and < 100_000_000)
                hits.Add(new SkillHit(actor, target, damage, skill, power, type == 3));
            if (!Tail(ref r, flags, damage))
                break;
        }

        return hits.Count > 0 ? new SkillHits(hits) : null;
    }

    // What follows the damage. Layout 4 has a small field (a spirit's has none), bit 0x20
    // lists additional hits the damage already includes, bit 0x40 lists absorbing effects.
    // False when the bytes do not end at a record boundary.
    static bool Tail(ref Reader r, int flags, int damage)
    {
        var withField = r;
        if ((flags & 15) == 4 && TailFrom(ref withField, flags, damage, field: true))
        {
            r = withField;
            return true;
        }

        return (flags & 15) is 4 or 6 && TailFrom(ref r, flags, damage, field: false);
    }

    static bool TailFrom(ref Reader r, int flags, int damage, bool field)
    {
        if (field && (!r.VarInt(out int small) || small is < 1 or > 25))
            return false;
        if ((flags & 0x20) != 0)
        {
            if (!r.VarInt(out int count) || count is < 1 or > 25)
                return false;
            long extra = 0;
            for (int i = 0; i < count; i++)
            {
                if (!r.VarInt(out int hit) || hit < 0)
                    return false;
                extra += hit;
            }

            if (extra >= damage)
                return false;
        }

        if ((flags & 0x40) != 0 && (!r.VarInt(out int absorbed) || absorbed is < 1 or > 8 || !r.Skip(4 * absorbed)))
            return false;
        return r.Remaining == 0 || r.Remaining >= 2 && r.Rest[1] == 0 && r.Rest[0] is >= 1 and <= 7;
    }

    static Message? Hint(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        return r.VarInt(out int entity) && entity is >= 1 and <= 10_000_000 ? new SelfHint(entity) : null;
    }

    // tab:u8 count:u8(10) 10×34-byte offers (separator byte before offers 1, 5, 9)
    // … markers at 36/173/310 … refresh:i64 ms @345, zero:i32 @353; 357 bytes total.
    static Message? Merchant(ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        if (body.Length != 357 || body[0] is not (101 or 104) || body[1] != 10 || body[36] != 0x55 || body[173] != 0x55 || body[310] != 5 || BitConverter.ToInt32(body[353..]) != 0)
            return null;
        long refresh = BitConverter.ToInt64(body[345..]);
        if (refresh <= now.ToUnixTimeMilliseconds() || refresh > now.AddHours(2).ToUnixTimeMilliseconds())
            return null;
        var offers = new List<MerchantOffer>(10);
        var ids = new HashSet<int>();
        int at = 2;
        for (int i = 0; i < 10; i++)
        {
            if (i is 1 or 5 or 9)
                at++;
            var r = new Reader(body.Slice(at, 34));
            at += 34;
            r.I32(out int offer);
            r.I32(out int item);
            r.I64(out long quantity);
            r.Byte(out byte discountCode);
            r.Byte(out byte marker);
            r.I64(out long original);
            r.I64(out long price);
            int percent = discountCode switch
            {
                0 => 90,
                1 => 80,
                2 => 70,
                3 => 50,
                _ => 0
            };
            if (offer <= 0 || item <= 0 || !ids.Add(offer) || quantity != 1 || marker != 0x5d || percent == 0 || original is <= 0 or > 1_000_000_000 || price != original * percent / 100)
                return null;
            offers.Add(new(offer, item, price));
        }

        return new MerchantStock(body[0], DateTimeOffset.FromUnixTimeMilliseconds(refresh), offers);
    }
}

/// <summary>Souls needed to leave each rank (rank 0 = not yet owned).</summary>
public static class Ranks
{
    public static int? Requirement(int rank) => rank switch
    {
        0 => 5,
        1 => 25,
        2 => 75,
        _ => null
    };
}
