using System.Diagnostics;
using System.Runtime.Versioning;

namespace ArnoA2.App;

/// <summary>Opens a port to the local network in Windows Firewall, through the usual
/// administrator prompt.</summary>
public static class WindowsFirewall
{
    public const string Rule = "Arno's A2 Toolkit Phone Map";

    public static string Add(int port) => $"advfirewall firewall add rule name=\"{Rule}\" dir=in action=allow protocol=TCP localport={port} remoteip=localsubnet profile=any";

    [SupportedOSPlatform("windows")]
    public static bool Allowed
    {
        get
        {
            var start = new ProcessStartInfo("netsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (string arg in new[] { "advfirewall", "firewall", "show", "rule", "name=" + Rule })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            process?.StandardOutput.ReadToEnd();
            process?.WaitForExit(3000);
            return process is { HasExited: true, ExitCode: 0 };
        }
    }

    [SupportedOSPlatform("windows")]
    public static async Task<string?> Allow(int port)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("netsh", Add(port)) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }) ?? throw new IOException("netsh did not start");
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? null : "Not changed: Windows refused the rule.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "Not changed: the administrator prompt was closed.";
        }
    }
}
