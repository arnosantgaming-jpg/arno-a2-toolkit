using ArnoA2.App.Map;

namespace ArnoA2.App;

/// <summary>
/// Whether a game menu (inventory, shop, map, settings…) is open, from the thin white close X
/// the game draws in its top-right corner while one is: four grabs a second of that small
/// corner, nothing else, and nothing is kept.
/// </summary>
public sealed class MenuWatch : IDisposable
{
    // Measured at 1080p: the X is about 24 px across with its centre 31–35 px from the right
    // edge and 31–36 px from the top, depending on the menu.
    const int AreaWidth = 70, AreaHeight = 60, Top = 5, Arm = 9;
    readonly CancellationTokenSource stop = new();
    readonly Thread thread;
    volatile bool open;

    public MenuWatch()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "Menu watch", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    /// <summary>True while a menu is open; false when none is or the game can't be read.</summary>
    public bool Open => open;

    /// <summary>The corner searched, in game-window pixels.</summary>
    public static (int X, int Y, int Width, int Height) Area(int width, int height)
    {
        double scale = height / 1080.0;
        int w = (int)Math.Round(AreaWidth * scale), h = (int)Math.Round(AreaHeight * scale);
        return (width - w, (int)Math.Round(Top * scale), w, h);
    }

    /// <summary>
    /// Whether the RGB area holds the close X: two white 45° strokes crossing, with dark
    /// straight across and down from the middle, which text and scenery rarely give.
    /// </summary>
    public static bool HasClose(byte[] rgb, int width, int height, double scale = 1)
    {
        int arm = Math.Max(4, (int)Math.Round(Arm * scale)), gap = Math.Max(2, arm / 4);
        int Lum(int x, int y)
        {
            int i = 3 * (y * width + x);
            return Math.Min(rgb[i], Math.Min(rgb[i + 1], rgb[i + 2]));
        }

        // A stroke pixel may sit one off the exact diagonal.
        bool Bright(int x, int y) => Math.Max(Lum(x - 1, y), Math.Max(Lum(x, y), Lum(x + 1, y))) > 150;

        for (int cy = arm + 1; cy < height - arm - 1; cy++)
            for (int cx = arm + 2; cx < width - arm - 2; cx++)
            {
                int on = 0, total = 0;
                for (int t = -arm; t <= arm; t++)
                {
                    if (Math.Abs(t) < 2)
                        continue;
                    total += 2;
                    on += (Bright(cx + t, cy + t) ? 1 : 0) + (Bright(cx + t, cy - t) ? 1 : 0);
                }

                if (on < 0.85 * total)
                    continue;
                int dark = 0, checks = 0;
                for (int t = -arm; t <= arm; t++)
                {
                    if (Math.Abs(t) < gap * 2)
                        continue;
                    checks++;
                    if (Lum(cx + t, cy) < 90 && Lum(cx, cy + t) < 90)
                        dark++;
                }

                if (dark >= 0.9 * checks)
                    return true;
            }

        return false;
    }

    void Run()
    {
        using var capture = GameCapture.Open();
        if (!capture.Available)
            return;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                open = Step(capture);
            }
            catch (Exception)
            {
                open = false;
            }

            stop.Token.WaitHandle.WaitOne(250);
        }
    }

    static bool Step(IGameCapture capture)
    {
        if (capture.Size == null && !capture.Find())
            return false;
        var (width, height) = capture.Size!.Value;
        var (x, y, w, h) = Area(width, height);
        var rgb = capture.Read(x, y, w, h);
        if (rgb == null)
        {
            capture.Find();
            return false;
        }

        return HasClose(rgb, w, h, height / 1080.0);
    }

    public void Dispose()
    {
        stop.Cancel();
        thread.Join(500);
        stop.Dispose();
    }
}
