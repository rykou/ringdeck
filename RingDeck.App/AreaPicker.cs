using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using RingDeck.Windows;

namespace RingDeck.App;

/// <summary>The "Screenshot area" step: freezes the screen (every monitor), dims it, and lets the user drag out a
/// rectangle; that part of the frozen picture goes on the clipboard. Esc or a right click cancels. Runs on the thread
/// that runs actions, which has its own dispatcher, so the macro or flow simply waits for the choice.</summary>
public sealed class AreaPicker : Window
{
    readonly BitmapSource _frozen;
    readonly (int X, int Y, int Width, int Height) _bounds;
    readonly Canvas _layer = new();
    readonly Path _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), IsHitTestVisible = false };
    readonly Rectangle _frame = new() { StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly Border _size = new() { CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly TextBlock _sizeText = new() { Foreground = Brushes.White, FontSize = 12 };
    readonly TaskCompletionSource<bool> _done = new();
    Point? _from;

    /// <summary>Lets the user choose an area and puts it on the clipboard. False when they cancel (or the run stops);
    /// throws when the picture can't be put on the clipboard.</summary>
    public static async Task<bool> PickAsync(CancellationToken ct)
    {
        var picker = new AreaPicker();
        using var stop = ct.Register(() => picker.Dispatcher.BeginInvoke(() => picker.Finish(false)));
        picker.Show();
        picker.Activate();
        return await picker._done.Task;
    }

    AreaPicker()
    {
        _bounds = Screens.VirtualBounds();
        _frozen = ScreenGrab.Grab(_bounds.X, _bounds.Y, _bounds.Width, _bounds.Height);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;
        // Placed in physical pixels once the window exists; this size only avoids a flash at the default size.
        (Left, Top, Width, Height) = (-32000, -32000, 10, 10);

        var accent = Theme.Frozen(Theme.Accent);
        _frame.Stroke = accent;
        _size.Background = accent;
        _size.Child = _sizeText;
        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 26)), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0), IsHitTestVisible = false,
            Child = new TextBlock { Text = "Drag to choose the area for the screenshot  ·  Esc or right-click cancels", Foreground = Brushes.White, FontSize = 14 },
        };
        _layer.Children.Add(_shade);
        _layer.Children.Add(_frame);
        _layer.Children.Add(_size);
        var root = new Grid();
        root.Children.Add(new Image { Source = _frozen, Stretch = Stretch.Fill });
        root.Children.Add(_layer);
        root.Children.Add(hint);
        Content = root;

        SourceInitialized += (_, _) => Screens.PlaceRect(new WindowInteropHelper(this).Handle, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height);
        Loaded += (_, _) => Shade(null);
        SizeChanged += (_, _) => Shade(Selection());
        MouseLeftButtonDown += (_, e) => { _from = e.GetPosition(_layer); CaptureMouse(); Shade(Selection()); };
        MouseMove += (_, _) => { if (_from is not null) Shade(Selection()); };
        MouseLeftButtonUp += (_, _) =>
        {
            if (_from is null) return;
            ReleaseMouseCapture();
            var area = Selection();
            _from = null;
            if (area is not { Width: >= 4, Height: >= 4 } chosen) { Shade(null); return; }
            try
            {
                var picture = new CroppedBitmap(_frozen, ToPixels(chosen));
                picture.Freeze();
                ClipboardService.PutPicture(picture);
                Finish(true);
            }
            catch (Exception ex)
            {
                _done.TrySetException(ex);              // reported like any failing step
                Close();
            }
        };
        MouseRightButtonDown += (_, _) => Finish(false);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(false); };
        Closed += (_, _) => _done.TrySetResult(false);
    }

    Rect? Selection()
    {
        if (_from is not { } a) return null;
        var b = Mouse.GetPosition(_layer);
        var x = Math.Clamp(Math.Min(a.X, b.X), 0, ActualWidth);
        var y = Math.Clamp(Math.Min(a.Y, b.Y), 0, ActualHeight);
        return new Rect(x, y, Math.Min(Math.Abs(b.X - a.X), ActualWidth - x), Math.Min(Math.Abs(b.Y - a.Y), ActualHeight - y));
    }

    /// <summary>Dims everything but the selection, and frames it with its size in pixels.</summary>
    void Shade(Rect? area)
    {
        var all = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        _shade.Data = area is { } hole ? new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(hole)) : all;
        _frame.Visibility = _size.Visibility = area is null ? Visibility.Collapsed : Visibility.Visible;
        if (area is not { } r) return;
        Canvas.SetLeft(_frame, r.X);
        Canvas.SetTop(_frame, r.Y);
        (_frame.Width, _frame.Height) = (r.Width, r.Height);
        var pixels = ToPixels(r);
        _sizeText.Text = $"{pixels.Width} × {pixels.Height}";
        Canvas.SetLeft(_size, r.X);
        Canvas.SetTop(_size, r.Y >= 28 ? r.Y - 26 : r.Bottom + 4);
    }

    /// <summary>The selection in the frozen picture's pixels. The picture fills the window, so the window's own
    /// scale (which can differ per monitor) never matters.</summary>
    Int32Rect ToPixels(Rect r)
    {
        double sx = _frozen.PixelWidth / Math.Max(1, ActualWidth), sy = _frozen.PixelHeight / Math.Max(1, ActualHeight);
        var x = (int)Math.Round(r.X * sx);
        var y = (int)Math.Round(r.Y * sy);
        return new Int32Rect(x, y, Math.Clamp((int)Math.Round(r.Width * sx), 1, _frozen.PixelWidth - x), Math.Clamp((int)Math.Round(r.Height * sy), 1, _frozen.PixelHeight - y));
    }

    void Finish(bool chosen)
    {
        _done.TrySetResult(chosen);
        if (IsVisible) Close();
    }
}
