using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ArnoA2.App;

/// <summary>What the toolkit answers a phone with.</summary>
public sealed record PhoneReply(int Status, string Type, byte[] Body, bool Keep = false)
{
    public static PhoneReply Text(int status, string text) => new(status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));
}

/// <summary>
/// A very small web server for phones on the same network: one request per connection, and
/// nothing is answered without the private key from the link. The link is sent unencrypted, so
/// it keeps out other devices that don't have it, not someone reading the network's traffic.
/// </summary>
public sealed class PhoneServer : IDisposable
{
    public const int DefaultPort = 8766;
    const int MaxHead = 8192, MaxOpen = 16;
    readonly TcpListener listener;
    readonly byte[] key;
    readonly Func<string, string, IReadOnlyDictionary<string, string>, Task<PhoneReply>> answer;
    readonly CancellationTokenSource stop = new();
    readonly SemaphoreSlim open = new(MaxOpen);

    /// <param name="answer">Takes the method, the path and the query.</param>
    public PhoneServer(string key, Func<string, string, IReadOnlyDictionary<string, string>, Task<PhoneReply>> answer, int port = DefaultPort)
    {
        this.key = Encoding.UTF8.GetBytes(key);
        this.answer = answer;
        listener = new TcpListener(IPAddress.Any, port);
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>Starts listening. Throws <see cref="SocketException"/> when the port is taken.</summary>
    public void Start()
    {
        listener.Start();
        _ = Accept();
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
    }

    /// <summary>A long random key for the link.</summary>
    public static string NewKey() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>This computer's address on the local network.</summary>
    public static string? LocalAddress() => Local()?.Address.ToString();

    /// <summary>The local network, as 192.168.1.0/24.</summary>
    public static string? LocalNetwork() => Local() is { } local ? Network(local.Address, local.PrefixLength) : null;

    public static string Network(IPAddress address, int prefix)
    {
        var bytes = address.GetAddressBytes();
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] &= (byte)(prefix >= (i + 1) * 8 ? 255 : prefix <= i * 8 ? 0 : 255 << (8 - prefix % 8));
        return $"{new IPAddress(bytes)}/{prefix}";
    }

    // The adapter with a gateway.
    static UnicastIPAddressInformation? Local() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .Select(n => n.GetIPProperties())
        .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
        .SelectMany(p => p.UnicastAddresses)
        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

    async Task Accept()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                await open.WaitAsync(stop.Token);
                client = await listener.AcceptTcpClientAsync(stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Serve(client);
        }
    }

    async Task Serve(TcpClient client)
    {
        using var slow = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        slow.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var reply = await Handle(await ReadHead(stream, slow.Token));
                await Write(stream, reply, slow.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            open.Release();
        }
    }

    static async Task<string?> ReadHead(NetworkStream stream, CancellationToken token)
    {
        var head = new byte[MaxHead];
        int length = 0;
        while (length < head.Length)
        {
            int read = await stream.ReadAsync(head.AsMemory(length), token);
            if (read == 0)
                return null;
            length += read;
            var text = Encoding.ASCII.GetString(head, 0, length);
            int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0)
                return text[..end];
        }

        return null;
    }

    async Task<PhoneReply> Handle(string? head)
    {
        var parts = head?.Split("\r\n")[0].Split(' ');
        if (parts is not { Length: 3 })
            return PhoneReply.Text(400, "Bad request");
        var (method, target) = (parts[0], parts[1]);
        int mark = target.IndexOf('?');
        string path = mark < 0 ? target : target[..mark];
        var query = Query(mark < 0 ? "" : target[(mark + 1)..]);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(query.GetValueOrDefault("k", "")), key))
            return PhoneReply.Text(403, "This link is out of date. Scan the code in the toolkit again.");
        if (method is not ("GET" or "POST"))
            return PhoneReply.Text(405, "Method not allowed");
        try
        {
            return await answer(method, path, query);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PhoneReply.Text(500, "The toolkit could not answer.");
        }
    }

    static Dictionary<string, string> Query(string text)
    {
        var query = new Dictionary<string, string>();
        foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0)
                query[pair[..equals]] = Uri.UnescapeDataString(pair[(equals + 1)..]);
        }

        return query;
    }

    static async Task Write(NetworkStream stream, PhoneReply reply, CancellationToken token)
    {
        string reason = reply.Status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Internal Server Error" };
        string head = $"HTTP/1.1 {reply.Status} {reason}\r\nContent-Type: {reply.Type}\r\nContent-Length: {reply.Body.Length}\r\nCache-Control: {(reply.Keep ? "private, max-age=86400" : "no-store")}\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), token);
        await stream.WriteAsync(reply.Body, token);
    }
}
