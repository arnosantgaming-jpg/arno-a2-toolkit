using System.Diagnostics;

namespace ArnoA2.Capture;

/// <summary>What the daemon reads packets from.</summary>
public interface IPacketSource : IDisposable
{
    bool Offline { get; }
    /// <summary>Keeps only packets this machine received that match the expression.</summary>
    void FilterInbound(string expression);
    /// <summary>Next TCP segment, null on read timeout. Sets end at the end of a file.</summary>
    Segment? Next(out bool end);
}

/// <summary>Finds the TCP connections of the game process.</summary>
public interface IGameConnections
{
    IReadOnlyList<Connection> Scan();
}

/// <summary>The daemon's Linux and Windows parts, from the Linux and Windows folders.</summary>
public static class CapturePlatform
{
    public static IPacketSource Live() => OperatingSystem.IsWindows() ? NpcapCapture.Open() : PacketSource.Device("any");

    public static IGameConnections Scanner() => OperatingSystem.IsWindows() ? new WindowsGameConnections() : new GameConnections();

    /// <summary>Gives up what is no longer needed; returns a line for the status.</summary>
    public static string DropPrivileges()
    {
        // Windows: Npcap lets normal users capture, so the daemon has nothing extra to give up.
        if (OperatingSystem.IsWindows())
            return "";
        Privileges.DropAll();
        return "Privileges dropped: " + Privileges.Current();
    }

    public static bool Running(int process)
    {
        if (!OperatingSystem.IsWindows())
            return Directory.Exists("/proc/" + process);
        try
        {
            using var found = Process.GetProcessById(process);
            return !found.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
