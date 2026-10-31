using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArnoA2.App;

public enum MarkerShape { Dot, Diamond, Paw, Leaf, Square, Drop, Triangle, Fan, Cube }

/// <summary>The shapes map markers are drawn in, each fitting a circle of radius 1 around the origin.</summary>
public static class MapMarkers
{
    static readonly Dictionary<MarkerShape, Geometry> Shapes = new();

    public static Geometry Shape(MarkerShape shape) => Shapes.TryGetValue(shape, out var geometry) ? geometry : Shapes[shape] = Build(shape);

    /// <summary>Draws a marker, a dark edge keeping it readable over any terrain.</summary>
    public static void Draw(DrawingContext context, MarkerShape shape, Point at, double size, IBrush brush, IPen? edge)
    {
        using (context.PushTransform(Matrix.CreateScale(size, size) * Matrix.CreateTranslation(at.X, at.Y)))
            context.DrawGeometry(brush, edge, Shape(shape));
    }

    /// <summary>A pen of this many pixels for markers of this size: their drawing is scaled.</summary>
    public static IPen Edge(double size, double pixels = 1) => new Pen(new SolidColorBrush(Colors.Black, 0.65), pixels / size);

    static Geometry Build(MarkerShape shape)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        switch (shape)
        {
            case MarkerShape.Diamond:
                Polygon(g, (0, -1), (0.72, 0), (0, 1), (-0.72, 0));
                break;
            case MarkerShape.Square:
                Polygon(g, (-0.72, -0.72), (0.72, -0.72), (0.72, 0.72), (-0.72, 0.72));
                break;
            case MarkerShape.Triangle:
                Polygon(g, (0, -1), (0.9, 0.8), (-0.9, 0.8));
                break;
            case MarkerShape.Cube:
                // A hexagon, then the three edges meeting in the near corner: the marker's
                // dark edge draws them.
                Polygon(g, (0, -1), (0.87, -0.5), (0.87, 0.5), (0, 1), (-0.87, 0.5), (-0.87, -0.5));
                g.BeginFigure(new Point(-0.87, -0.5), false);
                g.LineTo(new Point(0, 0));
                g.LineTo(new Point(0.87, -0.5));
                g.EndFigure(false);
                g.BeginFigure(new Point(0, 0), false);
                g.LineTo(new Point(0, 1));
                g.EndFigure(false);
                break;
            case MarkerShape.Paw:
                Circle(g, new Point(0, 0.34), 0.55);
                Circle(g, new Point(-0.72, -0.1), 0.25);
                Circle(g, new Point(-0.28, -0.62), 0.27);
                Circle(g, new Point(0.28, -0.62), 0.27);
                Circle(g, new Point(0.72, -0.1), 0.25);
                break;
            case MarkerShape.Leaf:
                g.BeginFigure(new Point(-0.8, 0.8), true);
                g.QuadraticBezierTo(new Point(-0.9, -0.8), new Point(0.9, -0.9));
                g.QuadraticBezierTo(new Point(0.8, 0.9), new Point(-0.8, 0.8));
                g.EndFigure(true);
                break;
            case MarkerShape.Drop:
                g.BeginFigure(new Point(0, -1), true);
                g.QuadraticBezierTo(new Point(0.72, -0.25), new Point(0.72, 0.28));
                g.ArcTo(new Point(-0.72, 0.28), new Size(0.72, 0.72), 0, false, SweepDirection.Clockwise);
                g.QuadraticBezierTo(new Point(-0.72, -0.25), new Point(0, -1));
                g.EndFigure(true);
                break;
            case MarkerShape.Fan:
                g.BeginFigure(new Point(0, 0.85), true);
                g.LineTo(new Point(-0.95, -0.2));
                g.ArcTo(new Point(0.95, -0.2), new Size(0.95, 0.95), 0, false, SweepDirection.Clockwise);
                g.EndFigure(true);
                break;
            default:
                Circle(g, new Point(0, 0), 0.8);
                break;
        }

        return geometry;
    }

    static void Polygon(StreamGeometryContext g, params (double X, double Y)[] corners)
    {
        g.BeginFigure(new Point(corners[0].X, corners[0].Y), true);
        foreach (var (x, y) in corners.Skip(1))
            g.LineTo(new Point(x, y));
        g.EndFigure(true);
    }

    static void Circle(StreamGeometryContext g, Point c, double r)
    {
        g.BeginFigure(new Point(c.X + r, c.Y), true);
        g.ArcTo(new Point(c.X - r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
        g.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
        g.EndFigure(true);
    }
}

/// <summary>One marker on its own, as the key beside a resource's name.</summary>
public sealed class MarkerGlyph : Control
{
    readonly MarkerShape shape;
    readonly IBrush brush;

    public MarkerGlyph(MarkerShape shape, IBrush brush, double size = 12)
    {
        (this.shape, this.brush) = (shape, brush);
        Width = Height = size;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    public override void Render(DrawingContext context)
    {
        double size = Bounds.Width / 2;
        MapMarkers.Draw(context, shape, new Point(size, size), size, brush, MapMarkers.Edge(size, 0.7));
    }
}
