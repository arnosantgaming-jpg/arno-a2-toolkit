using System.Text.Json;
using ArnoA2.App.Dps;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>Which tools were open, so the next start brings them back.</summary>
public sealed class HubSettings
{
    public bool Pets { get; set; } = true;
    public bool Meter { get; set; } = true;

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
/// all share. Closing it closes the toolkit.</summary>
public sealed class HubWindow : Window
{
    readonly AppState app;
    readonly MapHub maps;
    readonly string folder;
    readonly HubSettings saved;
    readonly TextBlock status = new() { FontSize = 12, Foreground = Dps.Ui.Muted, TextWrapping = TextWrapping.Wrap };
    TrackerWindow? pets;
    MeterWindow? meter;
    SettingsWindow? settings;
    bool closing;

    public HubWindow(AppState app, MapHub maps)
    {
        this.app = app;
        this.maps = maps;
        folder = Path.GetDirectoryName(app.Store.Folder)!;
        saved = HubSettings.Load(folder);
        Title = "Arno's A2 Toolkit";
        Width = 340;
        Height = 300;
        MinWidth = 300;
        MinHeight = 260;
        Background = Dps.Ui.Window;

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "ARNO'S A2 TOOLKIT", FontSize = 15, FontWeight = FontWeight.SemiBold, LetterSpacing = 2, Foreground = Dps.Ui.Accent });
        panel.Children.Add(status);
        panel.Children.Add(Tool("Pets", "Ranks, souls, map and farming spots", OpenPets));
        panel.Children.Add(Tool("DPS Meter", "Damage, skills and the fight log", OpenMeter));
        panel.Children.Add(Tool("Farming Assist", "Coming next", null));
        panel.Children.Add(Dps.Ui.Flat("Settings", OpenSettings, "Capture permission and window rules"));
        Content = new ScrollViewer { Content = panel };

        app.Changed += Refresh;
        Refresh();
        Opened += (_, _) =>
        {
            if (saved.Pets)
                OpenPets();
            if (saved.Meter)
                OpenMeter();
        };
        Closing += (_, _) => closing = true;
        Closed += (_, _) =>
        {
            app.Changed -= Refresh;
            pets?.Close();
            meter?.Close();
            settings?.Close();
        };
    }

    void Refresh() => status.Text = app.CaptureStatus;

    static Control Tool(string name, string about, Action? open)
    {
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8), IsEnabled = open != null };
        button.Content = new StackPanel { Children = { new TextBlock { Text = name, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = about, FontSize = 11, Foreground = Dps.Ui.Muted } } };
        if (open != null)
            button.Click += (_, _) => open();
        return button;
    }

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
            };
            pets.Show();
            Remember(s => s.Pets = true);
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
            };
            meter.Show();
            Remember(s => s.Meter = true);
        }

        meter.Activate();
    }

    void OpenSettings()
    {
        if (settings == null)
        {
            settings = new SettingsWindow(app, maps, pets);
            settings.Closed += (_, _) => settings = null;
            settings.Show();
        }

        settings.Activate();
    }
}
