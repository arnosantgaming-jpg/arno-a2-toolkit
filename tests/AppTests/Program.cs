using ArnoA2.App;
using ArnoA2.Meter;
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

var state = new AppState(new Store(data), daemonPath: null);
var engine = state.Engine;
var now = DateTimeOffset.UtcNow;
var meter = new MeterWindow(state);
meter.Show();
Check(Text(meter).Contains(state.CaptureStatus), "before the game starts the meter says what it is waiting for");
foreach (var (actor, damage, seconds) in new[] { (4242, 18_400, 0), (9001, 12_100, 1), (4242, 22_750, 3), (9002, 6_300, 4), (4242, 19_900, 6) })
    engine.Damage.Hit(actor, damage, now.AddSeconds(seconds), TimeSpan.FromSeconds(8));
meter.Refresh();
Save(meter, "meter.png");
Check(Text(meter).Contains("Player 9001") && Text(meter).Contains("0:06 in combat"), "meter ranks everyone by damage and shows the fight's length");
engine.Damage.Reset();
meter.Refresh();
Check(Text(meter).Contains(state.CaptureStatus), "meter empties on reset");

var settings = new SettingsWindow(state);
settings.Show();
Save(settings, "settings.png");
Check(Text(settings).Contains("Fights") && Text(settings).Contains("Capture"), "settings sections");
settings.Close();
meter.Close();

Console.WriteLine($"{checks} app checks passed; screenshots in {output}");
