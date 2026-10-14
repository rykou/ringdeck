namespace RingDeck.Core;

/// <summary>Rules for rings that share a shortcut: a ring made for the program in front wins over an "everywhere" ring.</summary>
public static class RingPicker
{
    /// <summary>The ring to open for the program in front, or null when none applies (the shortcut then goes to the app).</summary>
    public static Ring? Pick(IEnumerable<Ring> candidates, string? exe)
    {
        var list = candidates.ToList();
        return list.FirstOrDefault(r => r.OnlyIn.Count > 0 && ProgramNames.AnyMatch(r.OnlyIn, exe))
            ?? list.FirstOrDefault(r => r.OnlyIn.Count == 0);
    }

    /// <summary>Another enabled ring that competes with this one: same shortcut and an overlapping place
    /// (both everywhere, or a program in common). Null when there is none.</summary>
    public static Ring? Clash(AppConfig config, Ring ring)
    {
        if (!Shortcut.TryParse(ring.Shortcut, out var mine)) return null;
        return config.Rings.FirstOrDefault(other =>
            !ReferenceEquals(other, ring) && other.Enabled
            && Shortcut.TryParse(other.Shortcut, out var theirs) && theirs == mine
            && (ring.OnlyIn.Count == 0 ? other.OnlyIn.Count == 0 : ring.OnlyIn.Any(p => ProgramNames.AnyMatch(other.OnlyIn, p))));
    }
}
