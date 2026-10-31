using System.Diagnostics;
using System.Security.Cryptography;

namespace ArnoA2.App;

/// <summary>
/// One-click update of the portable Windows exe: download the release's exe, check it against
/// the release's checksum, swap it in, and start it. A running exe can't be overwritten but can
/// be renamed, so the old one steps aside as ".old" and the new start deletes it.
/// </summary>
public static class SelfUpdate
{
    public const string AfterUpdate = "--after-update";

    /// <summary>True for the portable exe, which runs from an unpacked copy of itself. The
    /// plain folder build has its files beside the exe, so one file can't update it.</summary>
    public static bool Possible => OperatingSystem.IsWindows() && Environment.ProcessPath is { } exe && Path.GetFullPath(Path.GetDirectoryName(exe)!).TrimEnd('\\', '/') != Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\', '/');

    public static string Asset(string version) => $"arno-a2-toolkit-{version}-windows-x64.exe";

    /// <summary>Downloads the update beside the exe (same drive, so the swap can't half fail).</summary>
    public static async Task Install(AvailableUpdate update, Action<int> progress)
    {
        string exe = Environment.ProcessPath!, incoming = exe + ".new", url = $"{update.Downloads}/{Asset(update.Version)}";
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("arno-a2-toolkit");
        string expected = Checksum(await client.GetStringAsync(url + ".sha256")) ?? throw new IOException("The release has no valid checksum.");
        using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0, read = 0;
            await using var from = await response.Content.ReadAsStreamAsync();
            await using var to = File.Create(incoming);
            var buffer = new byte[1 << 20];
            int n, shown = -1;
            while ((n = await from.ReadAsync(buffer)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, n));
                read += n;
                int percent = total > 0 ? (int)(read * 100 / total) : 0;
                if (percent != shown)
                    progress(shown = percent);
            }
        }

        if (!Matches(incoming, expected))
        {
            File.Delete(incoming);
            throw new IOException("The download did not match its checksum.");
        }

        Swap(exe, incoming);
        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        start.ArgumentList.Add(AfterUpdate);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(start)?.Dispose();
    }

    /// <summary>The hash in a sha256sum line, lower case, or null.</summary>
    public static string? Checksum(string text)
    {
        string first = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length == 64 && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    public static bool Matches(string file, string expected)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream)) == expected;
    }

    /// <summary>Puts the new exe in place, or leaves the old one where it was.</summary>
    public static void Swap(string exe, string incoming)
    {
        string old = exe + ".old";
        File.Delete(old);
        File.Move(exe, old);
        try
        {
            File.Move(incoming, exe);
        }
        catch
        {
            File.Move(old, exe);
            throw;
        }
    }

    /// <summary>In the new version: waits for the old one to close so its single-instance lock
    /// is free, then removes what it left: the old exe and its unpacked copy.</summary>
    public static void Finish(string[] args)
    {
        int at = Array.IndexOf(args, AfterUpdate);
        if (at < 0)
            return;
        try
        {
            if (at + 1 < args.Length && int.TryParse(args[at + 1], out int old))
            {
                using var process = Process.GetProcessById(old);
                process.WaitForExit(20_000);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Already gone.
        }

        try
        {
            if (Environment.ProcessPath is { } exe)
                File.Delete(exe + ".old");
            var unpacked = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('\\', '/'));
            if (Possible && unpacked.Parent is { Name: "arno-a2-toolkit", Parent.Name: ".net" } versions)
                foreach (var folder in versions.GetDirectories().Where(d => d.FullName != unpacked.FullName))
                    folder.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next update.
        }
    }
}
