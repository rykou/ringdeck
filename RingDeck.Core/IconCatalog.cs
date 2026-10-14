using System.Text.Json;

namespace RingDeck.Core;

/// <summary>One pickable icon. Id is what a slice stores: "emoji:1f600" or "icon:f230".</summary>
public sealed record IconEntry(string Id, string Name, string Group, IReadOnlyList<string> Keywords);

/// <summary>The bundled emoji and icon lists, and searching them.</summary>
public static class IconCatalog
{
    public static List<IconEntry> ParseEmoji(string json) => Parse(json, e =>
        new IconEntry("emoji:" + e.GetProperty("u").GetString(), e.GetProperty("n").GetString() ?? "",
            e.TryGetProperty("g", out var g) ? g.GetString() ?? "" : "",
            e.TryGetProperty("k", out var k) ? k.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>()));

    public static List<IconEntry> ParseIcons(string json) => Parse(json, e =>
        new IconEntry("icon:" + e.GetProperty("c").GetInt32().ToString("x"), e.GetProperty("n").GetString() ?? "", "Icons", Array.Empty<string>()));

    /// <summary>Brand logos (Simple Icons). Keywords hold the slug and aliases; the colour is kept per id.</summary>
    public static List<IconEntry> ParseBrands(string json) => Parse(json, e =>
    {
        var slug = e.GetProperty("s").GetString() ?? "";
        var keywords = new List<string> { slug };
        if (e.TryGetProperty("k", out var k)) keywords.AddRange(k.EnumerateArray().Select(x => (x.GetString() ?? "").ToLowerInvariant()));
        var hex = e.TryGetProperty("h", out var h) ? h.GetString() ?? "" : "";
        BrandColors["brand:" + slug] = "#" + hex;
        return new IconEntry("brand:" + slug, e.GetProperty("n").GetString() ?? "", "Brands", keywords);
    });

    static readonly Dictionary<string, string> BrandColors = new();

    /// <summary>A brand's own colour as "#RRGGBB", or null when the id isn't among the given brands.</summary>
    public static string? BrandColor(IEnumerable<IconEntry> brands, string id) =>
        brands.Any(b => b.Id == id) && BrandColors.TryGetValue(id, out var hex) ? hex : null;

    static List<IconEntry> Parse(string json, Func<JsonElement, IconEntry> read)
    {
        var list = new List<IconEntry>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                try { list.Add(read(e)); }
                catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException) { }   // skip a bad entry
            }
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        return list;
    }

    /// <summary>Groups in the order they first appear.</summary>
    public static List<string> Groups(IEnumerable<IconEntry> entries) => entries.Select(e => e.Group).Distinct().ToList();

    /// <summary>Entries whose name or keywords have words starting with every word of the query. Exact names come
    /// first, then names starting with the query, then other name matches, then keyword matches.</summary>
    public static List<IconEntry> Search(IEnumerable<IconEntry> entries, string query, string? group, int limit)
    {
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var inGroup = group is null ? entries : entries.Where(e => e.Group == group);
        if (words.Length == 0) return inGroup.Take(limit).ToList();
        var q = string.Join(' ', words);
        var ranked = new List<(int Rank, int Order, IconEntry Entry)>();
        var order = 0;
        foreach (var e in inGroup)
        {
            order++;
            var name = e.Name.ToLowerInvariant();
            var nameWords = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var keywordWords = e.Keywords.SelectMany(k => k.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
            bool Covers(string[] pool) => words.All(w => pool.Any(p => p.StartsWith(w, StringComparison.Ordinal)));
            int rank;
            if (name == q) rank = 0;
            else if (name.StartsWith(q, StringComparison.Ordinal)) rank = 1;
            else if (Covers(nameWords)) rank = 2;
            else if (Covers(nameWords.Concat(keywordWords).ToArray())) rank = 3;
            else continue;
            ranked.Add((rank, order, e));
        }
        return ranked.OrderBy(r => r.Rank).ThenBy(r => r.Order).Take(limit).Select(r => r.Entry).ToList();
    }

    /// <summary>"emoji:1f600" → ("emoji", "1f600"); anything without a known prefix → ("", "").</summary>
    public static (string Kind, string Value) Split(string icon)
    {
        var colon = icon.IndexOf(':');
        if (colon <= 0) return ("", "");
        var kind = icon[..colon];
        return kind is "emoji" or "icon" or "glyph" or "file" or "brand" ? (kind, icon[(colon + 1)..]) : ("", "");
    }

    /// <summary>The character to draw with the icon font for an "icon:" id.</summary>
    public static string IconText(string icon) =>
        Split(icon) is ("icon", var hex) && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code) ? char.ConvertFromUtf32(code) : "";
}
