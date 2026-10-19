namespace RingDeck.Core;

/// <summary>Builds macro steps from what the user does while recording, on one timeline: pointer movement as mouse
/// move steps, each key and mouse button as separate down and up steps, and the real time between them as wait
/// steps. Movement is split into a new mouse move step at every key or click, and whenever the pointer rests for
/// <see cref="StillMs"/>. A click where the pointer was not already recorded to be adds a jump there first, so the
/// macro clicks the same spot.</summary>
public sealed class MacroRecorder
{
    /// <summary>A pointer resting this long ends the current mouse move step; the rest becomes a wait.</summary>
    public const int StillMs = 250;

    readonly Func<long> _clock;
    readonly HashSet<int> _keysDown = new();
    readonly HashSet<ClickButton> _buttonsDown = new();
    long? _last;                       // when the last step ended
    (int X, int Y)? _pointer;          // where the recording last saw the pointer
    long _pointerAt;
    List<PathPoint>? _path;            // the mouse move being recorded
    long _pathStart, _lastMoveAt;
    ScrollStep? _scroll;               // the last step, while it is a scroll more wheel notches can join
    int _scrollDelta;                  // its wheel movement so far (120 per notch; precise wheels send less)
    long _scrollAt;

    /// <summary>Wheel notches this close together, the same way, are one scroll step.</summary>
    public const int ScrollJoinMs = 300;
    const int Notch = 120;

    public MacroRecorder(Func<long> clockMs) => _clock = clockMs;

    public List<MacroStep> Steps { get; } = new();

    /// <summary>Steps so far, counting a mouse move still being recorded.</summary>
    public int Count => Steps.Count + (_path is null ? 0 : 1);

    /// <summary>A wait from the end of the last step until <paramref name="at"/> (none before the first step).</summary>
    void WaitUntil(long at)
    {
        _scroll = null;                // any other step ends the scroll that could still grow
        if (_last is { } last && at - last > 0) Steps.Add(new DelayStep { Ms = (int)Math.Min(at - last, int.MaxValue) });
        _last = at;
    }

    void EndPath()
    {
        if (_path is null) return;
        Steps.Add(new MouseMoveStep { Path = MousePath.Format(_path) });
        _last = _lastMoveAt;
        _path = null;
    }

    /// <summary>Where the pointer is now. Call regularly (every few milliseconds) to record movement.</summary>
    public void OnPointer(int x, int y)
    {
        var now = _clock();
        if (_pointer is not { } was) { _pointer = (x, y); _pointerAt = now; return; }
        if (was == (x, y))
        {
            if (_path is not null && now - _lastMoveAt >= StillMs) EndPath();
            _pointerAt = now;
            return;
        }
        if (_path is null)
        {
            // The move starts where the pointer rested, at the last moment it was seen there.
            _pathStart = _pointerAt;
            WaitUntil(_pathStart);
            _path = new List<PathPoint> { new(was.X, was.Y, 0) };
        }
        _path.Add(new PathPoint(x, y, (int)Math.Max(now - _pathStart, _path[^1].Ms)));
        _lastMoveAt = now;
        _pointer = (x, y);
        _pointerAt = now;
    }

    public void OnKey(int vk, bool down)
    {
        if (vk < 0x08) return;                                  // mouse buttons come through OnMouse
        if (down)
        {
            if (!_keysDown.Add(vk)) return;                     // a held key repeats its "down": keep the first
            EndPath();
            WaitUntil(_clock());
            Steps.Add(new KeyDownStep { Key = KeyNames.NameOf(vk) });
        }
        else
        {
            if (!_keysDown.Remove(vk)) return;                  // pressed before recording started
            EndPath();
            WaitUntil(_clock());
            Steps.Add(new KeyUpStep { Key = KeyNames.NameOf(vk) });
        }
    }

    public void OnMouse(ClickButton button, bool down, int x, int y)
    {
        if (down)
        {
            EndPath();
            var now = _clock();
            // Buttons don't auto-repeat: a second press means the release of the first never arrived. Record that
            // release now, or this button would count as held and every later click with it would be lost.
            if (!_buttonsDown.Add(button)) Steps.Add(new MouseUpStep { Button = button });
            WaitUntil(now);
            if (_pointer != (x, y)) Steps.Add(new MouseMoveStep { Path = MousePath.Format(new[] { new PathPoint(x, y, 0) }) });
            (_pointer, _pointerAt) = ((x, y), now);
            Steps.Add(new MouseDownStep { Button = button });
        }
        else
        {
            if (!_buttonsDown.Remove(button)) return;
            EndPath();
            WaitUntil(_clock());
            Steps.Add(new MouseUpStep { Button = button });
        }
    }

    /// <summary>The mouse wheel turned: <paramref name="delta"/> in Windows wheel units (120 per notch; positive is
    /// up, or right when <paramref name="horizontal"/>), with the pointer at x, y.</summary>
    public void OnWheel(int delta, bool horizontal, int x, int y)
    {
        if (delta == 0) return;
        EndPath();
        var now = _clock();
        var direction = horizontal ? (delta > 0 ? ScrollDirection.Right : ScrollDirection.Left)
                                   : (delta > 0 ? ScrollDirection.Up : ScrollDirection.Down);
        if (_scroll is { } open && open.Direction == direction && now - _scrollAt <= ScrollJoinMs && _pointer == (x, y))
        {
            _scrollDelta += Math.Abs(delta);
            open.Notches = Math.Max(1, (int)Math.Round(_scrollDelta / (double)Notch));
            _scrollAt = now;
            _last = now;
            return;
        }
        WaitUntil(now);
        if (_pointer != (x, y)) Steps.Add(new MouseMoveStep { Path = MousePath.Format(new[] { new PathPoint(x, y, 0) }) });
        (_pointer, _pointerAt) = ((x, y), now);
        _scrollDelta = Math.Abs(delta);
        _scroll = new ScrollStep { Direction = direction, Notches = Math.Max(1, (int)Math.Round(_scrollDelta / (double)Notch)) };
        _scrollAt = now;
        Steps.Add(_scroll);
    }

    /// <summary>The recorded steps, with anything still held let go at the end so the macro never leaves a key down.</summary>
    public List<MacroStep> Finish()
    {
        EndPath();
        foreach (var vk in _keysDown) Steps.Add(new KeyUpStep { Key = KeyNames.NameOf(vk) });
        foreach (var b in _buttonsDown) Steps.Add(new MouseUpStep { Button = b });
        _keysDown.Clear();
        _buttonsDown.Clear();
        return Steps;
    }
}
