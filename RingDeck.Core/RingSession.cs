namespace RingDeck.Core;

/// <summary>NotReady: the slice needs the pointer to rest on it longer; the ring stays open.</summary>
/// <summary>WentBack: the pointer was in a nested ring's gap towards its parent; the parent is current again.</summary>
public enum ChooseKind { None, Closed, Run, EnteredNested, MissingRing, NotReady, WentBack }

/// <summary>What a pointer move changed: nothing, a nested ring opened, or the pointer went back to an earlier ring.</summary>
public enum PointerChange { None, Entered, WentBack }

/// <summary>A ring the user came through on the way to the current one: where it is and which slice led on.</summary>
public sealed record TrailRing(Ring Ring, double X, double Y, RingLayout Layout, int Selected, double Radius);

public readonly record struct ChooseResult(ChooseKind Kind, Slice? Slice);

/// <summary>State of the ring that is currently open: which ring, where, what is selected, and how deep.</summary>
public sealed class RingSession
{
    public const double DeadZone = 28, SliceSize = 56;
    public const int MaxDepth = 32, MaxConfirmMs = 5000;

    readonly Func<Guid, Ring?> _findRing;
    readonly Func<long> _clock;
    long _selectedSince;
    bool _hoverArmed = true;

    /// <summary>Resting on a slice that opens another ring opens it after <see cref="HoverOpenMs"/>.</summary>
    public bool HoverOpens { get; set; } = true;
    public int HoverOpenMs { get; set; } = 220;
    readonly Stack<TrailRing> _parents = new();
    bool _pushArmed;

    public RingSession(Func<Guid, Ring?> findRing, Func<long>? clockMs = null)
    {
        _findRing = findRing;
        _clock = clockMs ?? (() => Environment.TickCount64);
    }

    public Ring? Current { get; private set; }
    public Guid RootId { get; private set; }
    public double CenterX { get; private set; }
    public double CenterY { get; private set; }
    public int Selected { get; private set; } = -1;

    /// <summary>For the selected slice: how much of its hover time has passed, 0 to 1; 1 for slices that need none.</summary>
    public double HoldProgress =>
        Current is null || Selected < 0 || Selected >= Current.Slices.Count || Current.Slices[Selected].ConfirmMs <= 0 ? 1
        : Math.Clamp((_clock() - _selectedSince) / (double)Current.Slices[Selected].ConfirmMs, 0, 1);

    void Select(int index)
    {
        if (index != Selected) { _selectedSince = _clock(); _hoverArmed = true; }
        Selected = index;
    }
    /// <summary>True when the ring stays open until a click: click mode, or after a release entered a child ring.</summary>
    public bool Sticky { get; private set; }
    public bool IsOpen => Current is not null;
    public int Depth => _parents.Count;
    public RingLayout Layout { get; private set; } = RingLayout.Full(0);
    public double Radius => Current is null ? 0 : Layout.RadiusFor(Current.Radius, SliceSize);
    /// <summary>The rings the user came through, outermost first. They stay on screen, linked to the current ring.</summary>
    public IReadOnlyList<TrailRing> Trail => _parents.Reverse().ToList();

    /// <summary>The centre of the slice in the parent ring that led to the current ring (where the link line starts).</summary>
    public (double X, double Y)? Link
    {
        get
        {
            if (_parents.Count == 0) return null;
            var p = _parents.Peek();
            var (x, y) = RingGeometry.PointAt(p.Layout.Angle(p.Selected), p.Radius);
            return (p.X + x, p.Y + y);
        }
    }
    /// <summary>How far from the centre the pointer must go to push through a nested slice: one slice width past
    /// the slice circle, which is still reachable when the ring sits against a screen edge.</summary>
    public double PushDistance => Radius + SliceSize;

    public void Open(Ring ring, double x, double y)
    {
        _parents.Clear();
        Current = ring;
        RootId = ring.Id;
        CenterX = x;
        CenterY = y;
        Selected = -1;
        Layout = RingLayout.Full(ring.Slices.Count);
        Sticky = ring.Mode == OpenMode.Click;
        _pushArmed = true;
    }

    /// <summary>Moves a just-opened ring, for example after clamping it to the screen.</summary>
    public void Recenter(double x, double y)
    {
        CenterX = x;
        CenterY = y;
        // A nested ring keeps its gap facing the slice it came from, wherever it ended up.
        if (Link is { } link && Current is not null)
            Layout = RingLayout.Fan(Current.Slices.Count, RingGeometry.AngleOf(link.X - x, link.Y - y));
    }

    /// <summary>True when the pointer is in a nested ring's gap, towards the ring it came from.</summary>
    public bool PointsBack(double x, double y) => _parents.Count > 0 && Layout.PointsBack(x - CenterX, y - CenterY, DeadZone);

    /// <summary>Updates the selection. A nested ring opens when the pointer rests on its slice (or pushes past it);
    /// going back over an earlier ring returns to it.</summary>
    public PointerChange PointerMoved(double x, double y)
    {
        if (Current is null) return PointerChange.None;
        if (ReturnToAncestor(x, y)) return PointerChange.WentBack;
        double dx = x - CenterX, dy = y - CenterY;
        Select(Layout.SliceAt(dx, dy, DeadZone));
        var inside = dx * dx + dy * dy < PushDistance * PushDistance;
        if (inside) _pushArmed = true;
        if (Selected < 0 || Current.Slices[Selected].Action is not NestedRingAction) return PointerChange.None;
        if (HoldProgress < 1) return PointerChange.None;  // a slice with a wait opens nothing until its circle is full
        var rested = HoverOpens && _hoverArmed && _clock() - _selectedSince >= HoverOpenMs;
        // After Back the pointer is still on (or far out past) the nested slice; it has to move off it, or back in,
        // before the child opens again.
        var pushed = !Sticky && !inside && _pushArmed;
        if (!rested && !pushed) return PointerChange.None;
        return TryEnter(Current.Slices[Selected]) ? PointerChange.Entered : PointerChange.None;
    }

    /// <summary>The pointer is over an earlier ring of the trail, on another slice than the one that led on (or in its
    /// middle): go back to that ring.</summary>
    bool ReturnToAncestor(double x, double y)
    {
        var trail = _parents.ToArray();                    // nearest first
        for (var level = 0; level < trail.Length; level++)
        {
            var t = trail[level];
            double dx = x - t.X, dy = y - t.Y, reach = t.Radius + SliceSize * 0.6;
            if (dx * dx + dy * dy > reach * reach) continue;
            var over = t.Layout.SliceAt(dx, dy, DeadZone);
            if (over == t.Selected) return false;          // still on the slice that opened the ring after it
            for (var i = 0; i <= level; i++) Back();
            Select(over);
            return true;
        }
        return false;
    }

    /// <summary>A click (or a hold-mode release) at the pointer position.</summary>
    public ChooseResult Choose(double x, double y)
    {
        if (Current is null) return new ChooseResult(ChooseKind.None, null);
        Select(Layout.SliceAt(x - CenterX, y - CenterY, DeadZone));
        // Letting go of the shortcut over the gap (typically still on the slice whose ring just opened) chooses
        // nothing, like letting go in the middle; a click there goes back.
        if (Selected < 0 && PointsBack(x, y) && !_releasing)
        {
            Back();
            Sticky = true;                       // a release that went back leaves the parent waiting for a click
            return new ChooseResult(ChooseKind.WentBack, null);
        }
        if (Selected < 0) { Close(); return new ChooseResult(ChooseKind.Closed, null); }
        var slice = Current.Slices[Selected];
        switch (slice.Action)
        {
            case null:
                Close();
                return new ChooseResult(ChooseKind.Closed, null);
            case NestedRingAction when HoldProgress < 1:
                return TooSoon(slice);
            case NestedRingAction:
                if (TryEnter(slice)) { Sticky = true; return new ChooseResult(ChooseKind.EnteredNested, slice); }
                Close();
                return new ChooseResult(ChooseKind.MissingRing, slice);
            default:
                if (HoldProgress < 1) return TooSoon(slice);
                Close();
                return new ChooseResult(ChooseKind.Run, slice);
        }
    }

    /// <summary>The ring's shortcut key was released.</summary>
    public ChooseResult Release(double x, double y)
    {
        if (Current is null || Sticky) return new ChooseResult(ChooseKind.None, null);
        _releasing = true;
        try { return Choose(x, y); }
        finally { _releasing = false; }
    }

    bool _releasing;

    /// <summary>A slice whose circle hasn't filled yet. Letting go of the shortcut cancels: the ring closes and
    /// nothing runs. A click (click mode, or a ring that is waiting for one) leaves the ring open to try again.</summary>
    ChooseResult TooSoon(Slice slice)
    {
        if (!_releasing) return new ChooseResult(ChooseKind.NotReady, slice);
        Close();
        return new ChooseResult(ChooseKind.Closed, null);
    }

    /// <summary>Goes up one level. Returns false when that closed the root ring (or nothing was open).</summary>
    public bool Back()
    {
        if (Current is null) return false;
        if (_parents.Count == 0) { Close(); return false; }
        var parent = _parents.Pop();
        (Current, CenterX, CenterY, Layout) = (parent.Ring, parent.X, parent.Y, parent.Layout);
        Selected = parent.Selected;              // the pointer is usually still on the slice that led on
        _selectedSince = _clock();
        _hoverArmed = false;                     // so resting there doesn't reopen the ring that was just left
        _pushArmed = false;
        return true;
    }

    public void Close()
    {
        _parents.Clear();
        Current = null;
        Selected = -1;
        Sticky = false;
    }

    /// <summary>Opens the ring a nested slice points to, further out in that slice's direction, fanned away from
    /// this ring. This ring stays in <see cref="Trail"/>.</summary>
    bool TryEnter(Slice slice)
    {
        if (slice.Action is not NestedRingAction nested || _parents.Count >= MaxDepth || Selected < 0) return false;
        if (_findRing(nested.RingId) is not { Slices.Count: > 0 } child) return false;
        var angle = Layout.Angle(Selected);
        var parentRadius = Radius;
        var childLayout = RingLayout.Fan(child.Slices.Count, angle + 180);
        var distance = parentRadius + SliceSize * 1.4 + childLayout.RadiusFor(child.Radius, SliceSize) * 0.8;
        _parents.Push(new TrailRing(Current!, CenterX, CenterY, Layout, Selected, parentRadius));
        var (dx, dy) = RingGeometry.PointAt(angle, distance);
        Current = child;
        CenterX += dx;
        CenterY += dy;
        Layout = childLayout;
        Selected = -1;
        _pushArmed = true;
        return true;
    }
}
