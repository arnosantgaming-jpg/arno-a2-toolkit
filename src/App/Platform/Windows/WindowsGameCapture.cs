using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ArnoA2.App.Map;

/// <summary>
/// Copies a small rectangle of the screen where the AION 2 window is, like a screenshot of
/// that corner. It reads pixels the game has already drawn; nothing is injected into or read
/// from the game process. The game must be visible there, so a window on top of its minimap is
/// read too, and exclusive fullscreen may come back black: borderless windowed works.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGameCapture : IGameCapture
{
    [DllImport("user32")] static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr window, StringBuilder text, int length);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr window, StringBuilder text, int length);
    [DllImport("user32")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32")] static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32")] static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32")] static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32")] static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32")] static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    delegate bool EnumProc(IntPtr window, IntPtr parameter);
    const uint SourceCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, Bits;
        public int Compression, ImageSize, XPerMeter, YPerMeter, Used, Important, Colors;
    }

    IntPtr window;

    public bool Available => true;
    public (int Width, int Height)? Size { get; private set; }

    public bool Find()
    {
        window = IntPtr.Zero;
        Size = null;
        EnumWindows((candidate, _) =>
        {
            if (!IsGame(candidate))
                return true;
            window = candidate;
            return false;
        }, IntPtr.Zero);
        return window != IntPtr.Zero && Measure();
    }

    // The class keeps out other windows with the name in their title, such as a browser tab.
    static bool IsGame(IntPtr candidate)
    {
        var text = new StringBuilder(256);
        if (!IsWindowVisible(candidate) || GetClassNameW(candidate, text, text.Capacity) == 0 || text.ToString() != "UnrealWindow")
            return false;
        text.Clear();
        return GetWindowTextW(candidate, text, text.Capacity) > 0 && text.ToString().StartsWith("AION2", StringComparison.OrdinalIgnoreCase);
    }

    bool Measure()
    {
        if (!GetClientRect(window, out var client) || client.Right <= 0 || client.Bottom <= 0)
            return false;
        Size = (client.Right, client.Bottom);
        return true;
    }

    public byte[]? Read(int x, int y, int width, int height)
    {
        if (window == IntPtr.Zero && !Find())
            return null;
        if (!IsWindow(window) || IsIconic(window) || !Measure())
        {
            window = IntPtr.Zero;
            return null;
        }

        var origin = new Point { X = x, Y = y };
        ClientToScreen(window, ref origin);
        var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 }; // negative: top row first
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateDIBSection(screen, ref info, 0, out var pixels, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero)
        {
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
            return null;
        }

        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, origin.X, origin.Y, SourceCopy))
                return null;
            var bgrx = new byte[width * height * 4];
            Marshal.Copy(pixels, bgrx, 0, bgrx.Length);
            var rgb = new byte[width * height * 3];
            for (int i = 0; i < width * height; i++)
            {
                rgb[i * 3] = bgrx[i * 4 + 2];
                rgb[i * 3 + 1] = bgrx[i * 4 + 1];
                rgb[i * 3 + 2] = bgrx[i * 4];
            }

            return rgb;
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public void Dispose()
    {
    }
}
