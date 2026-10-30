using System.Text;
using ArnoA2.Protocol;

// Synthetic protocol checks. Real captures contain account data and are never committed;
// run tools/Replay --meter against your own recordings for end-to-end checks.
int checks = 0;
void Check(bool ok, string what)
{
    checks++;
    if (!ok)
        throw new Exception("FAILED: " + what);
}

Message? Decode(byte op, byte family, byte[] body) => MessageDecoder.Decode(new(op, family, body));

// --- Builders -------------------------------------------------------------------
static byte[] Var(int value)
{
    var bytes = new List<byte>();
    uint v = (uint)value;
    do
    {
        byte b = (byte)(v & 0x7f);
        v >>= 7;
        bytes.Add(v != 0 ? (byte)(b | 0x80) : b);
    }
    while (v != 0);
    return bytes.ToArray();
}

static byte[] I32(int v) => BitConverter.GetBytes(v);
static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
static byte[] FrameBytes(byte op, byte family, byte[] body) => Cat(Var(body.Length + 6), new[] { op, family }, body);

// Literal-only LZ4 block: one sequence carrying all bytes (valid LZ4).
static byte[] Lz4Literal(byte[] data)
{
    var output = new List<byte>();
    int n = data.Length;
    output.Add((byte)(Math.Min(n, 15) << 4));
    if (n >= 15)
    {
        int rest = n - 15;
        while (rest >= 255)
        {
            output.Add(255);
            rest -= 255;
        }

        output.Add((byte)rest);
    }

    output.AddRange(data);
    return output.ToArray();
}

static byte[] Bundle(byte[] frames) => Cat(new byte[] { 0xFF, 0xFF }, I32(frames.Length), Lz4Literal(frames));

List<Frame> Split(params byte[][] chunks)
{
    var stream = new FrameStream();
    var frames = new List<Frame>();
    stream.FrameDecoded += frames.Add;
    foreach (var chunk in chunks)
        stream.Push(chunk);
    return frames;
}

// --- Reader ---------------------------------------------------------------------
{
    var r = new Reader(Cat(Var(300), Var(0), new byte[] { 0x80, 0x00 }));
    Check(r.VarInt(out int a) && a == 300, "varint 300");
    Check(r.VarInt(out int b) && b == 0, "varint 0");
    Check(!r.VarInt(out _) && r.Position == 3, "over-long varint rejected without consuming");
    var empty = new Reader(Array.Empty<byte>());
    Check(!empty.I32(out _) && !empty.Byte(out _) && !empty.Skip(1), "reads past end fail");
}

// --- LZ4 ------------------------------------------------------------------------
{
    // "abcabcabcabc": 3 literals then a match of 9 at distance 3 (overlapping copy).
    var block = new byte[] { 0x35, (byte)'a', (byte)'b', (byte)'c', 3, 0 };
    var output = new byte[12];
    Check(Lz4.Decode(block, output, out int used) == Lz4Result.Done && used == 6 && Encoding.ASCII.GetString(output) == "abcabcabcabc", "LZ4 overlapping match");
    Check(Lz4.Decode(block.AsSpan(0, 3), new byte[12], out _) == Lz4Result.NeedMore, "LZ4 waits for more input");
    Check(Lz4.Decode(new byte[] { 0x10, 1, 9, 0 }, new byte[8], out _) == Lz4Result.Corrupt, "LZ4 rejects distance beyond output");
    var big = Enumerable.Range(0, 600).Select(i => (byte)i).ToArray();
    var bigOut = new byte[600];
    Check(Lz4.Decode(Lz4Literal(big), bigOut, out _) == Lz4Result.Done && bigOut.SequenceEqual(big), "LZ4 long literal run");
}

// --- Framing --------------------------------------------------------------------
{
    var a = FrameBytes(0x11, 0x22, new byte[] { 1, 2, 3 });
    var b = FrameBytes(0x33, 0x44, Enumerable.Repeat((byte)7, 200).ToArray()); // two-byte length
    var frames = Split(Cat(a, b));
    Check(frames.Count == 2 && frames[0].Code == 0x1122 && frames[0].Body.SequenceEqual(new byte[] { 1, 2, 3 }) && frames[1].Body.Length == 200, "two frames, one and two-byte lengths");

    var both = Cat(a, b);
    var pieces = Enumerable.Range(0, both.Length).Select(i => new[] { both[i] }).ToArray();
    Check(Split(pieces).Count == 2, "frames split one byte at a time");

    var bundled = Split(Bundle(Cat(a, b)));
    Check(bundled.Count == 2 && bundled[1].Code == 0x3344, "stream bundle");

    var nested = Split(FrameBytes(0xFF, 0xFF, Cat(I32(a.Length), Lz4Literal(a))));
    Check(nested.Count == 1 && nested[0].Code == 0x1122, "nested bundle inside a frame");

    var stream = new FrameStream();
    var resynced = new List<Frame>();
    stream.FrameDecoded += resynced.Add;
    // Joined mid-message: an over-long length (80 00) and an impossibly short frame (02)
    // cannot start a frame, so they are skipped before the real frame.
    stream.Push(Cat(new byte[] { 0x80, 0x00, 0x02 }, a));
    Check(resynced.Count == 1 && resynced[0].Code == 0x1122 && stream.SkippedBytes == 3, "resync after garbage");

    var faulty = new FrameStream();
    int faults = 0;
    faulty.Faulted += _ => faults++;
    faulty.Push(Cat(new byte[] { 0xFF, 0xFF }, I32(10), new byte[] { 0x10, 1, 9, 0 }));
    Check(faults == 1, "corrupt bundle reported");
}

// --- Messages -------------------------------------------------------------------
{
    var name = Encoding.UTF8.GetBytes("Tester");
    var identity = Decode(0x33, 0x36, Cat(Var(4242), I32(0), new byte[] { 7, (byte)name.Length }, name, new byte[] { 0xD2, 0x04 }, new byte[20]));
    Check(identity is CharacterIdentity { Entity: 4242, Name: "Tester", Server: 1234 }, "identity");
    Check(Decode(0x33, 0x36, Cat(Var(4242), I32(0), new byte[] { 6, 3 }, "abc"u8.ToArray(), new byte[2])) == null, "identity without name flag rejected");

    Check(Decode(0x33, 0x36, Cat(Var(4242), I32(0), new byte[] { 7, (byte)name.Length }, name, new byte[] { 0xD2, 0x04 }, I32(30), new byte[20])) is CharacterIdentity { Class: PlayerClass.Cleric }, "identity carries the class");
    Check(Decode(0x45, 0x36, Cat(Var(5151), I32(0), new byte[] { 7, (byte)name.Length }, name, new byte[9])) is PlayerSeen { Entity: 5151, Name: "Tester" }, "another player's name");
    Check(Decode(0x44, 0x36, Cat(Var(5151), I32(0), new byte[] { 6, 3 }, "abc"u8.ToArray())) == null, "a record without its name flag is not a player");
    Check(PlayerClasses.FromRoster(7) == PlayerClass.Gladiator && PlayerClasses.FromRoster(36) == PlayerClass.Chanter && PlayerClasses.FromRoster(99) == null, "roster classes");
    Check(PlayerClasses.FromSkill(13_450_000) == PlayerClass.Assassin && PlayerClasses.FromSkill(14_010_000) == PlayerClass.Ranger && PlayerClasses.FromSkill(19_010_000) == PlayerClass.Fighter, "class from skill ids");
    Check(PlayerClasses.FromSkill(12_000_000) == null && PlayerClasses.FromSkill(16_500_000) == null && PlayerClasses.FromSkill(16_010_000) == PlayerClass.Spiritmaster && PlayerClasses.FromSkill(100_600) == PlayerClass.Spiritmaster, "generic and spirit-only skills do not name a class");

    Check(Decode(0x21, 0x36, Cat(I32(1), I32(1010), new byte[8])) is ZoneEntered { Zone: 1010 }, "zone");

    Check(Decode(0x00, 0x8D, Cat(Var(4242), new byte[6])) is SelfHint { Entity: 4242 }, "self hint");

    // Skill hits, in the layouts the game sends.
    IReadOnlyList<SkillHit>? Hits(params byte[][] parts) => (Decode(0x04, 0x38, Cat(parts)) as SkillHits)?.Hits;
    byte[] Head(int target, int layout, int actor, uint skill, byte uid = 0x05) => Cat(Var(target), Var(layout), Var(0), Var(actor), BitConverter.GetBytes(skill), new[] { uid }, Var(1));
    Check(Hits(Head(7, 4, 8, 12_000_000u, uid: 0x92), new byte[8], Var(15_000), Var(500), Var(1)) is [{ Actor: 8, Target: 7, Damage: 500 }], "uid byte above 127 does not hide the hit type");
    Check(Hits(Head(7, 4, 8, 12_000_000u), new byte[8], Var(15_000), Var(900), Var(1)) is [{ Damage: 900 }], "uid byte below 128 keeps the damage aligned");
    Check(Hits(Head(7, 4, 8, 12_000_000u), new byte[8], Var(0), Var(15_000), Var(60_876), Var(1)) is [{ Damage: 60_876 }], "a zero pad moves the damage past the power value");
    Check(Hits(Head(7, 6, 8, 12_000_000u), new byte[] { 4 }, Var(300), new byte[8], Var(15_000), Var(700)) is [{ Damage: 700 }], "layout 6 with a two-byte restoration value");
    Check(Hits(Head(7, 5, 8, 12_000_000u), new byte[12], Var(15_000), Var(650)) is [{ Damage: 650 }], "layout 5");
    Check(Hits(Head(7, 0x24, 8, 12_000_000u), new byte[8], Var(15_000), Var(1_000), Var(1), Var(2), Var(100), Var(200)) is [{ Damage: 1_000 }], "additional hits are already in the damage");
    Check(Hits(Head(7, 4, 8, 12_000_000u), new byte[8], Var(15_000), Var(100), Var(1), new byte[] { 1, 0 }, Head(9, 4, 8, 12_000_000u), new byte[8], Var(15_000), Var(200), Var(1)) is [{ Target: 7, Damage: 100 }, { Target: 9, Damage: 200 }], "an area skill's chained targets are all read");
    Check(Hits(Head(7, 4, 8, 1_500_000u), new byte[8], Var(15_000), Var(100), Var(1)) == null, "a creature's skill is not player damage");
    Check(Hits(Head(8, 4, 8, 12_000_000u), new byte[8], Var(15_000), Var(100), Var(1)) == null, "hitting oneself is a heal, not damage");
    Check(Hits(Head(7, 2, 8, 12_000_000u)) == null, "a miss carries no damage");
}

Console.WriteLine($"{checks} protocol checks passed.");
