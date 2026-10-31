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

    var known = new Engine(store: null, new Settings { Character = "Hero" });
    byte[] Kill(int owner, string who) { var n = Encoding.UTF8.GetBytes(who); return Frame(0x04, 0x8D, Cat(Var(888), I32(0), Var(owner), new byte[] { 0x19, 0x05, (byte)n.Length }, n, new byte[4])); }
    known.Data(1, Cat(Hit(5200, 11_010_000), Kill(5200, "Hero")), t0);
    Check(known.LocalEntity == 5200 && known.Character == "Hero" && known.Rows(known.Damage.Fight) is [{ You: true, Name: "Hero", Class: PlayerClass.Gladiator }], "a kill in your saved name finds your row without a login");
    known.Data(1, Cat(Hit(5300, 14_010_000), Kill(5300, "Friend")), t0);
    Check(known.LocalEntity == 5200 && known.Rows(known.Damage.Fight).Single(r => r.Actor == 5300) is { You: false, Name: "Friend" }, "someone else's kill names them, not you");
    var edited = new Engine(store: null, new Settings());
    edited.Data(1, Cat(Hit(5400, 11_010_000), Kill(5400, "Typed")), t0);
    Check(edited.LocalEntity == null, "without a saved name no row is yours");
    edited.ChangeSettings(s => s.Character = "Typed");
    Check(edited.LocalEntity == 5400, "typing your name finds your row");
    var login = new Engine(store: null, new Settings());
    login.Data(1, identity, t0);
    Check(login.Settings.Character == "Tester", "a login remembers the character");

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
        File.Delete(Path.Combine(root, "settings.json"));

        var first = new Engine(store, new Settings());
        byte[] Frame(byte op, byte family, byte[] body) => Cat(Var(body.Length + 6), new[] { op, family }, body);
        byte[] spawn = Frame(0x45, 0x36, Cat(Var(5151), I32(0), new byte[] { 7, 5 }, Encoding.UTF8.GetBytes("Other"), new byte[9]));
        byte[] hit = Frame(0x04, 0x38, Cat(Var(777), Var(4), Var(0), Var(5151), BitConverter.GetBytes(14_010_000u), new byte[] { 5 }, Var(1), new byte[8], Var(15_000), Var(500), Var(1)));
        first.Opened(1, "a:1 ⇄ b:2");
        first.Data(1, spawn, t0);
        first.SaveIfChanged();
        var restarted = new Engine(store, new Settings());
        restarted.Opened(1, "a:1 ⇄ b:2");
        restarted.Data(1, hit, t0);
        Check(restarted.Rows(restarted.Damage.Fight)[0].Name == "Other", "a restart on the same connection keeps the names");
        var other = new Engine(store, new Settings());
        other.Opened(1, "a:9 ⇄ b:2");
        other.Data(1, hit, t0);
        Check(other.Rows(other.Damage.Fight)[0].Name == null, "a new connection does not trust old ids");
        var nested = Path.Combine(root, "share", "arno-dps-meter");
        Directory.CreateDirectory(Path.Combine(root, "share", "pets"));
        File.WriteAllText(Path.Combine(root, "share", "pets", "data.json"), """{"ActiveProfile":"a","Profiles":[{"Id":"a","Characters":["Hero"]}]}""");
        Check(new Store(nested).Load().Character == "Hero", "first run takes the character from the pet tracker");
        File.WriteAllText(Path.Combine(root, "share", "pets", "data.json"), """{"ActiveProfile":"a","Profiles":[{"Id":"a","Characters":["Hero","Alt"]}]}""");
        Check(new Store(nested).Load().Character == null, "several characters are not guessed between");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

{
    var look = new Settings { MeterOpacity = 5 };
    look.Normalize();
    Check(look.MeterOpacity == 20 && new Settings().MeterOpacity == 50, "the background starts half see-through and never goes fully invisible");
}

// --- Summons -------------------------------------------------------------------------
{
    byte[] Wire(byte op, byte family, byte[] body) => Cat(Var(body.Length + 6), new[] { op, family }, body);
    byte[] Hit(int actor, uint skill, int power) => Wire(0x04, 0x38, Cat(Var(777), Var(4), Var(0), Var(actor), BitConverter.GetBytes(skill), new byte[] { 5 }, Var(1), new byte[8], Var(power), Var(500), Var(1)));
    byte[] Spawn(int entity, int owner) => Wire(0x41, 0x36, Cat(Var(entity), new byte[] { 0x5F, 0, 0, 1, 2, 3 }, new byte[] { 0x80, 0x75, 0xD5, 0x2A, 0xBB, 0x03, 0, 0 }, Var(owner), new byte[4]));
    byte[] Player(int entity, string name) => Wire(0x44, 0x36, Cat(Var(entity), new byte[4], new byte[] { 7, (byte)name.Length }, Encoding.UTF8.GetBytes(name)));
    int Damage(Engine e, int actor) => (int)e.Rows(e.Damage.Fight).Where(r => r.Actor == actor).Sum(r => r.Damage);

    var spawned = new Engine(null, new Settings());
    spawned.Opened(1);
    spawned.Data(1, Cat(Player(100, "Owner"), Hit(100, 16_010_000, 12_000), Hit(40000, 16_100_000, 9_000), Spawn(40000, 100)), t0);
    Check(spawned.Rows(spawned.Damage.Fight) is [{ Actor: 100, Name: "Owner", Damage: 1000, Hits: 2 }], "a spawned summon's damage joins its owner, hits from before the spawn too");

    var shared = new Engine(null, new Settings());
    shared.Opened(1);
    shared.Data(1, Cat(Hit(200, 16_010_000, 12_000), Hit(200, 16_020_000, 12_000), Hit(200, 16_030_000, 12_000), Hit(41000, 16_100_000, 12_000)), t0);
    Check(shared.Rows(shared.Damage.Fight) is [{ Actor: 200, Damage: 2000, Class: PlayerClass.Spiritmaster }], "a summon with no spawn joins the one player who shares its power");

    var players = new Engine(null, new Settings());
    players.Opened(1);
    players.Data(1, Cat(Hit(300, 14_010_000, 12_000), Hit(301, 14_010_000, 12_000), Hit(301, 14_020_000, 12_000)), t0);
    Check(Damage(players, 300) == 500 && Damage(players, 301) == 1000, "two players with one power stay apart");

    var two = new Engine(null, new Settings());
    two.Opened(1);
    var rotation = Enumerable.Range(1, 3).Select(i => (uint)(16_000_000 + i * 10_000)).ToArray();
    two.Data(1, Cat(rotation.SelectMany(s => Cat(Hit(400, s, 12_000), Hit(401, s, 12_000))).ToArray(), Hit(42000, 16_100_000, 12_000)), t0);
    Check(two.Rows(two.Damage.Fight).Count == 3, "a summon with two possible owners stays on its own row");

    var assumed = new Engine(null, new Settings { Character = "Hero" });
    assumed.Opened(1);
    assumed.Data(1, Cat(Cat(Enumerable.Repeat(Wire(0x00, 0x8D, Cat(Var(700), new byte[6])), 12).ToArray()), Hit(700, 14_010_000, 12_000)), t0);
    Check(assumed.Assumed && assumed.Rows(assumed.Damage.Fight) is [{ You: true, Name: "Hero" }], "recognised without a login, you keep your saved name");

    var other = new Engine(null, new Settings());
    other.Opened(1);
    other.Data(1, Cat(Enumerable.Repeat(Wire(0x33, 0x8A, Cat(Var(800), new byte[6])), 12).ToArray()), t0);
    Check(other.LocalEntity == null, "33:8A names other characters, so it does not identify you");

    var mine = new Engine(null, new Settings { Character = "Hero" });
    mine.Opened(1);
    mine.Data(1, Cat(Hit(900, 11_010_000, 12_000), Hit(901, 14_010_000, 12_000)), t0);
    mine.Claim(900);
    Check(mine.Rows(mine.Damage.Fight).Single(r => r.Actor == 900) is { You: true, Name: "Hero" } && mine.Character == "Hero", "the row you mark takes your saved name");
    mine.Data(1, Player(901, "Friend"), t0);
    mine.Claim(901);
    Check(mine.Settings.Character == "Friend" && mine.Rows(mine.Damage.Fight).Single(r => r.Actor == 901).You && !mine.Rows(mine.Damage.Fight).Single(r => r.Actor == 900).You, "marking a named row takes that name as yours");

    var unnamed = new Engine(null, new Settings());
    unnamed.Opened(1);
    unnamed.Data(1, Hit(910, 11_010_000, 12_000), t0);
    unnamed.Claim(910);
    unnamed.Data(1, Cat(Enumerable.Repeat(Wire(0x00, 0x8D, Cat(Var(920), new byte[6])), 12).ToArray()), t0);
    Check(unnamed.LocalEntity == 910, "a marked row is not guessed over");

    byte[] Mob(int entity, int code) => Wire(0x41, 0x36, Cat(Var(entity), new byte[] { 0x0C, 0, 0, (byte)code, (byte)(code >> 8), (byte)(code >> 16), 0, 0, 2 }));
    byte[] Struck(int actor, int target, uint skill, int damage, int type) => Wire(0x04, 0x38, Cat(Var(target), Var(4), Var(0), Var(actor), BitConverter.GetBytes(skill), new byte[] { 5 }, Var(type), new byte[8], Var(12_000), Var(damage), Var(1)));

    string folder = Directory.CreateTempSubdirectory().FullName;
    var logged = new Engine(new Store(folder), new Settings());
    logged.Opened(1);
    logged.Data(1, Cat(Mob(777, 2100003), Mob(778, 2000002), Player(100, "Owner"), Struck(100, 778, 14_110_000, 9_000, 1), Struck(100, 777, 14_110_000, 1_000, 3)), t0);
    logged.Data(1, Struck(100, 777, 14_110_010, 3_000, 1), t0.AddSeconds(4));
    Check(logged.Damage.Fight.Bosses.Damage == 4_000 && logged.Damage.Fight.Damage == 13_000, "boss damage counts apart from the rest");
    Check(logged.TargetName(logged.Damage.Fight.Bosses) == "Neikel of the East" && logged.TargetName(logged.Damage.Fight) == "Draconute Ranger", "targets are named");
    logged.Data(1, Wire(0x42, 0x36, Cat(Var(777), new byte[] { 0, 3 })), t0.AddSeconds(4));
    logged.Tick(t0.AddSeconds(5));
    Check(logged.History.Count == 0, "a fight is not over until it goes quiet");
    logged.Tick(t0.AddSeconds(20));
    logged.Tick(t0.AddSeconds(21));
    Check(logged.History is [{ Boss: true, Killed: true, Target: "Neikel of the East", Damage: 4_000, Players: [{ Name: "Owner", Skills: [{ Name: "Gale Arrow", Damage: 4_000, Hits: 2, Crits: 1, Biggest: 3_000 }] }] }], "a boss fight goes to the history once, skills under one name");

    logged.Data(1, Cat(Struck(100, 778, 14_110_000, 500, 1)), t0.AddSeconds(30));
    logged.Data(1, Struck(100, 778, 14_110_000, 500, 1), t0.AddSeconds(35));
    logged.Tick(t0.AddSeconds(50));
    Check(logged.History.Count == 2 && logged.History[0] is { Boss: false, Killed: false, Damage: 1_000 } && logged.History[1].Boss, "other fights are kept too, newest first");

    logged.Data(1, Struck(100, 778, 14_110_000, 500, 1), t0.AddSeconds(60));
    logged.Tick(t0.AddSeconds(80));
    Check(logged.History.Count == 2, "a fight of a moment is not kept");

    var capped = new Store(folder);
    var sample = logged.History[0];
    for (int i = 0; i < 205; i++)
        capped.Save(sample with { Id = $"other-{i:D5}" });
    Check(Directory.GetFiles(Path.Combine(folder, "history"), "other-*").Length == 200 && logged.History.Any(f => f.Boss), "old fights go, and trash does not push out bosses");

    var strong = new Engine(null, new Settings());
    strong.Opened(1);
    strong.Data(1, Cat(Wire(0x45, 0x36, Cat(Var(950), new byte[4], new byte[] { 7, 3 }, "Ann"u8.ToArray(), new byte[30], new byte[] { 0xF4, 0xCB, 0x1F, 0xA0, 4, 0, 0, 0x0C, 0, 0, 0, 0x73, 0x3D, 2, 0, 0, 0, 0, 0, 0, 0 })), Hit(950, 14_010_000, 12_000)), t0);
    Check(strong.Rows(strong.Damage.Fight) is [{ Name: "Ann", CombatPower: 146_803 }], "a row carries its player's combat power");

    var reused = new Engine(null, new Settings());
    reused.Opened(1);
    reused.Data(1, Cat(Spawn(500, 100), Hit(500, 14_010_000, 12_000), Player(500, "Late")), t0);
    Check(reused.Rows(reused.Damage.Fight) is [{ Actor: 500, Name: "Late" }], "an id that turns out to be a player is no summon");
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
