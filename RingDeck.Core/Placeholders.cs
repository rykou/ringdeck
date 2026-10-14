using System.Text;

namespace RingDeck.Core;

public static class Placeholders
{
    /// <summary>Expands {date} {time} {datetime} {clipboard}; "{{" is a literal brace. Unknown tokens are kept.
    /// <paramref name="extra"/> adds tokens of its own (like {selection}); <paramref name="escape"/> is applied to every
    /// value put in (JSON bodies).</summary>
    public static string Expand(string text, DateTime now, Func<string?> clipboard,
        IReadOnlyDictionary<string, Func<string?>>? extra = null, Func<string, string>? escape = null)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '{') { sb.Append(text[i++]); continue; }
            if (i + 1 < text.Length && text[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
            var end = text.IndexOf('}', i + 1);
            if (end < 0) { sb.Append(text, i, text.Length - i); break; }
            var token = text.Substring(i + 1, end - i - 1);
            var value = extra is not null && extra.TryGetValue(token.Trim().ToLowerInvariant(), out var get) ? get() ?? "" : Resolve(token, now, clipboard);
            // Not a placeholder: keep the brace and look again from the next character, so "{"a": "{date}"}" still
            // finds {date}.
            if (value is null) { sb.Append('{'); i++; continue; }
            sb.Append(escape is null ? value : escape(value));
            i = end + 1;
        }
        return sb.ToString();
    }

    /// <summary>True when the text uses the token, e.g. Uses(text, "selection") for "{selection}".</summary>
    public static bool Uses(string text, string token) => text.Replace("{{", "").Contains("{" + token + "}", StringComparison.OrdinalIgnoreCase);

    static string? Resolve(string token, DateTime now, Func<string?> clipboard)
    {
        var colon = token.IndexOf(':');
        var name = (colon < 0 ? token : token[..colon]).Trim().ToLowerInvariant();
        var format = colon < 0 ? null : token[(colon + 1)..];
        try
        {
            return name switch
            {
                "date" => now.ToString(format ?? "d"),
                "time" => now.ToString(format ?? "t"),
                "datetime" => now.ToString(format ?? "g"),
                "clipboard" => clipboard() ?? "",
                _ => null,
            };
        }
        catch (FormatException) { return null; }
    }
}
