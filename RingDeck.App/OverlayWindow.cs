using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using RingDeck.Windows;

namespace RingDeck.App;

public sealed class OverlayWindow : Window
{
    readonly ScaleTransform _userScale = new(1, 1);
    IntPtr _hwnd;

    public OverlayWindow()
    {
        ThemeMode = ThemeMode.None;          // the Fluent backdrop would paint an opaque background
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Left = Top = -32000;
        Width = Height = 16;
        Content = new Grid { LayoutTransform = _userScale, Children = { Current } };
    }

    /// <summary>The one ring this window shows (each ring on screen has a window of its own).</summary>
    public RingVisual Current { get; } = new();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Screens.MakeOverlay(_hwnd);
    }

    /// <summary>Centres the window on a pixel position. <paramref name="extent"/> is half the side in ring units.</summary>
    public void Place(int centerX, int centerY, double extent, double dpiScale, double userScale)
    {
        _userScale.ScaleX = _userScale.ScaleY = userScale;
        var size = (int)Math.Ceiling(extent * 2 * dpiScale * userScale);
        Screens.Place(_hwnd, centerX - size / 2, centerY - size / 2, size);
    }

    public void Park() => Screens.Park(_hwnd);

    public void Redraw() => Current.InvalidateVisual();
}
