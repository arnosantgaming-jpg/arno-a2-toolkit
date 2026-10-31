using ArnoA2.Meter;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArnoA2.App;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(AppState app, Action? opacityChanged = null)
    {
        Title = "Arno's DPS Meter · settings";
        Width = 440;
        Height = 520;
        Background = Ui.Window;
        var engine = app.Engine;
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
        panel.Children.Add(Ui.Note("Filled in at each login. It marks your row once the game names you, usually at your first kill."));

        panel.Children.Add(Ui.Heading("Capture"));
        var captureNote = Ui.Note(app.CaptureStatus);
        panel.Children.Add(captureNote);
        string daemon = Path.Combine(AppContext.BaseDirectory, "capture", "arno-dps-capture");
        panel.Children.Add(Ui.Button("Grant capture permission", async () =>
        {
            captureNote.Text = "Waiting for the password dialog…";
            try
            {
                captureNote.Text = await AppState.GrantPermission(daemon);
            }
            catch (Exception ex)
            {
                captureNote.Text = "Could not grant: " + ex.Message;
            }
        }, "Gives the capture program (only) permission to read network packets; asks for your password"));
        if (KdeWindowRules.Available)
        {
            var kdeNote = Ui.Note("");
            try
            {
                kdeNote.Text = KdeWindowRules.Installed() ? "The KDE window rule is installed." : "Adds a KDE window rule so the meter stays above a fullscreen game.";
            }
            catch (Exception)
            {
            }

            panel.Children.Add(Ui.Button("Keep the meter above the game (KDE)", () =>
            {
                try
                {
                    KdeWindowRules.Install();
                    kdeNote.Text = "Added. Remove it in System Settings → Window Management → Window Rules.";
                }
                catch (Exception ex)
                {
                    kdeNote.Text = "Could not add the rule: " + ex.Message;
                }
            }));
            panel.Children.Add(kdeNote);
        }

        panel.Children.Add(Ui.Heading("About"));
        panel.Children.Add(Ui.Note("Arno's DPS Meter is unofficial and not affiliated with NCSOFT; using third-party tools can put your account at risk. GPL-3.0."));
        Content = new ScrollViewer { Content = panel };
    }
}
