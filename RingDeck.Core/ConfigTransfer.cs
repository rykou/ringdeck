namespace RingDeck.Core;

public static class ConfigTransfer
{
    static AppConfig Clone(AppConfig config) => ConfigStore.FromJson(ConfigStore.ToJson(config))!;

    /// <summary>A copy holding one ring and every ring it can reach through nested slices.</summary>
    public static AppConfig Export(AppConfig config, Guid ringId)
    {
        var copy = Clone(config);
        copy.Settings = new GlobalSettings();
        copy.Rings.RemoveAll(r => r.Id != ringId && !RingGraph.Reaches(config, ringId, r.Id));
        copy.Rings = copy.Rings.OrderBy(r => r.Id == ringId ? 0 : 1).ToList();
        return copy;
    }

    /// <summary>Adds every incoming ring under fresh ids. Shortcuts already in use are cleared; image icons are dropped
    /// because the image files are not part of the export.</summary>
    public static List<Ring> Import(AppConfig target, AppConfig incoming)
    {
        var copy = Clone(incoming);
        var ids = copy.Rings.ToDictionary(r => r.Id, _ => Guid.NewGuid());
        var taken = target.Rings.Select(r => r.Shortcut).Where(s => s != "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ring in copy.Rings)
        {
            ring.Id = ids[ring.Id];
            if (ring.Shortcut != "" && !taken.Add(ring.Shortcut)) ring.Shortcut = "";
            foreach (var slice in ring.Slices)
            {
                slice.Id = Guid.NewGuid();
                if (slice.Icon.StartsWith("file:", StringComparison.Ordinal)) slice.Icon = "";
                if (slice.Action is NestedRingAction nested && ids.TryGetValue(nested.RingId, out var mapped)) nested.RingId = mapped;
            }
        }
        target.Rings.AddRange(copy.Rings);
        return copy.Rings;
    }

    public static Ring Duplicate(Ring ring)
    {
        var copy = Clone(new AppConfig { Rings = { ring } }).Rings[0];
        copy.Id = Guid.NewGuid();
        copy.Name = ring.Name + " copy";
        copy.Shortcut = "";
        foreach (var slice in copy.Slices) slice.Id = Guid.NewGuid();
        return copy;
    }
}
