using ArnoA2.App.Map;
using ArnoA2.Protocol;

namespace ArnoA2.App;

/// <summary>A Hidden Cube the game told us about, at a known cube spot.</summary>
public sealed record CubeSighting(string Region, string Area, double X, double Y);

/// <summary>Picks real cubes out of the game's messages. The message itself isn't decoded:
/// what marks it is its kind and size, and a position exactly on one of the cube spots.</summary>
public sealed class CubeWatch
{
    const byte Op = 0x34, Family = 0x36;
    const int Size = 55, Position = 9;
    static readonly TimeSpan Repeat = TimeSpan.FromMinutes(1);
    readonly Dictionary<(double, double), DateTimeOffset> recent = new();

    public CubeSighting? See(Frame frame, DateTimeOffset now)
    {
        if (frame.Op != Op || frame.Family != Family || frame.Body.Length != Size)
            return null;
        float x = BitConverter.ToSingle(frame.Body, Position), y = BitConverter.ToSingle(frame.Body, Position + 4);
        if (!float.IsFinite(x) || !float.IsFinite(y) || CubeAtlas.Exact(x, y) is not { } spot)
            return null;
        if (recent.TryGetValue((spot.X, spot.Y), out var last) && now - last < Repeat)
            return null;
        recent[(spot.X, spot.Y)] = now;
        return spot;
    }
}
