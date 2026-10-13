namespace RingDeck.Core;

public enum ChooseKind { None, Closed, Run, EnteredNested, MissingRing }

public readonly record struct ChooseResult(ChooseKind Kind, Slice? Slice);

/// <summary>State of the ring that is currently open: which ring, where, what is selected, and how deep.</summary>
public sealed class RingSession
{
    public const double DeadZone = 28, SliceSize = 56, PushThrough = 1.7;
    public const int MaxDepth = 32;

    readonly Func<Guid, Ring?> _findRing;
    readonly Stack<(Ring Ring, double X, double Y)> _parents = new();

    public RingSession(Func<Guid, Ring?> findRing) => _findRing = findRing;

    public Ring? Current { get; private set; }
    public Guid RootId { get; private set; }
    public double CenterX { get; private set; }
    public double CenterY { get; private set; }
    public int Selected { get; private set; } = -1;
    /// <summary>True when the ring stays open until a click: click mode, or after a release entered a child ring.</summary>
    public bool Sticky { get; private set; }
    public bool IsOpen => Current is not null;
    public int Depth => _parents.Count;
    public double Radius => Current is null ? 0 : RingGeometry.EffectiveRadius(Current.Radius, Current.Slices.Count, SliceSize);

    public void Open(Ring ring, double x, double y)
    {
        _parents.Clear();
        Current = ring;
        RootId = ring.Id;
        CenterX = x;
        CenterY = y;
        Selected = -1;
        Sticky = ring.Mode == OpenMode.Click;
    }

    /// <summary>Moves a just-opened ring, for example after clamping it to the screen.</summary>
    public void Recenter(double x, double y) { CenterX = x; CenterY = y; }

    /// <summary>Updates the selection. Returns true when pushing outward on a nested slice entered its ring.</summary>
    public bool PointerMoved(double x, double y)
    {
        if (Current is null) return false;
        double dx = x - CenterX, dy = y - CenterY;
        Selected = RingGeometry.SliceAt(dx, dy, Current.Slices.Count, DeadZone);
        if (Selected < 0 || Sticky) return false;
        if (dx * dx + dy * dy < Math.Pow(Radius * PushThrough, 2)) return false;
        return TryEnter(Current.Slices[Selected], x, y);
    }

    /// <summary>A click (or a hold-mode release) at the pointer position.</summary>
    public ChooseResult Choose(double x, double y)
    {
        if (Current is null) return new ChooseResult(ChooseKind.None, null);
        Selected = RingGeometry.SliceAt(x - CenterX, y - CenterY, Current.Slices.Count, DeadZone);
        if (Selected < 0) { Close(); return new ChooseResult(ChooseKind.Closed, null); }
        var slice = Current.Slices[Selected];
        switch (slice.Action)
        {
            case null:
                Close();
                return new ChooseResult(ChooseKind.Closed, null);
            case NestedRingAction:
                if (TryEnter(slice, x, y)) { Sticky = true; return new ChooseResult(ChooseKind.EnteredNested, slice); }
                Close();
                return new ChooseResult(ChooseKind.MissingRing, slice);
            default:
                Close();
                return new ChooseResult(ChooseKind.Run, slice);
        }
    }

    /// <summary>The ring's shortcut key was released.</summary>
    public ChooseResult Release(double x, double y) =>
        Current is null || Sticky ? new ChooseResult(ChooseKind.None, null) : Choose(x, y);

    /// <summary>Goes up one level. Returns false when that closed the root ring (or nothing was open).</summary>
    public bool Back()
    {
        if (Current is null) return false;
        if (_parents.Count == 0) { Close(); return false; }
        (Current, CenterX, CenterY) = _parents.Pop();
        Selected = -1;
        return true;
    }

    public void Close()
    {
        _parents.Clear();
        Current = null;
        Selected = -1;
        Sticky = false;
    }

    bool TryEnter(Slice slice, double x, double y)
    {
        if (slice.Action is not NestedRingAction nested || _parents.Count >= MaxDepth) return false;
        if (_findRing(nested.RingId) is not { Slices.Count: > 0 } child) return false;
        _parents.Push((Current!, CenterX, CenterY));
        Current = child;
        CenterX = x;
        CenterY = y;
        Selected = -1;
        return true;
    }
}
