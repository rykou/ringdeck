namespace RingDeck.Core;

/// <summary>What a row of the macro editor shows: one step, or (when down and up are not shown separately) a key or
/// button press made of a down step, an optional wait (how long it is held) and the matching up step.</summary>
public enum MacroRowKind { Step, KeyPress, ButtonPress }

/// <param name="Start">Index of the row's first step.</param>
/// <param name="Count">How many steps the row covers: 1, or 2-3 for a press (down, optional wait, up).</param>
public sealed record MacroRow(int Start, int Count, MacroRowKind Kind)
{
    /// <summary>The wait between down and up, for a press that has one.</summary>
    public DelayStep? Hold(IList<MacroStep> steps) => Count == 3 ? steps[Start + 1] as DelayStep : null;
}

/// <summary>Groups macro steps into editor rows and edits them row by row. Grouping is display only: the steps
/// themselves, and what the macro does, never change when the view switches between joined and separate.</summary>
public static class MacroView
{
    public static List<MacroRow> Rows(IList<MacroStep> steps, bool split)
    {
        var rows = new List<MacroRow>();
        for (var i = 0; i < steps.Count;)
        {
            var press = split ? null : PressAt(steps, i);
            if (press is { } p) { rows.Add(p); i += p.Count; }
            else { rows.Add(new MacroRow(i, 1, MacroRowKind.Step)); i++; }
        }
        return rows;
    }

    static MacroRow? PressAt(IList<MacroStep> steps, int i)
    {
        MacroStep? At(int k) => k < steps.Count ? steps[k] : null;
        switch (steps[i])
        {
            case KeyDownStep down:
                bool KeyUp(MacroStep? s) => s is KeyUpStep up && SameKey(up.Key, down.Key);
                if (KeyUp(At(i + 1))) return new MacroRow(i, 2, MacroRowKind.KeyPress);
                if (At(i + 1) is DelayStep && KeyUp(At(i + 2))) return new MacroRow(i, 3, MacroRowKind.KeyPress);
                return null;
            case MouseDownStep mouse:
                bool ButtonUp(MacroStep? s) => s is MouseUpStep up && up.Button == mouse.Button;
                if (ButtonUp(At(i + 1))) return new MacroRow(i, 2, MacroRowKind.ButtonPress);
                if (At(i + 1) is DelayStep && ButtonUp(At(i + 2))) return new MacroRow(i, 3, MacroRowKind.ButtonPress);
                return null;
            default:
                return null;
        }
    }

    static bool SameKey(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Moves a whole row one row up (delta -1) or down (+1), past the neighbouring row.</summary>
    public static void Move(IList<MacroStep> steps, IReadOnlyList<MacroRow> rows, int row, int delta)
    {
        var other = row + delta;
        if (row < 0 || row >= rows.Count || other < 0 || other >= rows.Count) return;
        var (first, second) = delta < 0 ? (rows[other], rows[row]) : (rows[row], rows[other]);
        // Swap two adjacent blocks: take the second out and put it back in front of the first.
        var moved = Enumerable.Range(second.Start, second.Count).Select(k => steps[k]).ToList();
        for (var k = 0; k < second.Count; k++) steps.RemoveAt(second.Start);
        for (var k = 0; k < moved.Count; k++) steps.Insert(first.Start + k, moved[k]);
    }

    /// <summary>Moves row <paramref name="from"/> (all its steps) so it comes just before row <paramref name="before"/>;
    /// <paramref name="before"/> equal to the number of rows puts it at the end. Used by drag and drop.</summary>
    public static void MoveTo(List<MacroStep> steps, IReadOnlyList<MacroRow> rows, int from, int before)
    {
        if (from < 0 || from >= rows.Count || before < 0 || before > rows.Count || before == from || before == from + 1) return;
        var row = rows[from];
        var block = steps.GetRange(row.Start, row.Count);
        var at = before == rows.Count ? steps.Count : rows[before].Start;
        steps.RemoveRange(row.Start, row.Count);
        if (at > row.Start) at -= row.Count;
        steps.InsertRange(at, block);
    }

    public static void Remove(IList<MacroStep> steps, MacroRow row)
    {
        for (var k = 0; k < row.Count; k++) steps.RemoveAt(row.Start);
    }

    /// <summary>Removes every wait step (including how long recorded presses were held). Returns how many.</summary>
    public static int RemoveWaits(List<MacroStep> steps) => steps.RemoveAll(s => s is DelayStep);

    /// <summary>Gives every wait step the same length. Returns how many waits there are.</summary>
    public static int SetWaits(IEnumerable<MacroStep> steps, int ms)
    {
        var count = 0;
        foreach (var wait in steps.OfType<DelayStep>()) { wait.Ms = Math.Clamp(ms, 0, ActionRunner.MaxDelayMs); count++; }
        return count;
    }

    /// <summary>Rough time the macro takes: its waits and mouse moves (keys and clicks themselves are near instant).</summary>
    public static int TotalMs(IEnumerable<MacroStep> steps)
    {
        long total = 0;
        foreach (var step in steps)
            total += step switch
            {
                DelayStep wait => Math.Clamp(wait.Ms, 0, ActionRunner.MaxDelayMs),
                MouseMoveStep move => MousePath.DurationMs(MousePath.Parse(move.Path), move.Speed),
                _ => 0,
            };
        return (int)Math.Min(total, int.MaxValue);
    }
}
