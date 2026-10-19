using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

/// <summary>Small panel for recording macro steps: F9 starts, F9 stops, Esc cancels. Records pointer movement,
/// clicks and keys (each can be switched off) on one timeline, with the time between them. Keys and clicks still
/// reach the app being recorded; clicks on this panel or on the editor are left out. Closes with the steps in Result.</summary>
public sealed class RecorderWindow : Window
{
    const int F9 = 0x78, Esc = 0x1B;

    // Remembered while RingDeck runs, so the next recording starts with the same choices.
    static bool s_moves = true, s_clicks = true, s_scroll = true, s_keys = true;

    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly MacroRecorder _recorder;
    readonly DispatcherTimer _timer = new(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(8) };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    readonly CheckBox _moves, _clicks, _scroll, _keys;
    readonly Func<int, bool, bool>? _previousCapture;
    bool _recording;
    long _startedAt;
    Window? _badge;                    // the red circle shown instead of this panel while recording

    /// <summary>The recorded steps, when the user stopped with F9.</summary>
    public List<MacroStep>? Result { get; private set; }

    /// <param name="movementOnly">Start with only "mouse movement" ticked (re-recording one mouse move step).</param>
    public RecorderWindow(bool movementOnly = false)
    {
        _recorder = new MacroRecorder(() => _clock.ElapsedMilliseconds);
        Title = "Record macro steps";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Nothing here takes keyboard focus, so Space or Enter typed during a recording can't press a button.
        CheckBox Option(string text, bool on) => new() { Content = text, IsChecked = on, Focusable = false, Margin = new Thickness(0, 0, 14, 0) };
        _moves = Option("Mouse movement", movementOnly || s_moves);
        _clicks = Option("Clicks", !movementOnly && s_clicks);
        _scroll = Option("Scrolling", !movementOnly && s_scroll);
        _keys = Option("Keys", !movementOnly && s_keys);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "Press F9 to start recording, then do what the macro should do, in any window. Press F9 again to stop. Esc cancels.",
        });
        var options = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        options.Children.Add(_moves);
        options.Children.Add(_clicks);
        options.Children.Add(_scroll);
        options.Children.Add(_keys);
        panel.Children.Add(options);
        panel.Children.Add(_status);
        var cancel = new Button { Content = "Cancel", IsCancel = true, Focusable = false, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(cancel);
        Content = panel;
        ShowStatus();

        _timer.Tick += (_, _) =>
        {
            if (_moves.IsChecked == true) { var (x, y) = Screens.Cursor(); _recorder.OnPointer(x, y); }
            ShowStatus();
        };
        _previousCapture = App.Instance.Controller.Capture;
        App.Instance.Controller.Capture = OnInput;
        App.Instance.Controller.WheelCapture = OnWheel;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _badge?.Close();
            App.Instance.Controller.Capture = _previousCapture;
            App.Instance.Controller.WheelCapture = null;
        };
        Loaded += (_, _) =>
        {
            // Top middle of the work area, out of the way of most paths.
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + 16;
        };
    }

    /// <summary>Runs inside the input hook: F9 and Esc are this panel's; everything else passes through to the
    /// app in front and, while recording, is noted.</summary>
    bool OnInput(int vk, bool down)
    {
        if (vk == F9) { if (down) Dispatcher.BeginInvoke(Toggle); return true; }
        if (vk == Esc) { if (down) Dispatcher.BeginInvoke(() => { if (IsVisible) DialogResult = false; }); return true; }
        if (!_recording) return false;
        if (vk is 0x01 or 0x02 or 0x04)
        {
            var (x, y) = Screens.Cursor();
            // Only a press is left out (when it lands on this panel, the red circle or the editor). A release always
            // goes to the recorder, which keeps it only when it kept the press, so no press is left half recorded.
            if (_clicks.IsChecked == true && !(down && IsOurs(Screens.TopWindowAt(x, y))))
                _recorder.OnMouse(vk switch { 0x01 => ClickButton.Left, 0x02 => ClickButton.Right, _ => ClickButton.Middle }, down, x, y);
            return false;
        }
        if (vk >= 0x08 && _keys.IsChecked == true) _recorder.OnKey(vk, down);
        return false;
    }

    /// <summary>Runs inside the input hook: notes a wheel turn (not over our own windows) and lets it through.</summary>
    bool OnWheel(int delta, bool horizontal)
    {
        if (!_recording || _scroll.IsChecked != true) return false;
        var (x, y) = Screens.Cursor();
        if (!IsOurs(Screens.TopWindowAt(x, y))) _recorder.OnWheel(delta, horizontal, x, y);
        return false;
    }

    /// <summary>True for this panel, the red circle and the editor: windows whose clicks are not part of the macro.
    /// Judged by the window actually on top under the pointer, not by where the windows are.</summary>
    bool IsOurs(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        bool Is(Window? w) => w is not null && new System.Windows.Interop.WindowInteropHelper(w).Handle == hwnd;
        return Is(this) || Is(_badge) || Is(Owner) || Is(Owner?.Owner);
    }

    void Toggle()
    {
        if (!IsVisible) return;
        if (!_recording)
        {
            (s_moves, s_clicks, s_scroll, s_keys) = (_moves.IsChecked == true, _clicks.IsChecked == true, _scroll.IsChecked == true, _keys.IsChecked == true);
            _moves.IsEnabled = _clicks.IsEnabled = _scroll.IsEnabled = _keys.IsEnabled = false;
            _recording = true;
            _startedAt = _clock.ElapsedMilliseconds;
            if (_moves.IsChecked == true) { var (x, y) = Screens.Cursor(); _recorder.OnPointer(x, y); }
            _timer.Start();
            ShowStatus();
            ShowBadge();
            return;
        }
        _timer.Stop();
        _recording = false;
        if (_moves.IsChecked == true) { var (x, y) = Screens.Cursor(); _recorder.OnPointer(x, y); }
        Result = _recorder.Finish();
        DialogResult = true;
    }

    /// <summary>While recording, this panel moves off screen and a small red circle in the bottom left corner
    /// shows that recording is on. Clicking the circle stops, like F9.</summary>
    void ShowBadge()
    {
        const double Size = 44;
        var dot = new System.Windows.Shapes.Ellipse { Width = Size, Height = Size, Fill = System.Windows.Media.Brushes.Red,
            Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 2 };
        var stop = new System.Windows.Shapes.Rectangle { Width = 14, Height = 14, RadiusX = 2, RadiusY = 2, Fill = System.Windows.Media.Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var face = new Grid { Cursor = System.Windows.Input.Cursors.Hand };
        face.Children.Add(dot);
        face.Children.Add(stop);
        var area = SystemParameters.WorkArea;
        _badge = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
            Width = Size, Height = Size, Left = area.Left + 16, Top = area.Bottom - Size - 16, Content = face,
            ToolTip = "Recording. Click here or press F9 to stop, Esc to cancel.",
        };
        face.MouseLeftButtonUp += (_, _) => Toggle();
        _badge.Show();
        Left = Top = -32000;                       // out of the way; stopping closes the panel, so it never needs to come back
    }

    void ShowStatus()
    {
        _status.Foreground = _recording ? System.Windows.Media.Brushes.OrangeRed : (System.Windows.Media.Brush)Application.Current.Resources["SubtleBrush"];
        _status.Text = !_recording ? "Waiting for F9…"
            : $"● Recording: {_recorder.Count} steps, {(_clock.ElapsedMilliseconds - _startedAt) / 1000.0:0.0} s. Press F9 to stop.";
    }
}
