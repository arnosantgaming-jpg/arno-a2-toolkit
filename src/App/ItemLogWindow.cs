using System.Globalization;
using ArnoA2.Meter;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArnoA2.App;

/// <summary>How the item log looks and what it lists, remembered between starts.</summary>
public sealed class ItemLogLook
{
    public static readonly int[] LineChoices = { 5, 10, 15, 20 };
    public List<string> HiddenGrades { get; set; } = new();
    public List<string> HiddenTypes { get; set; } = new();
    public int Lines { get; set; } = 10;
    public string Size { get; set; } = "Medium";
    public bool Times { get; set; } = true;

    /// <summary>Back to how it first looks.</summary>
    public void Reset() => (HiddenGrades, HiddenTypes, Lines, Size, Times) = (new(), new(), 10, "Medium", true);

    public bool Shows(ItemInfo item) => !HiddenGrades.Contains(item.Grade) && !HiddenTypes.Contains(item.Type);
}

/// <summary>Items you obtain, newest on top, each in its grade's colour: a log of its own, apart
/// from chat. Drag it anywhere; right-click to filter by grade and type or change how it looks.</summary>
public sealed class ItemLogWindow : Window
{
    static readonly IBrush Soft = Navy.OverlayText2;
    readonly AppState app;
    readonly ItemLogLook look;
    readonly Action save;
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TextBlock title = new() { Text = "OBTAINED", FontWeight = FontWeight.SemiBold, LetterSpacing = 1, Foreground = Brush.Parse("#45D6E0") };
    readonly TextBlock empty = new() { Foreground = Soft };
    readonly Grid lines = new() { RowSpacing = 2 };
    int shown = -1;

    public ItemLogWindow(AppState app, ItemLogLook look, Action save, int? x, int? y, Action<int, int> moved)
    {
        this.app = app;
        this.look = look;
        this.save = save;
        Title = KdeWindowRules.ItemLogTitle;
        Icon = ToolTheme.AppIcon;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;

        lines.ColumnDefinitions.AddRange(new[] { new ColumnDefinition(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) });
        var stack = new StackPanel { Spacing = 4, MinWidth = 180, Children = { title, lines, empty } };
        var face = new Border { Padding = new Thickness(11, 6), CornerRadius = new CornerRadius(7), Background = Navy.Overlay, BorderBrush = Navy.OverlayRim, BorderThickness = new Thickness(1), Child = stack, Cursor = new Cursor(StandardCursorType.SizeAll) };
        ToolTip.SetTip(face, "Items you obtained, named from " + ItemTable.Source + ". Drag to move, right-click for options");
        face.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(face).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        face.ContextMenu = new ContextMenu();
        face.ContextMenu.Opening += (_, _) => face.ContextMenu.ItemsSource = Options();
        Content = face;

        Opened += (_, _) =>
        {
            if (x is int left && y is int top && Screens.ScreenFromPoint(new PixelPoint(left, top)) != null)
                Position = new PixelPoint(left, top);
            else if (Screens.Primary is { } main)
                Position = new PixelPoint(main.WorkingArea.X + 24, main.WorkingArea.Y + 300);
            PositionChanged += (_, e) => moved(e.Point.X, e.Point.Y);
        };
        tick.Tick += (_, _) => Refresh();
        tick.Start();
        Closed += (_, _) => tick.Stop();
        Refresh();
    }

    /// <summary>The newest items that pass the filters, as many as the look allows.</summary>
    public static IReadOnlyList<(ObtainedItem Item, ItemInfo Info)> Listed(IEnumerable<ObtainedItem> items, ItemLogLook look) =>
        items.Select(i => (i, ItemTable.Get(i.Item))).Where(p => look.Shows(p.Item2)).Take(look.Lines).ToArray();

    /// <summary>Redraws when something new came in or the look changed.</summary>
    public void Refresh(bool force = false)
    {
        var log = app.Meter.Items;
        if (!force && log.Version == shown)
            return;
        shown = log.Version;
        double scale = SessionWindow.Scale(look.Size);
        title.FontSize = 10.5 * scale;
        empty.FontSize = 12 * scale;
        lines.Children.Clear();
        lines.RowDefinitions.Clear();
        var listed = Listed(log.Items, look);
        foreach (var ((item, info), row) in listed.Select((p, i) => (p, i)))
        {
            lines.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var color = Brush.Parse(ItemTable.ColorOf(info.Grade));
            var cells = new (TextBlock Cell, int Column)[]
            {
                (new TextBlock { Text = item.At.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), FontSize = 11.5 * scale, Foreground = Soft, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = look.Times }, 0),
                (new TextBlock { Text = info.Name, FontSize = 12.5 * scale, Foreground = color }, 1),
                (new TextBlock { Text = Amount(item.Amount), FontSize = 12.5 * scale, FontWeight = FontWeight.Bold, Foreground = color, Margin = new Thickness(14, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right }, 2)
            };
            foreach (var (cell, column) in cells)
            {
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                ToolTip.SetTip(cell, $"{info.Grade} {info.Category}".Trim());
                lines.Children.Add(cell);
            }
        }

        empty.Text = log.Items.Count == 0 ? "Nothing obtained yet" : "Nothing that passes the filters";
        empty.IsVisible = listed.Count == 0;
    }

    /// <summary>Opens the item log's settings in the toolkit.</summary>
    public Action? ShowSettings { get; set; }

    List<MenuItem> Options() => OverlayMenu.Items(ShowSettings, Close, ("Clear log", () => app.Meter.Items.Clear()));

    /// <summary>"+3" or "+1,284".</summary>
    public static string Amount(long amount) => "+" + amount.ToString("N0", CultureInfo.InvariantCulture);
}
