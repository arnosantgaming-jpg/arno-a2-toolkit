using System.Globalization;
using Avalonia.Controls;

namespace ArnoA2.App;

/// <summary>Which timed events to be told about, and how early.</summary>
public sealed class EventAlerts
{
    public bool Shugo { get; set; }
    public bool AirRaid { get; set; }
    public bool Rift { get; set; }
    public string Server { get; set; } = "Global";
    public int Minutes { get; set; } = 2;

    public bool Any => Shugo || AirRaid || Rift;
}

/// <summary>
/// The game's timed events, from their published schedule on the server clock (GMT+9, Taiwan
/// GMT+8): the Shugo Festival on the hour, the Air Raid at half past, and the Spacetime Rift
/// every three hours (from 00:00 on Global, 02:00 in Korea and Taiwan). Nothing is read from
/// the game, so a schedule change after maintenance needs an update here.
/// </summary>
public static class GameEvents
{
    public const string Shugo = "Shugo Festival", AirRaid = "Air Raid", Rift = "Spacetime Rift";
    public static readonly string[] Servers = { "Global", "Korea", "Taiwan" };
    static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    static TimeSpan Offset(string server) => TimeSpan.FromHours(server == "Taiwan" ? 8 : 9);

    /// <summary>The chosen events that start at this minute.</summary>
    public static IEnumerable<string> StartingAt(DateTimeOffset start, EventAlerts on)
    {
        var server = start.ToOffset(Offset(on.Server));
        if (server.Minute == 0 && on.Shugo)
            yield return Shugo;
        if (server.Minute == 30 && on.AirRaid)
            yield return AirRaid;
        if (server.Minute == 0 && on.Rift && server.Hour % 3 == (on.Server == "Global" ? 0 : 2))
            yield return Rift;
    }

    /// <summary>Events whose alert time, the start less the chosen minutes, falls after
    /// <paramref name="from"/> and up to <paramref name="to"/>. A long gap, such as the PC
    /// sleeping, only looks back two minutes, so old events aren't announced late.</summary>
    public static List<(DateTimeOffset Start, List<string> Names)> Due(DateTimeOffset from, DateTimeOffset to, EventAlerts on)
    {
        var found = new List<(DateTimeOffset, List<string>)>();
        if (to - from > 2 * Minute)
            from = to - 2 * Minute;
        var lead = TimeSpan.FromMinutes(Math.Max(0, on.Minutes));
        var start = Floor(from + lead) + Minute;
        for (; start <= to + lead; start += Minute)
            if (StartingAt(start, on).ToList() is { Count: > 0 } names)
                found.Add((start, names));
        return found;
    }

    /// <summary>When an event next starts, on or after now.</summary>
    public static DateTimeOffset Next(string name, DateTimeOffset now, EventAlerts on)
    {
        var only = new EventAlerts { Shugo = name == Shugo, AirRaid = name == AirRaid, Rift = name == Rift, Server = on.Server };
        var start = Floor(now) + (now == Floor(now) ? TimeSpan.Zero : Minute);
        while (!StartingAt(start, only).Any())
            start += Minute;
        return start;
    }

    /// <summary>What an alert says, e.g. "Shugo Festival and Spacetime Rift in 2 min".</summary>
    public static string Text(IReadOnlyList<string> names, int minutes) =>
        string.Join(" and ", names) + (minutes > 0 ? $" in {minutes.ToString(CultureInfo.InvariantCulture)} min" : (names.Count > 1 ? " start now" : " starts now"));

    static DateTimeOffset Floor(DateTimeOffset time) => new(time.UtcTicks - time.UtcTicks % Minute.Ticks, TimeSpan.Zero);
}

public sealed class EventSettingsPanel : UserControl
{
    public EventSettingsPanel(EventAlerts saved, Action save)
    {
        var next = Ui.Note("");
        void Changed()
        {
            save();
            Show();
        }

        void Show()
        {
            var now = DateTimeOffset.UtcNow;
            next.Text = "Next, on this PC's clock: " + string.Join(" · ", new[] { GameEvents.Shugo, GameEvents.AirRaid, GameEvents.Rift }
                .Select(name => (name, at: GameEvents.Next(name, now, saved).ToLocalTime())).OrderBy(e => e.at).Select(e => $"{e.name} {e.at:HH:mm}"));
        }

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Ui.Heading("Alerts"));
        panel.Children.Add(Ui.Toggle("Shugo Festival, every hour on the hour", saved.Shugo, v =>
        {
            saved.Shugo = v;
            Changed();
        }));
        panel.Children.Add(Ui.Toggle("Air Raid, every hour at half past", saved.AirRaid, v =>
        {
            saved.AirRaid = v;
            Changed();
        }));
        panel.Children.Add(Ui.Toggle("Spacetime Rift, every three hours", saved.Rift, v =>
        {
            saved.Rift = v;
            Changed();
        }));
        panel.Children.Add(Ui.Choice("Server", GameEvents.Servers, saved.Server, v =>
        {
            saved.Server = v;
            Changed();
        }));
        panel.Children.Add(Ui.Number("Minutes before it starts", saved.Minutes, 0, 15, v =>
        {
            saved.Minutes = v;
            Changed();
        }));
        panel.Children.Add(next);
        panel.Children.Add(Ui.Note("Each alert is a sound and a note at the top of the screen, and a banner on the phone map. The times come from the published schedule, not from the game, so after maintenance they can be off: the game's own Event menu is always right. Rifts open at different hours on Global than in Korea and Taiwan."));
        Content = panel;
        Show();
    }
}
