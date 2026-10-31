using ArnoA2.Tracker;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>The Pet Tracker's drawer: what it tracks and how the mini-map behaves.</summary>
public sealed class PetSettingsPanel : UserControl
{
    public PetSettingsPanel(AppState app, PetLayer layer)
    {
        var engine = app.Engine;
        var s = engine.Settings;
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(Ui.Heading("Tracking"));
        panel.Children.Add(Ui.Choice("Order", new[] { "Unfinished first", "Recent only", "Closest to next rank" }, s.Sort, v => engine.ChangeSettings(x => x.Sort = v)));
        panel.Children.Add(Ui.Number("Rows shown", s.RowLimit, 1, 20, v => engine.ChangeSettings(x => x.RowLimit = v)));
        panel.Children.Add(Ui.Toggle("Hide recent pets after a while", s.ExpireInactive, v => engine.ChangeSettings(x => x.ExpireInactive = v)));
        panel.Children.Add(Ui.Number("…after seconds", s.InactiveSeconds, 5, 600, v => engine.ChangeSettings(x => x.InactiveSeconds = v)));
        panel.Children.Add(Ui.Number("Nearby pets stay for seconds", s.NearbyStaleSeconds, 10, 600, v => engine.ChangeSettings(x => x.NearbyStaleSeconds = v)));
        panel.Children.Add(Ui.Number("Pinned pets (max)", s.PinLimit, 1, 5, v => engine.ChangeSettings(x => x.PinLimit = v)));
        panel.Children.Add(Ui.Toggle("Black Cloud Merchant suggestions", s.MerchantSuggestions, v => engine.ChangeSettings(x => x.MerchantSuggestions = v)));
        panel.Children.Add(Ui.Toggle("New characters share this account's collection", s.NewCharactersJoin, v => engine.ChangeSettings(x => x.NewCharactersJoin = v)));
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
        panel.Children.Add(Ui.Toggle("Hide background image", !s.TrackerArt, v =>
        {
            engine.ChangeSettings(x => x.TrackerArt = !v);
            App.Tracker?.ApplyOpacity();
        }));
        panel.Children.Add(Ui.Note("Lower opacity lets the game show through, and fades the pet with it."));

        MiniMapSettings.Add(panel, layer.Hub);

        panel.Children.Add(Ui.Heading("Learned spawns"));
        var learned = Ui.Note("");
        void Count() => learned.Text = $"{layer.Hub.Learned.Count} spawns learned from play, shown with a white edge. A place counts once two different mobs appear there, away from every known spawn. This can only add spots: one a patch removed stays until the data is refreshed.";
        Count();
        panel.Children.Add(Ui.Toggle("Learn new pet spawns while playing", layer.Hub.Preferences.LearnSpawns, v =>
        {
            layer.Hub.Preferences.LearnSpawns = v;
            layer.Hub.Save();
        }));
        panel.Children.Add(Ui.Button("Forget learned spawns", () =>
        {
            layer.Hub.ForgetLearned();
            Count();
        }));
        panel.Children.Add(learned);

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
}

/// <summary>The settings of the mini-map every tool shares.</summary>
public static class MiniMapSettings
{
    public static void Add(StackPanel panel, MapHub hub)
    {
        var look = hub.Preferences;
        panel.Children.Add(Ui.Heading("Mini-map"));
        panel.Children.Add(Ui.Choice("Shape", new[] { "Circle", "Rectangle" }, look.MiniShape == "Rectangle" ? "Rectangle" : "Circle", v =>
        {
            look.MiniShape = v == "Rectangle" ? "Rectangle" : "Round";
            hub.Save();
            hub.Mini.Window?.ApplyShape(v != "Rectangle");
        }));
        double current = hub.Mini.Window?.Width ?? look.MiniWidth;
        string[] sizes = { "50%", "75%", "100%" };
        panel.Children.Add(Ui.Choice("Size", sizes, sizes[current < 190 ? 0 : current < 265 ? 1 : 2], v =>
        {
            double size = MiniMapWindow.FullSize * int.Parse(v.TrimEnd('%')) / 100;
            look.MiniWidth = size;
            look.MiniHeight = size + MiniMapWindow.Footer;
            hub.Save();
            hub.Mini.Window?.Resize(size);
        }));
        panel.Children.Add(Ui.Toggle("Read the in-game minimap for a precise position", hub.Preferences.ReadMinimap, v =>
        {
            hub.SetMinimapReading(v);
            hub.Save();
        }));
        panel.Children.Add(Ui.Note("Reads only the minimap corner of the game window (about 5% of one CPU core). Verteron, Altgard and the Abyss."));
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
        string daemon = Desktop.CaptureProgram;
        if (OperatingSystem.IsWindows())
            panel.Children.Add(Ui.Note("Capture uses Npcap, which you install once from npcap.com. Leave its \"administrators only\" option off."));
        else
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
    public AboutPanel(AppState app, string version, bool checkUpdates, Action<bool> setCheckUpdates)
    {
        Content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.Heading("Arno's A2 Toolkit " + version),
                Ui.Note($"Data: {Path.GetDirectoryName(app.Store.Folder)}"),
                Ui.Toggle("Check for updates at start", checkUpdates, setCheckUpdates),
                Ui.Note("Asks GitHub for the newest version number. Nothing about you or your game is sent, and an update only happens when you click it."),
                Ui.Note("Arno's A2 Toolkit is unofficial and not affiliated with NCSOFT; using third-party tools can put your account at risk. Message formats and data: 0Fox2Give/PetTracker and SkeeveAN/Aion-DPS-Meter. GPL-3.0.")
            }
        };
    }
}
