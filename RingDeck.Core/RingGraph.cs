namespace RingDeck.Core;

public static class RingGraph
{
    /// <summary>True when ring <paramref name="to"/> can be reached from <paramref name="from"/> through nested-ring slices.</summary>
    public static bool Reaches(AppConfig config, Guid from, Guid to)
    {
        var seen = new HashSet<Guid>();
        var todo = new Stack<Guid>();
        todo.Push(from);
        while (todo.Count > 0)
        {
            var id = todo.Pop();
            if (!seen.Add(id) || config.Find(id) is not { } ring) continue;
            foreach (var slice in ring.Slices)
            {
                if (slice.Action is not NestedRingAction nested) continue;
                if (nested.RingId == to) return true;
                todo.Push(nested.RingId);
            }
        }
        return false;
    }

    /// <summary>True when adding a slice in <paramref name="parent"/> that opens <paramref name="child"/> would make a loop.</summary>
    public static bool WouldCycle(AppConfig config, Guid parent, Guid child) =>
        parent == child || Reaches(config, child, parent);
}
