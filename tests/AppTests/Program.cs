using ArnoA2.App;
using ArnoA2.Protocol;
using ArnoA2.Tracker;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

// Renders every window with a synthetic session and checks the basics. Usage: AppTests <output folder>
string output = Path.GetFullPath(args.Length > 0 ? args[0] : "test-results/app");
Directory.CreateDirectory(output);
string data = Path.Combine(output, "data");
if (Directory.Exists(data))
    Directory.Delete(data, true);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string what)
{
    checks++;
    if (!ok)
        throw new Exception("FAILED: " + what);
}

string Text(Control c) => string.Join("\n", c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
void Save(Window w, string name)
{
    for (int i = 0; i < 3; i++)
        Dispatcher.UIThread.RunJobs();
    using var frame = w.CaptureRenderedFrame() ?? throw new Exception("no frame");
    frame.Save(Path.Combine(output, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
}

var game = GameData.Instance;
int dratona = game.Pets.Values.Single(p => p.Name == "Dratona").Id;
int spirit = game.Pets.Values.Single(p => p.Name == "Ancient Spirit").Id;
var store = new Store(data);
var state = new AppState(store, null, daemonPath: null);
var now = DateTimeOffset.UtcNow;
// A login: collection, character, a few rewards, a position in Verteron near Eastern Cantas Valley.
var engine = state.Engine;
var collection = new CollectionSnapshot(0, new Dictionary<int, int> { [dratona] = 2, [spirit] = 1 }, new Dictionary<int, int> { [dratona] = 40, [spirit] = 22 });
engine.Collection.Apply(collection, now);
foreach (var (pet, souls) in new[] { (dratona, 3), (spirit, 2) })
{
    engine.Collection.Gain(pet, souls);
    engine.Recent.Touch(pet, "", now, true);
    engine.Session.Souls(pet, souls, now);
}

state.Positions.Observe(new ZoneEntered(1010, -72190f, -69614f), null, now);
var maps = new MapHub(state, data);
var petMap = new PetLayer(state, maps);
var tracker = new TrackerWindow(state, petMap);
tracker.Show();
Save(tracker, "tracker.png");
Check(Text(tracker).Contains("Dratona") && Text(tracker).Contains("43/75") && Text(tracker).Contains("Session: 5 souls") && Text(tracker).Contains("Cogni"), "tracker lists recent pets with type, souls and the session");

var finder = new Window { Width = 640, Height = 760, Content = new FinderPanel(state, petMap, () => { }) };
finder.Show();
finder.GetVisualDescendants().OfType<TextBox>().First().Text = "Dratona";
Dispatcher.UIThread.RunJobs();
Save(finder, "finder.png");
Check(Text(finder).Contains("Souls from:") && Text(finder).Contains("3 pets"), "finder searches and shows sources");

string home = Path.Combine(output, "adopt");
if (Directory.Exists(home))
    Directory.Delete(home, true);
Directory.CreateDirectory(Path.Combine(home, "arno-dps-meter", "history"));
File.WriteAllText(Path.Combine(home, "arno-dps-meter", "history", "boss-1.json"), "{}");
AppState.Adopt(home);
Check(File.Exists(Path.Combine(home, "arno-a2-toolkit", "meter", "history", "boss-1.json")) && File.Exists(Path.Combine(home, "arno-dps-meter", "history", "boss-1.json")), "first run copies saved data and leaves the old apps theirs");
File.WriteAllText(Path.Combine(home, "arno-a2-toolkit", "meter", "history", "boss-1.json"), "newer");
AppState.Adopt(home);
Check(File.ReadAllText(Path.Combine(home, "arno-a2-toolkit", "meter", "history", "boss-1.json")) == "newer", "later starts do not copy over the toolkit's data");

var hub = new HubWindow(state, maps);
hub.Show();
Save(hub, "hub.png");
Check(Text(hub).Contains("DPS Meter") && Text(hub).Contains("Pet Tracker") && Text(hub).Contains("Hidden Cube Tracker") && !Text(hub).Contains("Coming soon"), "the main window lists the tools");
Check(Text(hub).Contains("Server Ping"), "the ping overlay has a switch");
var cubeFile = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../data/CubeSpots.json"))).RootElement;
var firstRegion = cubeFile.EnumerateObject().First();
var firstGroup = firstRegion.Value[0];
float cubeX = (float)firstGroup.GetProperty("Spots")[0][0].GetDouble(), cubeY = (float)firstGroup.GetProperty("Spots")[0][1].GetDouble();
byte[] CubeBody(int size, int at, float x, float y)
{
    var body = new byte[size];
    BitConverter.TryWriteBytes(body.AsSpan(at), x);
    BitConverter.TryWriteBytes(body.AsSpan(at + 4), y);
    return body;
}

var cubeWatch = new CubeWatch();
var cubeTime = DateTimeOffset.UnixEpoch;
var sighting = cubeWatch.See(new(0x34, 0x36, CubeBody(55, 9, cubeX, cubeY)), cubeTime);
Check(sighting != null && sighting.Region == firstRegion.Name && sighting.Area == firstGroup.GetProperty("Area").GetString(), "a cube message on a cube spot is a sighting");
Check(cubeWatch.See(new(0x34, 0x36, CubeBody(55, 9, cubeX, cubeY)), cubeTime.AddSeconds(8)) == null, "the same cube alerts once");
Check(cubeWatch.See(new(0x34, 0x36, CubeBody(55, 9, cubeX, cubeY)), cubeTime.AddMinutes(2)) != null, "a later cube on that spot alerts again");
var otherWatch = new CubeWatch();
Check(otherWatch.See(new(0x1E, 0x56, CubeBody(55, 9, cubeX, cubeY)), cubeTime) == null, "another message kind is not a sighting");
Check(otherWatch.See(new(0x34, 0x36, CubeBody(59, 9, cubeX, cubeY)), cubeTime) == null, "another size is not a sighting");
Check(otherWatch.See(new(0x34, 0x36, CubeBody(55, 5, cubeX, cubeY)), cubeTime) == null, "a position elsewhere in the message is not a sighting");
Check(otherWatch.See(new(0x34, 0x36, CubeBody(55, 9, cubeX + 200, cubeY)), cubeTime) == null, "something 2 m from a spot is not a cube");
Check(HubWindow.CubeText("Owllau", 12.4) == "Hidden Cube: Owllau, 12 m away" && HubWindow.CubeText("Owllau", null) == "Hidden Cube: Owllau", "the alert says where the cube is");
var cubeNote = new AlertWindow("Hidden Cube: Owllau, 12 m away", ToolTheme.Cubes);
cubeNote.Show();
Dispatcher.UIThread.RunJobs();
Check(cubeNote.Title == KdeWindowRules.AlertTitle && cubeNote.Topmost && Text(cubeNote).Contains("Owllau"), "the alert stays on top");
Save(cubeNote, "cube-alert.png");
cubeNote.Close();
// Timed events: server clock GMT+9 (Taiwan GMT+8); 12:00 UTC is 21:00 on Global.
var events = new EventAlerts { Shugo = true, AirRaid = true, Rift = true, Minutes = 2 };
var noon = DateTimeOffset.Parse("2026-11-06T12:00:00Z");
Check(GameEvents.StartingAt(noon, events).SequenceEqual(new[] { GameEvents.Shugo, GameEvents.Rift }), "21:00 on Global has the Shugo Festival and a rift");
Check(GameEvents.StartingAt(noon.AddMinutes(30), events).SequenceEqual(new[] { GameEvents.AirRaid }), "half past has the Air Raid");
Check(!GameEvents.StartingAt(noon.AddHours(1), events).Contains(GameEvents.Rift), "rifts are three hours apart");
Check(GameEvents.StartingAt(noon.AddHours(2), new EventAlerts { Rift = true, Server = "Korea" }).Contains(GameEvents.Rift)
    && !GameEvents.StartingAt(noon, new EventAlerts { Rift = true, Server = "Korea" }).Any(), "Korea's rifts open two hours after Global's");
Check(GameEvents.StartingAt(noon.AddHours(3), new EventAlerts { Rift = true, Server = "Taiwan" }).Contains(GameEvents.Rift), "Taiwan's rifts follow its own clock, an hour behind Korea's");
Check(!GameEvents.StartingAt(noon, new EventAlerts { Rift = true }).Contains(GameEvents.Shugo), "only the chosen events alert");
var due = GameEvents.Due(noon.AddMinutes(-2).AddSeconds(-1), noon.AddMinutes(-2), events);
Check(due is [{ Names: [GameEvents.Shugo, GameEvents.Rift] }] && due[0].Start == noon, "the alert comes the chosen minutes before");
Check(GameEvents.Due(noon.AddMinutes(-2), noon.AddMinutes(-2).AddSeconds(1), events).Count == 0, "and only once");
Check(GameEvents.Due(noon.AddHours(-5), noon.AddMinutes(10), events).Count == 0, "after a long sleep old events are not announced");
Check(GameEvents.Next(GameEvents.AirRaid, noon.AddMinutes(1), events) == noon.AddMinutes(30) && GameEvents.Next(GameEvents.Shugo, noon, events) == noon, "the next start is found");
Check(GameEvents.Text(new[] { GameEvents.Shugo, GameEvents.Rift }, 2) == "Shugo Festival and Spacetime Rift in 2 min" && GameEvents.Text(new[] { GameEvents.AirRaid }, 0) == "Air Raid starts now", "the alert says what starts when");
var eventPanel = new Window { Width = 620, Height = 520, Content = new EventSettingsPanel(events, () => { }) };
eventPanel.Show();
Dispatcher.UIThread.RunJobs();
Check(Text(eventPanel).Contains("Spacetime Rift") && Text(eventPanel).Contains("Next, on this PC's clock"), "the event alerts page lists the events and when they're next");
Save(eventPanel, "event-alerts.png");
eventPanel.Close();
hub.OpenPing();
var pingWindow = new PingWindow(state, null, null, (_, _) => { });
pingWindow.Show();
Dispatcher.UIThread.RunJobs();
Check(pingWindow.Title == KdeWindowRules.PingTitle && Text(pingWindow).Contains("PING") && Text(pingWindow).Contains("—"), "the ping overlay waits for a reading");
Save(pingWindow, "ping.png");
pingWindow.Close();
Check(PingWindow.Reading(42) == "42 ms" && PingWindow.Reading(null) == "—", "the ping reads in milliseconds");
Check(PingWindow.Tint(79) != PingWindow.Tint(80) && PingWindow.Tint(149) != PingWindow.Tint(150) && PingWindow.Tint(null) != PingWindow.Tint(1), "the ping is coloured by how good it is");
hub.OpenPets();
hub.OpenMeter();
Dispatcher.UIThread.RunJobs();
Save(hub, "hub-launched.png");
Check(Text(hub).Contains("Find a Pet") && Text(hub).Contains("Mini-map"), "a launched tool shows its buttons on its card");
Check(UpdateCheck.Newer("{\"tag_name\":\"v0.4.0\"}", "0.3.0") is { Version: "0.4.0", Page: "https://github.com/arnosantgaming-jpg/arno-a2-toolkit/releases/tag/v0.4.0" }, "a newer release is offered");
Check(UpdateCheck.Newer("{\"tag_name\":\"v0.3.0\"}", "0.3.0") == null && UpdateCheck.Newer("{\"tag_name\":\"v0.2.9\"}", "0.3.0") == null, "the same or an older release is not offered");
Check(UpdateCheck.Newer("{\"tag_name\":\"v1.0.0/../../evil\"}", "0.3.0") == null && UpdateCheck.Newer("{\"message\":\"Not Found\"}", "0.3.0") == null && UpdateCheck.Newer("[]", "0.3.0") == null && UpdateCheck.Newer("<html>", "0.3.0") == null, "odd answers are ignored");
{
    string folder = Path.Combine(data, "update-test");
    Directory.CreateDirectory(folder);
    string exe = Path.Combine(folder, "app.exe"), incoming = exe + ".new";
    File.WriteAllText(exe, "old");
    File.WriteAllText(incoming, "new");
    File.WriteAllText(exe + ".old", "older");
    SelfUpdate.Swap(exe, incoming);
    Check(File.ReadAllText(exe) == "new" && File.ReadAllText(exe + ".old") == "old" && !File.Exists(incoming), "an update swaps the exe and keeps the old one aside");
    bool refused = false;
    try
    {
        SelfUpdate.Swap(exe, incoming);
    }
    catch (FileNotFoundException)
    {
        refused = true;
    }

    Check(refused && File.ReadAllText(exe) == "new", "a failed swap leaves the working exe in place");
    string sum = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("new")));
    Check(SelfUpdate.Checksum(sum.ToUpperInvariant() + "  app.exe\n") == sum && SelfUpdate.Checksum("not found") == null && SelfUpdate.Checksum("") == null, "a checksum line is read, and anything else refused");
    File.WriteAllText(incoming, "tampered");
    Check(SelfUpdate.Matches(exe, sum) && !SelfUpdate.Matches(incoming, sum), "a download must match its checksum");
    Check(new AvailableUpdate("1.2.3", "https://github.com/a/b/releases/tag/v1.2.3").Downloads == "https://github.com/a/b/releases/download/v1.2.3", "release files are found from the release page");
    Check(!SelfUpdate.Possible, "the plain Linux build doesn't update itself");
}

Check(!Text(hub).Contains("Update to v"), "no update prompt until a newer release is found");
hub.OfferUpdate(new("9.9.9", "https://github.com/arnosantgaming-jpg/arno-a2-toolkit/releases/tag/v9.9.9"));
Dispatcher.UIThread.RunJobs();
Check(Text(hub).Contains("Update to v9.9.9"), "a newer release shows the update prompt");
Save(hub, "hub-update.png");
hub.ShowDrawer(HubWindow.PhoneDrawer);
for (int i = 0; i < 5; i++)
    Dispatcher.UIThread.RunJobs();
Check(Text(hub).Contains("Open the map to phones"), "the menu's Phone Map opens the phone switch");
Save(hub, "hub-phone-drawer.png");
hub.ShowDrawer(HubWindow.MeterDrawer);
Thread.Sleep(400);
Save(hub, "hub-meter-drawer.png");
Check(Text(hub).Contains("Background opacity"), "the meter's settings open in a drawer");
hub.ShowDrawer(HubWindow.MapDrawer);
Thread.Sleep(400);
Save(hub, "hub-map-drawer.png");
Check(Text(hub).Contains("Open in Window") && Text(hub).Contains("Best spots"), "the map opens in a drawer");
hub.ShowDrawer(HubWindow.FinderDrawer);
Thread.Sleep(400);
Save(hub, "hub-pets-drawer.png");
Check(Text(hub).Contains("pets"), "the finder opens in a drawer");
hub.OpenResources();
Dispatcher.UIThread.RunJobs();
Check(Text(hub).Contains("Resource Tracker") && Text(hub).Contains("Hidden Cube Tracker"), "the Resource Tracker launches from its card");
hub.ShowDrawer(HubWindow.EventsDrawer);
Thread.Sleep(400);
Save(hub, "hub-events-drawer.png");
Check(Text(hub).Contains("Event Alerts") && Text(hub).Contains("Shugo Festival"), "the event alerts open in a drawer from the menu");
hub.ShowDrawer(HubWindow.ResourceDrawer);
Thread.Sleep(400);
Save(hub, "hub-resource-drawer.png");
Check(Text(hub).Contains("Resource Tracker Settings") && Text(hub).Contains("Shellfish") && Text(hub).Contains("Mini-map"), "the Resource Tracker's settings open in a drawer");
hub.ShowDrawer(HubWindow.ResourceMapDrawer);
Thread.Sleep(400);
Save(hub, "hub-resource-map.png");
Check(Text(hub).Contains("Gather") && Text(hub).Contains("Best spots") && Text(hub).Contains("nodes"), "the resource map opens in a drawer with ranked nodes");
hub.OpenCubes();
Dispatcher.UIThread.RunJobs();
hub.ShowDrawer(HubWindow.CubeDrawer);
Thread.Sleep(400);
Save(hub, "hub-cube-drawer.png");
Check(Text(hub).Contains("Hidden Cube Tracker Settings") && Text(hub).Contains("Skip checked spots"), "the Hidden Cube Tracker's settings open in a drawer");
hub.ShowDrawer(HubWindow.CubeMapDrawer);
Thread.Sleep(400);
Save(hub, "hub-cube-map.png");
Check(Text(hub).Contains("Best spots") && Text(hub).Contains("spots"), "the cube map opens in a drawer with ranked spots");
hub.Close();

foreach (var key in ArnoA2.App.Map.WorldMaps.Order)
{
    var nodes = ArnoA2.App.Map.ResourceAtlas.Region(key);
    var worldMap = ArnoA2.App.Map.WorldMaps.All[key];
    var tiles = ArnoA2.App.Map.SpawnAtlas.Region(key).Tiles.Select(t => (t.Column, t.Row)).ToHashSet();
    int inside = nodes.Count(n => tiles.Contains(((int)(n.X * worldMap.Grid), (int)(n.Y * worldMap.Grid))));
    Check(nodes.Count > 200 && inside == nodes.Count, $"every {key} node lands on a map tile ({nodes.Count})");
}
Check(ArnoA2.App.Map.ResourceAtlas.Names.All(n => ArnoA2.App.Map.ResourceAtlas.Kinds.Any(k => k.Kind == n.Kind)), "every node name has a kind");

Directory.CreateDirectory(data);
var resources = new ResourceLayer(maps, data);
var gather = new ResourceWindow(resources);
gather.Show();
Dispatcher.UIThread.RunJobs();
maps.Accept(state.Positions.View(now));
Dispatcher.UIThread.RunJobs();
Save(gather, "resources.png");
Check(Text(gather).Contains("Odyle") && Text(gather).Contains("Shellfish") && Text(gather).Contains(" m "), "the Resource Tracker lists each kind with the closest node");
Check(resources.Hotspots.Count > 0 && resources.Target != null && resources.Guidance().Contains(" m "), "the Resource Tracker ranks nearby spots");
resources.Choose("Odyle", false);
Check(!resources.Shows("Odyle") && resources.Dots.Count > 0 && resources.Dots.Count < ArnoA2.App.Map.ResourceAtlas.Region("verteron").Count, "hiding a kind takes its dots off the map");
foreach (var kind in ArnoA2.App.Map.ResourceAtlas.Kinds.Select(k => k.Kind))
    resources.Choose(kind, false);
Check(resources.Hotspots.Count == 0 && resources.Guidance() == "Choose something to gather", "with nothing chosen the tool says so");
foreach (var kind in ArnoA2.App.Map.ResourceAtlas.Kinds.Select(k => k.Kind))
    resources.Choose(kind, true);
Check(resources.Dots.Select(d => d.Shape).Distinct().Count() == 7, "each kind of resource has its marker shape");
Check(petMap.Dots.All(d => d.Shape == MarkerShape.Paw), "pet spawns draw as paws");
Check(maps.Mini.Active.All(l => l != resources), "launching a tool doesn't put it on the mini-map");
if (maps.Mini.Active.Count == 0)
    tracker.ToggleMiniMap();
gather.ToggleMiniMap();
gather.MiniMap!.StopCamera();
Dispatcher.UIThread.RunJobs();
Thread.Sleep(100);
Save(gather.MiniMap, "resource-minimap.png");
Check(maps.Mini.Active.Count == 2 && maps.Mini.Active[0] == petMap && maps.Mini.Active[1] == resources, "the pet tracker and the resource tracker share one mini-map, blue ring first");
var shared = gather.MiniMap;
gather.Close();
Dispatcher.UIThread.RunJobs();
Check(maps.Mini.Window == shared && maps.Mini.Active.Count == 1, "the mini-map stays for the tool still running");
tracker.Close();
Dispatcher.UIThread.RunJobs();
Check(gather.MiniMap == null && File.Exists(Path.Combine(data, "resource-settings.json")), "closing the tool closes its mini-map and saves");
foreach (var (key, count) in new[] { ("verteron", 1440), ("altgard", 1371) })
{
    var cubeSpots = ArnoA2.App.Map.CubeAtlas.Region(key);
    var worldMap = ArnoA2.App.Map.WorldMaps.All[key];
    var tiles = ArnoA2.App.Map.SpawnAtlas.Region(key).Tiles.Select(t => (t.Column, t.Row)).ToHashSet();
    Check(cubeSpots.Count == count && cubeSpots.All(c => tiles.Contains(((int)(c.X * worldMap.Grid), (int)(c.Y * worldMap.Grid)))), $"every {key} cube spot lands on a map tile ({cubeSpots.Count})");
}
Check(ArnoA2.App.Map.CubeAtlas.Region("abyss").Count == 0, "the Abyss has no cube data");

maps.SetRegion("verteron");
var tracked = new TrackerWindow(state, petMap);
tracked.Show();
var gathering = new ResourceWindow(resources);
gathering.Show();
var cubeMap = new CubeLayer(maps, data);
var seek = new CubeWindow(cubeMap);
seek.Show();
Dispatcher.UIThread.RunJobs();
Check(maps.Mini.Window == null, "launching tools doesn't open the mini-map");
maps.Accept(state.Positions.View(now));
maps.SetRegion("verteron");
Dispatcher.UIThread.RunJobs();
Save(seek, "cubes.png");
Check(cubeMap.Hotspots.Count > 0 && cubeMap.Dots.All(d => d.Shape == MarkerShape.Cube) && Text(seek).Contains(" m "), "the Hidden Cube Tracker ranks spots with distances");
Check(cubeMap.Names(cubeMap.Hotspots[0].Ids).Length > 0 && !cubeMap.Names(cubeMap.Hotspots[0].Ids).Contains("Hidden"), "spots are named by their area");
var checkedSpot = cubeMap.Hotspots[0];
cubeMap.Check(checkedSpot);
Check(cubeMap.CheckedCount > 0 && !cubeMap.Hotspots.Any(h => Math.Abs(h.X - checkedSpot.X) < 1e-9 && Math.Abs(h.Y - checkedSpot.Y) < 1e-9), "a checked spot leaves the ranking");
cubeMap.Preferences.ShowChecked = false;
cubeMap.Redraw();
Check(cubeMap.Dots.Count == ArnoA2.App.Map.CubeAtlas.Region("verteron").Count - cubeMap.CheckedCount, "checked spots can be hidden from the map");
cubeMap.Preferences.ShowChecked = true;
cubeMap.ClearChecked();
Check(cubeMap.CheckedCount == 0, "checked spots can be forgotten");
tracked.ToggleMiniMap();
gathering.ToggleMiniMap();
seek.ToggleMiniMap();
seek.MiniMap!.StopCamera();
Dispatcher.UIThread.RunJobs();
Thread.Sleep(100);
Save(seek.MiniMap, "cube-minimap.png");
Check(maps.Mini.Active.Select(l => l.Ring).SequenceEqual(new[] { 0, 1, 2 }), "all three tools share the mini-map, blue, green, purple");
gathering.ToggleMiniMap();
Check(maps.Mini.Active.Select(l => l.Ring).SequenceEqual(new[] { 0, 2 }), "a tool's Mini-map button takes it off again");
seek.Close();
gathering.Close();
tracked.Close();
Dispatcher.UIThread.RunJobs();
Check(maps.Mini.Window == null, "the mini-map closes with the last tool");

maps.Preferences.LearnSpawns = true;
maps.ForgetLearned();
var soldier = ArnoA2.App.Map.SpawnAtlas.Region("verteron").Points[0];
var verteronMap = ArnoA2.App.Map.WorldMaps.All["verteron"];
var newPlace = ArnoA2.App.Map.SpawnAtlas.Region("verteron").Points.Where(p => p.NpcId == soldier.NpcId).Aggregate(soldier, (a, _) => a) with { X = 0.05, Y = 0.05 };
var (farX, farY) = verteronMap.World(newPlace.X, newPlace.Y);
var (nearX, nearY) = verteronMap.World(soldier.X, soldier.Y);
var mobMetre = 100f;
state.Positions.Observe(new ZoneEntered(1010, null, null), null, now);
void Appear(int entity, double x, double y) => state.Positions.Observe(new NpcAppeared(entity, soldier.NpcId, (float)x, (float)y), null, now);
Appear(90001, nearX + 20 * mobMetre, nearY);
Appear(90002, nearX + 20 * mobMetre, nearY);
Check(maps.Learned.Count == 0, "mobs near a known spawn teach nothing");
Appear(90003, farX, farY);
Check(maps.Learned.Count == 0, "one sighting is not a spawn");
Appear(90003, farX, farY);
Check(maps.Learned.Count == 0, "the same mob twice is not a spawn");
Appear(90004, farX + 100 * mobMetre, farY);
Check(maps.Learned.Count == 0, "a second mob 100 m away is a wanderer, not the same spawn");
Appear(90005, farX + 5 * mobMetre, farY);
Check(maps.Learned.Count == 1, "a second mob at the same new place teaches a spawn");
Appear(90006, farX, farY);
Appear(90007, farX, farY);
Check(maps.Learned.Count == 1, "a learned place isn't learned twice");
Check(new ArnoA2.App.Map.LearnedSpawns(data).Count == 1, "learned spawns survive a restart");
petMap.SetTarget("Pet", soldier.PetId);
Check(petMap.Dots.Count(d => d.Learned) == 1, "a learned spawn shows on the pet maps, marked");
maps.Preferences.LearnSpawns = false;
Appear(90008, farX + 400 * mobMetre, farY);
Appear(90009, farX + 400 * mobMetre, farY);
Check(maps.Learned.Count == 1, "learning can be turned off");
maps.Preferences.LearnSpawns = true;
maps.ForgetLearned();
Check(maps.Learned.Count == 0 && petMap.Dots.All(d => !d.Learned), "learned spawns can be forgotten");
petMap.SetTarget("Unfinished");

var reloaded = new ResourceLayer(maps, data);
Check(reloaded.Preferences.Kinds.Count == ArnoA2.App.Map.ResourceAtlas.Kinds.Length, "the choice of resources is remembered");

var settings = new Window { Width = 500, Height = 720, Content = new ScrollViewer { Content = new StackPanel { Children = { new PetSettingsPanel(state, petMap), new ToolkitSettingsPanel(state) } } } };
settings.Show();
Save(settings, "settings.png");
Check(Text(settings).Contains("Mini-map") && Text(settings).Contains("Capture"), "settings sections");

maps.Preferences.MiniShape = "Round";
tracker.ToggleMiniMap();
var mini = tracker.MiniMap!;
mini.StopCamera(); // headings below are set by hand
mini.Resize(MiniMapWindow.FullSize / 2);
Dispatcher.UIThread.RunJobs();
Check(mini.Bounds.Width == 150 && mini.Bounds.Height == 150 + MiniMapWindow.Footer, "the mini-map shrinks to half size");
Save(mini, "minimap-half.png");
mini.Resize(MiniMapWindow.FullSize);
Dispatcher.UIThread.RunJobs();
Thread.Sleep(400);
foreach (var b in mini.GetVisualDescendants().OfType<Border>().Where(b => b.Child is StackPanel { Orientation: Avalonia.Layout.Orientation.Horizontal } sp && sp.Children.OfType<GoldSlider>().Any()))
    b.IsVisible = true; // show the hover controls for the screenshot
foreach (var b in mini.GetVisualDescendants().OfType<Border>().Where(b => b.Child is TextBlock { Text: "✥ Move" }))
    b.IsVisible = true;
Save(mini, "minimap-round.png");
var slider = mini.GetVisualDescendants().OfType<GoldSlider>().Single();
var pill = slider.GetVisualAncestors().OfType<Border>().First();
var sliderBox = slider.TransformToVisual(pill)!.Value.Transform(new Point(0, 0));
double label = mini.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Opacity").TransformToVisual(pill)!.Value.Transform(new Point(0, 0)).Y + mini.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Opacity").Bounds.Height / 2;
double sliderMiddle = sliderBox.Y + slider.Bounds.Height / 2;
Check(sliderBox.Y >= 0 && sliderBox.Y + slider.Bounds.Height <= pill.Bounds.Height + 0.5, $"opacity slider fits inside its pill ({sliderBox.Y:F1}+{slider.Bounds.Height:F1} in {pill.Bounds.Height:F1})");
Check(Math.Abs(sliderMiddle - pill.Bounds.Height / 2) <= 1 && Math.Abs(label - sliderMiddle) <= 1.5, $"slider and label centred in the pill (label {label:F1}, slider {sliderMiddle:F1}, pill middle {pill.Bounds.Height / 2:F1})");
slider.Value = 0.4;
Check(Math.Abs(mini.GetVisualDescendants().OfType<MapCanvas>().Single().Opacity - 0.4) < 0.01 && Math.Abs(slider.GetVisualAncestors().OfType<Border>().First().Opacity - 1) < 0.01, "opacity fades the whole map, not the controls");
Save(mini, "minimap-faded.png");
slider.Value = 0.8;
mini.ApplyShape(false);
mini.Width = 380;
mini.Height = 260;
Thread.Sleep(200);
Save(mini, "minimap-rectangle.png");
Check(maps.Player != null && maps.Region == "verteron", "position placed on Verteron");

// A long guidance line in a narrow mini-map scrolls instead of being cut off.
mini.Width = 200;
mini.Height = 230;
var marquee = mini.GetVisualDescendants().OfType<MarqueeText>().Single();
marquee.Text = "1038 m NE · Krall Warshaman, Dracuni Herbalist, Forest Spirit";
for (int i = 0; i < 5; i++)
    Dispatcher.UIThread.RunJobs();
Check(marquee.Scrolls && marquee.Bounds.Width <= mini.Width, "long guidance fits the window and scrolls");
marquee.Advance(1.0);
Check(marquee.Offset == 0, "guidance holds still while pausing on its start");
for (int i = 0; i < 30; i++)
    marquee.Advance(0.05);
Check(marquee.Offset > 0, "guidance scrolls after its pause");
double scrolled = marquee.Offset;
marquee.Text = "1037 m NE · Krall Warshaman, Dracuni Herbalist, Forest Spirit";
marquee.Advance(0.05);
Check(marquee.Offset > scrolled, "guidance keeps scrolling when its distance changes");
Save(mini, "minimap-scrolling.png");

// Turning with the camera: the player becomes an arrow pointing the camera way, and the
// map can turn about the player so that way points up.
var canvas = mini.Canvas;
slider.Value = 1;
mini.ApplyShape(true);
mini.Width = 300;
mini.Height = 330;
for (int i = 0; i < 5; i++)
    Dispatcher.UIThread.RunJobs();
Thread.Sleep(100);
var middle = new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
bool Blue(Point at)
{
    for (int i = 0; i < 3; i++)
        Dispatcher.UIThread.RunJobs();
    using var frame = mini.CaptureRenderedFrame() ?? throw new Exception("no frame");
    var p = canvas.TranslatePoint(at, mini)!.Value;
    var pixel = new byte[4];
    var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixel, System.Runtime.InteropServices.GCHandleType.Pinned);
    try
    {
        frame.CopyPixels(new PixelRect((int)p.X, (int)p.Y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
    }
    finally
    {
        handle.Free();
    }

    // The arrow's fill is #4FC3F7: strong blue and green, little red (lighter where it is smoothed).
    bool rgba = frame.Format == Avalonia.Platform.PixelFormat.Rgba8888;
    int r = pixel[rgba ? 0 : 2], g = pixel[1], b = pixel[rgba ? 2 : 0];
    return b > 220 && g > 170 && r < 150;
}

canvas.Follow = true;
mini.ShowHeading(90);
Thread.Sleep(100);
Check(canvas.Turned == 0 && Blue(middle + new Vector(7, 0)) && !Blue(middle + new Vector(0, -7)) && !Blue(middle + new Vector(-7, 0)), "north up, looking east: the arrow points right");
Check(Blue(middle), "the arrow covers the middle (the dot has a white centre)");
Save(mini, "minimap-arrow.png");
var player = maps.Player!.Value;
var east = canvas.Screen(player.X + 0.002, player.Y);
Check(east.X > middle.X + 1 && Math.Abs(east.Y - middle.Y) < 0.5, "north up: east is to the right");

canvas.TurnMap = true;
mini.ShowHeading(37);
Check(Math.Abs(canvas.Turned - 37) < 1e-9 && Blue(middle + new Vector(0, -7)) && !Blue(middle + new Vector(7, 0)), "turned with the camera: the arrow points up");
double rad = 37 * Math.PI / 180;
var ahead = canvas.Screen(player.X + 0.002 * Math.Sin(rad), player.Y - 0.002 * Math.Cos(rad));
Check(Math.Abs(ahead.X - middle.X) < 0.5 && ahead.Y < middle.Y - 1, $"what the camera looks at shows straight above the player ({ahead})");
var north = canvas.Screen(player.X, player.Y - 0.002);
Check(north.X < middle.X - 1 && north.Y < middle.Y, "north moves left of up when looking north-east");
foreach (var (x, y) in new[] { (0.1, 0.2), (0.5, 0.5), (0.93, 0.07), (player.X, player.Y) })
{
    var (rx, ry) = canvas.Region(canvas.Screen(x, y));
    Check(Math.Abs(rx - x) < 1e-9 && Math.Abs(ry - y) < 1e-9, $"turned Screen/Region round trip at {x}, {y}");
}

mini.ShowHeading(251);
var spot = canvas.Screen(0.3, 0.6);
Check(Math.Abs(canvas.Region(spot).X - 0.3) < 1e-9 && Math.Abs(canvas.Region(spot).Y - 0.6) < 1e-9, "a click on the turned map lands on the place under it");
Save(mini, "minimap-turned.png");
mini.ApplyShape(false);
mini.Width = 380;
mini.Height = 290;
Save(mini, "minimap-turned-rectangle.png");
mini.ApplyShape(true);
mini.Width = 300;
mini.Height = 330;
for (int i = 0; i < 5; i++)
    Dispatcher.UIThread.RunJobs();

// Panning and zoom follow the pointer on the turned map.
// Grab a place away from the numbered spots (a click on one makes it the target instead).
var grab = new[] { new Vector(-40, -30), new Vector(40, -30), new Vector(-40, 30), new Vector(40, 30), new Vector(0, -60), new Vector(-60, 0) }
    .Select(v => middle + v)
    .MaxBy(p => petMap.Hotspots.Select(h => canvas.Screen(h.X, h.Y)).Select(h => Math.Sqrt((h.X - p.X) * (h.X - p.X) + (h.Y - p.Y) * (h.Y - p.Y))).DefaultIfEmpty(999).Min());
var under = canvas.Region(grab);
var drop = grab + new Vector(25, 35);
var target = petMap.TargetSpot;
mini.MouseDown(canvas.TranslatePoint(grab, mini)!.Value, Avalonia.Input.MouseButton.Left);
mini.MouseMove(canvas.TranslatePoint(drop, mini)!.Value);
mini.MouseUp(canvas.TranslatePoint(drop, mini)!.Value, Avalonia.Input.MouseButton.Left);
var moved = canvas.Region(drop);
Check(petMap.TargetSpot == target && !canvas.Follow && Math.Abs(moved.X - under.X) < 1e-6 && Math.Abs(moved.Y - under.Y) < 1e-6, "dragging the turned map keeps the grabbed place under the pointer");
var anchor = middle + new Vector(30, -20);
var before = canvas.Region(anchor);
canvas.ZoomAt(anchor, 1.25);
var after = canvas.Region(anchor);
Check(Math.Abs(after.X - before.X) < 1e-9 && Math.Abs(after.Y - before.Y) < 1e-9, "zooming the turned map keeps the place under the pointer");
canvas.ZoomAt(anchor, 0.8);
canvas.Follow = true;
// Back on the player once the map's next animation frame has re-centred it.
for (int i = 0; i < 50 && Math.Abs(canvas.Center.X - player.X) + Math.Abs(canvas.Center.Y - player.Y) > 1e-6; i++)
{
    Thread.Sleep(20);
    Dispatcher.UIThread.RunJobs();
}

mini.ShowHeading(null);
Check(canvas.Turned == 0 && Blue(middle + new Vector(0, -6)) && Blue(middle + new Vector(0, 6)) && !Blue(middle), "camera not read: north up with the plain dot");
canvas.TurnMap = false;

// Easing goes the short way round and settles on the reading.
double? shown = 350;
shown = ArnoA2.App.Map.CameraHeading.Ease(shown, 10, 1 / 60.0);
Check(shown is double first && (first > 350 || first < 10), $"easing from 350° to 10° crosses north ({shown})");
for (int i = 0; i < 60; i++)
    shown = ArnoA2.App.Map.CameraHeading.Ease(shown, 10, 1 / 60.0);
Check(shown is double settled && Math.Abs(settled - 10) < 0.1, "easing settles on the reading within a second");
Check(ArnoA2.App.Map.CameraHeading.Ease(10, null, 1 / 60.0) == null, "no reading: no heading");

// The camera cone on a made-up minimap: bright haze 116° wide over speckled sand.
var random = new Random(7);
foreach (double facing in new[] { 0.0, 37, 130, 251, 333 })
{
    var sand = new byte[219 * 147 * 3];
    for (int py = 0; py < 147; py++)
        for (int px = 0; px < 219; px++)
        {
            int i = (py * 219 + px) * 3, n = random.Next(40);
            (sand[i], sand[i + 1], sand[i + 2]) = ((byte)(150 + n), (byte)(120 + n), (byte)(60 + n / 2));
            double angle = (Math.Atan2(px - 109.5, 73.5 - py) * 180 / Math.PI + 360) % 360, off = Math.Abs(((angle - facing) % 360 + 540) % 360 - 180);
            if (off <= 58 && Math.Sqrt((px - 109.5) * (px - 109.5) + (py - 73.5) * (py - 73.5)) > 10)
                for (int c = 0; c < 3; c++)
                    sand[i + c] = (byte)(sand[i + c] + (255 - sand[i + c]) * 0.45);
        }

    double? read = ArnoA2.App.Map.CameraHeading.Read(sand, 219, 147);
    Check(read is double r && Math.Abs(((r - facing) % 360 + 540) % 360 - 180) <= 3, $"the camera cone at {facing}° reads as {read}");
}

var plain = new byte[219 * 147 * 3];
for (int i = 0; i < plain.Length; i++)
    plain[i] = (byte)(100 + random.Next(30));
Check(ArnoA2.App.Map.CameraHeading.Read(plain, 219, 147) == null, "no cone (minimap hidden): no reading, the map stays north up");
tracker.OpenFullMap();
var full = tracker.FullMap!;
Thread.Sleep(400);
Save(full, "fullmap.png");
Check(Text(full).Contains("Best spots"), "full map lists spots");

// The Abyss: no zone id is known for it, so it is recognised from its pet creatures.
var abyss = ArnoA2.App.Map.WorldMaps.All["abyss"];
ArnoA2.App.Map.Positions Seen(int zone, string region)
{
    var seen = new ArnoA2.App.Map.Positions();
    seen.Observe(new ZoneEntered(zone, null, null), null, now);
    var map = ArnoA2.App.Map.WorldMaps.All[region];
    int entity = 1;
    foreach (var point in ArnoA2.App.Map.SpawnAtlas.Region(region).Points.GroupBy(p => p.NpcId).Take(3).Select(g => g.First()))
    {
        var (x, y) = map.World(point.X, point.Y);
        seen.Observe(new NpcAppeared(entity++, point.NpcId, (float)x, (float)y), null, now);
    }

    return seen;
}

Check(Seen(4242, "abyss").Region() == "abyss", "Abyss creatures in a zone without a map: the Abyss");
Check(Seen(0, "abyss").Region() == "abyss", "Abyss creatures before any zone message: the Abyss");
Check(Seen(4242, "verteron").Region() == null, "Verteron creatures in another zone (an instance) do not move the map");
Check(Seen(1110, "abyss").Region() == "altgard", "a known zone wins over the creatures");
Check(Seen(20, "verteron").Region() == "abyss", "zone 20 is the Abyss, even with no creatures recognised");
// Entering it live: an unknown zone, then its creatures come into view.
state.Positions.Observe(new ZoneEntered(4242, null, null), null, now);
int abyssEntity = 900000;
foreach (var point in ArnoA2.App.Map.SpawnAtlas.Region("abyss").Points.Where((_, i) => i % 40 == 0).Take(12))
{
    var (x, y) = abyss.World(point.X, point.Y);
    state.Positions.Observe(new NpcAppeared(abyssEntity++, point.NpcId, (float)x, (float)y), null, now);
}

Thread.Sleep(700);
Save(full, "fullmap-abyss.png");
var regionBox = full.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Items.Contains("Abyss"));
Check(maps.Region == "abyss" && regionBox.SelectedItem as string == "Abyss" && maps.Spawns.Points.Count > 900, "in the Abyss the full map shows it with its spawns");
Check(maps.Spawns.Tiles.All(t => File.Exists(Path.Combine(AppContext.BaseDirectory, "maps", "abyss", $"{t.Column}_{t.Row}.jpg"))), "the Abyss tiles are bundled");

// The game's Abyss minimap: the islands on a pale void, at the scale calibrated in Verteron
// (the Abyss tiles have half the pixels per metre). Made from the tiles themselves.
var terrain = new ArnoA2.App.Map.TerrainCache(Path.Combine(AppContext.BaseDirectory, "maps"));
using (var island = SkiaSharp.SKBitmap.Decode(Path.Combine(AppContext.BaseDirectory, "maps", "abyss", "1_2.jpg")))
{
    (double X, double Y) truth = (0.4131, 0.5317);
    double onTile = ArnoA2.App.Map.MinimapMatcher.OnMap(abyss, 2.74);
    var sky = new byte[219 * 147 * 3];
    var noise = new Random(3);
    for (int py = 0; py < 147; py++)
        for (int px = 0; px < 219; px++)
        {
            var c = island.GetPixel((int)(truth.X * abyss.Span - 1024 + (px - 109.5) * onTile), (int)(truth.Y * abyss.Span - 2048 + (py - 73.5) * onTile));
            int i = (py * 219 + px) * 3, n = noise.Next(12);
            bool voidPixel = 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue < 50;
            (sky[i], sky[i + 1], sky[i + 2]) = voidPixel ? ((byte)(140 + n), (byte)(147 + n), (byte)(139 + n)) : (c.Red, c.Green, c.Blue);
        }

    var found = ArnoA2.App.Map.MinimapMatcher.Locate(terrain, abyss, sky, 219, 147, 0.42, 0.53, 120, 2.74);
    double off = found == null ? double.MaxValue : Math.Sqrt(Math.Pow(found.X - truth.X, 2) + Math.Pow(found.Y - truth.Y, 2)) * abyss.Metres;
    Check(off < 4, $"the Abyss minimap is found on the tiles ({off:F1} m off, score {found?.Score:F2})");
    var dark = ArnoA2.App.Map.MinimapMatcher.Locate(terrain, abyss with { VoidGray = null }, sky, 219, 147, 0.42, 0.53, 120, 2.74);
    Check(dark == null || Math.Sqrt(Math.Pow(dark.X - truth.X, 2) + Math.Pow(dark.Y - truth.Y, 2)) * abyss.Metres > 20, "without the void repaint the pale void hides the islands (why the Abyss needs it)");
    // Shown position: halfway toward each close minimap match, a far one taken at once.
    double metre = 1 / abyss.Metres;
    maps.AcceptMinimap(new ArnoA2.App.Map.MinimapFix(truth.X, truth.Y, 0.5, 2.74), null, "abyss");
    maps.AcceptMinimap(new ArnoA2.App.Map.MinimapFix(truth.X + 4 * metre, truth.Y, 0.5, 2.74), null, "abyss");
    Check(Math.Abs((maps.Player!.Value.X - truth.X) / metre - 2) < 1e-6, "a minimap match 4 m away moves the marker halfway");
    maps.AcceptMinimap(new ArnoA2.App.Map.MinimapFix(truth.X + 60 * metre, truth.Y, 0.5, 2.74), null, "abyss");
    Check(Math.Abs((maps.Player!.Value.X - truth.X) / metre - 60) < 1e-6, "a match 58 m away is taken at once");
    Check(Math.Abs(ArnoA2.App.Map.MinimapMatcher.OnMap(ArnoA2.App.Map.WorldMaps.All["verteron"], 2.74) - 2.74) < 1e-9 && Math.Abs(ArnoA2.App.Map.MinimapMatcher.OnMap(ArnoA2.App.Map.WorldMaps.All["altgard"], 2.74) - 2.74 / 2) < 0.03, "one minimap calibration serves Verteron and, at half the tile pixels per metre, Altgard");
}
// The phone map: nothing without the link's key, and tiles can't reach outside the maps folder.
{
    var board = new PhoneBoard(maps, new PetLayer(state, maps), new ResourceLayer(maps, store.Folder), new CubeLayer(maps, store.Folder), "", _ => { });
    Check(!board.Running && board.Link == null, "the phone map is off until it's turned on");
    Check(board.Start(0), "the phone map starts");
    using var web = new System.Net.Http.HttpClient();
    string Url(string path) => $"http://127.0.0.1:{board.Port}{path}";
    string secret = board.Link?.Split("?k=")[1] ?? throw new Exception("no link (no network here?)");
    (int Status, string Body) Get(string path, System.Net.Http.HttpMethod? method = null)
    {
        var asking = web.SendAsync(new System.Net.Http.HttpRequestMessage(method ?? System.Net.Http.HttpMethod.Get, Url(path)));
        while (!asking.IsCompleted)
            Dispatcher.UIThread.RunJobs();
        var reply = asking.Result;
        var reading = reply.Content.ReadAsStringAsync();
        while (!reading.IsCompleted)
            Dispatcher.UIThread.RunJobs();
        return ((int)reply.StatusCode, reading.Result);
    }

    Check(Get("/").Status == 403 && Get("/state?k=wrong").Status == 403 && Get("/tile?region=verteron&c=3&r=3").Status == 403, "the phone map answers nothing without the key");
    Check(Get($"/?k={secret}").Body.Contains("wakeLock"), "the key opens the phone page");
    var live = Get($"/state?k={secret}");
    Check(live.Status == 200 && live.Body.Contains("\"region\"") && live.Body.Contains("\"cubes\""), "the phone gets the map's state");
    Check(Get($"/dots?k={secret}&layer=cubes").Body.StartsWith('['), "the phone gets a tool's dots");
    Check(Get($"/tile?k={secret}&region=..&c=0&r=0").Status == 404 && Get($"/tile?k={secret}&region=verteron&c=../../x&r=0").Status == 404, "a tile request can't leave the maps folder");
    Check(Get($"/do?k={secret}&a=nothing", System.Net.Http.HttpMethod.Post).Status == 400, "the phone can only ask for known actions");
    Check(board.Code() != null, "the link becomes a QR code");
    Check(PhoneServer.Network(System.Net.IPAddress.Parse("192.168.1.74"), 24) == "192.168.1.0/24" && PhoneServer.Network(System.Net.IPAddress.Parse("10.20.135.7"), 20) == "10.20.128.0/20", "the home network is worked out from the address");
    Check(string.Join(" ", LinuxFirewall.Ufw("192.168.1.0/24", 8766)) == "/usr/bin/ufw allow from 192.168.1.0/24 to any port 8766 proto tcp", "ufw opens the port to the home network only");
    Check(LinuxFirewall.Firewalld("192.168.1.0/24", 8766)[2].Contains("source address=192.168.1.0/24 port port=8766 protocol=tcp accept"), "firewalld opens the port to the home network only");
    Check(WindowsFirewall.Add(8766).Contains("localport=8766 remoteip=localsubnet"), "Windows opens the port to the local subnet only");
    var phonePanel = new Window { Width = 620, Height = 620, Content = new PhoneSettings(board, _ => { }) };
    phonePanel.Show();
    Check(phonePanel.GetVisualDescendants().OfType<Image>().Single().IsVisible && Text(phonePanel).Contains("Open the map to phones"), "the settings show the link's code while it's on");
    Save(phonePanel, "phone-settings.png");
    phonePanel.Close();
    board.NewLink();
    Check(board.Link?.Split("?k=")[1] != secret && Get($"/state?k={secret}").Status is 403 or 0, "a new link shuts out the old one");
    board.Stop();
    Check(!board.Running, "the phone map stops");
}
var (centreX, centreY) = abyss.Normalize(abyss.World(0.5, 0.5).X, abyss.World(0.5, 0.5).Y);
Check(Math.Abs(centreX - 0.5) < 1e-9 && Math.Abs(centreY - 0.5) < 1e-9, "Abyss map transform round trip");

// HP bars: menu close X, visibility and readings.
{
    byte[] Corner(bool cross, bool text = false)
    {
        const int w = 70, h = 60;
        var rgb = new byte[w * h * 3];
        void Dot(int x, int y) { int i = 3 * (y * w + x); rgb[i] = rgb[i + 1] = rgb[i + 2] = 230; }
        if (cross)
            for (int t = -12; t <= 12; t++)
            {
                Dot(37 + t, 30 + t);
                Dot(37 + t, 30 - t);
            }
        if (text)
            for (int x = 10; x < 60; x++)
                for (int y = 10; y < 18; y++)
                    if ((x + y) % 3 == 0)
                        Dot(x, y);
        return rgb;
    }
    var (ax, ay, aw, ah) = MenuWatch.Area(1920, 1080);
    Check((ax, ay, aw, ah) == (1850, 5, 70, 60) && MenuWatch.Area(2560, 1440) == (2467, 7, 93, 80), "the menu corner follows the window size");
    Check(MenuWatch.HasClose(Corner(true), 70, 60), "a menu's close X is seen");
    Check(!MenuWatch.HasClose(Corner(false), 70, 60) && !MenuWatch.HasClose(Corner(false, text: true), 70, 60), "an empty corner or text is no close X");
    var arrow = new byte[70 * 60 * 3];
    for (int y = 0; y < MenuWatch.Arrow.Length; y++)
        for (int x = 0; x < MenuWatch.Arrow[y].Length; x++)
            if (MenuWatch.Arrow[y][x] == '#')
            {
                int i = 3 * ((20 + y) * 70 + 15 + x);
                (arrow[i], arrow[i + 1], arrow[i + 2]) = (250, 234, 190);
            }
    Check(MenuWatch.HasBack(arrow, 70, 60) && MenuWatch.MenuShown(arrow, 70, 60) && !MenuWatch.HasClose(arrow, 70, 60), "a full-screen menu's gold back arrow is seen");
    Check(!MenuWatch.HasBack(Corner(true), 70, 60) && !MenuWatch.HasBack(Corner(false, text: true), 70, 60), "an X or text is no back arrow");
    var solid = Enumerable.Repeat((byte)230, 70 * 60 * 3).ToArray();
    Check(!MenuWatch.MenuShown(solid, 70, 60), "a bright sky is no menu");
    Check(BarsWindow.Visibility(true, false, false, false, false, false) == 0, "the shortcut hides the bars");
    Check(BarsWindow.Visibility(false, false, false, false, true, true) is > 0.1 and < 1 && BarsWindow.Visibility(false, false, true, true, true, true) < 0.1 && BarsWindow.Visibility(false, false, false, false, false, true) == 1, "idle dims only when chosen, and full fades further");
    Check(BarsWindow.Visibility(false, true, true, true, true, true) == 1, "the pointer brings faded bars back");
    Check(BarsWindow.Reading(30_790, 32_947, "Numbers") == "30,790 / 32,947" && BarsWindow.Reading(16_000, 32_000, "Percent") == "50 %" && BarsWindow.Reading(null, 0, "Numbers") == "", "bar readings");
    Check(BarsWindow.Dimensions("Small") == (280, 15) && BarsWindow.Dimensions("odd") == (380, 19), "bar sizes");
}

Console.WriteLine($"{checks} app checks passed; screenshots in {output}");

