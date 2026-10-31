using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ArnoA2.App.Dps;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    public bool CheckUpdates { get; set; } = true;

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

/// <summary>The toolkit's main window: every tool in one place, and the capture status they
/// all share. Laid out on a fixed 1536×1024 canvas, the size its art was cut at, which scales
/// with the window. Closing it closes the toolkit.</summary>
public sealed class HubWindow : Window
{
    const double CanvasWidth = 1536, CanvasHeight = 1024;
    const string SteamGame = "steam://rungameid/3393110";
    readonly Border updatePrompt = new() { IsVisible = false, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
    const double NavWidth = 265, DrawerWidth = 1027, DrawerScale = 1.3, MapScale = 1.15;
    public const string PetsDrawer = "pets", FinderDrawer = "finder", SessionDrawer = "session", MapDrawer = "map", MeterDrawer = "meter", LogDrawer = "log", ResourceDrawer = "resources", ResourceMapDrawer = "resource-map", CubeDrawer = "cubes", CubeMapDrawer = "cube-map", ToolkitDrawer = "toolkit", AboutDrawer = "about";

    static readonly IBrush Ink = Brush.Parse("#05070D");
    static readonly IBrush Bright = Brush.Parse("#E6EDF8");
    static readonly IBrush Soft = Brush.Parse("#AEBBCF");
    static readonly IBrush Rim = Brush.Parse("#1F3350");
    static readonly IBrush PanelFill = Brush.Parse("#C00A111D");
    static readonly IBrush Green = Brush.Parse("#35D986");
    static readonly IBrush Amber = Brush.Parse("#F2B632");
    static readonly IBrush Off = Brush.Parse("#5E6B7E");

    readonly AppState app;
    readonly PetLayer maps;
    readonly ResourceLayer nodes;
    readonly CubeLayer spots;
    readonly string folder;
    readonly HubSettings saved;
    readonly Stopwatch session = new();
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Ellipse dot = new() { Width = 13, Height = 13 }, navDot = new() { Width = 12, Height = 12 };
    readonly TextBlock detected = Text("", 15, Green), navDetected = Text("", 14, Bright);
    readonly TextBlock link = Row(""), character = Row(""), uptime = Row(""), status = Text("", 12.5, Soft);
    readonly Image petsToggle = new(), meterToggle = new(), resourceToggle = new(), cubeToggle = new(), loginToggle = new(), restoreToggle = new();
    readonly Border launch;
    readonly List<(Func<bool> Running, Control Ready, Control Launched)> cards = new();
    Border popOut = null!;
    readonly Border drawer, drawerBody = new() { Margin = new Thickness(30, 0, 30, 24) };
    readonly TranslateTransform drawerSlide = new() { X = -DrawerWidth };
    readonly Image drawerIcon = new() { Width = 44, Height = 44 };
    readonly TextBlock drawerTitle = new() { FontSize = 27, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly List<(string? Key, Border Item, Color Accent)> navigation = new();
    TrackerWindow? pets;
    MeterWindow? meter;
    ResourceWindow? resources;
    CubeWindow? cubes;
    string? opened;
    bool closing;

    public HubWindow(AppState app, MapHub position)
    {
        this.app = app;
        maps = new PetLayer(app, position);
        nodes = new ResourceLayer(position, app.Store.Folder);
        spots = new CubeLayer(position, app.Store.Folder);
        folder = Path.GetDirectoryName(app.Store.Folder)!;
        saved = HubSettings.Load(folder);
        Title = "Arno's A2 Toolkit";
        Icon = ToolTheme.AppIcon;
        Width = 1152;
        Height = 768;
        MinWidth = 960;
        MinHeight = 640;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ink;

        var canvas = new Canvas { Width = CanvasWidth, Height = CanvasHeight, Background = Ink, ClipToBounds = true };
        Place(canvas, Faded(Art("hero.png", 1026, 268), vertical: true), 266, 0);
        Place(canvas, Card(478, 332, "#2E8CFF", "cpet.png", "Pet Tracker", "Track pet ranks, souls and\nwhere to farm them.", "pets.png", 240, 262,
            new[] { ("shield", "Pet Ranks"), ("chest", "Souls Needed"), ("pin", "Farming Spots"), ("map", "World Map"), ("star", "Mini-Map Overlay") },
            "blue", OpenPets, () => ShowDrawer(PetsDrawer), () => pets != null,
            new (string, Action)[] { ("Settings", () => ShowDrawer(PetsDrawer)), ("Find a Pet", () => ShowDrawer(FinderDrawer)), ("Session", () => ShowDrawer(SessionDrawer)), ("Mini-map", () => pets?.ToggleMiniMap()), ("Map", OpenMap) }), 285, 270);
        Place(canvas, Card(486, 332, "#FF4048", "cdps.png", "DPS Meter", "Real-time damage meter with\ndetailed statistics.", "meter.png", 240, 262,
            new[] { ("bars", "Live DPS"), ("burst", "Class & Combat Power"), ("pie", "Damage %, Total Damage"), ("swords", "Skill Breakdown"), ("boss", "Boss Fight History") },
            "red", OpenMeter, () => ShowDrawer(MeterDrawer), () => meter != null,
            new (string, Action)[] { ("Settings", () => ShowDrawer(MeterDrawer)), ("Fight Log", () => ShowDrawer(LogDrawer)) }), 784, 270);
        Place(canvas, Card(478, 336, "#22C878", "cleaf.png", "Resource Tracker", "See where gathering resources\nand materials spawn.", "resource.png", 164, 263,
            new[] { ("hammer", "Gathering Nodes"), ("essence", "Odyle"), ("pouch", "Cooking Materials") },
            "green", OpenResources, () => ShowDrawer(ResourceDrawer), () => resources != null,
            new (string, Action)[] { ("Settings", () => ShowDrawer(ResourceDrawer)), ("Mini-map", () => resources?.ToggleMiniMap()), ("Map", OpenResourceMap) }), 285, 623);
        Place(canvas, Card(486, 336, "#9A4DFF", "ccube.png", "Hidden Cube Tracker", "See where Hidden Cubes can\nappear in Verteron and Altgard.", "cube.png", 280, 263,
            new[] { ("pin", "Spawn Locations"), ("box", "Checked Spots"), ("map", "Interactive Map") },
            "purple", OpenCubes, () => ShowDrawer(CubeDrawer), () => cubes != null,
            new (string, Action)[] { ("Settings", () => ShowDrawer(CubeDrawer)), ("Mini-map", () => cubes?.ToggleMiniMap()), ("Map", OpenCubeMap) }), 784, 623);

        drawer = Drawer();
        Place(canvas, drawer, NavWidth, 0);
        Navigation(canvas);

        launch = Hot(new Border { Width = 202, Height = 50, Background = Brush.Parse("#0C1626"), BorderBrush = Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = Centered(Text("Launch AION 2", 15.5, Bright)) }, LaunchGame, "Starts the game through Steam");
        GameStatus(canvas);
        QuickSettings(canvas);
        ToolsOverlay(canvas);
        ToolkitPanel(canvas);
        WindowButtons(canvas);

        var grip = new Border { Width = 18, Height = 18, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.BottomRightCorner), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        grip.PointerPressed += (_, e) =>
        {
            BeginResizeDrag(WindowEdge.SouthEast, e);
            e.Handled = true;
        };
        // The drawer's own controls drag; only the launcher's empty space moves the window.
        drawerBody.PointerPressed += (_, e) => e.Handled = true;
        var root = new Panel { Background = Ink, Children = { new Viewbox { Stretch = Stretch.Uniform, Child = canvas }, grip } };
        root.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        Content = root;

        app.Changed += Refresh;
        clock.Tick += (_, _) => Refresh();
        clock.Start();
        Refresh();
        Opened += (_, _) =>
        {
            if (saved.Restore && saved.Pets)
                OpenPets();
            if (saved.Restore && saved.Meter)
                OpenMeter();
            if (saved.Restore && saved.Resources)
                OpenResources();
            if (saved.Restore && saved.Cubes)
                OpenCubes();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && opened != null)
                CloseDrawer();
        };
        Closing += (_, _) => closing = true;
        Closed += (_, _) =>
        {
            app.Changed -= Refresh;
            clock.Stop();
            pets?.Close();
            meter?.Close();
            resources?.Close();
            cubes?.Close();
        };
    }

    void Refresh()
    {
        bool found = app.CaptureStatus.StartsWith("Capturing", StringComparison.Ordinal);
        if (found && !session.IsRunning)
            session.Restart();
        else if (!found)
            session.Reset();

        dot.Fill = navDot.Fill = found ? Green : Amber;
        detected.Text = navDetected.Text = found ? "AION 2 Detected" : "Not Detected";
        detected.Foreground = found ? Green : Amber;
        link.Text = !found ? "Waiting" : app.Meter.Connection == ArnoA2.Meter.Connection.Live ? "Live" : "Finding you";
        character.Text = app.Meter.Character ?? app.Meter.Settings.Character ?? "—";
        uptime.Text = found ? session.Elapsed.ToString(@"hh\:mm\:ss") : "—";
        status.Text = app.CaptureStatus;
        foreach (var (running, ready, launched) in cards)
            (ready.IsVisible, launched.IsVisible) = (!running(), running());
        launch.IsHitTestVisible = !found;
        launch.Opacity = found ? 0.45 : 1;
        Show(petsToggle, pets != null);
        Show(meterToggle, meter != null);
        Show(resourceToggle, resources != null);
        Show(cubeToggle, cubes != null);
        Show(loginToggle, Desktop.StartsAtLogin);
        Show(restoreToggle, saved.Restore);
    }

    void Navigation(Canvas canvas)
    {
        Place(canvas, new Border { Width = NavWidth, Height = CanvasHeight, Background = Ink }, 0, 0);
        Place(canvas, Faded(Art("nav-bottom.png", 258, 210), vertical: false, top: true), 4, 750);
        Place(canvas, Art("logo.png", 240, 240), 12, 4);
        Place(canvas, new Border { Width = 1, Height = CanvasHeight, Background = Rim }, 264, 0);

        Place(canvas, Nav("i-home.png", "Home", null, ToolTheme.Pets), 10, 250);
        Place(canvas, new TextBlock { Text = "SETTINGS", FontSize = 13, FontWeight = FontWeight.SemiBold, LetterSpacing = 2.5, Foreground = Off }, 30, 324);
        Place(canvas, Nav("i-pet.png", "Pet Tracker", PetsDrawer, ToolTheme.Pets), 10, 350);
        Place(canvas, Nav("i-dps.png", "DPS Meter", MeterDrawer, ToolTheme.Meter), 10, 400);
        Place(canvas, Nav("i-leaf.png", "Resource Tracker", ResourceDrawer, ToolTheme.Resources), 10, 450);
        Place(canvas, Nav("i-cube.png", "Hidden Cube Tracker", CubeDrawer, ToolTheme.Cubes), 10, 500);
        Place(canvas, Nav("i-gear.png", "Toolkit", ToolkitDrawer, ToolTheme.Toolkit), 10, 550);
        Place(canvas, new Border { Width = 216, Height = 1, Background = Rim }, 24, 610);
        Place(canvas, Nav("i-info.png", "About", AboutDrawer, ToolTheme.Toolkit), 10, 624);
        Mark();

        Place(canvas, Text("v" + Version, 14, Off), 32, 958);
        Place(canvas, updatePrompt, 84, 958);
        Place(canvas, navDot, 34, 985);
        Place(canvas, navDetected, 54, 981);
    }

    Border Nav(string icon, string label, string? key, Color accent)
    {
        var item = Hot(NavItem(icon, label), () =>
        {
            if (key == null || key == opened)
                CloseDrawer();
            else
                ShowDrawer(key);
        });
        navigation.Add((key, item, accent));
        return item;
    }

    // Lights the menu entry whose drawer is out; Home when none is.
    void Mark()
    {
        foreach (var (key, item, accent) in navigation)
        {
            bool on = key == opened || key == PetsDrawer && opened is FinderDrawer or SessionDrawer or MapDrawer || key == MeterDrawer && opened == LogDrawer || key == ResourceDrawer && opened == ResourceMapDrawer || key == CubeDrawer && opened == CubeMapDrawer;
            item.Background = on ? new SolidColorBrush(accent, 0.3) : Brushes.Transparent;
            item.BorderBrush = on ? new SolidColorBrush(accent) : Brushes.Transparent;
            item.BoxShadow = on ? BoxShadows.Parse($"0 0 12 0 #A0{accent.R:X2}{accent.G:X2}{accent.B:X2}") : default;
        }
    }

    static Border NavItem(string icon, string label) => new() { Width = 242, Height = 46, Background = Brushes.Transparent, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(5), Child = NavRow(icon, label) };

    Border Drawer()
    {
        var close = Hot(new Border { Width = 40, Height = 40, Background = Brushes.Transparent, Child = Centered(new TextBlock { Text = "✕", FontSize = 20, Foreground = Soft }) }, CloseDrawer, "Close");
        var top = new DockPanel { Margin = new Thickness(30, 22, 24, 12) };
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        popOut = Hot(new Border { Height = 40, Padding = new Thickness(16, 0), Margin = new Thickness(0, 0, 10, 0), Background = Brush.Parse("#800A111D"), BorderBrush = new SolidColorBrush(ToolTheme.Pets), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = Centered(Text("Open in Window", 15, Bright)) }, PopOutMap, "Move the map to a window of its own, for a second monitor");
        DockPanel.SetDock(popOut, Dock.Right);
        top.Children.Add(popOut);
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { drawerIcon, drawerTitle } };
        top.Children.Add(title);
        DockPanel.SetDock(top, Dock.Top);
        drawerSlide.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new CubicEaseOut() } };
        return new Border
        {
            Width = DrawerWidth,
            Height = CanvasHeight,
            RenderTransform = drawerSlide,
            IsHitTestVisible = false,
            BoxShadow = BoxShadows.Parse("10 0 26 0 #D0000000"),
            Child = new DockPanel { Children = { top, drawerBody } }
        };
    }

    /// <summary>Slides a page out from under the menu, over the launcher.</summary>
    public void ShowDrawer(string key)
    {
        var (title, icon, accent, page) = key switch
        {
            PetsDrawer => ("Pet Tracker Settings", "cpet.png", ToolTheme.Pets, (Func<Control>)(() => new PetSettingsPanel(app, maps))),
            FinderDrawer => ("Find a Pet", "cpet.png", ToolTheme.Pets, () => new FinderPanel(app, maps, OpenMap)),
            ResourceDrawer => ("Resource Tracker Settings", "cleaf.png", ToolTheme.Resources, () => new ResourceSettingsPanel(nodes, () => resources)),
            ResourceMapDrawer => ("Map", "cleaf.png", ToolTheme.Resources, () => new MapPanel(nodes)),
            CubeDrawer => ("Hidden Cube Tracker Settings", "ccube.png", ToolTheme.Cubes, () => new CubeSettingsPanel(spots, () => cubes)),
            CubeMapDrawer => ("Map", "ccube.png", ToolTheme.Cubes, () => new MapPanel(spots)),
            MapDrawer => ("Map", "cpet.png", ToolTheme.Pets, () => new MapPanel(maps)),
            SessionDrawer => ("This Session", "cpet.png", ToolTheme.Pets, () => new SessionPanel(app)),
            MeterDrawer => ("DPS Meter Settings", "cdps.png", ToolTheme.Meter, () => new MeterSettingsPanel(app, () => meter?.ApplyOpacity())),
            LogDrawer => ("Fight Log", "cdps.png", ToolTheme.Meter, () => new HistoryPanel(app)),
            ToolkitDrawer => ("Toolkit Settings", "i-gear.png", ToolTheme.Toolkit, () => new ToolkitSettingsPanel(app)),
            _ => ("About", "i-info.png", ToolTheme.Toolkit, () => new AboutPanel(app, Version, saved.CheckUpdates, on => Remember(s => s.CheckUpdates = on)))
        };
        opened = key;
        drawerIcon.Source = Picture(icon);
        RenderOptions.SetBitmapInterpolationMode(drawerIcon, BitmapInterpolationMode.HighQuality);
        drawerTitle.Text = title;
        drawerTitle.Foreground = new SolidColorBrush(accent);
        ToolTheme.Tint(drawer, accent);
        drawer.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromRgb((byte)(accent.R / 7 + 6), (byte)(accent.G / 7 + 7), (byte)(accent.B / 7 + 12)), 0), new GradientStop(Color.Parse("#05070D"), 1) }
        };
        bool map = key is MapDrawer or ResourceMapDrawer or CubeMapDrawer;
        popOut.IsVisible = map;
        popOut.BorderBrush = new SolidColorBrush(accent);
        drawerBody.Margin = map ? default : new Thickness(30, 0, 30, 24);
        double scale = map ? MapScale : DrawerScale;
        drawerBody.Child = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = Scrolled(page()) };
        drawer.IsHitTestVisible = true;
        drawerSlide.X = 0;
        Mark();
    }

    // The fight log and the finder scroll their own lists.
    static Control Scrolled(Control panel) => panel is HistoryPanel or FinderPanel or MapPanel ? panel : new ScrollViewer { Content = panel };

    void CloseDrawer()
    {
        opened = null;
        drawer.IsHitTestVisible = false;
        drawerSlide.X = -DrawerWidth;
        Mark();
        // Off screen its panels would keep following the game for nothing.
        DispatcherTimer.RunOnce(() =>
        {
            if (opened == null)
                drawerBody.Child = null;
        }, TimeSpan.FromMilliseconds(260));
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
        bool green = opened == ResourceMapDrawer, purple = opened == CubeMapDrawer;
        CloseDrawer();
        if (purple)
        {
            if (cubes == null)
                OpenCubes();
            cubes?.OpenFullMap();
            return;
        }

        if (green)
        {
            if (resources == null)
                OpenResources();
            resources?.OpenFullMap();
            return;
        }

        if (pets == null)
            OpenPets();
        pets?.OpenFullMap();
    }

    static Control NavRow(string icon, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        row.Children.Add(Art(icon, 30, 30));
        row.Children.Add(new TextBlock { Text = label, FontSize = 17, Foreground = Bright, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>A tool's card.</summary>
    Control Card(double width, double height, string accent, string icon, string name, string about, string art, double artWidth, double artHeight, (string Icon, string Label)[] rows, string color, Action open, Action configure, Func<bool> running, (string Label, Action Act)[] tools)
    {
        var inside = new Canvas { Width = width, Height = height };
        Place(inside, Faded(Art(art, artWidth, artHeight), vertical: false), width - artWidth - 2, 6);
        Place(inside, Art(icon, 48, 48), 20, 16);
        Place(inside, new TextBlock { Text = name, FontSize = 27, FontWeight = FontWeight.SemiBold, Foreground = Bright }, 84, 16);
        Place(inside, new TextBlock { Text = about, FontSize = 15, LineHeight = 20, Foreground = Soft }, 85, 58);
        for (int i = 0; i < rows.Length; i++)
        {
            Place(inside, Art("r-" + rows[i].Icon + ".png", 28, 28), 22, 108 + i * 31);
            Place(inside, Text(rows[i].Label, 15.5, Soft), 68, 112 + i * 31);
        }

        var ready = new Canvas { Width = width, Height = 66 };
        Place(ready, Hot(Art("set-" + color + ".png", 142, 60), configure, "Settings"), 16, 0);
        Place(ready, Hot(Art("launch-" + color + ".png", 274, 60), open, "Open " + name), 196, 0);
        Place(inside, ready, 0, height - 66);

        // Once launched, the same strip holds what belongs to the tool.
        var launched = new Grid { Width = width - 36, Height = 46, ColumnDefinitions = new ColumnDefinitions(string.Join(",", tools.Select(_ => "*"))) };
        for (int i = 0; i < tools.Length; i++)
        {
            var tool = Hot(new Border { Margin = new Thickness(3, 0), Background = Brush.Parse("#800A111D"), BorderBrush = Brush.Parse(accent), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = Centered(Text(tools[i].Label, 14.5, Bright)) }, tools[i].Act);
            Grid.SetColumn(tool, i);
            launched.Children.Add(tool);
        }

        Place(inside, launched, 18, height - 60);
        cards.Add((running, ready, launched));

        return new Border
        {
            Width = width,
            Height = height,
            ClipToBounds = false,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brush.Parse(accent),
            BoxShadow = BoxShadows.Parse($"0 0 12 0 #60{accent[1..]}"),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#0A1220"), 0), new GradientStop(Color.Parse("#070B14"), 1) }
            },
            Child = new Border { ClipToBounds = true, CornerRadius = new CornerRadius(6), Child = inside }
        };
    }

    void GameStatus(Canvas canvas)
    {
        var inside = Panel(canvas, 44, 274, "p-game.png", "Game Status");
        Place(inside, dot, 28, 62);
        Place(inside, detected, 48, 58);
        Details(inside, 96, "Connection", link);
        Details(inside, 126, "Character", character);
        Details(inside, 156, "Session", uptime);
        Place(inside, launch, 17, 206);
    }

    static void Details(Canvas inside, double y, string label, TextBlock value)
    {
        Place(inside, Text(label, 14, Off), 20, y);
        Place(inside, value, 122, y);
    }

    void QuickSettings(Canvas canvas)
    {
        var inside = Panel(canvas, 330, 160, "p-quick.png", "Quick Settings");
        Switch(inside, 62, "Start at login", loginToggle, () =>
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
        Switch(inside, 106, "Reopen tools at start", restoreToggle, () => Remember(s => s.Restore = !s.Restore));
    }

    void ToolsOverlay(Canvas canvas)
    {
        var inside = Panel(canvas, 502, 270, "p-tools.png", "Tools Overlay");
        Switch(inside, 62, "Pet Tracker", petsToggle, () =>
        {
            if (pets == null)
                OpenPets();
            else
                pets.Close();
        }, true);
        Switch(inside, 110, "DPS Meter", meterToggle, () =>
        {
            if (meter == null)
                OpenMeter();
            else
                meter.Close();
        }, true);
        Switch(inside, 158, "Resource Tracker", resourceToggle, () =>
        {
            if (resources == null)
                OpenResources();
            else
                resources.Close();
        }, true);
        Switch(inside, 206, "Hidden Cubes", cubeToggle, () =>
        {
            if (cubes == null)
                OpenCubes();
            else
                cubes.Close();
        }, true);
    }

    void ToolkitPanel(Canvas canvas)
    {
        var inside = Panel(canvas, 784, 175, "i-gear.png", "Toolkit");
        status.Width = 200;
        status.TextWrapping = TextWrapping.Wrap;
        Place(inside, status, 20, 56);
        Place(inside, Hot(Tile("Settings"), () => ShowDrawer(ToolkitDrawer), "Capture permission and window rules"), 17, 106);
        Place(inside, Hot(Tile("Data"), OpenData, "Open the folder your data is kept in"), 123, 106);
    }

    static Border Tile(string label) => new() { Width = 96, Height = 50, Background = Brush.Parse("#0C1626"), BorderBrush = Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = Centered(Text(label, 15, Bright)) };

    Canvas Panel(Canvas canvas, double y, double height, string icon, string title)
    {
        const double Width = 236;
        var inside = new Canvas { Width = Width, Height = height };
        Place(inside, Art(icon, 28, 28), 18, 15);
        Place(inside, new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#BFE0FF") }, 56, 18);
        Place(canvas, new Border { Width = Width, Height = height, Background = PanelFill, BorderBrush = Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = inside }, 1295, y);
        return inside;
    }

    void Switch(Canvas inside, double y, string label, Image toggle, Action flip, bool eye = false)
    {
        double x = 20;
        if (eye)
        {
            Place(inside, Art("p-eye.png", 22, 16), 18, y + 4);
            x = 48;
        }

        Place(inside, Text(label, 14, Soft), x, y + 3);
        toggle.Width = 46;
        toggle.Height = 24;
        RenderOptions.SetBitmapInterpolationMode(toggle, BitmapInterpolationMode.HighQuality);
        Place(inside, Hot(new Border { Background = Brushes.Transparent, Child = toggle }, () =>
        {
            flip();
            Refresh();
        }), 172, y);
    }

    static void Show(Image toggle, bool on) => toggle.Source = Picture(on ? "on.png" : "off.png");

    void WindowButtons(Canvas canvas)
    {
        var close = Art("w-close.png", 34, 32);
        var hit = new Border { Background = Brushes.Transparent, Child = close };
        hit.PointerEntered += (_, _) => close.Source = Picture("w-close-hot.png");
        hit.PointerExited += (_, _) => close.Source = Picture("w-close.png");
        Place(canvas, Hot(Art("w-min.png", 34, 32), () => WindowState = WindowState.Minimized, "Minimise"), 1414, 6);
        Place(canvas, Hot(Art("w-max.png", 34, 32), () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, "Maximise"), 1454, 6);
        Place(canvas, Hot(hit, Close, "Close the toolkit"), 1494, 6);
    }

    static void Place(Canvas canvas, Control control, double x, double y)
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        canvas.Children.Add(control);
    }

    static TextBlock Text(string text, double size, IBrush brush) => new() { Text = text, FontSize = size, Foreground = brush };

    static TextBlock Row(string text) => Text(text, 14, Bright);

    static Control Centered(Control child)
    {
        child.HorizontalAlignment = HorizontalAlignment.Center;
        child.VerticalAlignment = VerticalAlignment.Center;
        return child;
    }

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

    /// <summary>Melts art's edges into what's behind it: bottom and left, bottom alone, or top and bottom.</summary>
    static Control Faded(Control art, bool vertical, bool top = false)
    {
        var bottom = new RelativePoint(0, 1, RelativeUnit.Relative);
        var origin = new RelativePoint(0, 0, RelativeUnit.Relative);
        art.OpacityMask = top || vertical
            ? new LinearGradientBrush
            {
                StartPoint = top ? origin : bottom,
                EndPoint = top ? bottom : origin,
                GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.3), new GradientStop(Colors.Black, top ? 0.7 : 1), new GradientStop(top ? Colors.Transparent : Colors.Black, 1) }
            }
            : new LinearGradientBrush
            {
                StartPoint = origin,
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.4) }
            };
        if (top || vertical)
            return art;
        return new Border
        {
            Child = art,
            OpacityMask = new LinearGradientBrush
            {
                StartPoint = bottom,
                EndPoint = origin,
                GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.25) }
            }
        };
    }

    static T Hot<T>(T control, Action action, string? tip = null) where T : Control
    {
        control.Cursor = new Cursor(StandardCursorType.Hand);
        if (tip != null)
            ToolTip.SetTip(control, tip);
        control.PointerEntered += (_, _) => control.Opacity = 0.82;
        control.PointerExited += (_, _) => control.Opacity = 1;
        control.PointerPressed += (_, e) => e.Handled = true;
        control.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && control.IsPointerOver)
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
