using System.Globalization;
using ArnoA2.Meter;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArnoA2.App;

/// <summary>How the HP and mana bars look, and where they were left.</summary>
public sealed class BarSettings
{
    public static readonly string[] Sizes = { "Small", "Medium", "Large" };
    public static readonly string[] NumberStyles = { "Numbers", "Percent", "None" };
    public static readonly (string Name, string Color)[] HpColors = { ("Red", "#D23A33"), ("Crimson", "#A81E3C"), ("Orange", "#E2702B"), ("Green", "#3FAF4F") };
    public static readonly (string Name, string Color)[] ManaColors = { ("Blue", "#3C7FD6"), ("Cyan", "#22B5D8"), ("Purple", "#8A5BE0") };
    public static readonly (string Name, string Color)[] StaminaColors = { ("Gold", "#E0B93C"), ("Green", "#5FC25A"), ("White", "#D9D6CC") };

    public bool Open { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public string Size { get; set; } = "Medium";
    public string Numbers { get; set; } = "Numbers";
    public bool Fade { get; set; } = true;
    public bool IdleFade { get; set; } = true;
    public int LowHp { get; set; } = 30;
    public string HpColor { get; set; } = "Red";
    public string ManaColor { get; set; } = "Blue";
    public bool Stamina { get; set; } = true;
    public string StaminaColor { get; set; } = "Gold";
}

/// <summary>
/// Your HP, mana and stamina as wide bars to put anywhere on the screen. A hit leaves a pale strip
/// that drains after a moment, so you see how much it took. The game sends a value only when
/// it changes, so each bar learns its maximum from the highest value seen and is blank until
/// its first change. They dim when you've done nothing for a moment. Drag to move; right-click
/// for options.
/// </summary>
public sealed class BarsWindow : Window
{
    static readonly IBrush Track = new SolidColorBrush(Color.Parse("#0B0C10"), 0.82), Rim = Brush.Parse("#5A5546"), Chip = Brush.Parse("#F0DBA8"), Label = Brush.Parse("#F4F2EA"), Waiting = Brush.Parse("#9C9888");
    static readonly TimeSpan ChipHold = TimeSpan.FromMilliseconds(500), FadeAfter = TimeSpan.FromSeconds(4), IdleAfter = TimeSpan.FromSeconds(2);
    readonly AppState app;
    readonly BarSettings saved;
    readonly Action save;
    readonly DispatcherTimer frame = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly Border hpTrack = Frame(), manaTrack = Frame(), staminaTrack = Frame(), hpFill = new(), hpChip = new() { Background = Chip }, manaFill = new(), staminaFill = new();
    readonly TextBlock hpText = Text(), manaText = Text();
    readonly Border face;
    double hpShown, chipShown, manaShown, staminaShown, width;
    DateTimeOffset hitAt, fullSince = DateTimeOffset.UtcNow;
    bool hovered, hidden;
    PixelPoint place;

    public BarsWindow(AppState app, BarSettings saved, Action save)
    {
        this.app = app;
        this.saved = saved;
        this.save = save;
        Title = KdeWindowRules.BarsTitle;
        Icon = ToolTheme.AppIcon;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;

        hpTrack.Child = new Panel { Children = { Left(hpChip), Left(hpFill), hpText } };
        manaTrack.Child = new Panel { Children = { Left(manaFill), manaText } };
        staminaTrack.Child = Left(staminaFill);
        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(hpTrack);
        stack.Children.Add(manaTrack);
        stack.Children.Add(staminaTrack);
        face = new Border { Padding = new Thickness(6), Background = Brushes.Transparent, Child = stack, Cursor = new Cursor(StandardCursorType.SizeAll) };
        ToolTip.SetTip(face, "Your HP, mana and stamina. Drag to move, right-click for options");
        face.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(face).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        face.PointerEntered += (_, _) => hovered = true;
        face.PointerExited += (_, _) => hovered = false;
        face.ContextMenu = new ContextMenu();
        face.ContextMenu.Opening += (_, _) => face.ContextMenu.ItemsSource = Options();
        Content = face;
        Restyle();

        Opened += (_, _) =>
        {
            if (saved.X is int x && saved.Y is int y && Screens.ScreenFromPoint(new PixelPoint(x, y)) != null)
                Position = new PixelPoint(x, y);
            else if (Screens.Primary is { } main)
                Position = new PixelPoint(main.WorkingArea.X + (main.WorkingArea.Width - (int)Bounds.Width) / 2, main.WorkingArea.Bottom - 220);
            PositionChanged += (_, e) =>
            {
                (saved.X, saved.Y) = (e.Point.X, e.Point.Y);
                save();
            };
        };
        frame.Tick += (_, _) => Step();
        frame.Start();
        Closed += (_, _) => frame.Stop();
        Step();
    }

    /// <summary>True while the shortcut has the bars hidden.</summary>
    public bool HiddenByUser => hidden;

    /// <summary>Hides the bars until called again, for a shortcut key.</summary>
    public void Toggle()
    {
        hidden = !hidden;
        // Off the screen while hidden, so clicks reach the game beneath.
        if (hidden)
        {
            place = Position;
            Hide();
        }
        else
            HubWindow.Reshow(this, place);
        Step();
    }

    /// <summary>How visible the bars should be: gone when hidden by the shortcut, faint when
    /// full, dimmed when idle, unless the pointer is on them.</summary>
    public static double Visibility(bool hidden, bool hovered, bool fadeFull, bool full, bool idleFade, bool idle) =>
        hidden ? 0 : hovered ? 1 : fadeFull && full ? 0.08 : idleFade && idle ? 0.3 : 1;

    /// <summary>The bar width and HP bar height for a size.</summary>
    public static (double Width, double Height) Dimensions(string size) => size switch
    {
        "Small" => (280, 15),
        "Large" => (520, 24),
        _ => (380, 19)
    };

    /// <summary>What a bar says: "30,790 / 30,790", "100 %", or nothing.</summary>
    public static string Reading(long? value, long max, string style)
    {
        if (value is not long now)
            return "";
        return style switch
        {
            "Percent" => (max > 0 ? Math.Round(100.0 * now / max) : 0).ToString("0", CultureInfo.InvariantCulture) + " %",
            "None" => "",
            _ => now.ToString("N0", CultureInfo.InvariantCulture) + " / " + max.ToString("N0", CultureInfo.InvariantCulture)
        };
    }

    void Restyle()
    {
        var (barWidth, height) = Dimensions(saved.Size);
        width = barWidth;
        bool numbers = saved.Numbers != "None";
        hpTrack.Width = manaTrack.Width = staminaTrack.Width = width;
        hpTrack.Height = height;
        manaTrack.Height = numbers ? Math.Round(height * 0.7) : Math.Round(height * 0.45);
        hpText.FontSize = Math.Max(9.5, height * 0.6);
        manaText.FontSize = Math.Max(9, manaTrack.Height * 0.72);
        staminaTrack.Height = Math.Max(5, Math.Round(height * 0.32));
        staminaTrack.IsVisible = saved.Stamina;
        manaText.IsVisible = numbers;
        hpFill.Background = Brush.Parse(Pick(BarSettings.HpColors, saved.HpColor));
        manaFill.Background = Brush.Parse(Pick(BarSettings.ManaColors, saved.ManaColor));
        staminaFill.Background = Brush.Parse(Pick(BarSettings.StaminaColors, saved.StaminaColor));
    }

    void Step()
    {
        var now = DateTimeOffset.UtcNow;
        var own = app.Meter.Own();
        double hp = own?.HpShare ?? 0, mana = own?.ManaShare ?? 0, stamina = own?.Stamina == null ? 1 : own.StaminaShare;
        // A hit drops the bar at once and leaves the chip; healing moves both together.
        if (hp < hpShown - 0.0005)
            hitAt = now;
        hpShown = hp;
        if (hp >= chipShown)
            chipShown = hp;
        else if (now - hitAt > ChipHold)
            chipShown = Math.Max(hp, chipShown - 0.025);
        manaShown = mana;
        // The game sends stamina refilling about once a second, so rises glide; spending shows at once.
        staminaShown = stamina < staminaShown ? stamina : staminaShown + (stamina - staminaShown) * 0.12;

        hpFill.Width = width * hpShown;
        hpChip.Width = width * chipShown;
        manaFill.Width = width * manaShown;
        staminaFill.Width = width * staminaShown;
        bool low = own?.Hp != null && saved.LowHp > 0 && hp * 100 < saved.LowHp;
        hpFill.Opacity = low ? 0.6 + 0.4 * Math.Abs(Math.Sin(now.ToUnixTimeMilliseconds() / 260.0)) : 1;
        hpText.Text = own?.Hp == null ? "HP shows after its first change" : Reading(own.Hp, own.MaxHp, saved.Numbers);
        hpText.Foreground = own?.Hp == null ? Waiting : Label;
        manaText.Text = Reading(own?.Mana, own?.MaxMana ?? 0, saved.Numbers);

        bool full = own is { Hp: not null, Mana: not null } && hp >= 0.999 && mana >= 0.999 && (!saved.Stamina || stamina >= 0.999);
        if (!full)
            fullSince = now;
        // Idle only after something has been seen: just started, the bars stay up.
        bool idle = app.Meter.ActiveAt is { } active && now - active > IdleAfter;
        double target = Visibility(hidden, hovered, saved.Fade, full && now - fullSince > FadeAfter, saved.IdleFade, idle);
        // Coming back and hiding show at once; only fading out is gradual.
        face.Opacity = target >= face.Opacity || target == 0 ? target : face.Opacity + (target - face.Opacity) * 0.15;
        face.IsHitTestVisible = target > 0;
    }

    List<MenuItem> Options()
    {
        MenuItem Item(string header, bool on, Action pick)
        {
            var item = new MenuItem { Header = (on ? "● " : "    ") + header };
            item.Click += (_, _) =>
            {
                pick();
                save();
                Restyle();
            };
            return item;
        }

        MenuItem Group(string header, IEnumerable<MenuItem> items) => new() { Header = header, ItemsSource = items.ToList() };

        return new List<MenuItem>
        {
            Group("Size", BarSettings.Sizes.Select(s => Item(s, saved.Size == s, () => saved.Size = s))),
            Group("Text", BarSettings.NumberStyles.Select(s => Item(s, saved.Numbers == s, () => saved.Numbers = s))),
            Group("HP colour", BarSettings.HpColors.Select(c => Item(c.Name, saved.HpColor == c.Name, () => saved.HpColor = c.Name))),
            Group("Mana colour", BarSettings.ManaColors.Select(c => Item(c.Name, saved.ManaColor == c.Name, () => saved.ManaColor = c.Name))),
            Group("Stamina colour", BarSettings.StaminaColors.Select(c => Item(c.Name, saved.StaminaColor == c.Name, () => saved.StaminaColor = c.Name))),
            Item("Stamina bar", saved.Stamina, () => saved.Stamina = !saved.Stamina),
            Group("Low HP pulse", new[] { 0, 20, 30, 50 }.Select(p => Item(p == 0 ? "Off" : $"Below {p} %", saved.LowHp == p, () => saved.LowHp = p))),
            Item("Fade when full", saved.Fade, () => saved.Fade = !saved.Fade),
            Item("Fade when idle", saved.IdleFade, () => saved.IdleFade = !saved.IdleFade),
            Item("Close", false, Close)
        };
    }

    static string Pick((string Name, string Color)[] colors, string name) => colors.FirstOrDefault(c => c.Name == name).Color ?? colors[0].Color;

    static Border Frame() => new() { Background = Track, BorderBrush = Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), ClipToBounds = true };

    static Border Left(Border fill)
    {
        fill.HorizontalAlignment = HorizontalAlignment.Left;
        fill.Width = 0;
        return fill;
    }

    static TextBlock Text() => new() { FontWeight = FontWeight.SemiBold, Foreground = Label, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
}
