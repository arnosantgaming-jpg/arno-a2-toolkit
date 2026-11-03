using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using ArnoA2.App.Map;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using QRCoder;

namespace ArnoA2.App;

/// <summary>The phone map: a page any phone on the network can open while this is on, showing
/// the shared map, with its own choice of tools, and buttons for the toolkit. It controls only
/// the toolkit: nothing it does reaches the game. The position is followed only while a phone
/// is looking, since that reads the game's minimap.</summary>
public sealed class PhoneBoard
{
    const int LookingSeconds = 5;
    readonly MapHub hub;
    readonly (string Id, string Name, MapLayer Layer)[] layers;
    readonly CubeLayer cubes;
    readonly Action<string> saveKey;
    static readonly TimeSpan AlertShown = TimeSpan.FromSeconds(15);
    (string Text, int Id, DateTimeOffset At) alert = ("", 0, DateTimeOffset.MinValue);
    readonly DispatcherTimer watch = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Dictionary<MapLayer, (IReadOnlyList<MapDot> Dots, int Revision)> revisions = new();
    string key;
    PhoneServer? server;
    CameraHeading? camera;
    long lastLook;
    bool following;

    public PhoneBoard(MapHub hub, PetLayer pets, ResourceLayer nodes, CubeLayer cubes, string key, Action<string> saveKey)
    {
        this.hub = hub;
        this.cubes = cubes;
        this.key = key;
        this.saveKey = saveKey;
        layers = new (string, string, MapLayer)[] { ("pets", "Pets", pets), ("nodes", "Resources", nodes), ("cubes", "Cubes", cubes) };
        watch.Tick += (_, _) => Watch();
    }

    public bool Running => server != null;
    public string? Problem { get; private set; }
    public int Port => server?.Port ?? PhoneServer.DefaultPort;
    /// <summary>The private link to open on a phone, null while off or without a network.</summary>
    public string? Link => server != null && PhoneServer.LocalAddress() is { } address ? $"http://{address}:{Port}/?k={key}" : null;

    public bool Start(int port = PhoneServer.DefaultPort)
    {
        if (server != null)
            return true;
        if (key.Length == 0)
            saveKey(key = PhoneServer.NewKey());
        var starting = new PhoneServer(key, Answer, port);
        try
        {
            starting.Start();
        }
        catch (SocketException)
        {
            starting.Dispose();
            Problem = $"Port {port} is in use by something else.";
            return false;
        }

        (server, Problem) = (starting, null);
        watch.Start();
        return true;
    }

    public void Stop()
    {
        watch.Stop();
        server?.Dispose();
        server = null;
        Follow(false);
    }

    /// <summary>Replaces the link, so a phone that had the old one can't use it any more.</summary>
    public void NewLink()
    {
        bool running = Running;
        Stop();
        saveKey(key = PhoneServer.NewKey());
        if (running)
            Start();
    }

    /// <summary>The link as a QR code, null without one.</summary>
    public Bitmap? Code()
    {
        if (Link is not { } link)
            return null;
        using var data = new QRCodeGenerator().CreateQrCode(link, QRCodeGenerator.ECCLevel.M);
        using var image = new MemoryStream(new PngByteQRCode(data).GetGraphic(8));
        return new Bitmap(image);
    }

    void Watch() => Follow(Environment.TickCount64 - Volatile.Read(ref lastLook) < LookingSeconds * 1000);

    void Follow(bool looking)
    {
        if (looking == following)
            return;
        following = looking;
        if (looking)
        {
            hub.Attach();
            camera = new CameraHeading();
        }
        else
        {
            hub.Detach();
            camera?.Dispose();
            camera = null;
        }
    }

    async Task<PhoneReply> Answer(string method, string path, IReadOnlyDictionary<string, string> query)
    {
        if (method == "POST")
            return path == "/do" ? await Dispatcher.UIThread.InvokeAsync(() => Do(query)) : PhoneReply.Text(404, "Not found");
        switch (path)
        {
            case "/":
                return new PhoneReply(200, "text/html; charset=utf-8", Resource("phone.html"));
            case "/keep-awake.mp4":
                return new PhoneReply(200, "video/mp4", Resource("keep-awake.mp4"), Keep: true);
            case "/state":
                Volatile.Write(ref lastLook, Environment.TickCount64);
                return Json(await Dispatcher.UIThread.InvokeAsync(State));
            case "/dots":
                return Json(await Dispatcher.UIThread.InvokeAsync(() => Dots(query.GetValueOrDefault("layer", ""))));
            case "/tile":
                return Tile(query);
            default:
                return PhoneReply.Text(404, "Not found");
        }
    }

    static byte[] Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    static PhoneReply Json(object value) => new(200, "application/json", JsonSerializer.SerializeToUtf8Bytes(value));

    // The region is checked and the tile numbers are numbers, so a request can't leave the maps folder.
    static PhoneReply Tile(IReadOnlyDictionary<string, string> query)
    {
        if (!WorldMaps.All.ContainsKey(query.GetValueOrDefault("region", "")) || !int.TryParse(query.GetValueOrDefault("c"), out int column) || !int.TryParse(query.GetValueOrDefault("r"), out int row))
            return PhoneReply.Text(404, "Not found");
        string file = Path.Combine(AppContext.BaseDirectory, "maps", query["region"], $"{column}_{row}.jpg");
        return File.Exists(file) ? new PhoneReply(200, "image/jpeg", File.ReadAllBytes(file), Keep: true) : PhoneReply.Text(404, "Not found");
    }

    static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    static string Hex(IBrush brush) => brush is ISolidColorBrush solid ? Hex(solid.Color) : "#ffffff";

    int Revision(MapLayer layer)
    {
        var (dots, revision) = revisions.GetValueOrDefault(layer);
        if (!ReferenceEquals(dots, layer.Dots))
            revisions[layer] = (layer.Dots, ++revision);
        return revision;
    }

    /// <summary>Shows a note on the phone for a while, and buzzes it.</summary>
    public void Alert(string text) => alert = (text, alert.Id + 1, DateTimeOffset.UtcNow);

    /// <summary>What the phone shows right now. Dots go separately, since they rarely change.</summary>
    public object State()
    {
        var map = hub.Map;
        var pets = layers[0].Layer;
        var mobs = hub.Live is { } live && live.Region == hub.Region
            ? live.Mobs.Select(m =>
            {
                var (x, y) = map.Normalize(m.X, m.Y);
                bool wanted = pets.WantsMob(m.PetId);
                return new object[] { x, y, wanted, wanted ? Hex(MapCanvas.FamilyBrush(m.PetId)) : "#90a4ae" };
            }).ToArray()
            : Array.Empty<object[]>();
        return new
        {
            region = hub.Region,
            grid = map.Grid,
            background = map.Background,
            metres = map.Metres,
            radius = FarmingHotspots.RadiusMetres / map.Metres,
            tiles = hub.Spawns.Tiles.Select(t => new[] { t.Column, t.Row }).ToArray(),
            player = hub.Player is { } p ? new[] { p.X, p.Y } : null,
            precise = hub.Precise,
            heading = camera?.Degrees,
            problem = hub.Error,
            alert = DateTimeOffset.UtcNow - alert.At < AlertShown ? new { id = alert.Id, text = alert.Text } : null,
            mobs,
            layers = layers.Select(l => new
            {
                id = l.Id,
                name = l.Name,
                color = Hex(l.Layer.Colors.Accent),
                spot = Hex(l.Layer.Colors.Spot),
                revision = Revision(l.Layer),
                target = l.Layer.TargetSpot,
                spots = l.Layer.Hotspots.Select(s => new[] { s.X, s.Y }).ToArray(),
                goal = l.Layer.Target is { } goal && (goal.DistanceMetres ?? 0) >= FarmingHotspots.RadiusMetres,
                guidance = l.Layer.Guidance()
            }).ToArray()
        };
    }

    object Dots(string id)
    {
        var layer = layers.FirstOrDefault(l => l.Id == id).Layer;
        return (layer?.Dots ?? Array.Empty<MapDot>()).Select(d => new object[] { Math.Round(d.X, 5), Math.Round(d.Y, 5), Hex(d.Brush), d.Shape.ToString(), d.Learned }).ToArray();
    }

    /// <summary>One button press on the phone.</summary>
    public PhoneReply Do(IReadOnlyDictionary<string, string> query)
    {
        var layer = layers.FirstOrDefault(l => l.Id == query.GetValueOrDefault("layer")).Layer;
        switch (query.GetValueOrDefault("a"))
        {
            case "target" when layer != null && int.TryParse(query.GetValueOrDefault("i"), out int spot) && spot >= 0 && spot < layer.Hotspots.Count:
                layer.TargetSpot = spot;
                break;
            case "next" when layer is { Hotspots.Count: > 0 }:
                layer.TargetSpot = (layer.TargetSpot + 1) % layer.Hotspots.Count;
                break;
            case "check" when cubes.Target is { } target:
                cubes.Check(target);
                break;
            case "forget":
                cubes.ClearChecked();
                break;
            default:
                return PhoneReply.Text(400, "Unknown action");
        }

        return PhoneReply.Text(200, "Done");
    }
}

/// <summary>The Phone Map drawer: the switch, and the code to scan while it's on.</summary>
public sealed class PhoneSettings : UserControl
{
    readonly PhoneBoard phone;
    readonly Image code = new() { Width = 220, Height = 220, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    readonly SelectableTextBlock link = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock status = Ui.Note("");
    readonly Control newLink;
    readonly Button firewall;
    readonly string? firewallName = Desktop.Firewall;
    string? firewallNote;

    public PhoneSettings(PhoneBoard phone, Action<bool> remember)
    {
        this.phone = phone;
        newLink = Ui.Button("Make a new link", () =>
        {
            phone.NewLink();
            Update();
        }, "The old link and any phone using it stop working");
        firewall = Ui.Button("Allow phones through the firewall", AllowPhones, "Asks for your password, then opens this one port to your home network only");
        Content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.Toggle("Open the map to phones on this network", phone.Running, on =>
                {
                    if (on)
                        phone.Start();
                    else
                        phone.Stop();
                    remember(phone.Running);
                    Update();
                }),
                status, code, link, firewall, newLink,
                Ui.Note("Scan the code with a phone on the same Wi-Fi. The phone shows your position and map, and can change the toolkit's targets. It can't touch the game. Anyone with the link on your network can do the same, and it isn't encrypted, so use this at home rather than on public Wi-Fi.")
            }
        };
        Update();
    }

    void Update()
    {
        string? url = phone.Link;
        code.Source = phone.Code();
        code.IsVisible = link.IsVisible = url != null;
        newLink.IsVisible = phone.Running;
        firewall.IsVisible = phone.Running && url != null && firewallName != null && !Desktop.FirewallAllows;
        link.Text = url;
        status.Text = phone.Problem ?? (!phone.Running ? "Off." : url == null ? "No network found." : firewallNote ?? (firewallName != null ? $"On. {firewallName} is running: if the phone can't open the page, allow it below." : "On."));
    }

    async void AllowPhones()
    {
        if (PhoneServer.LocalNetwork() is not { } network)
            return;
        firewall.IsEnabled = false;
        firewallNote = "Waiting for the password dialog…";
        Update();
        string? problem = await Desktop.AllowThroughFirewall(network, phone.Port);
        firewallNote = problem ?? $"On. {firewallName} now lets phones on {network} reach port {phone.Port}.";
        firewall.IsEnabled = true;
        Update();
    }
}
