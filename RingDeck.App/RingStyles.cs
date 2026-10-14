using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>The ring styles other than Classic: <see cref="RingStyle.Soft"/>, <see cref="RingStyle.Clean"/>,
/// <see cref="RingStyle.Neon"/> and <see cref="RingStyle.Labels"/>. Slice positions, sizes and selection are the same as in the classic style;
/// only the drawing differs.</summary>
public sealed partial class RingVisual
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly Color Ink = Color.FromRgb(0x33, 0x33, 0x33);

    /// <summary>Everything the styles share for one frame.</summary>
    sealed class Frame
    {
        public required DrawingContext Dc;
        public required Ring Ring;
        public required Point Center;
        public required double Dpi;
        public required Color Accent;
        public required double Spread;      // distance of the slices from the centre at this point of the animation
        public required double Hover;       // 0..1: how far the selected slice has grown (0 when nothing is selected)
        public int Count => Ring.Slices.Count;
    }

    void DrawStyled(DrawingContext dc, Ring ring, RingStyle style)
    {
        var pose = Animator.Pose;
        var center = LiveCenter;
        var hover = Selected >= 0 && Selected < Animator.SliceScales.Count
            ? Math.Clamp((Animator.SliceScales[Selected] - 1) / (RingAnimator.SelectedScale - 1), 0, 1) : 0;
        var f = new Frame
        {
            Dc = dc, Ring = ring, Center = center, Dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip,
            Accent = Theme.Parse(ring.Accent, Theme.Accent), Spread = Radius * pose.RadiusFactor, Hover = hover,
        };
        dc.PushOpacity(Math.Clamp(pose.Alpha, 0, 1));
        dc.PushTransform(new ScaleTransform(pose.Scale, pose.Scale, center.X, center.Y));
        switch (style)
        {
            case RingStyle.Soft: DrawSoft(f); break;
            case RingStyle.Clean: DrawClean(f); break;
            case RingStyle.Neon: DrawNeon(f); break;
            case RingStyle.Labels: DrawLabels(f); break;
        }
        dc.Pop();
        dc.Pop();
    }

    double AngleOf(int i, Frame f) => L.Angle(i) + Animator.Pose.Rotation;

    Point PositionOf(int i, Frame f) => Along(f.Center, AngleOf(i, f), f.Spread);

    /// <summary>A slice's size factor. While a slice is selected the others grow too, more the closer they are to
    /// the pointer; the selected one grows to <paramref name="selected"/>.</summary>
    double SizeOf(int i, Frame f, double selected, double neighbours, double falloff)
    {
        var own = i < Animator.SliceScales.Count ? Math.Clamp((Animator.SliceScales[i] - 1) / (RingAnimator.SelectedScale - 1), 0, 1) : 0;
        var diff = Math.Abs(((AngleOf(i, f) - Animator.Pose.Rotation - Animator.PointerAngle) % 360 + 540) % 360 - 180);
        var near = Animator.HasPointer ? neighbours - Math.Pow(diff / 180, falloff) * (neighbours - 1) : 1;
        var resting = 1 + f.Hover * (near - 1);
        return resting + own * (selected - resting);
    }

    // ---- Soft: round buttons with shadows, a shaded disc with the chosen wedge lit up.

    void DrawSoft(Frame f)
    {
        var dc = f.Dc;
        var border = Color.FromRgb(0x6D, 0x6D, 0x6D);
        var outer = f.Spread + RingSession.SliceSize * 1.1;
        if (f.Count > 0 && outer > Hub)
        {
            var shade = RadialFade(f.Center, outer, Color.FromArgb(0x40, 0, 0, 0), Hub / outer);
            Geometry disc = new EllipseGeometry(f.Center, outer, outer);
            if (Selected >= 0 && Selected < f.Count && f.Count > 1)
            {
                var half = L.Step / 2;
                var wedge = Sector(f.Center, 0, outer, AngleOf(Selected, f) - half, AngleOf(Selected, f) + half);
                disc = new CombinedGeometry(GeometryCombineMode.Exclude, disc, wedge);
                dc.DrawGeometry(RadialFade(f.Center, outer, Color.FromArgb((byte)(0x50 * f.Hover), f.Accent.R, f.Accent.G, f.Accent.B), Hub / outer), null, wedge);
            }
            dc.DrawGeometry(shade, null, disc);
        }

        var hub = Hub * (1 + 0.1 * f.Hover);
        var centre = Theme.Parse(f.Ring.Color, Color.FromRgb(0x1E, 0x1E, 0x24));
        Shadow(dc, f.Center + new Vector(2, 2), hub, 10, 0.5);
        dc.DrawEllipse(Theme.Frozen(centre), new Pen(Theme.Frozen(border), 1), f.Center, hub, hub);

        for (var i = 0; i < f.Count; i++)
        {
            var slice = f.Ring.Slices[i];
            var at = PositionOf(i, f);
            var r = RingSession.SliceSize / 2 * SizeOf(i, f, 1.15, 1.15, 0.25);
            var selected = i == Selected;
            if (i == Faded) dc.PushOpacity(0.25);
            if (Dim >= 1 && slice.Action is NestedRingAction nested) Grandchildren(f, nested, at, r + 1, 9, _ => border);
            var fill = Theme.Parse(slice.Color, Color.FromRgb(0x2D, 0x2D, 0x3A));
            if (selected) fill = Mix(fill, f.Accent, 0.45 * f.Hover);
            Shadow(dc, at + new Vector(2, 2), r, 10, 0.5);
            dc.DrawEllipse(Theme.Frozen(fill), new Pen(Theme.Frozen(selected ? Mix(border, f.Accent, f.Hover) : border), 1), at, r, r);
            DrawIcon(dc, IconOf(slice), slice.Label, at, r, Contrast(fill), f.Dpi);
            HoldRing(dc, slice, i, at, r, f.Accent);
            if (f.Ring.ShowLabels && slice.Label.Length > 0)
                DrawText(dc, slice.Label, new Point(at.X, at.Y + r + 4 + (slice.ConfirmMs > 0 ? 10 : 0)), 11, Theme.Frozen(Colors.White), 120, f.Dpi, false, true);
            if (i == Faded) dc.Pop();
        }
        CentreLabel(f, Contrast(centre), null, hub);
    }

    // ---- Clean: icons float on their own; a white disc in the middle points at the choice.

    void DrawClean(Frame f)
    {
        var dc = f.Dc;
        var centre = Theme.Parse(f.Ring.Color, Color.FromRgb(0x1E, 0x1E, 0x24));
        var iconColour = Mix(Color.FromRgb(0xFF, 0xC8, 0xE1), f.Accent, 0.5);

        Shadow(dc, f.Center + new Vector(0, 5), Hub, 10, 0.3);
        if (Animator.HasPointer && Selected >= 0)
        {
            // The little arrow on the disc's edge, in the disc's colour.
            var tip = Along(f.Center, Animator.PointerAngle, Hub + 9 * f.Hover);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(Along(f.Center, Animator.PointerAngle - 14, Hub - 2), true, true);
                g.LineTo(tip, true, true);
                g.LineTo(Along(f.Center, Animator.PointerAngle + 14, Hub - 2), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(Theme.Frozen(centre), null, geometry);
        }
        dc.DrawEllipse(Theme.Frozen(centre), null, f.Center, Hub, Hub);

        for (var i = 0; i < f.Count; i++)
        {
            var slice = f.Ring.Slices[i];
            var at = PositionOf(i, f);
            var r = RingSession.SliceSize / 2 * SizeOf(i, f, 1.2, 1.1, 0.2);
            if (i == Faded) dc.PushOpacity(0.25);
            if (slice.Action is NestedRingAction)
            {
                // Rings inside rings keep a disc.
                r *= 0.85;
                Shadow(dc, at + new Vector(0, 5), r, 10, 0.3);
                dc.DrawEllipse(Theme.Frozen(centre), null, at, r, r);
                DrawIcon(dc, IconOf(slice), slice.Label, at, r, Contrast(centre), f.Dpi);
            }
            else
            {
                Shadow(dc, at + new Vector(0, 3), r * 0.75, r * 0.5, 0.45);
                DrawIcon(dc, IconOf(slice), slice.Label, at, r, iconColour, f.Dpi);
            }
            HoldRing(dc, slice, i, at, r, f.Accent);
            if (f.Ring.ShowLabels && slice.Label.Length > 0)
                DrawText(dc, slice.Label, new Point(at.X, at.Y + r + 2 + (slice.ConfirmMs > 0 ? 10 : 0)), 11, Theme.Frozen(Colors.White), 120, f.Dpi, false, true);
            if (i == Faded) dc.Pop();
        }
        CentreLabel(f, Contrast(centre), null, Hub);
    }

    // ---- Neon: glowing icons, spinning rings in the middle, a faint lit wedge.

    void DrawNeon(Frame f)
    {
        var dc = f.Dc;
        var glow = f.Accent;
        var outer = f.Spread + RingSession.SliceSize * 2.2;
        var inner = Hub * 1.3;

        if (f.Count > 1 && outer > inner)
        {
            var half = L.Step / 2;
            if (Selected >= 0 && Selected < f.Count)
            {
                var wedge = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = f.Center, GradientOrigin = f.Center, RadiusX = outer, RadiusY = outer };
                wedge.GradientStops.Add(new GradientStop(Colors.Transparent, inner / outer));
                wedge.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(0x24 * f.Hover), glow.R, glow.G, glow.B), (inner + 50) / outer));
                wedge.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                wedge.Freeze();
                dc.DrawGeometry(wedge, null, Sector(f.Center, inner, outer, AngleOf(Selected, f) - half, AngleOf(Selected, f) + half));
            }
            var line = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = f.Center, GradientOrigin = f.Center, RadiusX = outer, RadiusY = outer };
            line.GradientStops.Add(new GradientStop(Colors.Transparent, inner / outer));
            line.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), Math.Min(1, (inner + 40) / outer)));
            line.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            line.Freeze();
            var pen = new Pen(line, 1);
            for (var i = 0; i < f.Count; i++)
            {
                var a = AngleOf(i, f) + half;
                dc.DrawLine(pen, Along(f.Center, a, inner), Along(f.Center, a, outer));
            }
            if (!L.IsFull) dc.DrawLine(pen, Along(f.Center, AngleOf(0, f) - half, inner), Along(f.Center, AngleOf(0, f) - half, outer));
        }

        // The resting ring turns slowly in grey; when a slice is chosen a bright one, twice as big, spins fast.
        var seconds = Clock.Elapsed.TotalSeconds;
        Shadow(dc, f.Center, Hub * 1.15, Hub * 0.35, 0.3 * f.Hover);
        if (f.Hover < 1) Dashes(dc, f.Center, Hub * (0.62 + 0.3 * f.Hover), seconds * 36, Color.FromRgb(120, 120, 120), Color.FromRgb(120, 120, 120), 1 - f.Hover);
        if (f.Hover > 0) Dashes(dc, f.Center, Hub * (0.9 + 0.35 * f.Hover), seconds * 180, Colors.White, glow, f.Hover);
        if (Animator.HasPointer && Selected >= 0)
        {
            // A glowing chevron just outside the spinning ring, pointing at the choice.
            var r = Hub * 1.25 + 10;
            var a = Animator.PointerAngle;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(Along(f.Center, a - 9, r), false, false);
                g.LineTo(Along(f.Center, a, r + 8), true, true);
                g.LineTo(Along(f.Center, a + 9, r), true, true);
            }
            geometry.Freeze();
            dc.PushOpacity(f.Hover);
            dc.DrawGeometry(null, new Pen(Theme.Frozen(glow, 0.35), 7) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
            dc.DrawGeometry(null, new Pen(Theme.Frozen(Colors.White), 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
            dc.Pop();
        }

        for (var i = 0; i < f.Count; i++)
        {
            var slice = f.Ring.Slices[i];
            var at = PositionOf(i, f);
            var r = RingSession.SliceSize / 2 * SizeOf(i, f, 1.15, 1.15, 0.25);
            var selected = i == Selected;
            if (i == Faded) dc.PushOpacity(0.25);
            Shadow(dc, at, r * 0.9, r * 0.6, 0.25);
            Halo(dc, at, r * (selected ? 1.15 : 0.95), glow, selected ? 0.55 + 0.25 * f.Hover : 0.4);
            DrawIcon(dc, IconOf(slice), slice.Label, at, r * 0.9, Colors.White, f.Dpi);
            HoldRing(dc, slice, i, at, r * 0.9, f.Accent);
            if (slice.Action is NestedRingAction)
            {
                var dot = Along(f.Center, AngleOf(i, f), f.Spread + r + 8);
                Halo(dc, dot, 7, glow, 0.6);
                dc.DrawEllipse(Theme.Frozen(Colors.White), null, dot, 2.2, 2.2);
            }
            if (f.Ring.ShowLabels && slice.Label.Length > 0)
                DrawText(dc, slice.Label, new Point(at.X, at.Y + r + 3 + (slice.ConfirmMs > 0 ? 10 : 0)), 11, Theme.Frozen(Colors.White), 120, f.Dpi, false, Color.FromArgb(0x28, glow.R, glow.G, glow.B), true);
            if (i == Faded) dc.Pop();
        }
        CentreLabel(f, Colors.White, Color.FromArgb(0x38, glow.R, glow.G, glow.B), Hub * 1.2);
    }

    // ---- Labels: small pastel buttons, each with its name in a pill coloured by direction.

    void DrawLabels(Frame f)
    {
        var dc = f.Dc;
        var centre = Theme.Parse(f.Ring.Color, Color.FromRgb(0x1E, 0x1E, 0x24));
        var pillFill = Color.FromRgb(0x33, 0x33, 0x33);
        Shadow(dc, f.Center, Hub, 15, 0.4);
        dc.DrawEllipse(Theme.Frozen(centre), null, f.Center, Hub, Hub);
        HubIcon(f, Contrast(centre), Hub);

        // Pills first, so every button sits on top of its own pill and of its neighbours' pills.
        var radii = new double[f.Count];
        for (var i = 0; i < f.Count; i++)
        {
            var slice = f.Ring.Slices[i];
            var r = radii[i] = RingSession.SliceSize / 2 * 0.75 * SizeOf(i, f, 1.06, 1, 1);
            if (!f.Ring.ShowLabels || slice.Label.Length == 0) continue;
            var at = PositionOf(i, f);
            var hue = Norm(AngleOf(i, f));
            var selected = i == Selected;
            if (i == Faded) dc.PushOpacity(0.25);
            var text = new FormattedText(slice.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TextFace, r * 2 / 2.7,
                Theme.Frozen(Color.FromRgb(0xDD, 0xDD, 0xDD)), f.Dpi) { MaxTextWidth = 180, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            var h = r * 2;
            Rect pill;
            Point textAt;
            if (hue <= 22.5 || hue >= 337.5 || Math.Abs(hue - 180) <= 22.5)
            {
                // Straight up or down: the pill sits above or below the button, centred.
                var w = text.Width + h * 0.8;
                var top = hue > 90 && hue < 270 ? at.Y + r * 0.8 : at.Y - r * 0.8 - h;
                pill = new Rect(at.X - w / 2, top, w, h);
                textAt = new Point(at.X - text.Width / 2, top + (h - text.Height) / 2);
            }
            else if (hue < 180)
            {
                var w = h + 5 + text.Width + h / 3;
                pill = new Rect(at.X - r, at.Y - r, w, h);
                textAt = new Point(at.X + r + 5, at.Y - text.Height / 2);
            }
            else
            {
                var w = h + 5 + text.Width + h / 3;
                pill = new Rect(at.X + r - w, at.Y - r, w, h);
                textAt = new Point(at.X - r - 5 - text.Width, at.Y - text.Height / 2);
            }
            Shadow(dc, new Point(pill.X + pill.Width / 2, pill.Y + pill.Height / 2), Math.Min(pill.Width, pill.Height) / 2, 10, 0.3);
            var edge = Hsl(hue, 0.5, selected ? 0.6 + 0.25 * f.Hover : 0.6);
            var fill = selected ? Mix(pillFill, Hsl(hue, 0.8, 0.8), 0.18 * f.Hover) : pillFill;
            dc.DrawRoundedRectangle(Theme.Frozen(fill), new Pen(Theme.Frozen(edge), 2), pill, h / 2, h / 2);
            dc.DrawText(text, textAt);
            if (i == Faded) dc.Pop();
        }

        for (var i = 0; i < f.Count; i++)
        {
            var slice = f.Ring.Slices[i];
            var at = PositionOf(i, f);
            var hue = Norm(AngleOf(i, f));
            var r = radii[i];
            if (i == Faded) dc.PushOpacity(0.25);
            if (Dim >= 1 && slice.Action is NestedRingAction nested) Grandchildren(f, nested, at, r * 0.95, 6, _ => Hsl(hue, 0.5, 0.8));
            var fill = Hsl(hue, 0.5, i == Selected ? 0.8 + 0.1 * f.Hover : 0.8);
            Shadow(dc, at, r, 12, 0.4);
            dc.DrawEllipse(Theme.Frozen(fill), null, at, r, r);
            DrawIcon(dc, IconOf(slice), slice.Label, at, r, Ink, f.Dpi);
            HoldRing(dc, slice, i, at, r, f.Accent);
            if (i == Faded) dc.Pop();
        }
    }

    // ---- Shared pieces.

    /// <summary>The selected slice's name in the middle of the ring.</summary>
    void CentreLabel(Frame f, Color colour, Color? glow, double hub)
    {
        if (Selected < 0 || Selected >= f.Count) { HubIcon(f, colour, hub); return; }
        DrawText(f.Dc, f.Ring.Slices[Selected].Label, f.Center, 12.5, Theme.Frozen(colour), hub * 2 - 12, f.Dpi, true, glow, glow is not null);
    }

    /// <summary>The ring's own icon in the middle, while nothing is chosen.</summary>
    void HubIcon(Frame f, Color colour, double hub)
    {
        if (Selected < 0 && f.Ring.Icon.Length > 0) DrawIcon(f.Dc, f.Ring.Icon, f.Ring.Name, f.Center, hub * 0.72, colour, f.Dpi);
    }

    /// <summary>Small dots peeking out behind a slice that opens another ring, one per slice of that ring, in the
    /// directions those slices will appear.</summary>
    void Grandchildren(Frame f, NestedRingAction nested, Point at, double distance, double size, Func<int, Color> colour)
    {
        if (FindRing?.Invoke(nested.RingId) is not { } child || child.Slices.Count == 0) return;
        var shown = Math.Min(child.Slices.Count, 12);
        for (var j = 0; j < shown; j++)
        {
            var dot = Along(at, RingGeometry.SliceAngle(j, child.Slices.Count), distance);
            f.Dc.DrawEllipse(Theme.Frozen(colour(j)), null, dot, size / 2 + 0.5, size / 2 + 0.5);
        }
    }

    /// <summary>A soft round shadow: dark in the middle, fading out over <paramref name="blur"/>.</summary>
    static void Shadow(DrawingContext dc, Point at, double r, double blur, double alpha)
    {
        if (alpha <= 0) return;
        var total = r + blur;
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * alpha), 0, 0, 0), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * alpha), 0, 0, 0), Math.Max(0, (r - blur) / total)));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        brush.Freeze();
        dc.DrawEllipse(brush, null, at, total, total);
    }

    static void Halo(DrawingContext dc, Point at, double r, Color colour, double alpha)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * alpha), colour.R, colour.G, colour.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(160 * alpha), colour.R, colour.G, colour.B), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 1));
        brush.Freeze();
        dc.DrawEllipse(brush, null, at, r * 1.4, r * 1.4);
    }

    /// <summary>A dashed ring turned by <paramref name="turn"/> degrees, with a glow in <paramref name="glow"/>.</summary>
    static void Dashes(DrawingContext dc, Point center, double radius, double turn, Color core, Color glow, double alpha)
    {
        if (alpha <= 0) return;
        (double From, double To)[] parts = [(0, 75), (95, 115), (135, 145), (180, 255), (275, 295), (315, 325)];
        var soft = new Pen(Theme.Frozen(glow, 0.3), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var sharp = new Pen(Theme.Frozen(core), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.PushOpacity(alpha);
        foreach (var (from, to) in parts)
        {
            var arc = Arc(center, radius, from + turn, to + turn);
            dc.DrawGeometry(null, soft, arc);
            dc.DrawGeometry(null, sharp, arc);
        }
        dc.Pop();
    }

    /// <summary>A pie slice (or ring slice, with an inner radius) between two angles, clockwise from straight up.</summary>
    static Geometry Sector(Point c, double inner, double outer, double from, double to)
    {
        var large = to - from > 180;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(Along(c, from, outer), true, true);
            g.ArcTo(Along(c, to, outer), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, false);
            if (inner > 0)
            {
                g.LineTo(Along(c, to, inner), true, false);
                g.ArcTo(Along(c, from, inner), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, false);
            }
            else g.LineTo(c, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>A brush that is <paramref name="colour"/> out to <paramref name="solidTo"/> (a fraction of the radius)
    /// and then fades to nothing at the edge.</summary>
    static Brush RadialFade(Point center, double radius, Color colour, double solidTo)
    {
        var brush = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = center, GradientOrigin = center, RadiusX = radius, RadiusY = radius };
        brush.GradientStops.Add(new GradientStop(colour, 0));
        brush.GradientStops.Add(new GradientStop(colour, Math.Clamp(solidTo, 0, 1)));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 1));
        brush.Freeze();
        return brush;
    }

    static double Norm(double angle) => (angle % 360 + 360) % 360;

    static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    static Color Hsl(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var h = Norm(hue) / 60;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var (r, g, b) = h switch { < 1 => (c, x, 0.0), < 2 => (x, c, 0.0), < 3 => (0.0, c, x), < 4 => (0.0, x, c), < 5 => (x, 0.0, c), _ => (c, 0.0, x) };
        var m = lightness - c / 2;
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
