using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArnoA2.App;

/// <summary>The short always-on-top note of an alert, such as a cube appearing or an event
/// about to start. Click to dismiss.</summary>
public sealed class AlertWindow : Window
{
    static readonly TimeSpan Shown = TimeSpan.FromSeconds(12);

    public AlertWindow(string text, Color accent)
    {
        Title = KdeWindowRules.AlertTitle;
        Icon = ToolTheme.AppIcon;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Content = new Border
        {
            Padding = new Thickness(18, 10),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(accent),
            Background = new SolidColorBrush(Color.Parse("#05070D"), 0.88),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center }
        };
        PointerPressed += (_, _) => Close();
        Opened += (_, _) =>
        {
            if (Screens.Primary is { } main)
                Position = new PixelPoint(main.WorkingArea.X + (main.WorkingArea.Width - (int)(Bounds.Width * main.Scaling)) / 2, main.WorkingArea.Y + 80);
            DispatcherTimer.RunOnce(Close, Shown);
        };
    }
}
