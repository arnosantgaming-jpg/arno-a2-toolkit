using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ArnoA2.App.Dps;
using Avalonia;
using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ArnoA2.App;

/// <summary>Which tools were open, so the next start brings them back.</summary>
public sealed class HubSettings
{
    public bool Pets { get; set; } = true;
    public bool Meter { get; set; } = true;
    public bool Resources { get; set; }
    public bool Cubes { get; set; }
    public bool Restore { get; set; } = true;
    public bool HideInMenus { get; set; } = true;
    public bool InGameOnly { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;
    public bool Phone { get; set; }
    public bool Ping { get; set; }
    public int? PingX { get; set; }
    public int? PingY { get; set; }
    public bool Session { get; set; }
    public int? SessionX { get; set; }
    public int? SessionY { get; set; }
    public SessionLook SessionLook { get; set; } = new();
    public bool ItemLog { get; set; }
    public int? ItemLogX { get; set; }
    public int? ItemLogY { get; set; }
    public ItemLogLook ItemLogLook { get; set; } = new();
    public bool WindowRules { get; set; }
    public int WindowRulesVersion { get; set; }
    public BarSettings Bars { get; set; } = new();
    public string PhoneKey { get; set; } = "";
    public EventAlerts Events { get; set; } = new();

    static string File(string folder) => Path.Combine(folder, "hub.json");

    public static HubSettings Load(string folder)
    {
        try
        {
            return System.IO.File.Exists(File(folder)) ? JsonSerializer.Deserialize<HubSettings>(System.IO.File.ReadAllText(File(folder))) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File(folder), JsonSerializer.Serialize(this));
    }
}

/// <summary>The toolkit's main window: a status bar on top, pages down the side, and on the
/// Tools page every tool as a row with what it's doing right now, a shortcut and its switch,
/// beside the chosen tool's settings. Closing it closes the toolkit.</summary>
public sealed class HubWindow : Window
{
    const string SteamGame = "steam://rungameid/3393110";
    public const string PetsDrawer = "pets", FinderDrawer = "finder", SessionDrawer = "session", MapDrawer = "map", MeterDrawer = "meter", LogDrawer = "log", ResourceDrawer = "resources", ResourceMapDrawer = "resource-map", CubeDrawer = "cubes", CubeMapDrawer = "cube-map", ToolkitDrawer = "toolkit", PhoneDrawer = "phone", EventsDrawer = "events", AboutDrawer = "about", SettingsPage = "settings", ToolsPage = "tools", BarsPage = "bars-settings", SessionGainsPage = "session-settings", ItemLogPage = "itemlog-settings";

    /// <summary>A tool on the Tools page: its overlay, a live line, a shortcut and its settings tabs.</summary>
    sealed record Tool(string Key, string Name, string Group, Color Accent, string? Icon, string Glyph, string About, Func<bool> Running, Action Open, Action Close, Func<string> Live, (string Label, Action Act)? Quick, (string Tab, Func<Control> Page)[] Pages);

    sealed record ToolRow(Tool Tool, Border Row);

    readonly Border updatePrompt = new() { IsVisible = false, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
    readonly AppState app;
    readonly PetLayer maps;
    readonly ResourceLayer nodes;
    readonly CubeLayer spots;
    readonly PhoneBoard phone;
    readonly MapHub position;
    readonly string folder;
    readonly HubSettings saved;
    readonly Stopwatch session = new();
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly List<Tool> tools = new();
    readonly List<ToolRow> rows = new();
    readonly List<(Border Track, Border Knob, Func<bool> On)> toggles = new();
    readonly List<(string Key, Border Item)> navigation = new();
    readonly Ellipse statusDot = new() { Width = 8, Height = 8 }, captureDot = new() { Width = 8, Height = 8 };
    readonly TextBlock statusText = Text("", 12.5, Navy.Text2, FontWeight.SemiBold), characterText = Text("", 12.5, Navy.Text, FontWeight.SemiBold), sessionText = Text("", 12.5, Navy.Text, FontWeight.SemiBold), pingText = Text("", 12, Navy.Text2, FontWeight.SemiBold), pingBars = Text("▂▄▆", 11, Navy.Muted), eventText = Text("", 12, Navy.Warning), captureText = Text("", 11.5, Navy.Text2), minimapText = Text("", 11.5, Navy.Text2), phoneText = Text("", 11.5, Navy.Text2), overlaysText = Text("", 11.5, Navy.Text2);
    readonly Border eventPill, launch;
    readonly Grid toolsView = new();
    readonly Border pageView = new() { IsVisible = false };
    readonly StackPanel list = new() { Margin = new Thickness(0, 0, 4, 0) };
    readonly Border detail = new() { Margin = new Thickness(16, 0, 0, 0), CornerRadius = new CornerRadius(10), Background = Navy.Surface1, BorderBrush = Navy.Line, BorderThickness = new Thickness(1), ClipToBounds = true };
    readonly Border detailIcon = new() { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock detailTitle = new() { FontSize = 21, FontWeight = FontWeight.Bold, Foreground = Navy.Text, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock detailAbout = Text("", 12.5, Navy.Text2), detailLive = Text("", 12, Navy.Muted), badgeText = Text("", 11.5, Navy.Muted, FontWeight.SemiBold);
    readonly Border detailBadge = new() { Padding = new Thickness(9, 2), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    readonly Avalonia.Controls.Primitives.UniformGrid tabs = new() { Rows = 1 };
    readonly Border tabsFrame = new() { CornerRadius = new CornerRadius(8), BorderBrush = Navy.Line, BorderThickness = new Thickness(1), Background = Navy.Background, Padding = new Thickness(2) };
    readonly Border detailBody = new();
    readonly Border popOut, detailSwitch = new(), detailQuick = new();
    Tool? current;
    int tab;
    TrackerWindow? pets;
    MeterWindow? meter;
    ResourceWindow? resources;
    CubeWindow? cubes;
    PingWindow? ping;
    BarsWindow? bars;
    SessionWindow? gains;
    ItemLogWindow? itemLog;
    AlertWindow? alert;
    MenuWatch? menus;
    readonly Dictionary<Window, PixelPoint> stepped = new();
    readonly DispatcherTimer menuCheck = new() { Interval = TimeSpan.FromMilliseconds(100) };
    static readonly HashSet<string> Overlays = new() { KdeWindowRules.TrackerTitle, KdeWindowRules.MiniMapTitle, KdeWindowRules.MeterTitle, KdeWindowRules.ResourceTitle, KdeWindowRules.CubeTitle, KdeWindowRules.PingTitle, KdeWindowRules.BarsTitle, KdeWindowRules.SessionTitle, KdeWindowRules.ItemLogTitle };
    DateTimeOffset eventsChecked = DateTimeOffset.UtcNow;
    DateTimeOffset? lastCube;
    string opened = ToolsPage;
    bool closing;

    public HubWindow(AppState app, MapHub position)
    {
        this.app = app;
        this.position = position;
        maps = new PetLayer(app, position);
        nodes = new ResourceLayer(position, app.Store.Folder);
        spots = new CubeLayer(position, app.Store.Folder);
        folder = Path.GetDirectoryName(app.Store.Folder)!;
        saved = HubSettings.Load(folder);
        phone = new PhoneBoard(position, maps, nodes, spots, saved.PhoneKey, key => Remember(s => s.PhoneKey = key));
        Title = "Arno's A2 Toolkit";
        Icon = ToolTheme.AppIcon;
        Width = 1280;
        Height = 800;
        MinWidth = 980;
        MinHeight = 620;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Navy.Background;
        ToolTheme.Tint(this, Navy.AccentColor);

        tabsFrame.Child = tabs;
        detailBadge.Child = badgeText;
        eventPill = Pill(null, eventText);
        eventPill.BorderBrush = Brush.Parse("#7A5E26");
        launch = Button("Launch AION 2", LaunchGame, "Starts the game through Steam", primary: true);
        popOut = Button("Open in window", PopOutMap, "Move the map to a window of its own, for a second monitor");
        DefineTools();

        var content = new Panel { Children = { toolsView, pageView } };
        BuildTools();
        var body = new DockPanel();
        var top = TopBar();
        DockPanel.SetDock(top, Dock.Top);
        body.Children.Add(top);
        var bottom = BottomBar();
        DockPanel.SetDock(bottom, Dock.Bottom);
        body.Children.Add(bottom);
        var side = Sidebar();
        DockPanel.SetDock(side, Dock.Left);
        body.Children.Add(side);
        body.Children.Add(content);

        var grip = new Border { Width = 16, Height = 16, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.BottomRightCorner), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        grip.PointerPressed += (_, e) =>
        {
            BeginResizeDrag(WindowEdge.SouthEast, e);
            e.Handled = true;
        };
        Compact(pageView);
        Content = new Panel { Background = Navy.Background, Children = { body, grip } };
        ShowTool(tools[0], 0);
        Mark();

        app.Changed += Refresh;
        app.CubeSeen += CubeAlert;
        clock.Tick += (_, _) =>
        {
            Refresh();
            EventCheck();
        };
        clock.Start();
        menuCheck.Tick += (_, _) => HideOverlays(menus?.Open == true || saved.InGameOnly && app.Meter.LocalEntity == null);
        menuCheck.Start();
        Refresh();
        Opened += (_, _) =>
        {
            if (saved.Phone)
                phone.Start();
            if (spots.Preferences.Alerts || saved.Events.Any)
                KeepAbove();
            if (saved.Restore && saved.Pets)
                OpenPets();
            if (saved.Restore && saved.Meter)
                OpenMeter();
            if (saved.Restore && saved.Resources)
                OpenResources();
            if (saved.Restore && saved.Cubes)
                OpenCubes();
            if (saved.Restore && saved.Ping)
                OpenPing();
            if (saved.Restore && saved.Bars.Open)
                OpenBars();
            if (saved.Restore && saved.Session)
                OpenSession();
            if (saved.Restore && saved.ItemLog)
                OpenItemLog();
        };
        Closing += (_, _) => closing = true;
        Closed += (_, _) =>
        {
            app.Changed -= Refresh;
            app.CubeSeen -= CubeAlert;
            alert?.Close();
            clock.Stop();
            menuCheck.Stop();
            menus?.Dispose();
            phone.Stop();
            pets?.Close();
            meter?.Close();
            resources?.Close();
            cubes?.Close();
            ping?.Close();
            bars?.Close();
            gains?.Close();
            itemLog?.Close();
        };
    }

    void DefineTools()
    {
        var invariant = CultureInfo.InvariantCulture;
        string Overlay(string what) => what + " Drag the overlay to move it.";
        tools.Add(new("meter", "DPS Meter", "COMBAT", Color.Parse("#FF5968"), "cdps.png", "", "Real-time damage for you and your party, with a skill breakdown and boss fights saved.",
            () => meter != null, OpenMeter, () => meter?.Close(), () =>
            {
                var fight = app.Meter.Damage.Fight;
                if (fight.Empty)
                    return "No fight yet";
                var you = fight.Rows(app.Meter.LocalEntity).FirstOrDefault(r => r.You);
                string time = TimeSpan.FromSeconds(fight.Seconds).ToString(@"m\:ss", invariant);
                return you == null ? $"Fight {time} · you haven't hit yet" : $"Fight {time} · you {Short(you.Dps)} DPS · {you.Share.ToString("P0", invariant)} of the damage";
            }, ("Fight log", () => ShowDrawer(LogDrawer)),
            new (string, Func<Control>)[] { ("Settings", () => new MeterSettingsPanel(app, () => meter?.ApplyOpacity())), ("Fight log", () => new HistoryPanel(app)) }));
        tools.Add(new("bars", "HP and Mana", "COMBAT", Color.Parse("#45A8FF"), null, "♥", Overlay("Your HP, mana and stamina as bars above the game, with a shield on HP while one holds."),
            () => bars != null, OpenBars, () => bars?.Close(), () => app.Meter.Own() is { Hp: not null } own
                ? $"HP {own.HpShare.ToString("P0", invariant)}" + (own.Shield > 0 ? $" · shield +{own.Shield.ToString("N0", invariant)}" : "") + (own.Mana != null ? $" · mana {own.ManaShare.ToString("P0", invariant)}" : "")
                : "Waiting for your HP", null,
            new (string, Func<Control>)[] { ("Settings", () => new BarsSettingsPanel(saved.Bars, () => Changed(() => bars?.Restyle()))) }));
        tools.Add(new("minimap", "Mini-map", "FARMING", Color.Parse("#5FC8E8"), null, "◎", "One small map that follows you. Each map tool you turn on (pets, resources, cubes) adds its ring and marks to it.",
            () => position.Mini.On, () => position.Mini.Switch(true), () => position.Mini.Switch(false), () => !position.Mini.On ? "Off"
                : position.Mini.Active.Count == 0 ? "Waiting for a map tool to be turned on"
                : "Showing " + string.Join(", ", position.Mini.Active.Select(l => l == maps ? "pets" : l == nodes ? "resources" : "cubes")),
            null, new (string, Func<Control>)[] { ("Settings", () =>
            {
                var panel = new StackPanel { Spacing = 10 };
                MiniMapSettings.Add(panel, position);
                panel.Children.Add(Ui.Note("Drag the mini-map to move it; its slider sets how see-through it is. Turn on the Pet, Resource or Hidden Cube Tracker to put their marks on it."));
                return panel;
            }) }));
        tools.Add(new("resources", "Resource Tracker", "FARMING", Color.Parse("#29D391"), "cleaf.png", "", "Where gathering nodes and materials spawn, on the map and mini-map.",
            () => resources != null, OpenResources, () => resources?.Close(), () =>
            {
                long gathered = app.Meter.Items.Items.Where(i => ItemTable.Get(i.Item).Category == "Gatherable Material").Sum(i => i.Amount);
                return $"{Region()} · {Plural(nodes.Preferences.Kinds.Count, "kind")} shown · {gathered.ToString("N0", invariant)} gathered";
            }, ("Map", OpenResourceMap),
            new (string, Func<Control>)[] { ("Settings", () => new ResourceSettingsPanel(nodes, () => resources)), ("Map", () => new MapPanel(nodes)) }));
        tools.Add(new("pets", "Pet Tracker", "FARMING", Color.Parse("#65BFFF"), "cpet.png", "", "Pet ranks, the souls you still need and where to farm them.",
            () => pets != null, OpenPets, () => pets?.Close(), () => $"{Plural((int)app.Engine.Session.TotalSouls, "soul")} and {Plural(app.Engine.Session.Kills, "kill")} this session",
            ("Find a pet", () => ShowDrawer(FinderDrawer)),
            new (string, Func<Control>)[] { ("Settings", () => new PetSettingsPanel(app, maps)), ("Find a pet", () => new FinderPanel(app, maps, OpenMap)), ("Session", () => new SessionPanel(app)), ("Map", () => new MapPanel(maps)) }));
        tools.Add(new("cubes", "Hidden Cube Tracker", "FARMING", Color.Parse("#B879FF"), "ccube.png", "", "Where Hidden Cubes can appear in Verteron and Altgard, and an alert when one does.",
            () => cubes != null, OpenCubes, () => cubes?.Close(), () => (spots.Preferences.Alerts ? "Alerts on" : "Alerts off") + (lastCube is { } seen ? $" · last cube {Ago(seen)}" : " · no cube seen yet"),
            ("Map", OpenCubeMap),
            new (string, Func<Control>)[] { ("Settings", () => new CubeSettingsPanel(spots, () => cubes)), ("Map", () => new MapPanel(spots)) }));
        tools.Add(new("session", "Session Gains", "SESSION", Color.Parse("#E8B64C"), null, "◈", Overlay("Kinah, bound Kinah, Abyss Points and Rank Points earned since the session started, in all and per hour."),
            () => gains != null, OpenSession, () => gains?.Close(), () =>
            {
                var gained = app.Meter.Session;
                var now = DateTimeOffset.UtcNow;
                string rate = gained.PerHour(930100031, now) is double perHour ? " · " + SessionWindow.Rate(perHour) : "";
                return $"{SessionWindow.Elapsed(now - gained.Since)} · Kinah {SessionWindow.Earned(gained.Gained(930100031))}{rate} · Abyss {SessionWindow.Earned(gained.Gained(930100003))}";
            }, ("New session", () => app.Meter.Session.Restart(DateTimeOffset.UtcNow)),
            new (string, Func<Control>)[] { ("Settings", () => new GainsSettingsPanel(saved.SessionLook, () => Changed(() => gains?.Restyle()), () =>
            {
                app.Meter.Session.Restart(DateTimeOffset.UtcNow);
                gains?.Restyle();
            })) }));
        tools.Add(new("itemlog", "Item Log", "SESSION", Color.Parse("#45D6E0"), null, "≡", Overlay("The items you obtain, newest on top and in their grade's colour, apart from chat."),
            () => itemLog != null, OpenItemLog, () => itemLog?.Close(), () => app.Meter.Items.Items is { Count: > 0 } items
                ? $"Last: {ItemTable.Get(items[0].Item).Name} {ItemLogWindow.Amount(items[0].Amount)} · {Plural(items.Count, "line")}"
                : "Nothing obtained yet", ("Clear", () => app.Meter.Items.Clear()),
            new (string, Func<Control>)[] { ("Settings", () => new ItemLogSettingsPanel(saved.ItemLogLook, () => Changed(() => itemLog?.Refresh(true)), () => app.Meter.Items.Clear())) }));
        tools.Add(new("ping", "Server Ping", "STATUS", Color.Parse("#4F9BEA"), null, "◉", Overlay("The game's own ping in a small readout you can drag anywhere."),
            () => ping != null, OpenPing, () => ping?.Close(), () => app.Ping is int ms ? $"{ms} ms" : "Waiting for a reading", null, Array.Empty<(string, Func<Control>)>()));
    }

    Tool ToolOf(string key) => tools.First(t => t.Key == key);

    // ---------------------------------------------------------------- frame

    Control TopBar()
    {
        var logo = new Image { Source = Picture("logo.png"), Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(logo, BitmapInterpolationMode.HighQuality);
        var game = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { logo, Text("AION 2", 14, Navy.Text, FontWeight.SemiBold), statusDot, statusText, launch } };
        var who = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36, 0, 0, 0), Children = { Text("Character", 12, Navy.Muted), characterText, new Border { Width = 1, Height = 16, Background = Navy.LineStrong, Margin = new Thickness(8, 0) }, Text("Session", 12, Navy.Muted), sessionText } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(WindowButton("—", () => WindowState = WindowState.Minimized, "Minimise"));
        buttons.Children.Add(WindowButton("□", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, "Maximise"));
        buttons.Children.Add(WindowButton("✕", Close, "Close the toolkit"));
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center, Children = { eventPill, buttons } };
        var bar = new DockPanel { Height = 50 };
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { game, who } });
        var frame = new Border { Background = Navy.Sidebar, BorderBrush = Navy.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(18, 0, 8, 0), Child = bar };
        frame.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else
                    BeginMoveDrag(e);
            }
        };
        return frame;
    }

    Control BottomBar()
    {
        static Control Fact(string label, Control value) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { Text(label, 11.5, Navy.Muted), value } };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26, VerticalAlignment = VerticalAlignment.Center, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { captureDot, Fact("Capture:", captureText) } }, Fact("Mini-map:", minimapText), Fact("Phone map:", phoneText) } };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, VerticalAlignment = VerticalAlignment.Center, Children = { Fact("Overlays:", overlaysText), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { pingBars, pingText } } } };
        var bar = new DockPanel { Height = 34 };
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(left);
        return new Border { Background = Navy.Sidebar, BorderBrush = Navy.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 0, 22, 0), Child = bar };
    }

    Control Sidebar()
    {
        // The logo carries the toolkit's name.
        var brand = new Image { Source = Picture("logo.png"), Width = 128, Height = 128, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 10) };
        RenderOptions.SetBitmapInterpolationMode(brand, BitmapInterpolationMode.HighQuality);
        var items = new StackPanel { Spacing = 4, Margin = new Thickness(12, 0) };
        foreach (var (key, label, icon) in new[] { (ToolsPage, "Tools", "i-home.png"), (EventsDrawer, "Event Alerts", "i-bell.png"), (PhoneDrawer, "Phone Map", "i-phone.png"), (SettingsPage, "Settings", "p-quick.png"), (ToolkitDrawer, "Toolkit", "i-gear.png"), (AboutDrawer, "About", "i-info.png") })
        {
            if (key == AboutDrawer)
                items.Children.Add(new Border { Height = 1, Background = Navy.Line, Margin = new Thickness(6, 8) });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Children = { Art(icon, 20, 20), Text(label, 13.5, Navy.Text) } };
            var item = Hot(new Border { Height = 40, Padding = new Thickness(12, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Background = Brushes.Transparent, Child = row }, () => ShowDrawer(key));
            navigation.Add((key, item));
            items.Children.Add(item);
        }

        // The castle fades into the menu above it; the version sits on it.
        var art = new Image { Source = Picture("nav-bottom.png"), Stretch = Stretch.UniformToFill, Height = 190, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Bottom };
        var fade = new Border { Height = 190, VerticalAlignment = VerticalAlignment.Bottom, Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#FF0B111D"), 0), new GradientStop(Color.Parse("#000B111D"), 0.45), new GradientStop(Color.Parse("#990B111D"), 1) } } };
        var foot = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 14), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Children = { updatePrompt, new TextBlock { Text = "v" + Version, FontSize = 12, Foreground = Navy.Text2, HorizontalAlignment = HorizontalAlignment.Center } } };
        var bottom = new Panel { Height = 190, ClipToBounds = true, Children = { art, fade, foot } };
        var side = new DockPanel { Width = 210 };
        DockPanel.SetDock(brand, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        side.Children.Add(brand);
        side.Children.Add(bottom);
        side.Children.Add(items);
        return new Border { Background = Navy.Sidebar, BorderBrush = Navy.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = side };
    }

    // ---------------------------------------------------------------- tools page

    void BuildTools()
    {
        toolsView.Margin = new Thickness(22, 18, 22, 18);
        toolsView.RowDefinitions = new RowDefinitions("Auto,*");
        toolsView.ColumnDefinitions = new ColumnDefinitions("260,*");
        var title = new StackPanel { Margin = new Thickness(2, 0, 0, 16), Children = { Text("Tools", 24, Navy.Text, FontWeight.Bold), Text("Your AION 2 companion tools and overlays. Switch one on to show its overlay.", 12.5, Navy.Text2) } };
        Grid.SetColumnSpan(title, 2);
        toolsView.Children.Add(title);

        list.Spacing = 8;
        foreach (var tool in tools)
            list.Children.Add(Row(tool));
        var scroll = new ScrollViewer { Content = list };
        Grid.SetRow(scroll, 1);
        toolsView.Children.Add(scroll);

        var header = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { detailQuick, popOut, detailSwitch } };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { detailIcon, detailTitle, detailBadge } });
        detailAbout.TextWrapping = TextWrapping.Wrap;
        detailAbout.Margin = new Thickness(0, 8, 0, 0);
        detailLive.Margin = new Thickness(0, 4, 0, 0);
        tabsFrame.Margin = new Thickness(0, 16, 0, 0);
        var top = new StackPanel { Margin = new Thickness(20, 18, 20, 0), Children = { header, detailAbout, detailLive, tabsFrame } };
        var panel = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(detailBody);
        detail.Child = panel;
        Compact(detailBody);
        Grid.SetColumn(detail, 1);
        Grid.SetRow(detail, 1);
        toolsView.Children.Add(detail);
    }

    Control Row(Tool tool)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        var icon = IconOf(tool, 24);
        icon.Width = 26;
        grid.Children.Add(icon);
        var name = Text(tool.Name, 13.5, Navy.Text, FontWeight.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var toggle = Toggle(tool.Running, () =>
        {
            if (tool.Running())
                tool.Close();
            else
                tool.Open();
        }, "Show or hide the " + tool.Name + " overlay");
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(toggle);
        var row = Hot(new Border { Height = 50, Padding = new Thickness(14, 0, 12, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = grid }, () => ShowTool(tool, 0));
        ToolTip.SetTip(row, tool.About);
        rows.Add(new(tool, row));
        return row;
    }

    static Control IconOf(Tool tool, double size)
    {
        Control inside = tool.Icon is { } png ? Art(png, size, size) : new TextBlock { Text = tool.Glyph, FontSize = size * 0.8, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(tool.Accent), TextAlignment = TextAlignment.Center };
        inside.HorizontalAlignment = HorizontalAlignment.Center;
        inside.VerticalAlignment = VerticalAlignment.Center;
        return inside;
    }

    void ShowTool(Tool tool, int page)
    {
        current = tool;
        tab = Math.Clamp(page, 0, Math.Max(0, tool.Pages.Length - 1));
        opened = ToolsPage;
        toolsView.IsVisible = true;
        pageView.IsVisible = false;
        pageView.Child = null;
        detailIcon.Child = IconOf(tool, 28);
        detailTitle.Text = tool.Name;
        detailAbout.Text = tool.About;
        detailSwitch.Child = Toggle(tool.Running, () =>
        {
            if (tool.Running())
                tool.Close();
            else
                tool.Open();
        }, "Show or hide the overlay");
        detailQuick.Child = tool.Quick is { } quick ? Button(quick.Label, quick.Act, null) : null;
        ToolTheme.Tint(detail, tool.Accent);

        // Tabs as one segmented bar, the chosen one filled; none for a single page.
        tabs.Children.Clear();
        tabs.Columns = tool.Pages.Length;
        for (int i = 0; i < tool.Pages.Length; i++)
        {
            int index = i;
            bool on = i == tab;
            var label = new TextBlock { Text = tool.Pages[i].Tab, FontSize = 13, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, Foreground = on ? Brushes.White : Navy.Text2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            tabs.Children.Add(Hot(new Border { Height = 34, Margin = new Thickness(2), CornerRadius = new CornerRadius(6), Background = on ? Navy.Accent : Brushes.Transparent, Child = label }, () => ShowTool(tool, index)));
        }

        tabsFrame.IsVisible = tool.Pages.Length > 1;
        bool map = tool.Pages.Length > 0 && tool.Pages[tab].Tab == "Map";
        popOut.IsVisible = map;
        detailBody.Margin = map ? new Thickness(1, 12, 1, 1) : new Thickness(20, 16, 20, 18);
        detailBody.Child = tool.Pages.Length == 0 ? Cardify(new StackPanel { Children = { Ui.Note("This overlay has no settings. Drag it to move it.") } }) : Scrolled(Cardify(tool.Pages[tab].Page()));
        Refresh();
        Mark();
    }

    /// <summary>Puts a settings panel's sections in cards: each heading starts a card, titled by it.</summary>
    static Control Cardify(Control page)
    {
        object? inner = (page as ContentControl)?.Content;
        var stack = page as StackPanel ?? inner as StackPanel ?? (inner as ScrollViewer)?.Content as StackPanel;
        if (stack == null)
            return page;
        var children = stack.Children.ToList();
        bool sections = children.Any(c => c is TextBlock t && t.Classes.Contains("heading"));
        if (!sections && children.Any(c => c.Classes.Contains("card")))
            return page;
        stack.Children.Clear();
        var cards = new StackPanel { Spacing = 12 };
        StackPanel? body = null;
        foreach (var child in children)
        {
            if (child is TextBlock heading && heading.Classes.Contains("heading"))
            {
                body = null;
                cards.Children.Add(Card(heading.Text ?? "", out body));
                continue;
            }

            if (body == null)
                cards.Children.Add(Card(null, out body));
            body.Children.Add(child);
        }

        stack.Spacing = 0;
        stack.Children.Add(cards);
        return page;
    }

    static Border Card(string? title, out StackPanel body)
    {
        body = new StackPanel { Spacing = 10 };
        var inside = new StackPanel();
        if (title != null)
        {
            inside.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Navy.Text });
            inside.Children.Add(new Border { Height = 1, Background = Navy.Line, Margin = new Thickness(-16, 10, -16, 12) });
        }

        inside.Children.Add(body);
        var card = new Border { Padding = new Thickness(16, 12, 16, 14), CornerRadius = new CornerRadius(8), Background = Navy.Surface2, BorderBrush = Navy.Line, BorderThickness = new Thickness(1), Child = inside };
        card.Classes.Add("card");
        return card;
    }

    // Smaller text and controls inside the settings, so a page fits without scrolling far.
    static void Compact(Control area)
    {
        area.SetValue(TextElement.FontSizeProperty, 13.0);
        area.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<CheckBox>()) { Setters = { new Avalonia.Styling.Setter(MinHeightProperty, 26.0) } });
        area.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<ComboBox>()) { Setters = { new Avalonia.Styling.Setter(MinHeightProperty, 30.0), new Avalonia.Styling.Setter(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(10, 4)) } });
        area.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Button>()) { Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(12, 5)), new Avalonia.Styling.Setter(Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, new CornerRadius(6)) } });
    }

    // ---------------------------------------------------------------- other pages

    /// <summary>Opens a page: a tool's tab on the Tools page, or one of the side menu's pages.</summary>
    public void ShowDrawer(string key)
    {
        switch (key)
        {
            case ToolsPage:
                ShowTool(current ?? tools[0], tab);
                return;
            case PetsDrawer or FinderDrawer or SessionDrawer or MapDrawer:
                ShowTool(ToolOf("pets"), key switch { FinderDrawer => 1, SessionDrawer => 2, MapDrawer => 3, _ => 0 });
                return;
            case MeterDrawer or LogDrawer:
                ShowTool(ToolOf("meter"), key == LogDrawer ? 1 : 0);
                return;
            case ResourceDrawer or ResourceMapDrawer:
                ShowTool(ToolOf("resources"), key == ResourceMapDrawer ? 1 : 0);
                return;
            case CubeDrawer or CubeMapDrawer:
                ShowTool(ToolOf("cubes"), key == CubeMapDrawer ? 1 : 0);
                return;
            case BarsPage or SessionGainsPage or ItemLogPage:
                ShowTool(ToolOf(key == BarsPage ? "bars" : key == SessionGainsPage ? "session" : "itemlog"), 0);
                return;
        }

        var (title, page) = key switch
        {
            EventsDrawer => ("Event Alerts", (Func<Control>)(() => new EventSettingsPanel(saved.Events, () =>
            {
                Remember(_ => { });
                if (saved.Events.Any)
                    KeepAbove();
            }))),
            PhoneDrawer => ("Phone Map", () => new PhoneSettings(phone, on => Remember(s => s.Phone = on))),
            ToolkitDrawer => ("Toolkit", () => new StackPanel { Spacing = 14, Children = { Cardify(new ToolkitSettingsPanel(app)), Button("Open the data folder", OpenData, "Open the folder your data is kept in") } }),
            SettingsPage => ("Settings", SettingsPanel),
            _ => ("About", () => new AboutPanel(app, Version, saved.CheckUpdates, on => Remember(s => s.CheckUpdates = on)))
        };
        opened = key;
        toolsView.IsVisible = false;
        pageView.IsVisible = true;
        var inner = new StackPanel { Spacing = 14, MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(28, 22, 28, 24), Children = { Text(title, 24, Navy.Text, FontWeight.Bold), Cardify(page()) } };
        pageView.Child = new ScrollViewer { Content = inner };
        Mark();
    }

    Control SettingsPanel()
    {
        var panel = new StackPanel { Spacing = 4 };
        void Setting(string label, string about, Func<bool> on, Action flip)
        {
            var words = new StackPanel { Children = { Text(label, 13.5, Navy.Text), Text(about, 11.5, Navy.Muted) } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(words);
            var toggle = Toggle(on, flip, label);
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);
            panel.Children.Add(new Border { Padding = new Thickness(12, 9), CornerRadius = new CornerRadius(6), Background = Navy.Surface1, BorderBrush = Navy.Line, BorderThickness = new Thickness(1), Child = grid });
        }

        Setting("Start at login", "Open the toolkit when you sign in to your desktop.", () => Desktop.StartsAtLogin, () =>
        {
            try
            {
                Desktop.SetStartsAtLogin(!Desktop.StartsAtLogin);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the switch shows what's really there
            }
        });
        Setting("Reopen tools at start", "Bring back the overlays that were open last time.", () => saved.Restore, () => Remember(s => s.Restore = !s.Restore));
        Setting("Hide in game menus", "Step overlays out of the way while a game menu covers the screen.", () => saved.HideInMenus, () => Remember(s => s.HideInMenus = !s.HideInMenus));
        Setting("Only while in game", "Show overlays only once your character is found.", () => saved.InGameOnly, () => Remember(s => s.InGameOnly = !s.InGameOnly));
        Setting("Check for updates at start", "Asks GitHub for the newest version number; nothing about you is sent.", () => saved.CheckUpdates, () => Remember(s => s.CheckUpdates = !s.CheckUpdates));
        Refresh();
        return panel;
    }

    // Lights the side menu's page; Tools for any tool.
    void Mark()
    {
        foreach (var (key, item) in navigation)
        {
            bool on = key == opened;
            item.Background = on ? Brush.Parse("#1A339DFF") : Brushes.Transparent;
            item.BorderBrush = on ? Navy.Accent : Brushes.Transparent;
        }
    }

    // The fight log, the finder and the maps scroll their own lists.
    static Control Scrolled(Control panel) => panel is HistoryPanel or FinderPanel or MapPanel ? panel : new ScrollViewer { Content = panel };

    // An overlay setting changed: keep it and redraw that overlay.
    void Changed(Action redraw)
    {
        saved.Save(folder);
        redraw();
    }

    // An overlay's "Settings…": the toolkit comes forward on that tool's page.
    void Bring(string key)
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        ShowTool(ToolOf(key), 0);
    }

    // ---------------------------------------------------------------- refresh

    void Refresh()
    {
        bool found = app.CaptureStatus.StartsWith("Capturing", StringComparison.Ordinal);
        if (found && !session.IsRunning)
            session.Restart();
        else if (!found)
            session.Reset();

        bool live = found && app.Meter.Connection == ArnoA2.Meter.Connection.Live;
        string who = app.Meter.Character ?? app.Meter.Settings.Character ?? "";
        statusText.Text = !found ? "Waiting for the game" : live ? "Connected" : "Finding you";
        var tone = !found ? Navy.Warning : live ? Navy.Success : Navy.Accent;
        statusDot.Fill = tone;
        statusText.Foreground = tone;
        characterText.Text = who.Length > 0 ? who : "—";
        sessionText.Text = found ? session.Elapsed.ToString(@"hh\:mm\:ss") : "—";
        launch.IsVisible = !found;
        var (eventName, eventAt) = NextEvent(DateTimeOffset.UtcNow);
        var wait = eventAt - DateTimeOffset.UtcNow;
        eventText.Text = wait.TotalMinutes < 1 ? $"{eventName} now" : wait.TotalHours < 1 ? $"{eventName} in {(int)wait.TotalMinutes} min" : $"{eventName} at {eventAt.ToLocalTime():HH:mm}";
        captureDot.Fill = app.CaptureProblem ? Navy.Danger : found ? Navy.Success : Navy.Warning;
        captureText.Text = app.CaptureStatus;
        captureText.Foreground = app.CaptureProblem ? Navy.Danger : found ? Navy.Success : Navy.Text2;
        minimapText.Text = !position.Mini.On ? "Off" : position.Mini.Window != null ? "On" : "Waiting for a map tool";
        phoneText.Text = phone.Link is { } link ? new Uri(link).Authority : "Off";
        overlaysText.Text = $"{tools.Count(t => t.Running())} / {tools.Count}";
        pingText.Text = app.Ping is int ms ? $"{ms} ms" : "— ms";
        var pingTone = app.Ping switch { null => Navy.Muted, < 80 => Navy.Success, < 150 => Navy.Warning, _ => Navy.Danger };
        pingText.Foreground = pingBars.Foreground = pingTone;

        foreach (var (tool, row) in rows)
        {
            bool chosen = tool == current && opened == ToolsPage;
            row.Background = chosen ? Navy.Surface3 : Navy.Surface1;
            row.BorderBrush = chosen ? Navy.Accent : Navy.Line;
        }

        if (current != null)
        {
            bool running = current.Running();
            detailLive.Text = current.Live();
            badgeText.Text = running ? "● On" : "Off";
            badgeText.Foreground = running ? Navy.Success : Navy.Muted;
            detailBadge.BorderBrush = running ? Navy.Success : Navy.LineStrong;
            detailBadge.Background = running ? Brush.Parse("#1A29D391") : Brushes.Transparent;
        }

        foreach (var (track, knob, on) in toggles)
        {
            bool yes = on();
            track.Background = yes ? Navy.Accent : Navy.LineStrong;
            knob.HorizontalAlignment = yes ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }


        WatchMenus();
    }

    (string Name, DateTimeOffset At) NextEvent(DateTimeOffset now) =>
        new[] { GameEvents.Shugo, GameEvents.AirRaid, GameEvents.Rift }.Select(name => (name, GameEvents.Next(name, now, saved.Events))).MinBy(e => e.Item2);

    string Region() => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(position.Region.Replace('_', ' ').Replace('-', ' '));

    static string Plural(long count, string noun) => $"{count.ToString("N0", CultureInfo.InvariantCulture)} {noun}{(count == 1 ? "" : "s")}";

    static string Short(double value) => value >= 1_000_000 ? (value / 1_000_000).ToString("0.0", CultureInfo.InvariantCulture) + "M" : value >= 1_000 ? (value / 1_000).ToString("0.0", CultureInfo.InvariantCulture) + "k" : value.ToString("0", CultureInfo.InvariantCulture);

    static string Ago(DateTimeOffset at) => (DateTimeOffset.UtcNow - at) is var span && span.TotalMinutes < 1 ? "just now" : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago" : $"{(int)span.TotalHours} h ago";

    // The overlays (not alerts) step out of the way until your character is found and while a
    // game menu covers the screen. The corner is only watched while an overlay is up.
    void WatchMenus()
    {
        bool wanted = saved.HideInMenus && OverlayWindows().Any();
        if (wanted && menus == null)
            menus = new MenuWatch();
        else if (!wanted && menus != null)
        {
            menus.Dispose();
            menus = null;
        }
    }

    static IEnumerable<Window> OverlayWindows() =>
        Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.Where(w => Overlays.Contains(w.Title ?? "")) : Enumerable.Empty<Window>();

    // Taken off the screen rather than made see-through, so clicks reach the game beneath.
    void HideOverlays(bool hide)
    {
        if (hide)
        {
            foreach (var window in OverlayWindows().Where(w => w.IsVisible).ToList())
            {
                stepped[window] = window.Position;
                window.Hide();
            }
        }
        else if (stepped.Count > 0)
        {
            // One closed meanwhile is no longer listed and stays closed.
            var open = OverlayWindows().ToHashSet();
            foreach (var (window, place) in stepped)
                if (open.Contains(window) && window is not BarsWindow { HiddenByUser: true })
                    Reshow(window, place);
            stepped.Clear();
        }
    }

    /// <summary>Shows a hidden overlay where it was, without taking focus from the game.</summary>
    public static void Reshow(Window window, PixelPoint place)
    {
        window.ShowActivated = false;
        window.Show();
        window.Position = place;
    }

    void OpenMap()
    {
        if (pets == null)
            OpenPets();
        if (pets?.FullMap != null)
            pets.OpenFullMap();
        else
            ShowDrawer(MapDrawer);
    }

    void OpenResourceMap()
    {
        if (resources == null)
            OpenResources();
        if (resources?.FullMap != null)
            resources.OpenFullMap();
        else
            ShowDrawer(ResourceMapDrawer);
    }

    void OpenCubeMap()
    {
        if (cubes == null)
            OpenCubes();
        if (cubes?.FullMap != null)
            cubes.OpenFullMap();
        else
            ShowDrawer(CubeMapDrawer);
    }

    void PopOutMap()
    {
        var tool = current ?? tools[0];
        string key = tool.Key;
        ShowTool(tool, 0);
        if (key == "cubes")
        {
            if (cubes == null)
                OpenCubes();
            cubes?.OpenFullMap();
        }
        else if (key == "resources")
        {
            if (resources == null)
                OpenResources();
            resources?.OpenFullMap();
        }
        else
        {
            if (pets == null)
                OpenPets();
            pets?.OpenFullMap();
        }
    }

    // ---------------------------------------------------------------- small parts

    Border Toggle(Func<bool> on, Action flip, string tip)
    {
        var knob = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Navy.Text, Margin = new Thickness(2) };
        var track = new Border { Width = 32, Height = 16, CornerRadius = new CornerRadius(8), Child = knob, VerticalAlignment = VerticalAlignment.Center };
        toggles.Add((track, knob, on));
        bool yes = on();
        track.Background = yes ? Navy.Accent : Navy.LineStrong;
        knob.HorizontalAlignment = yes ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        return Hot(track, () =>
        {
            flip();
            Refresh();
        }, tip);
    }

    static Border Button(string label, Action act, string? tip, bool primary = false) =>
        Hot(new Border { Padding = new Thickness(10, 4), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Background = primary ? Navy.Accent : Navy.Surface2, BorderBrush = primary ? Navy.Accent : Navy.LineStrong, VerticalAlignment = VerticalAlignment.Center, Child = Text(label, 12, primary ? Navy.Background : Navy.Text2, primary ? FontWeight.SemiBold : FontWeight.Normal) }, act, tip);

    static Border Pill(Ellipse? dot, TextBlock text)
    {
        text.VerticalAlignment = VerticalAlignment.Center;
        var inside = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (dot != null)
        {
            dot.VerticalAlignment = VerticalAlignment.Center;
            inside.Children.Add(dot);
        }

        inside.Children.Add(text);
        return new Border { Padding = new Thickness(9, 3), CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), BorderBrush = Navy.LineStrong, Background = Navy.Surface1, VerticalAlignment = VerticalAlignment.Center, Child = inside };
    }

    static Border WindowButton(string glyph, Action act, string tip) =>
        Hot(new Border { Width = 34, Height = 30, CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Child = new TextBlock { Text = glyph, FontSize = 13, Foreground = Navy.Text2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }, act, tip);

    static TextBlock Text(string text, double size, IBrush brush, FontWeight? weight = null) => new() { Text = text, FontSize = size, Foreground = brush, FontWeight = weight ?? FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };

    static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";

    static readonly Dictionary<string, Bitmap?> pictures = new();

    static Bitmap? Picture(string name)
    {
        if (!pictures.TryGetValue(name, out var bitmap))
        {
            string path = Path.Combine(AppContext.BaseDirectory, "hub", name);
            pictures[name] = bitmap = File.Exists(path) ? new Bitmap(path) : null;
        }

        return bitmap;
    }

    static Image Art(string name, double width, double height)
    {
        var image = new Image { Source = Picture(name), Width = width, Height = height, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        return image;
    }

    static T Hot<T>(T control, Action action, string? tip = null) where T : Control
    {
        control.Cursor = new Cursor(StandardCursorType.Hand);
        if (tip != null)
            ToolTip.SetTip(control, tip);
        control.PointerEntered += (_, _) => control.Opacity = 0.85;
        control.PointerExited += (_, _) => control.Opacity = 1;
        control.PointerPressed += (_, e) => e.Handled = true;
        // A switch or button inside a row acts alone, not as a click on the row too.
        control.PointerReleased += (_, e) =>
        {
            if (e.Handled || e.InitialPressMouseButton != MouseButton.Left || !control.IsPointerOver)
                return;
            e.Handled = true;
            action();
        };
        return control;
    }

    /// <summary>Asks once, at start, whether a newer version is out.</summary>
    public async Task CheckForUpdates()
    {
        if (saved.CheckUpdates && await UpdateCheck.Run(Version) is { } update)
            OfferUpdate(update);
    }

    /// <summary>Shows the prompt beside the version. A click updates the portable Windows exe
    /// and restarts it; otherwise, or if that fails, it opens the release page.</summary>
    public void OfferUpdate(AvailableUpdate update)
    {
        var label = new TextBlock { Text = $"Update to v{update.Version}", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(ToolTheme.Toolkit) };
        updatePrompt.Child = label;
        ToolTip.SetTip(updatePrompt, SelfUpdate.Possible ? "Download, install and restart" : "Open the download page");
        bool busy = false;
        updatePrompt.PointerReleased += async (_, _) =>
        {
            if (busy)
                return;
            if (!SelfUpdate.Possible)
            {
                Desktop.Open(update.Page, "xdg-open");
                return;
            }

            busy = true;
            try
            {
                await SelfUpdate.Install(update, percent => Dispatcher.UIThread.Post(() => label.Text = $"Downloading {percent} %"));
                Close();
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                label.Text = "Update failed";
                ToolTip.SetTip(updatePrompt, ex.Message);
                Desktop.Open(update.Page, "xdg-open");
            }
        };
        updatePrompt.IsVisible = true;
    }

    void LaunchGame() => Desktop.Open(SteamGame, "steam");

    void OpenData() => Desktop.Open(folder, "xdg-open");

    void Remember(Action<HubSettings> change)
    {
        if (closing)
            return;
        change(saved);
        try
        {
            saved.Save(folder);
        }
        catch (IOException)
        {
            // only a convenience for the next start
        }
    }

    public void OpenPets()
    {
        if (pets == null)
        {
            App.Tracker = pets = new TrackerWindow(app, maps);
            pets.Closed += (_, _) =>
            {
                App.Tracker = pets = null;
                Remember(s => s.Pets = false);
                Refresh();
            };
            pets.Show();
            Remember(s => s.Pets = true);
            Refresh();
        }

        pets.Activate();
    }

    public void OpenMeter()
    {
        if (meter == null)
        {
            meter = new MeterWindow(app);
            meter.Closed += (_, _) =>
            {
                meter = null;
                Remember(s => s.Meter = false);
                Refresh();
            };
            meter.Show();
            Remember(s => s.Meter = true);
            Refresh();
        }

        meter.Activate();
    }

    public void OpenCubes()
    {
        if (cubes == null)
        {
            cubes = new CubeWindow(spots);
            cubes.Closed += (_, _) =>
            {
                cubes = null;
                Remember(s => s.Cubes = false);
                Refresh();
            };
            cubes.Show();
            Remember(s => s.Cubes = true);
            Refresh();
        }

        cubes.Activate();
    }

    public void OpenPing()
    {
        if (ping == null)
        {
            ping = new PingWindow(app, saved.PingX, saved.PingY, (x, y) => Remember(s => (s.PingX, s.PingY) = (x, y)));
            ping.Closed += (_, _) =>
            {
                ping = null;
                Remember(s => s.Ping = false);
                Refresh();
            };
            ping.Show();
            KeepAbove();
            Remember(s => s.Ping = true);
            Refresh();
        }
    }

    public void OpenItemLog()
    {
        if (itemLog == null)
        {
            itemLog = new ItemLogWindow(app, saved.ItemLogLook, () => saved.Save(folder), saved.ItemLogX, saved.ItemLogY, (x, y) => Remember(s => (s.ItemLogX, s.ItemLogY) = (x, y)));
            itemLog.ShowSettings = () => Bring("itemlog");
            itemLog.Closed += (_, _) =>
            {
                itemLog = null;
                Remember(s => s.ItemLog = false);
                Refresh();
            };
            itemLog.Show();
            KeepAbove();
            Remember(s => s.ItemLog = true);
            Refresh();
        }
    }

    public void OpenSession()
    {
        if (gains == null)
        {
            gains = new SessionWindow(app, saved.SessionLook, () => saved.Save(folder), saved.SessionX, saved.SessionY, (x, y) => Remember(s => (s.SessionX, s.SessionY) = (x, y)));
            gains.ShowSettings = () => Bring("session");
            gains.Closed += (_, _) =>
            {
                gains = null;
                Remember(s => s.Session = false);
                Refresh();
            };
            gains.Show();
            KeepAbove();
            Remember(s => s.Session = true);
            Refresh();
        }
    }

    /// <summary>The bars' shortcut: opens them when off, else hides or shows them.</summary>
    public void ToggleBars()
    {
        if (bars == null)
            OpenBars();
        else
            bars.Toggle();
    }

    public void OpenBars()
    {
        if (bars == null)
        {
            bars = new BarsWindow(app, saved.Bars, () => saved.Save(folder));
            bars.ShowSettings = () => Bring("bars");
            bars.Closed += (_, _) =>
            {
                bars = null;
                Remember(s => s.Bars.Open = false);
                Refresh();
            };
            bars.Show();
            KeepAbove();
            Remember(s => s.Bars.Open = true);
            Refresh();
        }
    }

    /// <summary>What an alert says: where the cube is and how far from you, when that's known.</summary>
    public static string CubeText(string area, double? metres) => "Hidden Cube: " + area + (metres is double m ? $", {Math.Round(m).ToString("0", System.Globalization.CultureInfo.InvariantCulture)} m away" : "");

    void CubeAlert(CubeSighting cube)
    {
        lastCube = DateTimeOffset.UtcNow;
        if (!spots.Preferences.Alerts)
            return;
        double? metres = null;
        if (position.Region == cube.Region && position.Player is { } player)
        {
            var (x, y) = position.Map.Normalize(cube.X, cube.Y);
            metres = Math.Sqrt(Math.Pow(x - player.X, 2) + Math.Pow(y - player.Y, 2)) * position.Map.Metres;
        }

        Alert(CubeText(cube.Area, metres), ToolTheme.Cubes);
    }

    /// <summary>Announces the chosen timed events as their alert time comes round.</summary>
    void EventCheck()
    {
        var now = DateTimeOffset.UtcNow;
        var due = GameEvents.Due(eventsChecked, now, saved.Events);
        eventsChecked = now;
        foreach (var (_, names) in due)
            Alert(GameEvents.Text(names, saved.Events.Minutes), ToolTheme.Toolkit);
    }

    /// <summary>A sound, a note at the top of the screen and a banner on the phone map.</summary>
    void Alert(string text, Color accent)
    {
        alert?.Close();
        alert = new AlertWindow(text, accent);
        var shown = alert;
        shown.Closed += (_, _) =>
        {
            if (alert == shown)
                alert = null;
        };
        shown.Show();
        phone.Alert(text);
        Desktop.Chime();
    }

    // On KDE a fullscreen game covers "always on top" windows without the window rules. Asked
    // for once, so someone who removes them isn't overruled at every start.
    void KeepAbove()
    {
        if (!KdeWindowRules.Available || saved.WindowRules && saved.WindowRulesVersion >= KdeWindowRules.Version)
            return;
        _ = Task.Run(() =>
        {
            try
            {
                if (!KdeWindowRules.Installed())
                    KdeWindowRules.Install();
                Dispatcher.UIThread.Post(() => Remember(s => (s.WindowRules, s.WindowRulesVersion) = (true, KdeWindowRules.Version)));
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Toolkit Settings has the button, and says what went wrong
            }
        });
    }

    public void OpenResources()
    {
        if (resources == null)
        {
            resources = new ResourceWindow(nodes);
            resources.Closed += (_, _) =>
            {
                resources = null;
                Remember(s => s.Resources = false);
                Refresh();
            };
            resources.Show();
            Remember(s => s.Resources = true);
            Refresh();
        }

        resources.Activate();
    }
}
