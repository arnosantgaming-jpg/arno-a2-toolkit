using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace ArnoA2.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        SelfUpdate.Finish(args);
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
            if (args.Contains(Remote.ToggleBars))
                return Remote.Send(Remote.ToggleBars) ? 0 : 1;
            Console.Error.WriteLine("Arno's A2 Toolkit is already running.");
            return 0;
        }

        using (instance)
            // Drawn on the CPU: these small windows gain nothing from the GPU, and sharing the
            // NVIDIA driver with a running game crashes inside it.
            return AppBuilder.Configure<App>().UsePlatformDetect().With(new X11PlatformOptions { RenderingMode = new[] { X11RenderingMode.Software } }).With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } }).StartWithClassicDesktopLifetime(args);
    }
}

public sealed class App : Application
{
    public static TrackerWindow? Tracker { get; set; }
    AppState? state;
    IDisposable? remote;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        // Subpixel text needs a solid background behind it: over the see-through overlays it comes
        // out with dark blocks and coloured edges around the letters.
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppState.Adopt();
            var store = AppState.OpenStore();
            string daemon = Desktop.CaptureProgram;
            state = new AppState(store, new ArnoA2.Meter.Store(ArnoA2.Meter.Store.DefaultFolder()), File.Exists(daemon) ? daemon : null);
            var hub = new HubWindow(state, new MapHub(state, store.Folder));
            desktop.MainWindow = hub;
            desktop.ShutdownRequested += (_, _) => state.Stop();
            hub.Closed += (_, _) => state.Stop();
            try
            {
                remote = Remote.Listen(command =>
                {
                    if (command == Remote.ToggleBars)
                        Avalonia.Threading.Dispatcher.UIThread.Post(hub.ToggleBars);
                });
                hub.Closed += (_, _) => remote.Dispose();
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Without it the shortcut does nothing; everything else works.
            }
            state.Start();
            _ = hub.CheckForUpdates();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
