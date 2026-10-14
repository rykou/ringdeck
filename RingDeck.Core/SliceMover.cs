namespace RingDeck.Core;

/// <summary>Moves or copies a slice to another ring (the editor's drag and drop onto the ring list).</summary>
public static class SliceMover
{
    /// <returns>null when done (or nothing needed doing), otherwise why it was refused; nothing changes then.</returns>
    public static string? Transfer(AppConfig config, Ring from, Slice slice, Ring to, bool copy)
    {
        if (!from.Slices.Contains(slice)) return "That slice is no longer in its ring.";
        if (!copy && ReferenceEquals(from, to)) return null;
        if (slice.Action is NestedRingAction nested && RingGraph.WouldCycle(config, to.Id, nested.RingId))
        {
            var target = config.Find(nested.RingId)?.Name ?? "another ring";
            return $"\"{slice.Label}\" opens \"{target}\", so putting it in \"{to.Name}\" would make that ring open itself.";
        }
        if (copy)
        {
            var duplicate = ConfigTransfer.DuplicateSlice(slice);
            duplicate.Label = slice.Label;                 // in another ring the copy keeps its name
            to.Slices.Add(duplicate);
        }
        else
        {
            from.Slices.Remove(slice);
            to.Slices.Add(slice);
        }
        return null;
    }
}
