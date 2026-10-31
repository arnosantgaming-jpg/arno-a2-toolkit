using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

namespace ArnoA2.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // One toolkit per user: a second start just exits.
        string lockPath = Path.Combine(Path.GetDirectoryName(ArnoA2.Protocol.CaptureChannel.DefaultPath())!, "app.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        FileStream instance;
        try
        {
            instance = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Console.Error.WriteLine("Arno's A2 Toolkit is already running.");
            return 0;
        }

        using (instance)
            // Drawn on the CPU: these small windows gain nothing from the GPU, and sharing the
            // NVIDIA driver with a running game crashes inside it.
            return AppBuilder.Configure<App>().UsePlatformDetect().With(new X11PlatformOptions { RenderingMode = new[] { X11RenderingMode.Software } }).StartWithClassicDesktopLifetime(args);
    }
}

public sealed class App : Application
{
    public static TrackerWindow? Tracker { get; set; }
    AppState? state;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppState.Adopt();
            var store = AppState.OpenStore();
            string daemon = Path.Combine(AppContext.BaseDirectory, "capture", "arno-a2-capture");
            state = new AppState(store, new ArnoA2.Meter.Store(ArnoA2.Meter.Store.DefaultFolder()), File.Exists(daemon) ? daemon : null);
            var hub = new HubWindow(state, new MapHub(state, store.Folder));
            desktop.MainWindow = hub;
            desktop.ShutdownRequested += (_, _) => state.Stop();
            hub.Closed += (_, _) => state.Stop();
            state.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
