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
    readonly InputSender _input;
    readonly HotkeyMatcher _matcher = new();
    readonly RingSession _session;
    readonly ActionRunner _runner;
    readonly ClipboardService _clipboard = new();
    readonly AiClient _ai = new();
    readonly OverlayWindow _overlay = new();
    readonly HashSet<int> _swallowUp = new();
    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    readonly Dispatcher _actions;          // a second STA thread: clipboard, SendInput and launching can block,
                                           // and Windows drops a low-level hook whose thread stops answering
    CancellationTokenSource? _running;
    ScreenInfo _screen;
    double _unit = 1, _dpiScale = 1;
    TimeSpan _lastFrame;
    bool _ticking, _disposed, _userPaused, _suppressed;
    readonly DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };

    public RingController(Func<AppConfig> config, INotifier notifier)
    {
        _config = config;
        _notifier = notifier;
        _session = new RingSession(id => _config().Find(id));
        _input = new InputSender(_hook.IsPhysicallyDown);
        _runner = new ActionRunner(_input, _clipboard, new Launcher(), notifier, focuser: new WindowFocuser(_input));
        _actions = StartActionThread();
        _hook.Error = App.Log;
        _overlay.Current.FindRing = _overlay.Outgoing.FindRing = id => _config().Find(id);
        _overlay.Show();
        _overlay.Park();
        _hook.Key = OnKey;
        _hook.Install();
        _foregroundTimer.Tick += (_, _) => CheckForeground();
        _foregroundTimer.Start();
        Reload();
    }

    static Dispatcher StartActionThread()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "RingDeck actions" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    /// <summary>Queues work to run on the UI thread right after the hook callback returns.</summary>
    void Post(Action action) => _dispatcher.BeginInvoke(DispatcherPriority.Send, action);

    void CancelRunning()
    {
        try { _running?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public Func<int, bool, bool>? Capture { get; set; }
    /// <summary>The user's own pause switch (tray menu).</summary>
    public bool Paused { get => _userPaused; set { _userPaused = value; _matcher.Paused = _userPaused || _suppressed; } }

    /// <summary>Runs a few times a second: rings stay off while a fullscreen app or a listed program has focus,
    /// so the shortcut keys reach the game untouched.</summary>
    void CheckForeground()
    {
        if (_session.IsOpen) return;                    // never flip while a ring is on screen
        try { _suppressed = Suppression.Applies(_config().Settings, ForegroundProbe.Get()); }
        catch (Exception ex) { _suppressed = false; App.Log(ex); }
        _matcher.Paused = _userPaused || _suppressed;
    }

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
        if (down && vk == VkEsc && _running is not null)
        {
            CancelRunning();
            _swallowUp.Add(vk);
            return true;
        }
        if (down && _session.IsOpen && vk is VkEsc or VkBack or VkRight or VkLeft)
        {
            _swallowUp.Add(vk);
            Post(() =>
            {
                if (!_session.IsOpen) return;
                switch (vk)
                {
                    case VkEsc: CloseRing(); break;
                    case VkLeft: ChooseAtPointer(false); break;
                    default: GoBack(); break;
                }
            });
            return true;
        }
        var mods = InputSender.CurrentMods();
        var result = _matcher.OnKey(vk, down, mods, ChooseRing);
        // Only the decision is made here; the work is posted, in order, so the callback returns at once.
        if (result.Pressed is { } pressed)
        {
            var mask = (mods & (Mods.Alt | Mods.Win)) != 0;
            Post(() =>
            {
                if (mask) _input.SendMask();
                if (_session.IsOpen) CloseRing(); else OpenRing(pressed);
            });
        }
        if (result.Released is { } released)
            Post(() => { if (_session.IsOpen && _session.RootId == released) ChooseAtPointer(true); });
        return result.Swallow;
    }

    /// <summary>Rings sharing a shortcut: the one made for the program in front, else the "everywhere" one, else none
    /// (and the key goes to the program). Runs inside the hook, so the program is only looked up when it matters.</summary>
    Guid? ChooseRing(IReadOnlyList<Guid> ids)
    {
        var rings = ids.Select(id => _config().Find(id)).OfType<Ring>().ToList();
        if (rings.All(r => r.OnlyIn.Count == 0)) return rings.FirstOrDefault()?.Id;
        string? exe = null;
        try { exe = ForegroundProbe.Get().Exe; }
        catch (Exception ex) { App.Log(ex); }
        return RingPicker.Pick(rings, exe)?.Id;
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
        _screen = Screens.At(px, py);
        if (ring.OpenAt == OpenAt.Center)
        {
            // open in the middle of the screen the pointer is on, and put the pointer at the ring's centre so a flick works
            (px, py) = RingGeometry.Centre(_screen.Left, _screen.Top, _screen.Right, _screen.Bottom);
            Screens.MoveCursor(px, py);
        }
        _dpiScale = _screen.DpiScale;
        _unit = _dpiScale * Math.Clamp(_config().Settings.Scale, 0.5, 2.0);
        _session.Open(ring, px / _unit, py / _unit);
        ClampSession();
        _overlay.Outgoing.Ring = null;
        ShowCurrent();
        Sound("open");
    }

    void Sound(string name)
    {
        var settings = _config().Settings;
        if (settings.Sounds) SoundEffects.Play(name, settings.SoundVolume);
    }

    /// <summary>Keeps the current ring, labels included, on the monitor it opened on. Child rings open at the
    /// pointer, so they need this as much as the first ring does.</summary>
    void ClampSession()
    {
        var (x, y) = RingGeometry.ClampCenter(_session.CenterX, _session.CenterY, _session.Radius + RingSession.SliceSize + 12,
            _screen.Left / _unit, _screen.Top / _unit, _screen.Right / _unit, _screen.Bottom / _unit);
        _session.Recenter(x, y);
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
        ClampSession();
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
                Sound("open");
                return;
        }
        if (result.Kind == ChooseKind.Run) Sound("select");
        _overlay.Current.Animator.Close();
        if (result.Kind == ChooseKind.MissingRing)
            _notifier.Notify($"\"{result.Slice?.Label}\" has nothing to open", "The ring it points to was deleted or has no slices.");
        if (result.Kind == ChooseKind.Run && result.Slice is { Action: AiChatAction ai } aiSlice)
        {
            OpenAi(aiSlice, ai);
            return;
        }
        if (result.Kind == ChooseKind.Run && result.Slice is { } slice)
            RunSlice(slice);
    }

    /// <summary>AI slice: note the window in front and grab its selection (actions thread), then open the panel.</summary>
    void OpenAi(Slice slice, AiChatAction ai)
    {
        if (_config().FindConnection(ai.ConnectionId) is not { } connection)
        {
            _notifier.Notify($"\"{slice.Label}\" has no AI connection", "Pick or create one for this slice in the editor.");
            return;
        }
        _actions.BeginInvoke(new Func<Task>(async () =>
        {
            string? input = null;
            IntPtr target = IntPtr.Zero;
            try
            {
                await Task.Delay(60);                     // let the swallowed shortcut key-up settle
                target = Focus.Foreground();
                input = ai.Input switch
                {
                    AiInput.Selection when _input.CanSendToForeground() => await SelectionGrabber.GrabAsync(_input, _clipboard, CancellationToken.None),
                    AiInput.Clipboard => _clipboard.GetText(),
                    _ => null,
                };
            }
            catch (Exception ex) { App.Log(ex); }
            if (ai.ShowChat) _ = _dispatcher.BeginInvoke(() => AiPanel.Open(connection, ai, input, target, InsertReply));
            else await AnswerToClipboard(slice, connection, ai, input);
        }));
    }

    /// <summary>No chat window: ask once, put the answer on the clipboard, say so. Runs on the actions thread.</summary>
    async Task AnswerToClipboard(Slice slice, AiConnection connection, AiChatAction ai, string? input)
    {
        string reply;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            reply = await AiChat.AskOnceAsync(_ai, connection, ai, input, App.Instance.Secrets.Reveal, cts.Token);
        }
        catch (Exception ex) when (ex is AiException or OperationCanceledException)
        {
            _notifier.Notify($"\"{slice.Label}\" failed", ex is AiException ? ex.Message : "No answer within 2 minutes.");
            return;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _notifier.Notify($"\"{slice.Label}\" failed", ex.Message);
            return;
        }
        for (var waited = 0; !_clipboard.TrySetText(reply); waited += 50)
        {
            if (waited >= 1000) { _notifier.Notify($"\"{slice.Label}\" answered", "But the clipboard is in use by another program, so it could not be copied."); return; }
            await Task.Delay(50);
        }
        var preview = reply.ReplaceLineEndings(" ");
        _notifier.Notify($"\"{slice.Label}\": copied to the clipboard", preview.Length > 120 ? preview[..120] + "…" : preview);
    }

    /// <summary>Pastes an AI reply into the window the slice was used in (the panel has already handed focus back).</summary>
    void InsertReply(IntPtr target, string text) =>
        _actions.BeginInvoke(new Func<Task>(async () =>
        {
            await Task.Delay(150);
            if (Focus.Foreground() != target) Focus.Restore(target);
            await _runner.PasteTextAsync(text, CancellationToken.None);
        }));

    void RunSlice(Slice slice)
    {
        CancelRunning();
        var cts = new CancellationTokenSource();
        _running = cts;
        _actions.BeginInvoke(new Func<Task>(async () =>
        {
            try
            {
                await Task.Delay(30);             // let the swallowed shortcut key-up settle before sending input
                await _runner.RunAsync(slice, cts.Token);
            }
            catch (Exception ex) { App.Log(ex); }
            finally
            {
                Interlocked.CompareExchange(ref _running, null, cts);
                cts.Dispose();
            }
        }));
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
        _foregroundTimer.Stop();
        CancelRunning();
        _actions.BeginInvokeShutdown(DispatcherPriority.Normal);
        if (_ticking) CompositionTarget.Rendering -= OnFrame;
        _overlay.Close();
    }
}
