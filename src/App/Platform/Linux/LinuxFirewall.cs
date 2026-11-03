using System.Diagnostics;

namespace ArnoA2.App;

/// <summary>Opens a port to the home network in the firewall that's running. Changing it takes
/// the desktop's password dialog, and reading ufw's rules would too, so whether the port is
/// already open isn't known: adding a rule twice does nothing.</summary>
public static class LinuxFirewall
{
    public static string? Running => Active("ufw") ? "ufw" : Active("firewalld") ? "firewalld" : null;

    public static string[] Ufw(string network, int port) => new[] { "/usr/bin/ufw", "allow", "from", network, "to", "any", "port", port.ToString(), "proto", "tcp" };

    // The network comes from the system's own address, so it's safe inside the shell line.
    public static string[] Firewalld(string network, int port) => new[] { "/bin/sh", "-c", $"firewall-cmd --permanent --add-rich-rule='rule family=ipv4 source address={network} port port={port} protocol=tcp accept' && firewall-cmd --reload" };

    public static async Task<string?> Allow(string network, int port)
    {
        var start = new ProcessStartInfo("pkexec") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string arg in Running == "firewalld" ? Firewalld(network, port) : Ufw(network, port))
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("pkexec did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await output;
        await process.WaitForExitAsync();
        return process.ExitCode == 0 ? null : process.ExitCode is 126 or 127 ? "Not changed: the password dialog was closed." : "Not changed: " + error.Trim();
    }

    static bool Active(string service)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("systemctl", new[] { "is-active", "--quiet", service }) { UseShellExecute = false });
            process?.WaitForExit(2000);
            return process is { HasExited: true, ExitCode: 0 };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
