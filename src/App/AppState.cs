using System.Diagnostics;
using ArnoA2.Meter;
using ArnoA2.Protocol;
using Avalonia.Threading;

namespace ArnoA2.App;

/// <summary>
/// Everything the windows show, in one place: the meter engine and the capture daemon
/// link. Lives on the UI thread; the daemon socket is read on a
/// background task and each record is posted here.
/// </summary>
public sealed class AppState
{
    public Engine Engine { get; }
    public Store Store { get; }
    public string CaptureStatus { get; private set; } = "Starting capture…";
    public bool CaptureProblem { get; private set; }
    public event Action? Changed;
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly CancellationTokenSource stop = new();
    readonly string? daemonPath;
    Process? daemon;
    DateTimeOffset lastSave = DateTimeOffset.UtcNow;

    public AppState(Store store, string? daemonPath)
    {
        Store = store;
        this.daemonPath = daemonPath;
        Engine = new Engine(store);
        tick.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        tick.Start();
        if (daemonPath != null)
            _ = Task.Run(() => LinkLoop(stop.Token));
    }

    public void Stop()
    {
        stop.Cancel();
        tick.Stop();
        Engine.Damage.Finish();
        Engine.SaveIfChanged();
        try
        {
            daemon?.Kill();
        }
        catch (InvalidOperationException)
        {
        }
    }

    void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        Engine.Tick(now);
        if (now - lastSave > TimeSpan.FromSeconds(5))
        {
            lastSave = now;
            try
            {
                Engine.SaveIfChanged();
            }
            catch (IOException ex)
            {
                CaptureStatus = "Could not save settings: " + ex.Message;
            }
        }

        Changed?.Invoke();
    }

    async Task LinkLoop(CancellationToken cancel)
    {
        string socket = CaptureChannel.DefaultPath();
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                using var link = await Connect(socket, cancel);
                Post(() =>
                {
                    CaptureStatus = "Waiting for AION 2…";
                    CaptureProblem = false;
                });
                while (await CaptureChannel.ReadAsync(link, cancel) is { } record)
                    Post(() => Apply(record));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidDataException)
            {
                Post(() =>
                {
                    CaptureStatus = ex.Message;
                    CaptureProblem = true;
                });
            }

            await Task.Delay(2000, cancel).ConfigureAwait(false);
        }
    }

    /// <summary>Connect to a running daemon, or start ours and connect.</summary>
    async Task<Stream> Connect(string socket, CancellationToken cancel)
    {
        try
        {
            return await CaptureChannel.ConnectAsync(socket, cancel);
        }
        catch (System.Net.Sockets.SocketException)
        {
        }

        if (daemon is { HasExited: false })
            daemon.Kill();
        var start = new ProcessStartInfo(daemonPath!) { UseShellExecute = false, RedirectStandardError = true };
        start.ArgumentList.Add("--parent");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        daemon = Process.Start(start) ?? throw new IOException("The capture program did not start.");
        for (int i = 0; i < 50; i++)
        {
            if (daemon.HasExited)
            {
                string error = (await daemon.StandardError.ReadToEndAsync(cancel)).Trim();
                throw new IOException(error.Contains("cap_net_raw", StringComparison.Ordinal) || error.Contains("ermission", StringComparison.Ordinal)
                    ? "Capture needs permission. Open Settings → Capture → Grant permission."
                    : "The capture program stopped: " + error);
            }

            try
            {
                return await CaptureChannel.ConnectAsync(socket, cancel);
            }
            catch (System.Net.Sockets.SocketException)
            {
                await Task.Delay(100, cancel);
            }
        }

        throw new IOException("The capture program did not open its socket.");
    }

    void Apply(ChannelMessage record)
    {
        var now = DateTimeOffset.UtcNow;
        switch (record.Kind)
        {
            case ChannelKind.FlowOpened:
                Engine.Opened(record.Flow, record.Text);
                break;
            case ChannelKind.Data:
                Engine.Data(record.Flow, record.Payload, now);
                break;
            case ChannelKind.Gap:
                Engine.Gap(record.Flow, record.LostBytes, now);
                break;
            case ChannelKind.FlowClosed:
                Engine.Closed(record.Flow, now);
                break;
            case ChannelKind.Status:
                var parts = record.Text.Split('|');
                CaptureStatus = parts.Length > 2 && parts[2] != "0" ? "Capturing the game connection" : "Waiting for AION 2…";
                CaptureProblem = false;
                break;
        }
    }

    static void Post(Action action) => Dispatcher.UIThread.Post(action);

    /// <summary>Grant the capture program its one permission through the desktop's password dialog.</summary>
    public static async Task<string> GrantPermission(string daemonPath)
    {
        var start = new ProcessStartInfo("pkexec") { UseShellExecute = false, RedirectStandardError = true };
        foreach (string arg in new[] { "/usr/bin/setcap", "cap_net_raw=ep", daemonPath })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("pkexec did not start");
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 ? "Permission granted. Capture restarts in a moment." : "Not granted: " + error.Trim();
    }
}
