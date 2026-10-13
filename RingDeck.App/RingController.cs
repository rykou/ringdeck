using System.Windows.Media;
using System.Windows.Threading;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

public sealed class RingController : IDisposable
{
    const int VkLeft = 0x01, VkRight = 0x02, VkBack = 0x08, VkEsc = 0x1B;

    readonly Func<AppConfig> _config;
    readonly INotifier _notifier;
    readonly InputHook _hook = new();
    readonly InputSender _input = new();
    readonly HotkeyMatcher _matcher = new();
    readonly RingSession _session;
    readonly ActionRunner _runner;
    readonly OverlayWindow _overlay = new();
    readonly HashSet<int> _swallowUp = new();
    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    CancellationTokenSource? _running;
    double _unit = 1, _dpiScale = 1;
    TimeSpan _lastFrame;
    bool _ticking, _disposed;

    public RingController(Func<AppConfig> config, INotifier notifier)
    {
        _config = config;
        _notifier = notifier;
        _session = new RingSession(id => _config().Find(id));
        _runner = new ActionRunner(_input, new ClipboardService(), new Launcher(), notifier);
        _overlay.Current.FindRing = _overlay.Outgoing.FindRing = id => _config().Find(id);
        _overlay.Show();
        _overlay.Park();
        _hook.Key = OnKey;
        _hook.Install();
        Reload();
    }

    public Func<int, bool, bool>? Capture { get; set; }
    public bool Paused { get => _matcher.Paused; set => _matcher.Paused = value; }

    public void Reload()
    {
        var bindings = new List<(Shortcut, Guid)>();
        foreach (var ring in _config().Rings)
            if (ring.Enabled && Shortcut.TryParse(ring.Shortcut, out var shortcut)) bindings.Add((shortcut, ring.Id));
        _matcher.SetBindings(bindings);
    }

    // Runs inside the low-level hook: decide fast, never block, never run an action here.
    bool OnKey(int vk, bool down)
    {
        if (Capture is { } capture) return capture(vk, down);
        if (!down && _swallowUp.Remove(vk)) return true;
        if (down && vk == VkEsc && _running is { } running)
        {
            running.Cancel();
            _swallowUp.Add(vk);
            return true;
        }
        if (down && _session.IsOpen && vk is VkEsc or VkBack or VkRight or VkLeft)
        {
            _swallowUp.Add(vk);
            switch (vk)
            {
                case VkEsc: CloseRing(); break;
                case VkLeft: ChooseAtPointer(false); break;
                default: GoBack(); break;
            }
            return true;
        }
        var mods = InputSender.CurrentMods();
        var result = _matcher.OnKey(vk, down, mods);
        if (result.Pressed is { } pressed)
        {
            if ((mods & (Mods.Alt | Mods.Win)) != 0) _dispatcher.BeginInvoke(_input.SendMask);
            if (_session.IsOpen) CloseRing(); else OpenRing(pressed);
        }
        if (result.Released is { } released && _session.IsOpen && _session.RootId == released) ChooseAtPointer(true);
        return result.Swallow;
    }

    /// <summary>A click (or, for release, the shortcut key coming up) at the pointer. Choosing can move the session
    /// to a child ring, so the centre is captured first for the transition.</summary>
    void ChooseAtPointer(bool release)
    {
        var (x, y) = Pointer();
        var (oldX, oldY) = (_session.CenterX, _session.CenterY);
        Handle(release ? _session.Release(x, y) : _session.Choose(x, y), oldX, oldY);
    }

    (double X, double Y) Pointer()
    {
        var (px, py) = Screens.Cursor();
        return (px / _unit, py / _unit);
    }

    void OpenRing(Guid id)
    {
        if (_config().Find(id) is not { Enabled: true } ring) return;
        var (px, py) = Screens.Cursor();
        var screen = Screens.At(px, py);
        _dpiScale = screen.DpiScale;
        _unit = _dpiScale * Math.Clamp(_config().Settings.Scale, 0.5, 2.0);
        var radius = RingGeometry.EffectiveRadius(ring.Radius, ring.Slices.Count, RingSession.SliceSize);
        var (x, y) = RingGeometry.ClampCenter(px / _unit, py / _unit, radius + RingSession.SliceSize,
            screen.Left / _unit, screen.Top / _unit, screen.Right / _unit, screen.Bottom / _unit);
        _session.Open(ring, x, y);
        _overlay.Outgoing.Ring = null;
        ShowCurrent();
    }

    void ShowCurrent()
    {
        var visual = _overlay.Current;
        visual.Ring = _session.Current;
        visual.Radius = _session.Radius;
        visual.Selected = -1;
        visual.Offset = default;
        visual.Animator = new RingAnimator();
        visual.Animator.Open(_session.Current!.Slices.Count);
        _overlay.Place((int)(_session.CenterX * _unit), (int)(_session.CenterY * _unit),
            RingVisual.ExtentFor(_session.Radius), _dpiScale, _unit / _dpiScale);
        StartTicking();
    }

    /// <summary>The session moved to another ring (child or parent): fade the old one out where it was.</summary>
    void Transition(double oldX, double oldY)
    {
        var current = _overlay.Current;
        var outgoing = _overlay.Outgoing;
        outgoing.Ring = current.Ring;
        outgoing.Radius = current.Radius;
        outgoing.Selected = current.Selected;
        outgoing.Animator = current.Animator;
        outgoing.Offset = new System.Windows.Vector(oldX - _session.CenterX, oldY - _session.CenterY);
        outgoing.Animator.Close();
        ShowCurrent();
    }

    void GoBack()
    {
        var (x, y) = (_session.CenterX, _session.CenterY);
        if (_session.Back()) Transition(x, y);
        else _overlay.Current.Animator.Close();
    }

    void CloseRing()
    {
        _session.Close();
        _overlay.Current.Animator.Close();
    }

    void Handle(ChooseResult result, double oldX, double oldY)
    {
        switch (result.Kind)
        {
            case ChooseKind.None:
                return;
            case ChooseKind.EnteredNested:
                Transition(oldX, oldY);
                return;
        }
        _overlay.Current.Animator.Close();
        if (result.Kind == ChooseKind.MissingRing)
            _notifier.Notify($"\"{result.Slice?.Label}\" has nothing to open", "The ring it points to was deleted or has no slices.");
        if (result.Kind == ChooseKind.Run && result.Slice is { } slice)
            _dispatcher.BeginInvoke(() => RunSlice(slice));
    }

    async void RunSlice(Slice slice)
    {
        _running?.Cancel();
        var cts = _running = new CancellationTokenSource();
        try
        {
            await Task.Delay(30);                 // let the swallowed shortcut key-up settle before sending input
            await _runner.RunAsync(slice, cts.Token);
        }
        catch (Exception ex) { App.Log(ex); }
        finally
        {
            if (ReferenceEquals(_running, cts)) _running = null;
            cts.Dispose();
        }
    }

    void StartTicking()
    {
        if (_ticking) return;
        _ticking = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : (now - _lastFrame).TotalSeconds;
        if (dt <= 0) return;                      // WPF can raise Rendering twice for one frame
        _lastFrame = now;
        var settings = _config().Settings;
        var current = _overlay.Current;
        double? target = null;
        if (_session.IsOpen)
        {
            var (x, y) = Pointer();
            var (oldX, oldY) = (_session.CenterX, _session.CenterY);
            if (_session.PointerMoved(x, y)) { Transition(oldX, oldY); current = _overlay.Current; }
            current.Selected = _session.Selected;
            double dx = x - _session.CenterX, dy = y - _session.CenterY;
            if (dx * dx + dy * dy >= RingSession.DeadZone * RingSession.DeadZone) target = RingGeometry.AngleOf(dx, dy);
        }
        current.Animator.Tick(dt, target, current.Selected, settings.Animations, settings.InstantPointer, settings.SpiralClose);
        _overlay.Outgoing.Animator.Tick(dt, null, -1, settings.Animations, settings.InstantPointer, settings.SpiralClose);
        _overlay.Redraw();
        if (!_session.IsOpen && current.Animator.Phase == AnimPhase.Hidden && _overlay.Outgoing.Animator.Phase == AnimPhase.Hidden)
        {
            CompositionTarget.Rendering -= OnFrame;
            _ticking = false;
            _overlay.Park();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hook.Dispose();
        _running?.Cancel();
        if (_ticking) CompositionTarget.Rendering -= OnFrame;
        _overlay.Close();
    }
}
