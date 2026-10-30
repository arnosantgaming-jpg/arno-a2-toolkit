using System.Text;
using ArnoA2.Meter;
using ArnoA2.Protocol;

// Synthetic meter checks (no account data).
int checks = 0;
void Check(bool ok, string what)
{
    checks++;
    if (!ok)
        throw new Exception("FAILED: " + what);
}

var t0 = DateTimeOffset.Parse("2026-10-24T12:00:00Z");

// --- Damage meter --------------------------------------------------------------------
{
    var quiet = TimeSpan.FromSeconds(8);
    var m = new DamageMeter();
    m.Hit(1, 300, t0, quiet);
    Check(m.Fight.Rows(1)[0] is { You: true, Dps: 300 }, "a lone hit counts as one second, not an enormous rate");
    m.Hit(2, 100, t0.AddSeconds(2), quiet);
    m.Hit(1, 300, t0.AddSeconds(4), quiet);
    var rows = m.Fight.Rows(1);
    Check(rows.Select(r => r.Actor).SequenceEqual(new[] { 1, 2 }) && m.Fight.Seconds == 4 && rows[0] is { Damage: 600, Hits: 2, Biggest: 300, Dps: 150, Share: 600d / 700 } && !rows[1].You, "ranked by damage over the fight's length");
    m.Hit(1, 50, t0.AddSeconds(30), quiet);
    Check(m.Fight.Damage == 50 && m.Fight.Seconds == 0, "a quiet spell starts a new fight");
    Check(m.Session.Damage == 750 && m.Session.Seconds == 4, "the session adds fights up and leaves out the wait between them");
    m.Hit(1, 1, t0.AddSeconds(29), quiet);
    Check(m.Fight.Seconds == 0 && m.Fight.Damage == 51, "a hit stamped out of order adds no time");
    m.Interrupt();
    Check(m.Fight.Incomplete && m.Session.Incomplete, "a gap marks the meter incomplete");
    m.Hit(1, 1, t0.AddMinutes(5), quiet);
    Check(!m.Fight.Incomplete && m.Session.Incomplete, "the next fight is whole again, the session is not");
    m.Reset();
    Check(m.Fight.Empty && m.Session.Empty && !m.Session.Incomplete, "reset clears both");
    m.Interrupt();
    Check(!m.Fight.Incomplete, "an empty meter stays complete");
}

// --- Engine ----------------------------------------------------------------------------
{
    byte[] Frame(byte op, byte family, byte[] body) => Cat(Var(body.Length + 6), new[] { op, family }, body);
    var name = Encoding.UTF8.GetBytes("Tester");
    var identity = Frame(0x33, 0x36, Cat(Var(4242), I32(0), new byte[] { 7, (byte)name.Length }, name, new byte[2], new byte[8]));
    var hit = Frame(0x04, 0x38, Cat(Var(777), Var(4), Var(0), Var(4242), BitConverter.GetBytes(12_000_000u), new byte[] { 0x92 }, Var(1), new byte[8], Var(15_000), Var(500), Var(1)));

    var e = new Engine(store: null, new Settings());
    Check(e.Connection == Connection.Waiting, "waiting before any traffic");
    e.Data(1, hit, t0);
    Check(e.Connection == Connection.Identifying && e.Damage.Fight.Rows(e.LocalEntity) is [{ Actor: 4242, Damage: 500, You: false }], "hits count before the character is known");
    e.Data(1, identity, t0);
    Check(e.Connection == Connection.Live && e.Character == "Tester" && e.Damage.Fight.Rows(e.LocalEntity)[0].You, "the row becomes yours once the character is known");
    e.Gap(1, 100, t0.AddSeconds(1));
    Check(e.Damage.Session.Incomplete && e.Gaps == 1, "a capture gap reaches the meter");

    var seen = new Engine(store: null, new Settings());
    byte[] Spawn(int entity, string who) { var n = Encoding.UTF8.GetBytes(who); return Frame(0x45, 0x36, Cat(Var(entity), I32(0), new byte[] { 7, (byte)n.Length }, n, new byte[9])); }
    byte[] Hit(int actor, uint skill) => Frame(0x04, 0x38, Cat(Var(777), Var(4), Var(0), Var(actor), BitConverter.GetBytes(skill), new byte[] { 5 }, Var(1), new byte[8], Var(15_000), Var(500), Var(1)));
    seen.Data(1, Cat(Spawn(5151, "Other"), Hit(5151, 14_010_000), Hit(6000, 13_010_000), Hit(6001, 12_000_000)), t0);
    var named = seen.Rows(seen.Damage.Fight);
    Check(named.Single(r => r.Actor == 5151) is { Name: "Other", Class: PlayerClass.Ranger }, "name from the spawn, class from the skill");
    Check(named.Single(r => r.Actor == 6000) is { Name: null, Class: PlayerClass.Assassin } && named.Single(r => r.Actor == 6001) is { Name: null, Class: null }, "unknowns stay unknown");
    seen.Data(1, Spawn(5151, "Newcomer"), t0);
    Check(seen.Rows(seen.Damage.Fight).Single(r => r.Actor == 5151) is { Name: "Newcomer", Class: null }, "a reused id does not keep the old character's class");
    seen.Closed(1, t0);
    Check(seen.Rows(seen.Damage.Fight).All(r => r.Name == null && r.Class == null), "logging out forgets names");

    var hinted = new Engine(store: null, new Settings());
    hinted.Data(1, Enumerable.Range(0, 12).SelectMany(_ => Frame(0x00, 0x8D, Cat(Var(9000), new byte[4]))).ToArray(), t0);
    Check(hinted.LocalEntity == 9000 && hinted.Assumed, "player recognised mid-session from self hints");
    hinted.Closed(1, t0);
    Check(hinted.Connection == Connection.Waiting && hinted.LocalEntity == null, "a closed game connection forgets the character");
}

// --- Settings ----------------------------------------------------------------------------
{
    string root = Path.Combine(Environment.GetEnvironmentVariable("ARNOA2_TEST_DIR") ?? Path.GetTempPath(), "arno-dps-" + Environment.ProcessId);
    try
    {
        var store = new Store(root);
        var e = new Engine(store);
        e.ChangeSettings(s => s.MeterQuietSeconds = 500);
        Check(e.Settings.MeterQuietSeconds == 60 && e.SaveIfChanged() && !e.SaveIfChanged(), "clamped, and saved only when changed");
        Check(new Engine(store).Settings.MeterQuietSeconds == 60, "settings restored");
        File.WriteAllText(Path.Combine(root, "settings.json"), "{broken");
        Check(new Engine(store).Settings.MeterQuietSeconds == 8, "a damaged file falls back to the defaults");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

Console.WriteLine($"{checks} meter checks passed.");

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
