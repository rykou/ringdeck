using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>Draws one ring from its animator state. Used by the overlay and by the editor preview.</summary>
public sealed class RingVisual : FrameworkElement
{
    const double Hub = 38;
    static readonly FontFamily GlyphFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Emoji, Segoe UI Symbol, Segoe UI");
    static readonly Typeface GlyphFace = new(GlyphFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    static readonly Typeface TextFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Dictionary<string, ImageSource?> Images = new();

    public Ring? Ring { get; set; }
    public RingAnimator Animator { get; set; } = new();
    public int Selected { get; set; } = -1;
    public double Radius { get; set; } = 120;
    public Vector Offset { get; set; }
    public Func<Guid, Ring?>? FindRing { get; set; }

    /// <summary>Half the side of the square that fits a ring through its whole open and close motion.</summary>
    public static double ExtentFor(double radius) => (radius * 1.5 + RingSession.SliceSize * 1.5) * 1.2 + 20;

    protected override void OnRender(DrawingContext dc)
    {
        var ring = Ring;
        if (ring is null || Animator.Phase == AnimPhase.Hidden) return;
        var pose = Animator.Pose;
        var center = new Point(ActualWidth / 2 + Offset.X, ActualHeight / 2 + Offset.Y);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var accentColor = Theme.Parse(ring.Accent, Theme.Accent);
        var accent = Theme.Frozen(accentColor);
        var edge = Theme.Frozen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
        var count = ring.Slices.Count;

        dc.PushOpacity(Math.Clamp(pose.Alpha, 0, 1));
        dc.PushTransform(new ScaleTransform(pose.Scale, pose.Scale, center.X, center.Y));

        dc.DrawEllipse(Theme.Frozen(Theme.Parse(ring.Color, Color.FromRgb(0x1E, 0x1E, 0x24)), 0.94), new Pen(edge, 1.5), center, Hub, Hub);
        if (Animator.HasPointer && Selected >= 0)
        {
            var pen = new Pen(accent, 4.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, Arc(center, Hub, Animator.PointerAngle - 26, Animator.PointerAngle + 26));
        }

        for (var i = 0; i < count; i++)
        {
            var slice = ring.Slices[i];
            var angle = RingGeometry.SliceAngle(i, count) + pose.Rotation;
            var at = Along(center, angle, Radius * pose.RadiusFactor);
            var scale = i < Animator.SliceScales.Count ? Animator.SliceScales[i] : 1;
            var r = RingSession.SliceSize / 2 * scale;
            var fillColor = Theme.Parse(slice.Color, Color.FromRgb(0x2D, 0x2D, 0x3A));
            var selected = i == Selected;
            if (selected) dc.DrawEllipse(Theme.Frozen(accentColor, 0.28), null, at, r + 7, r + 7);
            dc.DrawEllipse(Theme.Frozen(fillColor), new Pen(selected ? accent : edge, selected ? 2.5 : 1), at, r, r);
            DrawIcon(dc, slice, at, r, Contrast(fillColor), dpi);
            if (slice.Action is NestedRingAction)
                dc.DrawEllipse(accent, null, Along(center, angle, Radius * pose.RadiusFactor + r + 7), 3, 3);
            if (ring.ShowLabels && !selected && slice.Label.Length > 0)
                DrawText(dc, slice.Label, new Point(at.X, at.Y + r + 3), 11, Theme.Frozen(Colors.White), 120, dpi, false, true);
        }

        if (Selected >= 0 && Selected < count)
        {
            var slice = ring.Slices[Selected];
            if (slice.Action is NestedRingAction nested && FindRing?.Invoke(nested.RingId) is { } child)
            {
                var shown = Math.Min(child.Slices.Count, 7);
                for (var j = 0; j < shown; j++)
                {
                    var a = RingGeometry.SliceAngle(Selected, count) + pose.Rotation + (j - (shown - 1) / 2.0) * 13;
                    var dot = Along(center, a, Radius * pose.RadiusFactor + RingSession.SliceSize * 0.98);
                    dc.DrawEllipse(Theme.Frozen(Theme.Parse(child.Slices[j].Color, Colors.Gray)), new Pen(edge, 1), dot, 7, 7);
                }
            }
            DrawText(dc, slice.Label, center, 12.5, Theme.Frozen(Contrast(Theme.Parse(ring.Color, Colors.Black))), Hub * 2 - 12, dpi, true, false);
        }

        dc.Pop();
        dc.Pop();
    }

    static Point Along(Point center, double angle, double radius)
    {
        var (x, y) = RingGeometry.PointAt(angle, radius);
        return new Point(center.X + x, center.Y + y);
    }

    static Geometry Arc(Point center, double radius, double from, double to)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(Along(center, from, radius), false, false);
            g.ArcTo(Along(center, to, radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    static Color Contrast(Color background) =>
        0.299 * background.R + 0.587 * background.G + 0.114 * background.B > 150 ? Color.FromRgb(0x16, 0x16, 0x1A) : Colors.White;

    void DrawIcon(DrawingContext dc, Slice slice, Point at, double r, Color color, double dpi)
    {
        if (slice.Icon.StartsWith("file:", StringComparison.Ordinal) && Image(slice.Icon) is { } image)
        {
            var half = r * 0.62;
            dc.DrawImage(image, new Rect(at.X - half, at.Y - half, half * 2, half * 2));
            return;
        }
        var glyph = slice.Icon.StartsWith("glyph:", StringComparison.Ordinal) ? slice.Icon[6..] : "";
        var isLetter = glyph.Length == 0;
        if (isLetter) glyph = slice.Label.Trim().Length > 0 ? slice.Label.Trim()[..1].ToUpperInvariant() : "?";
        var text = new FormattedText(glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            isLetter ? TextFace : GlyphFace, r * 0.9, Theme.Frozen(color), dpi);
        dc.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
    }

    static void DrawText(DrawingContext dc, string value, Point at, double size, Brush brush, double maxWidth, double dpi, bool middle, bool shadow)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TextFace, size, brush, dpi)
        {
            MaxTextWidth = maxWidth, MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
        };
        var origin = new Point(at.X - maxWidth / 2, middle ? at.Y - text.Height / 2 : at.Y);
        if (shadow)
        {
            var dark = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TextFace, size, Theme.Frozen(Colors.Black, 0.75), dpi)
            {
                MaxTextWidth = maxWidth, MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
            };
            dc.DrawText(dark, new Point(origin.X + 1, origin.Y + 1));
        }
        dc.DrawText(text, origin);
    }

    static ImageSource? Image(string icon)
    {
        if (Images.TryGetValue(icon, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            if (App.Instance.Icons.PathOf(icon) is { } path)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
        }
        catch (Exception) { }   // damaged or unsupported image: draw the fallback glyph, and cache the miss
        return Images[icon] = image;
    }
}
