using ArnoA2.Tracker;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>The Pet Tracker's drawer: what it tracks and how the mini-map behaves.</summary>
public sealed class PetSettingsPanel : UserControl
{
    public PetSettingsPanel(AppState app, MapHub maps)
    {
        var engine = app.Engine;
        var s = engine.Settings;
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(Ui.Heading("Tracking"));
        panel.Children.Add(Choice("Order", new[] { "Unfinished first", "Recent only", "Closest to next rank" }, s.Sort, v => engine.ChangeSettings(x => x.Sort = v)));
        panel.Children.Add(Number("Rows shown", s.RowLimit, 1, 20, v => engine.ChangeSettings(x => x.RowLimit = v)));
        panel.Children.Add(Toggle("Hide recent pets after a while", s.ExpireInactive, v => engine.ChangeSettings(x => x.ExpireInactive = v)));
        panel.Children.Add(Number("…after seconds", s.InactiveSeconds, 5, 600, v => engine.ChangeSettings(x => x.InactiveSeconds = v)));
        panel.Children.Add(Number("Nearby pets stay for seconds", s.NearbyStaleSeconds, 10, 600, v => engine.ChangeSettings(x => x.NearbyStaleSeconds = v)));
        panel.Children.Add(Number("Pinned pets (max)", s.PinLimit, 1, 5, v => engine.ChangeSettings(x => x.PinLimit = v)));
        panel.Children.Add(Toggle("Black Cloud Merchant suggestions", s.MerchantSuggestions, v => engine.ChangeSettings(x => x.MerchantSuggestions = v)));
        panel.Children.Add(Toggle("New characters share this account's collection", s.NewCharactersJoin, v => engine.ChangeSettings(x => x.NewCharactersJoin = v)));
        panel.Children.Add(Ui.Note("Pet collections are account-wide in AION 2. Turn this off only if you play several accounts."));

        panel.Children.Add(Ui.Heading("Look"));
        var opacityLabel = new TextBlock { Text = $"{s.TrackerOpacity} %", Width = 50, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var opacity = new Slider { Minimum = 20, Maximum = 100, Value = s.TrackerOpacity };
        opacity.ValueChanged += (_, e) =>
        {
            int percent = (int)Math.Round(e.NewValue);
            opacityLabel.Text = $"{percent} %";
            engine.ChangeSettings(x => x.TrackerOpacity = percent);
            App.Tracker?.ApplyOpacity();
        };
        var look = new DockPanel();
        DockPanel.SetDock(opacityLabel, Dock.Right);
        look.Children.Add(opacityLabel);
        look.Children.Add(opacity);
        panel.Children.Add(new TextBlock { Text = "Background opacity" });
        panel.Children.Add(look);
        panel.Children.Add(Toggle("Hide background image", !s.TrackerArt, v =>
        {
            engine.ChangeSettings(x => x.TrackerArt = !v);
            App.Tracker?.ApplyOpacity();
        }));
        panel.Children.Add(Ui.Note("Lower opacity lets the game show through, and fades the pet with it."));

        panel.Children.Add(Ui.Heading("Mini-map"));
        panel.Children.Add(Choice("Shape", new[] { "Circle", "Rectangle" }, maps.Preferences.MiniShape == "Rectangle" ? "Rectangle" : "Circle", v =>
        {
            maps.Preferences.MiniShape = v == "Rectangle" ? "Rectangle" : "Round";
            maps.Save();
            App.Tracker?.SetMiniMapShape(v != "Rectangle");
        }));
        double current = App.Tracker?.MiniMap?.Width ?? maps.Preferences.MiniWidth;
        string[] sizes = { "50%", "75%", "100%" };
        panel.Children.Add(Choice("Size", sizes, sizes[current < 190 ? 0 : current < 265 ? 1 : 2], v =>
        {
            double size = MiniMapWindow.FullSize * int.Parse(v.TrimEnd('%')) / 100;
            maps.Preferences.MiniWidth = size;
            maps.Preferences.MiniHeight = size + MiniMapWindow.Footer;
            maps.Save();
            App.Tracker?.MiniMap?.Resize(size);
        }));
        panel.Children.Add(Toggle("Read the in-game minimap for a precise position", maps.Preferences.ReadMinimap, v =>
        {
            maps.SetMinimapReading(v);
            maps.Save();
        }));
        panel.Children.Add(Ui.Note("Reads only the minimap corner of the game window (about 5% of one CPU core). Verteron, Altgard and the Abyss."));

        panel.Children.Add(Ui.Heading("Account"));
        var name = new TextBox { Text = engine.Profile.Name };
        name.LostFocus += (_, _) =>
        {
            engine.Profile.Name = string.IsNullOrWhiteSpace(name.Text) ? "My account" : name.Text.Trim();
            engine.ChangeSettings(_ => { });
        };
        panel.Children.Add(name);
        panel.Children.Add(Ui.Note(engine.Profile.Characters.Count > 0 ? "Characters: " + string.Join(", ", engine.Profile.Characters) : "No characters seen yet."));

        Content = panel;
    }

    static Control Toggle(string label, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    static Control Number(string label, int value, int min, int max, Action<int> changed)
    {
        var input = new NumericUpDown { Value = value, Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Width = 130 };
        input.ValueChanged += (_, _) =>
        {
            if (input.Value is decimal v)
                changed((int)v);
        };
        return Row(label, input);
    }

    static Control Choice(string label, string[] options, string value, Action<string> changed)
    {
        var box = new ComboBox { ItemsSource = options, SelectedItem = options.Contains(value) ? value : options[0], Width = 200 };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string v)
                changed(v);
        };
        return Row(label, box);
    }

    static Control Row(string label, Control input)
    {
        var row = new DockPanel();
        DockPanel.SetDock(input, Dock.Right);
        row.Children.Add(input);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}

/// <summary>What every tool shares: the capture permission and the window rules.</summary>
public sealed class ToolkitSettingsPanel : UserControl
{
    readonly AppState app;
    readonly TextBlock captureNote = Ui.Note("");

    public ToolkitSettingsPanel(AppState app)
    {
        this.app = app;
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(Ui.Heading("Capture"));
        panel.Children.Add(captureNote);
        string daemon = Path.Combine(AppContext.BaseDirectory, "capture", "arno-a2-capture");
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
                kdeNote.Text = KdeWindowRules.Installed() ? "KDE window rules are installed." : "Adds KDE window rules so the meter, tracker and mini-map stay above a fullscreen game.";
            }
            catch (Exception)
            {
            }

            panel.Children.Add(Ui.Button("Keep the tools above the game (KDE)", () =>
            {
                try
                {
                    KdeWindowRules.Install();
                    kdeNote.Text = "Added. Reopen the mini-map if it is still behind the game. Remove them in System Settings → Window Management → Window Rules.";
                }
                catch (Exception ex)
                {
                    kdeNote.Text = "Could not add the rules: " + ex.Message;
                }
            }));
            panel.Children.Add(kdeNote);
        }

        Content = panel;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        app.Changed += Update;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        app.Changed -= Update;
    }

    void Update() => captureNote.Text = captureNote.Text is { Length: > 0 } t && (t.StartsWith("Permission", StringComparison.Ordinal) || t.StartsWith("Not granted", StringComparison.Ordinal) || t.StartsWith("Waiting for the password", StringComparison.Ordinal)) ? t : app.CaptureStatus;
}

/// <summary>Who made this and where its data lives.</summary>
public sealed class AboutPanel : UserControl
{
    public AboutPanel(AppState app, string version)
    {
        Content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.Heading("Arno's A2 Toolkit " + version),
                Ui.Note($"Data: {Path.GetDirectoryName(app.Store.Folder)}"),
                Ui.Note("Arno's A2 Toolkit is unofficial and not affiliated with NCSOFT; using third-party tools can put your account at risk. Message formats and data: 0Fox2Give/PetTracker and SkeeveAN/Aion-DPS-Meter. GPL-3.0.")
            }
        };
    }
}
