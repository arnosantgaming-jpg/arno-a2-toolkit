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
var state = new AppState(store, daemonPath: null);
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
var tracker = new TrackerWindow(state, maps);
tracker.Show();
Save(tracker, "tracker.png");
Check(Text(tracker).Contains("Dratona") && Text(tracker).Contains("43/75") && Text(tracker).Contains("Session: 5 souls"), "tracker lists recent pets with souls and the session");

var finder = new FinderWindow(state, maps);
finder.Show();
finder.GetVisualDescendants().OfType<TextBox>().First().Text = "Dratona";
Dispatcher.UIThread.RunJobs();
Save(finder, "finder.png");
Check(Text(finder).Contains("Souls from:") && Text(finder).Contains("3 pets"), "finder searches and shows sources");

var settings = new SettingsWindow(state, maps, tracker);
settings.Show();
Save(settings, "settings.png");
Check(Text(settings).Contains("Mini-map") && Text(settings).Contains("Capture"), "settings sections");

maps.Preferences.MiniShape = "Round";
tracker.OpenMiniMap();
var mini = tracker.MiniMap!;
Thread.Sleep(400);
Save(mini, "minimap-round.png");
tracker.SetMiniMapShape(false);
mini.Width = 380;
mini.Height = 260;
Thread.Sleep(200);
Save(mini, "minimap-rectangle.png");
Check(maps.Player != null && maps.Region == "verteron", "position placed on Verteron");

tracker.OpenFullMap();
var full = tracker.FullMap!;
Thread.Sleep(400);
Save(full, "fullmap.png");
Check(Text(full).Contains("Best spots"), "full map lists spots");
Console.WriteLine($"{checks} app checks passed; screenshots in {output}");

