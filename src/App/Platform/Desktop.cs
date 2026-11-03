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

    /// <summary>The firewall that may block phones, null when none is running.</summary>
    public static string? Firewall => OperatingSystem.IsWindows() ? "Windows Firewall" : LinuxFirewall.Running;

    /// <summary>The port is known to be open already (Linux can't tell without a password).</summary>
    public static bool FirewallAllows => OperatingSystem.IsWindows() && WindowsFirewall.Allowed;

    /// <summary>Opens a port to the home network, asking the system's own prompt. Null when
    /// it worked, or what went wrong.</summary>
    public static Task<string?> AllowThroughFirewall(string network, int port) => OperatingSystem.IsWindows() ? WindowsFirewall.Allow(port) : LinuxFirewall.Allow(network, port);

    /// <summary>Two short rising tones. Silent where nothing can play them.</summary>
    public static void Chime() => Task.Run(() =>
    {
        if (OperatingSystem.IsWindows())
        {
            Console.Beep(880, 140);
            Console.Beep(1320, 220);
            return;
        }

        string file = Path.Combine(Path.GetTempPath(), "arno-a2-chime.wav");
        try
        {
            if (!File.Exists(file))
                File.WriteAllBytes(file, ChimeSound());
            foreach (string player in new[] { "pw-play", "paplay" })
            {
                try
                {
                    using var process = Process.Start(new ProcessStartInfo(player, new[] { file }) { UseShellExecute = false });
                    return;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // not installed: try the next
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the note on screen still shows
        }
    });

    // 16-bit mono PCM in a WAV file.
    static byte[] ChimeSound()
    {
        const int Rate = 44100;
        var samples = new[] { (880.0, 0.14), (1320.0, 0.24) }.SelectMany(tone => Enumerable.Range(0, (int)(Rate * tone.Item2)).Select(i =>
            (short)(Math.Sin(2 * Math.PI * tone.Item1 * i / Rate) * 12000 * (1 - i / (Rate * tone.Item2))))).ToArray();
        using var memory = new MemoryStream();
        using var wav = new BinaryWriter(memory);
        wav.Write("RIFF"u8);
        wav.Write(36 + samples.Length * 2);
        wav.Write("WAVEfmt "u8);
        wav.Write(16);
        wav.Write((short)1);
        wav.Write((short)1);
        wav.Write(Rate);
        wav.Write(Rate * 2);
        wav.Write((short)2);
        wav.Write((short)16);
        wav.Write("data"u8);
        wav.Write(samples.Length * 2);
        foreach (short sample in samples)
            wav.Write(sample);
        wav.Flush();
        return memory.ToArray();
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
