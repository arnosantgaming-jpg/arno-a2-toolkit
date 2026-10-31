using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ArnoA2.Capture;

/// <summary>Finds Npcap, which the user installs themselves (its licence doesn't let us ship it).</summary>
[SupportedOSPlatform("windows")]
static class Npcap
{
    public const string Missing = "Npcap is not installed. Get it from npcap.com, then start the toolkit again.";

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetDllDirectoryW(string path);

    /// <summary>wpcap.dll by full path, so no other copy can be picked up. It loads Packet.dll
    /// from the same folder, which is why that folder goes on the search path.</summary>
    public static IntPtr Load()
    {
        string folder = Path.Combine(Environment.SystemDirectory, "Npcap");
        SetDllDirectoryW(folder);
        return NativeLibrary.TryLoad(Path.Combine(folder, "wpcap.dll"), out var library) ? library : throw new IOException(Missing);
    }
}

/// <summary>
/// Windows has no "any" device, so each network adapter gets its own capture and reading
/// thread, and their packets meet in one queue. Npcap can't filter on direction either:
/// "received" is packets addressed to the adapter's own addresses.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NpcapCapture : IPacketSource
{
    const string Lib = PacketSource.Lib;

    // PacketSource says where the library is, and these calls can come before any of its own.
    static NpcapCapture() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(PacketSource).TypeHandle);

    [DllImport(Lib)] static extern int pcap_findalldevs(out IntPtr devices, byte[] error);
    [DllImport(Lib)] static extern void pcap_freealldevs(IntPtr devices);

    const uint Loopback = 1;
    const ushort AddressV4 = 2, AddressV6 = 23;

    readonly List<(PacketSource Source, IPAddress[] Addresses)> adapters = new();
    readonly BlockingCollection<Segment> queue = new(boundedCapacity: 16384);
    readonly List<Thread> readers = new();
    volatile bool stopping;

    public bool Offline => false;

    public static NpcapCapture Open()
    {
        var capture = new NpcapCapture();
        string? problem = null;
        foreach (var (name, addresses) in Adapters())
        {
            try
            {
                capture.adapters.Add((PacketSource.Device(name), addresses));
            }
            catch (IOException ex)
            {
                problem = ex.Message; // an adapter that can't be read doesn't stop the others
            }
        }

        if (capture.adapters.Count == 0)
            throw new IOException(problem ?? "No network adapter to capture from.");
        return capture;
    }

    /// <summary>Adapters with an address of their own, without loopback.</summary>
    static List<(string Name, IPAddress[] Addresses)> Adapters()
    {
        var found = new List<(string, IPAddress[])>();
        var error = new byte[256];
        if (pcap_findalldevs(out var list, error) != 0)
            throw new IOException("Could not list network adapters: " + System.Text.Encoding.UTF8.GetString(error).TrimEnd('\0'));
        try
        {
            // pcap_if { next; name; description; addresses; flags }
            for (var device = list; device != IntPtr.Zero; device = Marshal.ReadIntPtr(device))
            {
                uint flags = (uint)Marshal.ReadInt32(device, 4 * IntPtr.Size);
                var addresses = Addresses(Marshal.ReadIntPtr(device, 3 * IntPtr.Size));
                if ((flags & Loopback) == 0 && addresses.Length > 0 && Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(device, IntPtr.Size)) is { } name)
                    found.Add((name, addresses));
            }
        }
        finally
        {
            pcap_freealldevs(list);
        }

        return found;
    }

    // pcap_addr { next; addr; netmask; broadaddr; dstaddr }, addr a sockaddr.
    static IPAddress[] Addresses(IntPtr entry)
    {
        var found = new List<IPAddress>();
        for (; entry != IntPtr.Zero; entry = Marshal.ReadIntPtr(entry))
        {
            var socket = Marshal.ReadIntPtr(entry, IntPtr.Size);
            if (socket == IntPtr.Zero)
                continue;
            ushort family = (ushort)Marshal.ReadInt16(socket);
            int offset = family == AddressV4 ? 4 : family == AddressV6 ? 8 : -1;
            if (offset < 0)
                continue;
            var bytes = new byte[family == AddressV4 ? 4 : 16];
            Marshal.Copy(socket + offset, bytes, 0, bytes.Length);
            found.Add(new IPAddress(bytes));
        }

        return found.Where(a => !a.IsIPv6LinkLocal).ToArray();
    }

    /// <summary>The "dst host" filter for an adapter's addresses.</summary>
    public static string ReceivedBy(IEnumerable<IPAddress> addresses, string expression) => $"{expression} and ({string.Join(" or ", addresses.Select(a => "dst host " + a))})";

    public void FilterInbound(string expression)
    {
        foreach (var (source, addresses) in adapters)
            source.Filter(ReceivedBy(addresses, expression));
        foreach (var (source, _) in adapters)
        {
            var reader = new Thread(() => Read(source)) { IsBackground = true, Name = "adapter reader" };
            readers.Add(reader);
            reader.Start();
        }
    }

    void Read(PacketSource source)
    {
        try
        {
            while (!stopping)
                if (source.Next(out _) is { } segment)
                    queue.TryAdd(segment); // a full queue counts as packet loss; the stream repairs it as a gap
        }
        catch (IOException)
        {
            // The adapter went away; the others keep running.
        }
    }

    public Segment? Next(out bool end)
    {
        end = false;
        return queue.TryTake(out var segment, 100) ? segment : null;
    }

    public void Dispose()
    {
        stopping = true;
        foreach (var reader in readers)
            reader.Join(1000);
        foreach (var (source, _) in adapters)
            source.Dispose();
        queue.Dispose();
    }
}
