using System.Diagnostics;

namespace ArnoA2.App;

/// <summary>The app's dealings with the desktop, from the Linux and Windows folders.</summary>
static class Desktop
{
    public static string CaptureProgram => Path.Combine(AppContext.BaseDirectory, "capture", OperatingSystem.IsWindows() ? "arno-a2-capture.exe" : "arno-a2-capture");

    public static bool StartsAtLogin => OperatingSystem.IsWindows() ? WindowsAutostart.Enabled : LinuxAutostart.Enabled;

    public static void SetStartsAtLogin(bool on)
    {
        if (OperatingSystem.IsWindows())
            WindowsAutostart.Set(on);
        else
            LinuxAutostart.Set(on);
    }

    /// <summary>Opens a folder or link. On Linux through the given program, on Windows through
    /// whatever handles it there.</summary>
    public static void Open(string target, string linuxProgram)
    {
        try
        {
            var start = OperatingSystem.IsWindows() ? new ProcessStartInfo(target) { UseShellExecute = true } : new ProcessStartInfo(linuxProgram, new[] { target }) { UseShellExecute = false };
            Process.Start(start)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // not installed: nothing to open it with
        }
    }
}
