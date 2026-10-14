using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>A colour wheel: angle is the hue, distance from the centre is the saturation. Brightness is set from
/// outside (a slider) and darkens the whole wheel.</summary>
public sealed class ColorWheel : FrameworkElement
{
    double _hue, _saturation = 1, _value = 1;
    WriteableBitmap? _disc;

    public event Action<Color>? ColorChanged;

    public Color Color
    {
        get { var (r, g, b) = ColorMath.HsvToRgb(_hue, _saturation, _value); return Color.FromRgb(r, g, b); }
    }

    public double Brightness
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); InvalidateVisual(); ColorChanged?.Invoke(Color); }
    }

    /// <summary>Moves the marker to a colour without raising ColorChanged.</summary>
    public void Show(Color color)
    {
        (_hue, _saturation, _value) = ColorMath.RgbToHsv(color.R, color.G, color.B);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var radius = size / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var pixels = (int)Math.Ceiling(size * VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (_disc is null || _disc.PixelWidth != pixels) _disc = Disc(pixels);
        dc.DrawImage(_disc, new Rect(center.X - radius, center.Y - radius, size, size));
        if (_value < 1) dc.DrawEllipse(Theme.Frozen(Colors.Black, 1 - _value), null, center, radius, radius);

        var a = _hue * Math.PI / 180;
        var marker = new Point(center.X + Math.Cos(a) * _saturation * radius, center.Y - Math.Sin(a) * _saturation * radius);
        dc.DrawEllipse(Theme.Frozen(Color), new Pen(Brushes.White, 2.5), marker, 8, 8);
        dc.DrawEllipse(null, new Pen(Theme.Frozen(Colors.Black, 0.5), 1), marker, 9.5, 9.5);
    }

    /// <summary>The full-brightness wheel, drawn once per size. Edges are softened over one pixel.</summary>
    static WriteableBitmap Disc(int size)
    {
        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Pbgra32, null);
        var data = new byte[size * size * 4];
        var r = size / 2.0;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            double dx = x + 0.5 - r, dy = r - (y + 0.5);
            var d = Math.Sqrt(dx * dx + dy * dy);
            var alpha = Math.Clamp(r - d, 0, 1);
            if (alpha <= 0) continue;
            var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            var (cr, cg, cb) = ColorMath.HsvToRgb(hue, Math.Min(1, d / r), 1);
            var i = (y * size + x) * 4;
            data[i] = (byte)(cb * alpha); data[i + 1] = (byte)(cg * alpha); data[i + 2] = (byte)(cr * alpha); data[i + 3] = (byte)(255 * alpha);
        }
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), data, size * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { CaptureMouse(); Pick(e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured) Pick(e.GetPosition(this)); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { ReleaseMouseCapture(); e.Handled = true; }

    void Pick(Point p)
    {
        var radius = Math.Min(ActualWidth, ActualHeight) / 2;
        double dx = p.X - ActualWidth / 2, dy = ActualHeight / 2 - p.Y;
        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        _saturation = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / radius);
        if (_value < 0.05) _value = 1;          // picking a hue on a black wheel would show nothing
        InvalidateVisual();
        ColorChanged?.Invoke(Color);
    }
}
