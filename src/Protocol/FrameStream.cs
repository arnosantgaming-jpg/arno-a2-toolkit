using System.Buffers.Binary;

namespace ArnoA2.Protocol;

/// <summary>One game message: a two-byte code (op, family) and its body.</summary>
public sealed record Frame(byte Op, byte Family, byte[] Body)
{
    public ushort Code => (ushort)(Op << 8 | Family);
    public override string ToString() => $"{Op:X2}:{Family:X2} ({Body.Length} bytes)";
}

/// <summary>
/// Splits the server→client byte stream into frames.
///
/// Wire format (all observed in AION 2 captures):
///   frame  = length:varint(1–3 bytes, minimal) op:u8 family:u8 body
///            where the frame occupies (length + varint width − 4) bytes in total;
///   bundle = FF FF size:i32 lz4-block  (decompresses to exactly size bytes of frames);
///   a frame whose op and family are both FF carries a nested bundle as its body.
///
/// The stream may be joined mid-message (tracker started late, capture gap). Bytes
/// that cannot start a frame are skipped one at a time until the stream lines up.
/// Until it has, a stray byte can read as a length of up to a megabyte and swallow a
/// minute of real frames. So until a bundle decodes and tiles cleanly, which a stray byte
/// all but never does, a frame over <see cref="TrustedFrame"/> bytes only counts if a run
/// of frames follows it, and none may exceed <see cref="MaxUnsynced"/>.
/// Bundles are complete units, so errors inside them are reported, not skipped over.
/// </summary>
public sealed class FrameStream
{
    public const int MaxBundle = 1_000_000, MaxPending = 1 << 20, MaxNesting = 3, TrustedFrame = 1024, MaxUnsynced = 64 * 1024;
    byte[] buffer = new byte[64 * 1024];
    int start, end;
    bool synced;

    public long Frames { get; private set; }
    public long SkippedBytes { get; private set; }
    public long Faults { get; private set; }
    public event Action<Frame>? FrameDecoded;
    public event Action<string>? Faulted;

    /// <summary>Drop buffered bytes, e.g. after a TCP gap.</summary>
    public void Reset() => (start, end, synced) = (0, 0, false);

    public void Push(ReadOnlySpan<byte> bytes)
    {
        if (end - start + bytes.Length > MaxPending)
        {
            Reset();
            Fault("Pending data exceeded " + MaxPending + " bytes");
            return;
        }

        Append(bytes);
        while (start < end)
        {
            var pending = buffer.AsSpan(start, end - start);
            int used = Next(pending);
            if (used == 0)
                break; // wait for more bytes
            start += used;
        }

        if (start == end)
            start = end = 0;
    }

    void Append(ReadOnlySpan<byte> bytes)
    {
        if (end + bytes.Length > buffer.Length)
        {
            int length = end - start;
            var target = length + bytes.Length > buffer.Length ? new byte[Math.Max(buffer.Length * 2, length + bytes.Length)] : buffer;
            Buffer.BlockCopy(buffer, start, target, 0, length);
            buffer = target;
            start = 0;
            end = length;
        }

        bytes.CopyTo(buffer.AsSpan(end));
        end += bytes.Length;
    }

    /// <summary>Consume one unit from the front: returns bytes used, 0 to wait.</summary>
    int Next(ReadOnlySpan<byte> data)
    {
        if (data[0] == 0xFF)
        {
            if (data.Length < 2)
                return 0;
            if (data[1] == 0xFF)
                return Bundle(data);
        }

        switch (FrameLength(data, out int width, out int total))
        {
            case Length.Incomplete:
                return 0;
            case Length.Invalid:
                synced = false;
                SkippedBytes++;
                return 1;
        }

        if (!synced)
        {
            if (total > MaxUnsynced)
            {
                SkippedBytes++;
                return 1;
            }

            switch (Follows(data[Math.Min(total, data.Length)..]))
            {
                case Length.Invalid when data.Length > total:
                    SkippedBytes++;
                    return 1;
                case Length.Ok when data.Length > total:
                    break;
                default:
                    // Nothing after it yet: a small frame is taken on trust, a big one waits.
                    if (total > TrustedFrame)
                        return 0;
                    break;
            }
        }

        if (data.Length < total)
            return 0;
        Emit(data.Slice(width, total - width), 0);
        return total;
    }

    int Bundle(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
            return 0;
        int size = BinaryPrimitives.ReadInt32LittleEndian(data[2..]);
        if (size is <= 0 or > MaxBundle)
        {
            SkippedBytes++;
            return 1;
        }

        var output = new byte[size];
        switch (Lz4.Decode(data[6..], output, out int consumed))
        {
            case Lz4Result.NeedMore:
                return 0;
            case Lz4Result.Corrupt:
                Fault("Corrupt LZ4 bundle");
                SkippedBytes += 2;
                return 2;
        }

        synced = ReadAll(output, 1);
        return 6 + consumed;
    }

    /// <summary>Whether frames can follow here: a few in a row, or a bundle. Ok once the
    /// run reaches the end of what has arrived, Incomplete if nothing has.</summary>
    static Length Follows(ReadOnlySpan<byte> data)
    {
        const int Run = 4;
        int at = 0;
        for (int hop = 0; hop < Run && at < data.Length; hop++)
        {
            var rest = data[at..];
            if (rest[0] == 0xFF && (rest.Length == 1 || rest[1] == 0xFF))
                return Length.Ok;
            switch (FrameLength(rest, out _, out int total))
            {
                case Length.Invalid:
                    return Length.Invalid;
                case Length.Incomplete:
                    return hop == 0 ? Length.Incomplete : Length.Ok;
            }

            if (total > MaxUnsynced)
                return Length.Invalid;
            at += total;
        }

        return at == 0 ? Length.Incomplete : Length.Ok;
    }

    enum Length
    {
        Ok,
        Incomplete,
        Invalid
    }

    static Length FrameLength(ReadOnlySpan<byte> data, out int width, out int total)
    {
        width = total = 0;
        int value = 0;
        for (int i = 0; i < 3; i++)
        {
            if (i >= data.Length)
                return Length.Incomplete;
            byte b = data[i];
            value |= (b & 0x7f) << (7 * i);
            if (b >= 0x80)
                continue;
            if (i > 0 && b == 0)
                return Length.Invalid; // over-long encoding
            width = i + 1;
            total = value + width - 4;
            // A frame holds at least its length, op and family.
            return total < width + 2 || total > MaxPending ? Length.Invalid : Length.Ok;
        }

        return Length.Invalid;
    }

    /// <summary>Frames inside a decompressed bundle must tile it exactly; false if they don't.</summary>
    bool ReadAll(ReadOnlySpan<byte> data, int depth)
    {
        int at = 0;
        while (at < data.Length)
        {
            if (FrameLength(data[at..], out int width, out int total) != Length.Ok || at + total > data.Length)
            {
                Fault("Bad frame boundary inside a bundle");
                return false;
            }

            Emit(data.Slice(at + width, total - width), depth);
            at += total;
        }

        return true;
    }

    void Emit(ReadOnlySpan<byte> frame, int depth)
    {
        byte op = frame[0], family = frame[1];
        var body = frame[2..];
        if (op == 0xFF && family == 0xFF)
        {
            if (depth >= MaxNesting || body.Length < 4)
            {
                Fault("Bad nested bundle header");
                return;
            }

            int size = BinaryPrimitives.ReadInt32LittleEndian(body);
            if (size is <= 0 or > MaxBundle)
            {
                Fault("Bad nested bundle size");
                return;
            }

            var output = new byte[size];
            if (Lz4.Decode(body[4..], output, out _) != Lz4Result.Done)
            {
                Fault("Corrupt nested bundle");
                return;
            }

            ReadAll(output, depth + 1);
            return;
        }

        Frames++;
        FrameDecoded?.Invoke(new(op, family, body.ToArray()));
    }

    void Fault(string reason)
    {
        Faults++;
        Faulted?.Invoke(reason);
    }
}
