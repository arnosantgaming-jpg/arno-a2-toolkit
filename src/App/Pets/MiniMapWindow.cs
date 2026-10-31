using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using ArnoA2.App.Map;

namespace ArnoA2.App;

/// <summary>
/// Borderless overlay map shared by every running tool, round or rectangular: the player in
/// the middle as an arrow pointing the camera (north up, or turned so the camera points up), a
/// frame with compass marks and one thin ring per tool in its colour, and each tool's next
/// farming spot as a line or an arrow on the frame. The hover controls wear the first tool's colour.
/// Drag Move or the rim to move it, drag inside to pan; controls appear on hover.
/// </summary>
public sealed class MiniMapWindow : Window
{
    public const double Frame = 4, Footer = 30, FullSize = 300;
    const double RingGap = 2.5;
    IReadOnlyList<MapLayer> layers;
    readonly MapHub hub;
    readonly MapCanvas canvas;
    readonly Grid face = new();
    readonly FrameControl frame;
    readonly StackPanel controls = new() { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, IsVisible = false };
    readonly Border opacityPanel;
    readonly GoldSlider opacity;
    // Scrolls sideways when the guidance is longer than the label.
    readonly MarqueeText guidance = new() { FontSize = 11.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Border grip, mover;
    readonly Button shapeButton;
    readonly Button turnButton;
    CameraHeading? camera;
    IDisposable? ticking;
    readonly Border label;
    bool sizing;

    public MiniMapWindow(MapHub hub, IReadOnlyList<MapLayer> layers)
    {
        this.hub = hub;
        this.layers = layers;
        var preferences = hub.Preferences;
        frame = new FrameControl();
        opacity = new GoldSlider(layers[0].Colors) { Minimum = 0.2, Maximum = 1, Width = 100, VerticalAlignment = VerticalAlignment.Center };
        Title = KdeWindowRules.MiniMapTitle;
        Icon = ToolTheme.AppIcon;
        Width = Math.Clamp(preferences.MiniWidth, FullSize / 2, 1200);
        Height = Math.Clamp(preferences.MiniHeight, FullSize / 2 + Footer, 1200);
        MinWidth = FullSize / 2;
        MinHeight = FullSize / 2 + Footer;
        Topmost = true;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        if (preferences.MiniX is int x && preferences.MiniY is int y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }

        canvas = new MapCanvas(hub, layers, minimap: true) { Zoom = preferences.MiniZoom };
        opacity.Value = preferences.MiniOpacity;
        opacity.ValueChanged += value =>
        {
            preferences.MiniOpacity = value;
            Fade(value);
        };

        shapeButton = Small("◯", "Switch between circle and rectangle", ToggleShape);
        turnButton = Small("N", "", ToggleTurn);
        canvas.TurnMap = preferences.MiniTurn;
        ShowTurn();
        foreach (var button in new[]
        {
            Small("−", "Zoom out", () => canvas.ZoomAt(null, 0.8)),
            Small("+", "Zoom in", () => canvas.ZoomAt(null, 1.25)),
            Small("◎", "Centre on me", () =>
            {
                canvas.Follow = true;
                canvas.InvalidateVisual();
            }),
            Small("⟳", "Next farming spot", () =>
            {
                foreach (var layer in this.layers.Where(l => l.Hotspots.Count > 0))
                    layer.TargetSpot = (layer.TargetSpot + 1) % layer.Hotspots.Count;
                Update();
            }),
            turnButton,
            shapeButton,
            Small("✕", "Close the mini-map", Close)
        })
            controls.Children.Add(button);
        controls.Margin = new Thickness(0, 0, 0, Frame + 10);

        var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        opacityRow.Children.Add(new TextBlock { Text = "Opacity", FontSize = 10.5, Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center });
        opacityRow.Children.Add(opacity);
        opacityPanel = new Border { Padding = new Thickness(12, 0), Height = 28, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Frame + 10, 0, 0), Child = opacityRow, IsVisible = false };

        grip = new Border
        {
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            IsVisible = false,
            Child = new TextBlock { Text = "◢", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom }
        };
        grip.PointerPressed += (_, e) =>
        {
            sizing = true;
            BeginResizeDrag(WindowEdge.SouthEast, e);
        };

        mover = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 0),
            Height = 26,
            CornerRadius = new CornerRadius(13),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, Frame + 42, 0, 0),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            IsVisible = false,
            Child = new TextBlock { Text = "✥ Move", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }
        };
        ToolTip.SetTip(mover, "Drag to move the mini-map");
        mover.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        face.Children.Add(canvas);
        face.Children.Add(frame);
        face.Children.Add(opacityPanel);
        face.Children.Add(mover);
        face.Children.Add(controls);
        face.Children.Add(grip);
        // Pressing on the frame (not the map) moves the window.
        frame.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        label = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 2), Height = 24, HorizontalAlignment = HorizontalAlignment.Center, Child = guidance, Margin = new Thickness(8, 4, 8, 0) };
        var layout = new DockPanel();
        DockPanel.SetDock(label, Dock.Bottom);
        layout.Children.Add(label);
        layout.Children.Add(face);
        Content = layout;

        SetLayers(layers);
        Fade(preferences.MiniOpacity);
        PointerEntered += (_, _) => ShowControls(true);
        PointerExited += (_, _) => ShowControls(false);
        PointerReleased += (_, _) => sizing = false;
        ApplyShape(preferences.MiniShape != "Rectangle");

        canvas.ViewChanged += () => preferences.MiniZoom = canvas.Zoom;
        hub.Changed += Update;
        Opened += (_, _) =>
        {
            hub.Attach();
            Update();
            FollowCamera();
        };
        Closing += (_, _) =>
        {
            preferences.MiniX = Position.X;
            preferences.MiniY = Position.Y;
            preferences.MiniWidth = Width;
            preferences.MiniHeight = Height;
            hub.Save();
        };
        Closed += (_, _) =>
        {
            StopCamera();
            hub.Changed -= Update;
            hub.Detach();
        };
    }

    bool Round => frame.Round;

    /// <summary>The frame grows by one ring per tool past the first.</summary>
    double Edge => Frame + (layers.Count - 1) * RingGap;

    /// <summary>The tools now running: a ring and marks for each, outermost first.</summary>
    public void SetLayers(IReadOnlyList<MapLayer> running)
    {
        layers = running;
        var colors = running[0].Colors;
        IBrush accent = new SolidColorBrush(colors.Accent), glass = new SolidColorBrush(colors.Window, 0.8), solid = new SolidColorBrush(colors.Window, 0.9);
        canvas.Show(running);
        canvas.Margin = new Thickness(Edge);
        frame.Tint(running.Select(l => l.Colors).ToArray(), Edge, RingGap);
        opacity.Tint(colors);
        opacityPanel.Background = glass;
        (mover.Background, mover.BorderBrush) = (glass, accent);
        ((TextBlock)grip.Child!).Foreground = accent;
        (label.Background, label.BorderBrush) = (new SolidColorBrush(colors.Window, 0.85), new SolidColorBrush(colors.Accent, 0.5));
        foreach (var button in controls.Children.OfType<Button>())
            (button.Background, button.BorderBrush) = (solid, accent);
        Layout(new Size(Width, Height));
        frame.InvalidateVisual();
        canvas.InvalidateVisual();
        Update();
    }

    void ToggleShape()
    {
        ApplyShape(!Round);
        hub.Preferences.MiniShape = Round ? "Round" : "Rectangle";
        hub.Save();
    }

    public MapCanvas Canvas => canvas;

    void ToggleTurn()
    {
        canvas.TurnMap = hub.Preferences.MiniTurn = !canvas.TurnMap;
        hub.Save();
        ShowTurn();
        ApplyTurn();
    }

    /// <summary>The button shows the current mode: N (north up) or ↻ (turns with the camera).</summary>
    void ShowTurn()
    {
        turnButton.Content = canvas.TurnMap ? "↻" : "N";
        ToolTip.SetTip(turnButton, (canvas.TurnMap ? "Turning with the camera · click for north up" : "North up · click to turn with the camera")
            + (camera?.Status is { } status ? "\n" + status : ""));
    }

    /// <summary>Moves the frame's compass marks with north and redraws the map.</summary>
    void ApplyTurn()
    {
        frame.Turned = canvas.Turned;
        frame.InvalidateVisual();
        canvas.InvalidateVisual();
    }

    /// <summary>Reads your camera direction from the game's minimap while the mini-map is open,
    /// and eases the shown direction toward each reading every frame, so turning is smooth.</summary>
    void FollowCamera()
    {
        camera = new CameraHeading();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double last = 0;
        string? status = null;
        ticking = Avalonia.Threading.DispatcherTimer.Run(() =>
        {
            double now = clock.Elapsed.TotalSeconds, dt = now - last;
            last = now;
            double? shown = canvas.Heading, next = CameraHeading.Ease(shown, camera?.Degrees, dt);
            if (camera?.Status != status)
            {
                status = camera?.Status;
                ShowTurn();
            }

            if ((next == null) != (shown == null) || (next is double a && shown is double b && Math.Abs(CameraHeading.Wrap(a - b)) > 0.05))
            {
                canvas.Heading = next;
                ApplyTurn();
            }

            return camera != null;
        }, TimeSpan.FromMilliseconds(16));
    }

    /// <summary>Stops reading the camera (tests set <see cref="MapCanvas.Heading"/> themselves).</summary>
    public void StopCamera()
    {
        ticking?.Dispose();
        camera?.Dispose();
        (ticking, camera) = (null, null);
    }

    /// <summary>Applies a heading at once, as the camera timer does (for tests).</summary>
    public void ShowHeading(double? degrees)
    {
        canvas.Heading = degrees;
        ApplyTurn();
    }

    /// <summary>Resizes to a share of the full size, keeping the top-left corner where it is.</summary>
    public void Resize(double size)
    {
        Width = size;
        Height = size + Footer;
    }

    public void ApplyShape(bool round)
    {
        frame.Round = round;
        canvas.Round = round;
        shapeButton.Content = round ? "▢" : "◯";
        if (round)
            Height = Width + Footer; // a circle needs a square face
        Layout(new Size(Width, Height));
        frame.InvalidateVisual();
        canvas.InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (Round && sizing && Math.Abs(e.NewSize.Height - (e.NewSize.Width + Footer)) >= 1)
            Height = e.NewSize.Width + Footer; // keep the circle round while resizing
        Layout(e.NewSize);
    }

    void Layout(Size size)
    {
        double w = size.Width, h = Math.Max(size.Height - Footer, 0);
        if (Round)
            w = h = Math.Min(w, h);
        face.Width = Math.Max(w, 0);
        face.Height = h;
        double iw = Math.Max(w - 2 * Edge, 0), ih = Math.Max(h - 2 * Edge, 0);
        canvas.Clip = Round ? new EllipseGeometry(new Rect(0, 0, iw, ih)) : new RectangleGeometry(new Rect(0, 0, iw, ih), 8, 8);
    }

    /// <summary>See-through: the whole map, frame and label fade so the game shows behind
    /// them (the window itself is transparent). Hover controls stay fully visible.</summary>
    void Fade(double value)
    {
        canvas.Opacity = value;
        frame.Opacity = value;
        label.Opacity = Math.Max(value, 0.5);
    }

    void ShowControls(bool show)
    {
        controls.IsVisible = show;
        opacityPanel.IsVisible = show;
        grip.IsVisible = show;
        mover.IsVisible = show;
    }

    void Update() => guidance.Text = hub.Error ?? string.Join("   |   ", layers.Select(l => l.Guidance()).Distinct());

    Button Small(string label, string tip, Action action)
    {
        var button = new Button
        {
            Content = label,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White,
            FontSize = 14,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>A thin frame, a ring or a rounded rectangle, with N/E/S/W floating just inside the map.</summary>
    sealed class FrameControl : Control, Avalonia.Rendering.ICustomHitTest
    {
        IBrush Dark = Brushes.Black, North = Brushes.White;
        IPen[] Rings = Array.Empty<IPen>();
        double Thick = Frame, Gap;
        static readonly IPen Shadow = new Pen(Brush.Parse("#80000000"), 3);
        const double Grab = 9, Inset = 11;
        static readonly Typeface Face = new("Noto Sans", FontStyle.Normal, FontWeight.Bold);
        public void Tint(IReadOnlyList<ToolColors> tools, double thick, double gap)
        {
            Dark = new SolidColorBrush(tools[0].Window, 0.94);
            North = new SolidColorBrush(tools[0].Bright);
            Rings = tools.Select(t => (IPen)new Pen(new SolidColorBrush(t.Accent), 1.5)).ToArray();
            (Thick, Gap) = (thick, gap);
        }

        public bool Round { get; set; } = true;
        /// <summary>Degrees the map is turned anticlockwise: the marks move with north.</summary>
        public double Turned { get; set; }

        /// <summary>Only the rim takes the pointer, a little wider than the frame so it is easy to catch; the map inside keeps panning and clicks.</summary>
        public bool HitTest(Point p)
        {
            if (Round)
            {
                var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
                double r = Math.Min(Bounds.Width, Bounds.Height) / 2, d = Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y));
                return d >= r - (Grab + Thick - Frame) && d <= r;
            }

            var outer = new Rect(Bounds.Size);
            return outer.Contains(p) && !outer.Deflate(Grab + Thick - Frame).Contains(p);
        }

        public override void Render(DrawingContext context)
        {
            if (Bounds.Width <= 2 * Thick || Bounds.Height <= 2 * Thick)
                return;
            if (Round)
                RenderRing(context);
            else
                RenderRectangle(context);
        }

        void RenderRing(DrawingContext context)
        {
            var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
            double r = Math.Min(Bounds.Width, Bounds.Height) / 2;
            var band = new StreamGeometry();
            using (var g = band.Open())
            {
                g.SetFillRule(FillRule.EvenOdd);
                Circle(g, c, r - 1);
                Circle(g, c, r - Thick);
            }

            context.DrawGeometry(Dark, null, band);
            context.DrawEllipse(null, Shadow, c, r - Thick + 1, r - Thick + 1);
            for (int i = 0; i < Rings.Length; i++)
                context.DrawEllipse(null, Rings[i], c, r - 1.5 - i * Gap, r - 1.5 - i * Gap);

            double mid = r - Thick - Inset;
            foreach (var (text, deg) in Compass)
            {
                double a = (deg - Turned) * Math.PI / 180;
                Letter(context, text, new Point(c.X + Math.Sin(a) * mid, c.Y - Math.Cos(a) * mid));
            }
        }

        void RenderRectangle(DrawingContext context)
        {
            var outer = new Rect(Bounds.Size).Deflate(1);
            var inner = new Rect(Bounds.Size).Deflate(Thick);
            var band = new StreamGeometry();
            using (var g = band.Open())
            {
                g.SetFillRule(FillRule.EvenOdd);
                Rounded(g, outer, 12);
                Rounded(g, inner, 8);
            }

            context.DrawGeometry(Dark, null, band);
            context.DrawRectangle(null, Shadow, inner.Inflate(1), 8, 8);
            for (int i = 0; i < Rings.Length; i++)
                context.DrawRectangle(null, Rings[i], outer.Deflate(0.5 + i * Gap), 12 - i, 12 - i);
            // Each letter where its direction from the middle meets a line just inside the frame.
            double inset = Thick + Inset, rx = Bounds.Width / 2 - inset, ry = Bounds.Height / 2 - inset;
            foreach (var (text, deg) in Compass)
            {
                double a = (deg - Turned) * Math.PI / 180, dx = Math.Sin(a), dy = -Math.Cos(a);
                double t = Math.Min(Math.Abs(dx) < 1e-9 ? double.MaxValue : rx / Math.Abs(dx), Math.Abs(dy) < 1e-9 ? double.MaxValue : ry / Math.Abs(dy));
                Letter(context, text, new Point(Bounds.Width / 2 + dx * t, Bounds.Height / 2 + dy * t));
            }
        }

        static readonly (string Text, double Degrees)[] Compass = { ("N", 0), ("E", 90), ("S", 180), ("W", 270) };

        void Letter(DrawingContext context, string text, Point at)
        {
            var edge = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, Brushes.Black);
            var format = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, text == "N" ? North : Brushes.White);
            var corner = new Point(at.X - format.Width / 2, at.Y - format.Height / 2);
            // A dark edge keeps the letter readable over any terrain.
            foreach (var (x, y) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, 1), (-1, 1), (1, -1) })
                context.DrawText(edge, corner + new Point(x, y));
            context.DrawText(format, corner);
        }

        static void Circle(StreamGeometryContext g, Point c, double r)
        {
            g.BeginFigure(new Point(c.X + r, c.Y), true);
            g.ArcTo(new Point(c.X - r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.EndFigure(true);
        }

        static void Rounded(StreamGeometryContext g, Rect r, double radius)
        {
            g.BeginFigure(new Point(r.Left + radius, r.Top), true);
            g.LineTo(new Point(r.Right - radius, r.Top));
            g.ArcTo(new Point(r.Right, r.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(r.Right, r.Bottom - radius));
            g.ArcTo(new Point(r.Right - radius, r.Bottom), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(r.Left + radius, r.Bottom));
            g.ArcTo(new Point(r.Left, r.Bottom - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(r.Left, r.Top + radius));
            g.ArcTo(new Point(r.Left + radius, r.Top), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            g.EndFigure(true);
        }
    }
}
