using System.Text;

namespace ArnoA2.Protocol;

/// <summary>A decoded game message.</summary>
public abstract record Message;

/// <summary>33:36 — the logged-in character (sent at login and zone entry).</summary>
public sealed record CharacterIdentity(int Entity, string Name, ushort Server) : Message;

/// <summary>21:36 — zone entered.</summary>
public sealed record ZoneEntered(int Zone) : Message;

/// <summary>One player skill's damage to one target.</summary>
public sealed record SkillHit(int Actor, int Target, int Damage);

/// <summary>04:38 — a player skill hit one target, or several for an area skill.</summary>
public sealed record SkillHits(IReadOnlyList<SkillHit> Hits) : Message;

/// <summary>Frames that, in captures, almost only ever name the player's own entity
/// (stat updates and a few others). Used to recognise the player mid-session.</summary>
public sealed record SelfHint(int Entity) : Message;

public static class MessageDecoder
{
    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static Message? Decode(Frame frame)
    {
        var body = frame.Body.AsSpan();
        return (frame.Op, frame.Family) switch
        {
            (0x33, 0x36) => Identity(body),
            (0x21, 0x36) => Zone(body),
            (0x04, 0x38) => Hits(body),
            (0x00, 0x8D) or (0x44, 0x37) or (0x4A, 0x36) or (0x33, 0x8A) => Hint(body),
            _ => null
        };
    }

    // Name field: a flag byte whose low bit says a name follows, then length-prefixed UTF-8.
    static Message? Identity(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        if (!r.VarInt(out int entity) || entity is < 1 or > 9_999_999 || !r.Skip(4) || !r.Byte(out byte flags) || (flags & 1) == 0 || !r.Byte(out byte length) || length is < 1 or > 48 || !r.Bytes(length, out var raw) || !r.U16(out ushort server))
            return null;
        string name;
        try
        {
            name = StrictUtf8.GetString(raw);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        if (name.Length > 12 || !name.Any(char.IsLetter) || !name.All(c => char.IsLetterOrDigit(c) || c is '\'' or '-' or '_'))
            return null;
        return new CharacterIdentity(entity, name, server);
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
            if (skill is not ((>= 10_000_000 and < 300_000_000) or (>= 3_000_000 and < 3_100_000)) || !r.Skip(1) || !r.VarInt(out _))
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
                damage = shifted;
            // Hitting oneself is a heal.
            if (actor != target && damage is > 0 and < 100_000_000)
                hits.Add(new SkillHit(actor, target, damage));
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
