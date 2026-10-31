using System.Globalization;
using ArnoA2.Tracker;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ArnoA2.App;

/// <summary>
/// The always-on-top tracker, drawn like the meter: merchant suggestions, then pinned and
/// recent (or nearby) pets with their souls, a status line and the session so far.
/// </summary>
public sealed class TrackerWindow : Window
{
    const string Columns = "3,22,2*,58,36,46,*";
    readonly AppState app;
    readonly MapHub maps;
    readonly StackPanel rows = new();
    readonly TextBlock status = new() { FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 3, 2, 0) };
    readonly TextBlock session = new() { FontSize = 9.5, Foreground = Ui.Muted, Margin = new Thickness(6, 3, 2, 0) };
    readonly Border toast = new() { Background = Brush.Parse("#332E8CFF"), BorderBrush = Ui.Accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 3), Margin = new Thickness(0, 0, 0, 3), IsVisible = false };
    readonly Border recent, nearby, hideMaxed, backdrop = new() { BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
    readonly Image art = new() { Stretch = Stretch.UniformToFill };
    readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    MiniMapWindow? miniMap;
    FullMapWindow? fullMap;
    string rendered = "";

    public TrackerWindow(AppState app, MapHub maps)
    {
        this.app = app;
        this.maps = maps;
        Title = KdeWindowRules.TrackerTitle;
        var saved = app.Engine.Settings;
        Width = saved.TrackerWidth ?? 380;
        Height = saved.TrackerHeight ?? 210;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinWidth = 320;
        MinHeight = 110;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        // The window itself is see-through; the backdrop below carries the chosen opacity.
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ToolTheme.Tint(this, ToolTheme.Pets);

        recent = Ui.Flat("Recent", () => Mode("Recent"), "Pets you just earned souls for");
        nearby = Ui.Flat("Nearby", () => Mode("Nearby"), "Pets whose creatures are around you");
        hideMaxed = Ui.Flat("Hide maxed", () =>
        {
            app.Engine.ChangeSettings(s => s.HideMaxed = !s.HideMaxed);
            Refresh();
        }, "Leave out pets at MAX");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Children = { recent, nearby, hideMaxed, Ui.Flat("✕", Close, "Close") } };

        // No window frame: the header moves the window, the corner resizes it.
        var header = new DockPanel { Margin = new Thickness(2, 0, 0, 3), Background = Brushes.Transparent };
        header.PointerPressed += (_, e) =>
        {
            if (e.Source == header || e.Source is TextBlock { Parent: DockPanel })
                BeginMoveDrag(e);
        };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(new TextBlock { Text = "PETS", FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.5, Foreground = Ui.Bright, VerticalAlignment = VerticalAlignment.Center });

        var table = new DockPanel();
        var titles = Heading();
        DockPanel.SetDock(titles, Dock.Top);
        table.Children.Add(titles);
        table.Children.Add(new ScrollViewer { Content = rows });

        var layout = new DockPanel { Margin = new Thickness(5) };
        DockPanel.SetDock(header, Dock.Top);
        var grip = new TextBlock { Text = "◢", FontSize = 9, Foreground = Ui.Muted, Cursor = new Cursor(StandardCursorType.BottomRightCorner), Padding = new Thickness(4, 2, 0, 0) };
        grip.PointerPressed += (_, e) => BeginResizeDrag(WindowEdge.SouthEast, e);
        var footer = new DockPanel();
        DockPanel.SetDock(grip, Dock.Right);
        DockPanel.SetDock(session, Dock.Right);
        footer.Children.Add(grip);
        footer.Children.Add(session);
        footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(toast, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(footer);
        layout.Children.Add(toast);
        layout.Children.Add(new Border { Child = table, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true });

        string picture = Path.Combine(AppContext.BaseDirectory, "hub", "tracker-bg.png");
        art.Source = File.Exists(picture) ? new Bitmap(picture) : null;
        art.HorizontalAlignment = HorizontalAlignment.Center;
        RenderOptions.SetBitmapInterpolationMode(art, BitmapInterpolationMode.HighQuality);
        backdrop.ClipToBounds = true;
        backdrop.Child = new Panel { Children = { art, layout } };
        Content = backdrop;
        ApplyOpacity();

        Opened += (_, _) =>
        {
            Place();
            PositionChanged += (_, e) => app.Engine.ChangeSettings(s => (s.TrackerX, s.TrackerY) = (e.Point.X, e.Point.Y));
            SizeChanged += (_, e) => app.Engine.ChangeSettings(s => (s.TrackerWidth, s.TrackerHeight) = (e.NewSize.Width, e.NewSize.Height));
        };
        toastTimer.Tick += (_, _) =>
        {
            toast.IsVisible = false;
            toastTimer.Stop();
        };
        app.Changed += Refresh;
        app.Noticed += Notice;
        Closed += (_, _) =>
        {
            app.Changed -= Refresh;
            app.Noticed -= Notice;
            toastTimer.Stop();
            miniMap?.Close();
            fullMap?.Close();
        };
        Refresh();
    }

    // Where it was left, unless that screen is gone; at first, below the meter's corner.
    void Place()
    {
        var saved = app.Engine.Settings;
        if (saved.TrackerX is int x && saved.TrackerY is int y && Screens.ScreenFromPoint(new PixelPoint(x, y)) != null)
            Position = new PixelPoint(x, y);
        else if (Screens.Primary is { } main)
            Position = new PixelPoint(main.WorkingArea.X + 24, main.WorkingArea.Y + 380);
    }

    void Mode(string mode)
    {
        app.Engine.ChangeSettings(s => s.Mode = mode);
        Refresh();
    }

    /// <summary>Lower lets the game show through; the pet fades with the backdrop.</summary>
    public void ApplyOpacity()
    {
        double solid = app.Engine.Settings.TrackerOpacity / 100.0;
        backdrop.Background = new SolidColorBrush(Ui.Backdrop, solid);
        art.Opacity = solid * 0.6;
        art.IsVisible = app.Engine.Settings.TrackerArt;
    }

    public void OpenMiniMap()
    {
        if (miniMap == null)
        {
            miniMap = new MiniMapWindow(maps);
            miniMap.Closed += (_, _) => miniMap = null;
            miniMap.Show();
        }

        miniMap.Activate();
    }

    public void SetMiniMapShape(bool round) => miniMap?.ApplyShape(round);
    public MiniMapWindow? MiniMap => miniMap;
    public FullMapWindow? FullMap => fullMap;

    public void OpenFullMap()
    {
        if (fullMap == null)
        {
            fullMap = new FullMapWindow(maps);
            fullMap.Closed += (_, _) => fullMap = null;
            fullMap.Show();
        }

        fullMap.Activate();
    }

    void Notice(Notice notice)
    {
        string? text = notice switch
        {
            RankNotice r => $"{GameData.Instance.Pets[r.Pet].Name} reached {(r.Rank == 3 ? "MAX" : "rank " + r.Rank)}",
            GoalNotice g => $"Goal reached: {GameData.Instance.Pets[g.Pet].Name} rank {g.Rank}",
            GapNotice => "Some game data was lost; totals are last-known until your next login",
            _ => null
        };
        if (text == null)
            return;
        toast.Child = new TextBlock { Text = text, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Bright, FontWeight = FontWeight.SemiBold };
        toast.IsVisible = true;
        toastTimer.Stop();
        toastTimer.Start();
        maps.CollectionChanged();
    }

    void Refresh()
    {
        var engine = app.Engine;
        var settings = engine.Settings;
        var now = DateTimeOffset.UtcNow;
        Ui.Select(recent, settings.Mode == "Recent");
        Ui.Select(nearby, settings.Mode == "Nearby");
        Ui.Select(hideMaxed, settings.HideMaxed);

        (status.Text, status.Foreground) = (app.CaptureProblem, engine.Connection) switch
        {
            (true, _) => (app.CaptureStatus, Ui.Bright),
            (_, Connection.Waiting) => (app.CaptureStatus + " Start the tracker before logging in to sync your soul totals.", Ui.Muted),
            (_, Connection.Identifying) => ("Game found. Recognising your character… (using a skill speeds this up)", Ui.Muted),
            (_, Connection.Unsynced) => ($"{CharacterLabel()} · totals are last-known (≈). Log in with the tracker running to sync.", Ui.Accent),
            _ => ($"{CharacterLabel()} · live", Ui.Good)
        };
        ToolTip.SetTip(status, status.Text);

        var merchant = engine.MerchantList(now);
        var list = engine.Rows(now);
        string key = string.Join("|", merchant.Select(m => $"{m.Pet}:{m.Price}:{m.Bought}")) + "#" + string.Join("|", list.Select(r => $"{r.Pet}:{r.Progress.ProgressText}:{r.Progress.Rank}:{r.Pinned}:{r.Nearby}:{r.Creature}"));
        session.Text = engine.Session.StartedUtc is { } started
            ? $"Session: {engine.Session.TotalSouls} souls · {engine.Session.Kills} kills · {(int)(now - started).TotalMinutes} min{(engine.Session.Incomplete ? " · incomplete" : "")}"
            : "";
        if (key == rendered)
            return;
        rendered = key;
        rows.Children.Clear();
        foreach (var offer in merchant)
            rows.Children.Add(Row(engine.Collection.Pets[offer.Pet], offer.Bought ? "Black Cloud Merchant · bought, souls received" : $"Black Cloud Merchant · {offer.Price:N0} Kina", false));

        foreach (var row in list)
            rows.Children.Add(Row(row.Progress, row.Nearby ? row.Creature : row.Creature.Length > 0 ? "Killed " + row.Creature : null, row.Pinned));

        if (list.Count == 0 && merchant.Count == 0)
            rows.Children.Add(new TextBlock
            {
                Text = settings.Mode == "Nearby" ? "No pet creatures in view." : "Pets you earn souls for appear here.",
                FontSize = 10.5,
                Foreground = Ui.Muted,
                Margin = new Thickness(6, 8),
                TextWrapping = TextWrapping.Wrap
            });
    }

    static Grid Heading()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 18 };
        foreach (var (text, column, right) in new[] { ("PET", 2, false), ("TYPE", 3, false), ("RANK", 4, true), ("SOULS", 5, true) })
            grid.Children.Add(Column(new TextBlock { Text = text, FontSize = 9, LetterSpacing = 0.5, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(0, 0, 4, 0) }, column));
        return grid;
    }

    static Control Row(PetProgress progress, string? note, bool pinned)
    {
        var pet = GameData.Instance.Pets[progress.Pet];
        var family = MapCanvas.FamilyBrush(pet.Id);
        var color = family is ISolidColorBrush solid ? solid.Color : Colors.White;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 22, Background = new SolidColorBrush(color, pinned ? 0.16 : 0.07) };
        grid.Children.Add(Column(new Border { Background = pinned ? Ui.Accent : family }, 0));
        var portrait = Ui.Picture($"portraits/{pet.Id}.png", 20);
        RenderOptions.SetBitmapInterpolationMode(portrait, BitmapInterpolationMode.HighQuality);
        grid.Children.Add(Column(portrait, 1));
        grid.Children.Add(Column(new TextBlock { Text = (pinned ? "★ " : "") + pet.Name, FontSize = 11, FontWeight = pinned ? FontWeight.SemiBold : FontWeight.Normal, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }, 2));
        grid.Children.Add(Column(Type(pet, family), 3));
        grid.Children.Add(Column(Number(progress.Rank is int rank ? rank == 0 ? "–" : rank.ToString() : "?", 10.5, FontWeight.Normal, Ui.Muted), 4));
        grid.Children.Add(Column(Number(progress.ProgressText, 11, FontWeight.Bold, progress.Max ? Ui.Good : progress.RankUpReady ? Ui.Bright : family), 5));
        var fill = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Math.Max(progress.Fraction, 0.001).ToString(CultureInfo.InvariantCulture)}*,{Math.Max(1 - progress.Fraction, 0).ToString(CultureInfo.InvariantCulture)}*") };
        fill.Children.Add(new Border { Background = Dps.Ui.Bar(progress.Max ? ((ISolidColorBrush)Ui.Good).Color : color) });
        grid.Children.Add(Column(new Border { Background = Ui.Track, Height = 7, Margin = new Thickness(4, 0, 6, 0), Child = fill }, 6));
        string tip = pet.Name;
        if (progress.RankUpReady)
            tip += progress.Rank == 0 ? " · ready to unlock in game" : " · ready to rank up in game";
        else if (!progress.Max && progress.Balance == null && progress.Requirement != null)
            tip += " · soul total unknown, log in with the tracker running to sync";
        ToolTip.SetTip(grid, note != null ? tip + "\n" + note : tip);
        return new Border { Child = grid, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    static Control Type(Pet pet, IBrush family)
    {
        var type = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        if (pet.Family is Family.Unknown)
            return type;
        type.Children.Add(Ui.Picture($"crystals/{pet.Family.ToString().ToLowerInvariant()}.png", 12));
        type.Children.Add(new TextBlock { Text = pet.Family.ToString(), FontSize = 10, Foreground = family, VerticalAlignment = VerticalAlignment.Center });
        return type;
    }

    static Control Column(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    static TextBlock Number(string text, double size, FontWeight weight, IBrush brush) => new() { Text = text, FontSize = size, FontWeight = weight, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };

    string CharacterLabel() => app.Engine.Character is { Length: > 0 } name ? name + (app.Engine.Assumed ? " (recognised from traffic)" : "") : "Your character";
}
