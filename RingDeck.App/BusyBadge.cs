using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using RingDeck.Windows;

namespace RingDeck.App;

/// <summary>A small card in the bottom-right corner while a slice runs: a spinner, the slice's name, what it's doing
/// now, and a button that stops it. It never takes focus, so the window in front keeps getting the input.</summary>
public sealed class BusyBadge : Window
{
    readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _status = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly RotateTransform _spin = new();

    public BusyBadge(Action stop)
    {
        ThemeMode = ThemeMode.None;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "RingDeck is running";

        var fg = Theme.Frozen(Theme.Foreground);
        _title.Foreground = fg;
        _status.Foreground = Theme.Frozen(Theme.Subtle);

        // A quarter-open circle that turns: "working on it".
        var spinner = new Ellipse
        {
            Width = 18, Height = 18, StrokeThickness = 2.5, Stroke = Theme.Frozen(Theme.Accent),
            StrokeDashArray = new DoubleCollection { 5, 2.2 }, StrokeDashCap = PenLineCap.Round,
            RenderTransform = _spin, RenderTransformOrigin = new Point(0.5, 0.5),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        };
        var stopButton = new Button
        {
            Content = "✕", Width = 26, Height = 26, Padding = new Thickness(0), Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Focusable = false, ToolTip = "Stop it (Esc also stops it)",
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = fg, Cursor = System.Windows.Input.Cursors.Hand,
        };
        stopButton.Click += (_, _) => stop();

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260 };
        text.Children.Add(_title);
        text.Children.Add(_status);
        var row = new DockPanel();
        DockPanel.SetDock(spinner, Dock.Left);
        DockPanel.SetDock(stopButton, Dock.Right);
        row.Children.Add(spinner);
        row.Children.Add(stopButton);
        row.Children.Add(text);
        Content = new Border
        {
            Margin = new Thickness(8), Padding = new Thickness(12, 8, 6, 8), CornerRadius = new CornerRadius(10), MinWidth = 200,
            Background = Theme.Frozen(Theme.IsDark ? Color.FromRgb(0x24, 0x24, 0x2C) : Color.FromRgb(0xFA, 0xFA, 0xFC), 0.96),
            BorderBrush = Theme.Frozen(Theme.Accent, 0.5), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 },
            Child = row,
        };
        SizeChanged += (_, _) => PlaceInCorner();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Screens.MakeNoFocus(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Shows the badge for a slice (or updates it), without taking focus.</summary>
    public void ShowFor(string label, string status)
    {
        _title.Text = label.Trim().Length > 0 ? label.Trim() : "Running";
        SetStatus(status);
        if (IsVisible) return;
        Show();
        PlaceInCorner();
        _spin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    public void SetStatus(string status) => _status.Text = status;

    public void HideBadge()
    {
        _spin.BeginAnimation(RotateTransform.AngleProperty, null);
        Hide();
    }

    /// <summary>The bottom-right corner of the main screen, above the taskbar.</summary>
    void PlaceInCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 8;
        Top = area.Bottom - ActualHeight - 8;
    }
}
