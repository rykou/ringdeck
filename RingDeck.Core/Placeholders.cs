using System.Text;

namespace RingDeck.Core;

public static class Placeholders
{
    /// <summary>Expands {date} {time} {datetime} {clipboard}; "{{" is a literal brace. Unknown tokens are kept.</summary>
    public static string Expand(string text, DateTime now, Func<string?> clipboard)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '{') { sb.Append(text[i++]); continue; }
            if (i + 1 < text.Length && text[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
            var end = text.IndexOf('}', i + 1);
            if (end < 0) { sb.Append(text, i, text.Length - i); break; }
            var value = Resolve(text.Substring(i + 1, end - i - 1), now, clipboard);
            if (value is null) sb.Append(text, i, end - i + 1); else sb.Append(value);
            i = end + 1;
        }
        return sb.ToString();
    }

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
