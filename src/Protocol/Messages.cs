using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ArnoA2.Protocol;

/// <summary>A decoded game message.</summary>
public abstract record Message;

/// <summary>33:36 — the logged-in character (sent at login and zone entry).</summary>
public sealed record CharacterIdentity(int Entity, string Name, ushort Server, PlayerClass? Class = null, long? Power = null) : Message;

/// <summary>44:36 / 45:36 — another player came into view.</summary>
public sealed record PlayerSeen(int Entity, string Name, long? Power = null) : Message;

/// <summary>40:36 / 41:36 — a summon or spirit appeared, and who it belongs to.</summary>
public sealed record SummonSeen(int Entity, int Owner) : Message;

/// <summary>40:36 / 41:36 — a creature appeared, with its NPC code.</summary>
public sealed record NpcSeen(int Entity, int Code) : Message;

/// <summary>42:36 — something died.</summary>
public sealed record EntityDied(int Entity) : Message;

/// <summary>21:36 — zone entered.</summary>
public sealed record ZoneEntered(int Zone) : Message;

/// <summary>04:8D — a creature's kill and loot went to this player, or a summon's owner.
/// The game sends it for your own kills, so it names you without a login.</summary>
public sealed record OwnerNamed(int Target, int Owner, string Name, ushort Server) : Message;

/// <summary>One player skill's damage to one target.</summary>
/// <param name="Power">The actor's attack power, which a summon shares with its owner.</param>
public sealed record SkillHit(int Actor, int Target, int Damage, uint Skill, int Power = 0, bool Crit = false);

/// <summary>04:38 — a player skill hit one target, or several for an area skill.</summary>
public sealed record SkillHits(IReadOnlyList<SkillHit> Hits) : Message;

/// <summary>Frames that, in captures, almost only ever name the player's own entity
/// (stat updates and a few others). Used to recognise the player mid-session.</summary>
public sealed record SelfHint(int Entity) : Message;

public static class MessageDecoder
{
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    // Comes just before the owner in a summon's spawn record.
    static readonly byte[] OwnerMark = [0x80, 0x75, 0xD5, 0x2A, 0xBB, 0x03, 0x00, 0x00];

    public static Message? Decode(Frame frame)
    {
        var body = frame.Body.AsSpan();
        return (frame.Op, frame.Family) switch
        {
            (0x33, 0x36) => Identity(body),
            (0x44 or 0x45, 0x36) => Player(body),
            (0x04, 0x8D) => Owner(body),
            (0x40 or 0x41, 0x36) => Spawn(body),
            (0x42, 0x36) => Death(body),
            (0x21, 0x36) => Zone(body),
            (0x04, 0x38) => Hits(body),
            (0x00, 0x8D) or (0x44, 0x37) or (0x4A, 0x36) => Hint(body),
            _ => null
        };
    }

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

    static Message? Spawn(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        if (!r.VarInt(out int entity) || entity is < 1 or > 9_999_999 || !r.Peek(0, out byte kind))
            return null;
        return kind is 0x5F or 0x1F or 0x1D or 0x5D ? Summon(r.Rest, entity) : Npc(r.Rest, entity);
    }

    // kind:u8 … code:u24 00 (00|40) 02, within the first 60 bytes.
    static Message? Npc(ReadOnlySpan<byte> rest, int entity)
    {
        for (int i = 3; i + 2 < rest.Length && i < 60; i++)
            if (rest[i] == 0 && rest[i + 1] is 0 or 0x40 && rest[i + 2] == 2)
                return new NpcSeen(entity, rest[i - 3] | rest[i - 2] << 8 | rest[i - 1] << 16);
        return null;
    }

    // entity:varint 00 reason:u8; reason 3 is death.
    static Message? Death(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        return r.VarInt(out int entity) && r.Remaining == 2 && r.Skip(1) && r.Byte(out byte reason) && reason == 3 ? new EntityDied(entity) : null;
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

    // counter:u32 zone:i32 …
    static Message? Zone(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        return r.Skip(4) && r.I32(out int zone) && zone > 0 ? new ZoneEntered(zone) : null;
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
}
