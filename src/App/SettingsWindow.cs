using ArnoA2.Meter;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArnoA2.App;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(AppState app)
    {
        Title = "Arno's DPS Meter · settings";
        Width = 440;
        Height = 420;
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
