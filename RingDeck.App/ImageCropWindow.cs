using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>Zoom and pan a picture inside a circle, then keep the circle as a round slice icon (or the whole picture).</summary>
public sealed class ImageCropWindow : Window
{
    const double View = 320, Output = 256;
    readonly BitmapSource _picture;
    readonly Stage _stage;
    readonly Slider _zoom = new() { Margin = new Thickness(0, 12, 0, 0) };
    CropView _view;
    Point? _dragFrom;
    bool _syncing;

    /// <summary>The PNG to store, and whether it is a round crop that fills the slice.</summary>
    public (byte[] Png, bool Round)? Result { get; private set; }

    sealed class Stage(ImageCropWindow owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            var v = owner._view;
            dc.DrawRectangle(Theme.Frozen(Color.FromRgb(0x14, 0x14, 0x1A)), null, new Rect(0, 0, View, View));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, View, View)));
            dc.DrawImage(owner._picture, new Rect(v.X, v.Y, owner._picture.PixelWidth * v.Zoom, owner._picture.PixelHeight * v.Zoom));
            dc.Pop();
            // dim everything outside the circle, then outline it
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, View, View)),
                new EllipseGeometry(new Point(View / 2, View / 2), View / 2 - 1, View / 2 - 1));
            dc.DrawGeometry(Theme.Frozen(Colors.Black, 0.6), null, outside);
            dc.DrawEllipse(null, new Pen(Theme.Frozen(Theme.Accent), 2), new Point(View / 2, View / 2), View / 2 - 1, View / 2 - 1);
        }
    }

    public ImageCropWindow(byte[] pictureBytes)
    {
        _picture = Load(pictureBytes);
        _view = CropMath.Start(_picture.PixelWidth, _picture.PixelHeight, View);
        Title = "Crop the picture";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _stage = new Stage(this) { Width = View, Height = View, Cursor = Cursors.SizeAll, ClipToBounds = true };
        _stage.MouseLeftButtonDown += (_, e) => { _dragFrom = e.GetPosition(_stage); _stage.CaptureMouse(); };
        _stage.MouseMove += (_, e) =>
        {
            if (_dragFrom is not { } from || !_stage.IsMouseCaptured) return;
            var at = e.GetPosition(_stage);
            Apply(CropMath.Pan(_view, at.X - from.X, at.Y - from.Y, _picture.PixelWidth, _picture.PixelHeight, View));
            _dragFrom = at;
        };
        _stage.MouseLeftButtonUp += (_, _) => { _dragFrom = null; _stage.ReleaseMouseCapture(); };
        _stage.MouseWheel += (_, e) =>
        {
            var at = e.GetPosition(_stage);
            Apply(CropMath.ZoomAt(_view, _view.Zoom * (e.Delta > 0 ? 1.12 : 1 / 1.12), at.X, at.Y, _picture.PixelWidth, _picture.PixelHeight, View));
        };

        var cover = _view.Zoom;
        _zoom.Minimum = cover;
        _zoom.Maximum = cover * 8;
        _zoom.Value = cover;
        _zoom.ValueChanged += (_, e) =>
        {
            if (!_syncing) Apply(CropMath.ZoomAt(_view, e.NewValue, View / 2, View / 2, _picture.PixelWidth, _picture.PixelHeight, View));
        };

        var useRound = new Button { Content = "Use as icon", IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        useRound.Click += (_, _) => { Result = (Crop(), true); DialogResult = true; };
        var useWhole = new Button { Content = "Use whole image", ToolTip = "Keep the picture uncropped, smaller inside the slice (good for logos)" };
        useWhole.Click += (_, _) => { Result = (pictureBytes, false); DialogResult = true; };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(useWhole);
        buttons.Children.Add(useRound);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new Border { Child = _stage, CornerRadius = new CornerRadius(8), ClipToBounds = true });
        root.Children.Add(_zoom);
        root.Children.Add(new TextBlock
        {
            Text = "Drag to move · mouse wheel or slider to zoom. The circle is what the slice shows.",
            Foreground = (Brush)Application.Current.Resources["SubtleBrush"], Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = View,
        });
        root.Children.Add(buttons);
        Content = root;
    }

    /// <summary>Developer aid for --shot-editor: zoom toward a point as the mouse wheel would, then return the round crop.</summary>
    internal byte[] ZoomAndCrop(double factor, double x, double y)
    {
        Apply(CropMath.ZoomAt(_view, _view.Zoom * factor, x, y, _picture.PixelWidth, _picture.PixelHeight, View));
        return Crop();
    }

    void Apply(CropView view)
    {
        _view = view;
        _syncing = true;
        _zoom.Value = view.Zoom;
        _syncing = false;
        _stage.InvalidateVisual();
    }

    static BitmapSource Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        // .ico files hold several sizes: use the largest
        var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First();
        var picture = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        picture.Freeze();
        return picture;
    }

    /// <summary>The circle as a 256×256 PNG, transparent outside the circle.</summary>
    byte[] Crop()
    {
        var (x, y, size) = CropMath.SourceSquare(_view, View);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new EllipseGeometry(new Point(Output / 2, Output / 2), Output / 2, Output / 2));
            var scale = Output / size;
            dc.DrawImage(_picture, new Rect(-x * scale, -y * scale, _picture.PixelWidth * scale, _picture.PixelHeight * scale));
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap((int)Output, (int)Output, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
