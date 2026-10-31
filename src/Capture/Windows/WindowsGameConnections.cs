using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace ArnoA2.Capture;

/// <summary>
/// Finds the game's TCP connections through the system's connection table, which names each
/// connection's process, keeping the same ones as on Linux: open or opening, not loopback, not
/// HTTPS. Processes are only listed by name; the game process is never opened.
/// </summary>
public sealed class WindowsGameConnections : IGameConnections
{
    [DllImport("iphlpapi")] static extern uint GetExtendedTcpTable(byte[]? table, ref int size, bool sorted, int family, int kind, uint reserved);
    const int FamilyV4 = 2, FamilyV6 = 23, OwnerPidConnections = 4, RowV4 = 24, RowV6 = 56;
    const uint SynSent = 3, Established = 5, InsufficientBuffer = 122;

    public IReadOnlyList<Connection> Scan()
    {
        var games = GameProcesses();
        if (games.Count == 0)
            return Array.Empty<Connection>();
        return ParseV4(Table(FamilyV4), games).Concat(ParseV6(Table(FamilyV6), games)).Distinct().ToArray();
    }

    static HashSet<int> GameProcesses()
    {
        var games = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            if (GameConnections.GameNames.Contains(process.ProcessName.ToLowerInvariant() + ".exe"))
                games.Add(process.Id);
            process.Dispose();
        }

        return games;
    }

    static byte[] Table(int family)
    {
        int size = 0;
        while (true)
        {
            var table = new byte[size];
            uint result = GetExtendedTcpTable(table, ref size, false, family, OwnerPidConnections, 0);
            if (result == 0)
                return table;
            if (result != InsufficientBuffer)
                throw new IOException("Could not read the connection table: error " + result);
        }
    }

    // MIB_TCPTABLE_OWNER_PID: a count, then rows of { state, local address, local port,
    // remote address, remote port, process }, u32 each; ports in network order.
    public static IEnumerable<Connection> ParseV4(byte[] table, IReadOnlySet<int> processes)
    {
        for (int row = 0; row < Count(table, RowV4); row++)
        {
            int at = 4 + row * RowV4;
            if (Keep(table, at, at + 20, processes) is not true)
                continue;
            var remote = new IPAddress(table.AsSpan(at + 12, 4));
            int remotePort = Port(table, at + 16);
            if (!IPAddress.IsLoopback(remote) && remotePort != 443)
                yield return new(new IPAddress(table.AsSpan(at + 4, 4)), Port(table, at + 8), remote, remotePort);
        }
    }

    // MIB_TCP6TABLE_OWNER_PID: a count, then rows of { local address[16], scope, local port,
    // remote address[16], scope, remote port, state, process }.
    public static IEnumerable<Connection> ParseV6(byte[] table, IReadOnlySet<int> processes)
    {
        for (int row = 0; row < Count(table, RowV6); row++)
        {
            int at = 4 + row * RowV6;
            if (Keep(table, at + 48, at + 52, processes) is not true)
                continue;
            var remote = Unmapped(table.AsSpan(at + 24, 16));
            int remotePort = Port(table, at + 44);
            if (!IPAddress.IsLoopback(remote) && remotePort != 443)
                yield return new(Unmapped(table.AsSpan(at, 16)), Port(table, at + 20), remote, remotePort);
        }
    }

    static int Count(byte[] table, int row) => table.Length < 4 ? 0 : Math.Min((int)BinaryPrimitives.ReadUInt32LittleEndian(table), (table.Length - 4) / row);

    static bool Keep(byte[] table, int state, int process, IReadOnlySet<int> processes) => BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(state)) is SynSent or Established && processes.Contains(BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(process)));

    static int Port(byte[] table, int at) => BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(at));

    static IPAddress Unmapped(ReadOnlySpan<byte> bytes)
    {
        var address = new IPAddress(bytes);
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}
