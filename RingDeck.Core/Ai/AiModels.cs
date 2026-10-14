using System.Text.Json;

namespace RingDeck.Core;

/// <summary>Asks a service which models a key can use. Every built-in format has a list endpoint next to its chat
/// endpoint; the custom format has none.</summary>
public static class AiModels
{
    /// <summary>The GET request for the first page of models, with the same authentication as chat; null for Custom.</summary>
    public static HttpCall? ListCall(AiConnection c, Func<string, string> reveal)
    {
        if (c.Format == AiFormat.Custom) return null;
        var headers = AiRequests.Build(c, null, Array.Empty<ChatMessage>(), reveal).Headers;
        var endpoint = c.Endpoint.Trim();
        var path = endpoint.Split('?')[0].TrimEnd('/');
        var url = c.Format switch
        {
            AiFormat.Anthropic => (path.EndsWith("/messages", StringComparison.OrdinalIgnoreCase) ? path[..^"/messages".Length] : path) + "/models?limit=1000",
            AiFormat.Gemini => (path.IndexOf("/models", StringComparison.OrdinalIgnoreCase) is var i and >= 0 ? path[..i] : "https://generativelanguage.googleapis.com/v1beta") + "/models?pageSize=1000",
            _ => (path.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase) is var j and >= 0 ? path[..j] : path) + "/models",
        };
        return new HttpCall(url, headers, "", false);
    }

    /// <summary>Model ids on one page, and what to ask for the next page (null when this was the last).</summary>
    public static (List<string> Ids, string? Next) Parse(AiFormat format, string json)
    {
        var ids = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            switch (format)
            {
                case AiFormat.Gemini:
                    if (root.TryGetProperty("models", out var models))
                        foreach (var m in models.EnumerateArray())
                        {
                            var chat = !m.TryGetProperty("supportedGenerationMethods", out var methods)
                                || methods.EnumerateArray().Any(x => x.GetString() == "generateContent");
                            if (chat && m.TryGetProperty("name", out var name) && name.GetString() is { } n)
                                ids.Add(n.StartsWith("models/", StringComparison.Ordinal) ? n[7..] : n);
                        }
                    return (ids, root.TryGetProperty("nextPageToken", out var token) && token.GetString() is { Length: > 0 } t ? t : null);
                case AiFormat.Anthropic:
                    Ids(root, ids);
                    var more = root.TryGetProperty("has_more", out var h) && h.ValueKind == JsonValueKind.True;
                    return (ids, more && root.TryGetProperty("last_id", out var last) ? last.GetString() : null);
                default:
                    Ids(root, ids);
                    return (ids.Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList(), null);
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return (new List<string>(), null); }
    }

    static void Ids(JsonElement root, List<string> ids)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return;
        foreach (var m in data.EnumerateArray())
            if (m.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } s) ids.Add(s);
    }

    /// <summary>The URL of the page after the one that returned <paramref name="next"/>.</summary>
    public static string NextPage(AiFormat format, string firstPage, string next) =>
        firstPage + (format == AiFormat.Gemini ? "&pageToken=" : "&after_id=") + Uri.EscapeDataString(next);
}
