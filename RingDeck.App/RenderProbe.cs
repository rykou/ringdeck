using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>Developer aid: `RingDeck.exe --render out.png [slice]` draws the first ring to a PNG and exits, so the
/// renderer can be checked without touching the keyboard hooks.</summary>
static class RenderProbe
{
    public static void Render(AppConfig config, string path, int selected, RingStyle? style = null)
    {
        if (config.Rings.FirstOrDefault() is not { } ring) return;
        var radius = RingGeometry.EffectiveRadius(ring.Radius, ring.Slices.Count, RingSession.SliceSize);
        var side = RingVisual.ExtentFor(radius) * 2;
        var visual = new RingVisual { Ring = ring, Radius = radius, Selected = selected, FindRing = config.Find, StyleOverride = style, Width = side, Height = side };
        visual.Animator.ShowStatic(ring.Slices.Count);
        // RINGDECK_HOLD=0.6 draws the selected slice's hover wait 60% of the way.
        if (double.TryParse(Environment.GetEnvironmentVariable("RINGDECK_HOLD"), System.Globalization.CultureInfo.InvariantCulture, out var hold)) visual.HoldProgress = hold;
        if (selected >= 0)
            for (var i = 0; i < 30; i++)
                visual.Animator.Tick(1 / 60.0, RingGeometry.SliceAngle(selected, ring.Slices.Count), selected, true, false, true);
        visual.Measure(new Size(side, side));
        visual.Arrange(new Rect(0, 0, side, side));
        var bitmap = new RenderTargetBitmap((int)(side * 2), (int)(side * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>`--render-trail out.png [depth] [style]`: opens the first ring, clicks through up to
    /// <paramref name="depth"/> nested slices, and draws every ring on screen the way the overlay does.</summary>
    public static void RenderTrail(AppConfig config, string path, int depth, RingStyle? style)
    {
        if (config.Rings.FirstOrDefault() is not { } root) return;
        var session = new RingSession(config.Find);
        session.Open(root, 0, 0);
        for (var level = 0; level < depth; level++)
        {
            var index = session.Current!.Slices.FindIndex(s => s.Action is NestedRingAction);
            if (index < 0) break;
            var (x, y) = RingGeometry.PointAt(session.Layout.Angle(index), session.Radius);
            if (session.Choose(session.CenterX + x, session.CenterY + y).Kind != ChooseKind.EnteredNested) break;
        }
        var visuals = new List<RingVisual>();
        var trail = session.Trail;
        for (var i = 0; i < trail.Count; i++)
        {
            var t = trail[i];
            var v = new RingVisual { Ring = t.Ring, Radius = t.Radius, Layout = t.Layout, Selected = t.Selected, Dim = 0.45, FindRing = config.Find, StyleOverride = style, Center = new Point(t.X, t.Y) };
            if (i > 0) { var p = trail[i - 1]; var (lx, ly) = RingGeometry.PointAt(p.Layout.Angle(p.Selected), p.Radius); v.Link = new Vector(p.X + lx - t.X, p.Y + ly - t.Y); }
            v.Animator.ShowStatic(t.Ring.Slices.Count);
            v.Animator.Tick(1, t.Layout.Angle(t.Selected), t.Selected, false, true, false);
            visuals.Add(v);
        }
        var current = new RingVisual
        {
            Ring = session.Current, Radius = session.Radius, Layout = session.Layout, FindRing = config.Find, StyleOverride = style,
            Center = new Point(session.CenterX, session.CenterY),
            Link = session.Link is { } link ? new Vector(link.X - session.CenterX, link.Y - session.CenterY) : null,
        };
        // RINGDECK_GROW=0.08 draws the newest ring that many seconds into its opening (growing out of its slice).
        if (double.TryParse(Environment.GetEnvironmentVariable("RINGDECK_GROW"), System.Globalization.CultureInfo.InvariantCulture, out var grow))
        {
            current.GrowFrom = current.Link;
            current.Animator.Open(session.Current!.Slices.Count);
            for (var t = 0.0; t < grow; t += 1 / 120.0) current.Animator.Tick(1 / 120.0, null, -1, true, false, true);
        }
        else
        {
            current.Animator.ShowStatic(session.Current!.Slices.Count);
            // point at the first slice of the newest ring, so its highlight shows too
            current.Selected = 0;
            current.Animator.Tick(1, session.Layout.Angle(0), 0, false, true, false);
        }
        visuals.Add(current);

        double left = visuals.Min(v => v.Center.X - RingVisual.ExtentFor(v.Radius)), right = visuals.Max(v => v.Center.X + RingVisual.ExtentFor(v.Radius));
        double top = visuals.Min(v => v.Center.Y - RingVisual.ExtentFor(v.Radius)), bottom = visuals.Max(v => v.Center.Y + RingVisual.ExtentFor(v.Radius));
        double width = right - left, height = bottom - top, cx = (left + right) / 2, cy = (top + bottom) / 2;
        var grid = new System.Windows.Controls.Grid { Width = width, Height = height, Background = Brushes.Transparent };
        foreach (var v in visuals) { v.Offset = new Vector(v.Center.X - cx, v.Center.Y - cy); grid.Children.Add(v); }
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap((int)(width * 2), (int)(height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
