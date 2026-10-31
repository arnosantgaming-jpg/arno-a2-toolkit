using ArnoA2.Meter;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArnoA2.App.Dps;

public sealed class MeterSettingsWindow : Window
{
    public MeterSettingsWindow(AppState app, Action? opacityChanged = null)
    {
        Title = "Arno's A2 Toolkit · meter settings";
        Width = 440;
        Height = 360;
        Background = Ui.Window;
        var engine = app.Meter;
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(20) };

        panel.Children.Add(Ui.Heading("Fights"));
        var quiet = new NumericUpDown { Value = engine.Settings.MeterQuietSeconds, Minimum = 3, Maximum = 60, Increment = 1, FormatString = "0", Width = 130 };
        quiet.ValueChanged += (_, _) =>
        {
            if (quiet.Value is decimal v)
                engine.ChangeSettings(s => s.MeterQuietSeconds = (int)v);
        };
        var row = new DockPanel();
        DockPanel.SetDock(quiet, Dock.Right);
        row.Children.Add(quiet);
        row.Children.Add(new TextBlock { Text = "A fight ends after seconds without a hit", VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(row);

        panel.Children.Add(Ui.Heading("Look"));
        var opacityLabel = new TextBlock { Text = $"{engine.Settings.MeterOpacity} %", Width = 50, TextAlignment = Avalonia.Media.TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var opacity = new Slider { Minimum = 20, Maximum = 100, Value = engine.Settings.MeterOpacity };
        opacity.ValueChanged += (_, e) =>
        {
            int percent = (int)Math.Round(e.NewValue);
            opacityLabel.Text = $"{percent} %";
            engine.ChangeSettings(s => s.MeterOpacity = percent);
            opacityChanged?.Invoke();
        };
        var look = new DockPanel();
        DockPanel.SetDock(opacityLabel, Dock.Right);
        look.Children.Add(opacityLabel);
        look.Children.Add(opacity);
        panel.Children.Add(new TextBlock { Text = "Background opacity" });
        panel.Children.Add(look);
        panel.Children.Add(Ui.Note("Lower lets the game show through. Text and bars stay solid."));

        panel.Children.Add(Ui.Heading("Your character"));
        var character = new TextBox { Text = engine.Settings.Character, PlaceholderText = "Character name" };
        character.LostFocus += (_, _) => engine.ChangeSettings(s => s.Character = string.IsNullOrWhiteSpace(character.Text) ? null : character.Text.Trim());
        panel.Children.Add(character);
        panel.Children.Add(Ui.Note("Filled in at each login. A teleport also tells the game's name for you; or right-click your row and choose This is me."));

        Content = new ScrollViewer { Content = panel };
    }
}
