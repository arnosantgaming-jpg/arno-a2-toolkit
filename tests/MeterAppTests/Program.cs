using ArnoA2.App;
using ArnoA2.App.Dps;
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

string Text(Control c) => string.Join("\n", c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Inlines is { Count: > 0 } runs ? runs.Text : t.Text));
void Save(Window w, string name)
{
    for (int i = 0; i < 3; i++)
        Dispatcher.UIThread.RunJobs();
    using var frame = w.CaptureRenderedFrame() ?? throw new Exception("no frame");
    frame.Save(Path.Combine(output, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
}

var state = new AppState(new ArnoA2.Tracker.Store(Path.Combine(data, "pets")), new Store(data), daemonPath: null);
var engine = state.Meter;
var now = DateTimeOffset.UtcNow;
var meter = new MeterWindow(state);
meter.Show();
Check(Text(meter).Contains(state.CaptureStatus), "before the game starts the meter says what it is waiting for");
foreach (var (actor, damage, seconds) in new[] { (4242, 18_400, 0), (9001, 12_100, 1), (4242, 22_750, 3), (9002, 6_300, 4), (4242, 19_900, 6) })
    engine.Damage.Hit(actor, damage, now.AddSeconds(seconds), TimeSpan.FromSeconds(8));
static byte[] Var(int value)
{
    var bytes = new List<byte>();
    uint v = (uint)value;
    do
    {
        byte b = (byte)(v & 0x7f);
        v >>= 7;
        bytes.Add(v != 0 ? (byte)(b | 0x80) : b);
    }
    while (v != 0);
    return bytes.ToArray();
}

static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
static byte[] Frame(byte op, byte family, byte[] body) => Cat(Var(body.Length + 6), new[] { op, family }, body);
static byte[] Named(byte op, int entity, string name, byte[] rest) => Frame(op, 0x36, Cat(Var(entity), new byte[] { 0, 0, 0, 0, 7, (byte)name.Length }, System.Text.Encoding.UTF8.GetBytes(name), rest));
static byte[] Struck(int actor, int target, uint skill, int damage) => Frame(0x04, 0x38, Cat(Var(target), Var(4), Var(0), Var(actor), BitConverter.GetBytes(skill), new byte[] { 5 }, Var(1), new byte[8], Var(15_000), Var(damage), Var(1)));
static byte[] Hit(int actor, uint skill, int damage) => Frame(0x04, 0x38, Cat(Var(777), Var(4), Var(0), Var(actor), BitConverter.GetBytes(skill), new byte[] { 5 }, Var(1), new byte[8], Var(15_000), Var(damage), Var(1)));
Check(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "classes"), "*.png").Length == 8, "class logos ship with the app");
engine.Data(1, Cat(Named(0x33, 4242, "Arnaud", Cat(new byte[] { 0xD2, 0x04 }, BitConverter.GetBytes(7), new byte[20])), Named(0x45, 9001, "Lumiel", new byte[9]), Hit(9001, 17_010_000, 2_000), Hit(9002, 15_010_000, 1_000), Hit(9003, 12_000_000, 500), Hit(9004, 11_010_000, 450), Hit(9005, 13_010_000, 400), Hit(9006, 14_010_000, 350), Hit(9007, 16_010_000, 300), Hit(9008, 18_010_000, 250)), now);
meter.Refresh();
Save(meter, "meter.png");
Check(Text(meter).Contains("Arnaud") && Text(meter).Contains("Lumiel") && Text(meter).Contains("Player 9003") && Text(meter).Contains("00:00:06"), "meter shows names, numbers the unknown, and the fight's length");
Check(meter.GetVisualDescendants().OfType<Image>().Count() == 8, "each known class shows its logo before the name");
engine.Data(1, Hit(9001, 17_010_000, 5_000_000), now);
meter.Refresh();
Dispatcher.UIThread.RunJobs();
Check(Text(meter).IndexOf("Arnaud") < Text(meter).IndexOf("Lumiel") && Text(meter).Contains("Arnaud") && Text(meter).Split('\n').TakeWhile(t => t != "Arnaud").Last() == "2", "your row stays on top with your real rank");
engine.Damage.Reset();
meter.Refresh();
Check(Text(meter).Contains("appears here"), "meter empties on reset");

engine.Data(1, Cat(Frame(0x41, 0x36, Cat(Var(778), new byte[] { 0x0C, 0, 0, 0x23, 0x0B, 0x20, 0, 0, 2 })), Struck(4242, 778, 14_110_000, 90_000), Struck(4242, 778, 14_120_000, 60_000), Struck(9001, 778, 17_010_000, 40_000)), now.AddSeconds(10));
engine.Data(1, Struck(4242, 778, 14_110_000, 80_000), now.AddSeconds(16));
engine.Damage.Finish();
var history = new Window { Width = 520, Height = 400, Content = new HistoryPanel(state) };
history.Show();
Save(history, "history.png");
Check(Text(history).Contains("1 fights") && Text(history).Contains("Neikel of the East"), "history lists the boss fight");
var skills = new BreakdownWindow(engine.History.First(f => f.Boss).Players[0], engine.History.First(f => f.Boss).Seconds);
skills.Show();
Save(skills, "breakdown.png");
Check(Text(skills).Contains("Gale Arrow"), "the breakdown names skills");

var settings = new Window { Width = 440, Height = 360, Content = new MeterSettingsPanel(state) };
settings.Show();
Save(settings, "settings.png");
Check(Text(settings).Contains("Fights") && Text(settings).Contains("Look"), "settings sections");
settings.Close();
skills.Close();
history.Close();
meter.Close();

Console.WriteLine($"{checks} app checks passed; screenshots in {output}");
