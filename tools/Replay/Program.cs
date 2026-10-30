using ArnoA2.Protocol;
using ArnoA2.Replay;
using ArnoA2.Meter;

// replay <recording>            decode a recording and count the messages
// replay --meter <recording>    print the damage meter, and how many hits could not be read
// replay --live [seconds]       connect to a running capture daemon and print what it sees
// Recordings: a capture file (pcap) of the game connection, or a stream dump.
// They contain your account's game traffic: keep them private.
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: replay <recording> | --meter <recording> | --live [seconds]");
    return 2;
}

if (args[0] == "--live")
    return await Live.Run(args.Length > 1 && int.TryParse(args[1], out int seconds) ? seconds : 60);
if (args[0] == "--meter" && args.Length > 1)
    return Meter(args[1]);
return Decode(args[0]);

static int Decode(string path)
{
    var streams = new Dictionary<string, FrameStream>();
    var counts = new SortedDictionary<string, int>();
    int faults = 0;
    DateTimeOffset now = default;
    foreach (var chunk in Recording.Read(path))
    {
        now = chunk.Time;
        if (chunk.Data.Length == 0)
        {
            foreach (var s in streams.Values)
                s.Reset(); // capture gap
            continue;
        }

        if (!streams.TryGetValue(chunk.Flow, out var stream))
        {
            stream = streams[chunk.Flow] = new FrameStream();
            stream.Faulted += _ => faults++;
            stream.FrameDecoded += frame =>
            {
                var message = MessageDecoder.Decode(frame);
                string name = message?.GetType().Name ?? "(other)";
                counts[name] = counts.GetValueOrDefault(name) + 1;
                if (message is CharacterIdentity identity)
                    Console.WriteLine($"{now:HH:mm:ss} character: entity {identity.Entity}, name of {identity.Name.Length} letters");
                else if (message is ZoneEntered zone)
                    Console.WriteLine($"{now:HH:mm:ss} zone {zone.Zone}");
            };
        }

        stream.Push(chunk.Data);
    }

    Console.WriteLine($"\n{streams.Values.Sum(s => s.Frames)} frames, {streams.Values.Sum(s => s.SkippedBytes)} bytes skipped while resyncing, {faults} faults");
    foreach (var (name, count) in counts)
        Console.WriteLine($"  {name,-20} {count}");
    return 0;
}

static int Meter(string path)
{
    var meter = new DamageMeter();
    var streams = new Dictionary<string, FrameStream>();
    var quiet = TimeSpan.FromSeconds(new Settings().MeterQuietSeconds);
    int? local = null;
    int hits = 0, unread = 0, fights = 0;
    DateTimeOffset now = default;
    foreach (var chunk in Recording.Read(path))
    {
        now = chunk.Time;
        if (chunk.Data.Length == 0)
        {
            foreach (var s in streams.Values)
                s.Reset();
            meter.Interrupt();
            continue;
        }

        if (!streams.TryGetValue(chunk.Flow, out var stream))
        {
            stream = streams[chunk.Flow] = new FrameStream();
            stream.FrameDecoded += frame =>
            {
                var message = MessageDecoder.Decode(frame);
                if (message is CharacterIdentity identity)
                    local = identity.Entity;
                if (message is SkillHits skill)
                {
                    foreach (var hit in skill.Hits)
                    {
                        hits++;
                        fights += meter.Fight.Empty || meter.Fight.Quiet(now, quiet) ? 1 : 0;
                        meter.Hit(hit.Actor, hit.Damage, now, quiet);
                    }
                }
                else if (frame is { Op: 0x04, Family: 0x38 })
                    unread++;
            };
        }

        stream.Push(chunk.Data);
    }

    Console.WriteLine($"{hits} hits read, {unread} hit frames not read, {fights} fights, {meter.Session.Seconds:F1} s in combat{(meter.Session.Incomplete ? ", incomplete" : "")}");
    foreach (var row in meter.Session.Rows(local))
        Console.WriteLine($"  {(row.You ? "you" : "other"),-6} {row.Damage,12:N0} damage {row.Dps,10:N0}/s {row.Hits,6} hits, biggest {row.Biggest:N0}");
    return 0;
}
