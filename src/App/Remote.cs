using System.Net.Sockets;
using System.Text;

namespace ArnoA2.App;

/// <summary>
/// Lets a second start of the toolkit hand a command to the one already running, so a desktop
/// shortcut can run <c>arno-a2-toolkit --toggle-bars</c>. Only the user's own runtime folder
/// holds the socket.
/// </summary>
public static class Remote
{
    public const string ToggleBars = "--toggle-bars";

    static string SocketPath() => Path.Combine(Path.GetDirectoryName(ArnoA2.Protocol.CaptureChannel.DefaultPath())!, "app.sock");

    /// <summary>Sends a command; false when no toolkit is listening.</summary>
    public static bool Send(string command)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath()));
            socket.Send(Encoding.UTF8.GetBytes(command + "\n"));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>Takes commands until disposed; each is passed to <paramref name="received"/> on
    /// a background thread.</summary>
    public static IDisposable Listen(Action<string> received)
    {
        string path = SocketPath();
        File.Delete(path);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(4);
        var stop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            var buffer = new byte[64];
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var client = await listener.AcceptAsync(stop.Token);
                    int read = await client.ReceiveAsync(buffer, stop.Token);
                    received(Encoding.UTF8.GetString(buffer, 0, read).Trim());
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                }
            }
        });
        return new Closer(stop, listener, path);
    }

    sealed class Closer(CancellationTokenSource stop, Socket listener, string path) : IDisposable
    {
        public void Dispose()
        {
            stop.Cancel();
            listener.Dispose();
            File.Delete(path);
        }
    }
}
