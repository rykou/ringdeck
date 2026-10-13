using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>Developer aid: `RingDeck.exe --render out.png [slice]` draws the first ring to a PNG and exits, so the
/// renderer can be checked without touching the keyboard hooks.</summary>
static class RenderProbe
{
    public static void Render(AppConfig config, string path, int selected)
    {
        if (config.Rings.FirstOrDefault() is not { } ring) return;
        var radius = RingGeometry.EffectiveRadius(ring.Radius, ring.Slices.Count, RingSession.SliceSize);
        var side = RingVisual.ExtentFor(radius) * 2;
        var visual = new RingVisual { Ring = ring, Radius = radius, Selected = selected, FindRing = config.Find, Width = side, Height = side };
        visual.Animator.ShowStatic(ring.Slices.Count);
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
}
