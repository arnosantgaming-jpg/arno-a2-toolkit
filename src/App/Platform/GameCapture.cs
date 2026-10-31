namespace ArnoA2.App.Map;

/// <summary>Reads small rectangles of what the game has drawn. Not thread-safe: use one instance
/// from one thread.</summary>
public interface IGameCapture : IDisposable
{
    bool Available { get; }
    /// <summary>The game window's size, once <see cref="Find"/> has found it.</summary>
    (int Width, int Height)? Size { get; }
    /// <summary>Finds the game window (refreshed when it is lost).</summary>
    bool Find();
    /// <summary>RGB bytes of the rectangle, or null when the window is gone.</summary>
    byte[]? Read(int x, int y, int width, int height);
}

public static class GameCapture
{
    public static string Unavailable => OperatingSystem.IsWindows() ? "Could not read the screen." : "Reading the game's minimap needs an X11/XWayland display.";

    public static IGameCapture Open() => OperatingSystem.IsWindows() ? new WindowsGameCapture() : new X11GameCapture();
}
