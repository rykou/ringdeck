using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

/// <summary>A click-through window that carries a picture of the dragged slice under the mouse pointer.</summary>
public sealed class DragGhost : Window
{
    const double Side = 170;                 // in device-independent pixels; the slice sits a little above centre
    readonly Picture _picture;
    IntPtr _hwnd;

    sealed class Picture(Slice slice) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc) =>
            RingVisual.DrawLoose(dc, slice, new Point(ActualWidth / 2, ActualHeight / 2 - 12), Theme.Accent, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    public DragGhost(Slice slice)
    {
        ThemeMode = ThemeMode.None;           // keep the background truly transparent
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Opacity = 0.92;
        Left = Top = -32000;
        Width = Height = Side;
        Content = _picture = new Picture(slice);
        SourceInitialized += (_, _) => { _hwnd = new WindowInteropHelper(this).Handle; Screens.MakeOverlay(_hwnd); };
    }

    /// <summary>Centres the picture on the mouse pointer (physical pixels, so it stays right on any monitor).</summary>
    public void FollowPointer()
    {
        if (_hwnd == IntPtr.Zero) return;
        var (x, y) = Screens.Cursor();
        var size = (int)Math.Ceiling(Side * Screens.At(x, y).DpiScale);
        Screens.Place(_hwnd, x - size / 2, y - size / 2 + (int)(12 * Screens.At(x, y).DpiScale), size);
    }
}
