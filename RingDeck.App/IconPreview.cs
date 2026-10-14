using System.Windows;
using System.Windows.Media;

namespace RingDeck.App;

/// <summary>A slice icon on its own coloured circle, as it will look in the ring (used by the editor).</summary>
public sealed class IconPreview : FrameworkElement
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(IconPreview),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(string), typeof(IconPreview),
        new FrameworkPropertyMetadata("#2D2D3A", FrameworkPropertyMetadataOptions.AffectsRender));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Fill { get => (string)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public string Label { get; set; } = "";

    protected override void OnRender(DrawingContext dc)
    {
        var r = Math.Min(ActualWidth, ActualHeight) / 2 - 1;
        if (r <= 0) return;
        var fill = Theme.Parse(Fill, Color.FromRgb(0x2D, 0x2D, 0x3A));
        var at = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(Theme.Frozen(fill), new Pen(Theme.Frozen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 1), at, r, r);
        var light = 0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B > 150;
        RingVisual.DrawIcon(dc, Icon, Label, at, r, light ? Color.FromRgb(0x16, 0x16, 0x1A) : Colors.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
