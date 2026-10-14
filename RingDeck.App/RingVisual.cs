using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>Draws one ring from its animator state. Used by the overlay and by the editor preview.</summary>
public sealed partial class RingVisual : FrameworkElement
{
    const double Hub = 38;
    static readonly FontFamily GlyphFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Emoji, Segoe UI Symbol, Segoe UI");
    static readonly Typeface GlyphFace = new(GlyphFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    static readonly Typeface TextFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Dictionary<string, ImageSource?> Images = new();

    public Ring? Ring { get; set; }
    public RingAnimator Animator { get; set; } = new();
    public int Selected { get; set; } = -1;
    /// <summary>A slice drawn faded: the one being dragged in the editor.</summary>
    public int Faded { get; set; } = -1;
    public double Radius { get; set; } = 120;
    public Vector Offset { get; set; }
    public Func<Guid, Ring?>? FindRing { get; set; }
    /// <summary>Where the slices sit; a nested ring fans away from its parent. Null: the whole circle.</summary>
    public RingLayout? Layout { get; set; }
    /// <summary>Where this ring's centre is, in ring units (the controller uses it to place the window and the offsets).</summary>
    public Point Center { get; set; }
    /// <summary>From this ring's centre to the slice of the parent ring that opened it: a line is drawn between them.</summary>
    public Vector? Link { get; set; }
    /// <summary>From this ring's centre to where it grows out of (the parent's slice): while it opens it travels from
    /// there to its place, and while it closes it shrinks back.</summary>
    public Vector? GrowFrom { get; set; }

    /// <summary>Where the centre ends up, and where it is at this point of the opening or closing.</summary>
    Point FinalCenter => new(ActualWidth / 2 + Offset.X, ActualHeight / 2 + Offset.Y);
    Point LiveCenter => GrowFrom is { } from ? FinalCenter + from * (1 - Math.Clamp(Animator.Pose.Alpha, 0, 1)) : FinalCenter;

    /// <summary>Below 1 for rings the user came through: they stay visible, faded, under the current ring.</summary>
    public double Dim { get; set; } = 1;
    /// <summary>The fade <see cref="Dim"/> moves toward, a little each frame.</summary>
    public double DimTarget { get; set; } = 1;

    RingLayout L => Layout is { } layout && Ring is { } ring && layout.Count == ring.Slices.Count ? layout : RingLayout.Full(Ring?.Slices.Count ?? 0);

    /// <summary>The line from the parent ring's slice to this ring's centre, under everything else.</summary>
    void DrawLink(DrawingContext dc, Color accent, double alpha)
    {
        if (Link is not { } link) return;
        var start = FinalCenter + link;
        var toward = start - LiveCenter;
        if (toward.Length < Hub) return;
        var end = LiveCenter + toward * (Hub / toward.Length);
        dc.PushOpacity(alpha);
        dc.DrawLine(new Pen(Theme.Frozen(accent, 0.22), 12) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, start, end);
        dc.DrawLine(new Pen(Theme.Frozen(accent, 0.9), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, start, end);
        dc.DrawEllipse(Theme.Frozen(accent), null, start, 4, 4);
        dc.Pop();
    }

    /// <summary>How far the selected slice's hover wait has got, 0 to 1 (see <see cref="RingSession.HoldProgress"/>).</summary>
    public double HoldProgress { get; set; } = 1;
    /// <summary>Draw in this style instead of the ring's own (developer pictures).</summary>
    public RingStyle? StyleOverride { get; set; }

    /// <summary>Half the side of the square that fits a ring through its whole open and close motion.</summary>
    public static double ExtentFor(double radius) => (radius * 1.5 + RingSession.SliceSize * 1.5) * 1.2 + 20;

    protected override void OnRender(DrawingContext dc)
    {
        var ring = Ring;
        if (ring is null || Animator.Phase == AnimPhase.Hidden) return;
        var style = StyleOverride ?? ring.StyleIn(App.Instance.Config?.Settings ?? new GlobalSettings());
        dc.PushOpacity(Math.Clamp(Dim, 0, 1));
        DrawLink(dc, Theme.Parse(ring.Accent, Theme.Accent), Math.Clamp(Animator.Pose.Alpha, 0, 1));
        if (style != RingStyle.Classic) DrawStyled(dc, ring, style); else DrawClassic(dc, ring);
        dc.Pop();
    }

    void DrawClassic(DrawingContext dc, Ring ring)
    {
        var pose = Animator.Pose;
        var center = LiveCenter;
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
        if (Selected < 0 && ring.Icon.Length > 0)
            DrawIcon(dc, ring.Icon, ring.Name, center, Hub * 0.72, Contrast(Theme.Parse(ring.Color, Color.FromRgb(0x1E, 0x1E, 0x24))), dpi);

        for (var i = 0; i < count; i++)
        {
            var slice = ring.Slices[i];
            var angle = L.Angle(i) + pose.Rotation;
            var at = Along(center, angle, Radius * pose.RadiusFactor);
            var scale = i < Animator.SliceScales.Count ? Animator.SliceScales[i] : 1;
            var r = RingSession.SliceSize / 2 * scale;
            var fillColor = Theme.Parse(slice.Color, Color.FromRgb(0x2D, 0x2D, 0x3A));
            var selected = i == Selected;
            if (i == Faded) dc.PushOpacity(0.25);
            if (selected) dc.DrawEllipse(Theme.Frozen(accentColor, 0.28), null, at, r + 7, r + 7);
            dc.DrawEllipse(Theme.Frozen(fillColor), new Pen(selected ? accent : edge, selected ? 2.5 : 1), at, r, r);
            DrawIcon(dc, IconOf(slice), slice.Label, at, r, Contrast(fillColor), dpi);
            HoldRing(dc, slice, i, at, r, accentColor);
            if (slice.Action is NestedRingAction)
                dc.DrawEllipse(accent, null, Along(center, angle, Radius * pose.RadiusFactor + r + 7), 3, 3);
            // The label stays under the slice while it is selected too (it is also shown in the centre);
            // it only moves down a little to clear the highlight ring.
            if (ring.ShowLabels && slice.Label.Length > 0)
                DrawText(dc, slice.Label, new Point(at.X, at.Y + r + (selected ? 9 : 3) + (slice.ConfirmMs > 0 ? 8 : 0)), 11, Theme.Frozen(Colors.White), 120, dpi, false, true);
            if (i == Faded) dc.Pop();
        }

        if (Selected >= 0 && Selected < count)
        {
            var slice = ring.Slices[Selected];
            // A faded ring in the trail already shows where it led; no preview of the child there.
            if (Dim >= 1 && slice.Action is NestedRingAction nested && FindRing?.Invoke(nested.RingId) is { } child)
            {
                var shown = Math.Min(child.Slices.Count, 7);
                for (var j = 0; j < shown; j++)
                {
                    var a = L.Angle(Selected) + pose.Rotation + (j - (shown - 1) / 2.0) * 13;
                    var dot = Along(center, a, Radius * pose.RadiusFactor + RingSession.SliceSize * 0.98);
                    dc.DrawEllipse(Theme.Frozen(Theme.Parse(child.Slices[j].Color, Colors.Gray)), new Pen(edge, 1), dot, 7, 7);
                }
            }
            DrawText(dc, slice.Label, center, 12.5, Theme.Frozen(Contrast(Theme.Parse(ring.Color, Colors.Black))), Hub * 2 - 12, dpi, true, false);
        }

        dc.Pop();
        dc.Pop();
    }

    /// <summary>For a slice that needs the pointer to rest on it: a faint circle around it, and while it is selected an
    /// arc that fills clockwise from the top; once full the slice can run.</summary>
    void HoldRing(DrawingContext dc, Slice slice, int index, Point at, double r, Color accent)
    {
        if (slice.ConfirmMs <= 0) return;
        var radius = r + 9;
        dc.DrawEllipse(null, new Pen(Theme.Frozen(accent, 0.3), 2), at, radius, radius);
        if (index != Selected) return;
        var progress = Math.Clamp(HoldProgress, 0, 1);
        var pen = new Pen(Theme.Frozen(accent), 3.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (progress >= 1) { dc.DrawEllipse(null, pen, at, radius, radius); return; }
        if (progress <= 0) return;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(Along(at, 0, radius), false, false);
            g.ArcTo(Along(at, 360 * progress, radius), new Size(radius, radius), 0, progress > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    /// <summary>A slice's icon; a slice that opens another ring and has none borrows that ring's icon.</summary>
    string IconOf(Slice slice) =>
        slice.Icon.Length == 0 && slice.Action is NestedRingAction nested && FindRing?.Invoke(nested.RingId) is { Icon.Length: > 0 } child ? child.Icon : slice.Icon;

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

    /// <summary>True when a brand colour stands out from the slice (judged against the text colour chosen for it).</summary>
    static bool Distinct(Color brand, Color textOnSlice)
    {
        static double L(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        // textOnSlice is white on dark slices and near-black on light ones; the brand colour must not be close to
        // the slice itself, i.e. far enough from the opposite end.
        var dark = L(textOnSlice) > 0.5;
        return dark ? L(brand) > 0.32 : L(brand) < 0.68;
    }

    static Color Contrast(Color background) =>
        0.299 * background.R + 0.587 * background.G + 0.114 * background.B > 150 ? Color.FromRgb(0x16, 0x16, 0x1A) : Colors.White;

    static void DrawIcon(DrawingContext dc, Slice slice, Point at, double r, Color color, double dpi) =>
        DrawIcon(dc, slice.Icon, slice.Label, at, r, color, dpi);

    /// <summary>Draws a slice icon of any kind: an emoji picture, an icon-font glyph, an imported image, a typed
    /// character, or (when there is no icon) the label's first letter.</summary>
    internal static void DrawIcon(DrawingContext dc, string icon, string label, Point at, double r, Color color, double dpi)
    {
        if (icon.StartsWith("brand:", StringComparison.Ordinal) && IconAssets.EmojiPicture(icon) is { } logo)
        {
            // Brand logos are white shapes: paint them in the brand's colour, unless it would vanish on this slice.
            var half = r * 0.62;
            var rect = new Rect(at.X - half, at.Y - half, half * 2, half * 2);
            var tint = IconAssets.BrandColor(icon) is { } brand && Distinct(brand, color) ? brand : color;
            dc.PushOpacityMask(new ImageBrush(logo));
            dc.DrawRectangle(Theme.Frozen(tint), null, rect);
            dc.Pop();
            return;
        }
        var picture = IconCatalog.Split(icon) switch
        {
            ("file", _) => Image(icon),
            ("emoji", _) => IconAssets.EmojiPicture(icon),
            _ => null,
        };
        if (picture is not null)
        {
            // Round crops from the image editor fill the slice; other pictures sit inside it.
            var half = icon.EndsWith(".fill.png", StringComparison.OrdinalIgnoreCase) ? r : r * (icon.StartsWith("emoji:", StringComparison.Ordinal) ? 0.7 : 0.62);
            dc.DrawImage(picture, new Rect(at.X - half, at.Y - half, half * 2, half * 2));
            return;
        }
        if (IconCatalog.IconText(icon) is { Length: > 0 } fluent)
        {
            var glyphText = new FormattedText(fluent, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(IconAssets.IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), r * 0.95, Theme.Frozen(color), dpi);
            dc.DrawText(glyphText, new Point(at.X - glyphText.Width / 2, at.Y - glyphText.Height / 2));
            return;
        }
        var slice = new Slice { Icon = icon, Label = label };
        var glyph = slice.Icon.StartsWith("glyph:", StringComparison.Ordinal) ? slice.Icon[6..] : "";
        var isLetter = glyph.Length == 0;
        if (isLetter) glyph = slice.Label.Trim().Length > 0 ? slice.Label.Trim()[..1].ToUpperInvariant() : "?";
        var text = new FormattedText(glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            isLetter ? TextFace : GlyphFace, r * 0.9, Theme.Frozen(color), dpi);
        dc.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
    }

    /// <summary>One slice on its own, slightly enlarged and highlighted: the picture that follows the mouse while a
    /// slice is dragged in the editor.</summary>
    public static void DrawLoose(DrawingContext dc, Slice slice, Point at, Color accentColor, double dpi)
    {
        var r = RingSession.SliceSize / 2 * 1.15;
        var fill = Theme.Parse(slice.Color, Color.FromRgb(0x2D, 0x2D, 0x3A));
        dc.DrawEllipse(Theme.Frozen(Colors.Black, 0.35), null, new Point(at.X + 2, at.Y + 6), r + 4, r + 4);   // soft shadow
        dc.DrawEllipse(Theme.Frozen(accentColor, 0.28), null, at, r + 8, r + 8);
        dc.DrawEllipse(Theme.Frozen(fill), new Pen(Theme.Frozen(accentColor), 2.5), at, r, r);
        DrawIcon(dc, slice, at, r, Contrast(fill), dpi);
        if (slice.Label.Length > 0)
            DrawText(dc, slice.Label, new Point(at.X, at.Y + r + 10), 12, Theme.Frozen(Colors.White), 140, dpi, false, true);
    }

    static void DrawText(DrawingContext dc, string value, Point at, double size, Brush brush, double maxWidth, double dpi, bool middle, bool shadow) =>
        DrawText(dc, value, at, size, brush, maxWidth, dpi, middle, shadow ? Color.FromArgb(0xBF, 0, 0, 0) : null);

    static void DrawText(DrawingContext dc, string value, Point at, double size, Brush brush, double maxWidth, double dpi, bool middle, Color? shadow, bool glow = false)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TextFace, size, brush, dpi)
        {
            MaxTextWidth = maxWidth, MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
        };
        var origin = new Point(at.X - maxWidth / 2, middle ? at.Y - text.Height / 2 : at.Y);
        if (shadow is { } shade)
        {
            var dark = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TextFace, size, Theme.Frozen(shade), dpi)
            {
                MaxTextWidth = maxWidth, MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
            };
            // A glow is the shadow drawn all around the text; a shadow sits below and to the right.
            Vector[] offsets = glow ? [new(-1, 0), new(1, 0), new(0, -1), new(0, 1), new(-1.5, -1.5), new(1.5, 1.5), new(-1.5, 1.5), new(1.5, -1.5)] : [new(1, 1)];
            foreach (var o in offsets) dc.DrawText(dark, origin + o);
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
