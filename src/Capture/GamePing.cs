using System.Buffers.Binary;

namespace ArnoA2.Capture;

/// <summary>
/// The game's own ping, the same way A2Tools DPS Meter reads it. Every ten seconds the client
/// sends a small request and the game server answers with the client's timestamp in a
/// 03 36 00 00 frame. That timestamp runs on a clock of the client's own, so the round trip is
/// the reply's arrival minus the request it answers. Requests are encrypted and known only by
/// their frame size, so a stray match is possible: when a reply has several candidates, the
/// one keeping the same distance between the two clocks as before is the real one.
/// </summary>
public sealed class GamePing
{
    const long MaxMilliseconds = 9999, Tolerance = 15, DotNetEpoch = 62135596800000;
    const int RequestMin = 12, RequestMax = 13, ReplySize = 13;
    static readonly byte[] Reply = { 0x18, 0x03, 0x36, 0x00, 0x00 };
    static readonly byte[][] Relayed = { new byte[] { 0x05, 0x23, 0x0C, 0x0F }, new byte[] { 0x05, 0x23, 0x0D, 0x10 } };
    readonly Queue<long> requests = new();
    long[] previous = Array.Empty<long>();
    long? offset;

    public void Sent(ReadOnlySpan<byte> payload, long now)
    {
        if (!IsRequest(payload))
            return;
        requests.Enqueue(now);
        while (now - requests.Peek() > MaxMilliseconds)
            requests.Dequeue();
    }

    /// <summary>Round trip in milliseconds when the payload holds a ping reply.</summary>
    public int? Received(ReadOnlySpan<byte> payload, long now)
    {
        int? found = null;
        int at = 0;
        while (at + ReplySize <= payload.Length)
        {
            int next = payload[at..].IndexOf(Reply);
            if (next < 0 || at + next + ReplySize > payload.Length)
                break;
            at += next;
            long sent = BinaryPrimitives.ReadInt64LittleEndian(payload[(at + Reply.Length)..]);
            found = Time(sent, now) ?? found;
            at += ReplySize;
        }

        return found;
    }

    int? Time(long sent, long now)
    {
        long? paired = FromRequest(sent, now);
        long wall = now - (sent - DotNetEpoch); // Korea and Taiwan stamp the real time
        long? known = offset is long o ? unchecked(now - (sent + o)) : null;
        return new[] { paired, Valid(wall) ? wall : null, known }.FirstOrDefault(v => v is long r && Valid(r)) is long best ? (int)best : null;
    }

    long? FromRequest(long sent, long now)
    {
        var offsets = requests.Where(t => now - t is >= 0 and <= MaxMilliseconds).Select(t => unchecked(t - sent)).ToArray();
        long? picked = offsets.Length switch
        {
            0 => null,
            1 => offsets[0],
            _ => (offset is long known ? Near(known, offsets) : null) ?? offsets.Cast<long?>().FirstOrDefault(c => Near(c!.Value, previous) != null)
        };
        if (picked is long chosen)
        {
            if (offset is long held && Close(chosen, held) || Near(chosen, previous) != null)
                offset = chosen; // follows slow drift, and needs two pings to agree on a new clock
            else
                offset = null; // one of the clocks jumped
        }

        previous = offsets;
        return picked is long distance ? unchecked(now - (sent + distance)) : null;
    }

    static long? Near(long value, long[] others) => others.Cast<long?>().FirstOrDefault(o => Close(o!.Value, value));

    static bool Close(long a, long b) => Math.Abs(unchecked(a - b)) <= Tolerance;

    static bool Valid(long milliseconds) => milliseconds is >= 1 and <= MaxMilliseconds;

    /// <summary>Only the frame sizes are read: a payload counts only when it divides cleanly
    /// into frames, one of them the request's size. A relay tunnel marks it with a header.</summary>
    public static bool IsRequest(ReadOnlySpan<byte> payload)
    {
        foreach (var header in Relayed)
            if (payload.IndexOf(header) >= 0)
                return true;
        bool found = false;
        int at = 0;
        while (at < payload.Length)
        {
            if (!Length(payload[at..], out long value) || value - 3 <= 0 || at + value - 3 > payload.Length)
                return false;
            int size = (int)(value - 3);
            found |= size is >= RequestMin and <= RequestMax;
            at += size;
        }

        return found;
    }

    static bool Length(ReadOnlySpan<byte> bytes, out long value)
    {
        value = 0;
        for (int i = 0; i < 5 && i < bytes.Length; i++)
        {
            value |= (long)(bytes[i] & 0x7f) << (7 * i);
            if ((bytes[i] & 0x80) == 0)
                return true;
        }

        return false;
    }
}
