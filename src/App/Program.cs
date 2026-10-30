using ArnoA2.Meter;
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
        // One meter per user: a second start just exits.
        string lockPath = Path.Combine(Path.GetDirectoryName(ArnoA2.Protocol.CaptureChannel.DefaultPath())!, "app.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        FileStream instance;
        try
        {
            instance = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Console.Error.WriteLine("Arno's DPS Meter is already running.");
            return 0;
        }

        using (instance)
            return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class App : Application
{
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
            string daemon = Path.Combine(AppContext.BaseDirectory, "capture", "arno-dps-capture");
            state = new AppState(new Store(Store.DefaultFolder()), File.Exists(daemon) ? daemon : null);
            var meter = new MeterWindow(state);
            desktop.MainWindow = meter;
            desktop.ShutdownRequested += (_, _) => state.Stop();
            meter.Closed += (_, _) => state.Stop();
            state.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
