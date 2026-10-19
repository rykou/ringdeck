using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>A flow slice's canvas: cards linked by wires from Start, branches running at the same time. Cards are
/// added from the palette on the left, dragged around, and linked by dragging from a card's right dot onto another
/// card; the panel on the right edits the selected card's steps. The canvas pans (drag the background) and zooms
/// (mouse wheel).</summary>
public sealed class FlowWindow : Window
{
    const double CardW = 220, CardH = 68, PillW = 120, PillH = 46, Snap = 10;

    readonly EditorWindow _editor;
    readonly Grid _stage = new() { ClipToBounds = true, Focusable = true, FocusVisualStyle = null };
    readonly Canvas _world = new();
    readonly ScaleTransform _zoom = new();
    readonly TranslateTransform _pan = new();
    readonly StackPanel _inspector = new() { Margin = new Thickness(16) };
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Dictionary<Guid, MacroAction> _stepLists = new();
    readonly Brush _cardBack, _cardEdge, _subtle, _accent;

    FlowNode? _node;          // selected card
    FlowWire? _wire;          // selected wire
    enum DragKind { None, Pan, Move, Link }
    DragKind _drag;
    Point _dragFrom, _startPos;
    FlowNode? _dragNode;
    bool _moved;
    Path? _pending;

    public Slice Slice { get; }
    public FlowAction Flow { get; }

    /// <summary>What a piece of the canvas is, kept in its Tag for hit testing.</summary>
    sealed record Hit(string What, FlowNode? Node = null, FlowWire? Wire = null);

    public FlowWindow(EditorWindow editor, Slice slice, FlowAction flow)
    {
        (_editor, Slice, Flow) = (editor, slice, flow);
        Owner = editor;
        Title = $"Flow — {slice.Label}";
        Width = 1280;
        Height = 780;
        MinWidth = 900;
        MinHeight = 520;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _cardBack = Theme.Frozen(Theme.IsDark ? Color.FromRgb(0x23, 0x23, 0x2E) : Colors.White);
        _cardEdge = Theme.Frozen(Theme.IsDark ? Color.FromRgb(0x36, 0x36, 0x4A) : Color.FromRgb(0xD2, 0xD2, 0xDE));
        _subtle = Theme.Frozen(Theme.Subtle);
        _accent = Theme.Frozen(Theme.Accent);

        // ----- canvas -----
        var view = new TransformGroup();
        view.Children.Add(_zoom);
        view.Children.Add(_pan);
        _world.RenderTransform = view;
        var dot = Theme.IsDark ? Color.FromRgb(0x2E, 0x2E, 0x3C) : Color.FromRgb(0xC4, 0xC4, 0xD2);
        var grid = new DrawingBrush
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 24, 24), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 24, 24), ViewboxUnits = BrushMappingMode.Absolute, Transform = view,
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))),
                    new GeometryDrawing(new SolidColorBrush(dot), null, new EllipseGeometry(new Point(12, 12), 1.3, 1.3)),
                },
            },
        };
        _stage.SetResourceReference(Panel.BackgroundProperty, "StageBrush");
        _stage.Children.Add(new Rectangle { Fill = grid, IsHitTestVisible = false });
        _stage.Children.Add(_world);
        _stage.MouseLeftButtonDown += OnDown;
        _stage.MouseMove += OnMove;
        _stage.MouseLeftButtonUp += OnUp;
        _stage.MouseWheel += OnWheel;
        _stage.KeyDown += OnKey;
        _stage.SizeChanged += (_, e) => { if (e.PreviousSize.Width == 0) Fit(); };

        // ----- top bar over the canvas -----
        var fit = new Button { Content = "Fit to view", Margin = new Thickness(8, 0, 0, 0), ToolTip = "Show every card" };
        fit.Click += (_, _) => Fit();
        var bar = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
        DockPanel.SetDock(fit, Dock.Right);
        bar.Children.Add(fit);
        bar.Children.Add(_status);
        var hint = new TextBlock
        {
            Margin = new Thickness(12, 0, 12, 10), TextWrapping = TextWrapping.Wrap, Foreground = _subtle, FontSize = 12,
            Text = "Drag from a card's right dot onto another card to link them. Several links out of a card run at the same time. "
                 + "Drag the background to move around, scroll to zoom, Delete removes what's selected.",
        };
        var middle = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(hint, Dock.Top);
        middle.Children.Add(bar);
        middle.Children.Add(hint);
        middle.Children.Add(_stage);

        // ----- palette and inspector -----
        var palette = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Palette(), Width = 190 };
        var side = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _inspector, Width = 380 };
        // Edits in the side panel are saved like edits in the editor, and the cards show them straight away.
        side.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => Edited(s, e)));
        side.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(Edited));
        side.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(Edited));
        side.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => Edited(s, e)));
        side.AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((s, e) => Edited(s, e)));

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(middle, 1);
        Grid.SetColumn(side, 2);
        root.Children.Add(palette);
        root.Children.Add(middle);
        root.Children.Add(side);
        Content = root;
    }

    void Edited(object sender, RoutedEventArgs e)
    {
        _editor.EditedInMacroWindow(sender, e);
        if (IsLoaded) Redraw();
    }

    /// <summary>Everything changed shape: draw the canvas and the side panel again.</summary>
    public void Refresh()
    {
        Title = $"Flow — {Slice.Label}";
        if (_node is not null && !Flow.Nodes.Contains(_node)) _node = null;
        if (_wire is not null && !Flow.Wires.Contains(_wire)) _wire = null;
        Redraw();
        ShowInspector();
    }

    // =====================================================================================================
    // palette

    UIElement Palette()
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        void Header(string text) => panel.Children.Add(new TextBlock { Text = text, Foreground = _subtle, FontSize = 12, Margin = new Thickness(4, 10, 0, 4) });
        void Item(FlowCardKind kind, string text, string tip, Func<FlowNode?> make)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = Glyph(kind), FontFamily = IconAssets.IconFont, FontSize = 16, Width = 24,
                Foreground = new SolidColorBrush(KindColor(kind)), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4), ToolTip = tip };
            button.Click += (_, _) => { if (make() is { } node) Add(node); };
            panel.Children.Add(button);
        }
        Header("Record");
        Item(FlowCardKind.Steps, "Recorded steps", "Record mouse, clicks, scrolling and keys with F9 into one card", () =>
            _editor.RecordFor(this) is { } steps ? new FlowNode { Title = "Recorded steps", Steps = steps } : null);
        Header("Actions");
        Item(FlowCardKind.Open, "Open", "An app, file, folder or web address", () => Single(new LaunchStep()));
        Item(FlowCardKind.Type, "Type text", "Types text into the window in front", () => Single(new TextStep()));
        Item(FlowCardKind.Paste, "Paste text", "Pastes text through the clipboard", () => Single(new PasteStep()));
        Item(FlowCardKind.Keys, "Key press", "A key or a combination such as Ctrl+C", () => Single(new KeyStep()));
        Item(FlowCardKind.Click, "Click", "Clicks where the pointer is", () => Single(new ClickStep()));
        Item(FlowCardKind.Scroll, "Scroll", "Turns the mouse wheel", () => Single(new ScrollStep()));
        Item(FlowCardKind.Wait, "Wait", "A pause, in milliseconds", () => Single(new DelayStep { Ms = 500 }));
        Item(FlowCardKind.Clipboard, "Set clipboard", "Puts text on the clipboard", () => Single(new ClipboardStep()));
        Item(FlowCardKind.Screenshot, "Screenshot", "Puts a screenshot on the clipboard", () => Single(new ScreenshotStep()));
        Item(FlowCardKind.Focus, "Focus window", "Brings a window to the front", () => Single(new FocusStep()));
        Item(FlowCardKind.Web, "Send to web", "Discord, Slack, Teams, a phone notification, any web address", () => Single(new WebhookStep()));
        Item(FlowCardKind.Steps, "Steps", "An empty card for a list of steps", () => new FlowNode { Title = "Steps" });
        Header("End");
        Item(FlowCardKind.Stop, "Stop", "The end of a branch. At least one branch must reach a Stop.", () => new FlowNode { Kind = FlowNodeKind.Stop });
        return panel;
    }

    static FlowNode Single(MacroStep step) => new() { Steps = { step } };

    /// <summary>Puts a new card to the right of the selected one, linked from it; or in the middle of the view.</summary>
    void Add(FlowNode node)
    {
        var (w, h) = SizeOf(node);
        if (_node is { Kind: not FlowNodeKind.Stop } from)
        {
            // Sending straight after a screenshot almost always means sending the screenshot.
            if (from.Steps.Any(s => s is ScreenshotStep))
                foreach (var web in node.Steps.OfType<WebhookStep>()) web.Request.SendImage = true;
            var (fw, fh) = SizeOf(from);
            node.X = from.X + fw + 90;
            node.Y = from.Y + fh / 2 - h / 2;
            while (Flow.Nodes.Any(n => Overlaps(n, node))) node.Y += h + 30;
            Flow.Nodes.Add(node);
            FlowGraph.Link(Flow, from.Id, node.Id);
        }
        else
        {
            var centre = ToWorld(new Point(_stage.ActualWidth / 2, _stage.ActualHeight / 2));
            node.X = Math.Round((centre.X - w / 2) / Snap) * Snap;
            node.Y = Math.Round((centre.Y - h / 2) / Snap) * Snap;
            while (Flow.Nodes.Any(n => Overlaps(n, node))) node.Y += h + 30;
            Flow.Nodes.Add(node);
        }
        (_node, _wire) = (node, null);
        _editor.FlowChanged();
        Refresh();
        BringIntoView(node);
    }

    bool Overlaps(FlowNode a, FlowNode b)
    {
        if (a == b) return false;
        var (aw, ah) = SizeOf(a);
        var (bw, bh) = SizeOf(b);
        return new Rect(a.X, a.Y, aw, ah).IntersectsWith(new Rect(b.X - 10, b.Y - 10, bw + 20, bh + 20));
    }

    // =====================================================================================================
    // looks

    static (double W, double H) SizeOf(FlowNode node) => node.Kind == FlowNodeKind.Action ? (CardW, CardH) : (PillW, PillH);
    static Point OutPort(FlowNode n) { var (w, h) = SizeOf(n); return new Point(n.X + w, n.Y + h / 2); }
    static Point InPort(FlowNode n) { var (_, h) = SizeOf(n); return new Point(n.X, n.Y + h / 2); }

    static Color KindColor(FlowCardKind kind) => kind switch
    {
        FlowCardKind.Start => Color.FromRgb(0x2E, 0xCC, 0x71),
        FlowCardKind.Stop => Color.FromRgb(0xFF, 0x5B, 0x4D),
        FlowCardKind.Steps => Color.FromRgb(0xB1, 0x8C, 0xFF),
        FlowCardKind.Open or FlowCardKind.Focus => Color.FromRgb(0x4C, 0xC2, 0xFF),
        FlowCardKind.Type or FlowCardKind.Paste or FlowCardKind.Keys => Color.FromRgb(0xFF, 0xB8, 0x4C),
        FlowCardKind.Click or FlowCardKind.Move or FlowCardKind.Scroll => Color.FromRgb(0xFF, 0x7A, 0xB6),
        FlowCardKind.Clipboard or FlowCardKind.Screenshot => Color.FromRgb(0x3D, 0xDC, 0x84),
        FlowCardKind.Web => Color.FromRgb(0xFF, 0x7A, 0x59),
        _ => Color.FromRgb(0x9A, 0xA0, 0xB5),
    };

    static string Glyph(FlowCardKind kind) => char.ConvertFromUtf32(kind switch
    {
        FlowCardKind.Start => 62982, FlowCardKind.Stop => 63275, FlowCardKind.Steps => 63074, FlowCardKind.Open => 62555,
        FlowCardKind.Type => 63504, FlowCardKind.Paste => 62166, FlowCardKind.Keys => 62649, FlowCardKind.Click => 58438,
        FlowCardKind.Move => 61801, FlowCardKind.Scroll => 61835, FlowCardKind.Wait => 63527, FlowCardKind.Clipboard => 62154,
        FlowCardKind.Screenshot => 63118, FlowCardKind.Focus => 61022, FlowCardKind.Web => 63130, _ => 62702,
    });

    static Color Fade(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>Draws every wire, then every card on top.</summary>
    void Redraw()
    {
        _world.Children.Clear();
        _pending = null;
        var reachable = FlowGraph.Reachable(Flow);
        foreach (var wire in Flow.Wires) DrawWire(wire, reachable);
        foreach (var node in Flow.Nodes) DrawCard(node, reachable.Contains(node.Id));
        var problem = FlowGraph.Problem(Flow);
        _status.Text = problem is null ? $"✓ Ready · {Flow.Nodes.Count(n => n.Kind == FlowNodeKind.Action)} cards" : "⚠ " + problem;
        _status.Foreground = problem is null ? new SolidColorBrush(KindColor(FlowCardKind.Start)) : Brushes.Orange;
    }

    static PathGeometry Curve(Point a, Point b)
    {
        var dx = Math.Max(50, Math.Abs(b.X - a.X) * 0.5);
        var figure = new PathFigure { StartPoint = a };
        figure.Segments.Add(new BezierSegment(new Point(a.X + dx, a.Y), new Point(b.X - dx, b.Y), b, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    void DrawWire(FlowWire wire, HashSet<Guid> reachable)
    {
        if (FlowGraph.Find(Flow, wire.From) is not { } from || FlowGraph.Find(Flow, wire.To) is not { } to) return;
        var (a, b) = (OutPort(from), InPort(to));
        var geometry = Curve(a, b);
        var live = reachable.Contains(from.Id);
        var selected = wire == _wire;
        var brush = selected ? _accent : new LinearGradientBrush(KindColor(FlowText.Kind(from)), KindColor(FlowText.Kind(to)), 0)
            { MappingMode = BrushMappingMode.Absolute, StartPoint = a, EndPoint = b };
        _world.Children.Add(new Path { Data = geometry, Stroke = brush, StrokeThickness = selected ? 4.5 : 3, Opacity = live ? 1 : 0.4,
            IsHitTestVisible = false });
        // A card with several links out starts them all at once: a dash runs along those wires.
        if (live && Flow.Wires.Count(w => w.From == from.Id) > 1)
        {
            var dash = new Path { Data = geometry, Stroke = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 3, 4 }, IsHitTestVisible = false };
            dash.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, -7, TimeSpan.FromSeconds(0.6)) { RepeatBehavior = RepeatBehavior.Forever });
            _world.Children.Add(dash);
        }
        _world.Children.Add(new Path { Data = geometry, Stroke = Brushes.Transparent, StrokeThickness = 14, Cursor = Cursors.Hand,
            Tag = new Hit("wire", Wire: wire), ToolTip = $"{FlowText.Title(from)} → {FlowText.Title(to)}" });
        if (selected)
        {
            // A small remove button in the middle of the selected wire.
            var mid = Midpoint(a, b);
            var remove = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = _accent, Cursor = Cursors.Hand,
                Tag = new Hit("unlink", Wire: wire), ToolTip = "Remove this link",
                Child = new TextBlock { Text = "✕", Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Canvas.SetLeft(remove, mid.X - 11);
            Canvas.SetTop(remove, mid.Y - 11);
            _world.Children.Add(remove);
        }
    }

    static Point Midpoint(Point a, Point b)
    {
        var dx = Math.Max(50, Math.Abs(b.X - a.X) * 0.5);
        Point c1 = new(a.X + dx, a.Y), c2 = new(b.X - dx, b.Y);
        return new Point(0.125 * a.X + 0.375 * c1.X + 0.375 * c2.X + 0.125 * b.X, 0.125 * a.Y + 0.375 * c1.Y + 0.375 * c2.Y + 0.125 * b.Y);
    }

    void DrawCard(FlowNode node, bool live)
    {
        var kind = FlowText.Kind(node);
        var colour = KindColor(kind);
        var (w, h) = SizeOf(node);
        var selected = node == _node;
        FrameworkElement card;
        if (node.Kind == FlowNodeKind.Action)
        {
            var icon = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Fade(colour, 46)),
                Child = new TextBlock { Text = Glyph(kind), FontFamily = IconAssets.IconFont, FontSize = 20, Foreground = new SolidColorBrush(colour),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            words.Children.Add(new TextBlock { Text = FlowText.Title(node), FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
            words.Children.Add(new TextBlock { Text = FlowText.Summary(node), FontSize = 11, Foreground = _subtle, Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis });
            var inside = new DockPanel { Margin = new Thickness(12, 0, 12, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            inside.Children.Add(icon);
            inside.Children.Add(words);
            card = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(12), Background = _cardBack,
                BorderBrush = selected ? _accent : _cardEdge, BorderThickness = new Thickness(selected ? 2 : 1), Child = inside,
            };
        }
        else
        {
            var start = node.Kind == FlowNodeKind.Start;
            var words = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = Glyph(kind), FontFamily = IconAssets.IconFont, FontSize = 16, Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            words.Children.Add(new TextBlock { Text = start ? "Start" : "Stop", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14 });
            card = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(h / 2), Child = words,
                Background = new LinearGradientBrush(start ? Color.FromRgb(0x1F, 0xA4, 0x63) : Color.FromRgb(0xC0, 0x39, 0x2B), colour, 0),
                BorderBrush = selected ? _accent : Brushes.Transparent, BorderThickness = new Thickness(2),
            };
        }
        card.Effect = new DropShadowEffect
        {
            Color = selected ? Theme.Accent : Colors.Black, BlurRadius = selected ? 22 : 16, ShadowDepth = selected ? 0 : 4,
            Opacity = selected ? 0.75 : Theme.IsDark ? 0.5 : 0.18,
        };
        card.Opacity = live || node.Kind == FlowNodeKind.Start ? 1 : 0.45;
        card.Cursor = Cursors.SizeAll;
        card.Tag = new Hit("card", node);
        card.ToolTip = live || node.Kind == FlowNodeKind.Start ? null : "Not linked from Start, so it doesn't run";
        Canvas.SetLeft(card, node.X);
        Canvas.SetTop(card, node.Y);
        _world.Children.Add(card);
        if (node.Kind != FlowNodeKind.Start) AddPort(node, InPort(node), colour, "in", "Links in: this card waits for every card linked into it");
        if (node.Kind != FlowNodeKind.Stop) AddPort(node, OutPort(node), colour, "out", "Drag from here onto another card to link them");
    }

    void AddPort(FlowNode node, Point at, Color colour, string what, string tip)
    {
        var port = new Ellipse
        {
            Width = 14, Height = 14, StrokeThickness = 2.5, Stroke = new SolidColorBrush(colour), ToolTip = tip,
            Cursor = what == "out" ? Cursors.Cross : Cursors.Arrow, Tag = new Hit(what, node),
        };
        port.SetResourceReference(Shape.FillProperty, "StageBrush");
        Canvas.SetLeft(port, at.X - 7);
        Canvas.SetTop(port, at.Y - 7);
        _world.Children.Add(port);
    }

    // =====================================================================================================
    // mouse and keys

    Point ToWorld(Point onStage) => new((onStage.X - _pan.X) / _zoom.ScaleX, (onStage.Y - _pan.Y) / _zoom.ScaleY);

    static Hit? HitOf(object? source)
    {
        var d = source as DependencyObject;
        while (d is not null)
        {
            if (d is FrameworkElement { Tag: Hit hit }) return hit;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    FlowNode? CardAt(Point world) => Flow.Nodes.LastOrDefault(n =>
    {
        var (w, h) = SizeOf(n);
        return new Rect(n.X - 12, n.Y - 12, w + 24, h + 24).Contains(world);
    });

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        _stage.Focus();
        var world = e.GetPosition(_world);
        var hit = HitOf(e.OriginalSource);
        _moved = false;
        switch (hit?.What)
        {
            case "out":
                _drag = DragKind.Link;
                _dragNode = hit.Node;
                _pending = new Path { Stroke = _accent, StrokeThickness = 3, StrokeDashArray = new DoubleCollection { 2, 2 }, IsHitTestVisible = false };
                _world.Children.Add(_pending);
                break;
            case "card" or "in":
                Select(hit.Node, null);
                _drag = DragKind.Move;
                _dragNode = hit.Node;
                _dragFrom = world;
                _startPos = new Point(hit.Node!.X, hit.Node.Y);
                break;
            case "wire":
                Select(null, hit.Wire);
                return;
            case "unlink":
                Flow.Wires.Remove(hit.Wire!);
                _wire = null;
                _editor.FlowChanged();
                Refresh();
                return;
            default:
                Select(null, null);
                _drag = DragKind.Pan;
                _dragFrom = e.GetPosition(_stage);
                _startPos = new Point(_pan.X, _pan.Y);
                break;
        }
        _stage.CaptureMouse();
        e.Handled = true;
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        switch (_drag)
        {
            case DragKind.Move when _dragNode is { } node:
                var world = e.GetPosition(_world);
                var x = Math.Round((_startPos.X + world.X - _dragFrom.X) / Snap) * Snap;
                var y = Math.Round((_startPos.Y + world.Y - _dragFrom.Y) / Snap) * Snap;
                if (x == node.X && y == node.Y) return;
                (node.X, node.Y) = (x, y);
                _moved = true;
                Redraw();
                break;
            case DragKind.Pan:
                var now = e.GetPosition(_stage);
                _pan.X = _startPos.X + now.X - _dragFrom.X;
                _pan.Y = _startPos.Y + now.Y - _dragFrom.Y;
                break;
            case DragKind.Link when _dragNode is { } from && _pending is not null:
                var to = e.GetPosition(_world);
                var target = CardAt(to);
                var ok = target is not null && FlowGraph.CanLink(Flow, from.Id, target.Id);
                _pending.Data = Curve(OutPort(from), ok ? InPort(target!) : to);
                _pending.Stroke = target is not null && !ok ? Brushes.OrangeRed : _accent;
                break;
        }
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        var drag = _drag;
        _drag = DragKind.None;
        _stage.ReleaseMouseCapture();
        if (drag == DragKind.Link && _dragNode is { } from)
        {
            if (CardAt(e.GetPosition(_world)) is { } target && FlowGraph.Link(Flow, from.Id, target.Id)) _editor.FlowChanged();
            Redraw();
        }
        else if (drag == DragKind.Move && _moved) _editor.FlowChanged();
        _dragNode = null;
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var at = e.GetPosition(_stage);
        var world = ToWorld(at);
        var scale = Math.Clamp(_zoom.ScaleX * Math.Pow(1.1, e.Delta / 120.0), 0.3, 2);
        _zoom.ScaleX = _zoom.ScaleY = scale;
        _pan.X = at.X - world.X * scale;
        _pan.Y = at.Y - world.Y * scale;
        e.Handled = true;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back)
        {
            if (_wire is { } wire) Flow.Wires.Remove(wire);
            else if (_node is { Kind: not FlowNodeKind.Start } node) FlowGraph.Remove(Flow, node);
            else return;
            (_node, _wire) = (null, null);
            _editor.FlowChanged();
            Refresh();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) Select(null, null);
    }

    void Select(FlowNode? node, FlowWire? wire)
    {
        if (node == _node && wire == _wire) return;
        (_node, _wire) = (node, wire);
        Redraw();
        ShowInspector();
    }

    /// <summary>Developer aid (--shot-editor): selects the card with the most steps, so the picture shows the side panel.</summary>
    internal void SelectBusiestCard() =>
        Select(Flow.Nodes.Where(n => n.Kind == FlowNodeKind.Action).OrderByDescending(n => n.Steps.Count).FirstOrDefault(), null);

    /// <summary>Zooms and moves so every card is in view (between 60% and 100%).</summary>
    void Fit()
    {
        if (Flow.Nodes.Count == 0 || _stage.ActualWidth == 0) return;
        var left = Flow.Nodes.Min(n => n.X);
        var top = Flow.Nodes.Min(n => n.Y);
        var right = Flow.Nodes.Max(n => n.X + SizeOf(n).W);
        var bottom = Flow.Nodes.Max(n => n.Y + SizeOf(n).H);
        const double Margin = 60;
        // Never so small the cards can't be read: a wide flow starts at its left end instead, and scrolls.
        var scale = Math.Clamp(Math.Min((_stage.ActualWidth - 2 * Margin) / Math.Max(1, right - left), (_stage.ActualHeight - 2 * Margin) / Math.Max(1, bottom - top)), 0.6, 1);
        _zoom.ScaleX = _zoom.ScaleY = scale;
        var fitsAcross = (right - left) * scale <= _stage.ActualWidth - 2 * Margin;
        var fitsDown = (bottom - top) * scale <= _stage.ActualHeight - 2 * Margin;
        _pan.X = fitsAcross ? (_stage.ActualWidth - (right - left) * scale) / 2 - left * scale : Margin - left * scale;
        _pan.Y = fitsDown ? (_stage.ActualHeight - (bottom - top) * scale) / 2 - top * scale : Margin - top * scale;
    }

    void BringIntoView(FlowNode node)
    {
        var (w, h) = SizeOf(node);
        var s = _zoom.ScaleX;
        var (sx, sy) = (node.X * s + _pan.X, node.Y * s + _pan.Y);
        if (sx < 20) _pan.X += 20 - sx;
        if (sx + w * s > _stage.ActualWidth - 20) _pan.X -= sx + w * s - (_stage.ActualWidth - 20);
        if (sy < 20) _pan.Y += 20 - sy;
        if (sy + h * s > _stage.ActualHeight - 20) _pan.Y -= sy + h * s - (_stage.ActualHeight - 20);
    }

    // =====================================================================================================
    // side panel

    void ShowInspector() => _editor.BuildQuietly(_inspector, () =>
    {
        var add = _inspector.Children;
        TextBlock Heading(string text) => new() { Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = _subtle, Margin = new Thickness(0, 0, 0, 10) };
        Button Remove(string text, Action act)
        {
            var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            button.Click += (_, _) => { act(); (_node, _wire) = (null, null); _editor.FlowChanged(); Refresh(); };
            return button;
        }

        if (_wire is { } wire)
        {
            add.Add(Heading("Link"));
            add.Add(Note($"{FlowText.Title(FlowGraph.Find(Flow, wire.From)!)} → {FlowText.Title(FlowGraph.Find(Flow, wire.To)!)}"));
            add.Add(Remove("Remove link", () => Flow.Wires.Remove(wire)));
            return;
        }
        switch (_node)
        {
            case null:
                add.Add(Heading("Flow"));
                add.Add(Note("Cards run from Start along the links. A card with several links out starts all of them at the same time; "
                    + "a card with several links in waits for all of them. Cards that use the keyboard, mouse or clipboard take turns, so "
                    + "branches never type over each other."));
                add.Add(Note("At least one branch must end at a Stop. Other branches may simply end. Esc stops a running flow."));
                add.Add(Note("Pick a card from the left to add it. With a card selected, the new one is added after it and linked."));
                return;
            case { Kind: FlowNodeKind.Start }:
                add.Add(Heading("Start"));
                add.Add(Note("The flow begins here when the slice is chosen. Link it to the first card, or to several to start branches at once."));
                return;
            case { Kind: FlowNodeKind.Stop } stop:
                add.Add(Heading("Stop"));
                add.Add(Note("The end of a branch. At least one branch must reach a Stop; the flow is done when every branch has finished."));
                add.Add(Remove("Delete", () => FlowGraph.Remove(Flow, stop)));
                return;
            case { } node:
                add.Add(Heading(FlowText.Title(node)));
                add.Add(new TextBlock { Text = "Name (optional)", Foreground = _subtle, Margin = new Thickness(0, 0, 0, 4) });
                var name = new TextBox { Text = node.Title, ToolTip = "Leave empty to name the card after what it does" };
                name.TextChanged += (_, _) => node.Title = name.Text;
                add.Add(name);
                if (FlowGraph.ScreenshotNotSent(Flow, node) is { Count: > 0 } unsent)
                {
                    add.Add(new TextBlock
                    {
                        Text = "A card before this one takes a screenshot, but this card only sends text: \"Also send the picture on the clipboard\" is off.",
                        Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
                    });
                    var fix = new Button { Content = "Send the screenshot too", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
                    fix.Click += (_, _) =>
                    {
                        foreach (var request in unsent) request.SendImage = true;
                        _editor.FlowChanged();
                        Refresh();
                    };
                    add.Add(fix);
                }
                if (!_stepLists.TryGetValue(node.Id, out var steps) || steps.Steps != node.Steps)
                    _stepLists[node.Id] = steps = new MacroAction { Steps = node.Steps };
                var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
                add.Add(list);
                _editor.BuildMacroInto(list, steps);
                add.Add(Remove("Delete card", () => FlowGraph.Remove(Flow, node)));
                return;
        }
    });
}
